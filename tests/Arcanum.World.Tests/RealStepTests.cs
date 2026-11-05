using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// MIG12 S8 (ARC-DEV-127, docs/architecture/clocks-frame.md §3.5): the real-time steps over the world runtime's frame step. A journey step falls
/// due 50 ms after it is scheduled and then every 62 ms of real time, each step with its time jump (TL26, TL28); the jump is seen by the game
/// list in the same frame (TL20); real time runs behind the pause (TL24); the frames' split changes nothing. Entity ids and scripts are invented;
/// no original data.
/// </summary>
[TestClass]
public sealed class RealStepTests
{
    private const long Start = 54_000_000;
    private const long Hour = 3_600_000;

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
        public bool RunScriptCall(SessionScriptCall call) { Ran.Add(session.QueryFrameClocks().GameMs - Start); return true; }
    }

    // A journey: `jumps` steps, each 1 game hour, then the chain ends. Logs the clocks each step saw.
    private sealed class Journey(GameSessionOwner session, int steps, long hourMs = Hour) : IRealStepHost
    {
        public List<(SessionRealStep Step, FrameClocks Clocks)> Ran { get; } = [];
        public long? RunRealStep(SessionRealStep step, FrameClocks clocks)
        {
            Ran.Add((step, clocks));
            if (step != SessionRealStep.WorldMapStep) return null;
            Assert.AreEqual(ClockMutationDisposition.Changed, session.JumpClocks(session.QueryClockSnapshot().Token, hourMs).Disposition);
            return Ran.Count(r => r.Step == SessionRealStep.WorldMapStep) < steps ? 62 : null;
        }
    }

    private static void Call(GameSessionOwner session, long delayMs)
    {
        var scheduled = session.ScheduleScriptCall(session.Version, new(790, 6, new(new(79), SessionEventKinds.PlayerEntity), null), delayMs);
        Assert.AreEqual(GameSessionDisposition.Changed, scheduled.Disposition, scheduled.Failure.ToString());
    }

    [TestMethod]
    public void A_journey_steps_fall_due_at_50_then_every_62_real_ms_each_with_its_time_jump()
    {
        using var session = Create();
        var world = new Calls(session);
        var journey = new Journey(session, 3);
        var step = new FrameStep(session, world) { RealSteps = journey };
        step.Run(0);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleRealStep(SessionRealStep.WorldMapStep, 50));
        var report = step.Run(250);
        CollectionAssert.AreEqual(new[] { 51L, 113, 175 }, journey.Ran.Select(r => r.Clocks.RealMs).ToArray(), "50 ms after the start, then 62 ms of real time");
        CollectionAssert.AreEqual(new[] { SessionRealStep.WorldMapStep, SessionRealStep.WorldMapStep, SessionRealStep.WorldMapStep }, report.RealSteps.ToArray());
        // Each step saw the time of the steps before it: its own hour on top of the admitted game time (8 game ms to a real ms).
        CollectionAssert.AreEqual(new[] { 0L, Hour, 2 * Hour }, journey.Ran.Select((r, i) => r.Clocks.GameMs - Start - (r.Clocks.RealMs - 1) * 8).ToArray());
        Assert.AreEqual(Start + 250 * 8 + 3 * Hour, session.QueryClockSnapshot().GameTimestampMs);
        Assert.AreEqual(Start + 250 * 8 + 3 * Hour, report.Clocks.AnimationMs, "the animation clock takes every jump with the game clock");
        Assert.AreEqual(250L, report.Clocks.WorldMs, "a jump does not fast-forward the walks and flights that read world time");
        Assert.IsNull(session.QueryRealStep(SessionRealStep.WorldMapStep), "the chain ended with the host's null");
        Assert.IsTrue(report.HourChanged);
        Assert.AreEqual(0, report.RealStepsUnhandled);
    }

    [TestMethod]
    public void The_events_a_jump_made_due_fire_in_the_same_frame_after_the_step()
    {
        using var session = Create();
        var world = new Calls(session);
        // Due after one and a half hours: the second step's jump carries the clock past it.
        Call(session, Hour + Hour / 2);
        var step = new FrameStep(session, world) { RealSteps = new Journey(session, 3) };
        step.Run(0);
        session.ScheduleRealStep(SessionRealStep.WorldMapStep, 50);
        var report = step.Run(250);
        Assert.AreEqual(1, report.ScriptCallsRun);
        var at = world.Ran.Single();
        Assert.IsTrue(at >= 2 * Hour, $"it fired against the clock the second jump left, not at its own deadline: {at}");
        Assert.IsTrue(report.Passes >= 2, "a pass followed the step that jumped");
    }

    [TestMethod]
    public void The_frames_split_changes_nothing_in_the_journey()
    {
        var traces = new List<string>();
        foreach (var frames in new[] { new long[] { 250 }, new long[] { 5, 5, 5, 5, 230 }, new long[] { 100, 100, 50 }, new long[] { 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 12 } })
        {
            using var session = Create();
            var journey = new Journey(session, 3);
            var step = new FrameStep(session, new Calls(session)) { RealSteps = journey };
            step.Run(0);
            session.ScheduleRealStep(SessionRealStep.WorldMapStep, 50);
            var host = 0L;
            foreach (var frame in frames) step.Run(host += frame);
            traces.Add(string.Join(",", journey.Ran.Select(r => r.Clocks.RealMs)) + " @" + (session.QueryClockSnapshot().GameTimestampMs - Start) + "/" + session.QueryFrameClocks().RealMs);
        }
        Assert.AreEqual(1, traces.Distinct().Count(), string.Join(" | ", traces));
        Assert.AreEqual("51,113,175 @" + (250 * 8 + 3 * Hour) + "/251", traces[0]);
    }

    [TestMethod]
    public void A_step_falls_due_behind_the_menu_pause_and_only_its_jump_moves_game_time()
    {
        using var session = Create();
        var journey = new Journey(session, 2);
        var step = new FrameStep(session, new Calls(session)) { RealSteps = journey };
        step.Run(0);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        session.ScheduleRealStep(SessionRealStep.WorldMapStep, 50);
        var report = step.Run(250);
        CollectionAssert.AreEqual(new[] { 51L, 113 }, journey.Ran.Select(r => r.Clocks.RealMs).ToArray(), "real time ran behind the pause");
        Assert.AreEqual(Start + 2 * Hour, session.QueryClockSnapshot().GameTimestampMs, "the pause stopped the admitted game time, the jumps ignore it (TL26)");
        Assert.AreEqual(0L, report.Clocks.WorldMs, "world time stood still with the pause");
        Assert.AreEqual(1 + 250L, report.Clocks.RealMs);
    }

    [TestMethod]
    public void The_turn_based_event_is_run_by_the_host_and_asks_for_no_repeat()
    {
        using var session = Create();
        var journey = new Journey(session, 1);
        var step = new FrameStep(session, new Calls(session)) { RealSteps = journey };
        step.Run(0);
        session.ScheduleRealStep(SessionRealStep.TurnBasedStep, 2);
        var report = step.Run(10);
        Assert.AreEqual(1, journey.Ran.Count);
        Assert.AreEqual((SessionRealStep.TurnBasedStep, 3L), (journey.Ran[0].Step, journey.Ran[0].Clocks.RealMs), "2 ms after it was asked for");
        Assert.IsNull(session.QueryRealStep(SessionRealStep.TurnBasedStep), "the host decides to be asked for again, as the original's callback does");
        Assert.AreEqual(Start + 80, session.QueryClockSnapshot().GameTimestampMs, "the step made no jump");
        CollectionAssert.AreEqual(new[] { SessionRealStep.TurnBasedStep }, report.RealSteps.ToArray());
    }

    [TestMethod]
    public void A_step_with_no_host_is_consumed_unrun_and_a_bad_repeat_delay_ends_the_chain_without_losing_the_others()
    {
        using var session = Create();
        var step = new FrameStep(session, new Calls(session));
        step.Run(0);
        session.ScheduleRealStep(SessionRealStep.WorldMapStep, 10);
        var report = step.Run(50);
        Assert.AreEqual((0, 1), (report.RealSteps.Count, report.RealStepsUnhandled));
        Assert.IsNull(session.QueryRealStep(SessionRealStep.WorldMapStep));

        session.ScheduleRealStep(SessionRealStep.WorldMapStep, 10);
        step.RealSteps = new DelayHost(0);
        var refused = step.Run(100);
        Assert.AreEqual((1, 1), (refused.RealSteps.Count, refused.RealStepsRefused), "the refused repeat ends the chain and is reported");
        Assert.IsNull(session.QueryRealStep(SessionRealStep.WorldMapStep));
        // The other steps of the pass still run after a refused repeat.
        session.ScheduleRealStep(SessionRealStep.WorldMapStep, 10);
        session.ScheduleRealStep(SessionRealStep.TurnBasedStep, 10);
        step.RealSteps = new DelayHost(GameSessionOwner.MaxRealStepDelayMs + 1);
        var both = step.Run(150);
        Assert.AreEqual((2, 2), (both.RealSteps.Count, both.RealStepsRefused));
    }

    private sealed class DelayHost(long delay) : IRealStepHost
    {
        public long? RunRealStep(SessionRealStep step, FrameClocks clocks) => delay;
    }

    [TestMethod]
    public void A_frozen_frame_runs_a_step_that_was_already_due_and_a_jump_run_passes_too()
    {
        using var session = Create();
        var journey = new Journey(session, 1);
        var step = new FrameStep(session, new Calls(session)) { RealSteps = journey };
        step.Run(0);
        step.Run(100);
        session.ScheduleRealStep(SessionRealStep.TurnBasedStep, 1);
        // Nothing moves in a frozen frame, so a step 1 ms ahead is not due; a real frame after it makes it due.
        step.Run(5_000, frozen: true);
        Assert.AreEqual(0, journey.Ran.Count);
        step.Run(5_010);
        Assert.AreEqual(1, journey.Ran.Count);
        // A jump runs the passes too: a step due by then (none: real time did not move) stays pending.
        session.ScheduleRealStep(SessionRealStep.TurnBasedStep, 1);
        step.Jump(Hour);
        Assert.AreEqual(1, journey.Ran.Count, "a jump leaves real time alone");
        Assert.IsNotNull(session.QueryRealStep(SessionRealStep.TurnBasedStep));
    }
}
