using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-141 (MIG13 S8c, goal-table.md GT59, GT60, GT62): the nodes of the knockback goals 43 and 64 over a fake world. Time is the sum of the steps' delays.</summary>
[TestClass]
public sealed class KnockbackGoalsTests
{
    private const string Pushed = "oid:pushed";
    private static readonly CellCoord Start = new(10, 10), There = new(14, 10);

    private sealed class World : IAiKnockbackWorld
    {
        public CellCoord At = Start;
        public int TicksToEnd = 3, Ticked, Builds, Setups;
        public bool LineRefused, SetupFails;
        public readonly List<string> Log = [];

        public bool KnockbackAtDestination(string target, CellCoord destination) => At == destination;
        public bool KnockbackBuildLine(string target, CellCoord destination) { Builds++; Log.Add("build"); return !LineRefused; }
        public bool KnockbackSetup(string target) { Setups++; Log.Add("setup"); return !SetupFails; }
        public bool KnockbackTick(string target) { Ticked++; Log.Add("tick"); return Ticked < TicksToEnd; }
    }

    private static List<(AiGoalStepResult Step, long At)> Drive(GoalRun run, IAiKnockbackWorld world, int limit = 100)
    {
        var steps = new List<(AiGoalStepResult, long)>(); long at = 0;
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepKnockback(run, world);
            steps.Add((step, at));
            if (step.Ended) return steps;
            at += step.DelayMs; run = step.Run!;
        }
        throw new InvalidOperationException("the knockback run did not end");
    }

    private static GoalRun Run() => new(Pushed, default, AiExecutorRules.KnockbackFrame(There));

    [TestMethod]
    public void TheGoalSetIsTheKnockbackPair()
    {
        Assert.IsTrue(AiExecutorRules.IsKnockbackGoal(GoalType.Knockback));
        Assert.IsTrue(AiExecutorRules.IsKnockbackGoal(GoalType.AttemptMoveStraightKnockback));
        Assert.IsFalse(AiExecutorRules.IsKnockbackGoal(GoalType.AttemptMoveStraight));
        Assert.IsTrue(AiExecutorRules.IsKnockbackRunGoal(GoalType.Knockback));
        Assert.IsFalse(AiExecutorRules.IsKnockbackRunGoal(GoalType.AttemptMoveStraightKnockback), "64 is pushed by 43, never the bottom");
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(GoalType.Knockback), "the NPC AI host leaves a knockback's run alone");
        Assert.IsTrue(AiExecutorRules.KnockbackFrame(There).PathPending, "the path flag starts set (GT58)");
        Assert.AreEqual(5, GoalDescriptors.Of(GoalType.Knockback)!.Priority);
        Assert.AreEqual(3, GoalDescriptors.Of(GoalType.AttemptMoveStraightKnockback)!.Priority, "during the slide the run is a priority-3 run (GT62)");
    }

    [TestMethod]
    public void TheRunBuildsItsLineOnceThenTicksEvery35msAndEndsWithTheEndStep()
    {
        var world = new World { TicksToEnd = 3 };
        var steps = Drive(Run(), world);
        Assert.AreEqual(1, world.Builds);
        Assert.AreEqual(1, world.Setups);
        CollectionAssert.AreEqual(new[] { "build", "setup", "tick", "tick", "tick" }, world.Log);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End, "every ending of goal 64 is the end step of the whole run (GT60)");
        // The push chain ends at the setup's restart (0), then a tick answers 35 ms and the third tick ends the run in its step.
        CollectionAssert.AreEqual(new[] { 0, 35, 35 }, steps.Take(steps.Count - 1).Select(s => s.Step.DelayMs).ToArray());
    }

    [TestMethod]
    public void ARunOnItsDestinationAlreadyPopsWithNoLineAndNoTick()
    {
        var world = new World { At = There };
        var steps = Drive(Run(), world);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
        Assert.AreEqual(0, world.Builds + world.Setups + world.Ticked);
    }

    [TestMethod]
    public void ARefusedLineEndsTheRunWithTheEndStep()
    {
        var world = new World { LineRefused = true };
        var steps = Drive(Run(), world);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(0, world.Setups + world.Ticked, "the slide never begins");
    }

    [TestMethod]
    public void AFailedSetupEndsTheRun()
    {
        var world = new World { SetupFails = true };
        var steps = Drive(Run(), world);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(0, world.Ticked);
    }

    [TestMethod]
    public void TheSlideStackNeverPopsBackIntoTheKnockback()
    {
        var world = new World { TicksToEnd = 100 };
        var run = Run();
        for (var i = 0; i < 5; i++)
        {
            var step = AiExecutorRules.StepKnockback(run, world);
            Assert.IsFalse(step.Ended);
            run = step.Run!;
            Assert.AreEqual(2, run.Frames.Length, "43 beneath, 64 on top");
            Assert.AreEqual(GoalType.Knockback, run.Bottom.Goal);
            Assert.AreEqual(GoalType.AttemptMoveStraightKnockback, run.Top.Goal);
        }
    }
}
