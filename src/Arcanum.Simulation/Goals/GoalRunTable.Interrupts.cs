using System.Collections.Immutable;

namespace Arcanum.Simulation.Goals;

// ARC-DEV-131 (MIG13 S2): the interrupt rule applied to the table's runs, the per-owner walk, the sweeps and the kill (ledger GS16..GS21, GS24).
// None of them runs an end node or removes an event: they return what ended (GoalRunEnd) and the caller settles it (the node port, the event queue).
public sealed partial class GoalRunTable
{
    /// <summary>
    /// Interrupts one run at <paramref name="level"/> (0..6), the rule of 0x44E2C0. A run in the table that is empty of goals or interrupted during a game reset
    /// (<paramref name="gameReset"/>) is ended with no test. Otherwise the current goal's descriptor decides (<see cref="GoalInterruptRules.Verdict"/>): a refusal
    /// changes nothing, a no-interrupt goal below level 6 is left running (and the call succeeds), anything else is ended, whole, with the end node of every goal on its
    /// stack, top down (none during a reset); the run being stepped (<see cref="SteppingSlot"/>) is only marked done and the stepper ends it. An interrupt from outside
    /// the stepper runs no subturn hook, no idle restart and no AP charge (GS24); that is for the caller to leave out.
    /// </summary>
    [OriginalFunction(0x44E2C0, OriginalFunctionScope.Partial)]
    public GoalInterruptResult Interrupt(GoalHandle handle, int level, bool gameReset = false)
    {
        if (level is < 0 or > GoalInterruptRules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        if (!TryGet(handle, out var run)) return new(this, new(handle, GoalInterruptOutcome.NotFound, null));
        if (!gameReset)
        {
            // Every goal on a run's stack has a descriptor (GoalRun.WithFrames), so the current goal's descriptor is always there to read.
            var descriptor = GoalDescriptors.Of(run.Top.Goal) ?? throw new InvalidOperationException($"goal {(int)run.Top.Goal} on a run has no descriptor");
            switch (GoalInterruptRules.Verdict(descriptor, level))
            {
                case GoalInterruptVerdict.Refuse: return new(this, new(handle, GoalInterruptOutcome.Refused, null));
                case GoalInterruptVerdict.LeaveRunning: return new(this, new(handle, GoalInterruptOutcome.LeftRunning, null));
            }
        }
        return End(run, run with { Done = true }, gameReset ? GoalEndKind.Reset : GoalEndKind.Interrupted);
    }

    /// <summary>
    /// Interrupts every run of <paramref name="owner"/> in slot order (0x424070): the first refusal stops the walk and fails it, so the runs before it have already ended
    /// (GS17); an owner with no run succeeds. A <paramref name="quiet"/> call lowers the level to 0 (a non-zero "quiet" argument). The network branch (an interrupt
    /// sent to the other side first) is not followed.
    /// </summary>
    [OriginalFunction(0x424070, OriginalFunctionScope.Partial)]
    public GoalOwnerInterruptResult InterruptOwner(string owner, int level, bool quiet = false)
    {
        if (level is < 0 or > GoalInterruptRules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        var effective = quiet ? 0 : level;
        var table = this;
        var steps = ImmutableArray.CreateBuilder<GoalInterruptStep>();
        foreach (var run in RunsOf(owner))
        {
            var result = table.Interrupt(run.Handle, effective);
            table = result.Table;
            steps.Add(result.Step);
            if (!result.Step.Succeeded) return new(table, false, steps.ToImmutable());
        }
        return new(table, true, steps.ToImmutable());
    }

    /// <summary>
    /// Interrupts every run in use at <paramref name="level"/> (0x424290, the full-table sweep): a refusal is recorded and the sweep goes on. The stepper does this at
    /// level 3 at the next step after a start found the table full (<see cref="SweepIfExhausted"/>).
    /// </summary>
    [OriginalFunction(0x424290, OriginalFunctionScope.Partial)]
    public GoalSweepResult InterruptAll(int level)
    {
        if (level is < 0 or > GoalInterruptRules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        var (table, steps) = Sweep(level, _ => true, gameReset: false, stopAtFailure: false);
        return new(table, steps);
    }

    /// <summary>
    /// The sweep at the start of a round (0x424310, ledger GS19): every run whose current goal is not "not counted" is interrupted at level 3; a refusal is recorded and
    /// the sweep goes on. A round start does this for the map and each participant again; set-turn interrupts the previous participant itself (TL36, TL37).
    /// </summary>
    [OriginalFunction(0x424310, OriginalFunctionScope.Partial)]
    public GoalSweepResult InterruptForRoundStart()
    {
        var (table, steps) = Sweep(GoalInterruptRules.OrdinaryActions, run => !run.CurrentGoalNotCounted(), gameReset: false, stopAtFailure: false);
        return new(table, steps);
    }

    /// <summary>
    /// Ends every run at level 6 (0x424250, the sweep of a map reset and of a map clear): with no run in use nothing happens; the first failure stops the sweep (level 6
    /// refuses nothing). <paramref name="gameReset"/> is the original's reset flag: a reset runs no end node.
    /// </summary>
    [OriginalFunction(0x424250, OriginalFunctionScope.Partial)]
    public GoalSweepResult ClearAll(bool gameReset = false)
    {
        if (InUseCount <= 0) return new(this, []);
        var (table, steps) = Sweep(GoalInterruptRules.MaxLevel, _ => true, gameReset, stopAtFailure: true);
        return new(table, steps);
    }

    /// <summary>
    /// The stepper's table-exhaustion check (its head, 0x423566, ledger GS2): when a start found no free slot, the next step interrupts every run at level 3 and clears
    /// the flag; with the flag clear nothing happens. The original's log message names the situation "ran out of animation slots": a warning with a designed recovery,
    /// so both rule settings follow it (S2's reading, goal-table.md).
    /// </summary>
    public GoalSweepResult SweepIfExhausted()
    {
        if (!ExhaustionPending) return new(this, []);
        var swept = InterruptAll(GoalInterruptRules.OrdinaryActions);
        return new(swept.Table.WithExhaustionCleared(), swept.Steps);
    }

    /// <summary>
    /// Kills one run (0x44E160): it is marked done and killed (<see cref="GoalRunFlags.Killed"/>), with no rule and no level; the run being stepped is only marked (the stepper ends
    /// it), any other is freed and reported with its end nodes top down, which do nothing for a killed run (GS21).
    /// </summary>
    [OriginalFunction(0x44E160, OriginalFunctionScope.Partial)]
    public GoalInterruptResult KillRun(GoalHandle handle)
    {
        if (!TryGet(handle, out var run)) return new(this, new(handle, GoalInterruptOutcome.NotFound, null));
        return End(run, run with { Done = true, Flags = run.Flags | GoalRunFlags.Killed }, GoalEndKind.Killed);
    }

    /// <summary>
    /// Kills every run of <paramref name="owner"/> (0x423FF0, used when an object is destroyed or removed with its map and before a scenery object's animation restarts).
    /// The original skips it while the light-scheme hour runs; that gate is the caller's.
    /// </summary>
    [OriginalFunction(0x423FF0, OriginalFunctionScope.Partial)]
    public GoalKillResult Kill(string owner)
    {
        var table = this;
        var steps = ImmutableArray.CreateBuilder<GoalInterruptStep>();
        foreach (var run in RunsOf(owner))
        {
            var result = table.KillRun(run.Handle);
            table = result.Table;
            steps.Add(result.Step);
            if (!result.Step.Succeeded) break;
        }
        return new(table, steps.ToImmutable());
    }

    private GoalInterruptResult End(GoalRun run, GoalRun marked, GoalEndKind kind)
    {
        if (SteppingSlot == run.Handle.Slot) return new(WithRun(marked), new(run.Handle, GoalInterruptOutcome.MarkedDone, null));
        var freed = Free(run.Handle);
        var endNodes = kind == GoalEndKind.Reset ? [] : marked.Frames.Reverse().Select(f => f.Goal).ToImmutableArray();
        return new(freed.Table, new(run.Handle, GoalInterruptOutcome.Ended, new GoalRunEnd(marked, kind, endNodes, freed.SignificantGoalsLeft)));
    }

    private (GoalRunTable Table, ImmutableArray<GoalInterruptStep> Steps) Sweep(int level, Func<GoalRun, bool> include, bool gameReset, bool stopAtFailure)
    {
        var table = this;
        var steps = ImmutableArray.CreateBuilder<GoalInterruptStep>();
        foreach (var run in Runs.ToList())
        {
            if (!include(run)) continue;
            var result = table.Interrupt(run.Handle, level, gameReset);
            table = result.Table;
            steps.Add(result.Step);
            if (stopAtFailure && !result.Step.Succeeded) break;
        }
        return (table, steps.ToImmutable());
    }
}
