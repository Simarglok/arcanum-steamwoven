using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-132 (MIG13 S3, docs/architecture/goal-table.md §3.1, §3.2): the session owns the goal table and the <c>goal.step</c> kind on the animation domain. A start
/// queues the first step 5 ms of animation time ahead (GS7), a pass reports a step at its deadline, a step's close queues the next one its delay ×8 from the clock now
/// (TL40, TL22), every end removes the run's step by slot and id, a map change ends every run (GS19), nothing is saved, and a jump of game and animation time (the
/// round end's second, TL38) makes the steps due inside it fire in the next pass. Entity ids are invented; no original data.
/// </summary>
[TestClass]
public sealed class GoalTableStateTests
{
    private const string A = "npc:a";
    private const string B = "npc:b";

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class Ports : IGoalStartPorts
    {
        public bool Gate { get; set; } = true;
        public bool Loading { get; set; }
        public bool GatePasses(GoalStartRequest request) => Gate;
        public bool IsDestroyed(string owner) => false;
    }

    private static GoalStartRequest Request(GoalType goal, string owner = A, GoalStartPlan? plan = null, int pace = 100) =>
        new(owner, plan ?? GoalStarters.Generic, new GoalFrame(goal), PaceMs: pace);

    private static GoalHandle Started(GameSessionOwner owner, GoalStartRequest request)
    {
        var result = owner.StartGoal(request, new Ports());
        Assert.AreEqual(SessionGoalFailure.None, result.Failure);
        Assert.IsTrue(result.Started, result.Outcome?.ToString());
        return result.Handle!.Value;
    }

    private static long Animation(GameSessionOwner owner) => owner.QueryFrameClocks().AnimationMs;

    private static void Jump(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, ms).Disposition);

    private static GameEvent[] Steps(GameSessionOwner owner) => owner.QueryEvents().Where(e => e.Kind == SessionEventKinds.GoalStep).ToArray();

    [TestMethod]
    public void A_start_queues_the_first_step_5_ms_of_animation_time_ahead_on_the_owners_map()
    {
        using var owner = Create();
        var now = Animation(owner);
        var handle = Started(owner, Request(GoalType.MoveToTile));
        Assert.AreEqual(new GoalHandle(0, 1), handle, "the lowest free slot and the first run id");
        var step = Steps(owner).Single();
        Assert.AreEqual(GameEventDomain.Animation, step.Domain);
        Assert.AreEqual(now + GameSessionOwner.FirstGoalStepDelayMs, step.DeadlineMs, "0x44D730 queues {0, 5} with no conversion");
        Assert.AreEqual(Exterior.RuntimeMap, step.ScopeMap);
        Assert.AreEqual((Exterior.RuntimeMap, A), (step.Payload.Entities[0].Map, step.Payload.Entities[0].Code));
        CollectionAssert.AreEqual(new[] { 0L, 1, now }, step.Payload.Values.ToArray(), "the slot, the run id and the time it was queued at");
        Assert.AreEqual(step.DeadlineMs, owner.QueryGoalStep(handle));
        Assert.AreEqual(step.DeadlineMs, owner.QueryGoals()[0]!.DueMs, "the run records its due time");
        Assert.AreEqual(1, owner.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void A_pass_reports_a_step_at_its_deadline_and_not_before()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.MoveToTile));
        var due = owner.QueryGoalStep(handle)!.Value;
        Jump(owner, 4);
        Assert.AreEqual(0, owner.ProcessEvents(new NoWorld()).GoalSteps.Count, "4 ms of the 5 have passed");
        Jump(owner, 1);
        var pass = owner.ProcessEvents(new NoWorld());
        var fired = pass.GoalSteps.Single();
        Assert.AreEqual((handle, A, due), (fired.Handle, fired.Owner, fired.NowMs), "a step due exactly now fires; the owner exists while its run does");
        Assert.IsNull(owner.QueryGoalStep(handle), "the event left the queue; the run waits for its step");
        Assert.AreEqual(1, owner.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void A_closed_step_queues_the_next_one_its_delay_times_eight_from_the_clock_now()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.MoveToTile));
        Jump(owner, 500);
        owner.ProcessEvents(new NoWorld());
        var run = owner.BeginGoalStep(handle)!;
        var now = Animation(owner);
        var stepped = run.WithFrames([run.Bottom with { Node = 3 }]);
        var closed = owner.EndGoalStep(handle, SessionGoalStepAnswer.After(100, stepped));
        Assert.AreEqual((SessionGoalFailure.None, SessionGoalStepOutcome.Continued), (closed.Failure, closed.Outcome));
        Assert.AreEqual(now + 100 * GameSessionOwner.GoalStepClockRate, closed.NextDueMs,
            "relative to the clock at the step, not to the missed due time (no -animcatchup): the late step is not replayed");
        Assert.AreEqual(3, owner.QueryGoals()[0]!.Top.Node, "the stepped run replaced the table's");
        Assert.AreEqual(closed.NextDueMs, owner.QueryGoals()[0]!.DueMs);
        Assert.AreEqual(closed.NextDueMs, owner.QueryGoalStep(handle));
        Assert.IsNull(owner.QueryGoals().SteppingSlot);
    }

    [TestMethod]
    public void An_interrupt_during_a_step_only_marks_the_run_and_the_close_ends_it()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.MoveToTile));
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        Assert.IsNotNull(owner.BeginGoalStep(handle));
        Assert.IsNull(owner.BeginGoalStep(handle), "one step is open at a time");
        var interrupted = owner.InterruptGoal(handle, 3);
        Assert.AreEqual(GoalInterruptOutcome.MarkedDone, interrupted.Steps.Single().Outcome);
        Assert.AreEqual(1, owner.QueryGoals().InUseCount, "the stepped run stays until its step closes");
        Assert.AreEqual(SessionGoalFailure.NotStepping, owner.EndGoalStep(new GoalHandle(1, 9), SessionGoalStepAnswer.End).Failure, "only the open step closes");
        var closed = owner.EndGoalStep(handle, SessionGoalStepAnswer.After(100));
        Assert.AreEqual(SessionGoalStepOutcome.Ended, closed.Outcome, "a marked run ends whatever the host answers");
        Assert.AreEqual(GoalEndKind.Interrupted, closed.Ended!.Kind);
        CollectionAssert.AreEqual(new[] { GoalType.MoveToTile }, closed.Ended.EndNodeGoals.ToArray());
        Assert.AreEqual(0, owner.QueryGoals().InUseCount);
        Assert.AreEqual(0, Steps(owner).Length, "no step was queued for it");
    }

    [TestMethod]
    public void The_hosts_end_and_a_refused_repeat_both_free_the_run()
    {
        using var owner = Create();
        var first = Started(owner, Request(GoalType.MoveToTile));
        var second = Started(owner, Request(GoalType.MoveToTile, B));
        Jump(owner, 5);
        Assert.AreEqual(2, owner.ProcessEvents(new NoWorld()).GoalSteps.Count);
        owner.BeginGoalStep(first);
        var ended = owner.EndGoalStep(first, SessionGoalStepAnswer.End);
        Assert.AreEqual((SessionGoalStepOutcome.Ended, GoalEndKind.Finished), (ended.Outcome, ended.Ended!.Kind));
        Assert.AreEqual(0, ended.Ended.EndNodeGoals.Length, "the stepper ran the end nodes of what it popped");
        owner.BeginGoalStep(second);
        var refused = owner.EndGoalStep(second, SessionGoalStepAnswer.After(GameSessionOwner.MaxGoalStepDelayMs + 1));
        Assert.AreEqual(SessionGoalStepOutcome.Refused, refused.Outcome);
        CollectionAssert.AreEqual(new[] { GoalType.MoveToTile }, refused.Ended!.EndNodeGoals.ToArray(), "the goals still on the stack were never popped: their end nodes are due");
        Assert.AreEqual(0, owner.QueryGoals().InUseCount, "no run is left without a step");
        Assert.AreEqual(0, Steps(owner).Length);
    }

    [TestMethod]
    public void A_run_answered_with_another_handle_is_refused_and_ended()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.MoveToTile));
        var other = Started(owner, Request(GoalType.MoveToTile, B));
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        owner.BeginGoalStep(handle);
        var closed = owner.EndGoalStep(handle, SessionGoalStepAnswer.After(100, owner.QueryGoals()[other.Slot]));
        Assert.AreEqual(SessionGoalStepOutcome.Refused, closed.Outcome);
        Assert.AreEqual(handle, closed.Ended!.Run.Handle, "the stepped run ends, not the one the host named");
        Assert.IsTrue(owner.QueryGoals().TryGet(other, out _));
    }

    [TestMethod]
    public void Every_end_removes_the_runs_step_by_slot_and_run_id()
    {
        using var owner = Create();
        var walk = Started(owner, Request(GoalType.MoveToTile));
        // The dying goal starts at its own priority 5 (Generic): the walk of the same owner ends and its step goes.
        var dying = owner.StartGoal(Request(GoalType.Dying), new Ports());
        Assert.IsTrue(dying.Started);
        Assert.AreEqual(walk, dying.Ended.Single().Run.Handle);
        Assert.AreEqual(new GoalHandle(0, 2), dying.Handle, "the freed slot is reused with the next id");
        var step = Steps(owner).Single();
        CollectionAssert.AreEqual(new[] { 0L, 2 }, step.Payload.Values.Take(2).ToArray(), "only the new run's step is pending");
        Assert.IsNull(owner.QueryGoalStep(walk), "the old handle names no step though its slot does");
        Assert.AreEqual(GoalInterruptOutcome.NotFound, owner.KillGoal(walk).Steps.Single().Outcome, "the old handle names no run either");
        Assert.AreEqual(1, Steps(owner).Length, "and its kill leaves the new run's step in the same slot");

        var candy = Started(owner, Request(GoalType.EyeCandy, B, GoalStarters.NoInterrupt));
        Assert.AreEqual(2, Steps(owner).Length);
        var killed = owner.KillGoals(B);
        Assert.AreEqual(GoalEndKind.Killed, killed.Ended.Single().Kind);
        Assert.IsNull(owner.QueryGoalStep(candy));
        var stopped = owner.InterruptGoals(A, GoalInterruptRules.MaxLevel);
        Assert.IsTrue(stopped.Succeeded);
        Assert.AreEqual(0, Steps(owner).Length);
        Assert.AreEqual(0, owner.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void A_refused_interrupt_keeps_the_run_and_its_step()
    {
        using var owner = Create();
        var dying = Started(owner, Request(GoalType.Dying));
        var refused = owner.InterruptGoal(dying, 3);
        Assert.AreEqual(GoalInterruptOutcome.Refused, refused.Steps.Single().Outcome, "priority 5 refuses level 3");
        Assert.IsFalse(refused.Succeeded);
        Assert.IsNotNull(owner.QueryGoalStep(dying));
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.InterruptGoal(dying, 7).Failure);
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.InterruptGoals("bad owner", 3).Failure);
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.StartGoal(Request(GoalType.MoveToTile, "bad owner"), new Ports()).Failure);
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.StartGoal(Request(GoalType.MoveToTile, pace: 0), new Ports()).Failure);
        var gated = owner.StartGoal(Request(GoalType.MoveToTile, B), new Ports { Gate = false });
        Assert.AreEqual((SessionGoalFailure.None, GoalStartOutcome.Gate, (GoalHandle?)null), (gated.Failure, gated.Outcome, gated.Handle));
        Assert.AreEqual(1, Steps(owner).Length);
    }

    [TestMethod]
    public void A_map_change_ends_every_run_at_level_6_and_removes_their_steps()
    {
        using var owner = Create();
        Started(owner, Request(GoalType.Dying));
        Started(owner, Request(GoalType.EyeCandy, B, GoalStarters.NoInterrupt));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start with { Cell = new(101, 200) }).Disposition);
        Assert.AreEqual(2, owner.QueryGoals().InUseCount, "a move on the same map ends nothing");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(0, owner.QueryGoals().InUseCount, "level 6 ends even dying and the no-interrupt eye candy (GS19)");
        Assert.AreEqual(0, Steps(owner).Length, "no goal step is parked for the map left");
        var cave = Started(owner, Request(GoalType.MoveToTile));
        Assert.AreEqual(Cave.RuntimeMap, Steps(owner).Single().ScopeMap, "a run is on the active map");
        Assert.IsNotNull(owner.QueryGoalStep(cave));
    }

    [TestMethod]
    public void A_map_change_during_a_step_marks_the_stepped_run_and_its_close_ends_it()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.MoveToTile));
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        owner.BeginGoalStep(handle);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(1, owner.QueryGoals().InUseCount);
        var closed = owner.EndGoalStep(handle, SessionGoalStepAnswer.After(100));
        Assert.AreEqual((SessionGoalStepOutcome.Ended, GoalEndKind.Interrupted), (closed.Outcome, closed.Ended!.Kind));
        Assert.AreEqual(0, Steps(owner).Length, "the run does not carry its step onto the new map");
    }

    [TestMethod]
    public void A_jump_of_game_and_animation_time_fires_the_steps_due_inside_it_once()
    {
        // TL38, read again (round end 0x4B7740 → jump 0x45C200 of 1,000 ms onto the game and the animation clock): the round's second is a jump, so the steps due
        // within it fire at the next pass, each once.
        using var owner = Create();
        var near = Started(owner, Request(GoalType.MoveToTile));
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        owner.BeginGoalStep(near);
        owner.EndGoalStep(near, SessionGoalStepAnswer.After(100));      // due 800 ms of animation time from now
        var far = Started(owner, Request(GoalType.MoveToTile, B));
        owner.BeginGoalStep(far); // not due: the session lets a caller step a run early; its close re-queues it from now
        owner.EndGoalStep(far, SessionGoalStepAnswer.After(200));       // due 1,600 ms from now
        var game = owner.QueryClockSnapshot().GameTimestampMs;
        Jump(owner, TurnBasedRules.RoundGameTimeMs);
        Assert.AreEqual(game + TurnBasedRules.RoundGameTimeMs, owner.QueryClockSnapshot().GameTimestampMs);
        var pass = owner.ProcessEvents(new NoWorld());
        Assert.AreEqual(near, pass.GoalSteps.Single().Handle, "the step due within the second fires once; the later one waits");
        Assert.IsNotNull(owner.QueryGoalStep(far));
    }

    [TestMethod]
    public void The_table_and_the_steps_are_not_saved_and_the_schema_does_not_change()
    {
        using var owner = Create();
        var before = GameSessionSnapshotCodec.Encode(owner.Capture());
        Started(owner, Request(GoalType.MoveToTile));
        Started(owner, Request(GoalType.EyeCandy, B, GoalStarters.NoInterrupt));
        var capture = owner.Capture();
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, capture.SchemaVersion);
        Assert.AreEqual(0, capture.Events.Pending.Count);
        var text = GameSessionSnapshotCodec.Encode(capture);
        // The queue's sequence counter is saved and counted the unsaved steps too (as the real-time steps do); nothing else differs.
        static string Unsequenced(string snapshot) => System.Text.RegularExpressions.Regex.Replace(snapshot, "\"nextSequence\":\\d+", "");
        Assert.AreEqual(Unsequenced(before), Unsequenced(text), "a session with runs saves as it did without them");
        StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("goal\\.step"));
        using var restored = Restore(Decode(text));
        Assert.AreEqual(0, restored.QueryGoals().InUseCount, "a load starts with no run");
        Assert.AreEqual(0, Steps(restored).Length);
    }

    [TestMethod]
    public void A_full_table_refuses_a_start_and_sets_the_exhaustion_flag()
    {
        using var owner = Create();
        for (var i = 0; i < GoalRunTable.SlotCount; i++) Started(owner, Request(GoalType.EyeCandy, $"obj:{i}", GoalStarters.NoInterrupt));
        Assert.AreEqual(GoalRunTable.SlotCount, Steps(owner).Length, "one step per slot fits the queue");
        var full = owner.StartGoal(Request(GoalType.EyeCandy, "obj:x", GoalStarters.NoInterrupt), new Ports());
        Assert.AreEqual(GoalStartOutcome.TableFull, full.Outcome);
        Assert.IsTrue(owner.QueryGoals().ExhaustionPending, "the stepper's next step sweeps (S5)");
    }

    [TestMethod]
    public void A_run_whose_step_went_missing_is_killed_before_the_next_pass_and_reported()
    {
        using var owner = Create();
        var lost = Started(owner, Request(GoalType.MoveToTile));
        var kept = Started(owner, Request(GoalType.MoveToTile, B));
        owner.LoseGoalStepForTests(lost);
        var pass = owner.ProcessEvents(new NoWorld());
        Assert.AreEqual(lost, pass.GoalsReconciled.Single().Run.Handle, "no run waits for a step that will not come; S5 settles its end nodes");
        Assert.AreEqual(GoalEndKind.Killed, pass.GoalsReconciled[0].Kind);
        Assert.IsFalse(owner.QueryGoals().TryGet(lost, out _));
        Assert.IsNotNull(owner.QueryGoalStep(kept));
    }

    [TestMethod]
    public void A_pass_between_keeps_the_runs_an_earlier_pass_reported_for_their_steps()
    {
        // A host may run a pass (a jump through the frame step) while the frame step still holds the steps an earlier pass reported.
        using var owner = Create();
        var first = Started(owner, Request(GoalType.MoveToTile));
        var second = Started(owner, Request(GoalType.MoveToTile, B));
        Jump(owner, 5);
        Assert.AreEqual(2, owner.ProcessEvents(new NoWorld()).GoalSteps.Count);
        Assert.IsNotNull(owner.BeginGoalStep(first));
        Assert.AreEqual(0, owner.ProcessEvents(new NoWorld()).GoalsReconciled.Count, "neither the open step's run nor the reported one is lost");
        Assert.AreEqual(SessionGoalStepOutcome.Continued, owner.EndGoalStep(first, SessionGoalStepAnswer.After(100)).Outcome);
        Assert.IsNotNull(owner.BeginGoalStep(second), "the reported run is still there to step");
        Assert.AreEqual(SessionGoalStepOutcome.Continued, owner.EndGoalStep(second, SessionGoalStepAnswer.After(100)).Outcome);
        Assert.AreEqual(2, owner.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void An_abandoned_step_ends_its_run_with_its_stacks_end_nodes()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.MoveToTile));
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        owner.BeginGoalStep(handle);
        var closed = owner.EndGoalStep(handle, SessionGoalStepAnswer.Abandon);
        Assert.AreEqual((SessionGoalStepOutcome.Refused, GoalEndKind.Finished), (closed.Outcome, closed.Ended!.Kind));
        CollectionAssert.AreEqual(new[] { GoalType.MoveToTile }, closed.Ended.EndNodeGoals.ToArray());
        Assert.AreEqual(0, owner.QueryGoals().InUseCount);
    }

    // ARC-DEV-146 (MIG13 S10b, GS14, GT14): a walk's new destination overwrites the old in place: the same run, its replan flag set, its pending step kept.
    [TestMethod]
    public void A_running_walks_destination_is_changed_in_place_and_the_change_is_refused_where_the_table_would_refuse_it()
    {
        using var owner = Create();
        var handle = Started(owner, new GoalStartRequest("pc", GoalStarters.Walk, new GoalFrame(GoalType.MoveToTile, tile: new CellCoord(3, 4))));
        var due = owner.QueryGoalStep(handle);
        Assert.AreEqual(GoalRetargetOutcome.Unchanged, owner.RetargetGoal(handle, new CellCoord(3, 4)).Outcome, "the same destination: no change");
        Assert.IsTrue(owner.QueryGoals().TryGet(handle, out var same));
        Assert.AreEqual(GoalPathFlags.None, same.PathFlags & GoalPathFlags.Replan);
        var moved = owner.RetargetGoal(handle, new CellCoord(9, 1));
        Assert.IsTrue(moved.Accepted);
        Assert.AreEqual(GoalRetargetOutcome.Retargeted, moved.Outcome);
        Assert.IsTrue(owner.QueryGoals().TryGet(handle, out var run));
        Assert.AreEqual(new CellCoord(9, 1), run.Bottom.Tile);
        Assert.AreEqual(GoalPathFlags.Replan, run.PathFlags & GoalPathFlags.Replan, "the walk plans again");
        Assert.AreEqual(handle, run.Handle, "the same run");
        Assert.AreEqual(due, owner.QueryGoalStep(handle), "the pending step is kept");

        Assert.AreEqual(SessionGoalFailure.NoRun, owner.RetargetGoal(new GoalHandle(handle.Slot, handle.RunId + 1), new CellCoord(1, 1)).Failure);
        var attack = Started(owner, new GoalStartRequest(A, GoalStarters.Generic, new GoalFrame(GoalType.Attack, "npc:b")));
        var notAWalk = owner.RetargetGoal(attack, new CellCoord(1, 1));
        Assert.IsFalse(notAWalk.Accepted);
        Assert.AreEqual(GoalRetargetOutcome.NotAWalk, notAWalk.Outcome);

        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        Assert.IsNotNull(owner.BeginGoalStep(handle));
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.RetargetGoal(handle, new CellCoord(2, 2)).Failure, "the stepped run's answer would overwrite it");
    }

    // ARC-DEV-134 (MIG13 S4): the AI host's queued cast replaces the stack of a run that is not being stepped; its step stays.
    [TestMethod]
    public void A_runs_stack_is_replaced_only_outside_its_step_and_only_with_a_stack_a_run_can_hold()
    {
        using var owner = Create();
        var handle = Started(owner, Request(GoalType.CastSpell));
        var due = owner.QueryGoalStep(handle);
        var queued = new GoalFrame(GoalType.CastSpell) { SpellRun = 3 };
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle, [queued, new GoalFrame(GoalType.AttemptSpell)]));
        Assert.IsTrue(owner.QueryGoals().TryGet(handle, out var run));
        CollectionAssert.AreEqual(new[] { GoalType.CastSpell, GoalType.AttemptSpell }, run.Frames.Select(f => f.Goal).ToArray());
        Assert.AreEqual(3, run.Bottom.SpellRun);
        Assert.AreEqual(due, owner.QueryGoalStep(handle), "the pending step is kept");

        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.ReplaceGoalFrames(handle, []));
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.ReplaceGoalFrames(handle, [.. Enumerable.Repeat(queued, GoalRun.MaxFrames + 1)]));
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.ReplaceGoalFrames(handle, [new GoalFrame((GoalType)13)]), "goal 13 has no descriptor");
        Assert.AreEqual(SessionGoalFailure.NoRun, owner.ReplaceGoalFrames(new GoalHandle(handle.Slot, handle.RunId + 1), [queued]));

        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        Assert.IsNotNull(owner.BeginGoalStep(handle));
        Assert.AreEqual(SessionGoalFailure.InvalidRequest, owner.ReplaceGoalFrames(handle, [queued]), "the stepped run's answer would overwrite it");
        Assert.AreEqual(SessionGoalStepOutcome.Continued, owner.EndGoalStep(handle, SessionGoalStepAnswer.After(10)).Outcome);
        Assert.AreEqual(SessionGoalFailure.None, owner.ReplaceGoalFrames(handle, [queued]));
    }

    // ARC-DEV-134 (MIG13 S4): the runs the session ends by itself are reported once for their owner's host to run its end nodes; the caller's own ends are not.
    [TestMethod]
    public void The_runs_the_session_ends_by_itself_are_drained_once_and_the_callers_ends_are_not()
    {
        using var owner = Create();
        var lost = Started(owner, Request(GoalType.MoveToTile));
        var interrupted = Started(owner, Request(GoalType.MoveToTile, B));
        Assert.IsTrue(owner.InterruptGoal(interrupted, GoalInterruptRules.MaxLevel).Succeeded);
        owner.LoseGoalStepForTests(lost);
        owner.ProcessEvents(new NoWorld());
        var drained = owner.DrainGoalEnds();
        Assert.AreEqual(lost, drained.Single().Run.Handle, "the reconcile's kill is unclaimed; the caller's interrupt is the caller's");
        Assert.AreEqual(0, owner.DrainGoalEnds().Length, "drained once");
    }
}
