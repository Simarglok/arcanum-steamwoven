using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-132 (MIG13 S3, docs/architecture/goal-table.md §3.2): the goal runs' steps over the world runtime's frame step with a fake host. A pass reports the due
/// steps, the frame step opens each, the host steps it at its deadline with the clocks of that moment, the close queues the next step from now, and one more pass
/// follows; the pause stops the animation clock and with it the steps; with no host a due step ends its run. Entity ids are invented; no original data.
/// </summary>
[TestClass]
public sealed class GoalStepTests
{
    private static GameSessionOwner Create()
    {
        var profile = new ProfileBinding(1, "steamwoven-normalized-clock-v1", "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52",
            new string('a', 64), new string('c', 64), 1885, 15);
        var identity = new GlobalFlagIdentity(new(Guid.Parse("11111111-1111-1111-1111-111111111111")), new(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var flags = new GlobalFlagOwnerDescriptor(identity, new(Guid.Parse("33333333-3333-3333-3333-333333333333")), 0, [new FlagId(3)],
            [new GlobalFlagBootstrapValue(new FlagId(3), false)]);
        var result = GameSessionOwner.Create(new GameSessionDescriptor(profile, flags, new(new(79), new(100, 200), NeighborDirection.XPlus), null));
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private sealed class Calls(GameSessionOwner session) : ISessionScriptWorld
    {
        public List<long> Ran { get; } = [];
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => true;
        public bool RunScriptCall(SessionScriptCall call) { Ran.Add(session.QueryFrameClocks().AnimationMs); return true; }
    }

    private sealed class Ports : IGoalStartPorts
    {
        public bool Loading => false;
        public bool GatePasses(GoalStartRequest request) => true;
        public bool IsDestroyed(string owner) => false;
    }

    // Steps every run `steps` times at its pace (real ms), then ends it; logs the owner and the clocks each step saw.
    private sealed class Host(int steps) : IGoalStepHost
    {
        public List<(string Owner, FrameClocks Clocks)> Ran { get; } = [];
        public Func<GoalRun, SessionGoalStepAnswer?>? Override { get; set; }
        public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
        {
            Ran.Add((run.Owner, clocks));
            if (Override?.Invoke(run) is { } answer) return answer;
            var node = run.Top.Node + 1;
            return node > steps ? SessionGoalStepAnswer.End : SessionGoalStepAnswer.After(run.PaceMs, run.WithFrames([run.Top with { Node = node }]));
        }
    }

    private static GoalHandle Begin(GameSessionOwner session, string owner, int pace, GoalType goal = GoalType.MoveToTile)
    {
        var started = session.StartGoal(new(owner, GoalStarters.Generic, new GoalFrame(goal), PaceMs: pace), new Ports());
        Assert.IsTrue(started.Started, started.Failure + " " + started.Outcome);
        return started.Handle!.Value;
    }

    [TestMethod]
    public void Each_step_runs_at_its_own_deadline_with_the_clocks_of_that_moment_and_queues_the_next_from_now()
    {
        using var session = Create();
        var host = new Host(steps: 100);
        var step = new FrameStep(session, new Calls(session)) { GoalSteps = host };
        step.Run(0);
        var t0 = session.QueryFrameClocks().AnimationMs;
        Begin(session, "npc:a", 10);
        Begin(session, "npc:b", 13);
        var report = step.Run(100);
        long[] Seen(string owner) => host.Ran.Where(r => r.Owner == owner).Select(r => r.Clocks.AnimationMs - t0).ToArray();
        CollectionAssert.AreEqual(new long[] { 5, 85, 165, 245, 325, 405, 485, 565, 645, 725 }, Seen("npc:a"), "5 ms after the start, then 10 real ms x 8");
        CollectionAssert.AreEqual(new long[] { 5, 109, 213, 317, 421, 525, 629, 733 }, Seen("npc:b"));
        var times = host.Ran.Select(r => r.Clocks.AnimationMs).ToArray();
        CollectionAssert.AreEqual(times.OrderBy(t => t).ToArray(), times, "one mechanism steps every run, in deadline order");
        Assert.AreEqual(18, report.GoalSteps.Count);
        Assert.AreEqual(0, report.GoalStepsUnhandled + report.GoalStepsRefused);
        Assert.AreEqual(t0 + 800, report.Clocks.AnimationMs);
        Assert.AreEqual(2, session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void The_frames_split_changes_nothing_in_the_steps()
    {
        long[] Steps(params long[] stamps)
        {
            using var session = Create();
            var host = new Host(steps: 100);
            var step = new FrameStep(session, new Calls(session)) { GoalSteps = host };
            step.Run(0);
            var t0 = session.QueryFrameClocks().AnimationMs;
            Begin(session, "npc:a", 7);
            foreach (var stamp in stamps) step.Run(stamp);
            return host.Ran.Select(r => r.Clocks.AnimationMs - t0).ToArray();
        }
        CollectionAssert.AreEqual(Steps(200), Steps(16, 33, 50, 66, 83, 100, 116, 133, 150, 166, 183, 200));
    }

    [TestMethod]
    public void A_run_ends_with_the_hosts_end_and_a_refused_repeat_ends_its_run()
    {
        using var session = Create();
        var host = new Host(steps: 2);
        var step = new FrameStep(session, new Calls(session)) { GoalSteps = host };
        step.Run(0);
        Begin(session, "npc:a", 10);
        var bad = Begin(session, "npc:b", 10);
        host.Override = run => run.Handle == bad ? SessionGoalStepAnswer.After(0) : null;
        var report = step.Run(100);
        Assert.AreEqual(1, report.GoalStepsRefused, "a delay of 0 is out of range");
        Assert.AreEqual(2, host.Ran.Count(r => r.Owner == "npc:a"), "one step and the one that answered the end");
        Assert.AreEqual(0, session.QueryGoals().InUseCount);
        Assert.IsFalse(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.GoalStep));
    }

    [TestMethod]
    public void A_due_step_with_no_host_ends_its_run()
    {
        using var session = Create();
        var step = new FrameStep(session, new Calls(session));
        step.Run(0);
        Begin(session, "npc:a", 10);
        var report = step.Run(16);
        Assert.AreEqual((0, 1), (report.GoalSteps.Count, report.GoalStepsUnhandled));
        Assert.AreEqual(0, session.QueryGoals().InUseCount, "a run nobody steps would never end");
    }

    [TestMethod]
    public void The_menu_pause_stops_the_steps_with_the_animation_clock()
    {
        using var session = Create();
        var host = new Host(steps: 100);
        var step = new FrameStep(session, new Calls(session)) { GoalSteps = host };
        step.Run(0);
        Begin(session, "npc:a", 10);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        var animation = session.QueryFrameClocks().AnimationMs;
        step.Run(200);
        Assert.AreEqual(0, host.Ran.Count);
        Assert.AreEqual(animation, session.QueryFrameClocks().AnimationMs);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(false, PauseReason.None)).Disposition);
        step.Run(216);
        Assert.AreEqual(2, host.Ran.Count, "the clock goes on where it stopped: 5 ms, then 80 ms of the 128");
    }

    [TestMethod]
    public void What_a_step_makes_due_fires_in_the_same_frame()
    {
        using var session = Create();
        var calls = new Calls(session);
        var host = new Host(steps: 100);
        var step = new FrameStep(session, calls) { GoalSteps = host };
        step.Run(0);
        var t0 = session.QueryFrameClocks().AnimationMs;
        Begin(session, "npc:a", 10);
        var scheduled = session.ScheduleScriptCall(session.Version, new(790, 6, new(new(79), SessionEventKinds.PlayerEntity), null), 500);
        Assert.AreEqual(GameSessionDisposition.Changed, scheduled.Disposition);
        var started = false;
        // The first step jumps the clocks a round's second (a jump moves game and animation time, TL26, TL38) and starts another run (a node may start goals).
        host.Override = run =>
        {
            if (run.Owner != "npc:a" || started) return null;
            started = true;
            Assert.AreEqual(ClockMutationDisposition.Changed, session.JumpClocks(session.QueryClockSnapshot().Token, TurnBasedRules.RoundGameTimeMs).Disposition);
            Begin(session, "npc:b", 10);
            return SessionGoalStepAnswer.After(10);
        };
        var report = step.Run(16);
        Assert.AreEqual(t0 + 5 + TurnBasedRules.RoundGameTimeMs, calls.Ran.Single(), "the call the jump made due fired in the pass after the step");
        Assert.AreEqual(1, report.ScriptCallsRun);
        Assert.IsTrue(host.Ran.Any(r => r.Owner == "npc:b"), "the run the step started got its first step 5 ms later, inside the frame");
    }

    [TestMethod]
    public void A_host_that_throws_ends_every_run_of_the_pass_it_did_not_step()
    {
        using var session = Create();
        var host = new Host(steps: 100);
        var step = new FrameStep(session, new Calls(session)) { GoalSteps = host };
        step.Run(0);
        Begin(session, "npc:a", 10);
        Begin(session, "npc:b", 10);
        Begin(session, "npc:c", 10);
        host.Override = run => run.Owner == "npc:a" ? throw new InvalidOperationException("host") : null;
        Assert.ThrowsExactly<InvalidOperationException>(() => step.Run(16));
        Assert.AreEqual(1, host.Ran.Count, "the throw stopped the pass's steps");
        Assert.AreEqual(0, session.QueryGoals().InUseCount, "the runs due in the same pass end too: none waits for a step that will not come");
        Assert.IsFalse(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.GoalStep));
    }

    [TestMethod]
    public void A_goal_step_fires_whatever_the_worlds_existence_test_answers_for_its_owner()
    {
        // The kind checks its own owner (the run's handle and owner); the world's existence test still drops the other kinds' events.
        using var session = Create();
        var host = new Host(steps: 100);
        var step = new FrameStep(session, new Gone()) { GoalSteps = host };
        step.Run(0);
        Begin(session, "npc:a", 10);
        var scheduled = session.ScheduleScriptCall(session.Version, new(790, 6, new(new(79), "oid:gone"), null), 8);
        Assert.AreEqual(GameSessionDisposition.Changed, scheduled.Disposition);
        var report = step.Run(16);
        Assert.AreEqual(2, host.Ran.Count);
        Assert.AreEqual(1, report.Dropped, "the deferred call naming a gone object is still dropped");
    }

    private sealed class Gone : ISessionScriptWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => false;
        public bool RunScriptCall(SessionScriptCall call) => true;
    }

    [TestMethod]
    public void A_host_that_throws_leaves_no_open_step()
    {
        using var session = Create();
        var host = new Host(steps: 100) { Override = _ => throw new InvalidOperationException("host") };
        var step = new FrameStep(session, new Calls(session)) { GoalSteps = host };
        step.Run(0);
        var handle = Begin(session, "npc:a", 10);
        Assert.ThrowsExactly<InvalidOperationException>(() => step.Run(16));
        Assert.AreEqual(0, session.QueryGoals().InUseCount);
        Assert.IsNull(session.QueryGoals().SteppingSlot);
        Assert.IsNull(session.BeginGoalStep(handle), "the run is gone, and no step is held open");
        Begin(session, "npc:a", 10);
    }
}
