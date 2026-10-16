using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Clock.ClockFixtures;

namespace Arcanum.Simulation.Tests.Clock;

[TestClass]
public sealed class TimeFlowTests
{
    [TestMethod]
    public void AdmissionFollowsTheSourceWorksheet()
    {
        // Host deltas 4, 5, 6, 249, 250, 251 from the RES-020 worksheet, each from a fresh reference.
        foreach (var (delta, game) in new[] { (4L, 0L), (5L, 40L), (6L, 48L), (249L, 1992L), (250L, 2000L), (251L, 2000L) })
        {
            var flow = new GameTimeFlow();
            Assert.AreEqual(0, flow.Observe(1000));
            Assert.AreEqual(game, flow.Observe(1000 + delta), $"delta {delta}");
        }
    }

    [TestMethod]
    public void RejectedIntervalKeepsTheReferenceAndClampDiscardsTheExcess()
    {
        var flow = new GameTimeFlow();
        Assert.AreEqual(0, flow.Observe(0));
        Assert.AreEqual(0, flow.Observe(4));      // rejected, reference stays 0
        Assert.AreEqual(40, flow.Observe(5));     // admits 5 from 0
        Assert.AreEqual(2000, flow.Observe(256)); // admits clamped 250, reference 256
        Assert.AreEqual(0, flow.Observe(259));
        Assert.AreEqual(56, flow.Observe(263));   // 7 from 256: rejected intervals accumulate
        Assert.AreEqual(2000, flow.Observe(100_000));
        Assert.AreEqual(0, flow.Observe(100_004));
    }

    [TestMethod]
    public void FirstObservationBackwardsHostAndResetAdmitNothing()
    {
        var flow = new GameTimeFlow();
        Assert.AreEqual(0, flow.Observe(500));
        Assert.AreEqual(0, flow.Observe(100));    // backwards: restarts the reference at 100
        Assert.AreEqual(80, flow.Observe(110));
        flow.Reset();
        Assert.AreEqual(0, flow.Observe(10_000));
        Assert.AreEqual(800, flow.Observe(10_100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => flow.Observe(-1));
        Assert.AreEqual(0, flow.Observe(10_101), "a refused observation leaves the reference");
        Assert.AreEqual(40, flow.Observe(10_105));
    }

    [TestMethod]
    public void FlowIntoAPausedClockHasNoCatchupOnResume()
    {
        using var owner = Ready(Profile());
        var flow = new GameTimeFlow();
        flow.Observe(0);
        var clock = owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, flow.Observe(250), ClockAdvanceReason.OrdinaryAdvance).Snapshot;
        Assert.AreEqual(54_002_000, clock.GameTimestampMs);
        clock = owner.SetPaused(clock.Token, new(true, PauseReason.Menu)).Snapshot;
        for (var host = 500L; host <= 60_000; host += 250)
            Assert.AreEqual(ClockMutationDisposition.NoChange,
                owner.AdvanceGameTime(clock.Token, flow.Observe(host), ClockAdvanceReason.OrdinaryAdvance).Disposition);
        clock = owner.SetPaused(clock.Token, default).Snapshot;
        clock = owner.AdvanceGameTime(clock.Token, flow.Observe(60_100), ClockAdvanceReason.OrdinaryAdvance).Snapshot;
        Assert.AreEqual(54_002_800, clock.GameTimestampMs, "only the 100 ms after resume count");
    }

    [TestMethod]
    public void ExplicitWaitAdvancesWhilePausedAndKeepsThePause()
    {
        using var owner = At(54_000_000, 1, new(true, PauseReason.Menu));
        var before = owner.QueryClockSnapshot();
        var wait = owner.AdvanceGameTime(before.Token, 3_600_000, ClockAdvanceReason.ExplicitWait);
        State(wait.Snapshot, before.OwnerIdentity, Profile(), 57_600_000, new(true, PauseReason.Menu), 2,
            0, 57_600_000, 16, 0, 0, 1, 1, 1885, true);
        Result(owner, wait, ClockMutationDisposition.Changed, ClockFailure.None, wait.Snapshot);
        Result(owner, owner.AdvanceGameTime(wait.Snapshot.Token, 0, ClockAdvanceReason.ExplicitWait),
            ClockMutationDisposition.NoChange, ClockFailure.None, wait.Snapshot);
        Result(owner, owner.AdvanceGameTime(wait.Snapshot.Token, -1, ClockAdvanceReason.ExplicitWait),
            ClockMutationDisposition.Rejected, ClockFailure.InvalidDuration, wait.Snapshot);
        Result(owner, owner.AdvanceGameTime(before.Token, 1, ClockAdvanceReason.ExplicitWait),
            ClockMutationDisposition.Rejected, ClockFailure.StaleToken, wait.Snapshot);
        var running = owner.SetPaused(wait.Snapshot.Token, default).Snapshot;
        var day = owner.AdvanceGameTime(running.Token, 2 * 3_600_000, ClockAdvanceReason.ExplicitWait);
        State(day.Snapshot, before.OwnerIdentity, Profile(), 64_800_000, default, 4,
            0, 64_800_000, 18, 0, 0, 1, 1, 1885, false);
        Result(owner, owner.AdvanceGameTime(day.Snapshot.Token, long.MaxValue, ClockAdvanceReason.ExplicitWait),
            ClockMutationDisposition.Rejected, ClockFailure.TimeOverflow, day.Snapshot);
    }
}
