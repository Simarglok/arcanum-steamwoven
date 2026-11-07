using System.Collections.Immutable;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-169 (MIG13 S11a, goal-table.md S11a): the nodes of the hero's talk goal 21 and its approach goal 8 over a fake world.</summary>
[TestClass]
public sealed class HeroTalkGoalsTests
{
    private const string Npc = "npc:1";

    private sealed class World : IAiHeroWalkWorld
    {
        public CellCoord At = new(10, 10), Npc = new(12, 10);
        public bool Down, InRound, Gone, Refused, NoApproach;
        public CellCoord? Approach;
        public readonly List<string> Log = [];

        public CellCoord? Tile() => At;
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => Down;
        bool IAiHeroWalkWorld.InRound => InRound;

        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight)
        {
            var cells = new List<CellCoord>();
            for (var at = At; at != destination;) cells.Add(at = new(at.X + Math.Sign(destination.X - at.X), at.Y + Math.Sign(destination.Y - at.Y)));
            return new(AiHeroPlanOutcome.Found, [.. cells]);
        }

        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running) { Log.Add("step " + next.X + "," + next.Y); At = next; return 100; }
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) => false;
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => false;
        public int TurnStep(int rotation) => 0;

        public AiHeroTalkTarget? TalkTarget(string target) => Gone ? null : new(Npc, Refused);
        public CellCoord? ApproachTile(string target) => NoApproach ? null : Approach ?? new(Npc.X - 1, Npc.Y);
        public void OpenDialog(string target) => Log.Add("dialog " + target);
    }

    private static GoalRun Run(bool running = true) => new("pc", default, new GoalFrame(GoalType.Talk, Npc)) { Flags = running ? GoalRunFlags.Running : GoalRunFlags.None };

    private static (AiGoalStepResult Last, int Steps) Drive(GoalRun run, World world, int limit = 100)
    {
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run, world);
            if (step.Ended) return (step, i + 1);
            run = step.Run!;
        }
        throw new InvalidOperationException("the talk did not end");
    }

    [TestMethod]
    public void TheHeroTalksFromAtMostFourCellsAway()
    {
        // 0x4AFBB0 answers 5 for a player character and the distance must be strictly below it: 4 talks, 5 walks (the next test).
        foreach (var distance in new[] { 1, 2, 4 })
        {
            var w = new World { Npc = new(10 + distance, 10) };
            var (last, _) = Drive(Run(), w);
            CollectionAssert.AreEqual(new[] { "dialog " + Npc }, w.Log, $"distance {distance}");
            Assert.AreEqual(AiRunEnd.EndStep, last.End, "the act takes the end step (0x42AF00 answers 0)");
        }
    }

    [TestMethod]
    public void AHeroFiveCellsAwayWalksNextToTheNpcThenTalksFromThere()
    {
        var w = new World { Npc = new(15, 10) };
        var (last, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "step 11,10", "step 12,10", "step 13,10", "step 14,10", "dialog " + Npc }, w.Log);
        Assert.AreEqual(new CellCoord(14, 10), w.At, "the approach ends adjacent to the NPC (goal 8's range 1)");
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void TheApproachIsGoalEightWithRangeOneOverTheTalk()
    {
        var w = new World { Npc = new(20, 10) };
        var step = AiExecutorRules.StepHeroWalk(Run(), w);
        // The first step runs nodes 1 to 6 of the talk, the plan of goal 8 and the walk's own nodes until the first wait.
        Assert.AreEqual(GoalType.Talk, step.Run!.Bottom.Goal);
        Assert.IsTrue(step.Run.Frames.Any(f => f.Goal == GoalType.MoveNear && f.Range == AiExecutorRules.HeroTalkApproachRange && f.Target == Npc));
    }

    [TestMethod]
    public void AnNpcThatIsGoneOrRefusedEndsTheRunWithoutADialog()
    {
        var gone = new World { Gone = true };
        Assert.AreEqual(AiRunEnd.Finished, Drive(Run(), gone).Last.End, "node 1 pops the bottom goal: the run finishes, silently");
        Assert.AreEqual(0, gone.Log.Count);
        var refused = new World { Refused = true };
        Assert.AreEqual(AiRunEnd.Finished, Drive(Run(), refused).Last.End);
        Assert.AreEqual(0, refused.Log.Count);
    }

    [TestMethod]
    public void AHeroThatIsDownEndsTheRun()
    {
        var w = new World { Down = true };
        Assert.AreEqual(AiRunEnd.EndStep, Drive(Run(), w).Last.End);
        Assert.AreEqual(0, w.Log.Count);
    }

    [TestMethod]
    public void NoApproachCellEndsTheRunAfterThePauseOutsideARoundAtOnceInOne()
    {
        var far = new World { Npc = new(30, 10), NoApproach = true };
        var outside = Drive(Run(), far);
        Assert.AreEqual(0, far.Log.Count);
        Assert.IsTrue(outside.Steps >= 2, "the pause goal 6 waits a second first");
        var inRound = new World { Npc = new(30, 10), NoApproach = true, InRound = true };
        Assert.AreEqual(1, Drive(Run(), inRound).Steps, "in a round the run ends at once");
    }

    [TestMethod]
    public void AnApproachCellTheHeroStandsOnIsNoWay()
    {
        var w = new World { Npc = new(30, 10), Approach = new(10, 10) };
        var (_, steps) = Drive(Run(), w);
        Assert.AreEqual(0, w.Log.Count, "no walk and no endless planning");
        Assert.IsTrue(steps < 10);
    }
}
