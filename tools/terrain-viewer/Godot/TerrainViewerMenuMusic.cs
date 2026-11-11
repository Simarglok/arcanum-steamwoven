using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-139: the main menu's music without a game session or a scene. The original's main theme loops the whole time from the start until a
// game begins or a save loads (docs/audio/scheme-player.md, SN38). Since ARC-DEV-387 the start map loads only for a new character, so the menu
// stands over no scene and no session; its pair (the shop map's start sector, 26/29: scheme 26 loops arcanum.mp3 at every hour, 29 loads
// nothing) plays through a scheme player of its own, from the two slots <see cref="MainMenuSound.Choose(MainMenuSound.SoundPair?, MainMenuSound.SoundPair?, Func{int, bool})"/>
// fills. The song belongs to the menu: it starts when the main menu (root window Main) opens and goes when the menu ends (a new game's map
// open, a save loaded, any close); a scene published under the menu (the lazy start, a save's map) neither restarts nor stops it, and the
// scene's own pair is not played while the menu stands. The in-play menu keeps the game's music. The session's sound state is not touched.
public partial class TerrainViewerApp
{
    /// <summary>The menu has no game clock; its scheme plays at every hour (a reversed or short window in another module's menu scheme is not modelled).</summary>
    private const int MenuMusicHour = 12;

    private sealed class MenuMusicState(SchemePlayer player, SchemeSounds? slot0, SchemeSounds? slot1)
    {
        public SchemePlayer Player { get; } = player;
        public SchemeSounds? Slot0 { get; } = slot0;
        public SchemeSounds? Slot1 { get; } = slot1;
        public Dictionary<long, ViewerAudioHandle> Voices { get; } = [];
    }

    /// <summary>The menu's song while a main menu stands (null otherwise).</summary>
    private MenuMusicState? _menuMusic;

    /// <summary>Starts the menu's music (once per menu): the pair of the game init's opening map and start sector, through the scheme player.</summary>
    private void StartMenuMusic()
    {
        if (_menuMusic is not null || _audio is null || AreaSoundCatalog() is not { } catalog) return;
        using var span = PerfProbe.Enabled ? PerfProbe.Span("menu.music") : default;
        var (_, maps) = SoundWorld();
        if (maps is null || MainMenuSound.Choose(maps) is not { } place) return;
        var mapDefault = MapSoundDefault(place.Map.Catalogue) is { } map ? new MainMenuSound.SoundPair(map.Music, map.Ambient) : (MainMenuSound.SoundPair?)null;
        var sector = place.Sector is { } coordinate ? SectorSound((place.Map.Catalogue, coordinate.Id)) : null;
        var slots = MainMenuSound.Choose(mapDefault, sector is null ? null : new(sector.MusicScheme, sector.AmbientScheme),
            id => catalog.Scheme(id).Status == SoundSchemeStatus.Loaded);
        // A scene that already stands under this menu (the menu reopened over a game) loses its sounds: the menu's song replaces them.
        if (_areaSession is not null) StopSceneSound();
        _menuMusic = new(new SchemePlayer(WrapReversedWindows), SchemeContent(slots.Slot0), SchemeContent(slots.Slot1));
        GD.Print($"ARC-UI-139 menu-music map={place.Map.Name} sector={(place.Sector is { } c ? $"{c.X},{c.Y}" : "none")} "
            + $"default={(mapDefault is { } d ? $"{d.Music}/{d.Ambient}" : "none")} pair={(sector is not null ? $"{sector.MusicScheme}/{sector.AmbientScheme}" : "none")} slots={slots.Slot0}/{slots.Slot1}");
    }

    /// <summary>One frame of the menu's music; it ends with the menu (the menu's closes are many, the frame after the last one is the stop).</summary>
    private void TickMenuMusic()
    {
        if (_menuMusic is not { } state) return;
        if (_audio is null || AreaSoundCatalog() is not { } catalog || _mainMenu is null) { StopMenuMusic(); return; }
        foreach (var (voice, handle) in state.Voices.ToArray()) if (_audio.Volume(handle) is null) state.Voices.Remove(voice);
        var input = new SchemeInput(state.Slot0, state.Slot1, RestorePending: false, CombatMusic: false, CombatStarts: 0);
        var commands = state.Player.Update(input, (long)Time.GetTicksMsec(), MenuMusicHour,
            voice => state.Voices.TryGetValue(voice, out var handle) && _audio.IsPlaying(handle), _audioRandom);
        foreach (var command in commands) Execute(command, catalog, state.Voices, MenuMusicHour, session: null);
    }

    /// <summary>Stops every sound of the scene (a map opening, a scene's end). The main menu's song belongs to the menu, not to the scene under
    /// it, so it plays on while the menu stands (a scene's publication or teardown under the lazy-start menu neither stops nor restarts it).</summary>
    private void StopAllSounds()
    {
        if (_menuMusic is not null && _mainMenu is null) StopMenuMusic();
        if (_menuMusic is { } menu) _audio?.StopAllExcept(menu.Voices.Values); else _audio?.StopAll();
    }

    /// <summary>The menu ended: its song stops at once (a game's map opening stops every sound in the original).</summary>
    private void StopMenuMusic()
    {
        if (_menuMusic is not { } state) return;
        _menuMusic = null;
        foreach (var handle in state.Voices.Values) _audio?.Stop(handle);
        state.Voices.Clear();
        GD.Print("ARC-UI-139 menu-music stopped");
    }
}
