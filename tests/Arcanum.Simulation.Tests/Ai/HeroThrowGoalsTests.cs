using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-182 (MIG13 S12b, goal-table.md S12b): the nodes of the hero's throw goal 37 over a fake world.</summary>
[TestClass]
public sealed class HeroThrowGoalsTests
{
    private const string Item = "kit:7";

    private sealed class World : IAiHeroWalkWorld
    {
        public bool Settled = true, Refuse, ThrowRefused;
        public readonly List<string> Log = [];
        public readonly List<AiExecutorReason> Ends = [];

        public CellCoord? Tile() => new(10, 10);
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => false;
        bool IAiHeroWalkWorld.InRound => false;
        bool IAiHeroWalkWorld.HeroSettled => Settled;
        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight) => new(AiHeroPlanOutcome.Found, []);
        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running) => 0;
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) => false;
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => false;
        public int TurnStep(int rotation) => 0;

        public AiAttackSwing? HeroBeginThrow(string item, CellCoord tile) { Log.Add($"begin {item} {tile.X},{tile.Y}"); return Refuse ? null : new(300, 200); }
        public bool HeroThrowItem(string item, CellCoord tile) { Log.Add("throw"); return !ThrowRefused; }
        public void HeroThrowEnds(string item, AiExecutorReason reason) { Log.Add("ends " + reason); Ends.Add(reason); }
    }

    private static GoalRun Run() => new("pc", default, new GoalFrame(GoalType.ThrowItem, tile: new CellCoord(14, 10)) { Item = Item });

    private static (AiGoalStepResult Last, List<int> Delays) Drive(GoalRun run, IAiHeroWalkWorld world, int limit = 20)
    {
        var delays = new List<int>();
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run, world);
            if (step.Ended) return (step, delays);
            delays.Add(step.DelayMs);
            run = step.Run!;
        }
        throw new InvalidOperationException("the throw did not end");
    }

    [TestMethod]
    public void TheThrowBeginsWaitsForTheActionFrameThrowsAndEndsAfterTheRest()
    {
        var w = new World();
        var (last, delays) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "begin kit:7 14,10", "throw", "ends Thrown" }, w.Log);
        CollectionAssert.AreEqual(new[] { 300, 200 }, delays, "the throw is at the action frame, the end after the rest of the animation");
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void AHeroThatIsNotSettledWaitsForTheStandAndBeginsNothing()
    {
        var w = new World { Settled = false };
        var step = AiExecutorRules.StepHeroWalk(Run(), w);
        Assert.IsFalse(step.Ended);
        Assert.AreEqual(AiExecutorRules.HeroRoutePollMs, step.DelayMs);
        Assert.AreEqual(0, w.Log.Count);
    }

    [TestMethod]
    public void AThrowThatCannotBeginEndsTheRunAndSaysSo()
    {
        var w = new World { Refuse = true };
        var (last, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "begin kit:7 14,10", "ends ActionRefused" }, w.Log);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void ARefusedThrowAtTheActionFrameEndsTheRunWithoutTheRest()
    {
        var w = new World { ThrowRefused = true };
        var (_, delays) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "begin kit:7 14,10", "throw", "ends ActionRefused" }, w.Log);
        CollectionAssert.AreEqual(new[] { 300 }, delays);
    }

    [TestMethod]
    public void AThrowWithoutItsItemOrTileEndsAtOnce()
    {
        var w = new World();
        var (last, _) = Drive(new GoalRun("pc", default, new GoalFrame(GoalType.ThrowItem)), w);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        Assert.AreEqual(0, w.Log.Count);
    }
}
