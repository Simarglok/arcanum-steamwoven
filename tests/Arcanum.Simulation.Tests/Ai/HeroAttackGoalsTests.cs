using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-181 (MIG13 S12a, goal-table.md S12a): the nodes of the hero's attack goal 18 and attempt-attack goal 19 over a fake world.</summary>
[TestClass]
public sealed class HeroAttackGoalsTests
{
    private const string Foe = "atk:npc:1";

    private sealed class World : IAiHeroWalkWorld
    {
        public CellCoord At = new(10, 10), Target = new(13, 10);
        public bool Dead, Gone, Impossible, Unpaid, Repeat, Settled = true, NoApproach, StopOnReach;
        public int RefuseSteps;
        public int Reach = 1;
        public readonly List<string> Log = [];
        public readonly List<AiExecutorReason> Ends = [];

        public CellCoord? Tile() => At;
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => false;
        bool IAiHeroWalkWorld.InRound => false;
        bool IAiHeroWalkWorld.HeroSettled => Settled;

        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight)
        {
            var cells = new List<CellCoord>();
            for (var at = At; at != destination;) cells.Add(at = new(at.X + Math.Sign(destination.X - at.X), at.Y + Math.Sign(destination.Y - at.Y)));
            return new(AiHeroPlanOutcome.Found, [.. cells]);
        }

        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running)
        {
            if (RefuseSteps > 0) { RefuseSteps--; Log.Add("refused"); return 0; }
            Log.Add("step " + next.X + "," + next.Y); At = next; return 100;
        }
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) => false;
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => false;
        public int TurnStep(int rotation) => 0;

        public AiHeroAttackTarget? AttackTarget(string target) => Gone ? null : new(Target, Dead);
        public bool AttackPossible(string target) => !Impossible;
        public bool InAttackReach(string target) => AiExecutorRules.Distance(At, Target) <= Reach;
        public CellCoord? AttackApproachTile(string target) => NoApproach ? null : new(Target.X - 1, Target.Y);
        public bool AttackWalkInReach(string target) => StopOnReach && InAttackReach(target);
        public AiAttackSwing? HeroBeginAttack(string target) { Log.Add("swing"); return Unpaid ? null : new(300, 200); }
        public void HeroAttackHit(string target) => Log.Add("hit");
        public bool HeroAttackRepeats(string target) { Log.Add("repeat?"); return Repeat; }
        public void AttackEnds(string target, AiExecutorReason reason) => Ends.Add(reason);
    }

    private static GoalRun Run() => new("pc", default, new GoalFrame(GoalType.Attack, Foe)) { Flags = GoalRunFlags.Running };

    // Steps the run until it ends or the limit passes; every wait is added to the clock.
    private static (AiGoalStepResult Last, int Waited, List<int> Delays) Drive(GoalRun run, IAiHeroWalkWorld world, int limit = 200)
    {
        var waited = 0; var delays = new List<int>();
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run, world);
            if (step.Ended) return (step, waited, delays);
            waited += step.DelayMs; delays.Add(step.DelayMs);
            run = step.Run!;
        }
        throw new InvalidOperationException("the attack did not end");
    }

    [TestMethod]
    public void ATargetInReachSwingsHitsAtTheActionFrameAndEndsWhenNothingRepeats()
    {
        var w = new World { Target = new(11, 10) };
        var (last, waited, delays) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "swing", "hit", "repeat?" }, w.Log);
        Assert.AreEqual(AiRunEnd.Finished, last.End, "the attempt pops the bottom goal: the run is finished, not ended by an end step");
        Assert.AreEqual(300 + 200, waited, "the hit comes at the action frame, the repeat test after the rest of the animation");
        CollectionAssert.AreEqual(new[] { 300, 200 }, delays);
    }

    [TestMethod]
    public void ATargetOutOfReachIsWalkedToThenAttacked()
    {
        var w = new World { Target = new(14, 10) };
        var (last, _, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "step 11,10", "step 12,10", "step 13,10", "swing", "hit", "repeat?" }, w.Log);
        Assert.AreEqual(new CellCoord(13, 10), w.At, "the walk goes to the approach cell next to the target");
        Assert.AreEqual(AiRunEnd.Finished, last.End);
    }

    [TestMethod]
    public void ARepeatingAttackReplacesTheGoalByANewAttackFiveMillisecondsLater()
    {
        var w = new World { Target = new(11, 10), Repeat = true };
        GoalRun? run = Run();
        var delays = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run!, w);
            Assert.IsFalse(step.Ended);
            run = step.Run; delays.Add(step.DelayMs);
        }
        CollectionAssert.AreEqual(new[] { 300, 200, RealTimeCombatRules.RepeatDelayMs, 300 }, delays);
        Assert.AreEqual(GoalType.AttemptAttack, run!.Bottom.Goal, "the new attack ran node 1 of goal 18 and replaced itself by goal 19 again");
        CollectionAssert.AreEqual(new[] { "swing", "hit", "repeat?", "swing" }, w.Log, "the new attack starts from node 1 with the reach test and the swing");
    }

    [TestMethod]
    public void ATargetThatIsDeadOrGoneEndsTheRunBeforeAnyWalk()
    {
        foreach (var w in new[] { new World { Dead = true }, new World { Gone = true } })
        {
            var (last, _, _) = Drive(Run(), w);
            Assert.AreEqual(AiRunEnd.Finished, last.End);
            Assert.AreEqual(0, w.Log.Count);
        }
    }

    [TestMethod]
    public void AnAttackThatIsNotPossibleEndsTheRun()
    {
        var w = new World { Impossible = true };
        var (last, _, _) = Drive(Run(), w);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        Assert.AreEqual(AiExecutorReason.AttackImpossible, last.Reason);
        Assert.AreEqual(0, w.Log.Count);
    }

    [TestMethod]
    public void AnUnpaidAttackKillsTheRunAndSaysSo()
    {
        var w = new World { Target = new(11, 10), Unpaid = true };
        var (last, _, _) = Drive(Run(), w);
        Assert.AreEqual(AiRunEnd.Killed, last.End);
        CollectionAssert.AreEqual(new[] { "swing" }, w.Log);
        CollectionAssert.AreEqual(new[] { AiExecutorReason.Unpaid }, w.Ends);
    }

    [TestMethod]
    public void ATargetWithNoWayToItEndsTheRunBlocked()
    {
        var w = new World { Target = new(20, 10), NoApproach = true };
        var (last, _, _) = Drive(Run(), w);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        CollectionAssert.AreEqual(new[] { AiExecutorReason.AttackBlocked }, w.Ends);
    }

    [TestMethod]
    public void AWalkThatArrivesAndLeavesTheTargetOutOfReachIsNotTriedForEver()
    {
        // The shot stays closed (a blocker on the line): the host gives three approach cells of one click, then none, and the run ends.
        var w = new World { Target = new(14, 10), Reach = 1 };
        var (last, _, _) = Drive(Run(), new ShotClosed(w));
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        Assert.AreEqual(AiExecutorReason.AttackBlocked, last.Reason);
        CollectionAssert.AreEqual(new[] { AiExecutorReason.AttackBlocked }, w.Ends);
        Assert.AreEqual(0, w.Log.Count(l => l == "swing"));
    }

    // The same world with a reach test that never passes and an approach cell the hero keeps leaving (so each walk has somewhere to go).
    private sealed class ShotClosed(World inner) : IAiHeroWalkWorld
    {
        private int _walks;
        public CellCoord? Tile() => inner.At;
        public int Encumbrance => 0;
        public bool Down => false;
        public bool InRound => false;
        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight) => inner.PlanWalk(destination, running, straight);
        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running) => inner.BeginStep(from, next, running);
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) => false;
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => false;
        public int TurnStep(int rotation) => 0;
        public AiHeroAttackTarget? AttackTarget(string target) => new(inner.Target, false);
        public bool AttackPossible(string target) => true;
        public bool InAttackReach(string target) => false;
        public CellCoord? AttackApproachTile(string target) { _walks++; return _walks > 3 ? null : new(inner.At.X + (_walks % 2 == 0 ? -1 : 1), inner.At.Y); }
        public void AttackEnds(string target, AiExecutorReason reason) => inner.AttackEnds(target, reason);
    }

    [TestMethod]
    public void AStepTheBodyRefusesEndsOnlyTheWalkAndTheAttackPlansAnotherApproach()
    {
        // A talk's or use's refused step ends the run (a PC's walk re-routes nothing); an attack's walk ends alone and goal 18 asks for another approach cell.
        var w = new World { Target = new(13, 10), RefuseSteps = 1 };
        var (last, _, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "refused", "step 11,10", "step 12,10", "swing", "hit", "repeat?" }, w.Log);
        Assert.AreEqual(AiRunEnd.Finished, last.End);
    }

    [TestMethod]
    public void TheWalkOfAnAttackStopsAtTheFirstCellWithAClearShot()
    {
        // A ranged weapon (reach 5): the walk goes toward the cell next to the target (19) and stops at 15, the first cell from which the reach test passes.
        var w = new World { Target = new(20, 10), Reach = 5, StopOnReach = true };
        Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "step 11,10", "step 12,10", "step 13,10", "step 14,10", "step 15,10", "swing", "hit", "repeat?" }, w.Log);
    }

    [TestMethod]
    public void AHeroThatHasNotComeToAHaltWaitsForItsStepBeforeSwinging()
    {
        var w = new World { Target = new(11, 10), Settled = false };
        var step = AiExecutorRules.StepHeroWalk(Run(), w);
        Assert.IsFalse(step.Ended);
        Assert.AreEqual(AiExecutorRules.HeroRoutePollMs, step.DelayMs);
        Assert.AreEqual(0, w.Log.Count, "no swing starts while the hero walks");
    }
}
