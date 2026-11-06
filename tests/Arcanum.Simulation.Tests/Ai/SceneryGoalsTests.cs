using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-142 (MIG13 S9a, goal-table.md GT67, GT75–GT77): the nodes of the scenery loop goals 1 and 77 over a fake world. Time is the sum of the steps' delays.</summary>
[TestClass]
public sealed class SceneryGoalsTests
{
    private const string Torch = "sector:7:12";

    private sealed class World : IAiSceneryWorld
    {
        public int? Pause = 125;
        public bool NearNow = true, Stoned;
        public int Frame, Frames = 4, Measures, Burns;
        public readonly List<string> Log = [];

        public int? SceneryBegin(string scenery) { Log.Add("begin"); return Pause; }
        public bool SceneryNear(string scenery) { Measures++; Log.Add("near"); return NearNow; }
        public bool SceneryAdvance(string scenery)
        {
            if (Stoned) { Log.Add("stoned"); return false; }
            Frame = (Frame + 1) % Frames;
            Log.Add($"frame{Frame}");
            return Frame % 3 != 1;
        }
        public int? Round;
        public int Reached = 1;
        public readonly List<(bool Normal, bool Doubled)> BurnLog = [];
        public int? FireRound() => Round;
        public int Burn(string burner, bool normal, bool doubled) { Burns++; Log.Add("burn"); BurnLog.Add((normal, doubled)); return Reached; }
    }

    // Steps the run until `until` game milliseconds have passed (every step's delay counts), returning the times of the steps.
    private static List<long> Drive(ref GoalRun run, IAiSceneryWorld world, long until, out AiGoalStepResult? ended)
    {
        var times = new List<long>(); long at = 0; ended = null;
        while (at <= until)
        {
            var step = AiExecutorRules.StepScenery(run, world);
            times.Add(at);
            if (step.Ended) { ended = step; return times; }
            at += step.DelayMs; run = step.Run!;
        }
        return times;
    }

    private static GoalRun Run(bool burning = false) => new(Torch, default, AiExecutorRules.SceneryFrame(burning));

    [TestMethod]
    public void TheGoalSetIsTheLoopPair()
    {
        Assert.IsTrue(AiExecutorRules.IsSceneryGoal(GoalType.AnimateLoop));
        Assert.IsTrue(AiExecutorRules.IsSceneryGoal(GoalType.AnimateLoopFireDamage));
        Assert.IsFalse(AiExecutorRules.IsSceneryGoal(GoalType.AnimateDoorOpen));
        Assert.IsTrue(AiExecutorRules.IsSceneryRunGoal(GoalType.AnimateLoop));
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(GoalType.AnimateLoop), "the NPC AI host leaves a scenery's run alone");
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(GoalType.AnimateLoopFireDamage));
        Assert.AreEqual(GoalType.AnimateLoop, AiExecutorRules.SceneryFrame(false).Goal);
        Assert.AreEqual(GoalType.AnimateLoopFireDamage, AiExecutorRules.SceneryFrame(true).Goal);
        // GT67: priority 1, carried, not counted, and not a no-interrupt overlay (it is the scenery's main run).
        foreach (var goal in new[] { GoalType.AnimateLoop, GoalType.AnimateLoopFireDamage })
        {
            var descriptor = GoalDescriptors.Of(goal)!;
            Assert.AreEqual(1, descriptor.Priority);
            Assert.IsFalse(descriptor.NoInterrupt);
            Assert.IsTrue(descriptor.NotCounted);
        }
    }

    [TestMethod]
    public void ANearLoopBeginsThenStepsOneFramePerPauseAndWraps()
    {
        var world = new World();
        var run = Run();
        var times = Drive(ref run, world, 625, out var ended);
        Assert.IsNull(ended, "the loop never ends by itself");
        // The begin takes no time; the first check comes one pause later, then every pause.
        CollectionAssert.AreEqual(new long[] { 0, 125, 250, 375, 500, 625 }, times);
        CollectionAssert.AreEqual(new[] { "frame1", "frame2", "frame3", "frame0", "frame1" }, world.Log.Where(l => l.StartsWith("frame", StringComparison.Ordinal)).ToList());
        Assert.AreEqual("begin", world.Log[0]);
        Assert.AreEqual(1, world.Frame);
        Assert.AreEqual(0, world.Burns, "goal 1 burns nothing");
    }

    [TestMethod]
    public void ADistanceIsMeasuredAtTheBeginAndThenOnEveryThirdCheck()
    {
        var world = new World();
        var run = Run();
        Drive(ref run, world, 125 * 7, out _);
        // Begin 1, checks 1 and 2 reuse the answer, check 3 measures (GT75), checks 4 and 5 reuse, check 6 measures again; check 7 follows.
        Assert.AreEqual(3, world.Measures);
    }

    [TestMethod]
    public void AFarLoopSleepsEightHundredMillisecondsAndLooksAgain()
    {
        var world = new World { NearNow = false };
        var run = Run();
        var times = Drive(ref run, world, 2_525, out _);
        // Begin (far), check 1 at 125 sleeps 800, check 2 at 925 sleeps 800, check 3 at 1725 measures: still far, 800 more.
        CollectionAssert.AreEqual(new long[] { 0, 125, 925, 1725, 2525 }, times);
        Assert.AreEqual(0, world.Frame, "no frame steps while the player is away");
        // The player walks up: check 3 of the next count (the third measure) sees it.
        world.NearNow = true;
        Drive(ref run, world, 4_000, out _);
        Assert.IsTrue(world.Frame > 0, "a near answer steps again");
    }

    [TestMethod]
    public void AStonedOrUnreadObjectEndsAtTheBeginAndAStonedLoopHoldsItsFrame()
    {
        var refused = new World { Pause = null };
        var run = Run();
        var times = Drive(ref run, refused, 1_000, out var ended);
        Assert.IsNotNull(ended);
        Assert.AreEqual(AiRunEnd.EndStep, ended!.End, "the begin's refusal ends the run");
        Assert.AreEqual(1, times.Count);

        var world = new World();
        run = Run();
        Drive(ref run, world, 250, out _);
        world.Stoned = true;
        var before = world.Frame;
        Drive(ref run, world, 1_000, out ended);
        Assert.IsNull(ended, "a loop stoned after its begin goes on waiting");
        Assert.AreEqual(before, world.Frame);
    }

    [TestMethod]
    public void AZeroPauseIsLeftToTheSteppersOneMillisecondFloor()
    {
        var world = new World { Pause = 0 };
        var run = Run();
        var step = AiExecutorRules.StepScenery(run, world);
        Assert.IsFalse(step.Ended);
        Assert.AreEqual(0, step.DelayMs, "the node returns the stored pause; the stepper makes it at least one animation millisecond");
    }

    [TestMethod]
    public void BurningSceneryBurnsAtTheBeginAndOnEveryFrameWhoseNumberModThreeIsOne()
    {
        var world = new World { Frames = 8 };
        var run = Run(burning: true);
        Drive(ref run, world, 125 * 8, out var ended);
        Assert.IsNull(ended);
        // The begin burns once at once; frames 1 and 4 and 7 (mod 3 = 1) burn when they are entered. 8 steps cover frames 1..8 -> 1, 4, 7.
        Assert.AreEqual(1 + 3, world.Burns);
        Assert.AreEqual("burn", world.Log[2], "after the begin's measure comes the damage");
        Assert.AreEqual("frame1", world.Log[3]);
        Assert.AreEqual("burn", world.Log[4], "frame 1 burns");
    }

    [TestMethod]
    public void ATwoFrameBurningLoopBurnsOnItsOddFrameOnly()
    {
        var world = new World { Frames = 2 };
        var run = Run(burning: true);
        Drive(ref run, world, 125 * 4, out _);
        Assert.AreEqual(1 + 2, world.Burns, "begin, then frame 1 on steps 1 and 3");
    }

    // ---- ARC-DEV-144 (S9c, GT94): the round gate of the fire damage ----

    [TestMethod]
    public void GoalSeventySevenBurnsThreeFireOutsideARoundWithNoGate()
    {
        var world = new World { Frames = 8 };
        var run = Run(burning: true);
        Drive(ref run, world, 125 * 8, out _);
        Assert.IsTrue(world.BurnLog.Count > 0);
        Assert.IsTrue(world.BurnLog.All(b => !b.Normal && !b.Doubled), "3 fire, undoubled, every call");
        Assert.AreEqual(0, run.Top.FireRound, "nothing is stored outside a round");
    }

    [TestMethod]
    public void InARoundTheRunBurnsOnceDoubledAndStoresTheRoundId()
    {
        var world = new World { Frames = 8, Round = 3 };
        var run = Run(burning: true);
        Drive(ref run, world, 125 * 8, out _);
        Assert.AreEqual(1, world.Burns, "the first call of the round burns everyone once; the later calls of the round hurt nobody");
        Assert.IsTrue(world.BurnLog.Single().Doubled);
        Assert.AreEqual(3, run.Top.FireRound);
        // The next round burns again, doubled.
        world.Round = 4;
        Drive(ref run, world, 125 * 8, out _);
        Assert.AreEqual(2, world.Burns);
        Assert.AreEqual(4, run.Top.FireRound);
    }

    [TestMethod]
    public void ARoundWhoseBurnReachedNobodyStoresNothing()
    {
        var world = new World { Frames = 8, Round = 2, Reached = 0 };
        var run = Run(burning: true);
        Drive(ref run, world, 125 * 4, out _);
        Assert.AreEqual(0, run.Top.FireRound, "the slot is written only after a burn that reached a victim");
        Assert.IsTrue(world.Burns > 1, "so every call of the round asks again");
    }
}
