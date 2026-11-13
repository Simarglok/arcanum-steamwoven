using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-137 (MIG13 S6, goal-table.md GT26–GT37): the nodes of the stun 57, the knock-down 62, the get-up 63 and dying 31 as the stepper interprets them, over the executor tests' fake
/// world with an art, ART clips and the critter flags. Time is the sum of the steps' delays; a frame shows from the delay after the previous one, the last stays.
/// </summary>
[TestClass]
public sealed class CritterStateGoalsTests
{
    private const string Npc = "oid:bandit", Other = "oid:other";
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven;

    private static ExecutorRulesTests.World Arena()
    {
        var world = new ExecutorRulesTests.World();
        world.At[Npc] = new(10, 10);
        world.At[Other] = new(12, 10);
        world.Movers[Npc] = new(Fatigue: 20, MaxFatigue: 20);
        return world;
    }

    private static GoalRun Run(GoalFrame frame) => new(Npc, default, frame);

    private static int Time(IEnumerable<AiGoalStepResult> steps) => steps.Where(s => !s.Ended).Sum(s => s.DelayMs);

    [TestMethod]
    public void DyingPlaysTheKillsAnimationFrameByFrameAndEndsOnTheLastFrame()
    {
        var world = Arena();
        world.Clips[18] = new(5, 10);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.DyingFrame(18)), world, Steamwoven);
        // frames − 1 delays (DyingAnimationRules.EndMs), the delay being 1000 / the rate.
        Assert.AreEqual(DyingAnimationRules.EndMs(5, 100), Time(steps));
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        CollectionAssert.AreEqual(new[] { (18, 0), (18, 1), (18, 2), (18, 3), (18, 4) }, world.Writes);
        Assert.AreEqual((18, 4), world.Arts[Npc], "the last frame stays");
        Assert.AreEqual(1, world.Finished, "node 9 ran once");
        Assert.AreEqual(0, world.Cleared);
    }

    [TestMethod]
    public void DyingOfACritterLyingInItsFallKeepsTheFallAndGoesStraightToThePool()
    {
        var world = Arena();
        world.Clips[18] = new(5, 10);
        world.Arts[Npc] = (7, 3);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.DyingFrame(18)), world, Steamwoven);
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual(AiRunEnd.EndStep, steps[0].End);
        Assert.AreEqual(0, world.Writes.Count, "no animation was set");
        Assert.AreEqual((7, 3), world.Arts[Npc]);
        Assert.AreEqual(1, world.Finished);
    }

    [TestMethod]
    public void ADyingGoalOfACritterKilledWhileLyingKeepsTheFallEvenWithNoArtWritten()
    {
        var world = Arena();
        world.Clips[18] = new(5, 10);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.DyingFrame(18, lying: true)), world, Steamwoven);
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual(0, world.Writes.Count);
        Assert.AreEqual(1, world.Finished);
    }

    [TestMethod]
    public void DyingWithAnUnreadableHeaderStoresTheHundredMillisecondDelayAndEndsOnItsFirstFrame()
    {
        var world = Arena();
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.DyingFrame(7)), world, Steamwoven);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        Assert.AreEqual(0, Time(steps), "no clip, one frame: the last frame is the first");
        Assert.AreEqual((7, 0), world.Arts[Npc]);
    }

    [TestMethod]
    public void AConsciousKnockDownFallsThenGetsUpAfterTwoHundredMillisecondsAndStands()
    {
        var world = Arena();
        world.Clips[7] = new(4, 10);
        world.Clips[8] = new(3, 20);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.KnockDownFrame()), world, Steamwoven);
        // The fall: 3 delays of 100; the replacement waits 200; the get-up plays 2 delays of 50, then its end node stands the critter up.
        Assert.AreEqual(300 + AiExecutorRules.KnockDownGetUpWaitMs + 100, Time(steps));
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        CollectionAssert.AreEqual(new[] { (7, 0), (7, 1), (7, 2), (7, 3), (8, 0), (8, 1), (8, 2), (0, 0) }, world.Writes);
        Assert.IsFalse(world.Arts.ContainsKey(Npc), "standing");
    }

    [TestMethod]
    public void AnUnconsciousKnockDownKeepsTheLastFrameOfTheFallAndEnds()
    {
        var world = Arena();
        world.Clips[7] = new(4, 10);
        world.UnconsciousSet.Add(Npc);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.KnockDownFrame()), world, Steamwoven);
        Assert.AreEqual(300, Time(steps));
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        Assert.AreEqual((7, 3), world.Arts[Npc], "no end node resets the art");
        Assert.AreEqual(0, world.Calls.Count(c => c == "get up"));
    }

    [TestMethod]
    public void AGetUpInARoundIsRefusedForAnyoneButTheCurrentParticipantAndStandsAtOnce()
    {
        var world = Arena();
        world.Clips[8] = new(3, 20);
        world.Arts[Npc] = (7, 3);
        world.Round = true;
        world.Actor = Other;
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.GetUpFrame()), world, Steamwoven);
        // GT37 a: the start refuses, the last node and the end step run, and the end node stands the critter up with no get-up animation and no 5 points.
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        Assert.IsFalse(world.Arts.ContainsKey(Npc));
        Assert.AreEqual(0, world.Calls.Count(c => c == "get up"));
        Assert.IsFalse(world.Calls.Contains($"ap -{AiExecutorRules.GetUpCost}"));
    }

    [TestMethod]
    public void AGetUpOfTheCurrentParticipantPaysFivePointsAndPlays()
    {
        var world = Arena();
        world.Clips[8] = new(3, 20);
        world.Arts[Npc] = (7, 3);
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 9;
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.GetUpFrame()), world, Steamwoven);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
        Assert.AreEqual(4, world.Points[Npc]);
        Assert.AreEqual(100, Time(steps));
        Assert.IsFalse(world.Arts.ContainsKey(Npc));
    }

    [TestMethod]
    public void AGetUpWithoutPointsEndsTheTurnAndStandsAtOnce()
    {
        var world = Arena();
        world.Clips[8] = new(3, 20);
        world.Arts[Npc] = (7, 3);
        world.Round = true;
        world.Actor = Npc;
        world.Points[Npc] = 2;
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.GetUpFrame()), world, Steamwoven);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        Assert.IsTrue(world.Calls.Contains("end subturn"));
        Assert.AreEqual(0, world.Calls.Count(c => c == "get up"));
    }

    [TestMethod]
    public void TheStunPlaysItsAnimationOnceForEachCountOutsideARoundAndThenClearsTheBit()
    {
        var world = Arena();
        world.Clips[23] = new(3, 10);
        // Constitution 14: the count is (20 − 14) / 2 = 3, so three plays of two delays each.
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.StunFrame(14)), world, Steamwoven);
        Assert.AreEqual(3 * 200, Time(steps));
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        Assert.AreEqual(3, world.Writes.Count(w => w == (23, 0)), "one start per play");
        Assert.AreEqual(1, world.Cleared);
        Assert.IsFalse(world.Arts.ContainsKey(Npc), "the end node stands the critter up");
    }

    [TestMethod]
    public void AStunOfAHighConstitutionStillPlaysOnce()
    {
        var world = Arena();
        world.Clips[23] = new(2, 10);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.StunFrame(25)), world, Steamwoven);
        Assert.AreEqual(100, Time(steps));
        Assert.AreEqual(1, world.Writes.Count(w => w == (23, 0)));
        Assert.AreEqual(1, world.Cleared);
    }

    [TestMethod]
    public void AStunInARoundLoopsItsAnimationAndNeverCountsDownByItself()
    {
        var world = Arena();
        world.Clips[23] = new(2, 10);
        world.Round = true;
        world.Actor = Other;
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.StunFrame(14)), world, Steamwoven, limit: 60);
        Assert.AreEqual(60, steps.Count, "still running");
        Assert.IsFalse(steps.Any(s => s.Ended));
        Assert.AreEqual(0, world.Cleared);
        Assert.AreEqual(3, steps[^1].Run!.Top.Count, "the round's skipped turns count it down (the session's), not the node");
    }

    [TestMethod]
    public void AStunWhoseHeaderDoesNotReadEndsAtOnceAndClearsTheBit()
    {
        var world = Arena();
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.StunFrame(10)), world, Steamwoven);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        Assert.AreEqual(0, Time(steps));
        Assert.AreEqual(1, world.Cleared);
        Assert.IsFalse(world.Arts.ContainsKey(Npc));
    }

    [TestMethod]
    public void AStoneOrAParalysedCritterRefusesTheStunStart()
    {
        foreach (var stone in new[] { true, false })
        {
            var world = Arena();
            world.Clips[23] = new(3, 10);
            (stone ? world.StonedSet : world.ParalysedSet).Add(Npc);
            var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.StunFrame(10)), world, Steamwoven);
            Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End, stone ? "stoned" : "paralysed");
            Assert.AreEqual(0, Time(steps));
            Assert.AreEqual(1, world.Cleared);
        }
    }

    [TestMethod]
    public void AStunOfACritterLyingInItsFallGetsItUpFirst()
    {
        var world = Arena();
        world.Clips[23] = new(2, 10);
        world.Clips[8] = new(2, 10);
        world.Arts[Npc] = (7, 3);
        var steps = ExecutorRulesTests.Drive(Run(AiExecutorRules.StunFrame(18)), world, Steamwoven);
        Assert.IsTrue(world.Calls.IndexOf("get up") >= 0, "the get-up goal was pushed");
        Assert.IsTrue(world.Writes.IndexOf((8, 0)) < world.Writes.IndexOf((23, 0)));
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End);
        Assert.AreEqual(1, world.Cleared);
    }

    [TestMethod]
    public void AnInterruptOfTheStunRunsItsEndNodeWhichClearsTheBitAndStandsTheCritterUp()
    {
        var world = Arena();
        world.Arts[Npc] = (23, 1);
        AiExecutorRules.EndRun(Run(AiExecutorRules.StunFrame(10)), world);
        Assert.AreEqual(1, world.Cleared);
        Assert.IsFalse(world.Arts.ContainsKey(Npc));
        // A knock-down that has set its fall in between keeps its art.
        world = Arena();
        world.Arts[Npc] = (7, 0);
        AiExecutorRules.EndRun(Run(AiExecutorRules.StunFrame(10)), world);
        Assert.AreEqual((7, 0), world.Arts[Npc]);
        Assert.AreEqual(1, world.Cleared);
    }

    [TestMethod]
    public void TheStunEndNodeStandsAFlaggedNpcInTheCrouchArtAndAHeroOrAnUnflaggedOneInTheStandingArt()
    {
        // ARC-AI-1653 (0x42B250, 0x45EDE0): the standing art reads the critter flag (Prowling), not the shown pose.
        var world = Arena();
        world.Movers[Npc] = new(Prowling: true, Fatigue: 20, MaxFatigue: 20);
        world.Arts[Npc] = (AiExecutorRules.StunAnimation, 1);
        AiExecutorRules.EndRun(Run(AiExecutorRules.StunFrame(10)), world);
        Assert.AreEqual((AiExecutorRules.ConcealedAnimation, 0), world.Arts[Npc]);

        world = Arena();
        world.Movers[Npc] = new(Concealed: true, Fatigue: 20, MaxFatigue: 20);
        world.Arts[Npc] = (AiExecutorRules.StunAnimation, 1);
        AiExecutorRules.EndRun(Run(AiExecutorRules.StunFrame(10)), world);
        Assert.IsFalse(world.Arts.ContainsKey(Npc), "the shown pose is the goals' unconceal test only");
    }

    [TestMethod]
    public void TheFrameBuildersCarryTheStartersParameters()
    {
        Assert.AreEqual(3, AiExecutorRules.StunFrame(14).Count);
        Assert.AreEqual(-2, AiExecutorRules.StunFrame(24).Count, "(20 − 24) / 2 truncates toward zero");
        Assert.AreEqual(0, AiExecutorRules.StunFrame(19).Count, "(20 − 19) / 2 = 0");
        Assert.AreEqual(AiExecutorRules.StunAnimation, AiExecutorRules.StunFrame(10).Animation);
        Assert.AreEqual(7, AiExecutorRules.KnockDownFrame().Animation);
        Assert.AreEqual(8, AiExecutorRules.GetUpFrame().Animation);
        Assert.AreEqual(17, AiExecutorRules.DyingFrame(17).Animation);
        Assert.AreEqual(17, AiExecutorRules.DyingAnimationOf(DeathAnimation.Head));
        Assert.AreEqual(19, AiExecutorRules.DyingAnimationOf(DeathAnimation.Leg));
        Assert.AreEqual(18, AiExecutorRules.DyingAnimationOf(DeathAnimation.Body));
        Assert.AreEqual(7, AiExecutorRules.DyingAnimationOf(DeathAnimation.Plain));
        Assert.IsTrue(new[] { GoalType.Dying, GoalType.AnimateStunned, GoalType.KnockDown, GoalType.GetUp }.All(AiExecutorRules.IsCritterState));
        Assert.IsFalse(AiExecutorRules.IsCritterState(GoalType.Attack));
    }
}
