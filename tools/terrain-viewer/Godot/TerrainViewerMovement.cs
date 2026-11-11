using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-036 (MA-5, MV1–MV3): the spell Movement component's type 0 (the run's target tile, 0x4524C0) is the original's immediate path relocation (0x455350). The cast host logs the request
// (SpellCastEventKind.Movement); this host asks the path (the drop's request, AreaDropMapNow), walks at most the eight tiles it returns (ImmediatePathWalk), interrupts the walk and places the critter on the last tile.
// The hero: the grid actor is put on the last tile at once (as a knockback's push is), the session learns the place, the tile script of every tile walked runs (a tile-script-only entry, no jump point), and
// the last tile's own entry (jump point, tile script, sector script) follows as for any move; the camera follows the hero (the original centres the view on the local player). An NPC or a companion: the AI host's
// RelocateAlongPath. Other objects are not ready.
public partial class TerrainViewerApp
{
    private readonly HashSet<SpellCastEvent> _movementTaken = new(ReferenceEqualityComparer.Instance);
    // The relocations the host made or refused, for the smoke: the critter, where it stood, the tiles walked and the text the host gave.
    private readonly List<(string Target, CellCoord From, int Tiles, string Result)> _movementsForSelfTest = [];

    /// <summary>A cast host log entry asking for a relocation along the path (a Movement component of type 0): the critter goes toward the entry's tile (x in the value, y in the detail).</summary>
    private void TakeMovementEvent(SpellCastEvent entry)
    {
        if (entry.Kind != SpellCastEventKind.Movement || entry.Object is not { } target || _areaSession is null) return;
        if (_movementTaken.Count >= 256) _movementTaken.Clear();
        if (!_movementTaken.Add(entry)) return;
        var destination = new CellCoord(entry.Value, entry.Detail);
        var (from, tiles, result) = target == SessionSpellCaster.Hero ? RelocateHeroAlongPath(destination) : RelocateNpcAlongPath(target, destination);
        _movementsForSelfTest.Add((target, from, tiles, result));
        if (_movementsForSelfTest.Count > 16) _movementsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-MAGIC-036 movement run={entry.Run} target={target} from={from.X},{from.Y} destination={destination.X},{destination.Y} tiles={tiles} result={result}");
    }

    private (CellCoord From, int Tiles, string Result) RelocateNpcAlongPath(string npc, CellCoord destination)
    {
        if (AiFrameHost() is not { } host || AreaDropMapNow() is not { } map) return (default, 0, "no AI host or map");
        var from = host.Tile(npc) ?? default;
        var (tiles, result) = host.RelocateAlongPath(npc, destination, map);
        return (from, tiles, result);
    }

    /// <summary>The hero's relocation: the path from its committed tile, the walk interrupted, the grid actor on the last tile, the session's place and the tile scripts queued. Nothing without a route or while a change of place waits.</summary>
    private (CellCoord From, int Tiles, string Result) RelocateHeroAlongPath(CellCoord destination)
    {
        if (_worldActor.Session is not AreaGridSession grid || AreaDropMapNow() is not { } map) return (default, 0, "no hero grid or map");
        var left = grid.Chart.ToWorld(grid.State.CommittedCell);
        var from = new CellCoord(checked((int)left.X), checked((int)left.Y));
        if (AreaTransferPending) return (from, 0, "a change of place waits");
        var tiles = ImmediatePathWalk.Tiles(map, from, destination, DropOwner.PcSide);
        if (tiles.Count == 0) return (from, 0, "no route");
        var last = tiles[^1];
        var arrival = new AreaStepCell(last.X, last.Y);
        var chart = grid.Chart.ToChart(arrival);
        if (!grid.Chart.Contains(chart)) return (from, tiles.Count, $"arrival {last.X},{last.Y} not on the shown chart");
        // The level-5 interrupt of the hero's goals (0x424070): its walk ends and stops the route; the spell's own cast run is not the walk's.
        HeroRunsHost()?.InterruptForHold();
        if (!_worldActor.Relocate(chart)) return (from, tiles.Count, $"arrival {last.X},{last.Y} refused by the grid");
        WriteHeroLocation(grid, chart);
        // 0x455350 runs the tile script (0x4C0980) of every tile it steps onto, the last included; the relocation 0x43E770 then notifies the last tile (jump point, tile script, sector script).
        if (_mapTransfers is not null)
        {
            var previous = left;
            foreach (var tile in tiles)
            {
                var cell = new AreaStepCell(tile.X, tile.Y);
                _mapEntered.Enqueue(new(previous, cell, TileScriptOnly: true));
                previous = cell;
            }
            _mapEntered.Enqueue(new(left, arrival));
        }
        return (from, tiles.Count, $"{from.X},{from.Y} -> {last.X},{last.Y}");
    }
}
