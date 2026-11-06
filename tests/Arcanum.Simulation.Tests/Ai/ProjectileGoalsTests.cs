using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-140 (MIG13 S8b, goal-table.md GT53, GT54, GT62): the nodes of the projectile goal 36 over a fake projectile world. The world's flight is a number of ticks (the tick answers "ended" after
/// that many); time is the sum of the steps' delays.
/// </summary>
[TestClass]
public sealed class ProjectileGoalsTests
{
    private const string Projectile = "projectile:1", Target = "oid:target";
    private static readonly CellCoord Start = new(10, 10), There = new(14, 10), Back = new(10, 10);

    private sealed class World : IAiProjectileWorld
    {
        public CellCoord At = Start;
        public int TicksToEnd = 3, Ticked, Builds, Setups, Ends;
        public bool LineRefused, FaceFails, SetupFails;
        // A tick at which a boomerang turns back (0: none) and the tile it turns to.
        public int TurnAtTick;
        public CellCoord TurnTo = Back;
        public readonly List<string> Log = [];

        public bool ProjectileAtDestination(string projectile, CellCoord destination) => At == destination;
        public bool ProjectileFace(string projectile) { Log.Add("face"); return !FaceFails; }
        public bool ProjectileBuildLine(string projectile, CellCoord destination) { Builds++; Log.Add("build " + destination); return !LineRefused; }
        public bool ProjectileSetup(string projectile) { Setups++; Log.Add("setup"); return !SetupFails; }
        public ProjectileTickAnswer ProjectileTick(string projectile)
        {
            Ticked++;
            if (TurnAtTick == Ticked) return new(ProjectileTickResult.Turned, TurnTo);
            return Ticked >= TicksToEnd ? ProjectileTickAnswer.Ended : ProjectileTickAnswer.Continue;
        }
        public void ProjectileEnded(string projectile) { Ends++; Log.Add("end"); }
    }

    private static GoalRun Run(GoalFrame frame) => new(Projectile, default, frame);

    private static List<(AiGoalStepResult Step, long At)> Drive(GoalRun run, IAiProjectileWorld world, int limit = 400)
    {
        var steps = new List<(AiGoalStepResult, long)>(); long at = 0;
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepProjectile(run, world);
            steps.Add((step, at));
            if (step.Ended) return steps;
            at += step.DelayMs; run = step.Run!;
        }
        throw new InvalidOperationException("the projectile run did not end");
    }

    [TestMethod]
    public void TheGoalSetIsTheProjectileGoal()
    {
        Assert.IsTrue(AiExecutorRules.IsProjectileGoal(GoalType.Projectile));
        Assert.IsFalse(AiExecutorRules.IsProjectileGoal(GoalType.ShootSpell));
        Assert.IsTrue(AiExecutorRules.IsProjectileRunGoal(GoalType.Projectile));
        Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(GoalType.Projectile), "the NPC AI host leaves a projectile's run alone");
        Assert.IsFalse(AiExecutorRules.IsMissileRunGoal(GoalType.Projectile));
        Assert.IsTrue(AiExecutorRules.ProjectileFrame(Target, There).PathPending, "the path flag starts set (GT58)");
        Assert.AreEqual(5, GoalDescriptors.Of(GoalType.Projectile)!.Priority, "priority 5: only the map's close ends it (GT62)");
    }

    [TestMethod]
    public void ARunBuildsItsLineOnceThenTicksEvery35msAndTheEndPopsToItsEndNode()
    {
        var world = new World { TicksToEnd = 3 };
        var steps = Drive(Run(AiExecutorRules.ProjectileFrame(Target, There)), world);
        Assert.AreEqual(1, world.Builds, "the path flag is set, so the first node chain builds the line");
        Assert.AreEqual(1, world.Setups);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End, "the pop of the one goal ends the run");
        CollectionAssert.AreEqual(new[] { "face", "build " + There, "setup", "end" }, world.Log, "nodes 3..7 then the end node after the last tick");
        // the path node restarts at once, then a tick every 35 ms; the third tick ends the flight in the same step.
        CollectionAssert.AreEqual(new[] { 0, 35, 35 }, steps.Take(steps.Count - 1).Select(s => s.Step.DelayMs).ToArray());
        Assert.AreEqual(1, world.Ends);
    }

    [TestMethod]
    public void AProjectileStartingOnItsDestinationPopsAtOnceWithNoLineAndOnlyTheEndNode()
    {
        var world = new World { At = There };
        var steps = Drive(Run(AiExecutorRules.ProjectileFrame(Target, There)), world);
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual(AiRunEnd.Finished, steps[0].Step.End);
        CollectionAssert.AreEqual(new[] { "end" }, world.Log, "no line, no tick (a throw at the thrower's own tile: no strike, GT53)");
        Assert.AreEqual(0, world.Ticked);
    }

    [TestMethod]
    public void ARefusedLineAFailedFacingAndAFailedPathNodeEachPopToTheEndNode()
    {
        var refused = new World { LineRefused = true };
        Assert.AreEqual(AiRunEnd.Finished, Drive(Run(AiExecutorRules.ProjectileFrame(Target, There)), refused)[^1].Step.End);
        CollectionAssert.AreEqual(new[] { "face", "build " + There, "end" }, refused.Log);
        var faceless = new World { FaceFails = true };
        Drive(Run(AiExecutorRules.ProjectileFrame(Target, There)), faceless);
        CollectionAssert.AreEqual(new[] { "face", "end" }, faceless.Log);
        var failing = new World { SetupFails = true };
        Drive(Run(AiExecutorRules.ProjectileFrame(Target, There)), failing);
        CollectionAssert.AreEqual(new[] { "face", "build " + There, "setup", "end" }, failing.Log);
        Assert.AreEqual(0, failing.Ticked);
    }

    [TestMethod]
    public void ABoomerangsTurnTakesTheThrowersTileClearsTheFlightFlagAndBuildsTheLineAnew()
    {
        // The projectile is mid-flight (not on the thrower's tile) when it turns, so the destination check of node 3 passes.
        var world = new World { TicksToEnd = 6, TurnAtTick = 2, TurnTo = Back, At = new(12, 10) };
        var run = Run(AiExecutorRules.ProjectileFrame(Target, There));
        var builds = new List<int>();
        CellCoord? tileAfterTurn = null;
        for (var i = 0; i < 40; i++)
        {
            var step = AiExecutorRules.StepProjectile(run, world);
            if (step.Ended) break;
            run = step.Run!;
            builds.Add(world.Builds);
            if (world.Ticked == 2 && tileAfterTurn is null) tileAfterTurn = run.Top.Tile;
        }
        Assert.AreEqual(Back, tileAfterTurn, "the goal's destination is the thrower's tile (BR4)");
        Assert.AreEqual(2, world.Builds, "the path flag was set again: a second line, built from the tile the projectile turned on");
        Assert.AreEqual(2, world.Setups, "and the path node ran again");
        Assert.AreEqual(1, world.Ends);
    }

    [TestMethod]
    public void AnInterruptRunsTheEndNodeOnceAndAKillRunsNone()
    {
        var world = new World { TicksToEnd = 50 };
        var step = AiExecutorRules.StepProjectile(Run(AiExecutorRules.ProjectileFrame(Target, There)), world);
        Assert.IsFalse(step.Ended);
        AiExecutorRules.EndProjectileRun(step.Run!, world);
        Assert.AreEqual(1, world.Ends);
        world.Ends = 0;
        AiExecutorRules.EndProjectileRun(step.Run! with { Flags = GoalRunFlags.Killed }, world);
        Assert.AreEqual(0, world.Ends);
    }

    [TestMethod]
    public void AGoalThatIsNoProjectileGoalIsKilled()
    {
        var step = AiExecutorRules.StepProjectile(Run(new GoalFrame(GoalType.Animate, Projectile)), new World());
        Assert.AreEqual(AiRunEnd.Killed, step.End);
    }
}
