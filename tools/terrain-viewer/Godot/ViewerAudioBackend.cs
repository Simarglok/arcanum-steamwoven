using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Godot;

namespace Arcanum.TerrainViewer;

public enum ViewerAudioStatus
{
    /// <summary>Decoded and started on an output device.</summary>
    Playing,
    /// <summary>Decoded and started, but the engine has no output device (the Dummy driver of a headless run): nothing is
    /// heard, which the status says instead of claiming playback.</summary>
    Silent,
    /// <summary>The payload was not admitted (<see cref="SoundPayloadStatus"/>), so it was not given to the engine.</summary>
    NotAdmitted,
    /// <summary>The engine could not decode the admitted bytes.</summary>
    DecodeFailed,
    /// <summary>All voices are busy (<see cref="ViewerAudioBackend.MaxVoices"/>); nothing is stolen.</summary>
    VoiceLimit,
    Disposed,
}

/// <summary>A started sound: a voice slot and its generation, so a stale handle never stops a later sound in the slot.</summary>
public readonly record struct ViewerAudioHandle(int Slot, long Generation);

public readonly record struct ViewerAudioResult(ViewerAudioStatus Status, ViewerAudioHandle? Handle)
{
    public bool Started => Status is ViewerAudioStatus.Playing or ViewerAudioStatus.Silent;
}

/// <summary>
/// ARC-UI-040: the viewer's sound output (docs/audio/viewer-audio.md). It owns three engine buses under Master, named after
/// the original's channels, and up to <see cref="MaxVoices"/> stream players. A sound is an admitted payload
/// (<see cref="SoundCatalog.OpenPayload"/>) decoded by the engine (WAV PCM, MP3) at a 0–127 volume on a channel; the volume
/// arithmetic is <see cref="AudioVolumeRules"/>. Engine thread only. It never touches game state: a failure is a status.
/// </summary>
public sealed class ViewerAudioBackend : IDisposable
{
    public const int MaxVoices = 32;
    private sealed class Voice
    {
        public long Generation;
        public AudioStreamPlayer? Player;
        public AudioChannel Channel;
        public bool Loop;
    }

    private readonly Node _parent;
    private readonly Voice[] _voices = Enumerable.Range(0, MaxVoices).Select(_ => new Voice()).ToArray();
    private readonly List<string> _createdBuses = [];
    private bool _disposed;

    private ViewerAudioBackend(Node parent)
    {
        _parent = parent;
        Driver = AudioServer.GetDriverName();
        foreach (var channel in Enum.GetValues<AudioChannel>())
        {
            var name = BusName(channel);
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            var index = AudioServer.BusCount;
            AudioServer.AddBus(index); AudioServer.SetBusName(index, name); AudioServer.SetBusSend(index, "Master");
            _createdBuses.Add(name);
        }
    }

    /// <summary>The engine's audio driver; "Dummy" means no output device (<see cref="Audible"/> false).</summary>
    public string Driver { get; }
    public bool Audible => !string.Equals(Driver, "Dummy", StringComparison.Ordinal);
    public int ActiveVoices => _voices.Count(v => v.Player is not null);

    public static ViewerAudioBackend Create(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (!parent.IsInsideTree()) throw new InvalidOperationException("The audio parent must be in the scene tree.");
        return new(parent);
    }

    public static string BusName(AudioChannel channel) => channel switch
    {
        AudioChannel.Effects => "Effects",
        AudioChannel.Voice => "Voice",
        AudioChannel.Music => "Music",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    /// <summary>Starts <paramref name="payload"/> on <paramref name="channel"/> at <paramref name="volume"/> (0–127, mapped
    /// linearly to amplitude). A looped WAV repeats its whole sample range, a looped MP3 the whole file.</summary>
    public ViewerAudioResult Play(SoundPayload payload, AudioChannel channel, int volume, bool loop = false)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (_disposed) return new(ViewerAudioStatus.Disposed, null);
        if (payload.Status != SoundPayloadStatus.Admitted) return new(ViewerAudioStatus.NotAdmitted, null);
        var slot = Array.FindIndex(_voices, v => v.Player is null);
        if (slot < 0) return new(ViewerAudioStatus.VoiceLimit, null);
        var stream = Decode(payload, loop);
        if (stream is null) return new(ViewerAudioStatus.DecodeFailed, null);
        var voice = _voices[slot];
        voice.Generation++; voice.Channel = channel; voice.Loop = loop;
        voice.Player = new AudioStreamPlayer { Stream = stream, Bus = BusName(channel), ProcessMode = Node.ProcessModeEnum.Always };
        SetVolume(voice.Player, volume);
        _parent.AddChild(voice.Player);
        voice.Player.Play();
        return new(Audible ? ViewerAudioStatus.Playing : ViewerAudioStatus.Silent, new(slot, voice.Generation));
    }

    private static AudioStream? Decode(SoundPayload payload, bool loop)
    {
        try
        {
            var bytes = payload.Bytes.ToArray();
            if (payload.Kind == SoundPayloadKind.Wav)
            {
                var wav = AudioStreamWav.LoadFromBuffer(bytes);
                if (wav is null || !(wav.GetLength() > 0)) return null;
                if (loop)
                {
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                    wav.LoopBegin = 0; wav.LoopEnd = payload.Wav!.FrameCount;
                }
                return wav;
            }
            var mp3 = AudioStreamMP3.LoadFromBuffer(bytes);
            if (mp3 is null || !(mp3.GetLength() > 0)) return null;
            mp3.Loop = loop;
            return mp3;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return null; }
    }

    /// <summary>True while the handle's sound is still the one in its slot and the engine is playing it.</summary>
    public bool IsPlaying(ViewerAudioHandle handle) => Current(handle) is { } voice && voice.Player!.Playing;

    /// <summary>The handle's current volume on the 0–127 scale (for checks), or null for a stale handle.</summary>
    public double? Volume(ViewerAudioHandle handle) => Current(handle) is { } voice ? Mathf.DbToLinear(voice.Player!.VolumeDb) * AudioVolumeRules.MaxVolume : null;

    /// <summary>Stops the handle's sound; a stale or finished handle is a no-op (false).</summary>
    public bool Stop(ViewerAudioHandle handle)
    {
        if (Current(handle) is not { } voice) return false;
        Release(voice);
        return true;
    }

    public void StopAll()
    {
        foreach (var voice in _voices) if (voice.Player is not null) Release(voice);
    }

    /// <summary>The original sets every live sound of a channel to the bare channel volume when its setting changes.</summary>
    public void FlattenChannel(AudioChannel channel, int channelVolume)
    {
        foreach (var voice in _voices)
            if (voice.Player is not null && voice.Channel == channel) SetVolume(voice.Player, channelVolume);
    }

    /// <summary>Frees the players of finished one-shot sounds; call once per frame.</summary>
    public void Tick()
    {
        foreach (var voice in _voices)
            if (voice.Player is { } player && !voice.Loop && !player.Playing) Release(voice);
    }

    private Voice? Current(ViewerAudioHandle handle) =>
        !_disposed && handle.Slot is >= 0 and < MaxVoices && _voices[handle.Slot] is { Player: not null } voice && voice.Generation == handle.Generation ? voice : null;

    private static void SetVolume(AudioStreamPlayer player, int volume)
    {
        var amplitude = AudioVolumeRules.Amplitude(volume);
        player.VolumeDb = amplitude <= 0 ? -80 : Mathf.LinearToDb((float)amplitude);
    }

    private static void Release(Voice voice)
    {
        var player = voice.Player!;
        voice.Player = null;
        player.Stop();
        player.GetParent()?.RemoveChild(player);
        player.QueueFree();
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        foreach (var name in _createdBuses.AsEnumerable().Reverse())
            if (AudioServer.GetBusIndex(name) is var index and >= 0) AudioServer.RemoveBus(index);
        _disposed = true;
    }
}

/// <summary>ARC-UI-040: the channel volume preference, following <see cref="VisualProfileSettingsStore"/>:
/// <c>user://audio_volumes_v1.json</c> holding <see cref="AudioVolumesCodec"/> text; a missing, unreadable or invalid file
/// loads the defaults (5/5/5). <c>AUDIO_VOLUMES_TEST_PATH</c> isolates tests; an automated run without it has no file at
/// all, so it never reads or changes the owner's setting.</summary>
public sealed class AudioVolumesStore
{
    public const string TestVariable = "AUDIO_VOLUMES_TEST_PATH";
    public const string UserPath = "user://audio_volumes_v1.json";
    private readonly string? _path;

    public AudioVolumesStore(bool automated)
    {
        var test = OS.GetEnvironment(TestVariable);
        _path = !string.IsNullOrWhiteSpace(test) ? test : automated ? null : UserPath;
    }

    public bool Persistent => _path is not null;
    public bool HasSaved => _path is not null && global::Godot.FileAccess.FileExists(_path);

    public AudioVolumes Load()
    {
        if (_path is null || !global::Godot.FileAccess.FileExists(_path)) return AudioVolumes.Defaults;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Read);
        return file is null ? AudioVolumes.Defaults : AudioVolumesCodec.Deserialize(file.GetAsText()) ?? AudioVolumes.Defaults;
    }

    public bool Save(AudioVolumes volumes)
    {
        if (_path is null) return false;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) return false;
        file.StoreString(AudioVolumesCodec.Serialize(volumes));
        file.Flush();
        return file.GetError() == Error.Ok;
    }
}
