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
        Assert.AreEqual(participant.Frames, participant.Pumps, "one pump per frame, none held back");
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
        var after = host.NowMs;
        step.Run(1_000_000, frozen: true);
        Assert.AreEqual((frozen + 1, pumps, after), (participant.Frames, participant.Pumps, host.NowMs),
            "a frozen frame (a loop that does not ping), through the frame step, reaches the participant as frozen and steps nothing");
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
    [TestMethod]
    public void A_map_without_lighting_has_the_same_frames_the_clock_flows_the_runs_step_and_the_open_menu_pauses()
    {
        var f = AreaAiHostRebindTests.Open();
        using var fixture = f;
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        host.PerformAttack(f.A, Arcanum.Simulation.Ai.AiSchedulerRules.Pc);
        var participant = new AreaAiFrameParticipant(() => host);
        var driver = AreaFrameDriver.Create(f.Session, f.Events);
        var tap = new FrameClockTap();
        driver.Frame.Register(tap);
        driver.Frame.Register(participant);

        driver.Tick(1000, false);
        var game = f.Session.QueryClockSnapshot().GameTimestampMs;
        driver.Tick(1250, false);
        Assert.AreEqual(game + 8 * 250, f.Session.QueryClockSnapshot().GameTimestampMs, "an unlit map's game clock flows: game time is 8 times host time");
        Assert.AreEqual(250, host.NowMs, "and its runs are pumped on the frame's world milliseconds");
        Assert.IsTrue(participant.TakeSteps() > 0);
        Assert.AreEqual(250, tap.Clocks?.WorldMs, "the world-time tap of the other consumers (hero walk, projectiles) is fed by the same frames");

        driver.Tick(1500, true);
        Assert.IsTrue(driver.MenuPaused, "the open Esc menu holds the Menu pause");
        var paused = f.Session.QueryClockSnapshot().GameTimestampMs;
        var pumps = participant.Pumps;
        driver.Tick(1750, true);
        Assert.AreEqual((paused, pumps), (f.Session.QueryClockSnapshot().GameTimestampMs, participant.Pumps), "behind it neither the clock nor the runs move");

        driver.Tick(2000, false);
        Assert.IsFalse(driver.MenuPaused);
        Assert.IsFalse(f.Session.QueryClockSnapshot().PauseDisposition.Paused, "the pause the driver held is released");
        Assert.AreEqual(500, tap.Clocks?.WorldMs);
        Assert.AreEqual(250 + 250, host.NowMs, "the world resumed without a catch-up");
    }
    [TestMethod]
    public void The_npc_the_hero_talks_with_is_the_gates_conversation_partner_and_the_others_are_not()
    {
        // ARC-DEV-126 (MIG12 S7): a dialog no longer pauses the world, so the viewer names the talk partner for the AI gate (the host's Conversation).
        var f = AreaAiHostRebindTests.Open();
        using var fixture = f;
        string? partner = null;
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]) with { ConversationPartner = () => partner });
        Assert.AreEqual(Arcanum.Simulation.Ai.AiConversation.None, host.Conversation(f.A), "no dialog open: nobody is held");
        partner = f.A;
        Assert.AreEqual(Arcanum.Simulation.Ai.AiConversation.Pc, host.Conversation(f.A), "the speaker is the hero's partner");
        Assert.AreEqual(Arcanum.Simulation.Ai.AiConversation.None, host.Conversation("oid:someone-else"), "another NPC is not");
    }
    [TestMethod]
    public void A_frozen_frame_is_no_pause_moves_no_clock_and_runs_no_goal_step_and_the_next_frame_credits_at_most_250_ms()
    {
        // ARC-DEV-126 (MIG12 S7, TL25): the death cover and its slide show are frames in which the original does not ping.
        var f = AreaAiHostRebindTests.Open();
        using var fixture = f;
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        host.PerformAttack(f.A, Arcanum.Simulation.Ai.AiSchedulerRules.Pc);
        var participant = new AreaAiFrameParticipant(() => host);
        var driver = AreaFrameDriver.Create(f.Session, f.Events);
        var tap = new FrameClockTap();
        driver.Frame.Register(tap);
        driver.Frame.Register(participant);
        driver.Tick(1000, false);
        driver.Tick(1250, false);
        var game = f.Session.QueryClockSnapshot().GameTimestampMs;
        var real = f.Session.QueryFrameClocks().RealMs;
        var pumps = participant.Pumps;
        var world = tap.Clocks!.Value.WorldMs;

        for (var ms = 60_000; ms <= 61_000; ms += 250) driver.Tick(ms, false, frozen: true);
        Assert.IsFalse(f.Session.QueryClockSnapshot().PauseDisposition.Paused, "a frozen frame raises no pause: the cover is the host's word, not the session's");
        Assert.IsFalse(driver.MenuPaused);
        Assert.AreEqual((game, real, pumps, world), (f.Session.QueryClockSnapshot().GameTimestampMs, f.Session.QueryFrameClocks().RealMs, participant.Pumps, tap.Clocks!.Value.WorldMs),
            "no stamp is observed, so no clock moves and no goal step runs");
        Assert.IsTrue(tap.Flags.Frozen);

        driver.Tick(61_100, false);
        Assert.IsFalse(tap.Flags.Frozen);
        Assert.AreEqual(game + 8 * 250, f.Session.QueryClockSnapshot().GameTimestampMs, "the reference stayed at the last observed stamp: the next frame credits the 250 ms cut, not a minute");
        Assert.AreEqual(world + 250, tap.Clocks!.Value.WorldMs);
        Assert.IsTrue(participant.Pumps > pumps);

        // The Esc menu open over the death cover is both: still no stamp observed, and the pause it raised is released with it.
        driver.Tick(61_400, true, frozen: true);
        Assert.IsTrue(driver.MenuPaused);
        driver.Tick(61_500, false);
        Assert.IsFalse(driver.MenuPaused);
        Assert.IsFalse(f.Session.QueryClockSnapshot().PauseDisposition.Paused);
    }
    [TestMethod]
    public void The_real_time_steps_of_travel_and_the_round_run_on_a_map_without_lighting_even_behind_a_pause()
    {
        // ARC-DEV-124 with MIG12 S8: the frame driver of an unlit map hands out the real-time steps like a lit map's ambient driver.
        var f = AreaAiHostRebindTests.Open();
        using var fixture = f;
        var travel = new List<long>();
        var round = 0;
        var driver = AreaFrameDriver.Create(f.Session, new AreaEventWorld(() => null, () => null));
        driver.Frame.RealSteps = new AreaRealStepHost(() => { travel.Add(f.Session.QueryFrameClocks().RealMs); return travel.Count < 3 ? 62 : null; }, () => { round++; return null; });
        driver.Tick(1000, false);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ScheduleRealStep(SessionRealStep.WorldMapStep, 50));
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ScheduleRealStep(SessionRealStep.TurnBasedStep, 2));
        driver.Tick(1060, true);
        Assert.IsTrue(driver.MenuPaused, "the open Esc menu holds the Menu pause");
        Assert.AreEqual((1, 1), (travel.Count, round), "the first travel step and the round's event fell due behind the pause: real time runs behind it");
        for (var ms = 1120; ms <= 1300 && travel.Count < 3; ms += 60) driver.Tick(ms, true);
        Assert.AreEqual(3, travel.Count, "the chain goes on every 62 real ms behind the pause and ends when the host asks for no repeat");
        Assert.IsNull(f.Session.QueryRealStep(SessionRealStep.WorldMapStep), "no step is left behind");
    }
}
