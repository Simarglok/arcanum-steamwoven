using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.Simulation.Combat;

/// <summary>What the host says of the step the pushed object makes (the block test 0x42FD70, GT61): clear, or blocked by the terrain (a blocked tile, a wall, a closed portal, a blocking object) or by a living critter on the tile.</summary>
public enum KnockbackBlock { None, Terrain, Critter }

/// <summary>How a tick of the slide ended (<see cref="KnockbackRules.Tick"/>): the slide goes on, its line ran out, a step was blocked, or the point left the map.</summary>
public enum KnockbackTickKind { Continue, Exhausted, Blocked, OffMap }

/// <summary>
/// One tick of a knockback: the slide as it stands (the pushed object stays on <see cref="KnockbackSlide.Flight"/>'s tile), the tiles it entered in order, how the tick ended and, for a block, the line steps left
/// (<see cref="StepsLeft"/>, the collision damage's size).
/// </summary>
public readonly record struct KnockbackTick(ProjectileFlight Flight, KnockbackTickKind Kind, IReadOnlyList<CellCoord> Entered, CellCoord? BlockedBy, int StepsLeft);

/// <summary>
/// ARC-DEV-141 (MIG13 S8c, docs/architecture/goal-table.md §3.5, the S8 read GT59–GT62): the original's knockback as pure rules. The magic-tech AGoal component 43 starts it on the target (0x434E80): the direction is
/// the rotation from the source tile to the target's tile (0x4B8D50), on the same tile the target's own rotation turned half a circle (it is pushed backward), and the destination is four neighbour steps from the target's
/// tile (0x4B8FF0, a step off the map refuses the start). The slide (goal 64's step 0x42FA50) walks the 10-pixel line from the target's tile centre, four steps every 35 ms; each tile change asks the block test, and a block
/// leaves the object on the tile it stood on.
/// </summary>
public static class KnockbackRules
{
    /// <summary>The tiles the push moves (GT59: fixed, not data).</summary>
    public const int Distance = 4;
    /// <summary>The line steps a tick takes (GT61: fixed) and the tick's length in milliseconds (the global 0x5DE6CC every tick sets, 35).</summary>
    public const int StepsPerTick = 4, TickMs = ProjectileRules.TickMs;

    /// <summary>
    /// The destination of a knockback (0x434E80): <see cref="Distance"/> neighbour steps from <paramref name="target"/> in the direction away from <paramref name="source"/>; on the same tile the target's own
    /// rotation <paramref name="targetRotation"/> plus 4 (mod 8). Null when a step leaves the map (<paramref name="contains"/>): the starter refuses.
    /// </summary>
    public static CellCoord? Destination(CellCoord source, CellCoord target, int targetRotation, Func<CellCoord, bool> contains)
    {
        ArgumentNullException.ThrowIfNull(contains);
        var rotation = source == target ? ((targetRotation % 8) + 8 + 4) % 8 : LineOfFireRules.Rotation(source, target);
        var at = target;
        for (var i = 0; i < Distance; i++)
        {
            at = OriginalPathRequest.Neighbor(at, rotation);
            if (!contains(at)) return null;
        }
        return at;
    }

    /// <summary>The 10-pixel line from <paramref name="from"/> to <paramref name="destination"/> (the path node 0x4268F0 through 0x4201C0); null when the line is refused (more than 100 steps) or both are one tile.</summary>
    public static ProjectileFlight? Line(CellCoord from, CellCoord destination) => ProjectileRules.Launch(from, destination);

    /// <summary>The line's size the collision damage reads: the steps of the line not yet taken, the blocked one counted (the step's index is not advanced when the block happens, GT61).</summary>
    public static int StepsLeft(ProjectileFlight flight)
    {
        ArgumentNullException.ThrowIfNull(flight);
        return Math.Max(flight.Samples.Count - flight.Next, 0);
    }

    /// <summary>The line's fixed rotation 0..7 (the rotation from the pushed object's tile to the destination, stored by the path node 0x4268F0): the block test looks one step from the tile left in it.</summary>
    public static int LineRotation(CellCoord from, CellCoord destination) => LineOfFireRules.Rotation(from, destination);

    /// <summary>
    /// One tick of the slide (the step 0x42FA50): up to <see cref="StepsPerTick"/> steps of the line. Each step moves the point by one sample; a tile change asks <paramref name="block"/> with the tile left and the
    /// tile one step from it in the line's rotation <paramref name="rotation"/> (what the test 0x42FD70 looks at, GT61; at the map's edge nothing is tested), and when it answers a block the object stays on the tile it left.
    /// The slide ends in the very step that takes the line's last sample (<see cref="KnockbackTickKind.Exhausted"/>); a point off the map ends it too.
    /// </summary>
    public static KnockbackTick Tick(ProjectileFlight flight, Func<CellCoord, bool> contains, int rotation, Func<CellCoord, CellCoord, KnockbackBlock> block)
    {
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(contains);
        ArgumentNullException.ThrowIfNull(block);
        var entered = new List<CellCoord>();
        for (var i = 0; i < StepsPerTick; i++)
        {
            if (flight.Next >= flight.Samples.Count) return new(flight, KnockbackTickKind.Exhausted, entered, null, 0);
            var (stepped, crossing) = ProjectileRules.Step(flight, contains);
            if (stepped is null) return new(flight, KnockbackTickKind.OffMap, entered, null, 0);
            if (crossing is { } crossed)
            {
                var tested = OriginalPathRequest.Neighbor(crossed.Left, rotation);
                if (contains(tested) && block(crossed.Left, tested) != KnockbackBlock.None)
                    return new(flight, KnockbackTickKind.Blocked, entered, tested, StepsLeft(flight));
                entered.Add(crossed.Entered);
            }
            flight = stepped;
            if (flight.Next >= flight.Samples.Count) return new(flight, KnockbackTickKind.Exhausted, entered, null, 0);
        }
        return new(flight, KnockbackTickKind.Continue, entered, null, 0);
    }

    /// <summary>The largest roll of the collision damage's two amounts (normal and fatigue, each 1 to this): the line steps left, the blocked one counted (GT61: the original rolls 1 to half the line's bytes, two per step); at least 1.</summary>
    public static int BumpMaximum(int stepsLeft) => Math.Max(stepsLeft, 1);
}
