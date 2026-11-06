using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Goals;

/// <summary>What the rotate starter 0x433440 does for an object that should face a rotation (ARC-DEV-145, GT100).</summary>
public enum RotateStartVerdict
{
    /// <summary>A null object: the starter answers 0 and starts nothing.</summary>
    NoObject,
    /// <summary>The art already faces the target: nothing starts.</summary>
    AlreadyFacing,
    /// <summary>The object is not active (destroyed or off, dead, unconscious, stunned, paralysed or stoned): nothing starts.</summary>
    NotActive,
    /// <summary>A round runs and the object is not the current participant: nothing starts.</summary>
    NotParticipant,
    /// <summary>The object runs a main goal (an overlay does not count): nothing starts, and a fidget has no goal 53 from here.</summary>
    MainRunBusy,
    /// <summary>Goal 53 starts (interrupting runs of priority 2 or less at its own level).</summary>
    Start,
}

/// <summary>The facts the rotate starter's gates read (ARC-DEV-145).</summary>
/// <param name="HasObject">The object exists.</param>
/// <param name="Facing">The art's rotation 0..7 now.</param>
/// <param name="Target">The rotation asked for, 0..7.</param>
/// <param name="Active">0x45D790: not destroyed or off, dead, unconscious, stunned, paralysed or stoned.</param>
/// <param name="InRound">A turn-based round runs (0x4B6D70).</param>
/// <param name="IsCurrentParticipant">The object is the round's current participant (0x4B6D80); read only in a round.</param>
/// <param name="HasMainRun">The object runs a goal that is not an overlay (0x423300).</param>
public readonly record struct RotateStartFacts(bool HasObject, int Facing, int Target, bool Active, bool InRound, bool IsCurrentParticipant, bool HasMainRun);

/// <summary>What the game view's right release does after its combat-mode or concealment part (RR5, GT101), for the hero.</summary>
/// <param name="Interrupted">The level-3 interrupt of the hero's runs went through (no run of priority 4 or 5 refused).</param>
/// <param name="Turn">The rotate starter's verdict for the turn toward the pointer; <see cref="RotateStartVerdict.NoObject"/> when the tail stopped before it.</param>
/// <param name="Rotation">The rotation toward the pointer's tile, 0..7; null without a turn (a refused interrupt, a pointer off the game view or off the map).</param>
public readonly record struct RightReleaseTail(bool Interrupted, RotateStartVerdict Turn, int? Rotation)
{
    /// <summary>Goal 53 starts.</summary>
    public bool StartsTurn => Turn == RotateStartVerdict.Start;
}

/// <summary>
/// ARC-DEV-145 (MIG13 S10a): the pure rules of the hero's input that the request boundary applies: the always-run rule, the rotate goal
/// (53) and the right release's tail. Godot-free; the executable's reading is the S10 read of <c>docs/architecture/goal-table.md</c>.
/// </summary>
public static class HeroInputRules
{
    /// <summary>The level the right release interrupts the hero at (RR5): it ends runs of priority 3 or less and is refused by a run of priority 4 or 5.</summary>
    public const int RightReleaseLevel = 3;

    /// <summary>Game milliseconds between two 45-degree steps of goal 53 (the node's 30 ms restart, GT102).</summary>
    public const int RotateStepMs = 30;

    /// <summary>
    /// 0x4304C0: whether a walk the hero starts is a run. With the Always Run option on it runs unless Ctrl is held; with it off it
    /// runs while Ctrl is held or the Num Lock toggle is on (GS25). The starter then also needs encumbrance below 4 (L2–L4), which the
    /// caller reads.
    /// </summary>
    [OriginalFunction(0x4304C0, OriginalFunctionScope.Full)]
    public static bool AlwaysRun(bool option, bool ctrl, bool numLock) => option ? !ctrl : ctrl || numLock;

    /// <summary>The gates of the rotate starter 0x433440 in the order the original tests them (GT100).</summary>
    [OriginalFunction(0x433440, OriginalFunctionScope.Partial)]
    public static RotateStartVerdict RotateStart(RotateStartFacts facts)
    {
        if (!facts.HasObject) return RotateStartVerdict.NoObject;
        if (facts.Facing == facts.Target) return RotateStartVerdict.AlreadyFacing;
        if (!facts.Active) return RotateStartVerdict.NotActive;
        if (facts.InRound && !facts.IsCurrentParticipant) return RotateStartVerdict.NotParticipant;
        return facts.HasMainRun ? RotateStartVerdict.MainRunBusy : RotateStartVerdict.Start;
    }

    /// <summary>
    /// One step of goal 53's node 0x42C440: the rotation 0..7 one 45-degree step toward <paramref name="target"/> the short way (a 180-degree
    /// tie turns by −1 when the current rotation is the larger, by +1 otherwise), or <paramref name="current"/> when they are equal.
    /// </summary>
    [OriginalFunction(0x42C440, OriginalFunctionScope.Partial)]
    public static int RotateStep(int current, int target)
    {
        CheckRotation(current); CheckRotation(target);
        var diff = current - target;
        if (diff == 0) return current;
        var step = diff switch { >= 1 and <= 4 => -1, >= 5 and <= 7 => 1, <= -1 and >= -4 => 1, _ => -1 };
        return (current + step + 8) % 8;
    }

    /// <summary>The number of 45-degree steps goal 53 takes from <paramref name="current"/> to <paramref name="target"/>.</summary>
    public static int RotateSteps(int current, int target)
    {
        CheckRotation(current); CheckRotation(target);
        var steps = 0;
        for (var at = current; at != target; steps++) at = RotateStep(at, target);
        return steps;
    }

    /// <summary>
    /// The right release's tail (RR5, GT101), after the combat-mode or concealment part: the hero's runs are interrupted at level 3, and a
    /// refusal (a run of priority 4 or 5, such as a spell attempt) ends the tail; otherwise the hero turns toward the pointer's tile
    /// (<c>0x4B8D50</c>: the same tile gives rotation 5) through the rotate starter, which finds no main run left after the interrupt.
    /// </summary>
    /// <param name="highestRunPriority">The highest priority among the hero's runs now (0 for none; the hero's viewer-hosted action is priority 2 walking, 3 any ordinary action, 4 casting).</param>
    /// <param name="hero">The hero's cell; null when it has none on the map (no turn).</param>
    /// <param name="pointer">The tile under the pointer; null when the pointer is off the game view or off the map (no turn).</param>
    /// <param name="facing">The art's rotation now.</param>
    /// <param name="active">0x45D790 for the hero.</param>
    /// <param name="inRound">A round runs.</param>
    /// <param name="isCurrentParticipant">The hero is the round's current participant.</param>
    public static RightReleaseTail RightReleaseTailOf(int highestRunPriority, CellCoord? hero, CellCoord? pointer, int facing, bool active, bool inRound, bool isCurrentParticipant)
    {
        if (highestRunPriority > RightReleaseLevel) return new(false, RotateStartVerdict.NoObject, null);
        if (hero is not { } from || pointer is not { } tile) return new(true, RotateStartVerdict.NoObject, null);
        var target = LineOfFireRules.Rotation(from, tile);
        var verdict = RotateStart(new(true, facing, target, active, inRound, isCurrentParticipant, HasMainRun: false));
        return new(true, verdict, target);
    }

    private static void CheckRotation(int rotation)
    {
        if (rotation is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "A rotation is 0..7.");
    }
}
