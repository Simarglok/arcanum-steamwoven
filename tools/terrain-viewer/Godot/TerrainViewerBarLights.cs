using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1364: the top strip's buttons lit by game events, and the Map button's face (ledger GB15; checked in the 1.0.7.4 executable: the light
// callback 0x556A90 with the lit art table 0x5C6510, the map callback 0x556B70, both registered at 0x57C83E and called through 0x460790 and
// 0x4607B0). The state is AreaBarLights (AreaGameBar.Lights); this file applies the events to it:
//   Character: a level-up of the hero (0x4A69C0, ShowLevelUps) lights it; the character sheet open for the hero puts it out (0x5597C0).
//   Logbook:   a change of the hero's quests, blessings, curses or reputations (0x4C4E60, 0x4C5070, 0x4C42F0, 0x4C3E40, 0x4C4450, 0x4C3FA0, 0x4C1D20,
//              0x4C1E10) lights it; the logbook open puts it out (0x53F0E0).
//   Inventory: the hero's held list taking or losing an object (0x466640, 0x467860) lights it; the inventory window open puts it out (0x5727B0).
//   Map:       a newly known world-map area (0x4CAFD0) lights the world map face; the map window open puts it out (0x5607E0). The face itself is
//              the hero's sector: no town map (0x4BE380 answers 0) shows the world map art 194, a town map the town art 193 (0x564F60).
// A light is not stored while the slot's own window is open. The window-open puts it out all the time here (the original does it once when
// the window opens: the same final state, as the light is refused while the window stays open). The original keeps the state in its save
// (0x54A1A0, restored by 0x54A220); ARC-UI-1542: the session snapshot holds it (SessionBarLightsState, recorded here at each change, read back when a
// loaded session first shows), a new game starts idle.
public partial class TerrainViewerApp
{
    private GameSessionOwner? _barLightsSession;
    private long _barLightsRevision = -1;
    private SessionInterfaceStamp? _barLightsStamp;
    private int _barLightsLit;
    // ARC-UI-1542: the sector's last seen town-map answer (null before the first look of a session), and whether the lights came from a save: a restored
    // face holds until the answer changes, as the original's stored map mode does until the next map callback (0x556B70).
    private bool? _barLightsTownMap;
    private bool _barLightsRestored;

    /// <summary>The lights lit since the app started (for the logs and the smoke).</summary>
    internal int BarLightsLitForSelfTest => _barLightsLit;

    /// <summary>Whether the slot's own window is open (the light callback's test, GB15). The sheet counts only for the hero's own (an NPC's or companion's sheet is another window of the same mode).</summary>
    private bool BarLightWindowOpen(AreaLightSlot slot) => slot switch
    {
        AreaLightSlot.Character => SheetOpen && _sheetNpc is null,
        AreaLightSlot.Logbook => LogbookOpen,
        // The barter window is the inventory window in its barter mode (GB16), so the same flag.
        AreaLightSlot.Inventory => InventoryOpen || BarterOpen || ItemServiceOpen,
        _ => WorldMapOpen,
    };

    /// <summary>Lights a slot (the callback of GB15 with the flag set); true when that changed it.</summary>
    private bool TryLight(AreaLightSlot slot)
    {
        if (!_gameBar.Lights.Light(slot, true, BarLightWindowOpen(slot))) return false;
        _barLightsLit++;
        GD.Print($"ARC-UI-1364 bar light {slot} lit");
        return true;
    }

    /// <summary>ARC-UI-1542: hands the lit state to the session so a save holds it (a session records the same state as no change).</summary>
    private void RecordBarLights(GameSessionOwner session)
    {
        var lights = _gameBar.Lights;
        session.RecordBarLights(new(lights.LitMask, (int)lights.MapMode));
    }

    /// <summary>A level-up of the hero lights the Character button unless the sheet is open (GB16).</summary>
    private void LightCharacterForLevelUp()
    {
        if (!TryLight(AreaLightSlot.Character)) return;
        if (_areaSession is { } session) RecordBarLights(session);
        _gameBarPanel?.Render();
    }

    /// <summary>Per frame, before the bars show or hide: follows the session's events with the lights (the stamps' differences), puts out the
    /// light of every slot whose window is open, and sets the Map button's face from the hero's sector.</summary>
    private void SyncBarLights()
    {
        // No session between two maps of a transfer (the scene loads while the carried session is held back): the lights and the baseline stay, so the
        // carried session comes back to them; only a different session (a new game, a load) starts afresh.
        if (_areaSession is not { } session) return;
        var changed = false;
        if (!ReferenceEquals(session, _barLightsSession))
        {
            // A new session (a new game, a load): the first stamps are the baseline, nothing lights for what the session already holds.
            _barLightsSession = session; _barLightsStamp = null; _barLightsRevision = -1; _barLightsTownMap = null;
            changed |= _gameBar.Lights.Reset();
            // ARC-UI-1542: a loaded session carries the saved lights (0x54A220 restores them and redraws the five buttons); a new game or an older save starts idle.
            var saved = session.QueryBarLights();
            _barLightsRestored = saved is not null;
            if (saved is { } lights) changed |= _gameBar.Lights.Restore(lights.LitMask, (AreaLightSlot)lights.MapMode);
        }
        var revision = session.Version.Revision;
        if (revision != _barLightsRevision)
        {
            _barLightsRevision = revision;
            if (session.QueryHeroInterfaceStamp() is { } stamp)
            {
                if (_barLightsStamp is { } before)
                {
                    if (stamp.Logbook != before.Logbook) changed |= TryLight(AreaLightSlot.Logbook);
                    if (stamp.Inventory != before.Inventory) changed |= TryLight(AreaLightSlot.Inventory);
                    if (stamp.Areas != before.Areas) changed |= TryLight(AreaLightSlot.WorldMap);
                }
                _barLightsStamp = stamp;
            }
            else _barLightsStamp = null;
        }
        foreach (var slot in new[] { AreaLightSlot.Character, AreaLightSlot.Logbook, AreaLightSlot.Inventory, AreaLightSlot.WorldMap })
            if (BarLightWindowOpen(slot)) changed |= _gameBar.Lights.Light(slot, false, true);
        if (_readyMode == TerrainViewerMode.Area && _readyArea is not null && _worldActor.Session is AreaGridSession)
        {
            // The face follows the hero's sector. After a load the first answer is only the baseline: the saved face holds until the answer changes.
            var townMap = PcTownMapNumber() > 0;
            if (_barLightsTownMap is null && _barLightsRestored) _barLightsTownMap = townMap;
            else if (_barLightsTownMap != townMap)
            {
                _barLightsTownMap = townMap;
                changed |= _gameBar.Lights.SetMapMode(townMap ? AreaLightSlot.TownMap : AreaLightSlot.WorldMap);
            }
        }
        // Recorded whether or not a light changed now: a level-up that lit the Character button while no session was set (a map transfer) is still to be handed over, and the same state is no change.
        RecordBarLights(session);
        if (changed) _gameBarPanel?.Render();
    }
}
