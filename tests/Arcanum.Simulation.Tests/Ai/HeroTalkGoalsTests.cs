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
        public bool Down, InRound, Gone, Refused, NoApproach, Dead;
        public int DiesAfterSteps;
        private int _steps;
        public CellCoord? Approach;
        public readonly List<string> Log = [];

        public CellCoord? Tile() => At;
        int IAiHeroWalkWorld.Encumbrance => 0;
        bool IAiHeroWalkWorld.Down => Down;
        bool IAiHeroWalkWorld.InRound => InRound;

        public bool FarPlan, TurnNeeded;
        public int FollowRoutes, Turns;

        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight)
        {
            if (FarPlan) return new(AiHeroPlanOutcome.Far, default);
            var cells = new List<CellCoord>();
            for (var at = At; at != destination;) cells.Add(at = new(at.X + Math.Sign(destination.X - at.X), at.Y + Math.Sign(destination.Y - at.Y)));
            return new(AiHeroPlanOutcome.Found, [.. cells]);
        }

        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => AiStepBlock.Clear;
        public int BeginStep(CellCoord from, CellCoord next, bool running) { Log.Add("step " + next.X + "," + next.Y); At = next; if (DiesAfterSteps > 0 && ++_steps == DiesAfterSteps) Dead = true; return 100; }
        public bool StepPending(CellCoord from, CellCoord to) => false;
        public bool FollowRoute(CellCoord destination) { FollowRoutes++; return true; }
        public bool RouteFollowing(CellCoord destination) => false;
        public bool NeedsTurn(int rotation) => TurnNeeded;
        public int TurnStep(int rotation) { Turns++; return 0; }

        public AiHeroTarget? ObjectTarget(string target) => Gone ? null : new(Npc, Refused, Range, Portal: DoorFake is not null);
        public FakeDoors? DoorFake;
        public bool Settled = true;
        IAiDoorWorld? IAiHeroWalkWorld.Doors => DoorFake;
        bool IAiHeroWalkWorld.HeroSettled => Settled;
        public CellCoord? ApproachTile(string target, int range) => NoApproach ? null : Approach ?? new(Npc.X - 1, Npc.Y);
        public void OpenDialog(string target) => Log.Add("dialog " + target);
        public int Range = 2;
        public void UseObject(string target) => Log.Add("use " + target);
        public void PickUpObject(string target) => Log.Add("pickup " + target);
    }

    private sealed class FakeDoors(List<string> log) : IAiDoorWorld
    {
        public bool Open, Busy, Refuses, UseAnswer = true;
        public bool DoorValid(string door) => true;
        public bool DoorOpen(string door) => Open;
        public bool DoorBusy(string door) => Busy;
        public bool DoorRefusesOpen(string opener, string door) => Refuses;
        public bool UseDoor(string opener, string door) { log.Add("use-door " + opener); return UseAnswer; }
        public bool DoorHeld(string door) => false;
        public bool DoorStaysOpen(string door) => false;
        public bool DoorRound => false;
        public bool DoorwayOccupied(string door) => false;
        public bool StartDoorRun(string door, GoalType goal, string opener) { log.Add($"door-run {goal} by {opener}"); return true; }
        public bool DoorStep(string door, bool opening) => true;
        public int DoorStepMs(string door) => 100;
        public void PlayDoorSound(string door, DoorSound sound) { }
        public void SnapDoorClosed(string door) { }
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

    // ARC-AI-1320 (GT122): goal 21 itself holds no death test (node 1 refuses only what ObjectTarget reports as Refused or gone). These tests pin that contract of the executor: a world that keeps reporting the body (Dead only records
    // that it died) sees the walk go on and the dialog asked. The viewer port that stopped reporting a dead speaker is covered by the spirit-talk smoke (step 1b).
    [TestMethod]
    public void ATargetThatDiesDuringTheApproachStaysTheTargetAndTheDialogAsksAtTheBody()
    {
        var w = new World { Npc = new(15, 10), DiesAfterSteps = 2 };
        var (last, _) = Drive(Run(), w);
        Assert.IsTrue(w.Dead, "the target died on the way");
        CollectionAssert.AreEqual(new[] { "step 11,10", "step 12,10", "step 13,10", "step 14,10", "dialog " + Npc }, w.Log, "no refusal at node 1: the hero walks on and asks the dialog");
        Assert.AreEqual(new CellCoord(14, 10), w.At, "the approach still ends adjacent (goal 8, range 1)");
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void ATargetThatDiedBeforeTheStandIsTalkedToFromWithinFourCells()
    {
        var w = new World { Npc = new(12, 10), Dead = true };
        var (last, _) = Drive(Run(), w);
        CollectionAssert.AreEqual(new[] { "dialog " + Npc }, w.Log);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    // ARC-AI-1137 (GT122, second read): a talk with Shift carries the no-walk flag 0x100; the walk step answers 0, so the approach never takes a step and the guard kills the run.

    private static GoalRun NoWalkRun() => Run() with { Flags = GoalRunFlags.NoWalk };

    [TestMethod]
    public void AShiftTalkFromFiveCellsAwayWalksNowhereAndEndsSilently()
    {
        var w = new World { Npc = new(15, 10) };
        var (last, steps) = Drive(NoWalkRun(), w);
        Assert.AreEqual(0, w.Log.Count, "no step and no dialog");
        Assert.AreEqual(new CellCoord(10, 10), w.At);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        Assert.AreEqual(AiExecutorReason.NoWalkOutOfRange, last.Reason);
        Assert.IsTrue(steps <= 2, "the run does not plan or wait");
    }

    [TestMethod]
    public void AShiftTalkFromWithinFourCellsTalksAsAlways()
    {
        foreach (var distance in new[] { 1, 4 })
        {
            var w = new World { Npc = new(10 + distance, 10) };
            var (last, _) = Drive(NoWalkRun(), w);
            CollectionAssert.AreEqual(new[] { "dialog " + Npc }, w.Log, $"distance {distance}");
            Assert.AreEqual(AiRunEnd.EndStep, last.End);
        }
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

    // ARC-DEV-174 (MIG13 S11b): the use goals 38 and 81.

    private static GoalRun UseRun(GoalType goal = GoalType.UseObject) => new("pc", default, new GoalFrame(goal, Npc)) { Flags = GoalRunFlags.Running };

    [TestMethod]
    public void TheUseGoalsAreSteppedByTheHeroStepper()
    {
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.UseObject));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.UseContainer));
    }

    [TestMethod]
    public void AUseWithinTwoCellsActsAtOnceAndEndsTheRun()
    {
        foreach (var distance in new[] { 1, 2 })
        {
            var w = new World { Npc = new(10 + distance, 10) };
            var (last, _) = Drive(UseRun(), w);
            CollectionAssert.AreEqual(new[] { "use " + Npc }, w.Log, $"distance {distance}");
            Assert.AreEqual(AiRunEnd.EndStep, last.End);
        }
    }

    [TestMethod]
    public void AUseFromFartherWalksWithinTheRangeThenActs()
    {
        var w = new World { Npc = new(16, 10) };
        Drive(UseRun(), w);
        Assert.AreEqual("use " + Npc, w.Log[^1]);
        Assert.IsTrue(w.Log.Take(w.Log.Count - 1).All(l => l.StartsWith("step")), "only steps before the use");
        Assert.IsTrue(Math.Max(Math.Abs(w.At.X - 16), Math.Abs(w.At.Y - 10)) <= 2, "the hero stands within the use range");
    }

    [TestMethod]
    public void AContainerIsUsedFromOneCellAndTheRangeComesFromTheTarget()
    {
        var w = new World { Npc = new(12, 10), Range = 1 };
        Drive(UseRun(GoalType.UseContainer), w);
        Assert.AreEqual(new CellCoord(11, 10), w.At, "range 1: the hero steps next to it first");
        Assert.AreEqual("use " + Npc, w.Log[^1]);
    }

    [TestMethod]
    public void AUseOfATargetThatIsGoneEndsWithoutAnAct()
    {
        var w = new World { Gone = true };
        Drive(UseRun(), w);
        Assert.AreEqual(0, w.Log.Count);
    }

    // ARC-DEV-175 (MIG13 S11c): the pickup goals 15 and 16.

    private static GoalRun PickupRun() => new("pc", default, new GoalFrame(GoalType.PickupItem, Npc)) { Flags = GoalRunFlags.Running };

    [TestMethod]
    public void ThePickupGoalsAreSteppedByTheHeroStepper()
    {
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.PickupItem));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.AttemptPickup));
    }

    [TestMethod]
    public void AHeroOnTheItemsTileTakesItAtOnceAndTheRunEnds()
    {
        var w = new World { At = new(12, 10), Npc = new(12, 10) };
        var (last, _) = Drive(PickupRun(), w);
        CollectionAssert.AreEqual(new[] { "pickup " + Npc }, w.Log);
        Assert.AreEqual(AiRunEnd.EndStep, last.End, "goal 16's end step ends the whole run, goal 15 included");
    }

    [TestMethod]
    public void AHeroAwayFromTheItemWalksOntoItsTileThenTakesIt()
    {
        // Goal 15 node 10 writes range 0 and pushes goal 8: the hero walks onto the tile, not next to it (the same as the NPC); no step is taken before the walk, none after the pickup.
        var w = new World { At = new(10, 10), Npc = new(13, 10), Approach = new(13, 10) };
        Drive(PickupRun(), w);
        CollectionAssert.AreEqual(new[] { "step 11,10", "step 12,10", "step 13,10", "pickup " + Npc }, w.Log);
        Assert.AreEqual(new CellCoord(13, 10), w.At);
    }

    [TestMethod]
    public void TheApproachOfThePickupIsGoalEightWithRangeZero()
    {
        var w = new World { At = new(10, 10), Npc = new(13, 10), Approach = new(13, 10) };
        var step = AiExecutorRules.StepHeroWalk(PickupRun(), w);
        Assert.IsFalse(step.Ended);
        var frames = step.Run!.Frames;
        Assert.AreEqual(GoalType.MoveNear, frames.First(f => f.Goal == GoalType.MoveNear).Goal);
        Assert.AreEqual(0, frames.First(f => f.Goal == GoalType.MoveNear).Range);
        Assert.AreEqual(GoalType.PickupItem, step.Run.Bottom.Goal);
    }

    [TestMethod]
    public void APickupOfAnItemThatIsGoneEndsWithoutATake()
    {
        var w = new World { Gone = true };
        Drive(PickupRun(), w);
        Assert.AreEqual(0, w.Log.Count);
    }

    [TestMethod]
    public void AnItemTakenDuringTheWalkEndsTheRun()
    {
        // (The original resolves the item's location to its holder's and tries on; the item is gone for Steamwoven, so the run ends.)
        var w = new World { At = new(10, 10), Npc = new(13, 10), Approach = new(13, 10) };
        var run = PickupRun();
        run = AiExecutorRules.StepHeroWalk(run, w).Run!;
        w.Gone = true;
        var (_, _) = Drive(run, w);
        Assert.IsFalse(w.Log.Any(l => l.StartsWith("pickup", StringComparison.Ordinal)));
    }

    // ARC-COMBAT-2736 (tail of ARC-COMBAT-1948, GT148/GT213): a Shift click carries the no-walk flag 0x100; out of reach the walk step answers false and the whole run ends with no step, no act and no message;
    // in reach the goal acts as always (goal 15's reach is the item's own tile, goals 38 and 81's the use range).

    private static GoalRun NoWalk(GoalRun run) => run with { Flags = GoalRunFlags.NoWalk };

    [TestMethod]
    public void AShiftUseOrLootOutOfReachTakesNoStepAndEndsTheRunWithoutAnAct()
    {
        foreach (var goal in new[] { GoalType.UseObject, GoalType.UseContainer })
        {
            var w = new World { Npc = new(16, 10), Range = 1 };
            var (last, _) = Drive(NoWalk(UseRun(goal)), w);
            Assert.AreEqual(0, w.Log.Count, $"{goal}: no step and no use");
            Assert.AreEqual(new CellCoord(10, 10), w.At, goal.ToString());
            Assert.AreEqual(AiRunEnd.EndStep, last.End, goal.ToString());
            Assert.AreEqual(AiExecutorReason.NoWalkStep, last.Reason, goal.ToString());
        }
    }

    [TestMethod]
    public void AShiftUseOrLootWithinReachActsAsAlways()
    {
        foreach (var goal in new[] { GoalType.UseObject, GoalType.UseContainer })
        {
            var w = new World { Npc = new(11, 10), Range = 1 };
            var (last, _) = Drive(NoWalk(UseRun(goal)), w);
            CollectionAssert.AreEqual(new[] { "use " + Npc }, w.Log, goal.ToString());
            Assert.AreEqual(AiRunEnd.EndStep, last.End, goal.ToString());
        }
    }

    [TestMethod]
    public void AShiftUseOneCellBeyondTheTargetsRangeStillWalksNowhere()
    {
        var w = new World { Npc = new(13, 10), Range = 2 };
        var (last, _) = Drive(NoWalk(UseRun()), w);
        Assert.AreEqual(0, w.Log.Count);
        Assert.AreEqual(AiExecutorReason.NoWalkStep, last.Reason);
    }

    [TestMethod]
    public void AShiftPickupOutOfReachTakesNoStepAndEndsTheRunWithoutATake()
    {
        var w = new World { At = new(10, 10), Npc = new(13, 10), Approach = new(13, 10) };
        var (last, _) = Drive(NoWalk(PickupRun()), w);
        Assert.AreEqual(0, w.Log.Count, "no step and no pickup");
        Assert.AreEqual(new CellCoord(10, 10), w.At);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
        Assert.AreEqual(AiExecutorReason.NoWalkStep, last.Reason);
    }

    [TestMethod]
    public void AShiftPickupOnTheItemsOwnTileTakesItAsAlways()
    {
        var w = new World { At = new(12, 10), Npc = new(12, 10) };
        var (last, _) = Drive(NoWalk(PickupRun()), w);
        CollectionAssert.AreEqual(new[] { "pickup " + Npc }, w.Log);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void AShiftUseOrPickupOutOfReachTurnsTheHeroNowhere()
    {
        // 0x42E9B0 answers false for 0x100 before it writes the facing: no turn before the run ends.
        foreach (var run in new[] { UseRun(), UseRun(GoalType.UseContainer), PickupRun() })
        {
            var w = new World { Npc = new(16, 10), Range = 1, TurnNeeded = true, Approach = new(16, 10) };
            var (last, _) = Drive(NoWalk(run), w);
            Assert.AreEqual(0, w.Turns, run.Bottom.Goal.ToString());
            Assert.AreEqual(AiExecutorReason.NoWalkStep, last.Reason, run.Bottom.Goal.ToString());
        }
        var plain = new World { Npc = new(16, 10), Range = 1, TurnNeeded = true };
        Drive(UseRun(), plain);
        Assert.IsTrue(plain.Turns > 0, "without Shift the same walk turns the hero first");
    }

    [TestMethod]
    public void AShiftUseOrPickupOnAFarTargetHandsNothingToTheRouteFollower()
    {
        foreach (var run in new[] { UseRun(), UseRun(GoalType.UseContainer), PickupRun() })
        {
            var w = new World { Npc = new(16, 10), Range = 1, FarPlan = true, Approach = new(16, 10) };
            var (last, _) = Drive(NoWalk(run), w);
            Assert.AreEqual(0, w.FollowRoutes, run.Bottom.Goal.ToString());
            Assert.AreEqual(0, w.Log.Count, run.Bottom.Goal.ToString());
            Assert.AreEqual(AiExecutorReason.NoWalkStep, last.Reason, run.Bottom.Goal.ToString());
        }
        var plain = new World { Npc = new(16, 10), Range = 1, FarPlan = true };
        var again = UseRun();
        for (var i = 0; i < 3 && AiExecutorRules.StepHeroWalk(again, plain) is { Ended: false } step; i++) again = step.Run!;
        Assert.IsTrue(plain.FollowRoutes > 0, "without Shift the far goal goes to the route follower");
    }

    // ARC-DEV-176 (MIG13 S11d): the use of a door or window: goal 38 turns into the door goals.

    [TestMethod]
    public void AUseOfAClosedDoorInRangeBecomesGoalElevenThenTwelveAndStartsTheOpening()
    {
        var w = new World { Npc = new(11, 10) }; w.DoorFake = new(w.Log);
        var (last, _) = Drive(UseRun(), w);
        CollectionAssert.AreEqual(new[] { "use-door pc", "door-run AnimateDoorOpen by pc" }, w.Log);
        Assert.AreEqual(AiRunEnd.Finished, last.End, "goal 12 starts the door's own run and pops: the hero's run is over");
    }

    [TestMethod]
    public void AUseOfAnOpenDoorBecomesGoalFortyNineThenFiftyAndStartsTheClosing()
    {
        var w = new World { Npc = new(11, 10) }; w.DoorFake = new(w.Log) { Open = true };
        Drive(UseRun(), w);
        CollectionAssert.AreEqual(new[] { "door-run AnimateDoorClosed by pc" }, w.Log, "closing charges nothing and tests nothing but the magic hold (GT41)");
    }

    [TestMethod]
    public void ADoorWithARunOfItsOwnIsClosedNotOpened()
    {
        var w = new World { Npc = new(11, 10) }; w.DoorFake = new(w.Log) { Busy = true };
        Drive(UseRun(), w);
        CollectionAssert.AreEqual(new[] { "door-run AnimateDoorClosed by pc" }, w.Log);
    }

    [TestMethod]
    public void ARefusedOpenEndsTheRunWithoutTheUseOrTheDoorRun()
    {
        var w = new World { Npc = new(11, 10) }; w.DoorFake = new(w.Log) { Refuses = true };
        var (last, _) = Drive(UseRun(), w);
        Assert.AreEqual(0, w.Log.Count);
        Assert.AreEqual(AiRunEnd.EndStep, last.End);
    }

    [TestMethod]
    public void ADoorUseThatTheUseNodeRefusesEndsTheRunBeforeTheDoorRun()
    {
        var w = new World { Npc = new(11, 10) }; w.DoorFake = new(w.Log) { UseAnswer = false };
        Drive(UseRun(), w);
        CollectionAssert.AreEqual(new[] { "use-door pc" }, w.Log, "the 2 action points could not be paid");
    }

    [TestMethod]
    public void ADoorFromFarIsApproachedWithinTwoCellsThenOpened()
    {
        var w = new World { Npc = new(16, 10) }; w.DoorFake = new(w.Log);
        Drive(UseRun(), w);
        Assert.AreEqual("door-run AnimateDoorOpen by pc", w.Log[^1]);
        Assert.IsTrue(Math.Max(Math.Abs(w.At.X - 16), Math.Abs(w.At.Y - 10)) <= 2);
    }

    [TestMethod]
    public void TheDoorWaitsUntilTheHeroStandsSettled()
    {
        var w = new World { Npc = new(11, 10), Settled = false }; w.DoorFake = new(w.Log);
        var step = AiExecutorRules.StepHeroWalk(UseRun(), w);
        Assert.IsFalse(step.Ended);
        Assert.IsTrue(step.DelayMs > 0, "the run waits");
        Assert.AreEqual(0, w.Log.Count);
        w.Settled = true;
        var (_, _) = Drive(step.Run!, w);
        Assert.AreEqual("door-run AnimateDoorOpen by pc", w.Log[^1]);
    }

    [TestMethod]
    public void TheDoorGoalsAreSteppedByTheHeroStepper()
    {
        foreach (var goal in new[] { GoalType.OpenDoor, GoalType.AttemptOpenDoor, GoalType.CloseDoor, GoalType.AttemptCloseDoor })
            Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(goal), goal.ToString());
    }
}
