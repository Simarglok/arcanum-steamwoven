using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-DEV-149 (MIG13 S10b-2b): the follower as the feet of the goal table's walk: one given step at a time, and the first plan the table's walk asks.</summary>
[TestClass]
public sealed class RouteGivenStepTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    [TestMethod]
    public void AGivenStepIsTakenAtTheExactStepEndAndTheNextOneGivenInsideItChainsWithoutAGap()
    {
        var source = GridSource.Open(12, 3);
        var host = new RouteHost(source, new(0, 1), Step);
        var follower = new RouteFollower(source);
        Assert.IsTrue(follower.GiveStep(new(0, 1), new(1, 1), host.State.Time));
        Assert.IsTrue(follower.Given);
        host.Frames(follower, 2); // half a step: the step is under way
        Assert.AreEqual(MovementPhase.OutboundAfterEntry, host.State.Phase);
        // The next step is given from the cell the actor will stand on, before the step ends.
        Assert.IsTrue(follower.GiveStep(new(1, 1), new(2, 1), host.State.Time));
        host.Frames(follower, 2);
        Assert.IsTrue(follower.GiveStep(new(2, 1), new(3, 1), host.State.Time));
        host.Frames(follower, 4 * 2);
        Assert.AreEqual(RouteFollowStatus.Arrived, follower.Status);
        Assert.AreEqual(new CellCoord(3, 1), host.State.CommittedCell);
        CollectionAssert.AreEqual(new[] { new CellCoord(1, 1), new CellCoord(2, 1), new CellCoord(3, 1) }, host.Committed());
        Assert.AreEqual(0, follower.PlannedSegments, "the follower planned nothing: the table did");
        var starts = host.Events.Where(e => e.Kind == MovementEventKind.StepStarted).Select(e => e.Time).ToArray();
        CollectionAssert.AreEqual(new[] { T(0), T(1), T(2) }, starts, "no idle gap between the given steps");
    }

    [TestMethod]
    public void ARefusedGivenStepStopsTheRouteWithoutReplanningAroundIt()
    {
        var source = new GridSource("....", "..#.", "....");
        var host = new RouteHost(source, new(1, 1), Step);
        var follower = new RouteFollower(source, replanOnDenial: true);
        Assert.IsTrue(follower.GiveStep(new(1, 1), new(2, 1), host.State.Time));
        host.Frames(follower, 8);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED"), (follower.Status, follower.StopReason));
        Assert.AreEqual(new CellCoord(1, 1), host.State.CommittedCell);
        Assert.AreEqual(0, follower.PlannedSegments);
    }

    [TestMethod]
    public void AGivenStepFromAnotherCellThanTheActorsIsLostAndNothingIsPlanned()
    {
        var source = GridSource.Open(8, 3);
        var host = new RouteHost(source, new(0, 1), Step);
        var follower = new RouteFollower(source);
        Assert.IsTrue(follower.GiveStep(new(4, 1), new(5, 1), host.State.Time));
        host.Frames(follower, 4);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_GIVEN_STEP_LOST"), (follower.Status, follower.StopReason));
        Assert.AreEqual(new CellCoord(0, 1), host.State.CommittedCell);
    }

    [TestMethod]
    public void OnlyANeighbourIsAStepAndAFollowersOwnGoalIsNotGiven()
    {
        var source = GridSource.Open(8, 3);
        var follower = new RouteFollower(source);
        Assert.IsFalse(follower.GiveStep(new(0, 1), new(2, 1), T(0)), "two cells apart");
        Assert.IsFalse(follower.GiveStep(new(0, 1), new(0, 1), T(0)), "the same cell");
        Assert.IsFalse(follower.Given);
        Assert.IsTrue(follower.GiveStep(new(0, 1), new(1, 2), T(0)), "a diagonal neighbour");
        Assert.IsTrue(follower.SetTarget(new(6, 1), T(0)));
        Assert.IsFalse(follower.Given, "a goal of the follower's own");
    }

    [TestMethod]
    public void TheCritterTestBeforeASegmentsLastStepIsTheTablesAndNotAskedOfAGivenStep()
    {
        var source = GridSource.Open(8, 3);
        source.Critters.Add(new(1, 1));
        var host = new RouteHost(source, new(0, 1), Step);
        var follower = new RouteFollower(source, segmentEndCritterEndsWalk: () => true);
        Assert.IsTrue(follower.GiveStep(new(0, 1), new(1, 1), host.State.Time));
        host.Frames(follower, 8);
        Assert.AreEqual(new CellCoord(1, 1), host.State.CommittedCell, "the step onto the critter is taken: the table's step test decides");
    }

    [TestMethod]
    public void PlanWalkAsksTheWalksBudgetAndTheStraightRetryAndAnswersAFarGoalForTheCorridor()
    {
        var source = GridSource.Open(80, 40);
        var follower = new RouteFollower(source, corridorLimits: new RouteCorridorLimits(), straightRetry: true);
        var near = follower.PlanWalk(new(2, 2), new(12, 2), straight: false);
        Assert.AreEqual(RouteOutcome.Found, near.Plan.Outcome);
        Assert.IsFalse(near.Far);
        Assert.IsFalse(near.Straight);
        Assert.AreEqual(new CellCoord(12, 2), near.Plan.Cells[^1]);
        Assert.IsNull(follower.Target, "the follower's goal is untouched");

        var far = follower.PlanWalk(new(2, 2), new(70, 30), straight: false);
        Assert.IsTrue(far.Far, "beyond the planner's reach only a corridor gets there: " + far.Plan.ReasonCode);

        var straight = follower.PlanWalk(new(2, 2), new(12, 2), straight: true);
        Assert.AreEqual(RouteMethod.StraightRetry, straight.Plan.Method);
        Assert.IsTrue(straight.Straight);

        var noCorridor = new RouteFollower(source, corridorLimits: new RouteCorridorLimits(), corridorAllowed: () => false, straightRetry: true);
        Assert.IsFalse(noCorridor.PlanWalk(new(2, 2), new(70, 30), straight: false).Far, "the Original setting has no corridor");
    }

    [TestMethod]
    public void PlanWalkRetriesAStraightLineWhenThePlannerFindsNoPathAndTheHostWalksThePc()
    {
        var source = GridSource.Open(12, 5);
        var goal = new CellCoord(5, 2);
        source.Override = (from, direction) => RoutePlanner.Neighbor(from, direction) == goal ? RouteStepVerdict.Blocked : null;
        var walksPc = new RouteFollower(source, straightRetry: true);
        var retry = walksPc.PlanWalk(new(0, 2), goal, straight: false);
        Assert.AreEqual(RouteOutcome.Found, retry.Plan.Outcome);
        Assert.AreEqual(RouteMethod.StraightRetry, retry.Plan.Method);
        Assert.IsTrue(retry.Straight, "the run's flag 0x400: every later plan is the straight line");
        var other = new RouteFollower(source);
        var refused = other.PlanWalk(new(0, 2), goal, straight: false);
        Assert.AreEqual(RouteOutcome.NoRoute, refused.Plan.Outcome);
        Assert.IsFalse(refused.Straight);
    }
}
