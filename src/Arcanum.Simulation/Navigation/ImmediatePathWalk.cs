using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

/// <summary>
/// ARC-MAGIC-036 (magic audit MA-5, ledger MV1–MV3): the tiles of the original's immediate path relocation (0x455350), the body of the spell Movement component's types 0 and 1 (0x4524C0). The
/// original asks the one path request (0x41F3C0, with the drop's flags 0x10: <see cref="IDropMap.Path"/>) for a route from the object's tile to the target tile and **takes only its returned count**
/// (at most 8: the request's own cap), ignoring the direct attempt's completed steps; with no route, and with a target equal to the tile it stands on, nothing happens at all. Otherwise it steps
/// the first <c>count</c> rotations of the buffer from the object's tile (0x4B8FF0; a step off the map ends the walk there), runs the tile script of every tile it steps onto (0x4C0980, the last
/// one included), and then relocates the object to the last tile, which runs the same tile's script, its jump point for a player character and the sector's script once more (the move 0x43E770).
/// So a target farther than the cap leaves the object on the eighth tile of the route, not on the target.
/// </summary>
public static class ImmediatePathWalk
{
    /// <summary>
    /// The tiles the walk steps onto, in order (the last is where the object is placed); empty when nothing happens (the path request found no route, or the first step already left the map).
    /// </summary>
    [OriginalFunction(0x455350, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<CellCoord> Tiles(IDropMap map, CellCoord from, CellCoord to, DropOwner who)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!Enum.IsDefined(who)) throw new ArgumentOutOfRangeException(nameof(who));
        var path = map.Path(from, to, who) ?? throw new InvalidOperationException("WALK_PATH_NULL");
        if (path.Result <= 0) return [];
        var tiles = new List<CellCoord>(path.Result);
        var cell = from;
        for (var i = 0; i < path.Result; i++)
        {
            if (i >= path.Rotations.Count) throw new InvalidOperationException("WALK_PATH_SHORT");
            var rotation = path.Rotations[i];
            if (rotation is < 0 or > 7) throw new InvalidOperationException($"WALK_ROTATION_OUT_OF_RANGE {rotation}");
            if (map.Step(cell, rotation) is not { } next) break;
            tiles.Add(next);
            cell = next;
        }
        return tiles;
    }
}
