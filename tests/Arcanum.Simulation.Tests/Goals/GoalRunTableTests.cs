using System.Collections.Immutable;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-131 (MIG13 S2): the run table (ledger GS1..GS4, GS14, GS16..GS24) on synthetic runs.</summary>
[TestClass]
public sealed class GoalRunTableTests
{
    private const string A = "npc:a";
    private const string B = "npc:b";

    private static GoalFrame F(GoalType goal, string? target = null, string? item = null) => new(goal, target) { Item = item };

    private static (GoalRunTable Table, GoalHandle Handle) Add(GoalRunTable table, string owner, GoalType goal, string? target = null, GoalRunFlags flags = GoalRunFlags.None)
    {
        var allocation = table.Allocate(owner, F(goal, target), flags);
        Assert.IsNotNull(allocation.Handle, "a slot is free");
        return (allocation.Table, allocation.Handle.Value);
    }

    /// <summary>One run whose stack holds <paramref name="goals"/>, bottom first.</summary>
    private static (GoalRunTable Table, GoalHandle Handle) AddStack(GoalRunTable table, string owner, params GoalType[] goals)
    {
        var (added, handle) = Add(table, owner, goals[0]);
        Assert.IsTrue(added.TryGet(handle, out var run));
        return (Replace(added, run.WithFrames([.. goals.Select(g => F(g))])), handle);
    }

    private static GoalRunTable Replace(GoalRunTable table, GoalRun run)
    {
        Assert.IsTrue(table.TryReplace(run, out var next));
        return next;
    }

    private static int[] Slots(GoalRunTable table) => [.. table.Runs.Select(r => r.Handle.Slot)];

    [TestMethod]
    public void AnEmptyTableHasNoRunAndTheNextIdIsOne()
    {
        var table = GoalRunTable.Empty;
        Assert.AreEqual(0, table.InUseCount);
        Assert.AreEqual(1, table.NextRunId);
        Assert.IsFalse(table.ExhaustionPending);
        Assert.IsNull(table.SteppingSlot);
        Assert.IsNull(table[0]);
        Assert.IsFalse(table.TryGet(new GoalHandle(0, 1), out _));
        Assert.IsFalse(table.TryGet(new GoalHandle(-1, 1), out _));
        Assert.IsFalse(table.TryGet(new GoalHandle(216, 1), out _));
        Assert.AreEqual(0, table.SignificantGoalCount);
        Assert.AreEqual(0, table.RunsOf(A).Length);
    }

    [TestMethod]
    public void AllocationTakesTheLowestFreeSlotAndAFreedSlotsOldHandleNeverMatchesAgain()
    {
        var table = GoalRunTable.Empty;
        (table, var h0) = Add(table, A, GoalType.Attack);
        (table, var h1) = Add(table, A, GoalType.Fidget);
        (table, var h2) = Add(table, B, GoalType.Attack);
        Assert.AreEqual(new GoalHandle(0, 1), h0);
        Assert.AreEqual(new GoalHandle(1, 2), h1);
        Assert.AreEqual(new GoalHandle(2, 3), h2);
        Assert.AreEqual(3, table.InUseCount);
        Assert.AreEqual(4, table.NextRunId);

        var freed = table.Free(h1);
        Assert.AreEqual(GoalType.Fidget, freed.Run!.Bottom.Goal);
        table = freed.Table;
        Assert.AreEqual(2, table.InUseCount);
        Assert.IsFalse(table.TryGet(h1, out _), "the freed slot's old handle does not match");

        (table, var h3) = Add(table, B, GoalType.Talk);
        Assert.AreEqual(new GoalHandle(1, 4), h3, "the lowest free slot is reused with a new id");
        Assert.IsFalse(table.TryGet(h1, out _), "the old handle names the same slot with another id");
        Assert.IsTrue(table.TryGet(h3, out var run));
        Assert.AreEqual(B, run.Owner);
        Assert.IsFalse(table.Free(h1).AnimationsDone, "freeing a stale handle frees nothing");
        Assert.IsNull(table.Free(h1).Run);
    }

    [TestMethod]
    public void ARunHoldsItsStartValuesAndAGoalWithoutADescriptorCannotBeTheBottom()
    {
        var table = GoalRunTable.Empty;
        var allocation = table.Allocate(A, F(GoalType.MoveToTile), GoalRunFlags.Running, 5, 80);
        var run = allocation.Table[allocation.Handle!.Value.Slot]!;
        Assert.AreEqual(A, run.Owner);
        Assert.AreEqual(GoalRunFlags.Running, run.Flags);
        Assert.AreEqual(5, run.DueMs);
        Assert.AreEqual(80, run.PaceMs);
        Assert.AreEqual(1, run.Depth);
        Assert.IsFalse(run.Done);
        Assert.ThrowsExactly<ArgumentException>(() => table.Allocate(A, F(GoalType.UnlockDoor)));
        Assert.ThrowsExactly<ArgumentException>(() => table.Allocate("", F(GoalType.Attack)));
        Assert.ThrowsExactly<ArgumentException>(() => run.WithFrames([F(GoalType.PickWeapon)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => run.WithFrames([]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => run.WithFrames([.. Enumerable.Repeat(F(GoalType.Attack), 9)]));
        Assert.AreEqual(8, run.WithFrames([.. Enumerable.Repeat(F(GoalType.Attack), 8)]).Depth);
    }

    [TestMethod]
    public void AFullTableFailsTheStartSetsTheFlagAndTheNextStepSweepsAtLevelThree()
    {
        var table = GoalRunTable.Empty;
        // 213 ordinary attacks, a dying run (refuses level 3), an eye-candy overlay (no-interrupt) and the last slot for a walk.
        for (var i = 0; i < 213; i++) (table, _) = Add(table, $"npc:{i}", GoalType.Attack);
        (table, var dying) = Add(table, "npc:dying", GoalType.Dying);
        (table, var overlay) = Add(table, "npc:fx", GoalType.EyeCandy);
        (table, var walk) = Add(table, "npc:walker", GoalType.MoveToTile);
        Assert.AreEqual(216, table.InUseCount);
        Assert.IsFalse(table.ExhaustionPending);

        var failed = table.Allocate("npc:late", F(GoalType.Attack));
        Assert.IsNull(failed.Handle, "nothing is allocated");
        Assert.IsTrue(failed.Table.ExhaustionPending, "the flag of GS2");
        Assert.AreEqual(216, failed.Table.InUseCount);
        Assert.AreEqual(table.NextRunId, failed.Table.NextRunId, "no id is used");

        var swept = failed.Table.SweepIfExhausted();
        Assert.IsFalse(swept.Table.ExhaustionPending, "the sweep clears the flag");
        Assert.AreEqual(216, swept.Steps.Length, "every run in use is tried");
        Assert.AreEqual(214, swept.Ended.Count(), "the 213 attacks and the walk end");
        CollectionAssert.AreEquivalent(new[] { dying }, swept.Refused.ToArray());
        Assert.IsTrue(swept.Table.TryGet(dying, out _), "dying refuses level 3");
        Assert.IsTrue(swept.Table.TryGet(overlay, out _), "the overlay is left running");
        Assert.IsFalse(swept.Table.TryGet(walk, out _));
        Assert.AreEqual(2, swept.Table.InUseCount);

        var again = swept.Table.SweepIfExhausted();
        Assert.AreEqual(0, again.Steps.Length, "nothing more with the flag clear");
        Assert.AreSame(swept.Table, again.Table);
    }

    // ---- interrupts ----

    [TestMethod]
    public void AnInterruptReadsTheCurrentGoalAndEndsTheWholeRunWithEveryEndNodeTopDown()
    {
        var (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.Attack, GoalType.CombatApproach, GoalType.AttemptMove);
        var result = table.Interrupt(run, 3);
        Assert.AreEqual(GoalInterruptOutcome.Ended, result.Step.Outcome);
        Assert.IsTrue(result.Step.Succeeded);
        var end = result.Step.Ended!;
        Assert.AreEqual(GoalEndKind.Interrupted, end.Kind);
        CollectionAssert.AreEqual(new[] { GoalType.AttemptMove, GoalType.CombatApproach, GoalType.Attack }, end.EndNodeGoals.ToArray(), "top down, never only the top goal");
        Assert.IsTrue(end.Run.Done);
        Assert.AreEqual(run, end.Run.Handle);
        Assert.AreEqual(0, result.Table.InUseCount);
        Assert.IsFalse(result.Table.TryGet(run, out _));
        Assert.AreEqual(0, end.SignificantGoalsLeft);
        Assert.IsTrue(end.AnimationsDone);
    }

    [TestMethod]
    public void TheRuleReadsTheTopGoalNotTheBottom()
    {
        // The attack (3) with a walk (2) on top is stopped at level 2; the walk (2) with a spell attempt (4) on top is not stopped at level 3.
        var (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.Attack, GoalType.MoveNear);
        Assert.AreEqual(GoalInterruptOutcome.Ended, table.Interrupt(run, 2).Step.Outcome);
        (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.MoveToTile, GoalType.AttemptSpell);
        var refused = table.Interrupt(run, 3);
        Assert.AreEqual(GoalInterruptOutcome.Refused, refused.Step.Outcome);
        Assert.IsFalse(refused.Step.Succeeded);
        Assert.AreSame(table, refused.Table);
        Assert.AreEqual(GoalInterruptOutcome.Ended, table.Interrupt(run, 5).Step.Outcome);
    }

    [TestMethod]
    public void ANoInterruptRunIsLeftRunningBelowSixAndEndedAtSix()
    {
        var (table, run) = Add(GoalRunTable.Empty, A, GoalType.EyeCandy);
        for (var level = 0; level < 6; level++)
        {
            var left = table.Interrupt(run, level);
            Assert.AreEqual(GoalInterruptOutcome.LeftRunning, left.Step.Outcome, $"level {level}");
            Assert.IsTrue(left.Step.Succeeded, "success, not a refusal");
            Assert.AreSame(table, left.Table);
        }
        Assert.AreEqual(GoalInterruptOutcome.Ended, table.Interrupt(run, 6).Step.Outcome);
    }

    [TestMethod]
    public void TheRunBeingSteppedIsOnlyMarkedDoneAndTheStepperEndsIt()
    {
        var (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.Attack, GoalType.MoveNear);
        table = table.WithStepping(run.Slot);
        Assert.AreEqual(run.Slot, table.SteppingSlot);
        var marked = table.Interrupt(run, 6);
        Assert.AreEqual(GoalInterruptOutcome.MarkedDone, marked.Step.Outcome);
        Assert.IsTrue(marked.Step.Succeeded);
        Assert.IsNull(marked.Step.Ended);
        Assert.AreEqual(1, marked.Table.InUseCount, "still in the table");
        Assert.IsTrue(marked.Table.TryGet(run, out var done));
        Assert.IsTrue(done.Done);
        Assert.AreEqual(0, marked.Table.RunsOf(A).Length, "a done run is skipped by every query");
        Assert.IsNull(marked.Table.FirstRunOf(A));
        // The stepper's own end: clear the marker, then free the run.
        var freed = marked.Table.WithStepping(null).Free(run);
        Assert.AreEqual(0, freed.Table.InUseCount);
        Assert.IsTrue(freed.AnimationsDone);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.WithStepping(216));
    }

    [TestMethod]
    public void ASecondMarkOfTheRunBeingSteppedAnswersAlreadyMarkedAndKeepsAKillsFlag()
    {
        // ARC-DEV-135: the first mark is the one whose caller runs the end nodes; a later interrupt or kill by handle succeeds and says it was already marked.
        var (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.Attack, GoalType.MoveNear);
        table = table.WithStepping(run.Slot);
        var first = table.Interrupt(run, 6);
        Assert.AreEqual(GoalInterruptOutcome.MarkedDone, first.Step.Outcome);
        var second = first.Table.Interrupt(run, 6);
        Assert.AreEqual(GoalInterruptOutcome.AlreadyMarked, second.Step.Outcome);
        Assert.IsTrue(second.Step.Succeeded);
        Assert.IsNull(second.Step.Ended);
        Assert.AreEqual(1, second.Table.InUseCount);
        var killed = second.Table.KillRun(run);
        Assert.AreEqual(GoalInterruptOutcome.AlreadyMarked, killed.Step.Outcome);
        Assert.IsTrue(killed.Table.TryGet(run, out var done));
        Assert.IsTrue(done.Done && (done.Flags & GoalRunFlags.Killed) != 0, "the kill's flag is kept on the marked run");
    }

    [TestMethod]
    public void AGameResetEndsARunWithNoTestAndNoEndNode()
    {
        var (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.Dying, GoalType.AnimateStunned);
        Assert.AreEqual(GoalInterruptOutcome.Refused, table.Interrupt(run, 0).Step.Outcome);
        var reset = table.Interrupt(run, 0, gameReset: true);
        Assert.AreEqual(GoalInterruptOutcome.Ended, reset.Step.Outcome);
        Assert.AreEqual(GoalEndKind.Reset, reset.Step.Ended!.Kind);
        Assert.AreEqual(0, reset.Step.Ended.EndNodeGoals.Length);
        Assert.AreEqual(0, reset.Table.InUseCount);
    }

    [TestMethod]
    public void AnInterruptOfNoRunFailsAndALevelOutsideTheRangeIsRefused()
    {
        var table = GoalRunTable.Empty;
        var none = table.Interrupt(new GoalHandle(3, 9), 3);
        Assert.AreEqual(GoalInterruptOutcome.NotFound, none.Step.Outcome);
        Assert.IsFalse(none.Step.Succeeded);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.Interrupt(new GoalHandle(0, 1), 7));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.InterruptOwner(A, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.InterruptAll(7));
    }

    [TestMethod]
    public void TheWalkOverAnOwnersRunsStopsAtTheFirstRefusalAndLeavesEarlierRunsEnded()
    {
        var table = GoalRunTable.Empty;
        (table, var walk) = Add(table, A, GoalType.MoveToTile);   // slot 0, priority 2: ends at 3
        (table, var dying) = Add(table, A, GoalType.Dying);       // slot 1, priority 5: refuses
        (table, var attack) = Add(table, A, GoalType.Attack);     // slot 2: never reached
        (table, var other) = Add(table, B, GoalType.Attack);      // another owner: untouched
        var result = table.InterruptOwner(A, 3);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(2, result.Steps.Length);
        CollectionAssert.AreEqual(new[] { walk }, result.Ended.Select(e => e.Run.Handle).ToArray());
        Assert.IsFalse(result.Table.TryGet(walk, out _), "the run before the refusal has ended");
        Assert.IsTrue(result.Table.TryGet(dying, out _));
        Assert.IsTrue(result.Table.TryGet(attack, out _), "the walk stopped before it");
        Assert.IsTrue(result.Table.TryGet(other, out _));
    }

    [TestMethod]
    public void AnOwnerWithNoRunSucceedsAndAQuietCallLowersTheLevelToZero()
    {
        var table = GoalRunTable.Empty;
        var none = table.InterruptOwner(A, 3);
        Assert.IsTrue(none.Succeeded);
        Assert.AreEqual(0, none.Steps.Length);

        (table, var overlay) = Add(table, A, GoalType.EyeCandy);
        var quietOverlay = table.InterruptOwner(A, 6, quiet: true);
        Assert.IsTrue(quietOverlay.Succeeded, "level 0 leaves a no-interrupt goal running");
        Assert.IsTrue(quietOverlay.Table.TryGet(overlay, out _));
        Assert.AreEqual(GoalInterruptOutcome.Ended, table.InterruptOwner(A, 6).Steps.Single().Outcome);

        (table, var attack) = Add(table, A, GoalType.Attack);
        var quietAttack = table.InterruptOwner(A, 6, quiet: true);
        Assert.IsFalse(quietAttack.Succeeded, "level 0 refuses an ordinary goal (a no-interrupt start fails beside a busy run)");
        Assert.IsTrue(quietAttack.Table.TryGet(attack, out _));
    }

    [TestMethod]
    public void RoundStartInterruptsAtThreeSkippingRunsWhoseCurrentGoalIsNotCounted()
    {
        var table = GoalRunTable.Empty;
        (table, var attack) = Add(table, A, GoalType.Attack);
        (table, var loop) = Add(table, B, GoalType.AnimateLoop);                  // not counted by its descriptor
        (table, var dying) = Add(table, "npc:c", GoalType.Dying);                 // refuses level 3
        (table, var fx) = Add(table, "npc:d", GoalType.EyeCandy);                 // left running
        (table, var flagged) = Add(table, "npc:e", GoalType.Attack);              // not counted by its frame flag 0x80
        Assert.IsTrue(table.TryGet(flagged, out var flaggedRun));
        table = Replace(table, flaggedRun.WithFrames([flaggedRun.Bottom with { Flags = GoalFrameFlags.NotCounted }]));

        var sweep = table.InterruptForRoundStart();
        Assert.AreEqual(3, sweep.Steps.Length, "the two not-counted runs are skipped, not tried");
        CollectionAssert.AreEqual(new[] { attack }, sweep.Ended.Select(e => e.Run.Handle).ToArray());
        CollectionAssert.AreEqual(new[] { dying }, sweep.Refused.ToArray());
        Assert.AreEqual(GoalInterruptOutcome.LeftRunning, sweep.Steps.Single(s => s.Handle == fx).Outcome);
        Assert.IsTrue(sweep.Table.TryGet(loop, out _));
        Assert.IsTrue(sweep.Table.TryGet(flagged, out _));
        Assert.IsTrue(sweep.Table.TryGet(dying, out _));
        Assert.IsFalse(sweep.Table.TryGet(attack, out _));
    }

    [TestMethod]
    public void TheFullTableSweepTakesAnyLevelAndRecordsRefusals()
    {
        var table = GoalRunTable.Empty;
        (table, var spell) = Add(table, A, GoalType.AttemptSpell);
        (table, var attack) = Add(table, B, GoalType.Attack);
        var level3 = table.InterruptAll(3);
        CollectionAssert.AreEqual(new[] { spell }, level3.Refused.ToArray(), "priority 4 needs level 5");
        CollectionAssert.AreEqual(new[] { attack }, level3.Ended.Select(e => e.Run.Handle).ToArray());
        var level5 = level3.Table.InterruptAll(5);
        Assert.AreEqual(1, level5.Ended.Count());
        Assert.AreEqual(0, level5.Table.InUseCount);
        Assert.AreEqual(0, GoalRunTable.Empty.InterruptAll(6).Steps.Length);
    }

    [TestMethod]
    public void MapEndEndsEveryRunAtLevelSixWithTheirEndNodesUnlessItIsAGameReset()
    {
        var table = GoalRunTable.Empty;
        (table, _) = Add(table, A, GoalType.Dying);
        (table, _) = Add(table, B, GoalType.EyeCandy);
        (table, _) = AddStack(table, "npc:c", GoalType.Attack, GoalType.MoveNear);
        var cleared = table.ClearAll();
        Assert.AreEqual(3, cleared.Ended.Count());
        Assert.AreEqual(0, cleared.Table.InUseCount);
        Assert.IsTrue(cleared.Ended.All(e => e.Kind == GoalEndKind.Interrupted));
        Assert.AreEqual(2, cleared.Ended.Last().EndNodeGoals.Length);
        var reset = table.ClearAll(gameReset: true);
        Assert.IsTrue(reset.Ended.All(e => e.Kind == GoalEndKind.Reset && e.EndNodeGoals.Length == 0));
        Assert.AreEqual(0, reset.Table.InUseCount);
        var empty = GoalRunTable.Empty.ClearAll();
        Assert.AreEqual(0, empty.Steps.Length);
    }

    [TestMethod]
    public void AKillEndsEveryRunOfTheOwnerWithNoRuleAndMarksThemKilled()
    {
        var table = GoalRunTable.Empty;
        (table, _) = Add(table, A, GoalType.Dying);               // refuses every level below 6, a kill has none
        (table, _) = Add(table, A, GoalType.EyeCandy);
        (table, _) = AddStack(table, A, GoalType.Attack, GoalType.MoveNear);
        (table, var other) = Add(table, B, GoalType.Dying);
        var kill = table.Kill(A);
        Assert.AreEqual(3, kill.Ended.Count());
        Assert.IsTrue(kill.Ended.All(e => e.Kind == GoalEndKind.Killed && (e.Run.Flags & GoalRunFlags.Killed) != 0 && e.Run.Done));
        Assert.AreEqual(2, kill.Ended.Last().EndNodeGoals.Length, "the end nodes are called, for the node code's kill guard to skip");
        Assert.AreEqual(1, kill.Table.InUseCount);
        Assert.IsTrue(kill.Table.TryGet(other, out _));
        Assert.AreEqual(0, kill.Table.Kill(A).Steps.Length);

        // The stepped run is only marked (done and killed); the stepper ends it.
        (table, var stepped) = Add(GoalRunTable.Empty, A, GoalType.Attack);
        var marked = table.WithStepping(stepped.Slot).KillRun(stepped);
        Assert.AreEqual(GoalInterruptOutcome.MarkedDone, marked.Step.Outcome);
        Assert.IsTrue(marked.Table.TryGet(stepped, out var run));
        Assert.IsTrue(run.Done && (run.Flags & GoalRunFlags.Killed) != 0);
        Assert.AreEqual(GoalInterruptOutcome.NotFound, table.KillRun(new GoalHandle(5, 5)).Step.Outcome);
    }

    // ---- queries ----

    [TestMethod]
    public void AnOverlayInALowerSlotHidesTheWalkFromRunsGoalAndTheIdleTestButNotFromTheOtherQueries()
    {
        var table = GoalRunTable.Empty;
        (table, var eyeCandy) = Add(table, A, GoalType.EyeCandy);   // slot 0, an overlay
        (table, var walk) = Add(table, A, GoalType.MoveToTile);     // slot 1
        Assert.AreEqual(2, table.RunsOf(A).Length);
        Assert.AreEqual(eyeCandy, table.FirstRunOf(A)!.Handle);
        Assert.IsNull(table.RunRunningGoal(A, GoalType.MoveToTile), "the first-run shadowing of GS4: the walk is hidden");
        Assert.IsNotNull(table.RunRunningGoal(A, GoalType.EyeCandy));
        Assert.AreEqual(walk, table.MainRunOf(A)!.Handle, "the main run is the first without the no-interrupt flag");
        Assert.AreEqual(2, table.MainRunPriority(A));
        Assert.AreEqual(walk, table.RunWithBottomGoal(A, GoalType.MoveToTile)!.Handle, "the all-runs queries see it");
        Assert.AreEqual(walk, table.EqualGoalQueued(A, F(GoalType.MoveToTile))!.Handle);
        Assert.AreEqual(walk, table.BusyRun(A)!.Handle, "the eye candy is carried, so only the walk is busy");
        Assert.IsFalse(table.IsIdle(A, true));
    }

    [TestMethod]
    public void TheMainRunSkipsOverlaysAndAnOwnerWithOnlyOverlaysHasNone()
    {
        var table = GoalRunTable.Empty;
        (table, _) = Add(table, A, GoalType.EyeCandy);
        Assert.IsNull(table.MainRunOf(A));
        Assert.AreEqual(0, table.MainRunPriority(A));
        (table, var attack) = Add(table, A, GoalType.Attack);
        Assert.AreEqual(attack, table.MainRunOf(A)!.Handle);
        Assert.AreEqual(3, table.MainRunPriority(A));
        Assert.AreEqual(0, table.MainRunPriority(B));
        Assert.IsNull(table.MainRunOf(B));
    }

    [TestMethod]
    public void TheAliasQueriesReadTheQueriedGoalsList()
    {
        var table = GoalRunTable.Empty;
        (table, var run) = Add(table, A, GoalType.RunToTile);
        Assert.AreEqual(run, table.RunRunningGoal(A, GoalType.MoveToTile)!.Handle, "3 lists 4");
        Assert.AreEqual(run, table.RunRunningGoal(A, GoalType.RunToTile)!.Handle);
        Assert.AreEqual(run, table.RunRunningGoal(A, GoalType.MoveNearTile)!.Handle, "7 lists 4");
        Assert.IsNull(table.RunRunningGoal(A, GoalType.Attack));
        Assert.IsNull(table.RunWithBottomGoal(A, GoalType.MoveToTile), "the exact query takes no aliases");
        Assert.AreEqual(run, table.RunWithBottomGoal(A, GoalType.RunToTile)!.Handle);

        var wander = Add(GoalRunTable.Empty, A, GoalType.Wander).Table;
        Assert.IsNotNull(wander.RunRunningGoal(A, GoalType.Wander));
        Assert.IsNull(wander.RunRunningGoal(A, GoalType.MoveNearTile), "7 does not list 65");
        var walkRun = Add(GoalRunTable.Empty, A, GoalType.MoveNearTile).Table;
        Assert.IsNotNull(walkRun.RunRunningGoal(A, GoalType.Wander), "65 lists 7");
    }

    [TestMethod]
    public void AnEqualGoalNeedsTheSameActivityTheSameTargetAndTheSameItem()
    {
        var table = GoalRunTable.Empty;
        var allocation = table.Allocate(A, F(GoalType.Attack, "npc:foe"));
        table = allocation.Table;
        Assert.IsNotNull(table.EqualGoalQueued(A, F(GoalType.Attack, "npc:foe")));
        Assert.IsNotNull(table.EqualGoalQueued(A, F(GoalType.AttemptAttack, "npc:foe")), "19 lists 18");
        Assert.IsNull(table.EqualGoalQueued(A, F(GoalType.Attack, "npc:other")));
        Assert.IsNull(table.EqualGoalQueued(A, F(GoalType.Attack, "npc:foe", item: "item:knife")));
        Assert.IsNull(table.EqualGoalQueued(A, F(GoalType.Flee, "npc:foe")));
        Assert.IsNull(table.EqualGoalQueued(B, F(GoalType.Attack, "npc:foe")), "another owner's runs are not read");
        Assert.IsNull(table.EqualGoalQueued(A, F(GoalType.Kill, "npc:foe")), "20 lists 19, not 18");
    }

    [TestMethod]
    public void TheFirstRunAttacksComparesTargetsThroughTheirLeaders()
    {
        var table = Add(GoalRunTable.Empty, A, GoalType.Attack, "npc:foe").Table;
        Assert.IsNotNull(table.FirstRunAttacks(A, null), "no target: any attack");
        Assert.IsNotNull(table.FirstRunAttacks(A, "npc:foe"));
        Assert.IsNull(table.FirstRunAttacks(A, "npc:friend"));
        string? Leader(string who) => who == "npc:friend" ? "npc:foe" : null;
        Assert.IsNotNull(table.FirstRunAttacks(A, "npc:friend", Leader), "the leaders of the two are the same");
        Assert.IsNull(table.FirstRunAttacks(B, null));
        var walk = Add(GoalRunTable.Empty, A, GoalType.MoveToTile).Table;
        Assert.IsNull(walk.FirstRunAttacks(A, null));
        var hidden = Add(Add(GoalRunTable.Empty, A, GoalType.EyeCandy).Table, A, GoalType.Attack, "npc:foe").Table;
        Assert.IsNull(hidden.FirstRunAttacks(A, "npc:foe"), "first run only");
        Assert.IsNotNull(Add(GoalRunTable.Empty, A, GoalType.AttemptAttack, "npc:foe").Table.FirstRunAttacks(A, "npc:foe"), "19 is 18's alias");
    }

    [TestMethod]
    public void TheIdleTestNeedsOneIdleGoalOnTheFirstRunAndAnIdleArt()
    {
        foreach (var idle in new[] { GoalType.Animate, GoalType.AnimateLoop, GoalType.Fidget })
            Assert.IsTrue(Add(GoalRunTable.Empty, A, idle).Table.IsIdle(A, true), $"{idle}");
        var fidget = Add(GoalRunTable.Empty, A, GoalType.Fidget).Table;
        Assert.IsFalse(fidget.IsIdle(A, false), "the art's animation is not 0");
        Assert.IsFalse(Add(GoalRunTable.Empty, A, GoalType.MoveToTile).Table.IsIdle(A, true));
        Assert.IsFalse(Add(GoalRunTable.Empty, A, GoalType.Talk).Table.IsIdle(A, true));
        Assert.IsFalse(GoalRunTable.Empty.IsIdle(A, true), "no run");
        var (deep, run) = AddStack(GoalRunTable.Empty, A, GoalType.Fidget, GoalType.Fidget);
        Assert.IsFalse(deep.IsIdle(A, true), "a stack of two is not idle");
        Assert.IsTrue(deep.TryGet(run, out _));
    }

    [TestMethod]
    public void BusyRunSkipsCarriedBottomGoals()
    {
        var table = GoalRunTable.Empty;
        (table, _) = Add(table, A, GoalType.AnimateStunned);   // carried
        Assert.IsNull(table.BusyRun(A));
        (table, var attack) = Add(table, A, GoalType.Attack);   // not carried
        Assert.AreEqual(attack, table.BusyRun(A)!.Handle);
        Assert.IsNull(table.BusyRun(B));
    }

    [TestMethod]
    public void TheSignificantGoalCountFollowsTheRunsAndTheFreeReportsWhenNoneIsLeft()
    {
        var table = GoalRunTable.Empty;
        (table, var attack) = Add(table, A, GoalType.Attack);                                   // 1
        (table, _) = Add(table, A, GoalType.EyeCandy);                                          // overlay: 0
        (table, var fidget) = Add(table, B, GoalType.Fidget);                                   // priority 1: 0
        (table, var stack) = AddStack(table, B, GoalType.Fidget, GoalType.MoveToTile);          // fidget 0 + walk 1
        // The current goal is not counted, so the whole run counts 0 (the derived count; the original's counter would have counted the attack when it was pushed, open question Q3).
        (table, _) = AddStack(table, "npc:c", GoalType.Attack, GoalType.AnimateLoop);
        (table, var dying) = Add(table, "npc:d", GoalType.Dying);                               // 1
        Assert.AreEqual(3, table.SignificantGoalCount);

        var result = table.Free(attack);
        Assert.AreEqual(2, result.SignificantGoalsLeft);
        Assert.IsFalse(result.AnimationsDone);
        result = result.Table.Free(dying);
        Assert.AreEqual(1, result.SignificantGoalsLeft);
        result = result.Table.Free(stack);
        Assert.AreEqual(0, result.SignificantGoalsLeft);
        Assert.IsTrue(result.AnimationsDone, "the callback is due when the count is zero");
        result = result.Table.Free(fidget);
        Assert.IsTrue(result.AnimationsDone, "a free with the count at zero calls it whether or not the run was significant (GS23)");
        Assert.IsFalse(table.Free(new GoalHandle(200, 1)).AnimationsDone, "no run freed, no callback");
    }

    // ---- changes of a run's stack ----

    [TestMethod]
    public void AnInsertAtTheBottomShiftsTheGoalsUpAndTheNewGoalRunsLast()
    {
        var (table, run) = AddStack(GoalRunTable.Empty, A, GoalType.Attack, GoalType.CombatApproach);
        var inserted = table.InsertAtBottom(run, F(GoalType.AttemptSpell, "npc:t"));
        Assert.AreEqual(GoalInsertOutcome.Inserted, inserted.Outcome);
        Assert.IsTrue(inserted.Table.TryGet(run, out var after));
        CollectionAssert.AreEqual(new[] { GoalType.AttemptSpell, GoalType.Attack, GoalType.CombatApproach }, after.Frames.Select(f => f.Goal).ToArray());
        Assert.AreEqual(GoalType.CombatApproach, after.Top.Goal, "the current goal is unchanged");
        Assert.AreEqual(GoalType.AttemptSpell, after.Bottom.Goal);
        Assert.AreEqual(3, after.Depth);
        Assert.AreEqual(GoalInsertOutcome.NoRun, table.InsertAtBottom(new GoalHandle(9, 9), F(GoalType.Attack)).Outcome);
        Assert.AreEqual(GoalInsertOutcome.NoDescriptor, table.InsertAtBottom(run, F(GoalType.UnlockDoor)).Outcome);
    }

    [TestMethod]
    public void AnInsertOnAFullStackIsRefusedWithATypedResult()
    {
        var (table, run) = AddStack(GoalRunTable.Empty, A, [.. Enumerable.Repeat(GoalType.Attack, 8)]);
        Assert.IsTrue(table.TryGet(run, out var full));
        Assert.AreEqual(8, full.Depth);
        var refused = table.InsertAtBottom(run, F(GoalType.Fidget));
        Assert.AreEqual(GoalInsertOutcome.StackFull, refused.Outcome);
        Assert.AreSame(table, refused.Table);
        var (seven, run7) = AddStack(GoalRunTable.Empty, A, [.. Enumerable.Repeat(GoalType.Attack, 7)]);
        Assert.AreEqual(GoalInsertOutcome.Inserted, seven.InsertAtBottom(run7, F(GoalType.Fidget)).Outcome);
    }

    [TestMethod]
    public void ARetargetOverwritesTheBottomDestinationAndSetsTheReplanFlag()
    {
        var table = GoalRunTable.Empty;
        var allocation = table.Allocate(A, new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(3, 4)));
        table = allocation.Table;
        var handle = allocation.Handle!.Value;
        var same = table.RetargetDestination(handle, new CellCoord(3, 4));
        Assert.AreEqual(GoalRetargetOutcome.Unchanged, same.Outcome);
        Assert.AreSame(table, same.Table);
        var moved = table.RetargetDestination(handle, new CellCoord(9, 1));
        Assert.AreEqual(GoalRetargetOutcome.Retargeted, moved.Outcome);
        Assert.AreEqual(handle, moved.Handle);
        Assert.IsTrue(moved.Table.TryGet(handle, out var run));
        Assert.AreEqual(new CellCoord(9, 1), run.Bottom.Tile);
        Assert.AreEqual(GoalPathFlags.Replan, run.PathFlags);
        Assert.AreEqual(1, moved.Table.InUseCount, "no new run");
        Assert.AreEqual(GoalRetargetOutcome.NoRun, table.RetargetDestination(new GoalHandle(7, 7), new CellCoord(1, 1)).Outcome);
    }

    [TestMethod]
    public void ReplaceTakesOnlyTheSameRunOfTheSameOwner()
    {
        var (table, run) = Add(GoalRunTable.Empty, A, GoalType.Attack);
        Assert.IsTrue(table.TryGet(run, out var current));
        Assert.IsTrue(table.TryReplace(current with { DueMs = 40 }, out var next));
        Assert.AreEqual(40, next[run.Slot]!.DueMs);
        Assert.AreEqual(0, table[run.Slot]!.DueMs, "the old table is unchanged");
        Assert.IsFalse(table.TryReplace(new GoalRun(B, run, F(GoalType.Attack)), out _), "another owner");
        Assert.IsFalse(table.TryReplace(new GoalRun(A, new GoalHandle(run.Slot, 99), F(GoalType.Attack)), out _), "another id");
        Assert.IsFalse(table.TryReplace(new GoalRun(A, new GoalHandle(5, 1), F(GoalType.Attack)), out _), "a free slot");
    }

    [TestMethod]
    public void OnlyARunWhoseBottomGoalWalksToATileCanBeRetargeted()
    {
        foreach (var walk in new[] { GoalType.MoveToTile, GoalType.RunToTile, GoalType.MoveNearTile, GoalType.RunNearTile })
        {
            var allocation = GoalRunTable.Empty.Allocate(A, new GoalFrame(walk, tile: new CellCoord(1, 1)));
            Assert.AreEqual(GoalRetargetOutcome.Retargeted, allocation.Table.RetargetDestination(allocation.Handle!.Value, new CellCoord(2, 2)).Outcome, $"{walk}");
        }
        foreach (var other in new[] { GoalType.Attack, GoalType.MoveNear, GoalType.Wander, GoalType.Follow })
        {
            var allocation = GoalRunTable.Empty.Allocate(A, new GoalFrame(other, tile: new CellCoord(1, 1)));
            var refused = allocation.Table.RetargetDestination(allocation.Handle!.Value, new CellCoord(2, 2));
            Assert.AreEqual(GoalRetargetOutcome.NotAWalk, refused.Outcome, $"{other}");
            Assert.AreSame(allocation.Table, refused.Table);
            Assert.AreEqual(new CellCoord(1, 1), refused.Table[allocation.Handle.Value.Slot]!.Bottom.Tile, "the goal's tile is untouched");
        }
    }

    [TestMethod]
    public void AGoalWithoutADescriptorIsRefusedAtEveryDepthOfAStackAndOnAFullTableToo()
    {
        var (table, handle) = Add(GoalRunTable.Empty, A, GoalType.Attack);
        Assert.IsTrue(table.TryGet(handle, out var run));
        Assert.ThrowsExactly<ArgumentException>(() => run.WithFrames([F(GoalType.Attack), F(GoalType.UnlockDoor)]), "on top");
        Assert.ThrowsExactly<ArgumentException>(() => run.WithFrames([F(GoalType.Attack), F(GoalType.PickWeapon), F(GoalType.MoveNear)]), "in the middle");
        Assert.AreEqual(GoalInsertOutcome.NoDescriptor, table.InsertAtBottom(handle, F(GoalType.PickWeapon)).Outcome);
        var full = GoalRunTable.Empty;
        for (var i = 0; i < GoalRunTable.SlotCount; i++) (full, _) = Add(full, A, GoalType.Attack);
        Assert.ThrowsExactly<ArgumentException>(() => full.Allocate(A, F(GoalType.UnlockDoor)), "a full table refuses the argument too, it does not just set the flag");
        var failed = full.Allocate(A, F(GoalType.Attack));
        Assert.IsNull(failed.Handle, "a valid start on a full table fails");
        Assert.IsTrue(failed.Table.ExhaustionPending, "and sets the flag");
        Assert.IsFalse(full.ExhaustionPending, "on a new table");
    }

    [TestMethod]
    public void AGoalFrameKeepsItsChecksWhenItIsCopiedWithAnotherValue()
    {
        var frame = new GoalFrame(GoalType.Attack, "npc:foe");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = frame with { Goal = (GoalType)87 });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = frame with { Goal = (GoalType)(-1) });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = frame with { Range = -1 });
        Assert.AreEqual(GoalType.Follow, (frame with { Goal = GoalType.Follow }).Goal);
        Assert.AreEqual(5, (frame with { Range = 5 }).Range);
        Assert.IsFalse((frame with { Path = default }).Path.IsDefault, "a default path becomes an empty one");
        Assert.AreEqual(0, (frame with { Path = default }).Path.Length);
        Assert.IsFalse(frame.Path.IsDefault);
    }

    [TestMethod]
    public void TheSlotIndexerAndTheSteppingMarkerRefuseASlotOutsideTheTable()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = GoalRunTable.Empty[-1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = GoalRunTable.Empty[GoalRunTable.SlotCount]);
        Assert.IsNull(GoalRunTable.Empty[GoalRunTable.SlotCount - 1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GoalRunTable.Empty.WithStepping(-1));
        Assert.AreEqual(0, GoalRunTable.Empty.WithStepping(0).SteppingSlot, "the marker is a plain slot number: a free slot is accepted");
        Assert.IsNull(GoalRunTable.Empty.WithStepping(0).WithStepping(null).SteppingSlot);
    }

    [TestMethod]
    public void AMapEndSweepMarksTheSteppedRunDoneAndGoesOnToTheOthers()
    {
        var table = GoalRunTable.Empty;
        (table, var stepped) = Add(table, A, GoalType.Attack);
        (table, var other) = Add(table, B, GoalType.Attack);
        var cleared = table.WithStepping(stepped.Slot).ClearAll();
        CollectionAssert.AreEqual(new[] { GoalInterruptOutcome.MarkedDone, GoalInterruptOutcome.Ended }, cleared.Steps.Select(s => s.Outcome).ToArray());
        Assert.IsTrue(cleared.Steps.All(s => s.Succeeded), "a marked run counts as success, so the sweep is not stopped");
        Assert.AreEqual(1, cleared.Table.InUseCount, "the stepper still has to end the marked run");
        Assert.IsTrue(cleared.Table.TryGet(stepped, out var done) && done.Done);
        Assert.IsFalse(cleared.Table.TryGet(other, out _));
    }

    [TestMethod]
    public void TheTableIsAValueSoAnOperationLeavesTheOldTableAsItWas()
    {
        var table = GoalRunTable.Empty;
        (var added, var handle) = Add(table, A, GoalType.Attack);
        Assert.AreEqual(0, table.InUseCount);
        Assert.AreEqual(1, added.InUseCount);
        var ended = added.Interrupt(handle, 6);
        Assert.AreEqual(1, added.InUseCount);
        Assert.AreEqual(0, ended.Table.InUseCount);
        CollectionAssert.AreEqual(new[] { 0 }, Slots(added));
        Assert.AreEqual(ImmutableArray<GoalRun>.Empty.Length, ended.Table.RunsOf(A).Length);
    }
}
