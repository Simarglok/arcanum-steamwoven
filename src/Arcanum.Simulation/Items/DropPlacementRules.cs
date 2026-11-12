using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Items;

/// <summary>What a drop draws (ARC-COMBAT-010): the distance first, then one direction per try.</summary>
public enum DropRoll { Distance, Direction }

/// <summary>
/// Who drops the item, as the drop's path and walk tell owners apart (ARC-COMBAT-010): an object (no critter test on the walk, no trap
/// avoided, 0x4BBFE0 answers 0), a PC or an NPC with a PC leader (avoids only a spotted trap), or an NPC without one (avoids every
/// trap unless the trap says otherwise).
/// </summary>
public enum DropOwner { Object, PcSide, Npc }

/// <summary>The drop's draws from the one generator: an inclusive uniform integer in <c>min..max</c> (0x4CEF00).</summary>
public interface IDropRolls
{
    int Roll(DropRoll purpose, int min, int max);
}

/// <summary>
/// The result of the original's path request with the drop's flags (0x41F3C0 with flags 0x10, ARC-COMBAT-010): the count it
/// returned (<see cref="Result"/>, 0 for no path, at most 8), the steps its direct attempt completed before it failed (the request's
/// field 0x24, <see cref="Partial"/>, 0 when the direct attempt reached the target or did not run) and the rotation buffer as the
/// request left it (<see cref="Rotations"/>, each 0..7; it may hold more entries than the walk reads).
/// </summary>
public sealed record DropPath(int Result, int Partial, IReadOnlyList<int> Rotations)
{
    public static DropPath None { get; } = new(0, 0, []);
}

/// <summary>
/// The map the drop's placement reads (the host's, from its map data; ARC-COMBAT-010): the original's step to a neighbouring tile
/// (0x4B8FF0), its path request with the drop's flags (0x41F3C0) and its test for a PC or NPC on a tile (0x4407C0 with the critter
/// mask 0x18000, the dead included).
/// </summary>
public interface IDropMap
{
    /// <summary>The tile one step from <paramref name="from"/> in rotation 0..7 (the movement's <see cref="NeighborDirection"/>
    /// order), or null when it leaves the map; rotation 8 is a step in place.</summary>
    CellCoord? Step(CellCoord from, int rotation);

    /// <summary>The owner's path from its tile to <paramref name="to"/> (0x41F3C0 with flags 0x10).</summary>
    DropPath Path(CellCoord from, CellCoord to, DropOwner owner);

    /// <summary>Whether a PC or an NPC, living or dead, stands on the tile.</summary>
    bool HasCritter(CellCoord cell);
}

/// <summary>
/// ARC-COMBAT-010: where a dropped item lands (the drop 0x461A90 and the placement 0x4F4E40, read in the retail 1.0.7.4 executable by
/// two readers, ledger "Drop cell"). Pure functions over an injected map and the generator.
/// </summary>
public static class DropPlacementRules
{
    public const int MinDistance = 2, MaxDistance = 4, Tries = 10, MaxDirection = 8;

    /// <summary>The drop's first draw: the distance 2..4 (0x461A90), made before the drop's own refusals.</summary>
    [OriginalFunction(0x461A90, OriginalFunctionScope.Partial)]
    public static int Distance(IDropRolls rolls)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        return Checked(rolls.Roll(DropRoll.Distance, MinDistance, MaxDistance), MinDistance, MaxDistance);
    }

    /// <summary>
    /// The tile the item lands on (0x461AB0's tries of 0x4F4E40): up to <see cref="Tries"/> tries, each drawing a direction 0..8 and
    /// failing only when a step leaves the map; when every try fails the owner's tile. One try: <paramref name="distance"/> steps
    /// in the direction from the owner give the target; the owner's path to it (<see cref="IDropMap.Path"/>) keeps the owner's tile
    /// when it returned nothing and the direct attempt completed no step, or when it returned <c>distance + 2</c> or more; otherwise
    /// the walk reads the direct attempt's completed steps when there are any, else the returned count, from the owner's tile, and a
    /// critter owner stops on the first tile with no PC or NPC on it (tested before each step), else it ends where the walk ends.
    /// </summary>
    [OriginalFunction(0x4F4E40, OriginalFunctionScope.Partial)]
    public static CellCoord Place(CellCoord owner, int distance, DropOwner who, IDropMap map, IDropRolls rolls)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rolls);
        if (distance is < 0 or > MaxDistance) throw new ArgumentOutOfRangeException(nameof(distance));
        if (!Enum.IsDefined(who)) throw new ArgumentOutOfRangeException(nameof(who));
        if (distance == 0) return owner;
        for (var attempt = 0; attempt < Tries; attempt++)
            if (Try(owner, distance, who, map, rolls) is { } landed) return landed;
        return owner;
    }

    /// <summary>
    /// ARC-MAGIC-038 (ledger TP3, 0x4524C0 case 1): the one call of 0x4F4E40 the spell Movement component's Random_Tile_In_Radius makes for the object's tile, with the direction already drawn by the caller
    /// (<paramref name="direction"/>, 0..8: the one draw of 0x4CEF00): <paramref name="radius"/> steps in it, the owner's path to that tile, the same refusals and the same walk as one try of <see cref="Place"/>.
    /// Null when a step leaves the map: the component then relocates nothing at all (the original's output stays zero); otherwise the tile to relocate to, the owner's own when nothing is to happen.
    /// </summary>
    public static CellCoord? PlaceOnce(CellCoord owner, int radius, DropOwner who, IDropMap map, int direction)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!Enum.IsDefined(who)) throw new ArgumentOutOfRangeException(nameof(who));
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        return Try(owner, radius, who, map, new FixedDirection(direction));
    }

    private sealed class FixedDirection(int direction) : IDropRolls
    {
        public int Roll(DropRoll purpose, int min, int max) => direction;
    }

    /// <summary>The create action's distance per try: 1..3 steps (0x447220 case 52, ledger PG1).</summary>
    public const int MinCreateDistance = 1, MaxCreateDistance = 3;

    /// <summary>
    /// ARC-WORLD-081: the tile a script's create object (action 52, 0x447220) puts the new object on next to <paramref name="near"/>'s tile
    /// (ledger PG1, PG2): up to <see cref="Tries"/> tries, each drawing a distance 1..3 and then the placement's direction, the placement
    /// failing only when a step leaves the map; after the last failure the object's own tile. The object it is created next to is no
    /// critter owner for the walk when it is an object (the plant generator's marker is scenery); next to a critter <paramref name="owner"/>
    /// names its side, and the walk stops on the first tile with no PC or NPC (PG2).
    /// </summary>
    public static CellCoord PlaceCreated(CellCoord near, IDropMap map, IDropRolls rolls, DropOwner owner = DropOwner.Object)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rolls);
        if (!Enum.IsDefined(owner)) throw new ArgumentOutOfRangeException(nameof(owner));
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var distance = Checked(rolls.Roll(DropRoll.Distance, MinCreateDistance, MaxCreateDistance), MinCreateDistance, MaxCreateDistance);
            if (Try(near, distance, owner, map, rolls) is { } landed) return landed;
        }
        return near;
    }

    /// <summary>The generator's distance per try: 1..5 steps (0x4BA910, ledger PO4).</summary>
    public const int MinGeneratedDistance = 1, MaxGeneratedDistance = 5;

    /// <summary>
    /// ARC-WORLD-1157 (E2-a, ledger PO4): the spot of one creature a monster generator makes around <paramref name="near"/>, the generator's own tile: up to <see cref="Tries"/> tries,
    /// each drawing a distance 1..5 and then the placement's direction (one call of the tile search for a critter owner without a PC leader, <see cref="DropOwner.Npc"/>), a try counting when the call
    /// did not leave the map and <paramref name="accepted"/> (the host's test: the spot has the generator's tile class and, with the generator's off-screen option, its drawn rectangle moved
    /// to the spot is off the view) says so; after the last failure the generator's own tile. A generator of cap 1 makes its creature on its own tile without a draw (the caller's rule).
    /// </summary>
    /// <remarks>Executable: the generator's placement 0x4BA910 over 0x4F4E40.</remarks>
    public static CellCoord PlaceGenerated(CellCoord near, IDropMap map, IDropRolls rolls, Func<CellCoord, bool> accepted)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(accepted);
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var distance = Checked(rolls.Roll(DropRoll.Distance, MinGeneratedDistance, MaxGeneratedDistance), MinGeneratedDistance, MaxGeneratedDistance);
            if (Try(near, distance, DropOwner.Npc, map, rolls) is { } spot && accepted(spot)) return spot;
        }
        return near;
    }

    // One call of 0x4F4E40: null when a step leaves the map (the try failed).
    private static CellCoord? Try(CellCoord owner, int distance, DropOwner who, IDropMap map, IDropRolls rolls)
    {
        var direction = Checked(rolls.Roll(DropRoll.Direction, 0, MaxDirection), 0, MaxDirection);
        var target = owner;
        for (var step = 0; step < distance; step++)
        {
            if (map.Step(target, direction) is not { } next) return null;
            target = next;
        }
        var path = map.Path(owner, target, who) ?? throw new InvalidOperationException("DROP_PATH_NULL");
        if (path.Result <= 0 && path.Partial <= 0) return owner;
        if (path.Result >= distance + 2) return owner;
        var count = path.Partial != 0 ? path.Partial : path.Result;
        var cell = owner;
        for (var i = 0; i < count; i++)
        {
            if (who != DropOwner.Object && !map.HasCritter(cell)) return cell;
            if (i >= path.Rotations.Count) throw new InvalidOperationException("DROP_PATH_SHORT");
            if (map.Step(cell, Checked(path.Rotations[i], 0, 7)) is not { } next) return null;
            cell = next;
        }
        return cell;
    }

    private static int Checked(int value, int min, int max) =>
        value >= min && value <= max ? value : throw new InvalidOperationException($"DROP_VALUE_OUT_OF_RANGE {value}");
}
