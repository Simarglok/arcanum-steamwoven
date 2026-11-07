using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-183 (MIG13 S12c, goal-table.md S12c): the nodes of the hero's casting goals 26 and 27 over a fake world.</summary>
[TestClass]
public sealed class HeroCastGoalsTests
{
    private sealed class World : IAiHeroWalkWorld
    {
        public AiCastGoalStart Start = AiCastGoalStart.Started;
        public bool Face = true, Released = true, ActionFrame = true;
        public AiCastSwing Swing = new(true, 130, 70);
        public readonly List<string> Log = [];

        public CellCoord? Tile() => new(10, 10);
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => false;
        bool IAiHeroWalkWorld.InRound => false;
        bool IAiHeroWalkWorld.HeroSettled => true;
        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight) => new(AiHeroPlanOutcome.Found, []);
        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running) => 0;
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) => false;
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => false;
        public int TurnStep(int rotation) => 0;

        public AiCastGoalStart HeroStartCast(int run) { Log.Add($"start {run}"); return Start; }
        public bool HeroFaceCast(int run) { Log.Add("face"); return Face; }
        public AiCastSwing HeroBeginCast(int run) { Log.Add("begin"); return Swing; }
        public bool HeroCastActionFrame(int run) { Log.Add("action"); return ActionFrame; }
        public bool HeroReleaseCast(int run) { Log.Add("release"); return Released; }
        public void HeroCastEnds(int run, HeroCastEnd how) => Log.Add("ends " + how);
    }

    private static GoalRun Run() => new("pc", default, new GoalFrame(GoalType.CastSpell, "npc:1") { SpellRun = 7 });

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
        throw new InvalidOperationException("the cast did not end");
    }

    [TestMethod]
    public void ACastStartsTurnsPlaysToTheActionFrameReleasesAndPopsAfterTheRest()
    {
        var w = new World();
        var (last, delays) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "start 7", "face", "begin", "action", "ends Popped" }, w.Log);
        CollectionAssert.AreEqual(new[] { 130, 70 }, delays.Where(d => d > 1).ToArray(), "the release at the action frame, the pop after the rest of the play");
        Assert.AreEqual(AiRunEnd.Finished, last.End);
    }

    [TestMethod]
    public void AGoalWithoutAPlayReleasesAtOnceAndEndsThroughTheEndStep()
    {
        var w = new World { Swing = new(false) };
        var (last, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "start 7", "face", "begin", "release", "ends EndStep" }, w.Log);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void ARefusedStartOrTurnEndsTheRunAndFreesItUnpaid()
    {
        var refused = new World { Start = AiCastGoalStart.Refused };
        Assert.AreEqual(AiRunEnd.EndStep, Drive(Run(), refused).Last.End);
        CollectionAssert.AreEqual(new[] { "start 7", "ends Refused" }, refused.Log);
        var cannotTurn = new World { Face = false };
        Assert.AreEqual(AiRunEnd.EndStep, Drive(Run(), cannotTurn).Last.End);
        CollectionAssert.AreEqual(new[] { "start 7", "face", "ends Refused" }, cannotTurn.Log);
    }

    [TestMethod]
    public void AnActionFrameThatReleasesNothingPopsTheGoal()
    {
        var w = new World { ActionFrame = false };
        var (last, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "start 7", "face", "begin", "action", "ends Popped" }, w.Log);
        Assert.AreEqual(AiRunEnd.Finished, last.End);
    }

    [TestMethod]
    public void ACastGoalWithoutItsMagicRunEndsAtOnce()
    {
        var w = new World();
        var (last, _) = Drive(new GoalRun("pc", default, new GoalFrame(GoalType.CastSpell, "npc:1")), w);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        Assert.AreEqual(0, w.Log.Count);
    }
}
