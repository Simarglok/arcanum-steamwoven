using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-139 (MIG13 S8, goal-table.md GT52, GT56–GT58): the nodes of the spell-missile goals 28 and 78 over a fake missile world. The world's line is a number of ticks (a leg's steps run out after
/// that many); time is the sum of the steps' delays.
/// </summary>
[TestClass]
public sealed class MissileGoalsTests
{
    private const string Missile = "missile:1", Target = "oid:target";
    private static readonly CellCoord Start = new(10, 10), There = new(14, 10), Moved = new(14, 12);

    private sealed class World : IAiMissileWorld
    {
        public CellCoord At = Start;
        public CellCoord? TargetAt = There;
        public int TicksPerLeg = 3, Ticked, Pace = 100, Setups, Builds, Releases, Destroyed, Lost;
        public bool LineRefused, ReleaseFails, OffMap, SpeedFails;
        // The tile the missile reaches at the end of a leg (the destination the line was built for).
        public CellCoord LineTo;
        public readonly List<string> Log = [];

        public bool MissileAtDestination(string missile, CellCoord destination) => At == destination;
        public bool MissileTargetHere(string missile, string? target) => target is null || TargetAt is not { } t || t == At;
        public CellCoord? MissileRetarget(string missile, string? target, CellCoord destination) => target is not null && TargetAt is { } t && t != destination ? t : null;
        public bool MissileBuildLine(string missile, CellCoord destination) { Builds++; Log.Add("build " + destination); LineTo = destination; return !LineRefused; }
        public bool MissileSpeed(string missile) => !SpeedFails;
        public bool MissileRelease(string missile) { Releases++; Log.Add("release"); return !ReleaseFails; }
        public void MissileDestroy(string missile) { Destroyed++; Log.Add("destroy"); }
        public void MissileLost(string missile) { Lost++; Log.Add("lost"); }
        public int MissileLegSetup(string missile) { Setups++; Ticked = 0; return Pace; }
        public MissileTickResult MissileTick(string missile)
        {
            if (OffMap) return MissileTickResult.OffMap;
            Ticked++;
            if (Ticked < TicksPerLeg) return MissileTickResult.Continue;
            At = LineTo;
            return MissileTickResult.LegEnded;
        }
    }

    private static GoalRun Run(GoalFrame frame) => new(Missile, default, frame);

    private static List<(AiGoalStepResult Step, long At)> Drive(GoalRun run, IAiMissileWorld world, int limit = 400)
    {
        var steps = new List<(AiGoalStepResult, long)>(); long at = 0;
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepMissile(run, world);
            steps.Add((step, at));
            if (step.Ended) return steps;
            at += step.DelayMs; run = step.Run!;
        }
        throw new InvalidOperationException("the missile run did not end");
    }

    [TestMethod]
    public void TheGoalSetIsTheMissileGoals()
    {
        Assert.IsTrue(AiExecutorRules.IsMissileGoal(GoalType.ShootSpell));
        Assert.IsTrue(AiExecutorRules.IsMissileGoal(GoalType.AttemptMoveStraightSpell));
        Assert.IsFalse(AiExecutorRules.IsMissileGoal(GoalType.AnimateDoorOpen));
        Assert.IsTrue(AiExecutorRules.IsMissileRunGoal(GoalType.ShootSpell));
        Assert.IsFalse(AiExecutorRules.IsMissileRunGoal(GoalType.AttemptMoveStraightSpell), "78 is pushed, never a run's bottom");
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(GoalType.AnimateDoorOpen));
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(GoalType.ShootSpell));
        Assert.IsFalse(AiExecutorRules.IsObjectRunGoal(GoalType.Attack));
        Assert.IsTrue(AiExecutorRules.MissileFrame(Target, There, 7).PathPending, "the path flag starts set (GT58)");
    }

    [TestMethod]
    public void AMissileAlreadyOnItsTargetReleasesAtOnce()
    {
        var world = new World { At = There };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(1, steps.Count, "nodes 1, 4, 5, 6 run in one step");
        Assert.AreEqual(AiRunEnd.Finished, steps[0].Step.End);
        CollectionAssert.AreEqual(new[] { "release", "destroy", "lost" }, world.Log, "the pop runs the end node, which finds the missile destroyed already in the real world");
        Assert.AreEqual(0, world.Builds, "no line is built");
    }

    [TestMethod]
    public void ALegIsBuiltOnceFlownTickByTickAndReleasedAfterThePace()
    {
        var world = new World { TicksPerLeg = 3, Pace = 120 };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(1, world.Builds, "the path flag is set, so the first leg builds its line (GT58)");
        Assert.AreEqual(1, world.Setups);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
        Assert.AreEqual(1, world.Releases);
        // the setup restarts at once, then a tick every 35 ms, the last one popping after the pace.
        var delays = steps.Take(steps.Count - 1).Select(s => s.Step.DelayMs).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 35, 35, 120 }, delays);
        Assert.AreEqual(2 * AiExecutorRules.MissileTickMs + 120, steps[^1].At - steps[0].Step.DelayMs);
        Assert.AreEqual("destroy", world.Log[^2], "the release is followed by the destroy node");
    }

    [TestMethod]
    public void TheLegPushesGoalSeventyEightWhichPopsToTwentyEightAtItsFirstNode()
    {
        var world = new World { TicksPerLeg = 2 };
        var step = AiExecutorRules.StepMissile(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(GoalType.AttemptMoveStraightSpell, step.Run!.Top.Goal);
        Assert.AreEqual(2, step.Run.Depth);
        Assert.IsFalse(step.Run.Frames[0].PathPending, "the builder cleared the flag");
        Assert.AreEqual(1, step.Run.Frames[0].Node, "the pushed goal's parent resumes at its first node");
        Assert.AreEqual(There, step.Run.Top.Tile);
    }

    [TestMethod]
    public void AMissileThatStopsShortFliesTheSameLineAgainWithoutBuildingIt()
    {
        var world = new World { TicksPerLeg = 2 };
        // a destination the leg never reaches the first time: the line ends elsewhere
        var steps = new List<(AiGoalStepResult, long)>();
        var run = Run(AiExecutorRules.MissileFrame(Target, There, 1));
        var shortBy = true;
        for (var i = 0; i < 60; i++)
        {
            var step = AiExecutorRules.StepMissile(run, world);
            if (step.Ended) break;
            run = step.Run!;
            if (shortBy && world.Setups == 1 && world.Ticked == 1) { world.LineTo = Start; shortBy = false; }
        }
        // the leg ended on the start tile: not on the destination, the path flag is clear, so the same line is flown again (node 2 -> 8)
        Assert.AreEqual(1, world.Builds, "the line is built once");
        Assert.IsTrue(world.Setups >= 2, "a second leg's setup follows");
    }

    [TestMethod]
    public void AMovedTargetIsFollowedOnANewLeg()
    {
        var world = new World { TicksPerLeg = 2 };
        var run = Run(AiExecutorRules.MissileFrame(Target, There, 1));
        var steps = 0;
        for (; steps < 80; steps++)
        {
            var step = AiExecutorRules.StepMissile(run, world);
            if (step.Ended) break;
            run = step.Run!;
            // the target moves while the first leg flies
            if (world.Setups == 1 && world.Ticked == 1) world.TargetAt = Moved;
        }
        Assert.AreEqual(2, world.Builds, "the retarget sets the path flag: a second line");
        Assert.AreEqual(Moved, world.LineTo);
        Assert.AreEqual(1, world.Releases);
        Assert.AreEqual(2, world.Setups);
    }

    [TestMethod]
    public void ARefusedLineEndsTheRunThroughItsEndNode()
    {
        var world = new World { LineRefused = true };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        CollectionAssert.AreEqual(new[] { "build " + There, "lost" }, world.Log);
        Assert.AreEqual(0, world.Releases);
    }

    [TestMethod]
    public void AFailedReleaseEndsTheRun()
    {
        var world = new World { At = There, ReleaseFails = true };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        CollectionAssert.AreEqual(new[] { "release", "lost" }, world.Log);
    }

    [TestMethod]
    public void AStepOffTheMapEndsTheRunWithItsEndNodes()
    {
        var world = new World { OffMap = true };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(1, world.Lost, "the end node of 28 runs once, 78 has none (GT52)");
    }

    [TestMethod]
    public void ATileCastReleasesWhereItLandsWithNoTarget()
    {
        var world = new World { TargetAt = null, TicksPerLeg = 1 };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(null, There, 4)), world);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
        Assert.AreEqual(1, world.Releases);
    }

    [TestMethod]
    public void AFailedSpeedEndsTheRun()
    {
        var world = new World { SpeedFails = true };
        var steps = Drive(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(1, world.Lost);
    }

    [TestMethod]
    public void AnInterruptRunsTheEndNodeTopDownAndAKillRunsNone()
    {
        var world = new World { TicksPerLeg = 5 };
        var step = AiExecutorRules.StepMissile(Run(AiExecutorRules.MissileFrame(Target, There, 1)), world);
        AiExecutorRules.EndMissileRun(step.Run!, world);
        Assert.AreEqual(1, world.Lost, "28's end node runs once for a stack of 28 over 78");
        world.Lost = 0;
        AiExecutorRules.EndMissileRun(step.Run! with { Flags = GoalRunFlags.Killed }, world);
        Assert.AreEqual(0, world.Lost);
    }

    [TestMethod]
    public void AGoalThatIsNoMissileGoalIsKilled()
    {
        var step = AiExecutorRules.StepMissile(Run(new GoalFrame(GoalType.Animate, Missile)), new World());
        Assert.AreEqual(AiRunEnd.Killed, step.End);
    }
}
