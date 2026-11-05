using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-125 (MIG12 S6): the viewer's frame participant and the world timeline that reads it. Literal stamps and hand-computed
/// microseconds; a synthetic session with invented ids, no original data.
/// </summary>
[TestClass]
public sealed class FrameWorldClockTests
{
    private const long Host = 5_000_000_000;

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

    private sealed class NoEvents : ISessionScriptWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => true;
        public bool RunScriptCall(SessionScriptCall call) => true;
    }

    private static FrameStep Step(GameSessionOwner session, FrameClockTap tap)
    {
        var step = new FrameStep(session, new NoEvents());
        step.Register(tap);
        return step;
    }

    [TestMethod]
    public void The_tap_holds_no_clocks_until_its_first_frame_and_then_the_clocks_of_the_last_one()
    {
        using var session = Create();
        var tap = new FrameClockTap();
        var step = Step(session, tap);
        Assert.IsNull(tap.Clocks);
        Assert.AreEqual(0, tap.Frames);
        step.Run(1_000);
        Assert.AreEqual(1, tap.Frames, "the first stamp admits nothing, and the participant still runs");
        Assert.AreEqual(0, tap.Clocks!.Value.WorldMs);
        var report = step.Run(1_100);
        Assert.AreEqual(2, tap.Frames);
        Assert.AreEqual(report.Clocks, tap.Clocks);
        Assert.AreEqual(100, tap.Clocks!.Value.WorldMs, "100 admitted ms are 100 world ms");
        Assert.AreEqual(session.QueryFrameClocks(), tap.Clocks);
        Assert.IsFalse(tap.Flags.Paused);
    }

    [TestMethod]
    public void World_time_stops_with_the_pause_and_is_cut_like_the_original_ping_and_a_jump_does_not_move_it()
    {
        using var session = Create();
        var tap = new FrameClockTap();
        var step = Step(session, tap);
        step.Run(1_000);
        step.Run(1_040);
        Assert.AreEqual(40, tap.Clocks!.Value.WorldMs);
        step.Run(5_000);
        Assert.AreEqual(40 + 250, tap.Clocks!.Value.WorldMs, "a 3.96 s frame is cut at 250 ms");
        step.Run(5_002);
        Assert.AreEqual(40 + 250, tap.Clocks!.Value.WorldMs, "under 5 ms admits nothing");
        step.Run(5_010);
        Assert.AreEqual(40 + 250 + 10, tap.Clocks!.Value.WorldMs, "short frames add up from the unmoved reference");
        var before = tap.Clocks!.Value;
        step.Jump(3_600_000);
        step.Run(5_020);
        Assert.AreEqual(before.WorldMs + 10, tap.Clocks!.Value.WorldMs, "a jump of an hour moves game and animation time, not the world time");
        Assert.IsTrue(tap.Clocks!.Value.GameMs >= before.GameMs + 3_600_000);

        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        var paused = tap.Clocks!.Value.WorldMs;
        step.Run(5_120);
        Assert.IsTrue(tap.Flags.Paused);
        Assert.AreEqual(paused, tap.Clocks!.Value.WorldMs, "the Esc pause stops the world time");
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, new(false, PauseReason.None)).Disposition);
        step.Run(5_140);
        Assert.AreEqual(paused + 20, tap.Clocks!.Value.WorldMs, "the 100 ms behind the pause are not credited on resume, the next frame is");
    }

    [TestMethod]
    public void The_timeline_follows_the_host_until_a_frame_exists_and_then_the_world_milliseconds_without_a_step()
    {
        var timeline = new WorldTimeline();
        Assert.AreEqual(Host, timeline.Read(null, Host));
        Assert.IsFalse(timeline.FollowsFrames);
        Assert.AreEqual(Host + 7, timeline.Read(new FrameClockTap(), Host + 7), "a tap before its first frame is no source");
        var tap = new FrameClockTap();
        tap.Step(new FrameClocks(1, 0, 0, 3_000 * 8), default);
        Assert.AreEqual(Host + 7, timeline.Read(tap, Host + 500), "the frame clock continues the timeline where it was, it does not jump to the host");
        Assert.IsTrue(timeline.FollowsFrames);
        tap.Step(new FrameClocks(1, 0, 0, 3_000 * 8 + 8), default);
        Assert.AreEqual(Host + 7 + 1_000, timeline.Read(tap, Host + 900_000), "one world millisecond is 1000 microseconds, whatever the host did");
        tap.Step(new FrameClocks(1, 0, 0, 3_000 * 8 + 9), default);
        Assert.AreEqual(Host + 7 + 1_125, timeline.Read(tap, Host + 900_000), "one world eighth is 125 microseconds");
    }

    [TestMethod]
    public void The_timeline_stands_still_when_the_frame_clock_does_and_never_goes_back()
    {
        var timeline = new WorldTimeline();
        var tap = new FrameClockTap();
        tap.Step(new FrameClocks(1, 0, 0, 80), default);
        var at = timeline.Read(tap, Host);
        for (var host = 1; host < 5; host++) Assert.AreEqual(at, timeline.Read(tap, Host + host * 16_000), "a paused or short frame leaves the time where it was");
        tap.Step(new FrameClocks(1, 0, 0, 40), default);
        Assert.AreEqual(at, timeline.Read(tap, Host), "a frame clock behind the last value does not move it back");
        var next = new FrameClockTap();
        next.Step(new FrameClocks(1, 0, 0, 0), default);
        Assert.AreEqual(at, timeline.Read(next, Host - 10_000_000), "a new session's clock starts at 0 and the timeline continues from its last value");
        Assert.IsTrue(timeline.FollowsFrames);
        next.Step(new FrameClocks(1, 0, 0, 16), default);
        Assert.AreEqual(at + 2_000, timeline.Read(next, Host - 10_000_000));
        Assert.AreEqual(at + 2_000, timeline.Read(null, Host - 10_000_000), "the host's time behind the last value does not move it back either");
        Assert.AreEqual(Host + 50_000_000, timeline.Read(null, Host + 50_000_000));
        Assert.IsFalse(timeline.FollowsFrames);
    }

    [TestMethod]
    public void A_rebuilt_driver_and_a_replaced_session_continue_the_same_timeline()
    {
        using var first = Create();
        var tapA = new FrameClockTap();
        var stepA = Step(first, tapA);
        var timeline = new WorldTimeline();
        stepA.Run(1_000); stepA.Run(1_100);
        var a = timeline.Read(tapA, Host);
        using var second = Create();
        var tapB = new FrameClockTap();
        var stepB = Step(second, tapB);
        stepB.Run(9_000);
        Assert.AreEqual(a, timeline.Read(tapB, Host + 1), "a quick load starts the session's world clock at 0; the timeline does not step back");
        stepB.Run(9_050);
        Assert.AreEqual(a + 50_000, timeline.Read(tapB, Host + 1));
    }

    [TestMethod]
    public void The_light_animation_and_the_consumers_read_whole_world_milliseconds_of_the_tap()
    {
        var tap = new FrameClockTap();
        tap.Step(new FrameClocks(9, 1, 2, 123 * 8 + 7), default);
        Assert.AreEqual(123, tap.Clocks!.Value.WorldMs, "the light's base and its elapsed time are whole world milliseconds");
    }
}
