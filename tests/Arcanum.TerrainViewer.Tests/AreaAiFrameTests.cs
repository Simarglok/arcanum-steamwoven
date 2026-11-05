using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-124 (MIG12 S5): the AI host takes part in the frame step. The goal runs step on the frame's world milliseconds (admitted animation
/// time ÷ 8), once per frame after the event passes; a pause holds them back (TL23), a jump does not run them forward (TL26), a frozen frame
/// does not call them (TL25), a round does not stop them (TL38). Invented sectors and one NPC, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiFrameTests
{
    private static readonly FrameFlags Free = new(false, false, false);

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host, AreaAiFrameParticipant Participant, FrameStep Step) Open(Action<AreaAiHost>? before = null)
    {
        var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        host.PerformAttack(f.A, Arcanum.Simulation.Ai.AiSchedulerRules.Pc);
        Assert.IsNotNull(host.RunOf(f.A), "the attack starter gave the NPC a run");
        var participant = new AreaAiFrameParticipant(() => f.Host, before);
        var step = new FrameStep(f.Session, f.Events);
        step.Register(participant);
        return (f, host, participant, step);
    }

    [TestMethod]
    public void The_runs_step_on_the_frames_world_milliseconds_once_per_frame()
    {
        var (f, host, participant, step) = Open();
        using var fixture = f;
        step.AdvanceHost(0);
        var primed = participant.Frames;
        Assert.IsTrue(primed >= 1, "the priming frame admits nothing but still runs its participants");
        var first = host.NowMs;
        var report = step.AdvanceHost(1000);
        Assert.AreEqual(4, participant.Frames - primed, "250 ms frames: four of them");
        Assert.AreEqual(1, report.Participants, "the report counts the participant");
        Assert.AreEqual(first + 1000, host.NowMs, "the host's clock is the frame's world milliseconds, equal to real time unpaused");
        Assert.AreEqual(step.Session.QueryFrameClocks().WorldMs, host.NowMs);
        Assert.IsTrue(participant.PendingSteps > 0, "the due runs took steps");
        Assert.AreEqual(participant.PendingSteps, participant.TakeSteps());
        Assert.AreEqual(0, participant.PendingSteps, "the viewer takes the steps once");
    }

    [TestMethod]
    public void A_pause_holds_the_runs_back_and_a_resume_has_no_catch_up()
    {
        var (f, host, participant, step) = Open();
        using var fixture = f;
        step.AdvanceHost(500);
        var steps = participant.TakeSteps();
        Assert.IsTrue(steps > 0);
        var now = host.NowMs;
        var run = host.RunOf(f.A);

        var clock = f.Session.QueryClockSnapshot();
        Assert.AreEqual(ClockMutationDisposition.Changed, f.Session.SetPaused(clock.Token, new(true, PauseReason.Menu)).Disposition);
        var pumps = participant.Pumps;
        step.AdvanceHost(5000);
        Assert.AreEqual(pumps, participant.Pumps, "behind the Esc menu the goal runs do not step");
        Assert.IsTrue(participant.Paused > 0);
        Assert.AreEqual(0, participant.PendingSteps);
        Assert.AreEqual(now, host.NowMs, "the host's clock stands still: real time passes, world time does not");
        Assert.AreSame(run, host.RunOf(f.A));

        Assert.AreEqual(ClockMutationDisposition.Changed, f.Session.SetPaused(f.Session.QueryClockSnapshot().Token, default).Disposition);
        step.AdvanceHost(250);
        Assert.AreEqual(now + 250, host.NowMs, "after the resume the world moves on from where it stood");
    }

    [TestMethod]
    public void A_time_jump_does_not_fast_forward_the_runs_and_a_frozen_frame_does_not_call_them()
    {
        var (f, host, participant, step) = Open();
        using var fixture = f;
        step.AdvanceHost(250);
        var now = host.NowMs;
        var pumps = participant.Pumps;

        step.Jump(3_600_000);
        Assert.AreEqual(pumps, participant.Pumps, "a jump is not a frame: no participant runs");
        Assert.AreEqual(now, host.NowMs);
        step.AdvanceHost(250);
        Assert.AreEqual(now + 250, host.NowMs, "the next frame moves the world by its own interval, not by the jump");
        Assert.AreEqual(now + 250, step.Session.QueryFrameClocks().WorldMs);

        var frozen = participant.Frames;
        pumps = participant.Pumps;
        participant.Step(step.Session.QueryFrameClocks(), new(false, false, true));
        Assert.AreEqual((frozen + 1, pumps), (participant.Frames, participant.Pumps), "a frozen frame (a loop that does not ping) steps nothing");
    }

    [TestMethod]
    public void A_round_does_not_stop_the_runs_and_the_sector_touch_comes_before_the_pump_even_when_paused()
    {
        var touched = new List<string>();
        var (f, host, participant, step) = Open(ai => touched.Add("touch"));
        using var fixture = f;
        var clocks = new FrameClocks(1000, 8000, 8000, 8 * 400);
        participant.Step(clocks, new(false, true, false));
        Assert.AreEqual(400, host.NowMs, "a round stops the game clock only: the runs go on the animation clock");
        Assert.AreEqual(1, participant.Pumps);
        participant.Step(clocks with { WorldEighthMs = 8 * 500 }, new(true, false, false));
        Assert.AreEqual(400, host.NowMs, "paused: no pump");
        CollectionAssert.AreEqual(new[] { "touch", "touch" }, touched, "the view's sectors are touched on a paused frame too");
        Assert.AreEqual(1, participant.Pumps);
        Assert.AreEqual(1, participant.Paused);
    }

    [TestMethod]
    public void The_participant_follows_the_host_the_viewer_has_now_and_does_nothing_without_one()
    {
        AreaAiHost? current = null;
        var touched = 0;
        var participant = new AreaAiFrameParticipant(() => current, _ => touched++);
        participant.Step(new(1, 1, 1, 8 * 100), Free);
        Assert.AreEqual((1, 0, 0), (participant.Frames, participant.Pumps, touched), "no host: nothing to touch or pump");

        var (f, host, _, _) = Open();
        using var fixture = f;
        current = host;
        participant.Step(new(1, 1, 1, 8 * 100), Free);
        Assert.AreEqual((2, 1, 1), (participant.Frames, participant.Pumps, touched));
        Assert.AreEqual(100, host.NowMs);
    }

    [TestMethod]
    public void The_change_of_place_ping_opens_each_frame_before_time_moves_and_the_participants_run_last()
    {
        var f = AreaAiHostRebindTests.Open();
        using var fixture = f;
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        var log = new List<string>();
        var participant = new AreaAiFrameParticipant(() => host, _ => log.Add("touch@" + f.Session.QueryFrameClocks().WorldMs));
        var step = new FrameStep(f.Session, f.Events, placePing: () => log.Add("ping@" + f.Session.QueryFrameClocks().WorldMs));
        step.Register(participant);
        step.Run(0);
        step.Run(100);
        CollectionAssert.AreEqual(new[] { "ping@0", "touch@0", "ping@0", "touch@100" }, log,
            "the ping first, at the clocks the frame found; the participant after the frame moved the world");
    }
}
