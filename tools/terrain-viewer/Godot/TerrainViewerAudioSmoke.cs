using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.Session;
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
        await RunAreaSchemeSmokeAsync();
    }

    private sealed class FirstDraw : IAudioRandom
    {
        public int Next(int min, int max) => min;
    }

    /// <summary>ARC-UI-041 native smoke: the session holds the map's (or arrival sector's) scheme pair; with draws pinned to
    /// their minimum, a loop song inside its hour window plays on the music channel and an ambient sound inside its window
    /// fires within the first ticks; the combat stinger and loop start together and stop at once.</summary>
    private async Task RunAreaSchemeSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-041 scheme smoke: " + why); }
        Require(_areaSession is not null && _schemePlayer is not null && AreaSoundCatalog() is not null, "no session, player or catalogue");
        var session = _areaSession!;
        var state = session.QuerySound().State;
        var hour = session.QueryClockSnapshot().Hour;
        var schemes = new[] { SchemeContent(state.Slot0), SchemeContent(state.Slot1) }.OfType<SchemeSounds>().ToArray();
        var player = new SchemePlayer(WrapReversedWindows);
        var loopExpected = schemes.Any(s => s.Sounds.Any(x => x.Kind == SchemeSoundKind.LoopSong && x.File is not null && player.InWindow(x, hour)));
        var ambientExpected = schemes.Any(s => s.Sounds.Any(x => x.Kind == SchemeSoundKind.Ambient && x.File is not null && x.Frequency > 0 && player.InWindow(x, hour)));
        var random = _audioRandom;
        try
        {
            _audioRandom = new FirstDraw(); _ambientStarts = 0;
            StopSceneSound();
            var started = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - started < 800)
            {
                TickSceneSound();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var loopPlaying = _schemeVoices.Values.Any(_audio!.IsPlaying);
            if (_audio.Audible && loopExpected) Require(loopPlaying, "no loop song playing inside its window");
            if (ambientExpected) Require(_ambientStarts > 0, "no ambient fired with the first draw");
            var catalog = AreaSoundCatalog()!;
            // Any map's ambient path: request the first scheme of the catalogue with an ambient sound inside this hour.
            var ambientScheme = catalog.SchemeIds.Select(SchemeContent).OfType<SchemeSounds>()
                .FirstOrDefault(s => s.Sounds.Any(x => x.Kind == SchemeSoundKind.Ambient && x.File is not null && x.Frequency > 0 && player.InWindow(x, hour)));
            Require(ambientScheme is not null, "no scheme with an ambient sound at this hour");
            Require(session.PlaySoundScheme(session.Version, state.Slot0, ambientScheme!.Id).Disposition != GameSessionDisposition.Rejected, "scheme request refused");
            var before = _ambientStarts; started = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - started < 600)
            {
                TickSceneSound();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(_ambientStarts > before, $"no ambient of scheme {ambientScheme.Id} fired with the first draw");
            var requested = ambientScheme.Id;
            session.PlaySoundScheme(session.Version, state.Slot0, state.Slot1); // the map's pair again for later stages
            Execute(new SchemeCommand(SchemeCommandKind.StartCombat, Stinger: 1), catalog, session);
            Require(_combatStinger is not null && _combatLoop is not null, "combat stinger or loop did not start");
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_audio.Audible) Require(_audio.IsPlaying(_combatLoop!.Value), "combat loop not playing");
            Execute(new SchemeCommand(SchemeCommandKind.StopCombat), catalog, session);
            Require(_combatStinger is null && _combatLoop is null, "combat music not stopped");
            GD.Print($"ARC-UI-041 native-smoke schemes PASS slots={state.Slot0}/{state.Slot1} hour={hour} loopExpected={loopExpected} loopPlaying={loopPlaying} ambientExpected={ambientExpected} ambients={_ambientStarts} ambientScheme={requested} combat=stinger+loop");
        }
        finally
        {
            _audioRandom = random;
            StopSceneSound();
        }
    }
}
