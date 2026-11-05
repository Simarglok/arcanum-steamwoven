using System.Collections.Immutable;

namespace Arcanum.Simulation.Goals;

/// <summary>What the interrupt rule decides for one run (the original's 0x44E2C0 answers 0 for a refusal and 1 for the other two).</summary>
public enum GoalInterruptVerdict
{
    /// <summary>The run's current goal may not be interrupted at this level: the call fails.</summary>
    Refuse,
    /// <summary>A no-interrupt goal below level 6: the run is left running and the call succeeds.</summary>
    LeaveRunning,
    /// <summary>The run ends, whole, with the end node of every goal on its stack.</summary>
    Interrupt,
}

/// <summary>How a run came to its end (ledger GS16, GS21, GS24).</summary>
public enum GoalEndKind
{
    /// <summary>An interrupt: the end node of every goal runs once, top down; no turn hook, idle restart or AP charge (GS24).</summary>
    Interrupted,
    /// <summary>An interrupt during a game reset: no end node runs (GS16).</summary>
    Reset,
    /// <summary>A kill (GS21): the end nodes are called and do nothing for a killed run (<see cref="GoalRunFlags.Killed"/>).</summary>
    Killed,
    /// <summary>ARC-DEV-132 (MIG13 S3): the stepper ended the run itself, with no interrupt (the host's step answered the end, or its repeat could not be queued);
    /// the stepper has run the end nodes of the goals it popped, so none is left to run.</summary>
    Finished,
}

/// <summary>
/// A run the table ended from outside the stepper, for its owner of the end nodes to settle: <see cref="Run"/> is the run as it stood,
/// <see cref="EndNodeGoals"/> the goals whose end nodes run, top down (empty for a reset), <see cref="SignificantGoalsLeft"/> the significant-goal
/// count after the slot was freed; a count of zero means the round's "all animations done" callback is due (GS23, TL39). The events of the run
/// (by <see cref="GoalRun.Handle"/>) are removed by the caller.
/// </summary>
public sealed record GoalRunEnd(GoalRun Run, GoalEndKind Kind, ImmutableArray<GoalType> EndNodeGoals, int SignificantGoalsLeft)
{
    /// <summary>No significant goal is left: the round's callback is called (the free in the original does so after every free).</summary>
    public bool AnimationsDone => SignificantGoalsLeft == 0;
}

/// <summary>What interrupting one run did.</summary>
public enum GoalInterruptOutcome
{
    /// <summary>The handle matches no run (the original answers 0).</summary>
    NotFound,
    /// <summary>The verdict was <see cref="GoalInterruptVerdict.Refuse"/>; the run is unchanged.</summary>
    Refused,
    /// <summary>The verdict was <see cref="GoalInterruptVerdict.LeaveRunning"/>; the run is unchanged and the call succeeded.</summary>
    LeftRunning,
    /// <summary>The run was ended and freed; <see cref="GoalInterruptStep.Ended"/> says how.</summary>
    Ended,
    /// <summary>The run is the one being stepped: it is marked done and the stepper ends it (GS16, GS23).</summary>
    MarkedDone,
    /// <summary>The run is the one being stepped and was already marked done (by an earlier interrupt or kill): the call succeeds and the owner's end nodes are not run a second time (a kill's flag is kept).</summary>
    AlreadyMarked,
}

/// <summary>One interrupt of one run: the run's handle, what happened and, for <see cref="GoalInterruptOutcome.Ended"/>, the end.</summary>
public sealed record GoalInterruptStep(GoalHandle Handle, GoalInterruptOutcome Outcome, GoalRunEnd? Ended)
{
    /// <summary>The original's result: 0 for a refusal (and for no run), 1 otherwise.</summary>
    public bool Succeeded => Outcome is GoalInterruptOutcome.LeftRunning or GoalInterruptOutcome.Ended or GoalInterruptOutcome.MarkedDone or GoalInterruptOutcome.AlreadyMarked;
}

/// <summary>The table after interrupting one run (<see cref="GoalRunTable.Interrupt"/>).</summary>
public sealed record GoalInterruptResult(GoalRunTable Table, GoalInterruptStep Step);

/// <summary>The table after interrupting every run of one owner (<see cref="GoalRunTable.InterruptOwner"/>): the walk stops at the first refusal, so the runs before it have already ended (GS17).</summary>
public sealed record GoalOwnerInterruptResult(GoalRunTable Table, bool Succeeded, ImmutableArray<GoalInterruptStep> Steps)
{
    /// <summary>The runs that ended, in the order they ended.</summary>
    public IEnumerable<GoalRunEnd> Ended => Steps.Where(s => s.Ended is not null).Select(s => s.Ended!);
}

/// <summary>The table after a sweep over every run (round start, full table, map end); a refusal is recorded and the sweep goes on (GS19).</summary>
public sealed record GoalSweepResult(GoalRunTable Table, ImmutableArray<GoalInterruptStep> Steps)
{
    public IEnumerable<GoalRunEnd> Ended => Steps.Where(s => s.Ended is not null).Select(s => s.Ended!);
    public IEnumerable<GoalHandle> Refused => Steps.Where(s => s.Outcome is GoalInterruptOutcome.Refused).Select(s => s.Handle);
}

/// <summary>The table after a kill of every run of one owner (<see cref="GoalRunTable.Kill"/>).</summary>
public sealed record GoalKillResult(GoalRunTable Table, ImmutableArray<GoalInterruptStep> Steps)
{
    public IEnumerable<GoalRunEnd> Ended => Steps.Where(s => s.Ended is not null).Select(s => s.Ended!);
}

/// <summary>
/// The interrupt rule of the retail 1.0.7.4 executable (ARC-DEV-131, ledger GS16..GS18, read again in the S2 slice): the rule reads the
/// <b>current</b> goal's descriptor. An interrupt is a level 0..6 against a goal's priority 1..5:
/// <list type="bullet">
/// <item>level 6 interrupts whatever the flags are (a no-interrupt goal and priority 5 included);</item>
/// <item>below 6, a no-interrupt goal is left running and the call succeeds; priority 5 refuses;</item>
/// <item>priorities 2 and 3 are interrupted at level of at least the priority; priorities 1 and 4 at a level above it.</item>
/// </list>
/// So level 0 and 1 stop nothing, level 2 stops priorities 1..2, levels 3 and 4 priorities 1..3, level 5 priorities 1..4 and level 6 everything.
/// The table applies the verdict to its runs (<see cref="GoalRunTable.Interrupt"/>); starters use the fixed levels of <see cref="GoalStarters"/>.
/// </summary>
public static class GoalInterruptRules
{
    /// <summary>The highest level: stops everything, overlays included (the end of a run, a map change, a teleport, a failed payment).</summary>
    public const int MaxLevel = 6;
    /// <summary>The level of round start, new orders, the movement, attack, throw, follow and spread-out starters and the AI's decisions: stops the ordinary actions.</summary>
    public const int OrdinaryActions = 3;

    /// <summary>The verdict for a run whose current goal has <paramref name="current"/> as its descriptor, at <paramref name="level"/> (0..6).</summary>
    public static GoalInterruptVerdict Verdict(GoalDescriptor current, int level)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (level is < 0 or > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        if (level < MaxLevel)
        {
            if (current.NoInterrupt) return GoalInterruptVerdict.LeaveRunning;
            if (current.Priority == 5) return GoalInterruptVerdict.Refuse;
        }
        var interrupted = current.Priority switch
        {
            3 => level >= 3,
            2 => level >= 2,
            _ => level > current.Priority,
        };
        return interrupted ? GoalInterruptVerdict.Interrupt : GoalInterruptVerdict.Refuse;
    }
}
