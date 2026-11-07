using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-184 (MIG13 S12d, goal-table.md S12d): the nodes of the hero's skill goal 33 and pick pocket goal 86 over a fake world.</summary>
[TestClass]
public sealed class HeroSkillGoalsTests
{
    private const string Victim = "pp:npc:1";

    private sealed class World : IAiHeroWalkWorld
    {
        public CellCoord At = new(10, 10), Npc = new(11, 10);
        public bool Gone, CannotTurn;
        public readonly List<string> Log = [];

        public CellCoord? Tile() => At;
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => false;
        bool IAiHeroWalkWorld.InRound => false;
        bool IAiHeroWalkWorld.HeroSettled => true;
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

        public AiHeroTarget? ObjectTarget(string target) => Gone ? null : new(Npc);
        public CellCoord? ApproachTile(string target, int range) => new(Npc.X - 1, Npc.Y);
        public bool HeroFaceSkill(string target) { Log.Add("face"); return !CannotTurn; }
        public void HeroPickPocket(string target, string item, bool plant) => Log.Add($"attempt {item} plant={plant}");
        public void HeroPickLock(string target, string item) => Log.Add($"pick {item}");
    }

    private static GoalRun Run(bool plant = false) =>
        new("pc", default, new GoalFrame(GoalType.UseSkill, Victim) { Item = "item:1", Skill = AiExecutorRules.PickpocketSkill, Flags = plant ? GoalFrameFlags.Plant : GoalFrameFlags.None });

    private static AiGoalStepResult Drive(GoalRun run, IAiHeroWalkWorld world, int limit = 40)
    {
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run, world);
            if (step.Ended) return step;
            run = step.Run!;
        }
        throw new InvalidOperationException("the skill did not end");
    }

    [TestMethod]
    public void AVictimWithinRangeOneIsTurnedToAndAttemptedAtOnceAndTheRunFinishes()
    {
        var w = new World();
        var last = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "face", "attempt item:1 plant=False" }, w.Log);
        Assert.AreEqual(AiRunEnd.Finished, last.End, "a pick pocket pops with no animation (the hero plays none)");
    }

    [TestMethod]
    public void APlantCarriesItsFlagToTheAttempt()
    {
        var w = new World();
        Drive(Run(plant: true), w);
        CollectionAssert.AreEqual(new[] { "face", "attempt item:1 plant=True" }, w.Log);
    }

    [TestMethod]
    public void AVictimOutOfRangeIsWalkedToFirst()
    {
        var w = new World { Npc = new(14, 10) };
        Drive(Run(), w);
        Assert.AreEqual(new CellCoord(13, 10), w.At, "the approach goal 8 walks the hero next to the victim");
        Assert.AreEqual("attempt item:1 plant=False", w.Log[^1]);
    }

    [TestMethod]
    public void AVictimGoneOrACriterThatCannotTurnEndsTheRunWithoutAnAttempt()
    {
        var gone = new World { Gone = true };
        Assert.AreEqual(AiRunEnd.EndStep, Drive(Run(), gone).End);
        Assert.AreEqual(0, gone.Log.Count);
        var stuck = new World { CannotTurn = true };
        Assert.AreEqual(AiRunEnd.EndStep, Drive(Run(), stuck).End);
        CollectionAssert.AreEqual(new[] { "face" }, stuck.Log);
    }

    // ARC-DEV-185 (MIG13 S12e): skill 14 goes to goal 67.
    [TestMethod]
    public void APickLocksSkillTurnsToTheDoorAndAsksForThePick()
    {
        var w = new World();
        var last = Drive(new GoalRun("pc", default, new GoalFrame(GoalType.UseSkill, "door:1") { Item = "item:picks", Skill = AiExecutorRules.PickLocksSkill }), w);
        CollectionAssert.AreEqual(new[] { "face", "pick item:picks" }, w.Log);
        Assert.AreEqual(AiRunEnd.Finished, last.End);
    }

    [TestMethod]
    public void APickLocksSkillOutOfRangeWalksFirstAndAnotherSkillIsNotTheHeroGoal()
    {
        var w = new World { Npc = new(14, 10) };
        Drive(new GoalRun("pc", default, new GoalFrame(GoalType.UseSkill, "door:1") { Item = "item:picks", Skill = AiExecutorRules.PickLocksSkill }), w);
        Assert.AreEqual(new CellCoord(13, 10), w.At);
        Assert.AreEqual("pick item:picks", w.Log[^1]);
        var other = new World();
        Assert.AreEqual(AiRunEnd.EndStep, Drive(new GoalRun("pc", default, new GoalFrame(GoalType.UseSkill, Victim) { Item = "item:1", Skill = 10 }), other).End);
        Assert.AreEqual(0, other.Log.Count);
    }
}
