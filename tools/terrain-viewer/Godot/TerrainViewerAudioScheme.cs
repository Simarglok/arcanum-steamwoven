using Arcanum.Data.Audio;
using Arcanum.Data.Maps;
using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-041: the session's music and ambient schemes play in the AREA viewer (exterior, cave and map mode;
// docs/audio/scheme-player.md). The viewer tells the session when a map opens (its default pair from the map info, read
// through the universal map catalogue) and when the PC enters another sector (the sector's sound record); the
// Godot-free SchemePlayer turns the session's slots, the game hour and real time into song, ambient and combat commands,
// which this file plays through the backend. A quick load keeps the loaded state; only the player starts afresh.
public partial class TerrainViewerApp
{
    private SchemePlayer? _schemePlayer;
    private IAudioRandom _audioRandom = new SystemAudioRandom(new Random());
    private readonly Dictionary<long, ViewerAudioHandle> _schemeVoices = [];
    private ViewerAudioHandle? _combatStinger, _combatLoop;
    private (string Module, WorldCatalog? World, MapList? Maps)? _soundWorld;
    private readonly Dictionary<int, SchemeSounds?> _schemeCache = [];
    private readonly Dictionary<(int Map, long Sector), SectorSoundRecord?> _sectorSoundCache = [];
    private readonly Dictionary<string, SoundPayload> _ambientPayloads = new(StringComparer.Ordinal);
    private (int Map, long Sector)? _soundSector;
    private string? _schemeNotice;
    private int _ambientStarts;

    private sealed class CatalogSchemes(SoundCatalog catalog) : ISessionSoundSchemes
    {
        public bool Loads(int schemeId) => catalog.Scheme(schemeId).Status == SoundSchemeStatus.Loaded;
        public bool StartsTransition(int schemeId) => catalog.Scheme(schemeId).Entries.Any(e => e.Kind == SoundSchemeEntryKind.TransitionSong);
    }

    private SoundCatalog? AreaSoundCatalog() => _arguments?.ModulePath is { } module ? SoundCatalogFor(module) : null;

    // The universal map catalogue and map list of the module, opened once on the sound catalogue's stack.
    private (WorldCatalog? World, MapList? Maps) SoundWorld()
    {
        if (_arguments?.ModulePath is not { } module || AreaSoundCatalog() is null || _soundContent?.Stack is not { } stack) return (null, null);
        if (_soundWorld is { } cached && cached.Module == module) return (cached.World, cached.Maps);
        var maps = MapList.Read(stack);
        var world = WorldCatalog.Open(stack);
        _soundWorld = (module, world, maps.Status == MapListStatus.Read ? maps : null);
        _sectorSoundCache.Clear();
        GD.Print($"ARC-UI-041 map catalogue maps={world.Maps.Count} list={maps.Status}");
        return (_soundWorld.Value.World, _soundWorld.Value.Maps);
    }

    private WorldMap? SoundMap(int catalogueMap)
    {
        var (world, maps) = SoundWorld();
        return maps?.ByCatalogue(catalogueMap) is { } entry ? world?.Find(entry.Name) : null;
    }

    /// <summary>The map info's <c>SoundScheme</c> pair of a map, or null without one.</summary>
    private SessionMapSound? MapSoundDefault(int catalogueMap) =>
        SoundMap(catalogueMap)?.Info is { MusicScheme: { } music } info ? new SessionMapSound(music, info.AmbientScheme ?? 0) : null;

    /// <summary>For a quick load: the default pair of the saved actor's map (the original opens it before loading sound).</summary>
    private SessionMapSound? MapSoundDefault(ActorPlacement actor) =>
        SoundWorld().Maps?.ByRuntime(actor.Map.RuntimeMap) is { } entry ? MapSoundDefault(entry.Catalogue) : null;

    /// <summary>A map opened in play (area start, a cave or map transfer): the session stops every sound and plays the map's
    /// default pair; every sound of the scene stops; the arrival sector applies.</summary>
    private void EnterAreaMapSound(int catalogueMap)
    {
        if (_areaSession is not { } session || _audio is null || AreaSoundCatalog() is not { } catalog) return;
        session.AttachSoundSchemes(new CatalogSchemes(catalog));
        var mapDefault = MapSoundDefault(catalogueMap);
        var result = session.EnterMapSound(session.Version, mapDefault);
        StopSceneSound();
        _soundSector = null; FollowAreaSound();
        var state = session.QuerySound().State;
        GD.Print($"ARC-UI-041 map-sound map={catalogueMap} default={(mapDefault is { } d ? $"{d.Music}/{d.Ambient}" : "none")} result={result.Disposition} slots={state.Slot0}/{state.Slot1}");
    }

    /// <summary>A quick load replaced the session: it already holds the loaded sound state; the scene starts afresh.</summary>
    private void RebindAreaSound()
    {
        if (_areaSession is not { } session || AreaSoundCatalog() is not { } catalog) return;
        session.AttachSoundSchemes(new CatalogSchemes(catalog));
        StopSceneSound();
        _soundSector = CurrentSoundSector();
    }

    private void StopSceneSound()
    {
        _audio?.StopAll();
        _schemeVoices.Clear(); _combatStinger = _combatLoop = null;
        StopPositionalSound();
        _schemePlayer = new SchemePlayer(WrapReversedWindows, _areaSession?.QuerySound().CombatStarts ?? 0);
    }

    // Steamwoven runs a reversed hour window past midnight; the Original setting keeps the original's silence (SN14).
    private bool WrapReversedWindows => _areaSession?.Rules.Sound.HourWindowsNeverWrap != true;

    private (int Map, long Sector)? CurrentSoundSector()
    {
        if (_readyArea is not { } area || _worldActor.Session is not AreaGridSession grid) return null;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        return (area.Snapshot.CatalogueMap, new WorldSectorCoordinate((int)(cell.X / 64), (int)(cell.Y / 64)).Id);
    }

    /// <summary>The PC entered another sector: its sound record's pair plays unless both ids are 0.</summary>
    private void FollowAreaSound()
    {
        if (_areaSession is not { } session || CurrentSoundSector() is not { } current || current == _soundSector) return;
        _soundSector = current;
        if (SectorSound(current) is not { } record) return;
        var result = session.EnterSectorSound(session.Version, record.MusicScheme, record.AmbientScheme);
        if (result.Disposition == GameSessionDisposition.Changed)
            GD.Print($"ARC-UI-041 sector-sound sector={current.Sector} pair={record.MusicScheme}/{record.AmbientScheme}");
    }

    private SectorSoundRecord? SectorSound((int Map, long Sector) key)
    {
        if (_sectorSoundCache.TryGetValue(key, out var cached)) return cached;
        SectorSoundRecord? record = null;
        if (SoundMap(key.Map) is { } map && WorldSectorCoordinate.TryFromId(key.Sector, out var coordinate) && map.HasSectorFile(coordinate)
            && SoundWorld().World!.ReadSector(map, coordinate).Sector is { } sector)
            record = SoundCatalog.SectorSound(sector);
        _sectorSoundCache[key] = record;
        return record;
    }

    private SchemeSounds? SchemeContent(int id)
    {
        if (id == 0 || AreaSoundCatalog() is not { } catalog) return null;
        if (_schemeCache.TryGetValue(id, out var cached)) return cached;
        var scheme = catalog.Scheme(id);
        SchemeSounds? content = scheme.Status != SoundSchemeStatus.Loaded ? null : new(id, scheme.Entries.Select(e => new SchemeSound(e.ListNumber,
            e.Kind switch
            {
                SoundSchemeEntryKind.LoopSong => SchemeSoundKind.LoopSong,
                SoundSchemeEntryKind.AnchorSong => SchemeSoundKind.AnchorSong,
                SoundSchemeEntryKind.TransitionSong => SchemeSoundKind.TransitionSong,
                _ => SchemeSoundKind.Ambient,
            }, e.File.Status == SoundFileStatus.Found ? e.File.Key : null, e.Frequency, e.HourStart, e.HourEnd, e.VolumeMin, e.VolumeMax,
            e.BalanceLeft, e.BalanceRight)).ToArray());
        _schemeCache[id] = content;
        return content;
    }

    /// <summary>One frame of scheme sound: the PC's sector, then the player's commands.</summary>
    private void TickSceneSound()
    {
        if (_areaSession is not { } session || _audio is null || _schemePlayer is null || AreaSoundCatalog() is not { } catalog) return;
        FollowAreaSound();
        // Song voices the backend released (a finished one-shot, a faded loop) leave the map.
        foreach (var (voice, handle) in _schemeVoices.ToArray()) if (_audio.Volume(handle) is null) _schemeVoices.Remove(voice);
        var view = session.QuerySound();
        var state = view.State;
        var input = new SchemeInput(SchemeContent(state.Slot0), SchemeContent(state.Slot1), state.RestorePending, state.CombatMusic, view.CombatStarts);
        var commands = _schemePlayer.Update(input, (long)Time.GetTicksMsec(), session.QueryClockSnapshot().Hour,
            voice => _schemeVoices.TryGetValue(voice, out var handle) && _audio.IsPlaying(handle), _audioRandom);
        foreach (var command in commands) Execute(command, catalog, session);
    }

    private void Execute(SchemeCommand command, SoundCatalog catalog, GameSessionOwner session)
    {
        var music = AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Music);
        switch (command.Kind)
        {
            case SchemeCommandKind.StartSong:
                if (command.Sound!.File is { } song && Play(catalog.OpenPayload(catalog.ResolveFile(song)), AudioChannel.Music,
                        AudioVolumeRules.StartVolume(command.Volume, music), command.Loop) is { } songHandle)
                    _schemeVoices[command.Voice] = songHandle;
                break;
            case SchemeCommandKind.FadeOut:
                if (_schemeVoices.Remove(command.Voice, out var fading)) _audio!.FadeOut(fading, command.FadeMs);
                break;
            case SchemeCommandKind.PlayAmbient:
                if (command.Sound!.File is { } ambient)
                {
                    if (!_ambientPayloads.TryGetValue(ambient, out var payload))
                        _ambientPayloads[ambient] = payload = catalog.OpenPayload(catalog.ResolveFile(ambient));
                    var effects = AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Effects);
                    if (Play(payload, AudioChannel.Effects, AudioVolumeRules.StartVolume(command.Volume, effects), false, (command.Balance - 64) / 64.0) is not null)
                        _ambientStarts++;
                }
                break;
            case SchemeCommandKind.TransitionFinished:
                _schemeVoices.Remove(command.Voice);
                session.FinishSoundTransition(session.Version);
                break;
            case SchemeCommandKind.StartCombat:
                if (_combatStinger is { } oldStinger) _audio!.Stop(oldStinger);
                if (_combatLoop is { } oldLoop) _audio!.Stop(oldLoop);
                _combatStinger = Play(catalog.OpenPayload(catalog.ResolveSoundFile(SoundCatalog.CombatStingerName(command.Stinger))), AudioChannel.Music, music, false);
                _combatLoop = Play(catalog.OpenPayload(catalog.ResolveSoundFile(SoundCatalog.CombatLoopName)), AudioChannel.Music, music, true);
                _schemeNotice = $"combat music (stinger {command.Stinger})";
                break;
            case SchemeCommandKind.StopCombat:
                if (_combatStinger is { } stinger) _audio!.Stop(stinger);
                if (_combatLoop is { } loop) _audio!.Stop(loop);
                _combatStinger = _combatLoop = null; _schemeNotice = null;
                break;
        }
    }

    private ViewerAudioHandle? Play(SoundPayload payload, AudioChannel channel, int volume, bool loop, double pan = 0) =>
        _audio!.Play(payload, channel, volume, loop, pan) is { Started: true, Handle: { } handle } ? handle : null;

    private string SchemeStatus()
    {
        if (_areaSession is not { } session || _schemePlayer is null) return "";
        var state = session.QuerySound().State;
        return $" | music/ambient: slots {state.Slot0}/{state.Slot1}{(state.CombatMusic ? ", combat" : "")}, voices {_schemeVoices.Count}"
            + (_schemeNotice is null ? "" : "; " + _schemeNotice);
    }
}
