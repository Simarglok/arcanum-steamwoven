using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// MIG12 S4 (ARC-DEV-123, TM6, TM11): the world runtime's frame step over a synthetic session. One host stamp a frame, the frame divided at
/// the event deadlines with the three clocks moving together, the participants after the passes in a fixed order, the change-of-place
/// ping first, the jump, the pause and the typed bounds. Entity ids and scripts are invented; no original data.
/// </summary>
[TestClass]
public sealed class FrameStepTests
{
    private const long Start = 54_000_000;

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
        public List<(long At, SessionScriptCall Call)> Ran { get; } = [];
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => true;
        public bool RunScriptCall(SessionScriptCall call)
        {
            Ran.Add((session.QueryFrameClocks().GameMs, call));
            return true;
        }
    }

    private sealed class Recorder(string name, List<string> log, GameSessionOwner session) : IFrameParticipant
    {
        public readonly List<(FrameClocks Clocks, FrameFlags Flags)> Seen = [];
        public void Step(FrameClocks clocks, FrameFlags flags)
        {
            Seen.Add((clocks, flags));
            log.Add($"{name}@{clocks.GameMs - Start}");
            Assert.AreEqual(session.QueryFrameClocks(), clocks, "a participant reads the clocks of the frame, once");
        }
    }

    private static void Call(GameSessionOwner session, long delayMs)
    {
        var scheduled = session.ScheduleScriptCall(session.Version, new(790, 6, new(new(79), SessionEventKinds.PlayerEntity), null), delayMs);
        Assert.AreEqual(GameSessionDisposition.Changed, scheduled.Disposition, scheduled.Failure.ToString());
    }

    [TestMethod]
    public void A_frame_moves_the_three_clocks_together_and_reports_them()
    {
        using var session = Create();
        var step = new FrameStep(session, new Calls(session));
        Assert.AreEqual(0, step.Run(1_000).Steps, "the first stamp admits nothing");
        var report = step.Run(1_100);
        Assert.AreEqual(new FrameClocks(101, Start + 800, Start + 800, 100 * 8), report.Clocks);
        Assert.AreEqual(report.Clocks, session.QueryFrameClocks());
        Assert.AreEqual((100, 800), (report.Clocks.WorldMs, report.Clocks.GameMs - Start));
        Assert.IsFalse(report.HourChanged);
        Assert.AreSame(report, step.Last);
    }

    [TestMethod]
    public void The_participants_run_once_a_frame_after_the_passes_in_registration_order()
    {
        using var session = Create();
        var world = new Calls(session);
        var log = new List<string>();
        var first = new Recorder("a", log, session);
        var second = new Recorder("b", log, session);
        var step = new FrameStep(session, world);
        step.Register(first);
        step.Register(second);
        Assert.ThrowsExactly<InvalidOperationException>(() => step.Register(first), "a participant registers once");
        Assert.ThrowsExactly<ArgumentNullException>(() => step.Register(null!));
        Call(session, 100);
        step.Run(0);
        var report = step.Run(100);
        CollectionAssert.AreEqual(new[] { "a@0", "b@0", "a@800", "b@800" }, log, "once a frame (the first stamp's frame too), after its events, in the order registered");
        Assert.AreEqual(2, report.Participants);
        Assert.AreEqual(100L, world.Ran.Single().At - Start, "the call fired at its own deadline, before the participants");
        CollectionAssert.AreEqual(new[] { first, second }, step.Participants.ToArray());
        Assert.AreEqual(new FrameClocks(101, Start + 800, Start + 800, 100 * 8), first.Seen.Last().Clocks);
        Assert.AreEqual(default, first.Seen.Last().Flags);
    }

    [TestMethod]
    public void Events_fire_at_their_own_deadlines_whatever_the_frames_are()
    {
        var traces = new List<string>();
        foreach (var frames in new[] { new long[] { 250 }, new long[] { 5, 5, 5, 5, 230 }, new long[] { 100, 100, 50 } })
        {
            using var session = Create();
            var world = new Calls(session);
            foreach (var delay in new long[] { 100, 450, 451, 1_999 }) Call(session, delay);
            var step = new FrameStep(session, world);
            step.Run(0);
            var host = 0L;
            foreach (var frame in frames) step.Run(host += frame);
            traces.Add(string.Join(",", world.Ran.Select(r => r.At - Start)) + " @" + (session.QueryClockSnapshot().GameTimestampMs - Start));
        }
        Assert.AreEqual(1, traces.Distinct().Count(), string.Join(" | ", traces));
        Assert.AreEqual("100,450,451,1999 @2000", traces[0]);
    }

    [TestMethod]
    public void The_step_bound_folds_the_rest_of_the_frame_into_one_step_and_the_events_inside_it_fire_late()
    {
        using var session = Create();
        var world = new Calls(session);
        foreach (var delay in new long[] { 100, 300, 500 }) Call(session, delay);
        var step = new FrameStep(session, world, new() { MaxStepsPerFrame = 2 });
        step.Run(0);
        var report = step.Run(100);
        Assert.AreEqual((2, true), (report.Steps, report.Coalesced));
        CollectionAssert.AreEqual(new[] { 100L, 800, 800 }, world.Ran.Select(r => r.At - Start).ToArray());
        StringAssert.Contains(report.Status(session.QueryClockSnapshot().GameTimestampMs), "late (step bound)");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FrameStep(session, world, new() { MaxPassesPerStep = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FrameStep(session, world, new() { MaxStepsPerFrame = 5000 }));
    }

    [TestMethod]
    public void A_pause_stops_game_and_animation_time_and_the_participants_still_run_with_the_flag()
    {
        using var session = Create();
        var log = new List<string>();
        var seen = new Recorder("p", log, session);
        var step = new FrameStep(session, new Calls(session));
        step.Register(seen);
        step.Run(0);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        var paused = step.Run(100);
        Assert.AreEqual(new FrameClocks(101, Start, Start, 0), paused.Clocks, "only real time moved");
        Assert.IsTrue(seen.Seen.Last().Flags.Paused);
        Assert.AreEqual(1, paused.Steps);
    }

    [TestMethod]
    public void A_frozen_frame_moves_no_clock_but_runs_a_pass_and_the_participants()
    {
        using var session = Create();
        var log = new List<string>();
        var step = new FrameStep(session, new Calls(session));
        step.Register(new Recorder("p", log, session));
        step.Run(0);
        Call(session, 1);
        var frozen = step.Run(5_000, frozen: true);
        Assert.AreEqual((0, 1, 1), (frozen.Steps, frozen.Passes, frozen.Participants));
        Assert.AreEqual(1L, frozen.Clocks.RealMs);
        Assert.AreEqual(1, log.Count - 1, "the participants ran in the frozen frame too (the first frame's run is the other)");
        // The next frame credits at most 250 ms: the frozen time is not a backlog.
        Assert.AreEqual(250, step.Run(9_000).Clocks.RealMs - 1);
    }

    [TestMethod]
    public void A_step_without_an_event_world_moves_the_clocks_and_runs_no_pass()
    {
        using var session = Create();
        var step = new FrameStep(session, null);
        Assert.IsFalse(step.RunsEvents);
        step.Run(0);
        var report = step.Run(100);
        Assert.AreEqual((1, 0, Start + 800), (report.Steps, report.Passes, report.Clocks.GameMs));
        Assert.AreEqual(report.Clocks.GameMs, session.QueryClockSnapshot().GameTimestampMs);
    }

    [TestMethod]
    public void The_change_of_place_ping_runs_first_before_the_time_of_the_frame_moves()
    {
        using var session = Create();
        var world = new Calls(session);
        var order = new List<string>();
        var step = new FrameStep(session, world, placePing: () => order.Add("ping@" + (session.QueryFrameClocks().GameMs - Start)));
        step.Register(new Recorder("p", order, session));
        step.Run(0);
        step.Run(100);
        CollectionAssert.AreEqual(new[] { "ping@0", "p@0", "ping@0", "p@800" }, order);
    }

    [TestMethod]
    public void A_jump_ignores_the_pause_fires_events_once_late_and_reports_the_hour()
    {
        using var session = Create();
        var world = new Calls(session);
        var log = new List<string>();
        var step = new FrameStep(session, world);
        step.Register(new Recorder("p", log, session));
        Call(session, 600_000);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        var report = step.Jump(3_600_000);
        Assert.AreEqual((1, 1, true, 0), (report.Steps, world.Ran.Count, report.HourChanged, report.Participants), "no participant runs: a jump is not a frame");
        Assert.AreEqual(3_600_000L, world.Ran.Single().At - Start, "the call came due inside the jump and fired once, at its end");
        Assert.AreEqual(new FrameClocks(1, Start + 3_600_000, Start + 3_600_000, 0), report.Clocks, "a jump leaves real and world time");
        Assert.AreEqual(0, log.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => step.Jump(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => step.Run(-1));
    }

    [TestMethod]
    public void A_refused_frame_throws_and_leaves_no_frame_open()
    {
        using var session = Create();
        var step = new FrameStep(session, new Calls(session));
        step.Run(0);
        // A frame already open elsewhere (the one-frame rule) refuses the step; it throws and the open frame is not touched.
        var other = session.BeginFrame(50).Frame!;
        Assert.ThrowsExactly<InvalidOperationException>(() => step.Run(100));
        Assert.IsTrue(other.IsOpen);
        other.End();
        Assert.AreEqual(1, step.Run(100).Steps);
    }
}
