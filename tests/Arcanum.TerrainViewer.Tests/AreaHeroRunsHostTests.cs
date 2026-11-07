using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-146 (MIG13 S10b, goal-table.md §3.5, GT14): the hero's walk as a run of the goal table (goal 3, or 4 for a runner; owner pc) over a session and a fake body. The walk starter's gate, the
/// retarget in place, the level-3 interrupt and its end node (which stops the route). ARC-DEV-149 (S10b-2b): the table's walk nodes over the hero ports: the plan, the turn before the first step, the steps one
/// at a time and the plan again after each segment, a refused step, the no-path wait, a far goal the route follower takes. No original data.
/// </summary>
[TestClass]
public sealed class AreaHeroRunsHostTests
{
    private static readonly CellCoord Goal = new(110, 100), Other = new(120, 90);

    private sealed class Feet : IAreaHeroRunsViewer
    {
        public bool May = true;
        public CellCoord? Following;
        public readonly List<(CellCoord Destination, string Why)> Stops = [];
        public bool MayStart() => May;
        public int StunEnds;
        public void HeroStunEnds() => StunEnds++;
        public bool Forced, FellDown, StandsUp;
        public void HeroFalls(bool on) => Forced = on;
        public bool HeroFellDown => FellDown;
        public bool HeroStandsUp => StandsUp;
        public bool MayWait = true;
        public bool HeroStateMayWait(bool getUp) => MayWait;
        public bool Walking(CellCoord destination) => Following == destination;
        public void StopWalking(CellCoord destination, string why) { if (Following == destination) { Following = null; Stops.Add((destination, why)); } }
        public int Facing = 5;
        public bool Holds = true;
        public readonly List<int> Shown = [];
        public readonly List<int> Snaps = [];
        public int HeroFacing => Facing;
        public bool TurnHolds => Holds;
        public void ShowFacing(int rotation) { Facing = rotation; Shown.Add(rotation); }
        public void SnapFacing(int rotation) { Facing = rotation; Snaps.Add(rotation); }

        // ARC-DEV-149: the fake body walks the table's steps at once (its tile is the cell its step enters) and costs StepMs for each.
        public CellCoord Tile = new(100, 100);
        public int Encumbrance = 0, StepMs = 100;
        public bool Down, Settled = true, Round, Far, NoPath, Refuse, LastBlocked, StraightFirst;
        public readonly List<(CellCoord From, CellCoord Next)> Steps = [];
        public readonly List<(CellCoord Destination, bool Straight)> Plans = [];
        public bool RouteRefuses;
        public CellCoord? HeroTile => Tile;
        public int HeroEncumbrance => Encumbrance;
        public bool HeroDown => Down;
        public bool HeroSettled => Settled;
        public bool InRound => Round;

        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight)
        {
            Plans.Add((destination, straight));
            if (Far) return new(AiHeroPlanOutcome.Far);
            if (NoPath) return new(AiHeroPlanOutcome.NoPath);
            var cells = new List<CellCoord>();
            for (var at = Tile; at != destination;) cells.Add(at = new(at.X + Math.Sign(destination.X - at.X), at.Y + Math.Sign(destination.Y - at.Y)));
            return new(AiHeroPlanOutcome.Found, [.. cells], straight || StraightFirst);
        }

        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => LastBlocked && lastOfSegment ? AiStepBlock.Occupied : AiStepBlock.Clear;

        public int BeginStep(CellCoord from, CellCoord next, bool running)
        {
            if (Refuse) return 0;
            Steps.Add((from, next));
            if (Queue) Pending = (Tile, next); else Tile = next;
            return StepMs;
        }

        // ARC-DEV-163: Queue holds a given step until the test begins it (the body takes it at the end of the step under way).
        public bool Queue;
        public (CellCoord From, CellCoord To)? Pending;
        public bool StepPending(CellCoord from, CellCoord to) => Pending is { } p && p.From == from && p.To == to;

        public bool FollowRoute(CellCoord destination)
        {
            if (RouteRefuses) return false;
            Following = destination;
            return true;
        }

        // ARC-DEV-169: the talk target "npc:1" stands at Npc; the dialog is asked for through OpenDialog.
        public CellCoord Npc = new(130, 100);
        public bool NpcGone;
        public readonly List<string> Dialogs = [];
        public AiHeroTarget? ObjectTarget(string target) => NpcGone ? null : new(Npc, UseRange: 2);
        public bool ApproachOnTarget;
        public CellCoord? ApproachTile(string target, int range) => ApproachOnTarget && range == 0 ? Npc : new(Npc.X - 1, Npc.Y);
        public void OpenDialog(string target) => Dialogs.Add(target);
        public readonly List<string> Uses = [];
        public void UseObject(string target) => Uses.Add(target);
        public readonly List<string> Pickups = [];
        public void PickUpObject(string target) => Pickups.Add(target);

        // ARC-DEV-181: the attack target "atk:npc:1" stands at Npc; in reach when the hero is within AttackReach of it.
        public int AttackReach = 1;
        public readonly List<string> Attacks = [];
        public AiHeroAttackTarget? AttackTarget(string target) => NpcGone ? null : new(Npc);
        public bool AttackPossible(string target) => true;
        public bool InAttackReach(string target) => AiExecutorRules.Distance(Tile, Npc) <= AttackReach;
        public CellCoord? AttackApproachTile(string target) => new(Npc.X - 1, Npc.Y);
        public AiAttackSwing? HeroBeginAttack(string target) { Attacks.Add("swing"); return new(300, 200); }
        public void HeroAttackHit(string target) => Attacks.Add("hit");
        public void AttackEnds(string target, AiExecutorReason reason) => Attacks.Add("ends " + reason);
        // ARC-DEV-182: the throw goal 37 (animation: begin at once, action frame 300 ms, rest 200 ms).
        public readonly List<string> Throws = [];
        public AiAttackSwing? HeroBeginThrow(string item, CellCoord tile) { Throws.Add($"begin {item} {tile.X},{tile.Y}"); return new(300, 200); }
        public bool HeroThrowItem(string item, CellCoord tile) { Throws.Add("throw"); return true; }
        public void HeroThrowEnds(string item, AiExecutorReason reason) => Throws.Add("ends " + reason);
        // ARC-DEV-183: the casting goals (a play with the action frame at 300 ms and 200 ms of rest).
        public readonly List<string> Casts = [];
        public AiCastGoalStart HeroStartCast(int run) { Casts.Add("start " + run); return AiCastGoalStart.Started; }
        public AiCastSwing HeroBeginCast(int run) { Casts.Add("begin"); return new(true, 300, 200); }
        public bool HeroCastActionFrame(int run) { Casts.Add("release"); return true; }
        public void HeroCastEnds(int run, HeroCastEnd how) => Casts.Add("ends " + how);
        public void HeroCastInterrupted(int run, string why) => Casts.Add("interrupted " + why);
        // ARC-DEV-184: the pick pocket goals (the victim "pp:npc:1" stands at Npc).
        public readonly List<string> Skills = [];
        public bool HeroFaceSkill(string target) { Skills.Add("face"); return true; }
        public void HeroPickPocket(string target, string item, bool plant) => Skills.Add($"attempt {item} plant={plant}");
        public void HeroPickLock(string target, string item) => Skills.Add($"pick {item}");
    }

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public Feet Feet { get; } = new();
        public AreaHeroRunsHost Host { get; }
        public FrameStep Step { get; }

        public Fixture()
        {
            Host = new AreaHeroRunsHost(Session, Feet);
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, null, null, () => Host) };
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public GoalRun Walk => Session.QueryGoals().RunsOf(AreaHeroRunsHost.Hero).Single();
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void AWalkStartsAGoalThreeRunOfThePcAtPriorityTwoAndARunnerStartsGoalFour()
    {
        using var f = new Fixture();
        Assert.AreEqual(HeroWalkRunResult.Started, f.Host.Walk(Goal, run: false));
        Assert.AreEqual(GoalType.MoveToTile, f.Walk.Bottom.Goal);
        Assert.AreEqual(Goal, f.Walk.Bottom.Tile);
        Assert.AreEqual("pc", f.Walk.Owner);
        Assert.AreEqual(2, f.Host.HighestPriority, "the table walks: the run is a priority-2 goal while it is on the table");
        Assert.IsFalse(f.Walk.Flags.HasFlag(GoalRunFlags.Running));

        using var g = new Fixture();
        Assert.AreEqual(HeroWalkRunResult.Started, g.Host.Walk(Goal, run: true));
        Assert.AreEqual(GoalType.RunToTile, g.Walk.Bottom.Goal);
        Assert.IsTrue(g.Walk.Flags.HasFlag(GoalRunFlags.Running));
    }

    [TestMethod]
    public void ASecondClickRetargetsTheSameRunInPlaceAndTheSameTileChangesNothing()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        var handle = f.Walk.Handle;
        Assert.AreEqual(HeroWalkRunResult.Unchanged, f.Host.Walk(Goal, false));
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(Other, false));
        Assert.AreEqual(handle, f.Walk.Handle, "the same run");
        Assert.AreEqual(Other, f.Walk.Bottom.Tile);
        Assert.IsTrue(f.Walk.PathFlags.HasFlag(GoalPathFlags.Replan));
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        // A click with the other gait finds the same activity (goal 3 and 4 are aliases, GT3): retargeted too.
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(Goal, true));
        Assert.AreEqual(handle, f.Walk.Handle);
    }

    [TestMethod]
    public void TheMovementGateRefusesTheWalkAndLeavesNoRun()
    {
        using var f = new Fixture();
        f.Feet.May = false;
        Assert.AreEqual(HeroWalkRunResult.Refused, f.Host.Walk(Goal, false));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        f.Feet.May = true;
        f.Host.Walk(Goal, false);
        f.Feet.May = false;
        Assert.AreEqual(HeroWalkRunResult.Refused, f.Host.Walk(Other, false), "a running walk is not retargeted through the gate either");
        Assert.AreEqual(Goal, f.Walk.Bottom.Tile);
    }

    [TestMethod]
    public void TheTableWalksTheHeroOneStepAtATimeAndTheArrivalEndsTheRun()
    {
        using var f = new Fixture();
        f.Feet.Tile = new(100, 100);
        f.Host.Walk(new CellCoord(103, 100), false);
        f.Run(2000);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the hero stands on the destination: the run is over");
        CollectionAssert.AreEqual(new[] { new CellCoord(101, 100), new CellCoord(102, 100), new CellCoord(103, 100) }, f.Feet.Steps.Select(x => x.Next).ToArray());
        CollectionAssert.AreEqual(new[] { new CellCoord(100, 100), new CellCoord(101, 100), new CellCoord(102, 100) }, f.Feet.Steps.Select(x => x.From).ToArray());
        Assert.AreEqual(3, f.Host.WalkSteps);
        Assert.AreEqual(1, f.Feet.Plans.Count, "one plan covers the segment");
        Assert.AreEqual(0, f.Feet.Stops.Count, "a run that ended itself asks nothing of the route");
        Assert.AreEqual(0, f.Host.RouteEnds);
    }

    [TestMethod]
    public void EachStepWaitsItsTimeAndALongPathPlansAgainAfterEverySegmentOfEightSteps()
    {
        using var f = new Fixture();
        f.Feet.StepMs = 200;
        f.Host.Walk(new CellCoord(120, 100), true);
        f.Run(600);
        Assert.IsTrue(f.Feet.Steps.Count is >= 2 and <= 4, "a step every 200 ms: " + f.Feet.Steps.Count);
        f.Run(30_000);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(new CellCoord(120, 100), f.Feet.Tile);
        Assert.AreEqual(20, f.Feet.Steps.Count);
        Assert.AreEqual(3, f.Feet.Plans.Count, "20 steps are three segments of at most eight, each planned when the last one ended");
    }

    [TestMethod]
    public void AHeroFacingAnotherWayTurnsBeforeTheFirstStepAndAStandingFacingDoesNot()
    {
        using var f = new Fixture();
        f.Feet.Facing = 1;
        f.Host.Walk(new CellCoord(103, 100), false);
        f.Run(400);
        Assert.IsTrue(f.Feet.Shown.Count > 0, "the pushed goal 53 turned the art");
        Assert.AreEqual(Arcanum.Simulation.Combat.LineOfFireRules.Rotation(new(100, 100), new(101, 100)), f.Feet.Facing);
        Assert.IsTrue(f.Feet.Steps.Count > 0, "the walk went on after the turn");

        using var g = new Fixture();
        g.Feet.Facing = Arcanum.Simulation.Combat.LineOfFireRules.Rotation(new(100, 100), new(101, 100));
        g.Host.Walk(new CellCoord(103, 100), false);
        g.Run(200);
        Assert.AreEqual(0, g.Feet.Shown.Count, "already facing the way: no turn");

        using var h = new Fixture();
        h.Feet.Facing = 1; h.Feet.Settled = false;
        h.Host.Walk(new CellCoord(103, 100), false);
        h.Run(200);
        Assert.AreEqual(0, h.Feet.Shown.Count, "a hero that is not standing shows the step's own facing");
        Assert.IsTrue(h.Feet.Steps.Count > 0);
    }

    [TestMethod]
    public void ARefusedStepOrADownedHeroEndsTheWalkWithoutReroutingAndABlockedLastStepToo()
    {
        using var f = new Fixture();
        f.Feet.Refuse = true;
        f.Host.Walk(new CellCoord(105, 100), false);
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the body cannot take the step: the run ends");
        Assert.AreEqual(1, f.Feet.Plans.Count, "and nothing plans again");

        using var g = new Fixture();
        g.Feet.Down = true;
        g.Host.Walk(new CellCoord(105, 100), false);
        g.Run(100);
        Assert.AreEqual(0, g.Session.QueryGoals().InUseCount);
        Assert.AreEqual(0, g.Feet.Steps.Count);

        using var h = new Fixture();
        h.Feet.LastBlocked = true;
        h.Host.Walk(new CellCoord(103, 100), false);
        h.Run(2000);
        Assert.AreEqual(0, h.Session.QueryGoals().InUseCount);
        Assert.AreEqual(2, h.Feet.Steps.Count, "the step onto the segment's last cell was blocked");
    }

    [TestMethod]
    public void AStepThatLeftTheHeroBehindEndsTheWalkAtTheNextStep()
    {
        using var f = new Fixture();
        f.Host.Walk(new CellCoord(105, 100), false);
        f.Run(30);
        Assert.AreEqual(1, f.Feet.Steps.Count);
        f.Feet.Tile = new(100, 100); // the grid refused the step: the body stayed
        f.Run(400);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "no re-route of a refused step");
        Assert.AreEqual(1, f.Feet.Steps.Count);
        Assert.AreEqual(1, f.Feet.Plans.Count);
    }

    [TestMethod]
    public void AStepTheBodyOnlyQueuedKeepsTheWalkWaitingUntilItBeginsAndARefusedOneEndsIt()
    {
        // ARC-DEV-163: a step given in the last stretch of the step under way begins after the table's next call; that is no refusal.
        using var f = new Fixture();
        f.Feet.Queue = true;
        f.Host.Walk(new CellCoord(105, 100), false);
        f.Run(30);
        Assert.AreEqual(1, f.Feet.Steps.Count);
        f.Run(600); // the step's wait (100 ms) passes several times over
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the run waits for the queued step");
        Assert.AreEqual(1, f.Feet.Steps.Count, "nothing new is given while it waits");
        f.Feet.Tile = new(101, 100); f.Feet.Pending = null; // the body began it
        f.Run(300);
        Assert.AreEqual(2, f.Feet.Steps.Count, "the next step is given once the body began the last");
        Assert.AreEqual(1, f.Feet.Plans.Count);

        using var g = new Fixture();
        g.Feet.Queue = true;
        g.Host.Walk(new CellCoord(105, 100), false);
        g.Run(30);
        g.Feet.Pending = null; // the grid refused it: the body stayed
        g.Run(400);
        Assert.AreEqual(0, g.Session.QueryGoals().InUseCount, "a refused step ends the run");
        Assert.AreEqual(1, g.Feet.Plans.Count, "no re-route");
    }

    [TestMethod]
    public void ARetargetStopsTheSegmentAtTheNextStepAndPlansTheNewDestination()
    {
        using var f = new Fixture();
        f.Host.Walk(new CellCoord(110, 100), false);
        f.Run(250);
        var before = f.Feet.Steps.Count;
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(new CellCoord(100, 106), false));
        f.Run(30_000);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(new CellCoord(100, 106), f.Feet.Tile, "the walk ended at the new destination");
        Assert.AreEqual(new CellCoord(100, 106), f.Feet.Plans[^1].Destination);
        Assert.IsTrue(f.Feet.Steps.Count > before);
    }

    [TestMethod]
    public void NoPathWaitsASecondInRealTimeAndEndsAtOnceInARound()
    {
        using var f = new Fixture();
        f.Feet.NoPath = true;
        f.Host.Walk(new CellCoord(105, 100), false);
        f.Run(600);
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the pause goal 6 holds the run for a second");
        f.Run(700);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "then the run ends");
        Assert.AreEqual(0, f.Feet.Steps.Count);

        using var g = new Fixture();
        g.Feet.NoPath = true; g.Feet.Round = true;
        g.Host.Walk(new CellCoord(105, 100), false);
        g.Run(100);
        Assert.AreEqual(0, g.Session.QueryGoals().InUseCount, "in a round the run ends at once");
    }

    [TestMethod]
    public void AFarGoalIsHandedToTheRouteAndTheRunLivesWhileTheRouteFollowsIt()
    {
        using var f = new Fixture();
        f.Feet.Far = true;
        var far = new CellCoord(180, 100);
        f.Host.Walk(far, false);
        f.Run(200);
        Assert.AreEqual(far, f.Feet.Following, "the route follower took the whole goal");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(2, f.Host.HighestPriority);
        Assert.IsTrue(f.Walk.Bottom.Flags.HasFlag(GoalFrameFlags.RouteFollow));
        f.Run(500);
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the route walks: the run lives");
        Assert.AreEqual(0, f.Feet.Steps.Count, "the table gave no step");
        f.Feet.Following = null;
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the route arrived or stopped: the run ends");
        Assert.AreEqual(0, f.Feet.Stops.Count);

        f.Host.Walk(far, false);
        f.Run(100);
        Assert.AreEqual(far, f.Feet.Following);
        f.Feet.Following = Other;
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the route follows another goal now (an approach): the run is not its walk");
        Assert.AreEqual(Other, f.Feet.Following, "and the route is left alone");
    }

    [TestMethod]
    public void AFarGoalTheRouteRefusesIsNoPathAndARetargetOfAFarWalkPlansAgain()
    {
        using var f = new Fixture();
        f.Feet.Far = true; f.Feet.RouteRefuses = true;
        f.Host.Walk(new CellCoord(180, 100), false);
        f.Run(600);
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the no-path wait");
        f.Run(1200);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);

        using var g = new Fixture();
        g.Feet.Far = true;
        g.Host.Walk(new CellCoord(180, 100), false);
        g.Run(100);
        g.Feet.Far = false;
        Assert.AreEqual(HeroWalkRunResult.Retargeted, g.Host.Walk(new CellCoord(103, 100), false));
        g.Run(2000);
        Assert.AreEqual(0, g.Session.QueryGoals().InUseCount);
        Assert.AreEqual(new CellCoord(103, 100), g.Feet.Tile, "the new near goal is walked by the table");
    }

    [TestMethod]
    public void AStraightRetryOfThePlanKeepsEveryLaterPlanStraight()
    {
        using var f = new Fixture();
        f.Host.Walk(new CellCoord(120, 100), false);
        f.Run(30_000);
        Assert.IsTrue(f.Feet.Plans.All(plan => !plan.Straight), "no retry happened: no straight flag");

        using var g = new Fixture();
        g.Feet.StraightFirst = true; // the first plan was the PC's straight-line retry (LR4): the run's flag 0x400
        g.Host.Walk(new CellCoord(120, 100), false);
        g.Run(30_000);
        Assert.IsFalse(g.Feet.Plans[0].Straight);
        Assert.IsTrue(g.Feet.Plans.Skip(1).All(plan => plan.Straight), "every later plan toward the goal is the straight line");
        Assert.AreEqual(new CellCoord(120, 100), g.Feet.Tile);
    }

    [TestMethod]
    public void TheLevelThreeInterruptEndsTheWalkAndItsEndNodeStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        Assert.IsTrue(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "ROUTE_STOPPED_BY_RIGHT_RELEASE"));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual((Goal, "ROUTE_STOPPED_BY_RIGHT_RELEASE"), f.Feet.Stops.Single());
        Assert.IsNull(f.Feet.Following);
        Assert.IsTrue(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "again"), "nothing to interrupt: it succeeds");
    }

    [TestMethod]
    public void ASpellAttemptRefusesTheInterruptAndKeepsTheWalk()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        // A casting hero: a run of priority 4 on the table beside the walk, first in slot order (GS17: the first refusal stops the walk of the owner's runs).
        f.Session.KillGoals("pc");
        var cast = f.Session.StartGoal(new GoalStartRequest("pc", GoalStarters.GenericStartsAnyway, new GoalFrame(GoalType.AttemptSpell)), new OpenPorts());
        Assert.IsTrue(cast.Started);
        Assert.AreEqual(4, f.Host.HighestPriority);
        Assert.IsFalse(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "right release"), "a run of priority 4 refuses level 3");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(0, f.Feet.Stops.Count);
    }

    [TestMethod]
    public void ANewStartsOwnInterruptOfTheOldRunDoesNotStopTheRoute()
    {
        using var f = new Fixture();
        // A goal 53 turn of the hero is interrupted by the walk starter's level-3 interrupt (priority 2): no route stop, no leftover.
        f.Session.StartGoal(new GoalStartRequest("pc", GoalStarters.Generic, new GoalFrame(GoalType.Rotate)), new OpenPorts());
        f.Feet.Following = Goal;
        Assert.AreEqual(HeroWalkRunResult.Started, f.Host.Walk(Goal, false));
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the turn ended, the walk runs");
        Assert.AreEqual(1, f.Feet.Snaps.Count, "the turn's end node snapped the facing");
        Assert.AreEqual(0, f.Feet.Stops.Count);
        Assert.AreEqual(Goal, f.Feet.Following);
    }

    [TestMethod]
    public void ATurnIsAGoalFiftyThreeRunOfThePcTheTableStepsItThirtyMillisecondsAnOctantAndItsEndNodeSnapsTheFacing()
    {
        using var f = new Fixture();
        f.Feet.Facing = 5;
        Assert.IsTrue(f.Host.Turn(3));
        Assert.AreEqual(GoalType.Rotate, f.Host.TurnRun!.Bottom.Goal);
        Assert.AreEqual(3, f.Host.TurnRun.Bottom.Rotation);
        Assert.AreEqual(2, f.Host.HighestPriority);
        f.Run(100);
        CollectionAssert.AreEqual(new[] { 4, 3 }, f.Feet.Shown, "two 45-degree steps the short way");
        Assert.AreEqual(2, f.Host.TurnSteps);
        f.Run(200);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the step that finds the target reached ends the run");
        Assert.AreEqual(0, f.Feet.Snaps.Count, "a run that ended itself has nothing left to snap");

        f.Feet.Facing = 5;
        Assert.IsTrue(f.Host.Turn(2));
        f.Run(20);
        Assert.IsTrue(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "right release"));
        CollectionAssert.AreEqual(new[] { 2 }, f.Feet.Snaps, "an interrupt runs the end node: the facing snaps to the target");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void AHalfTurnTakesFourStepsAndTheRunEndsOneTickAfterTheLastAndALateFrameRunsEveryDueStep()
    {
        using var f = new Fixture();
        f.Feet.Facing = 1;
        Assert.IsTrue(f.Host.Turn(5));
        f.Run(120);
        Assert.AreEqual(4, f.Feet.Shown.Count, "180 degrees are four steps");
        Assert.AreEqual(5, f.Feet.Facing);
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);

        // One long frame: the table's steps that came due inside it all run.
        f.Feet.Facing = 0; f.Feet.Shown.Clear();
        Assert.IsTrue(f.Host.Turn(3));
        f.Step.AdvanceHost(400);
        Assert.AreEqual(3, f.Feet.Shown.Count, "a late frame runs every due step");
        Assert.AreEqual(3, f.Feet.Facing);
    }

    [TestMethod]
    public void ATurnTheHeroNoLongerHoldsEndsWithoutStepsAndAFacingAlreadyAtTheTargetEndsAtOnce()
    {
        using var f = new Fixture();
        f.Feet.Facing = 5;
        Assert.IsTrue(f.Host.Turn(2));
        f.Feet.Holds = false;
        f.Run(100);
        Assert.AreEqual(0, f.Feet.Shown.Count, "the hero walked or fell: no step");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(1, f.Host.RouteEnds);

        f.Feet.Holds = true; f.Feet.Facing = 4;
        Assert.IsTrue(f.Host.Turn(4));
        f.Run(60);
        Assert.AreEqual(0, f.Feet.Shown.Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "nothing to turn: the step ends the run");
    }

    [TestMethod]
    public void ATurnStartedOverAWalkEndsTheWalkAndStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        Assert.IsTrue(f.Host.Turn(2));
        Assert.IsNull(f.Host.WalkRun, "the level-2 start ended the walk");
        Assert.AreEqual("ROUTE_STOPPED_BY_TURN", f.Feet.Stops.Single().Why);
    }

    [TestMethod]
    public void AKilledOrResetWalkRunStillStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        f.Host.EndNodes(new GoalRunEnd(f.Walk, GoalEndKind.Killed, [GoalType.MoveToTile], 0), "KILLED");
        Assert.AreEqual("KILLED", f.Feet.Stops.Single().Why);
    }

    [TestMethod]
    public void TheEndNodeOfAWalkEndedFromOutsideStopsTheRouteAndAFinishedRunAsksNothing()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        var run = f.Walk;
        f.Host.EndNodes(new GoalRunEnd(run, GoalEndKind.Finished, [], 0));
        Assert.AreEqual(Goal, f.Feet.Following, "a finished run: nothing left to stop");
        f.Host.EndNodes(new GoalRunEnd(run, GoalEndKind.Interrupted, [GoalType.MoveToTile], 0), "ROUND_START");
        Assert.AreEqual((Goal, "ROUND_START"), f.Feet.Stops.Single());
    }

    [TestMethod]
    public void LosingTheHostEndsTheRunAtLevelSixAndStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        f.Host.LoseAll("the session changed");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual("the session changed", f.Feet.Stops.Single().Why);
    }

    [TestMethod]
    public void ARunWithNoHeroHostEndsInTheRouterAndOnlyTheHeroesWalkIsTheHeroHosts()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null));
        Assert.AreEqual(SessionGoalStepAnswer.End, router.StepGoal(f.Walk, default));
        Assert.AreEqual(1, router.HeroRunsLost);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroWalk(f.Walk));
        Assert.IsFalse(AreaHeroRunsHost.IsHeroWalk(new GoalRun("npc:a", default, new GoalFrame(GoalType.MoveToTile, tile: Goal))), "an NPC's walk is the AI host's");
        Assert.IsFalse(AreaHeroRunsHost.IsHeroWalk(new GoalRun("pc", default, new GoalFrame(GoalType.Attack, "npc:a"))));
    }

    private sealed class OpenPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    // ---- ARC-DEV-147 (MIG13 S10c, GT105–GT113): the held walk ----

    [TestMethod]
    public void AHeldWalkStartsAFlightMarkedRunAndTheTablePlansItAsAStraightLine()
    {
        using var f = new Fixture();
        Assert.AreEqual(HeroWalkRunResult.Started, f.Host.Walk(new CellCoord(108, 100), run: false, held: true));
        Assert.IsTrue(f.Walk.Flags.HasFlag(GoalRunFlags.Flight), "the held walk is marked as the original marks its repeats (0x400)");
        f.Run(200);
        Assert.IsTrue(f.Feet.Plans.Count > 0 && f.Feet.Plans.All(p => p.Straight), "every plan of the run is the straight line");

        using var g = new Fixture();
        g.Host.Walk(new CellCoord(108, 100), run: false);
        Assert.IsFalse(g.Walk.Flags.HasFlag(GoalRunFlags.Flight), "a plain click leaves the run unmarked");
        g.Run(200);
        Assert.IsTrue(g.Feet.Plans.Count > 0 && g.Feet.Plans.All(p => !p.Straight));
    }

    [TestMethod]
    public void ARepeatMarksTheRunWhetherItRetargetsOrAsksTheSameTileAndAPlainClickKeepsTheMark()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        var handle = f.Walk.Handle;
        Assert.IsFalse(f.Walk.Flags.HasFlag(GoalRunFlags.Flight), "the first press marks nothing");
        Assert.AreEqual(HeroWalkRunResult.Unchanged, f.Host.Walk(Goal, false, held: true));
        Assert.IsTrue(f.Walk.Flags.HasFlag(GoalRunFlags.Flight), "a repeat on the run's own tile still marks it (GT111)");
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(Other, false, held: true));
        Assert.AreEqual(handle, f.Walk.Handle, "retargeted in place, no restart");
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(Goal, false));
        Assert.IsTrue(f.Walk.Flags.HasFlag(GoalRunFlags.Flight), "no starter, retarget or release clears the mark: a later plain click keeps it");
    }

    [TestMethod]
    public void AHeldRepeatThatTheGateRefusesMarksNothing()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.May = false;
        Assert.AreEqual(HeroWalkRunResult.Refused, f.Host.Walk(Other, false, held: true));
        Assert.IsFalse(f.Walk.Flags.HasFlag(GoalRunFlags.Flight));
    }

    [TestMethod]
    public void ReleasingTheStickEndsTheWalkRunAndStopsTheRouteWhereItStands()
    {
        using var f = new Fixture();
        f.Feet.StepMs = 400;
        f.Host.Walk(new CellCoord(120, 100), false, held: true);
        f.Run(500);
        Assert.IsNotNull(f.Host.WalkRun);
        f.Feet.Following = new CellCoord(120, 100);
        Assert.IsTrue(f.Host.EndHeldWalk("ROUTE_STOPPED_BY_STICK"));
        Assert.IsNull(f.Host.WalkRun, "the level-3 interrupt ended the run");
        Assert.AreEqual("ROUTE_STOPPED_BY_STICK", f.Feet.Stops.Single().Why, "its end node stops the route");
        var steps = f.Feet.Steps.Count;
        f.Run(2000);
        Assert.AreEqual(steps, f.Feet.Steps.Count, "no step after the release");
        Assert.IsFalse(f.Host.EndHeldWalk("again"), "no walk, nothing to end");
    }

    // ARC-DEV-169 (MIG13 S11a): the hero's talk as a run of the table.

    [TestMethod]
    public void ATalkStartsGoalTwentyOneOfThePcAtPriorityThreeRunning()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Talk("npc:1", run: true));
        var run = f.Host.TalkRun!;
        Assert.AreEqual(GoalType.Talk, run.Bottom.Goal);
        Assert.AreEqual("npc:1", run.Bottom.Target);
        Assert.AreEqual("npc:1", f.Host.TalkTarget);
        Assert.IsTrue((run.Flags & GoalRunFlags.Running) != 0);
        Assert.AreEqual(3, f.Host.HighestPriority);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(run));
    }

    [TestMethod]
    public void ATalkThatIsWithinFourCellsAsksForTheDialogAtItsFirstStepAndEnds()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(103, 100);
        f.Host.Talk("npc:1", run: true);
        f.Run(100);
        CollectionAssert.AreEqual(new[] { "npc:1" }, f.Feet.Dialogs);
        Assert.IsNull(f.Host.TalkRun, "the act is the last node: the run ends");
        Assert.AreEqual(0, f.Feet.Steps.Count, "no walk");
    }

    [TestMethod]
    public void ATalkFromFarWalksNextToTheNpcOnTheTableThenAsksForTheDialog()
    {
        using var f = new Fixture();
        f.Host.Talk("npc:1", run: true);
        f.Run(5_000);
        Assert.AreEqual(new CellCoord(129, 100), f.Feet.Tile, "goal 8 walked the hero next to the NPC");
        CollectionAssert.AreEqual(new[] { "npc:1" }, f.Feet.Dialogs);
        Assert.IsNull(f.Host.TalkRun);
    }

    [TestMethod]
    public void ANewClickInterruptsATalkAndItsWalkStops()
    {
        using var f = new Fixture();
        f.Host.Talk("npc:1", run: true);
        f.Run(200);
        Assert.IsNotNull(f.Host.TalkRun);
        f.Feet.Following = new(129, 100);
        Assert.AreEqual(HeroWalkRunResult.Started, f.Host.Walk(Other, run: false), "the walk starter interrupts the talk at level 3");
        Assert.IsNull(f.Host.TalkRun);
        Assert.IsNotNull(f.Host.WalkRun, "the new walk runs");
        Assert.AreEqual(0, f.Feet.Dialogs.Count);
    }

    [TestMethod]
    public void ARepeatedTalkRestartsTheRunAndEndTalkKillsItAndStopsItsWalk()
    {
        using var f = new Fixture();
        f.Host.Talk("npc:1", run: true);
        var first = f.Host.TalkRun!.Handle;
        Assert.IsTrue(f.Host.Talk("npc:1", run: true));
        Assert.AreNotEqual(first, f.Host.TalkRun!.Handle, "a repeated order restarts (GS27)");
        f.Run(200);
        f.Feet.Following = new(129, 100);
        Assert.IsTrue(f.Host.EndTalk("test"));
        Assert.IsNull(f.Host.TalkRun);
        Assert.IsTrue(f.Feet.Stops.Any(s => s.Destination == new CellCoord(129, 100)), "the talk's walk stops with it");
        Assert.IsNull(f.Feet.Following);
        Assert.IsFalse(f.Host.EndTalk("again"));
    }

    [TestMethod]
    public void ATalkIsRefusedByTheMovementGate()
    {
        using var f = new Fixture();
        f.Feet.May = false;
        Assert.IsFalse(f.Host.Talk("npc:1", run: true));
        Assert.IsNull(f.Host.TalkRun);
    }

    [TestMethod]
    public void ATalkWhoseNpcIsGoneEndsWithoutADialog()
    {
        using var f = new Fixture();
        f.Host.Talk("npc:1", run: true);
        f.Feet.NpcGone = true;
        f.Run(200);
        Assert.IsNull(f.Host.TalkRun);
        Assert.AreEqual(0, f.Feet.Dialogs.Count);
    }

    // ARC-DEV-174 (MIG13 S11b): the hero's use as a run of the table.

    [TestMethod]
    public void AUseStartsGoalThirtyEightOfThePcAtPriorityThree()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Use("obj:1", container: false, run: true));
        Assert.AreEqual(GoalType.UseObject, f.Host.UseRun!.Bottom.Goal);
        Assert.AreEqual("obj:1", f.Host.UseTarget);
        Assert.AreEqual(3, f.Host.HighestPriority);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.UseRun));
        Assert.IsTrue(f.Host.Use("obj:1", container: true, run: true));
        Assert.AreEqual(GoalType.UseContainer, f.Host.UseRun!.Bottom.Goal, "a repeated order restarts the run (GS27)");
    }

    [TestMethod]
    public void AUseFromFarWalksWithinTwoCellsThenAsksForTheUse()
    {
        using var f = new Fixture();
        f.Host.Use("obj:1", container: false, run: true);
        f.Run(5_000);
        Assert.IsTrue(Math.Max(Math.Abs(f.Feet.Tile.X - f.Feet.Npc.X), Math.Abs(f.Feet.Tile.Y - f.Feet.Npc.Y)) <= 2);
        CollectionAssert.AreEqual(new[] { "obj:1" }, f.Feet.Uses);
        Assert.IsNull(f.Host.UseRun);
        Assert.AreEqual(0, f.Feet.Dialogs.Count);
    }

    [TestMethod]
    public void AUseWithinRangeActsAtItsFirstStepAndEndUseKillsARunAndStopsItsWalk()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(101, 100);
        f.Host.Use("obj:1", container: false, run: true);
        f.Run(100);
        CollectionAssert.AreEqual(new[] { "obj:1" }, f.Feet.Uses);
        f.Feet.Uses.Clear(); f.Feet.Npc = new(130, 100);
        f.Host.Use("obj:1", container: false, run: true);
        f.Run(200);
        f.Feet.Following = new(129, 100);
        Assert.IsTrue(f.Host.EndUse("test"));
        Assert.IsNull(f.Host.UseRun);
        Assert.IsTrue(f.Feet.Stops.Any(s => s.Destination == new CellCoord(129, 100)), "the use's walk stops with it");
        Assert.IsFalse(f.Host.EndUse("again"));
    }

    // ARC-DEV-175 (MIG13 S11c): the hero's pickup as a run of the table.

    [TestMethod]
    public void APickupStartsGoalFifteenOfThePcAtPriorityThree()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Pickup("obj:1", run: true));
        Assert.AreEqual(GoalType.PickupItem, f.Host.PickupRun!.Bottom.Goal);
        Assert.AreEqual(3, f.Host.HighestPriority);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.PickupRun));
        Assert.IsTrue(f.Host.Pickup("obj:1", run: true), "a repeated order restarts the run (GS27)");
        Assert.IsNotNull(f.Host.PickupRun);
    }

    [TestMethod]
    public void APickupFromFarWalksOntoTheTileThenAsksForTheTake()
    {
        using var f = new Fixture();
        // The fake's approach cell is next to the target; a pickup's range is 0, so the fake answers the target's own tile.
        f.Feet.ApproachOnTarget = true;
        f.Host.Pickup("obj:1", run: true);
        f.Run(5_000);
        Assert.AreEqual(f.Feet.Npc, f.Feet.Tile, "the hero stands on the item's tile");
        CollectionAssert.AreEqual(new[] { "obj:1" }, f.Feet.Pickups);
        Assert.IsNull(f.Host.PickupRun);
    }

    [TestMethod]
    public void EndPickupKillsARunAndStopsItsWalk()
    {
        using var f = new Fixture();
        f.Feet.ApproachOnTarget = true;
        f.Host.Pickup("obj:1", run: true);
        f.Run(200);
        f.Feet.Following = f.Feet.Npc;
        Assert.IsTrue(f.Host.EndPickup("test"));
        Assert.IsNull(f.Host.PickupRun);
        Assert.IsTrue(f.Feet.Stops.Any(s => s.Destination == f.Feet.Npc), "the pickup's walk stops with it");
        Assert.IsFalse(f.Host.EndPickup("again"));
        Assert.AreEqual(0, f.Feet.Pickups.Count);
    }

    // ARC-DEV-181 (MIG13 S12a): the hero's attack as a run of the table.

    [TestMethod]
    public void AnAttackStartsGoalEighteenOfThePcAtPriorityThree()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Attack("atk:npc:1", run: true));
        Assert.AreEqual(GoalType.Attack, f.Host.AttackRun!.Bottom.Goal);
        Assert.AreEqual(3, f.Host.HighestPriority);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.AttackRun));
        Assert.AreEqual("atk:npc:1", f.Host.AttackTarget);
        Assert.IsTrue(f.Host.Attack("atk:npc:1", run: true), "a repeated order restarts the run (GS27)");
        Assert.IsNotNull(f.Host.AttackRun);
    }

    [TestMethod]
    public void AnAttackInReachSwingsAndHitsOnceAtTheActionFrameThenTheRunIsOver()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(101, 100);
        f.Host.Attack("atk:npc:1", run: true);
        f.Run(200);
        CollectionAssert.AreEqual(new[] { "swing" }, f.Feet.Attacks, "the swing started, the action frame is 300 ms away");
        f.Run(200);
        CollectionAssert.AreEqual(new[] { "swing", "hit" }, f.Feet.Attacks);
        f.Run(500);
        Assert.IsNull(f.Host.AttackRun, "nothing repeats: the run is over");
    }

    [TestMethod]
    public void AnAttackFromFarWalksNextToTheTargetThenSwingsAndEndAttackKillsARunAndStopsItsWalk()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(130, 100);
        f.Host.Attack("atk:npc:1", run: true);
        f.Run(200);
        Assert.IsTrue(f.Host.AttackWalking, "the run holds the hero's walk goal above the attack");
        f.Feet.Following = new(129, 100);
        Assert.IsTrue(f.Host.EndAttack("test"));
        Assert.IsNull(f.Host.AttackRun);
        Assert.IsTrue(f.Feet.Stops.Any(s => s.Destination == new CellCoord(129, 100)), "the attack's walk stops with it");
        Assert.IsFalse(f.Host.EndAttack("again"));
        using var g = new Fixture();
        g.Feet.Npc = new(130, 100);
        g.Host.Attack("atk:npc:1", run: true);
        g.Run(5_000);
        Assert.AreEqual(new CellCoord(129, 100), g.Feet.Tile, "the hero stands next to the target");
        CollectionAssert.AreEqual(new[] { "swing", "hit" }, g.Feet.Attacks);
    }

    // ARC-DEV-182 (MIG13 S12b): the hero's held throw as a run of the table.

    [TestMethod]
    public void AThrowStartsGoalThirtySevenAtPriorityThreeAndThrowsAtTheActionFrame()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Throw("kit:7", new(104, 100)));
        Assert.AreEqual(GoalType.ThrowItem, f.Host.ThrowRun!.Bottom.Goal);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.ThrowRun));
        Assert.AreEqual(3, f.Host.HighestPriority);
        f.Run(200);
        CollectionAssert.AreEqual(new[] { "begin kit:7 104,100" }, f.Feet.Throws, "the swing began, the action frame is 300 ms away");
        f.Run(200);
        CollectionAssert.AreEqual(new[] { "begin kit:7 104,100", "throw" }, f.Feet.Throws);
        f.Run(500);
        CollectionAssert.AreEqual(new[] { "begin kit:7 104,100", "throw", "ends Thrown" }, f.Feet.Throws);
        Assert.IsNull(f.Host.ThrowRun, "the run is over after the rest of the animation");
    }

    [TestMethod]
    public void ACutThrowEndsItsRunAndClearsTheSwing()
    {
        using var f = new Fixture();
        f.Host.Throw("kit:7", new(104, 100));
        f.Run(100);
        Assert.IsTrue(f.Host.EndThrow("test"));
        Assert.IsNull(f.Host.ThrowRun);
        Assert.IsTrue(f.Feet.Throws.Contains("ends ActionRefused"), "an interrupted throw clears the pose and leaves the item in the pack");
        Assert.IsFalse(f.Host.EndThrow("again"));
    }

    // ARC-DEV-183 (MIG13 S12c): the hero's cast as a run of the table.

    [TestMethod]
    public void ACastStartsGoalTwentySixThatBecomesTwentySevenAndReleasesAtTheActionFrame()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Host.Cast(7, "npc:1"));
        Assert.AreEqual(GoalType.CastSpell, f.Host.CastRun!.Bottom.Goal);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.CastRun));
        f.Run(100);
        Assert.AreEqual(GoalType.AttemptSpell, f.Host.CastRun!.Bottom.Goal, "goal 26 replaced itself by goal 27 after the start");
        CollectionAssert.AreEqual(new[] { "start 7", "begin" }, f.Feet.Casts);
        f.Run(300);
        CollectionAssert.AreEqual(new[] { "start 7", "begin", "release" }, f.Feet.Casts);
        f.Run(500);
        CollectionAssert.AreEqual(new[] { "start 7", "begin", "release", "ends Popped" }, f.Feet.Casts);
        Assert.IsNull(f.Host.CastRun);
    }

    [TestMethod]
    public void ACutCastEndsItsRunAndTellsTheCastHost()
    {
        using var f = new Fixture();
        f.Host.Cast(7, "npc:1");
        f.Run(100);
        Assert.IsTrue(f.Host.EndCast("test"));
        Assert.IsNull(f.Host.CastRun);
        Assert.IsTrue(f.Feet.Casts.Contains("interrupted test"));
        Assert.IsFalse(f.Host.EndCast("again"));
    }

    // ARC-DEV-184 (MIG13 S12d): the hero's pick pocket as a run of the table.

    [TestMethod]
    public void APickPocketStartsGoalThirtyThreeThatBecomesEightySixAndAsksForTheAttempt()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(101, 100);
        Assert.IsTrue(f.Host.PickPocket("pp:npc:1", "item:1", plant: false));
        Assert.AreEqual(GoalType.UseSkill, f.Host.SkillRun!.Bottom.Goal);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.SkillRun));
        f.Run(300);
        CollectionAssert.AreEqual(new[] { "face", "attempt item:1 plant=False" }, f.Feet.Skills);
        Assert.IsNull(f.Host.SkillRun, "the run is over once the attempt was asked for");
    }

    [TestMethod]
    public void APickLockStartsGoalThirtyThreeWithSkillFourteenAndAsksForThePick()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(101, 100);
        Assert.IsTrue(f.Host.PickLock("door:1", "item:picks"));
        Assert.AreEqual(AiExecutorRules.PickLocksSkill, f.Host.SkillRun!.Bottom.Skill);
        f.Run(300);
        CollectionAssert.AreEqual(new[] { "face", "pick item:picks" }, f.Feet.Skills);
        Assert.IsNull(f.Host.SkillRun);
    }

    [TestMethod]
    public void ACutPickPocketEndsItsRun()
    {
        using var f = new Fixture();
        f.Feet.Npc = new(130, 100);
        f.Host.PickPocket("pp:npc:1", "item:1", plant: true);
        f.Run(100);
        Assert.IsNotNull(f.Host.SkillRun);
        Assert.IsTrue(f.Host.EndSkill("test"));
        Assert.IsNull(f.Host.SkillRun);
        Assert.IsFalse(f.Host.EndSkill("again"));
    }

    // ARC-DEV-187 (MIG13 S13a): the hero's stun is goal 57 of the owner pc; it interrupts the walk and its first step ends it and clears the bit.

    private sealed class StunPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    [TestMethod]
    public void AHeroStunInterruptsTheWalkAndItsFirstStepEndsItAndClearsTheBit()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        Assert.IsNotNull(f.Host.WalkRun);
        var started = f.Session.StartGoal(new("pc", GoalStarters.Stun, AiExecutorRules.StunFrame(8)), new StunPorts());
        Assert.IsTrue(started.Started);
        foreach (var end in started.Ended) f.Host.EndNodes(end, "stun");
        Assert.IsNull(f.Host.WalkRun, "the stun (level 4) interrupted the walk");
        Assert.IsNotNull(f.Host.StunRun);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.StunRun!));
        f.Run(200);
        Assert.IsNull(f.Host.StunRun, "the stun's first step ended it (no stun animation is read)");
        Assert.AreEqual(1, f.Feet.StunEnds);
    }

    [TestMethod]
    public void ACutStunStillRunsItsEndNode()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Session.StartGoal(new("pc", GoalStarters.Stun, AiExecutorRules.StunFrame(8)), new StunPorts()).Started);
        f.Host.LoseAll("view left");
        Assert.IsNull(f.Host.StunRun);
        Assert.AreEqual(1, f.Feet.StunEnds, "a stun cut short (a reset, a quick load) clears the bit through its end node");
    }

    // ARC-DEV-189 (MIG13 S13c): the hero's knock-down (62), get-up (63) and dying (31) runs: the viewer's fall and get-up animation are their feet.

    [TestMethod]
    public void AHeroKnockDownForcesTheFallInterruptsTheWalkAndEndsWhenItLies()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        var started = f.Session.StartGoal(new("pc", GoalStarters.KnockDown, AiExecutorRules.KnockDownFrame()), new StunPorts());
        Assert.IsTrue(started.Started);
        foreach (var end in started.Ended) f.Host.EndNodes(end, "knock-down");
        Assert.IsNull(f.Host.WalkRun, "the knock-down (level 5 then 3) interrupted the walk");
        Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(f.Host.DownRun!));
        f.Run(200);
        Assert.IsNotNull(f.Host.DownRun, "the run waits while the fall plays");
        Assert.IsTrue(f.Feet.Forced, "the fall is forced (a knock-down result leaves the hero conscious)");
        f.Feet.FellDown = true;
        f.Run(200);
        Assert.IsNull(f.Host.DownRun, "the run ends when the hero lies");
        Assert.IsFalse(f.Feet.Forced, "the forced fall is released: an unconscious hero stays down by its life state, a conscious one gets up");
    }

    [TestMethod]
    public void AHeroGetUpEndsWhenTheHeroStandsAndDyingEndsAtItsFirstStep()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Session.StartGoal(new("pc", GoalStarters.Generic, AiExecutorRules.GetUpFrame()), new StunPorts()).Started);
        f.Run(200);
        Assert.IsNotNull(f.Host.DownRun, "the get-up waits for the animation");
        f.Feet.StandsUp = true;
        f.Run(200);
        Assert.IsNull(f.Host.DownRun);
        Assert.IsTrue(f.Session.StartGoal(new("pc", GoalStarters.Generic, AiExecutorRules.DyingFrame(7)), new StunPorts()).Started);
        f.Run(200);
        Assert.IsNull(f.Host.DownRun, "dying ends at its first step: the death flow reads the life state");
    }

    [TestMethod]
    public void AKnockDownOrGetUpTheViewerCannotPlayEndsInsteadOfWaitingForever()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Session.StartGoal(new("pc", GoalStarters.KnockDown, AiExecutorRules.KnockDownFrame()), new StunPorts()).Started);
        f.Feet.MayWait = false;
        f.Run(200);
        Assert.IsNull(f.Host.DownRun);
        Assert.IsFalse(f.Feet.Forced);
        Assert.IsTrue(f.Session.StartGoal(new("pc", GoalStarters.Generic, AiExecutorRules.GetUpFrame()), new StunPorts()).Started);
        f.Run(200);
        Assert.IsNull(f.Host.DownRun, "a get-up the hero cannot make (held, short of action points) leaves the table at its step");
    }

    [TestMethod]
    public void ACutKnockDownReleasesTheForcedFall()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Session.StartGoal(new("pc", GoalStarters.KnockDown, AiExecutorRules.KnockDownFrame()), new StunPorts()).Started);
        f.Run(100);
        Assert.IsTrue(f.Feet.Forced);
        f.Host.LoseAll("view left");
        Assert.IsFalse(f.Feet.Forced);
    }

    // ARC-DEV-176 (MIG13 S11d): the door goals 38 turns into belong to the hero's use run.

    [TestMethod]
    public void TheDoorGoalsOfTheOpenerPcAreHeroUseRuns()
    {
        foreach (var goal in new[] { GoalType.OpenDoor, GoalType.AttemptOpenDoor, GoalType.CloseDoor, GoalType.AttemptCloseDoor })
        {
            var run = new GoalRun("pc", default, new GoalFrame(goal, "door"));
            Assert.IsTrue(AreaHeroRunsHost.IsHeroUse(run), goal.ToString());
            Assert.IsTrue(AreaHeroRunsHost.IsHeroRun(run));
            Assert.IsFalse(AreaHeroRunsHost.IsHeroUse(new GoalRun("npc:1", default, new GoalFrame(goal, "door"))), "an NPC opener's run is not the hero's");
        }
    }
}
