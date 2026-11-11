using System.Buffers.Binary;
using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-040 self-test on synthetic PCM (no original data): the backend decodes and starts an admitted WAV on a
    /// channel bus, reports Silent instead of Playing without an output device, keeps loops, flattens a channel, refuses
    /// what was not admitted and past the voice limit, stops by generation and removes its buses; the volume file round
    /// trips at the test path.</summary>
    private static async Task AssertViewerAudioAsync(Node host)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-040 audio self-test: " + why); }
        var payload = SoundPayloadReader.Inspect("synthetic/square.wav", SquareWave(22050, 22050));
        Require(payload.Status == SoundPayloadStatus.Admitted && payload.Wav!.FrameCount == 22050, "synthetic WAV admitted");
        var backend = ViewerAudioBackend.Create(host);
        TerrainViewerApp.MuteMasterForTests();  // ARC-DEV-403: the test tones are not heard
        var audible = backend.Audible;
        try
        {
            foreach (var channel in Enum.GetValues<AudioChannel>()) Require(AudioServer.GetBusIndex(ViewerAudioBackend.BusName(channel)) >= 0, $"{channel} bus");
            var expected = backend.Audible ? ViewerAudioStatus.Playing : ViewerAudioStatus.Silent;
            var effect = backend.Play(payload, AudioChannel.Effects, 63);
            Require(effect.Status == expected && effect.Handle is not null, $"effect started {effect.Status}");
            Require(Math.Abs(backend.Volume(effect.Handle!.Value)!.Value - 63) < 0.5, "start volume 63 of 127");
            backend.FlattenChannel(AudioChannel.Effects, 101);
            Require(Math.Abs(backend.Volume(effect.Handle.Value)!.Value - 101) < 0.5, "a channel change sets the bare channel volume");
            Require(backend.Reposition(effect.Handle.Value, 40, -1) && Math.Abs(backend.Volume(effect.Handle.Value)!.Value - 40) < 0.5,
                "a listener move sets the volume (ARC-UI-042)");
            Require(AudioServer.GetBusIndex(ViewerAudioBackend.BusName(AudioChannel.Effects) + " pan -4") >= 0, "and moves the voice to the hard-left bus");
            var music = backend.Play(payload, AudioChannel.Music, 20, loop: true);
            Require(music.Started && backend.ActiveVoices == 2, "a looped sound on the music bus");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            backend.Tick();
            Require(backend.Volume(music.Handle!.Value) is not null, "a loop is not freed by Tick");
            Require(backend.Stop(effect.Handle.Value) && !backend.Stop(effect.Handle.Value) && backend.Volume(effect.Handle.Value) is null, "stop once, then a stale handle");
            var refused = SoundPayloadReader.Inspect("synthetic/text.wav", "not audio"u8.ToArray());
            Require(backend.Play(refused, AudioChannel.Effects, 10).Status == ViewerAudioStatus.NotAdmitted, "a refused payload is not played");
            var started = new List<ViewerAudioResult>();
            while (backend.ActiveVoices < ViewerAudioBackend.MaxVoices) started.Add(backend.Play(payload, AudioChannel.Effects, 10, loop: true));
            Require(started.All(r => r.Started) && backend.Play(payload, AudioChannel.Effects, 10).Status == ViewerAudioStatus.VoiceLimit, "the voice limit refuses, nothing is stolen");
            var reused = started[0];
            backend.StopAll();
            Require(backend.ActiveVoices == 0 && backend.Volume(reused.Handle!.Value) is null, "stop all");
            var next = backend.Play(payload, AudioChannel.Voice, 10);
            var (old, fresh) = (reused.Handle!.Value, next.Handle!.Value);
            Require(fresh.Slot == 0 && fresh.Generation != old.Generation && !backend.Stop(old), "a reused slot ignores the old handle");
        }
        finally { backend.Dispose(); }
        foreach (var channel in Enum.GetValues<AudioChannel>()) Require(AudioServer.GetBusIndex(ViewerAudioBackend.BusName(channel)) < 0, $"{channel} bus removed");
        Require(backend.Play(payload, AudioChannel.Effects, 63).Status == ViewerAudioStatus.Disposed, "a disposed backend plays nothing");

        var previous = OS.GetEnvironment(AudioVolumesStore.TestVariable);
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-040-store-").FullName;
        try
        {
            var path = System.IO.Path.Combine(root, "audio.json");
            OS.SetEnvironment(AudioVolumesStore.TestVariable, path);
            var store = new AudioVolumesStore(automated: true);
            Require(store.Persistent && store.Load() == AudioVolumes.Defaults, "a missing file loads 5/5/5");
            Require(store.Save(new AudioVolumes(2, 9, 0)) && new AudioVolumesStore(automated: false).Load() == new AudioVolumes(2, 9, 0), "saved and read back");
            System.IO.File.WriteAllText(path, "{\"version\":1,\"effects\":12,\"voice\":9,\"music\":0}");
            Require(store.Load() == AudioVolumes.Defaults, "an invalid file loads the defaults");
            OS.UnsetEnvironment(AudioVolumesStore.TestVariable);
            var automated = new AudioVolumesStore(automated: true);
            Require(!automated.Persistent && automated.Load() == AudioVolumes.Defaults && !automated.Save(AudioVolumes.Defaults), "an automated run has no file");
        }
        finally
        {
            if (string.IsNullOrEmpty(previous)) OS.UnsetEnvironment(AudioVolumesStore.TestVariable);
            else OS.SetEnvironment(AudioVolumesStore.TestVariable, previous);
            System.IO.Directory.Delete(root, true);
        }
        Require(TerrainViewerApp.InterfaceSoundIds.Distinct().Count() == TerrainViewerApp.InterfaceSoundIds.Count
            && TerrainViewerApp.InterfaceSoundIds.All(id => id is >= 3000 and <= 3028), "interface ids");
        GD.Print($"ARC-UI-040 self-test audio PASS decode/start ({(audible ? "device" : "silent, no device")}), loop, flatten, refusals, voice limit, generations, buses removed, volume file");
    }

    /// <summary>A mono 16-bit PCM square wave written independently for the test.</summary>
    private static byte[] SquareWave(int rate, int frames)
    {
        var bytes = new byte[44 + frames * 2];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(36 + frames * 2)); "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)rate); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)rate * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)frames * 2);
        for (var i = 0; i < frames; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + i * 2), (short)((i / 50) % 2 == 0 ? 6000 : -6000));
        return bytes;
    }
}
