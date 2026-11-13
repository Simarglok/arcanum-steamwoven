using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Ai;

/// <summary>How loud a heard thing is (the index into the noise radii 2/8/15 at 0x5B50C0, ledger AW5 and AW9).</summary>
public enum Loudness
{
    /// <summary>Silent: a silent weapon, a concealed critter in the target scan, the dynamite notice, the attack set-up.</summary>
    Silent = 0,
    /// <summary>Normal: script condition 29, most attack events.</summary>
    Normal = 1,
    /// <summary>Loud: a loud weapon, damage and magick noise to guards.</summary>
    Loud = 2,
}

/// <summary>Why a sight or hearing check came out as it did; the early refusals give <see cref="AwarenessRules.NotPerceived"/>.</summary>
public enum AwarenessReason
{
    /// <summary>Seen or heard (excess 0).</summary>
    Perceived,
    /// <summary>The observer cannot perceive: stunned, unconscious and, for sight, blinded or sleeping.</summary>
    State,
    /// <summary>Sight only: the target is invisible and the observer neither has extraordinary Perception nor detects invisibility.</summary>
    Invisible,
    /// <summary>Sight only: the target is in one of the three directions behind the observer.</summary>
    Facing,
    /// <summary>More than <see cref="AwarenessRules.MaxDistance"/> tiles apart.</summary>
    Distance,
    /// <summary>Out of range by the excess (for hearing after the sound cost); nothing blocks the line.</summary>
    Range,
    /// <summary>Sight only: something stops the line of fire; the excess is one more than the range alone gives.</summary>
    Blocked,
}

/// <summary>A sight or hearing result (0x4AF260, 0x4AF470): the excess distance, 0 when perceived, and its reason.</summary>
public readonly record struct AwarenessResult(int Excess, AwarenessReason Reason)
{
    /// <summary>Every caller treats an excess of 0 as perceived (AW12).</summary>
    public bool Perceived => Excess == 0;
}

/// <summary>
/// The observer and target facts a sight check reads (0x4AF260, ledger AW2–AW4): the observer's critter flags, effective
/// Perception (stat 5) and current art rotation, the target's invisibility, and the prowling margin of a concealed target. The
/// margin comes from the shared skill check (ARC-SKILLS-003, effectiveness and difficulty with the prowler as source and the
/// observer as target, flag 0x4000) through <see cref="StealthRules.Margin"/>; 0 when the target is not concealed.
/// </summary>
public sealed record SightFacts
{
    /// <summary>The observer's effective Perception, 0..<see cref="AwarenessRules.MaxPerception"/>.</summary>
    public required int Perception { get; init; }
    /// <summary>The rotation 0..7 of the observer's current art.</summary>
    public required int Rotation { get; init; }
    /// <summary>Critter flag 0x20.</summary>
    public bool Stunned { get; init; }
    /// <summary>Critter flag 0x80.</summary>
    public bool Blinded { get; init; }
    /// <summary>Critter flag 0x1000.</summary>
    public bool Sleeping { get; init; }
    /// <summary>The unconscious test 0x45D800: true for a non-critter, false for an undead.</summary>
    public bool Unconscious { get; init; }
    /// <summary>The observer's Perception is extraordinary (0x4B0EE0).</summary>
    public bool ExtraordinaryPerception { get; init; }
    /// <summary>The observer's spell flag 0x200 (detecting invisible).</summary>
    public bool DetectsInvisible { get; init; }
    /// <summary>The target's object flag 0x200 or spell flag 0x1.</summary>
    public bool TargetInvisible { get; init; }
    /// <summary>The prowling margin 0..100 of a concealed target over this observer (critter flag 0x1, 0x45EDE0); 0 otherwise.</summary>
    public int ProwlingMargin { get; init; }
}

/// <summary>
/// The observer and target facts a hearing check reads (0x4AF470, ledger AW5): the observer's critter flags and effective
/// Perception, and the prowling margin of a target moving silently (critter flag 0x2, which concealment sets with 0x1). The margin
/// comes from the shared skill check with flag 0x2000 through <see cref="StealthRules.Margin"/>; 0 otherwise. Blindness, facing
/// and invisibility do not matter.
/// </summary>
public sealed record HearingFacts
{
    /// <summary>The observer's effective Perception, 0..<see cref="AwarenessRules.MaxPerception"/>.</summary>
    public required int Perception { get; init; }
    /// <summary>Critter flag 0x20.</summary>
    public bool Stunned { get; init; }
    /// <summary>Critter flag 0x1000: halves Perception.</summary>
    public bool Sleeping { get; init; }
    /// <summary>The unconscious test 0x45D800.</summary>
    public bool Unconscious { get; init; }
    /// <summary>The prowling margin 0..100 of a target moving silently over this observer; 0 otherwise.</summary>
    public int ProwlingMargin { get; init; }
}

/// <summary>The map a sight or hearing check walks: the line-of-fire map, plus whether a tile is soundproof (0x4D7180).</summary>
public interface IAwarenessMap<TObject> : ILineOfFireMap<TObject> where TObject : class
{
    /// <summary>Whether the tile is soundproof (a step into it costs a sound line <see cref="AwarenessRules.SoundproofCost"/>).</summary>
    bool Soundproof(CellCoord tile);
}

/// <summary>
/// ARC-WORLD-078, slice I2 of the NPC combat AI plan: whether a critter sees or hears another, as the retail 1.0.7.4 executable
/// computes it (stage A2, ledger AW1–AW6, AW13, AW17, AW18; read by two readers). Pure functions over explicit facts and an
/// injected map, in the world cells of <see cref="CellCoord"/>; the line of fire is <see cref="LineOfFireRules.Trace{TObject}"/>.
/// The prowling margin is an input (the shared skill check owns it); the callers (target scan, witnesses, guards, script
/// conditions 28 and 29) are later slices'.
/// </summary>
public static class AwarenessRules
{
    /// <summary>The result of an early refusal and the largest distance tested (AW2, AW5).</summary>
    public const int NotPerceived = 1000, MaxDistance = 1000;
    /// <summary>The perception distance's bounds (0x4AF240).</summary>
    public const int MinPerceptionDistance = 1, MaxPerceptionDistance = 10;
    /// <summary>The largest effective Perception the checks accept (a stat is far below it; it keeps the arithmetic in range).</summary>
    public const int MaxPerception = 1000;
    /// <summary>The Perception from which a critter sees the invisible (0x4B0EE0, ARC-AI-1519).</summary>
    public const int ExtraordinaryPerceptionMin = 20;
    /// <summary>What hearing takes off Perception before the distance (AW1).</summary>
    public const int HearingPerceptionOffset = 4;
    /// <summary>A sound line's step costs (0x43FE00 with flag 0x20, AW6): a wall on the crossed edge, a closed unbroken door or
    /// window, a soundproof tile entered.</summary>
    public const int WallSoundCost = 2, ClosedPortalSoundCost = 1, SoundproofCost = 8;

    // The noise radii 2/8/15 (0x5B50C0); hearing adds each one's excess over the silent radius.
    private static readonly int[] NoiseRadii = [2, 8, 15];

    /// <summary>0x4AF240: a Perception value as a distance, <c>clamp(x, 1, 10)</c>.</summary>
    [OriginalFunction(0x4AF240, OriginalFunctionScope.Full)]
    public static int PerceptionDistance(int perception) => Math.Clamp(perception, MinPerceptionDistance, MaxPerceptionDistance);

    /// <summary>The noise radius of a loudness (0x5B50C0): 2, 8 or 15 tiles, the half-size of an attack's alert squares (AW9).</summary>
    public static int NoiseRadius(Loudness loudness) => NoiseRadii[Index(loudness)];

    /// <summary>
    /// 0x45EFF0: whether an observer whose current art has <paramref name="observerRotation"/> faces a target, by the direction
    /// from its tile to the target's (<see cref="LineOfFireRules.Rotation"/>, 5 for the same tile): <c>k = (direction − rotation
    /// + 8) mod 8</c>; the three directions behind, k = 3, 4 or 5, are not faced (AW3).
    /// </summary>
    [OriginalFunction(0x45EFF0, OriginalFunctionScope.Full)]
    public static bool Faces(CellCoord observer, int observerRotation, CellCoord target)
    {
        CheckRotation(observerRotation);
        var k = (LineOfFireRules.Rotation(observer, target) - observerRotation + 8) % 8;
        return k is not (3 or 4 or 5);
    }

    /// <summary>
    /// 0x4AF260: the excess distance by which the observer does not see the target (0 = seen). 1000 for a stunned, blinded, sleeping
    /// or unconscious observer, an invisible target the observer cannot detect, a target behind it (<see cref="Faces"/>) or more than
    /// 1000 tiles away; otherwise <c>max(0, d − PerceptionDistance(P′))</c> with <c>P′</c> the Perception after the prowling margin
    /// and <c>d</c> the Chebyshev distance, plus 1 when the line of fire from the observer (with its pixel offset) to the target's
    /// tile reports a blocker; the line's cover is discarded.
    /// </summary>
    [OriginalFunction(0x4AF260, OriginalFunctionScope.Partial)]
    public static AwarenessResult See<TObject>(SightFacts facts, CellCoord observer, int offsetX, int offsetY, CellCoord target,
        ILineOfFireMap<TObject> map, CombatAnomalyProfile profile) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        CheckPerception(facts.Perception);
        CheckMargin(facts.ProwlingMargin);
        CheckRotation(facts.Rotation);
        if (facts.Stunned || facts.Blinded || facts.Sleeping || facts.Unconscious) return new(NotPerceived, AwarenessReason.State);
        if (facts.TargetInvisible && !facts.ExtraordinaryPerception && !facts.DetectsInvisible) return new(NotPerceived, AwarenessReason.Invisible);
        if (!Faces(observer, facts.Rotation, target)) return new(NotPerceived, AwarenessReason.Facing);
        var range = PerceptionDistance(StealthRules.PerceptionAfterMargin(facts.Perception, facts.ProwlingMargin));
        var distance = Distance(observer, target);
        if (distance > MaxDistance) return new(NotPerceived, AwarenessReason.Distance);
        var excess = (int)Math.Max(0, distance - range);
        if (LineOfFireRules.Trace(observer, offsetX, offsetY, target, map, profile).Blocker is not null) return new(excess + 1, AwarenessReason.Blocked);
        return excess == 0 ? new(0, AwarenessReason.Perceived) : new(excess, AwarenessReason.Range);
    }

    /// <summary>
    /// 0x4AF470: the excess distance by which the observer does not hear the target at <paramref name="loudness"/> (0 = heard). 1000
    /// for a stunned or unconscious observer or more than 1000 tiles; otherwise Perception is halved for a sleeping observer, lowered
    /// by the prowling margin, and the range is <c>H = (PerceptionDistance(P′ − 4) + radius − 2) / 2 − SoundCost</c> with the noise
    /// radius 2, 8 or 15; heard when <c>d ≤ H</c>, else the excess <c>d − H</c>.
    /// </summary>
    [OriginalFunction(0x4AF470, OriginalFunctionScope.Partial)]
    public static AwarenessResult Hear<TObject>(HearingFacts facts, CellCoord observer, int offsetX, int offsetY, CellCoord target,
        Loudness loudness, IAwarenessMap<TObject> map, CombatAnomalyProfile profile) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        CheckPerception(facts.Perception);
        CheckMargin(facts.ProwlingMargin);
        var radius = NoiseRadius(loudness);
        if (facts.Stunned || facts.Unconscious) return new(NotPerceived, AwarenessReason.State);
        var distance = Distance(observer, target);
        if (distance > MaxDistance) return new(NotPerceived, AwarenessReason.Distance);
        var perception = facts.Sleeping ? facts.Perception / 2 : facts.Perception;
        perception = StealthRules.PerceptionAfterMargin(perception, facts.ProwlingMargin);
        var range = (PerceptionDistance(perception - HearingPerceptionOffset) + radius - NoiseRadii[0]) / 2
            - SoundCost(observer, offsetX, offsetY, target, map, profile);
        return distance > range ? new((int)(distance - range), AwarenessReason.Range) : new(0, AwarenessReason.Perceived);
    }

    /// <summary>
    /// 0x4AF640: what the walls, doors and soundproof tiles between the two tiles take off a hearing range. The same tile costs 0; a
    /// line that cannot be built costs <see cref="LineOfFireRules.FailedLineCost"/> (AW17); otherwise the sum of
    /// <see cref="SoundStepCost{TObject}"/> over the steps of the line walk from the observer (with its pixel offset, AW18) to the
    /// target's tile. Nothing blocks a sound line and objects cost nothing.
    /// </summary>
    [OriginalFunction(0x4AF640, OriginalFunctionScope.Partial)]
    public static int SoundCost<TObject>(CellCoord from, int offsetX, int offsetY, CellCoord to, IAwarenessMap<TObject> map,
        CombatAnomalyProfile profile) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        if (LineOfFireRules.Walk(from, offsetX, offsetY, to, map.Contains) is not { } steps) return LineOfFireRules.FailedLineCost;
        var cost = 0;
        foreach (var step in steps) cost += SoundStepCost(step.From, step.Rotation, map, profile);
        return cost;
    }

    /// <summary>
    /// The step cost of one tile step with the sound flag 0x20 (0x43FE00, AW6): an edge step (odd rotation) costs
    /// <see cref="WallSoundCost"/> for each wall piece of any kind and <see cref="ClosedPortalSoundCost"/> for each closed, unbroken
    /// door or window on the source tile's crossed edge, then 1 in all when the neighbour is off the map, else
    /// <see cref="SoundproofCost"/> when the neighbour is soundproof and the same wall and portal costs on its opposite edge. A corner
    /// step (even rotation) sums four edge steps around the corner as the line of fire does (a side tile off the map: 1), so each side
    /// tile and crossed edge counts once; <see cref="CombatAnomalyProfile.CornerStepCountsBothRoutes"/> keeps the original's second
    /// count of the corner tile's soundproofing (AW13), otherwise it counts once.
    /// </summary>
    public static int SoundStepCost<TObject>(CellCoord from, int rotation, IAwarenessMap<TObject> map, CombatAnomalyProfile profile)
        where TObject : class
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        CheckRotation(rotation);
        if ((rotation & 1) == 1) return SoundEdgeStep(from, rotation, true, map);
        int ccw = (rotation + 7) % 8, cw = rotation + 1;
        var total = SoundEdgeStep(from, ccw, true, map);
        if (map.Step(from, ccw) is not { } first) return 1;
        total += SoundEdgeStep(first, cw, true, map);
        total += SoundEdgeStep(from, cw, true, map);
        if (map.Step(from, cw) is not { } second) return 1;
        return total + SoundEdgeStep(second, ccw, profile.CornerStepCountsBothRoutes, map);
    }

    // One edge step under the sound flag: the source tile's pieces on the edge, the neighbour (none: 1), its soundproofing and its
    // pieces on the opposite edge; its other objects cost nothing.
    private static int SoundEdgeStep<TObject>(CellCoord from, int edge, bool countSoundproof, IAwarenessMap<TObject> map) where TObject : class
    {
        var cost = PieceSoundCost(map.Edge(from, edge));
        if (map.Step(from, edge) is not { } next) return 1;
        if (countSoundproof && map.Soundproof(next)) cost += SoundproofCost;
        return cost + PieceSoundCost(map.Edge(next, (edge + 4) % 8));
    }

    private static int PieceSoundCost<TObject>(IEnumerable<LineOfFirePiece<TObject>> pieces) where TObject : class
    {
        var cost = 0;
        foreach (var piece in pieces)
            cost += piece.Kind switch
            {
                LineOfFirePieceKind.Solid or LineOfFirePieceKind.PassWalled or LineOfFirePieceKind.Window or LineOfFirePieceKind.Doorway => WallSoundCost,
                LineOfFirePieceKind.ClosedPortal => ClosedPortalSoundCost,
                LineOfFirePieceKind.OpenPortal => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(pieces)),
            };
        return cost;
    }

    /// <summary>
    /// The line-of-fire term of prowling's sight difficulty (0x4C8430 at 0x4C8BC6, AW4) from the line traced from the observer to the
    /// prowler's tile: a blocker hides the prowler (the difficulty becomes −100), else the line's cost is its cover. The shared check
    /// (<see cref="SkillCheckRules.Difficulty"/>) prices both; this only carries the line into its facts.
    /// </summary>
    public static SkillDifficultyFacts WithProwlerLine<TObject>(SkillDifficultyFacts facts, LineOfFireTrace<TObject> line) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(line);
        return line.Blocker is not null ? facts with { ProwlerHidden = true, ProwlerCover = 0 } : facts with { ProwlerHidden = false, ProwlerCover = line.Cost };
    }

    // The original's tile distance (0x4B96F0): the larger of the two axis deltas.
    private static long Distance(CellCoord a, CellCoord b) => Travel.WorldTravelGeometry.Distance(new(a.X, a.Y), new(b.X, b.Y));

    private static int Index(Loudness loudness) =>
        loudness is >= Loudness.Silent and <= Loudness.Loud ? (int)loudness : throw new ArgumentOutOfRangeException(nameof(loudness));

    private static void CheckPerception(int perception)
    {
        if (perception is < 0 or > MaxPerception) throw new ArgumentOutOfRangeException(nameof(perception), "an effective Perception is 0..1000");
    }

    private static void CheckMargin(int margin)
    {
        if (margin is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(margin), "a prowling margin is 0..100");
    }

    private static void CheckRotation(int rotation)
    {
        if (rotation is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(rotation));
    }
}
