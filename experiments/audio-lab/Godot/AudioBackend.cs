using AudioLab.Core;
using Godot;

namespace AudioLab.Godot;

// Exclusively owns the audio topology of a standalone lab process. Not a game
// singleton: integration must supply a separately accepted bus-ownership adapter.
public sealed class AudioBackend : IDisposable
{
    private sealed record Player(AudioStreamPlayer Node, AudioStream Stream);
    private static bool leased;
    private readonly Node parent;
    private readonly Dictionary<AudioHandle, Player> players = new();
    private readonly float originalMasterVolume;
    private readonly bool originalMasterMute;
    private bool disposed;
    internal AudioPolicy Policy { get; }
    public MixerPreferences Preferences => Policy.Preferences;
    public AudioLimits Limits => Policy.Limits;
    public int ActivePlayers => Policy.ActivePlayers;
    public long RetainedBytes => Policy.RetainedBytes;
    public int CacheBytes => 0;
    public string Driver { get; }
    public string Device { get; }
    public int NativePlayers => players.Count;
    private AudioBackend(Node parent, AudioLimits limits)
    {
        this.parent = parent; Policy = new(limits);
        Driver = AudioServer.GetDriverName(); Device = AudioServer.OutputDevice;
        originalMasterVolume = AudioServer.GetBusVolumeDb(0); originalMasterMute = AudioServer.IsBusMute(0);
        // ARC-DEV-1431: the engine's AddBus, SetBusName and SetBusSend take no audio driver lock (RemoveBus and SetBusCount do), so
        // a bus added while the audio thread mixes races its bus list and name map (same race as ARC-BUG-1396). Build all buses
        // here, once, under the lock.
        AudioServer.Lock();
        try
        {
            for (int i = 1; i <= 4; i++)
            {
                AudioServer.AddBus(i); AudioServer.SetBusName(i, ((AudioBus)i).ToString()); AudioServer.SetBusSend(i, "Master");
            }
        }
        finally { AudioServer.Unlock(); }
        leased = true; ApplyLevels();
    }
    public static AudioResult Create(Node parent, AudioLimits limits, out AudioBackend? backend)
    {
        backend = null;
        if (!GodotThread.IsMainThread()) return new(AudioError.WrongThread);
        if (!limits.Valid || !parent.IsInsideTree()) return new(AudioError.InvalidCommand);
        if (leased || AudioServer.BusCount != 1 || AudioServer.GetBusName(0) != "Master" || AudioServer.GetDriverName() == "Dummy") return new(AudioError.BackendUnavailable);
        backend = new(parent, limits); return new(AudioError.None);
    }
    public AudioResult Play(PlayCommand command, CancellationToken cancellation = default)
    {
        var reserved = Policy.Reserve(command, cancellation.IsCancellationRequested);
        if (!reserved.Success) return reserved;
        AudioStream? stream = null; AudioStreamPlayer? node = null;
        try
        {
            var bytes = command.Audio.CopyForDecoder();
            if (command.Audio.Kind == MediaKind.Wav)
            {
                if (!PcmWaveProfile.Accept(bytes)) return new(AudioError.InvalidMedia);
                stream = AudioStreamWav.LoadFromBuffer(bytes);
                if (stream is AudioStreamWav wav)
                {
                    var decoded = wav.Data;
                    if (decoded.Length != bytes.Length - 44 || wav.Format != AudioStreamWav.FormatEnum.Format16Bits) return new(AudioError.DecodeFailed);
                    var retained = Policy.RetainDecoded(reserved.Handle, decoded.Length);
                    if (!retained.Success) return retained;
                    wav.LoopMode = command.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                    wav.LoopBegin = 0; wav.LoopEnd = decoded.Length / (wav.Stereo ? 4 : 2);
                }
            }
            else
            {
                stream = AudioStreamMP3.LoadFromBuffer(bytes);
                if (stream is AudioStreamMP3 mp3)
                {
                    var retained = Policy.RetainDecoded(reserved.Handle, bytes.Length);
                    if (!retained.Success) return retained;
                    mp3.Loop = command.Loop;
                }
            }
            if (stream is null || !double.IsFinite(stream.GetLength()) || stream.GetLength() <= 0) return new(AudioError.DecodeFailed);
            if (cancellation.IsCancellationRequested) return new(AudioError.Cancelled);
            node = new() { Stream = stream, Bus = command.Bus.ToString(), ProcessMode = Node.ProcessModeEnum.Always };
            parent.AddChild(node); node.Play();
            players.Add(reserved.Handle, new(node, stream));
            node = null; stream = null; // Ownership transferred only after complete success.
            return reserved;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        { return new(AudioError.DecodeFailed); }
        finally
        {
            if (!players.ContainsKey(reserved.Handle)) Policy.Stop(reserved.Handle);
            if (node is not null) { node.Stream = null; node.Free(); }
            stream?.Dispose();
        }
    }
    public AudioResult Stop(AudioHandle handle)
    {
        var valid = Policy.Validate(handle); if (!valid.Success) return valid;
        if (players.Remove(handle, out var player))
        {
            player.Node.Stop(); player.Node.Stream = null; player.Node.Free(); player.Stream.Dispose();
        }
        return Policy.Stop(handle);
    }
    public AudioResult Pause(AudioHandle handle, bool pause)
    {
        var result = Policy.Pause(handle, pause);
        if (result.Success) players[handle].Node.StreamPaused = pause;
        return result;
    }
    public AudioResult Fade(AudioHandle handle, float gain, double seconds) => Policy.Fade(handle, gain, seconds);
    public AudioResult State(AudioHandle handle, out PlaybackState state) => Policy.State(handle, out state);
    public AudioResult SetLevel(AudioBus bus, BusLevel level)
    {
        var result = Policy.SetLevel(bus, level); if (result.Success) ApplyLevels(); return result;
    }
    public AudioResult ApplyPreferences(MixerPreferences preferences)
    {
        var result = Policy.ApplyPreferences(preferences); if (result.Success) ApplyLevels(); return result;
    }
    private void ApplyLevels()
    {
        foreach (var bus in Enum.GetValues<AudioBus>())
        {
            var level = Policy.Level(bus);
            AudioServer.SetBusVolumeDb((int)bus, level.Volume == 0 ? -80 : Mathf.LinearToDb(level.Volume));
            AudioServer.SetBusMute((int)bus, level.Muted || level.Volume == 0);
        }
    }
    public AudioResult Tick(double seconds)
    {
        var result = Policy.Tick(seconds); if (!result.Success) return result;
        foreach (var (handle, player) in players.ToArray())
        {
            Policy.State(handle, out var state);
            if (!player.Node.Playing && !state.Paused) { Stop(handle); continue; }
            player.Node.VolumeDb = state.Gain == 0 ? -80 : Mathf.LinearToDb(state.Gain);
        }
        return result;
    }
    public void Dispose()
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Dispose on engine thread.");
        if (disposed) return;
        foreach (var handle in players.Keys.ToArray()) Stop(handle);
        Policy.Dispose();
        for (int i = 4; i >= 1; i--) AudioServer.RemoveBus(i);
        AudioServer.SetBusVolumeDb(0, originalMasterVolume); AudioServer.SetBusMute(0, originalMasterMute);
        leased = false; disposed = true;
    }
}
