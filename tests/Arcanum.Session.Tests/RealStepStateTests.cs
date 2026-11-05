using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>MIG12 S8 (ARC-DEV-127): the real-time steps of the original's real list (the world-map journey's travel step, the turn-based event)
/// as session events: scheduled after the real clock, one pending of each, not saved, reported by the pass that finds them due and fired
/// behind the pause.</summary>
[TestClass]
public sealed class RealStepStateTests
{
    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // Spends `hostMs` on the clocks in one step, as a frame does (the session admits it at the host stamp).
    private static void Frame(GameSessionOwner owner, long hostMs)
    {
        var frame = owner.BeginFrame(hostMs).Frame!;
        if (frame.RemainingUnits > 0) Assert.IsTrue(frame.Step(frame.RemainingUnits).Ok);
        frame.End();
    }

    [TestMethod]
    public void A_step_is_scheduled_after_the_real_clock_and_one_of_each_kind_stays_pending()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.WorldMapStep));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRealStep(SessionRealStep.WorldMapStep, 50));
        Assert.AreEqual(51L, owner.QueryRealStep(SessionRealStep.WorldMapStep), "the real clock starts at 1 ms");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ScheduleRealStep(SessionRealStep.WorldMapStep, 2), "a second is not queued");
        Assert.AreEqual(51L, owner.QueryRealStep(SessionRealStep.WorldMapStep), "the pending one keeps its deadline");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRealStep(SessionRealStep.TurnBasedStep, 2));
        Assert.AreEqual(3L, owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        var pending = owner.QueryEvents();
        Assert.IsTrue(pending.All(e => e.Domain == GameEventDomain.Real && e.ScopeMap == 0));
        Assert.AreEqual(2, pending.Count);
    }

    [TestMethod]
    public void A_delay_out_of_range_is_refused_and_a_step_can_be_cancelled()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.ScheduleRealStep(SessionRealStep.WorldMapStep, 0));
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.ScheduleRealStep(SessionRealStep.WorldMapStep, GameSessionOwner.MaxRealStepDelayMs + 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRealStep(SessionRealStep.WorldMapStep, GameSessionOwner.MaxRealStepDelayMs));
        Assert.IsTrue(owner.CancelRealStep(SessionRealStep.WorldMapStep));
        Assert.IsFalse(owner.CancelRealStep(SessionRealStep.WorldMapStep));
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.WorldMapStep));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.ScheduleRealStep((SessionRealStep)9, 5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryRealStep((SessionRealStep)0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.CancelRealStep((SessionRealStep)9));
    }

    [TestMethod]
    public void A_pass_reports_the_due_step_once_and_consumes_it()
    {
        using var owner = Create();
        owner.ScheduleRealStep(SessionRealStep.WorldMapStep, 50);
        owner.ScheduleRealStep(SessionRealStep.TurnBasedStep, 80);
        owner.BeginFrame(0).Frame!.End();
        Frame(owner, 40);
        Assert.AreEqual(0, owner.ProcessEvents(new NoWorld()).RealSteps.Count, "not due yet");
        Frame(owner, 60);
        var pass = owner.ProcessEvents(new NoWorld());
        Assert.AreEqual(1, pass.RealSteps.Count);
        Assert.AreEqual((SessionRealStep.WorldMapStep, 61L), (pass.RealSteps[0].Step, pass.RealSteps[0].NowMs));
        Assert.AreEqual(51L, pass.RealSteps[0].Event.DeadlineMs);
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.WorldMapStep), "the event left the queue");
        Assert.AreEqual(0, owner.ProcessEvents(new NoWorld()).RealSteps.Count);
        Frame(owner, 80);
        Assert.AreEqual(SessionRealStep.TurnBasedStep, owner.ProcessEvents(new NoWorld()).RealSteps.Single().Step);
    }

    [TestMethod]
    public void A_step_falls_due_behind_the_pause_and_in_a_frozen_clock()
    {
        using var owner = Create();
        owner.ScheduleRealStep(SessionRealStep.WorldMapStep, 50);
        owner.BeginFrame(0).Frame!.End();
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        var game = owner.QueryClockSnapshot().GameTimestampMs;
        Frame(owner, 100);
        Assert.AreEqual(game, owner.QueryClockSnapshot().GameTimestampMs, "the pause stopped game time");
        Assert.AreEqual(SessionRealStep.WorldMapStep, owner.ProcessEvents(new NoWorld()).RealSteps.Single().Step, "real time ran behind it");
    }

    [TestMethod]
    public void The_steps_are_not_saved()
    {
        using var owner = Create();
        owner.ScheduleRealStep(SessionRealStep.WorldMapStep, 50);
        owner.ScheduleRealStep(SessionRealStep.TurnBasedStep, 2);
        var capture = owner.Capture();
        Assert.AreEqual(0, capture.Events.Pending.Count);
        var text = GameSessionSnapshotCodec.Encode(capture);
        StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("travel\\.step|combat\\.step"));
        using var restored = Restore(Decode(text));
        Assert.IsNull(restored.QueryRealStep(SessionRealStep.WorldMapStep), "a loaded session has no journey step: the viewer's journey ended with the load");
        Assert.IsNull(restored.QueryRealStep(SessionRealStep.TurnBasedStep));
    }
}
