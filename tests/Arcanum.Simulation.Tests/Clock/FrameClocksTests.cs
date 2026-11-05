using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Clock;

[TestClass]
public sealed class FrameClocksTests
{
    private const long Start = 54_000_000; // 15:00, the installed start hour
    private static readonly FrameFlags Run = default;
    private static readonly FrameFlags Paused = new(true, false, false);
    private static readonly FrameFlags Round = new(false, true, false);
    private static readonly FrameFlags Frozen = new(false, false, true);

    private static FrameClockKeeper Keeper(long animation = Start) => Ok(FrameClockKeeper.Start(animation));

    private static FrameClockKeeper Ok(FrameClockKeeperResult result)
    {
        Assert.AreEqual(FrameClockFailure.None, result.Failure);
        return result.Keeper!;
    }

    private static FrameAdmission Advance(FrameClockKeeper keeper, long host, FrameFlags flags)
    {
        var result = keeper.Advance(host, flags);
        Assert.AreEqual(FrameClockFailure.None, result.Failure);
        return result.Admission;
    }

    private static void Expect(FrameClockKeeper keeper, long real, long animation, long world)
    {
        Assert.AreEqual(real, keeper.RealMs, "real");
        Assert.AreEqual(animation, keeper.AnimationMs, "animation");
        Assert.AreEqual(world, keeper.WorldMs, "world");
    }

    [TestMethod]
    public void StartStampsAreTheOriginalsAndRestoreIsValidated()
    {
        var keeper = Keeper();
        Expect(keeper, 1, Start, 0);
        Assert.AreEqual(new FrameClocks(1, Start, Start, 0), keeper.ClocksAt(Start));
        Assert.AreEqual(new FrameClocks(7, 9, 11, 13), Ok(FrameClockKeeper.Restore(7, 11)).ClocksAt(9) with { WorldEighthMs = 13 });
        Assert.AreEqual(FrameClockFailure.InvalidState, FrameClockKeeper.Restore(0, 5).Failure);
        Assert.AreEqual(FrameClockFailure.InvalidState, FrameClockKeeper.Restore(1, -1).Failure);
        Assert.AreEqual(FrameClockFailure.InvalidState, FrameClockKeeper.Start(-1).Failure);
        Assert.IsNull(FrameClockKeeper.Restore(0, 0).Keeper);
        Expect(Ok(FrameClockKeeper.Restore(1, 0)), 1, 0, 0);
    }

    [TestMethod]
    public void AdmissionFollowsTheWorksheetAndMovesEveryClock()
    {
        // The deltas of the RES-020 worksheet, each from a fresh reference: real i, game and animation 8i, world i.
        foreach (var (delta, interval) in new[] { (4L, 0L), (5L, 5L), (6L, 6L), (249L, 249L), (250L, 250L), (251L, 250L) })
        {
            var keeper = Keeper();
            Assert.AreEqual(default, Advance(keeper, 1000, Run));
            var admitted = Advance(keeper, 1000 + delta, Run);
            Assert.AreEqual(new FrameAdmission(interval, interval * 8, interval * 8), admitted, $"delta {delta}");
            Expect(keeper, 1 + interval, Start + interval * 8, interval);
        }
    }

    [TestMethod]
    public void ShortFramesAddUpAndTheReferenceMovesBeforeTheCut()
    {
        var keeper = Keeper();
        Advance(keeper, 0, Run);
        Assert.AreEqual(default, Advance(keeper, 2, Run));
        Assert.AreEqual(default, Advance(keeper, 4, Run));
        Assert.AreEqual(new FrameAdmission(5, 40, 40), Advance(keeper, 5, Run), "the 5 ms add up from the unmoved reference");
        Assert.AreEqual(new FrameAdmission(250, 2000, 2000), Advance(keeper, 256, Run), "251 ms cut to 250, reference now 256");
        Assert.AreEqual(default, Advance(keeper, 259, Run));
        Assert.AreEqual(new FrameAdmission(7, 56, 56), Advance(keeper, 263, Run), "7 ms from 256: the excess was lost, not owed");
        Expect(keeper, 1 + 5 + 250 + 7, Start + 8 * 262, 262);
    }

    [TestMethod]
    public void FirstAndBackwardsStampsAdmitNothingAndResetForgetsTheReference()
    {
        var keeper = Keeper();
        Assert.AreEqual(default, Advance(keeper, 500, Run));
        Assert.AreEqual(default, Advance(keeper, 100, Run), "backwards: the reference restarts at 100");
        Assert.AreEqual(new FrameAdmission(10, 80, 80), Advance(keeper, 110, Run));
        keeper.ResetReference();
        Assert.AreEqual(default, Advance(keeper, 10_000, Run));
        Assert.AreEqual(new FrameAdmission(100, 800, 800), Advance(keeper, 10_100, Run));
        Expect(keeper, 111, Start + 8 * 110, 110);
    }

    [TestMethod]
    public void PauseStopsGameAndAnimationButNotRealTime()
    {
        var keeper = Keeper();
        Advance(keeper, 0, Run);
        Assert.AreEqual(new FrameAdmission(100, 0, 0), Advance(keeper, 100, Paused));
        Expect(keeper, 101, Start, 0);
        Assert.AreEqual(new FrameAdmission(100, 800, 800), Advance(keeper, 200, Run));
        Expect(keeper, 201, Start + 800, 100);
        // The pause is not a backlog: nothing was owed, so after it only the frame's own interval counts.
        for (var host = 300L; host <= 60_000; host += 100) Advance(keeper, host, new(true, true, false));
        Assert.AreEqual(new FrameAdmission(100, 800, 800), Advance(keeper, 60_100, Run));
        Expect(keeper, 1 + 100 + 100 + (60_000 - 200) + 100, Start + 1600, 200);
    }

    [TestMethod]
    public void RoundStopsGameTimeOnlyAndAnimationAndWorldKeepRunning()
    {
        var keeper = Keeper();
        Advance(keeper, 0, Run);
        Assert.AreEqual(new FrameAdmission(50, 0, 400), Advance(keeper, 50, Round));
        Expect(keeper, 51, Start + 400, 50);
        Assert.AreEqual(new FrameAdmission(50, 400, 400), Advance(keeper, 100, Run));
        Assert.AreEqual(new FrameAdmission(50, 0, 0), Advance(keeper, 150, new(true, true, false)), "paused in a round: only real");
    }

    [TestMethod]
    public void FrozenFramesObserveNothingAndTheNextCreditsAtMostTheCut()
    {
        var keeper = Keeper();
        Advance(keeper, 1000, Run);
        Assert.AreEqual(default, Advance(keeper, 1100, Frozen));
        Assert.AreEqual(default, Advance(keeper, 9_000, Frozen), "a frozen frame passes any stamp, even a backwards one, unobserved");
        Assert.AreEqual(default, Advance(keeper, -5, Frozen));
        Expect(keeper, 1, Start, 0);
        Assert.AreEqual(new FrameAdmission(250, 2000, 2000), Advance(keeper, 20_000, Run), "a long freeze credits 250 ms once");
        Assert.AreEqual(new FrameAdmission(10, 80, 80), Advance(keeper, 20_010, Run));
        // A short freeze inside the cut: the reference stayed, so the freeze is credited, as the original's next ping does.
        Advance(keeper, 20_100, Frozen);
        Assert.AreEqual(new FrameAdmission(90, 720, 720), Advance(keeper, 20_100, Run));
    }

    [TestMethod]
    public void JumpAddsToAnimationIgnoresPauseAndLeavesWorldTimeAlone()
    {
        var keeper = Keeper();
        Advance(keeper, 0, Run);
        Advance(keeper, 100, Run);
        Expect(keeper, 101, Start + 800, 100);
        var jump = keeper.Jump(3_600_000);
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.None, 3_600_000), jump);
        Expect(keeper, 101, Start + 800 + 3_600_000, 100);
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.None, 1), keeper.Jump(0), "a zero duration is 1 ms");
        Assert.AreEqual(Start + 800 + 3_600_001, keeper.AnimationMs);
        // Pause and round do not matter to a jump, and a jump fires nothing: the next frame is only its own interval.
        Advance(keeper, 200, Paused);
        keeper.Jump(1000);
        Assert.AreEqual(new FrameAdmission(50, 0, 400), Advance(keeper, 250, Round));
        Expect(keeper, 101 + 100 + 50, Start + 800 + 3_600_001 + 1000 + 400, 150);
    }

    [TestMethod]
    public void PlanJumpChangesNothingAndLimitsAreTypedRefusals()
    {
        var keeper = Keeper(long.MaxValue - 10);
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.None, 10), keeper.PlanJump(10));
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.TimeOverflow, 0), keeper.PlanJump(11));
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.InvalidDuration, 0), keeper.PlanJump(-1));
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.TimeOverflow, 0), keeper.Jump(long.MaxValue));
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.InvalidDuration, 0), keeper.Jump(-1));
        Assert.AreEqual(long.MaxValue - 10, keeper.AnimationMs, "refusals and plans change nothing");
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.None, 10), keeper.Jump(10));
        Assert.AreEqual(long.MaxValue, keeper.AnimationMs);
        Assert.AreEqual(new FrameJumpResult(FrameClockFailure.TimeOverflow, 0), keeper.Jump(0));
    }

    [TestMethod]
    public void AdvanceRefusesBadStampsAndStampsTooCloseToTheLimitWithoutChangingAnything()
    {
        var keeper = Keeper();
        Advance(keeper, 100, Run);
        Assert.AreEqual(FrameClockFailure.InvalidHostTime, keeper.Advance(-1, Run).Failure);
        Assert.AreEqual(new FrameAdmission(10, 80, 80), Advance(keeper, 110, Run), "the refused call left the reference");

        var near = Ok(FrameClockKeeper.Restore(1, long.MaxValue - 1999));
        // Refused before the host stamp is observed; clocks only grow, so a refusing keeper stays refusing and the
        // reference cannot be seen to move or stay: the state check below is what a refusal must preserve.
        var refused = near.Advance(0, Run);
        Assert.AreEqual(new FrameAdvanceResult(FrameClockFailure.TimeOverflow, default), refused);
        Assert.AreEqual(refused, near.Advance(1000, Run));
        Expect(near, 1, long.MaxValue - 1999, 0);
        Assert.AreEqual(default, Advance(near, 1000, Frozen), "a frozen frame needs no headroom");

        var ok = Ok(FrameClockKeeper.Restore(1, long.MaxValue - 2000));
        ok.Advance(0, Run);
        Assert.AreEqual(new FrameAdmission(250, 2000, 2000), Advance(ok, 1000, Run), "the largest admission still fits");
        Assert.AreEqual(long.MaxValue, ok.AnimationMs);
        Assert.AreEqual(FrameClockFailure.TimeOverflow, ok.Advance(1500, Run).Failure);
    }

    [TestMethod]
    public void WorldTimeKeepsEighthsSoNothingIsRoundedAway()
    {
        var keeper = Keeper();
        Advance(keeper, 0, Run);
        for (var host = 5L; host <= 50; host += 5) Advance(keeper, host, Run); // ten 5 ms frames
        Assert.AreEqual(50, keeper.WorldMs);
        Assert.AreEqual(400, keeper.ClocksAt(0).WorldEighthMs);
        Assert.AreEqual(50, keeper.ClocksAt(0).WorldMs);
        Assert.AreEqual(0, new FrameClocks(1, 0, 0, 7).WorldMs);
        Assert.AreEqual(1, new FrameClocks(1, 0, 0, 8).WorldMs);
    }

    [TestMethod]
    public void SameHostStampsGiveTheSameClocksHoweverTheyAreSplit()
    {
        // Determinism: two keepers fed the same stamps agree, and the total of a 250 ms span is the same whether it
        // arrives as five 50 ms frames or one 250 ms frame (no pause or freeze in between).
        var a = Keeper();
        var b = Keeper();
        Advance(a, 0, Run);
        Advance(b, 0, Run);
        for (var host = 50L; host <= 250; host += 50) Advance(a, host, Run);
        Advance(b, 250, Run);
        Assert.AreEqual(a.ClocksAt(1), b.ClocksAt(1));
        Expect(a, 251, Start + 2000, 250);
    }

    [TestMethod]
    public void TheGameFlowAdapterIsTheRealIntervalTimesTheRate()
    {
        var flow = new GameTimeFlow();
        var interval = new GameTimeFlow();
        Assert.AreEqual(0, interval.ObserveInterval(0));
        Assert.AreEqual(0, flow.Observe(0));
        foreach (var host in new long[] { 3, 5, 9, 300, 303, 1000 })
            Assert.AreEqual(interval.ObserveInterval(host) * 8, flow.Observe(host), $"host {host}");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interval.ObserveInterval(-1));
    }

    // MIG12 S4: admission without moving, and a frame spent in sub-steps.

    [TestMethod]
    public void AdmitObservesWithoutMovingAndStepsAddUpToTheAdvance()
    {
        var split = Keeper();
        var whole = Keeper();
        Advance(whole, 0, Run);
        Assert.AreEqual(default, split.Admit(0, Run).Admission, "the first observation admits nothing");
        var admitted = split.Admit(100, Run);
        Assert.AreEqual(new FrameAdmission(100, 800, 800), admitted.Admission);
        Expect(split, 1, Start, 0);
        // 800 units spent in odd pieces: the sub-millisecond share is carried, so real time ends exactly +100.
        foreach (var units in new long[] { 3, 5, 8, 13, 771 }) Assert.AreEqual(FrameClockFailure.None, split.Step(units, Run));
        Advance(whole, 100, Run);
        Assert.AreEqual(whole.ClocksAt(0), split.ClocksAt(0));
        Expect(split, 101, Start + 800, 100);
    }

    [TestMethod]
    public void AStepFollowsTheFlagsOfItsMomentAndRefusesABadLength()
    {
        var keeper = Keeper();
        Assert.AreEqual(FrameClockFailure.None, keeper.Step(16, Paused));
        Expect(keeper, 3, Start, 0);
        Assert.AreEqual(FrameClockFailure.None, keeper.Step(16, Round));
        Expect(keeper, 5, Start + 16, 2);
        Assert.AreEqual(FrameClockFailure.InvalidDuration, keeper.Step(0, Run));
        Assert.AreEqual(FrameClockFailure.InvalidDuration, keeper.Step(-8, Run));
        Expect(keeper, 5, Start + 16, 2);
        var near = Ok(FrameClockKeeper.Restore(1, long.MaxValue - 10));
        Assert.AreEqual(FrameClockFailure.TimeOverflow, near.Step(11, Run));
        Assert.AreEqual(long.MaxValue - 10, near.AnimationMs);
        Assert.AreEqual(FrameClockFailure.None, near.Step(11, Paused), "a pause moves no animation time, so it cannot overflow it");
    }

    [TestMethod]
    public void UnitsUntilNamesTheUnitsAtWhichEachClockReachesItsDeadline()
    {
        var keeper = Keeper();
        Advance(keeper, 0, Run);
        Assert.AreEqual(new FrameAdmission(100, 800, 800), keeper.Admit(100, Run).Admission);
        Assert.AreEqual(FrameClockFailure.None, keeper.Step(3, Run));
        // Real: 8 units a millisecond, less the carried 3 units; game and animation: 1 ms a unit.
        Assert.AreEqual((10 - 1) * 8 - 3, keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, 10, Start + 3, Run));
        Assert.AreEqual(97, keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Game, Start + 100, Start + 3, Run));
        Assert.AreEqual(97, keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Animation, Start + 100, Start + 3, Run));
        // A clock that stands (pause: game and animation; round: game) and a deadline not ahead have no units.
        Assert.IsNull(keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Game, Start + 100, Start + 3, Round));
        Assert.IsNull(keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Animation, Start + 100, Start + 3, Paused));
        Assert.IsNotNull(keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Animation, Start + 100, Start + 3, Round));
        Assert.IsNotNull(keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, 10, Start + 3, Paused), "real time runs behind the pause");
        Assert.IsNull(keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Game, Start + 3, Start + 3, Run));
        Assert.IsNull(keeper.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, 1, Start, Run));
    }

    [TestMethod]
    public void ACheckedStepChangesNothingAndAFarRealDeadlineIsNoBoundary()
    {
        var near = Ok(FrameClockKeeper.Restore(1, long.MaxValue - 10));
        Assert.AreEqual(FrameClockFailure.TimeOverflow, near.CanStep(11, Run));
        Assert.AreEqual(FrameClockFailure.None, near.CanStep(10, Run));
        Assert.AreEqual(FrameClockFailure.InvalidDuration, near.CanStep(0, Run));
        Assert.AreEqual((1, long.MaxValue - 10), (near.RealMs, near.AnimationMs), "asking moves nothing");
        Assert.IsNull(Keeper().UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, long.MaxValue / 2, Start, Run), "a real deadline past any frame");
    }
}
