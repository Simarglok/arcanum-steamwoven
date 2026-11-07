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
}
