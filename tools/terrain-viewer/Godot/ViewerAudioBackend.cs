using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.Session.Settings;
using Arcanum.TerrainViewer.Core;
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
    /// linearly to amplitude). A looped WAV repeats its whole sample range, a looped MP3 the whole file. <paramref name="pan"/>
    /// (−1 left … 1 right) routes the voice through one of nine panning buses of the channel (steps of 0.25, a Steamwoven
    /// choice; the original's mixer pans continuously).</summary>
    public ViewerAudioResult Play(SoundPayload payload, AudioChannel channel, int volume, bool loop = false, double pan = 0)
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
        voice.Player = new AudioStreamPlayer { Stream = stream, Bus = PanBus(channel, pan), ProcessMode = Node.ProcessModeEnum.Always };
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
                // A one-shot must end even when the file carries a smpl loop (the engine may apply it on load).
                if (loop)
                {
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                    wav.LoopBegin = 0; wav.LoopEnd = payload.Wav!.FrameCount;
                }
                else wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
                return wav;
            }
            var mp3 = AudioStreamMP3.LoadFromBuffer(bytes);
            if (mp3 is null || !(mp3.GetLength() > 0)) return null;
            mp3.Loop = loop;
            return mp3;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return null; }
    }

    public const int PanSteps = 4;

    /// <summary>The bus for a pan: the channel bus at the centre, else a child bus with a panner at the nearest quarter.</summary>
    private string PanBus(AudioChannel channel, double pan)
    {
        var step = (int)Math.Round(Math.Clamp(double.IsFinite(pan) ? pan : 0, -1, 1) * PanSteps);
        if (step == 0) return BusName(channel);
        var name = $"{BusName(channel)} pan {step:+0;-0}";
        if (AudioServer.GetBusIndex(name) >= 0) return name;
        var index = AudioServer.BusCount;
        AudioServer.AddBus(index); AudioServer.SetBusName(index, name); AudioServer.SetBusSend(index, BusName(channel));
        AudioServer.AddBusEffect(index, new AudioEffectPanner { Pan = (float)step / PanSteps });
        _createdBuses.Add(name);
        return name;
    }

    /// <summary>Fades the handle's sound to silence over <paramref name="milliseconds"/> and releases it; a stale handle is a
    /// no-op (false). A one-shot keeps playing until it ends unless faded.</summary>
    public bool FadeOut(ViewerAudioHandle handle, int milliseconds)
    {
        if (Current(handle) is not { } voice) return false;
        if (milliseconds <= 0) { Release(voice); return true; }
        var player = voice.Player!;
        voice.Loop = false;
        var tween = player.CreateTween();
        tween.TweenProperty(player, "volume_db", -80.0, milliseconds / 1000.0);
        tween.TweenCallback(Callable.From(() => { if (Current(handle) is { } current) Release(current); }));
        return true;
    }

    /// <summary>Sets a live sound's volume (0–127) and pan (−1 … 1) when the listener moves (ARC-UI-042); a stale handle is a
    /// no-op (false).</summary>
    public bool Reposition(ViewerAudioHandle handle, int volume, double pan)
    {
        if (Current(handle) is not { } voice) return false;
        SetVolume(voice.Player!, volume);
        var bus = PanBus(voice.Channel, pan);
        if (voice.Player!.Bus != bus) voice.Player.Bus = bus;
        return true;
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

    /// <summary>Stops every sound but the kept handles' (ARC-UI-139: a map opening under the main menu leaves the menu's song playing).</summary>
    public void StopAllExcept(IEnumerable<ViewerAudioHandle> keep)
    {
        var kept = keep.ToHashSet();
        for (var slot = 0; slot < _voices.Length; slot++)
            if (_voices[slot].Player is not null && !kept.Contains(new(slot, _voices[slot].Generation))) Release(_voices[slot]);
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

/// <summary>ARC-UI-040: the channel volume preference, the three volume lines of the one settings file (ARC-UI-1130,
/// <see cref="ViewerSettingsConfig"/>, once <c>user://audio_volumes_v1.json</c>); a missing or invalid line loads the default 5.
/// <c>AUDIO_VOLUMES_TEST_PATH</c> names a settings file for tests; an automated run without it has no file at all, so it never reads or
/// changes the owner's setting.</summary>
public sealed class AudioVolumesStore
{
    public const string TestVariable = "AUDIO_VOLUMES_TEST_PATH";
    private readonly string? _path;

    public AudioVolumesStore(bool automated) => _path = ViewerSettingsFile.Choose(TestVariable, automated);

    public bool Persistent => _path is not null;
    public bool HasSaved => _path is not null && ViewerSettingsConfig.HasAudio(ArcanumConfig.Read(_path));

    public AudioVolumes Load() => _path is null ? AudioVolumes.Defaults : ViewerSettingsConfig.ReadAudio(ArcanumConfig.Read(_path));

    public bool Save(AudioVolumes volumes) => _path is not null && ArcanumConfig.Save(_path, ViewerSettingsConfig.AudioChanges(volumes)) == ArcanumConfigSaveStatus.Saved;
}
