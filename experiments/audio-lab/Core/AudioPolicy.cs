// Independently authored synthetic audio lab. MIT; no CE-derived implementation.
namespace AudioLab.Core;

public enum AudioBus { Master, Music, Voice, SFX, Ambient }
public enum MediaKind { Wav, Mp3 }
public enum AudioError
{
    None, InvalidCommand, Cancelled, Disposed, WrongThread, ForeignHandle, StaleHandle,
    AlreadyStopped, PlayerLimit, BufferLimit, TotalByteLimit, InvalidMedia, DecodeFailed,
    BackendUnavailable, PreferencesMissing, PreferencesInvalid, PreferencesIo
}
public readonly record struct AudioResult(AudioError Error, AudioHandle Handle = default)
{
    public bool Success => Error == AudioError.None;
}
public readonly record struct AudioHandle(Guid Owner, int Slot, long Generation);
public readonly record struct BusLevel(float Volume, bool Muted);
public sealed record AudioLimits(int Players, int PerBufferBytes, long RetainedBytes)
{
    public static AudioLimits Lab { get; } = new(8, 1_048_576, 8_388_608);
    public bool Valid => Players is > 0 and <= 64 && PerBufferBytes > 0 && RetainedBytes >= PerBufferBytes;
}

// No externally mutable array/memory reaches a command or decoder.
public sealed class EncodedAudio
{
    private readonly byte[] bytes;
    public MediaKind Kind { get; }
    public int Length => bytes.Length;
    private EncodedAudio(byte[] bytes, MediaKind kind) { this.bytes = bytes; Kind = kind; }
    public static AudioResult Create(ReadOnlySpan<byte> bytes, MediaKind kind, int limit, out EncodedAudio? audio)
    {
        audio = null;
        if (!Enum.IsDefined(kind) || bytes.IsEmpty) return new(AudioError.InvalidMedia);
        if (limit <= 0 || bytes.Length > limit) return new(AudioError.BufferLimit);
        audio = new(bytes.ToArray(), kind);
        return new(AudioError.None);
    }
    public byte[] CopyForDecoder() => (byte[])bytes.Clone();
}
public sealed record PlayCommand(EncodedAudio Audio, AudioBus Bus, bool Loop = false);
public readonly record struct PlaybackState(bool Paused, float Gain);

// Engine independent. Serialized owner-thread policy, not a thread-safe command queue.
public sealed class AudioPolicy : IDisposable
{
    private sealed class Slot
    {
        public long Generation;
        public long Bytes;
        public bool Active;
        public bool Paused;
        public float Gain = 1;
        public float FadeFrom;
        public float FadeTarget;
        public double FadeElapsed;
        public double FadeDuration;
    }
    private readonly Guid owner = Guid.NewGuid();
    private readonly Slot[] slots;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly BusLevel[] levels = [new(.5f, false), new(1, false), new(1, false), new(1, false), new(1, false)];
    private bool disposed;
    public AudioLimits Limits { get; }
    public int ActivePlayers => slots.Count(s => s.Active);
    public long RetainedBytes => slots.Sum(s => s.Bytes);
    public int CacheBytes => 0;
    public AudioPolicy(AudioLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (!limits.Valid) throw new ArgumentOutOfRangeException(nameof(limits));
        Limits = limits; slots = Enumerable.Range(0, limits.Players).Select(_ => new Slot()).ToArray();
    }
    public AudioError Access() => Environment.CurrentManagedThreadId != thread ? AudioError.WrongThread : disposed ? AudioError.Disposed : AudioError.None;
    public AudioResult Reserve(PlayCommand command, bool cancelled = false)
    {
        var access = Access(); if (access != AudioError.None) return new(access);
        if (cancelled) return new(AudioError.Cancelled);
        if (command is null || command.Audio is null || command.Bus == AudioBus.Master || !Enum.IsDefined(command.Bus)) return new(AudioError.InvalidCommand);
        var bytes = command.Audio.Length;
        if (bytes > Limits.PerBufferBytes) return new(AudioError.BufferLimit);
        int index = Array.FindIndex(slots, s => !s.Active && s.Generation < long.MaxValue);
        if (index < 0) return new(AudioError.PlayerLimit);
        if (bytes > Limits.RetainedBytes - RetainedBytes) return new(AudioError.TotalByteLimit);
        var slot = slots[index]; slot.Generation++; slot.Bytes = bytes; slot.Active = true;
        slot.Paused = false; slot.Gain = 1; slot.FadeDuration = 0;
        return new(AudioError.None, new(owner, index, slot.Generation));
    }
    public AudioResult Validate(AudioHandle handle)
    {
        var access = Access(); if (access != AudioError.None) return new(access);
        if (handle.Owner != owner) return new(AudioError.ForeignHandle);
        if (handle.Slot < 0 || handle.Slot >= slots.Length || handle.Generation != slots[handle.Slot].Generation) return new(AudioError.StaleHandle);
        return new(slots[handle.Slot].Active ? AudioError.None : AudioError.AlreadyStopped, handle);
    }
    public AudioResult RetainDecoded(AudioHandle handle, int bytes)
    {
        var valid = Validate(handle); if (!valid.Success) return valid;
        if (bytes < 0 || bytes > Limits.PerBufferBytes) return new(AudioError.BufferLimit);
        if (bytes > Limits.RetainedBytes - RetainedBytes) return new(AudioError.TotalByteLimit);
        slots[handle.Slot].Bytes += bytes; return valid;
    }
    public AudioResult Stop(AudioHandle handle)
    {
        var valid = Validate(handle); if (!valid.Success) return valid;
        slots[handle.Slot].Active = false; slots[handle.Slot].Bytes = 0; return valid;
    }
    public AudioResult Pause(AudioHandle handle, bool paused)
    {
        var valid = Validate(handle); if (valid.Success) slots[handle.Slot].Paused = paused;
        return valid;
    }
    public AudioResult Fade(AudioHandle handle, float target, double seconds)
    {
        var valid = Validate(handle); if (!valid.Success) return valid;
        if (!float.IsFinite(target) || target is < 0 or > 1 || !double.IsFinite(seconds) || seconds < 0 || seconds > 60) return new(AudioError.InvalidCommand);
        var s = slots[handle.Slot]; s.FadeFrom = s.Gain; s.FadeTarget = target; s.FadeElapsed = 0; s.FadeDuration = seconds;
        if (seconds == 0) s.Gain = target;
        return valid;
    }
    public AudioResult Tick(double realSeconds)
    {
        var access = Access(); if (access != AudioError.None) return new(access);
        if (!double.IsFinite(realSeconds) || realSeconds < 0) return new(AudioError.InvalidCommand);
        foreach (var s in slots.Where(s => s.Active && s.FadeDuration > 0))
        {
            s.FadeElapsed = Math.Min(s.FadeDuration, s.FadeElapsed + realSeconds);
            s.Gain = s.FadeFrom + (s.FadeTarget - s.FadeFrom) * (float)(s.FadeElapsed / s.FadeDuration);
            if (s.FadeElapsed == s.FadeDuration) s.FadeDuration = 0;
        }
        return new(AudioError.None);
    }
    public AudioResult State(AudioHandle handle, out PlaybackState state)
    {
        state = default; var valid = Validate(handle);
        if (valid.Success) state = new(slots[handle.Slot].Paused, slots[handle.Slot].Gain);
        return valid;
    }
    public AudioResult SetLevel(AudioBus bus, BusLevel level)
    {
        var access = Access(); if (access != AudioError.None) return new(access);
        if (!Enum.IsDefined(bus) || !float.IsFinite(level.Volume) || level.Volume is < 0 or > 1) return new(AudioError.InvalidCommand);
        levels[(int)bus] = level; return new(AudioError.None);
    }
    public BusLevel Level(AudioBus bus) => Enum.IsDefined(bus) ? levels[(int)bus] : throw new ArgumentOutOfRangeException(nameof(bus));
    public MixerPreferences Preferences => new(1, levels[0], levels[1], levels[2], levels[3], levels[4]);
    public AudioResult ApplyPreferences(MixerPreferences preferences)
    {
        var access = Access(); if (access != AudioError.None) return new(access);
        if (preferences is null || !preferences.Valid) return new(AudioError.PreferencesInvalid);
        foreach (var bus in Enum.GetValues<AudioBus>()) levels[(int)bus] = preferences.Level(bus);
        return new(AudioError.None);
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Dispose on the owner thread.");
        foreach (var s in slots) { s.Active = false; s.Bytes = 0; }
        disposed = true;
    }
}
