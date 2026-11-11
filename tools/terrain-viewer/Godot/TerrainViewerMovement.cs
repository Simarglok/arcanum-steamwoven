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
// The hero: the tile script of every tile walked runs first (a tile-script-only entry, no jump point; ARC-MAGIC-038 moved it before the placement, as in the original), then the grid actor is put on the last tile at
// once (as a knockback's push is), the session learns the place, and the last tile's own entry (jump point, tile script, sector script) follows as for any move; the camera follows the hero (the original centres the
// view on the local player). An NPC or a companion: the AI host's RelocateAlongPath (its scripts before the placement through BeforeRelocationScripts). Other objects are not ready.
public partial class TerrainViewerApp
{
    private readonly HashSet<SpellCastEvent> _movementTaken = new(ReferenceEqualityComparer.Instance);
    // The relocations the host made or refused, for the smoke: the critter, where it stood, the tiles walked and the text the host gave.
    private readonly List<(string Target, CellCoord From, int Tiles, string Result)> _movementsForSelfTest = [];

    /// <summary>A cast host log entry asking for a relocation along the path (a Movement component of type 0): the critter goes toward the entry's tile (x in the value, y in the detail).</summary>
    private void TakeMovementEvent(SpellCastEvent entry)
    {
        if (entry.Kind is not (SpellCastEventKind.Movement or SpellCastEventKind.MovementRandom) || entry.Object is not { } target || _areaSession is null) return;
        if (_movementTaken.Count >= 256) _movementTaken.Clear();
        if (!_movementTaken.Add(entry)) return;
        // ARC-MAGIC-038: a random tile (type 1) is the placement 0x4F4E40's result, relocated to by the same walk.
        if (entry.Kind == SpellCastEventKind.MovementRandom) { TakeRandomMovement(entry, target); return; }
        var destination = new CellCoord(entry.Value, entry.Detail);
        var (from, tiles, result) = target == SessionSpellCaster.Hero ? RelocateHeroAlongPath(destination) : RelocateNpcAlongPath(target, destination);
        _movementsForSelfTest.Add((target, from, tiles, result));
        if (_movementsForSelfTest.Count > 16) _movementsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-MAGIC-036 movement run={entry.Run} target={target} from={from.X},{from.Y} destination={destination.X},{destination.Y} tiles={tiles} result={result}");
    }

    private void TakeRandomMovement(SpellCastEvent entry, string target)
    {
        var radius = entry.Value; var direction = entry.Detail;
        (CellCoord From, int Tiles, string Result) done;
        if (AreaDropMapNow() is not { } map) done = (default, 0, "no map");
        else if (target == SessionSpellCaster.Hero)
        {
            if (_worldActor.Session is not AreaGridSession grid) done = (default, 0, "no hero grid");
            else
            {
                var left = grid.Chart.ToWorld(grid.State.CommittedCell);
                var from = new CellCoord(checked((int)left.X), checked((int)left.Y));
                done = DropPlacementRules.PlaceOnce(from, radius, DropOwner.PcSide, map, direction) switch
                {
                    null => (from, 0, "a step of the radius walk leaves the map"),
                    { } to when to == from => (from, 0, "the placement kept the tile"),
                    { } to => RelocateHeroAlongPath(to),
                };
            }
        }
        else if (AiFrameHost() is { } host)
        {
            var from = host.Tile(target) ?? default;
            host.BeforeRelocationScripts = BeforeNpcRelocationScripts;
            var (tiles, result) = host.RelocateToRandomTile(target, radius, direction, map);
            done = (from, tiles, result);
        }
        else done = (default, 0, "no AI host");
        _movementsForSelfTest.Add((target, done.From, done.Tiles, done.Result));
        if (_movementsForSelfTest.Count > 16) _movementsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-MAGIC-038 random-movement run={entry.Run} target={target} radius={radius} direction={direction} from={done.From.X},{done.From.Y} tiles={done.Tiles} result={done.Result}");
    }

    // 0x455350: the tile script of every tile walked runs before the NPC is interrupted and placed.
    private void BeforeNpcRelocationScripts(string entity, IReadOnlyList<CellCoord> walked)
    {
        var homeMap = CompanionHomeMap(entity);
        foreach (var tile in walked)
        {
            if (_npcTilePending.Count >= 256) _npcTilePending.Dequeue();
            _npcTilePending.Enqueue((entity, homeMap, new(tile.X, tile.Y)));
        }
        RunNpcTilePending();
    }

    private (CellCoord From, int Tiles, string Result) RelocateNpcAlongPath(string npc, CellCoord destination)
    {
        if (AiFrameHost() is not { } host || AreaDropMapNow() is not { } map) return (default, 0, "no AI host or map");
        var from = host.Tile(npc) ?? default;
        // 0x455350: the tile script of every tile walked runs before the NPC is interrupted and placed.
        host.BeforeRelocationScripts = BeforeNpcRelocationScripts;
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
        // 0x455350 runs the tile script (0x4C0980) of every tile it steps onto, the last included, **before** it moves the object: here too, with the hero still on its old tile and its old place in the session.
        // ARC-MAGIC-038 (ledger TP5): the scripts run at once through the map-transfer step, not on a later frame after the placement.
        if (_mapTransfers is not null)
        {
            var previous = left;
            foreach (var tile in tiles)
            {
                var cell = new AreaStepCell(tile.X, tile.Y);
                _mapEntered.Enqueue(new(previous, cell, TileScriptOnly: true));
                previous = cell;
            }
            RunMapEnteredSteps();
            // A script of the walk may have asked for a change of place: the hero stays for it, and the arrival below would only pile a second notice on it.
            if (AreaTransferPending) return (from, tiles.Count, "a tile script asked for a change of place");
        }
        // The level-5 interrupt of the hero's goals (0x424070): its walk ends and stops the route; the spell's own cast run is not the walk's.
        HeroRunsHost()?.InterruptForHold();
        if (!_worldActor.Relocate(chart)) return (from, tiles.Count, $"arrival {last.X},{last.Y} refused by the grid");
        WriteHeroLocation(grid, chart);
        // The relocation 0x43E770 then notifies the last tile (jump point, tile script, sector script), as for any move.
        if (_mapTransfers is not null) _mapEntered.Enqueue(new(left, arrival));
        return (from, tiles.Count, $"{from.X},{from.Y} -> {last.X},{last.Y}");
    }
}
