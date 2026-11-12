using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-WORLD-1159 (E2-c): the viewer's half of the world journey's random encounters. It gives the pass world the module's encounter tables and the place of the roll (<see cref="EncounterSiteNow"/>: the start map, the hero in
/// no named area, the world-map marker while the party travels with the map open, else the hero's tile), and, after the pass, does what the original's hook does with a fired roll (0x5595B0): the open world map closes (a journey
/// ends in a landing where the marker stands, 0x560F40) and its opener is refused for 4,000 real milliseconds (0x560720, message 602), else an open rest window closes (0x57B450).
/// </summary>
public partial class TerrainViewerApp
{
    private (AreaWorldMapData Data, AreaEncounterHost Host)? _encounterHost;
    // 0x560720: the world map's opener is refused until this host time (real milliseconds); 0 for none.
    private long _worldMapLockedUntilHostMs;
    /// <summary>The real time the world map stays refused after an encounter (0x560720, the UI action 13 delay).</summary>
    internal const long WorldMapEncounterLockMs = 4000;

    /// <summary>The encounter host of the pass world: the tables of the loaded world-map data; null until it is loaded (no event is armed before).</summary>
    private AreaEncounterHost? EncounterHostNow()
    {
        if (_worldMapData is not { Data: { } data }) return null;
        if (_encounterHost is not { } held || !ReferenceEquals(held.Data, data)) _encounterHost = held = (data, new AreaEncounterHost(data.Encounters, EncounterSiteNow));
        return held.Host;
    }

    /// <summary>The place the Random Encounters hook acts at, or null when it does nothing (0x5595B0): not the start map, or the hero in a named area (0x4CB6A0).</summary>
    private SessionEncounterSite? EncounterSiteNow()
    {
        if (_areaSession is null || _readyArea is not { } area || _worldMapData is not { Data: { } data } || _worldActor.Session is not AreaGridSession grid) return null;
        if (area.Snapshot.RuntimeMap != data.StartRuntime) return null;
        var hero = PcCell();
        var heroCell = new CellCoord(checked((int)hero.X), checked((int)hero.Y));
        if ((AreaStartMapAreas.AreaAt(data, area.Snapshot.Sectors, heroCell) ?? 0) != 0) return null;
        // 0x566CC0: the world-map marker while the party travels with the map open, else the hero.
        var location = WorldMapOpen && _worldMap is { Travelling: true } map ? WorldTravelGeometry.Location(map.Position, data.Width) : new TravelLocation(hero.X, hero.Y);
        var sector = WorldTravelGeometry.Sector(location);
        var word = new TerrainWord(data.Sectors.TerrainWordAt(sector.X, sector.Y));
        var outside = word.Value == TerrainWord.OutsideGrid;
        var place = new EncounterPlace(location, outside ? -1 : word.Base, outside ? -1 : word.Second, AreaStartMapAreas.InTownMapSector(data, area.Snapshot.Sectors, location), SleepOpen);
        var steps = grid.Snapshot;
        return new(new SessionMapId(CurrentAreaMap), place, heroCell)
        {
            DropMap = AreaDropMapNow(),
            // 0x4D7110, as far as the shown window's terrain tells (a tile outside it cannot be judged and counts as free).
            Blocked = cell => steps.Terrain(new(cell.X, cell.Y))?.Disposition == CardinalTraversalDisposition.Blocked,
        };
    }

    /// <summary>After a pass: logs what the Random Encounters events did and acts on a fired one as the hook does (0x5595B0). The opener of the world map refuses while the lock runs.</summary>
    private void HandleEncounterNotices(long hostMs)
    {
        if (_areaSession is not { } session) return;
        foreach (var notice in session.TakeEncounterNotices())
        {
            GD.Print($"ARC-WORLD-1159 encounter outcome={notice.Outcome} roll={notice.Roll} table={notice.Table} row={notice.Row} made={notice.Made.Count} lost={notice.Lost} rearmed={notice.Rearmed}"
                + (notice.Place is { } place ? $" at=({place.Location.X},{place.Location.Y}) rest={place.RestWindowOpen}" : ""));
            _lastEncounterNotice = notice;
            if (notice.Outcome != SessionEncounterOutcome.Fired) continue;
            _encountersFired++;
            // RK16 (0x442260): the creature's creation finish runs the restock body, so a made critter's goods are generated from the frame as a script-made critter's are.
            foreach (var made in notice.Made) QueueCreatedInventory(made.Entity);
            if (WorldMapOpen && _worldMap is { } map)
            {
                _worldMapLockedUntilHostMs = hostMs + WorldMapEncounterLockMs;
                if (map.Travelling) LandWorldMap(map.Cancel(), "random encounter");
                else EndWorldMap();
            }
            else if (SleepOpen) EndSleep();
        }
    }

    private SessionEncounterNotice? _lastEncounterNotice;
    private int _encountersFired;
    internal long WorldMapLockedUntilForSelfTest => _worldMapLockedUntilHostMs;
}
