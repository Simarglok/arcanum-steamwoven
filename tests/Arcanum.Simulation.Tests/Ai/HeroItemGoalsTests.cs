using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-365 (T-g, ledger IA8, IA9, IA11, IA12): the nodes of the hero's item use goals 39 (on an object) and 41 (on a tile) and the use on a tile, over a fake world.</summary>
[TestClass]
public sealed class HeroItemGoalsTests
{
    private const string Item = "kit:4", Target = "npc:9";

    private sealed class World : IAiHeroWalkWorld
    {
        public CellCoord Hero = new(10, 10), There = new(10, 11);
        public CellCoord? Approach;
        public readonly List<string> Log = [];

        public CellCoord? Tile() => Hero;
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => false;
        bool IAiHeroWalkWorld.InRound => false;
        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight) => new(AiHeroPlanOutcome.Found, []);
        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running) => 0;
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) => false;
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => false;
        public int TurnStep(int rotation) => 0;

        public AiHeroTarget? ObjectTarget(string target) => target == Target ? new(There) : null;
        public CellCoord? ApproachTile(string target, int range) => Approach;
        public CellCoord? ItemTileApproach(CellCoord tile, int range) { Log.Add($"approach {tile.X},{tile.Y} range={range}"); return Approach; }
        public void HeroUseItem(string item, string target) => Log.Add($"use {item} on {target}");
        public void HeroUseItemOnTile(string item, CellCoord tile) => Log.Add($"use {item} at {tile.X},{tile.Y}");
    }

    private static GoalRun OnObject(GoalRunFlags flags = GoalRunFlags.None) => new("pc", default, new GoalFrame(GoalType.UseItem, Target) { Item = Item }, flags);
    private static GoalRun OnTile(CellCoord tile) => new("pc", default, new GoalFrame(GoalType.UseItemOnTile, tile: tile) { Item = Item });

    [TestMethod]
    public void BothGoalsAreGoalsOfTheHeroStepper()
    {
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.UseItem));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.UseItemOnTile));
    }

    [TestMethod]
    public void AnObjectNextToTheHeroIsUsedAtOnceAndTheRunEnds()
    {
        var w = new World();
        var step = AiExecutorRules.StepHeroWalk(OnObject(), w);
        CollectionAssert.AreEqual(new[] { $"use {Item} on {Target}" }, w.Log);
        Assert.AreEqual(AiRunEnd.EndStep, step.End);
        Assert.AreEqual(AiExecutorReason.ItemUsed, step.Reason);
    }

    [TestMethod]
    public void AnObjectOutOfRangeIsApproachedByTheMoveNearGoalWithRangeOneAndNothingIsUsedYet()
    {
        var w = new World { There = new(10, 15) };
        var step = AiExecutorRules.StepHeroWalk(OnObject(), w);
        Assert.AreEqual(0, w.Log.Count, "the use waits for the approach");
        Assert.IsFalse(step.Ended);
        var frames = step.Run!.Frames;
        Assert.AreEqual(GoalType.UseItem, frames[0].Goal);
        Assert.AreEqual(AiExecutorRules.HeroItemUseRange, frames[0].Range, "node 1 writes the range");
        Assert.IsTrue(frames.Any(f => f.Goal == GoalType.MoveNear && f.Range == AiExecutorRules.HeroItemUseRange && f.Target == Target), "goal 8 under goal 39");
    }

    [TestMethod]
    public void AGoalStartedWithTheNoWalkFlagActsFromWhereTheHeroStands()
    {
        var w = new World { There = new(10, 15) };
        var step = AiExecutorRules.StepHeroWalk(OnObject(GoalRunFlags.NoWalk), w);
        CollectionAssert.AreEqual(new[] { $"use {Item} on {Target}" }, w.Log, "IA8: Shift sets the flag and the walk does not move");
        Assert.AreEqual(AiRunEnd.EndStep, step.End);
    }

    [TestMethod]
    public void ATargetThatIsGoneEndsTheRunWithoutUsingTheItem()
    {
        var w = new World();
        var run = new GoalRun("pc", default, new GoalFrame(GoalType.UseItem, "npc:gone") { Item = Item });
        var step = AiExecutorRules.StepHeroWalk(run, w);
        Assert.AreEqual(0, w.Log.Count);
        Assert.AreEqual(AiRunEnd.EndStep, step.End);
        Assert.AreEqual(AiExecutorReason.ObjectGone, step.Reason);
    }

    [TestMethod]
    public void ATileWithinRangeIsUsedAtOnceAndAFarOneIsWalkedToFirst()
    {
        var near = new World();
        var done = AiExecutorRules.StepHeroWalk(OnTile(new(10, 11)), near);
        CollectionAssert.AreEqual(new[] { $"use {Item} at 10,11" }, near.Log);
        Assert.AreEqual(AiRunEnd.EndStep, done.End);

        var far = new World { Approach = new(10, 14) };
        var walking = AiExecutorRules.StepHeroWalk(OnTile(new(10, 15)), far);
        CollectionAssert.AreEqual(new[] { $"approach 10,15 range={AiExecutorRules.HeroItemUseRange}" }, far.Log);
        Assert.IsFalse(walking.Ended);
        Assert.IsTrue(walking.Run!.Frames.Any(f => f.Goal is GoalType.MoveToTile or GoalType.AttemptMove), "goal 3 walks to the cell next to the tile");
    }

    [TestMethod]
    public void ATileWithNoWayNextToItEndsTheRunWithNoPath()
    {
        var w = new World { Approach = null };
        var step = AiExecutorRules.StepHeroWalk(OnTile(new(10, 15)), w);
        Assert.AreEqual(AiRunEnd.EndStep, step.End);
        Assert.AreEqual(AiExecutorReason.NoPath, step.Reason);
        Assert.IsFalse(w.Log.Any(l => l.StartsWith("use ", StringComparison.Ordinal)));
    }

    // ---- the use on a tile (0x462FC0, IA12) ----

    private sealed class Tile(AiUsedItem used) : IItemUseTileWorld
    {
        public readonly List<string> Log = [];
        public bool InRound { get; set; }
        public string? RoundActor { get; set; }
        public AiUsedItem UsedItem(string user, string item) => used;
        public bool RunUseScript(string user, string item, string? target) { Log.Add("script"); return true; }
        public void OpenTargeting(string user, string item) => Log.Add("cursor");
        public void UseTrapDevice(string user, string item, string? target) => Log.Add("trap object");
        public void UseTrapDeviceOnTile(string user, string item, SpellLocation tile) => Log.Add($"trap tile {tile.X},{tile.Y}");
        public void ReadWritten(string user, string item) => Log.Add("read");
        public bool Lockable(string target) => true;
        public void StartSkillWithItem(string user, string item, string target, int skill) => Log.Add($"skill {skill}");
        public void UseItemMagic(string user, string item, string? target) => Log.Add("magic object");
        public void UseItemMagicOnTile(string user, string item, SpellLocation tile) => Log.Add($"magic tile {tile.X},{tile.Y}");
        public void SpendItem(string item) => Log.Add("spend");
        public void Consume(string id, int cost) => Log.Add($"consume {cost}");
    }

    private static AiUsedItem Used(AiItemKind kind, bool tooHard = false, bool needsTarget = true, bool trap = false, int charges = 0, bool magical = false) =>
        new(kind, tooHard, needsTarget, trap, charges, magical, false, false);

    [TestMethod]
    public void AChargedScrollCastsAtTheTileWithNoScrollTestAndNoUseScriptAndIsSpent()
    {
        var w = new Tile(Used(AiItemKind.Scroll, tooHard: true, charges: 1));
        var result = AiExecutorRules.UseItemOnTileNow(w, "pc", Item, new SpellLocation(7, 8));
        CollectionAssert.AreEqual(new[] { "magic tile 7,8", "spend" }, w.Log, "IA12 corrects TP1: no message 113, no use script");
        Assert.AreEqual((AiItemUseStep.Magic, true), (result.Step, result.Spent));
    }

    [TestMethod]
    public void ATrapDeviceTakesTheTileFormAndSpendsNothingHere()
    {
        var w = new Tile(Used(AiItemKind.Generic, trap: true));
        var result = AiExecutorRules.UseItemOnTileNow(w, "pc", Item, new SpellLocation(7, 8));
        CollectionAssert.AreEqual(new[] { "trap tile 7,8" }, w.Log);
        Assert.AreEqual(AiItemUseStep.TrapDevice, result.Step);
    }

    [TestMethod]
    public void AnItemWithNoMagicChargesFourPointsOnTheHerosSubturnAndTakesItsTypesBranch()
    {
        var food = new Tile(Used(AiItemKind.Food)) { InRound = true, RoundActor = "pc" };
        var eaten = AiExecutorRules.UseItemOnTileNow(food, "pc", Item, new SpellLocation(1, 1));
        CollectionAssert.AreEqual(new[] { "consume 4", "spend" }, food.Log);
        Assert.AreEqual((AiItemUseStep.Spent, true, true), (eaten.Step, eaten.Charged, eaten.Spent));

        var note = new Tile(Used(AiItemKind.Written));
        Assert.AreEqual(AiItemUseStep.Read, AiExecutorRules.UseItemOnTileNow(note, "pc", Item, new SpellLocation(1, 1)).Step);
        CollectionAssert.AreEqual(new[] { "read" }, note.Log);

        var lockpick = new Tile(Used(AiItemKind.Generic));
        Assert.AreEqual(AiItemUseStep.Nothing, AiExecutorRules.UseItemOnTileNow(lockpick, "pc", Item, new SpellLocation(1, 1)).Step, "no lockpick or heal branch on a tile");
        Assert.AreEqual(0, lockpick.Log.Count);
    }
}
