using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Magic;

/// <summary>How a missile's leg starts (<see cref="SpellMissileRules.Leg"/>): it flies (<see cref="SpellMissileLeg.Flight"/>), it is
/// already on its destination, or its line is refused (more than 100 steps of 10 pixels: the original's goal ends there).</summary>
public enum SpellMissileLegKind { Flies, AtDestination, Refused }

/// <summary>One leg of a spell missile: its kind and, for a leg that flies, its line (the combat projectile's,
/// <see cref="ProjectileRules.Launch"/>).</summary>
public readonly record struct SpellMissileLeg(SpellMissileLegKind Kind, ProjectileFlight? Flight);

/// <summary>One tick of a missile's flight (<see cref="SpellMissileRules.Tick"/>): the flight after it, whether the leg's steps ran out
/// in it, and whether a step left the map (the original fails there).</summary>
public readonly record struct SpellMissileTick(ProjectileFlight Flight, bool LegEnded, bool OffMap);

/// <summary>What the missile's goal does when a leg's steps ran out (<see cref="SpellMissileRules.LegEnd"/>).</summary>
public enum SpellMissileLegEnd
{
    /// <summary>The missile is on its destination and its target object (if any) stands there: the release (0x429C80) runs.</summary>
    Arrive,
    /// <summary>The missile is on its destination but the target object moved: the destination becomes the target's tile and a new
    /// leg flies there from the missile's tile.</summary>
    Retarget,
    /// <summary>The missile is not on its destination: the same line is flown again from where it is.</summary>
    Replay,
}

/// <summary>
/// ARC-MAGIC-018: a spell's missile (goals 28 [ag_shoot_spell] and 78 [ag_attempt_move_straight_spell], read in the retail 1.0.7.4
/// executable, ledger SM rows). The casting goal's release frame launches it instead of releasing when the spell's projectile eye
/// candy (entry 6·spell + 1) has art (0x429C40): a projectile object at the caster's tile flies the combat projectile's line (10-pixel
/// steps between two tile centres, at most 100) in ticks of 35 ms, but tests nothing on the tiles it crosses. When a leg's steps run
/// out it waits one pace of its own art, then: on its destination with its target object there (or none) the release runs — the target
/// re-check and the BEGIN — and the missile is destroyed; a target that moved is followed on a new leg from the missile's tile; a
/// refused line ends the goal without a BEGIN. Pure functions: the host draws the missile, keeps its clock and releases the run.
/// </summary>
public static class SpellMissileRules
{
    /// <summary>The pace of a missile whose art's frame rate does not read (0x42F2D0): 100 ms.</summary>
    public const int DefaultPaceMs = 100;

    /// <summary>
    /// The leg from <paramref name="from"/> to <paramref name="destination"/> (the path node 0x426840, the combat projectile's line
    /// 0x4201C0): the missile is already there when the two tiles are the same (the goal tests that before it builds a line), else it
    /// flies the pixel line between the two tiles' centres, refused past 100 steps.
    /// </summary>
    [OriginalFunction(0x426840, OriginalFunctionScope.Partial)]
    public static SpellMissileLeg Leg(CellCoord from, CellCoord destination)
    {
        if (from == destination) return new(SpellMissileLegKind.AtDestination, null);
        return ProjectileRules.Launch(from, destination) is { } flight ? new(SpellMissileLegKind.Flies, flight) : new(SpellMissileLegKind.Refused, null);
    }

    /// <summary>The steps of one tick (0x42F390 with the speed 0x458CA0 reads, the projectile entry's <c>Proj Speed:</c>): the speed,
    /// or <see cref="ProjectileRules.DefaultStepsPerTick"/> when it is 0; a negative speed takes no step, so the missile never
    /// moves (no installed line has one).</summary>
    [OriginalFunction(0x458CA0, OriginalFunctionScope.Partial)]
    public static int StepsPerTick(int projectileSpeed) => projectileSpeed == 0 ? ProjectileRules.DefaultStepsPerTick : Math.Max(0, projectileSpeed);

    /// <summary>The pace the missile waits after a leg (the setup 0x42F2D0): 1000 / its art's frame rate, truncated, or
    /// <see cref="DefaultPaceMs"/> when the rate does not read. A rate of 0 divides by zero in the original; Steamwoven takes the
    /// default.</summary>
    [OriginalFunction(0x42F2D0, OriginalFunctionScope.Partial)]
    public static int PaceMs(int? framesPerSecond) => framesPerSecond is > 0 and var fps ? 1000 / fps : DefaultPaceMs;

    /// <summary>
    /// One tick of the flight (0x42F390): up to <paramref name="steps"/> steps of the line, each advancing the art frame and moving the
    /// missile to the tile under its point; the tick ends early when the leg's steps run out (the goal then pops to goal 28). Nothing
    /// is tested on the tiles crossed: no strike, no obstruction, no range. A point off the map fails the tick (the original stops
    /// with an error).
    /// </summary>
    [OriginalFunction(0x42F390, OriginalFunctionScope.Partial)]
    public static SpellMissileTick Tick(ProjectileFlight flight, int steps, Func<CellCoord, bool> contains)
    {
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(contains);
        for (var i = 0; i < steps && flight.Next < flight.Samples.Count; i++)
        {
            if (ProjectileRules.Step(flight, contains).Flight is not { } next) return new(flight, false, true);
            flight = next;
        }
        return new(flight, flight.Next >= flight.Samples.Count, false);
    }

    /// <summary>
    /// What goal 28 does after a leg (0x424D90, 0x42A180, 0x42BEC0): a missile on its <paramref name="destination"/> arrives when it
    /// has no target object (<paramref name="hasTarget"/> false) or the target stands on that tile, else it follows the target to
    /// <paramref name="targetTile"/>; a missile elsewhere flies its line again (<see cref="Replay"/>). A target whose tile is unknown
    /// (gone from the map) counts as arrived (Steamwoven: the original reads a freed object's location, not modelled); the release's
    /// re-check then decides.
    /// </summary>
    [OriginalFunction(0x42A180, OriginalFunctionScope.Partial)]
    public static SpellMissileLegEnd LegEnd(CellCoord missile, CellCoord destination, bool hasTarget, CellCoord? targetTile)
    {
        if (missile != destination) return SpellMissileLegEnd.Replay;
        return !hasTarget || targetTile is not { } tile || tile == missile ? SpellMissileLegEnd.Arrive : SpellMissileLegEnd.Retarget;
    }

    /// <summary>The same line flown again from where the missile is (goal 78 pushed again: its setup starts the steps at the first).</summary>
    public static ProjectileFlight Replay(ProjectileFlight flight)
    {
        ArgumentNullException.ThrowIfNull(flight);
        return flight with { Next = 0 };
    }
}

/// <summary>What the casting goal's facing turn did (<see cref="SpellFacingRules.Turn"/>).</summary>
public enum SpellFacingKind
{
    /// <summary>The caster is stoned, or a critter caster is stunned or paralysed: the goal ends (through its end node).</summary>
    Refused,
    /// <summary>No target object, or the caster is its own target: nothing turns.</summary>
    Unturned,
    /// <summary>The caster turns to <see cref="SpellFacing.Rotation"/>.</summary>
    Turned,
}

/// <summary>The facing turn's answer: its kind and, when the caster turns, the rotation 0..7 (0x4B8D50).</summary>
public readonly record struct SpellFacing(SpellFacingKind Kind, int Rotation = 0);

/// <summary>
/// ARC-MAGIC-018: the casting goal's facing turn (goal 26's node 0x42C850, after the start, the target node and the range, before goal
/// 27 plays the cast; ledger SM rows).
/// </summary>
public static class SpellFacingRules
{
    /// <summary>Spell flag OSF_STONED and critter flags OCF_STUNNED | OCF_PARALYZED, which refuse the turn.</summary>
    public const int Stoned = 0x10000, StunnedOrParalyzed = 0x60;

    /// <summary>
    /// 0x42C850: a caster whose spell flags hold <see cref="Stoned"/>, or a PC or NPC caster whose critter flags hold stunned or
    /// paralysed, refuses (the goal ends); without a target object (a tile cast) or with the caster as its target nothing turns; else
    /// the caster turns at once (no animation) to the rotation from its tile toward the target object's tile (0x4B8D50, 5 for the
    /// same tile). The host sets the rotation on the caster's current art when that art exists (0x502220, 0x43EB30).
    /// </summary>
    [OriginalFunction(0x42C850, OriginalFunctionScope.Partial)]
    public static SpellFacing Turn(int casterSpellFlags, bool casterIsCritter, int casterCritterFlags, CellCoord caster, bool targetIsCaster,
        CellCoord? targetObject)
    {
        if (targetObject is not { } target || targetIsCaster) return new(SpellFacingKind.Unturned);
        if ((casterSpellFlags & Stoned) != 0 || casterIsCritter && (casterCritterFlags & StunnedOrParalyzed) != 0) return new(SpellFacingKind.Refused);
        return new(SpellFacingKind.Turned, LineOfFireRules.Rotation(caster, target));
    }
}
