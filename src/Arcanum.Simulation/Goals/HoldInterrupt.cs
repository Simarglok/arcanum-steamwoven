namespace Arcanum.Simulation.Goals;

/// <summary>
/// ARC-DEV-302 (GT146, GT147): the runs a hold's start interrupts. The interrupt-by-goal routine 0x44E4D0, called by the ObjFlag component for goal 3 and then for goal 61, ends a run of the
/// owner whose <b>bottom</b> frame or <b>current</b> (top) frame has that goal: a walk (goal 3 at the bottom) and any run whose current goal is the melee approach 61 (which goal 8 pushes).
/// A run 4, run-near 55 and 56, move-near 7 and a skill walk 33 step with goal 5 on top and are not ended, nor are attack, cast and skill runs whose current goal is not 61.
/// </summary>
public static class HoldInterrupt
{
    /// <summary>The goals the hold interrupts, in the order the component calls them.</summary>
    public static IReadOnlyList<GoalType> Goals { get; } = [GoalType.MoveToTile, GoalType.MeleeApproach];

    /// <summary>The owner's live runs the hold interrupts, in slot order.</summary>
    [OriginalFunction(0x44E4D0, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<GoalRun> Runs(GoalRunTable goals, string owner)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(owner);
        return [.. goals.RunsOf(owner).Where(r => !r.Done && Goals.Any(goal => r.Bottom.Goal == goal || r.Top.Goal == goal))];
    }
}
