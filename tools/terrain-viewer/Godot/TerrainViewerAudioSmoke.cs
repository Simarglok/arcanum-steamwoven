using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-040 native smoke on the original installation: the area module's sound catalogue opens, every fixed
    /// interface sound resolves, is admitted, decodes and starts on the effects channel, and the world-map travel music (an
    /// MP3 in the module folder) loops on the music channel; with an output device each is still playing a few frames
    /// later. Nothing here depends on hearing it.</summary>
    private async Task RunAreaAudioSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-040 audio smoke: " + why); }
        Require(_audio is not null, "no backend");
        var catalog = SoundCatalogFor(_arguments!.ModulePath);
        Require(catalog is not null, $"no catalogue ({_soundContent?.Detail})");
        var handles = new List<ViewerAudioHandle>();
        foreach (var id in InterfaceSoundIds)
        {
            var (sfx, payload, result) = PlaySfx(id);
            Require(sfx?.Playable == true && payload?.Status == SoundPayloadStatus.Admitted && result?.Started == true,
                $"interface sound {id}: {sfx?.Status} {payload?.Status} {result?.Status}");
            handles.Add(result!.Value.Handle!.Value);
        }
        var musicFile = catalog!.ResolveSoundFile(SoundCatalog.WorldMapTravelMusicName);
        var musicPayload = catalog.OpenPayload(musicFile);
        Require(musicPayload is { Status: SoundPayloadStatus.Admitted, Kind: SoundPayloadKind.Mp3 }, $"music {musicFile.Status} {musicPayload.Status}");
        var music = _audio!.Play(musicPayload, AudioChannel.Music, AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Music), loop: true);
        Require(music.Started, $"music {music.Status}");
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_audio.Audible)
            Require(_audio.IsPlaying(music.Handle!.Value) && handles.Any(_audio.IsPlaying), "nothing playing a few frames later");
        var effects = handles.Count;
        _audio.StopAll();
        Require(_audio.ActiveVoices == 0, "stop all");
        GD.Print($"ARC-UI-040 native-smoke audio PASS driver={_audio.Driver} interface={effects} music=mp3 loop catalogue=\"{_soundContent?.Detail}\"");
    }
}
