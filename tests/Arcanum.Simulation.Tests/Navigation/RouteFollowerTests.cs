using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests.Navigation;

[TestClass]
public sealed class RouteFollowerTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    [TestMethod]
    public void OneClickWalksAFarTargetInSegmentsAndStopsAtItsCentre()
    {
        var source = GridSource.Open(32, 3);
        var host = new RouteHost(source, new(0, 1), Step);
        var follower = new RouteFollower(source);
        Assert.IsTrue(follower.SetTarget(new(20, 1), host.State.Time));
        // 20 steps of one time unit, observed in quarter-unit frames, plus slack after arrival.
        host.Frames(follower, 4 * 22);
        Assert.AreEqual((RouteFollowStatus.Arrived, "ROUTE_ARRIVED"), (follower.Status, follower.StopReason));
        Assert.AreEqual(new CellCoord(20, 1), host.State.CommittedCell);
        Assert.AreEqual(MovementPhase.Settled, host.State.Phase);
        Assert.AreEqual(ActorIntent.Neutral, host.State.LatestIntent);
        Assert.AreEqual(3, follower.PlannedSegments, "8 + 8 + 4 steps; the cap is per segment, not per goal.");
        CollectionAssert.AreEqual(Enumerable.Range(1, 20).Select(x => new CellCoord(x, 1)).ToArray(), host.Committed());
        Assert.IsFalse(host.Events.Any(e => e.Kind is MovementEventKind.StepRejected or MovementEventKind.StepCancelled));
        // Steps chain at exact step ends: no idle gap between consecutive cells.
        var starts = host.Events.Where(e => e.Kind == MovementEventKind.StepStarted).Select(e => e.Time).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(0, 20).Select(i => T(i)).ToArray(), starts);
    }

    [TestMethod]
    public void RouteAroundAWallMatchesTheRuntimeAdmission()
    {
        var source = new GridSource(".......", "...#...", "...#...", "...#...", ".......");
        var host = new RouteHost(source, new(1, 2), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(5, 2), host.State.Time);
        host.Frames(follower, 4 * 8);
        Assert.AreEqual(RouteFollowStatus.Arrived, follower.Status);
        Assert.AreEqual(new CellCoord(5, 2), host.State.CommittedCell);
        CollectionAssert.AreEqual(follower.LastPlan!.Cells.ToArray(), host.Committed());
        Assert.IsFalse(host.Events.Any(e => e.Kind == MovementEventKind.StepRejected));
    }

    [TestMethod]
    public void NewClickMidStepFinishesTheStepThenReplansWithoutAJump()
    {
        var source = GridSource.Open(12, 12);
        var host = new RouteHost(source, new(2, 2), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(8, 2), host.State.Time);
        host.Frames(follower, 6); // 1.5 units: the second step (3,2)->(4,2) is in progress.
        Assert.AreEqual(MovementPhase.OutboundAfterEntry, host.State.Phase);
        Assert.IsFalse(follower.SetTarget(new(8, 2), host.State.Time), "The same goal is kept, not queued again.");
        Assert.AreEqual(1, follower.PlannedSegments);
        Assert.IsTrue(follower.SetTarget(new(4, 6), host.State.Time));
        host.Frames(follower, 4 * 8);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(4, 6)), (follower.Status, host.State.CommittedCell));
        Assert.IsFalse(host.Events.Any(e => e.Kind == MovementEventKind.StepCancelled), "Retargeting never cancels a started step.");
        var committed = host.Committed();
        CollectionAssert.AreEqual(new CellCoord[] { new(3, 2), new(4, 2), new(4, 3), new(4, 4), new(4, 5), new(4, 6) }, committed);
    }

    [TestMethod]
    public void UnreachableGoalStopsWithItsReasonAndNoMotion()
    {
        var source = new GridSource(".......", ".#####.", ".#...#.", ".#####.", ".......");
        var host = new RouteHost(source, new(0, 0), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(3, 2), host.State.Time);
        host.Frames(follower, 4);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_NOT_FOUND"), (follower.Status, follower.StopReason));
        Assert.AreEqual(RouteOutcome.NoRoute, follower.LastPlan!.Outcome);
        Assert.AreEqual(0, host.Events.Count);
        follower.SetTarget(new(0, 9), host.State.Time);
        host.Frames(follower, 1);
        Assert.AreEqual("ROUTE_TARGET_OUTSIDE_AREA", follower.StopReason);
    }

    [TestMethod]
    public void DeniedStepStopsTheRouteAndClearsTheIntent()
    {
        var plan = GridSource.Open(10, 1);
        // The authority disagrees about (4,0): the follower must report it, never step through.
        var admission = new GridSource("....#.....");
        var host = new RouteHost(admission, new(0, 0), Step);
        var follower = new RouteFollower(plan);
        follower.SetTarget(new(8, 0), host.State.Time);
        host.Frames(follower, 4 * 6);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED"), (follower.Status, follower.StopReason));
        Assert.AreEqual(new CellCoord(3, 0), host.State.CommittedCell);
        Assert.AreEqual(1, host.Events.Count(e => e.Kind == MovementEventKind.StepRejected));
        Assert.AreEqual(ActorIntent.Neutral, host.State.LatestIntent);
    }

    [TestMethod]
    public void StopMidStepEndsAtTheNextCellCentre()
    {
        var source = GridSource.Open(12, 1);
        var host = new RouteHost(source, new(0, 0), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(10, 0), host.State.Time);
        host.Frames(follower, 5); // Second step in progress, before its entry.
        Assert.AreEqual(MovementPhase.OutboundBeforeEntry, host.State.Phase);
        follower.Stop(host.State.Time);
        host.Frames(follower, 8);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STOPPED_BY_HOST"), (follower.Status, follower.StopReason));
        Assert.AreEqual((new CellCoord(2, 0), MovementPhase.Settled), (host.State.CommittedCell, host.State.Phase));
        Assert.IsFalse(host.Events.Any(e => e.Kind == MovementEventKind.StepCancelled));
    }

    [TestMethod]
    public void OneLongAdvanceRunsEveryStepBoundaryAsItsOwnSubAdvance()
    {
        var source = GridSource.Open(16, 1);
        var host = new RouteHost(source, new(0, 0), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(3, 0), T(1, 2));
        var drive = follower.Drive(host.State, T(10), host.Advance);
        Assert.IsTrue(drive.Completed);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(3, 0)), (follower.Status, drive.State.CommittedCell));
        // Click at 1/2, then boundaries at 3/2, 5/2, 7/2 (arrival with neutral), then the frame end.
        CollectionAssert.AreEqual(new[] { T(1, 2), T(3, 2), T(5, 2), T(7, 2), T(10) }, drive.Advances.Select(a => a.State!.Time).ToArray());
        Assert.AreEqual(T(10), host.State.Time);
        Assert.ThrowsExactly<ArgumentException>(() => follower.Next(host.State, T(9)));
    }
    [TestMethod]
    public void ReclickAfterADenialAtTheFrameEndStartsAgain()
    {
        var source = GridSource.Open(8, 1);
        var denials = 1;
        var admission = new GridSource("........") { Override = (from, _) => from == new CellCoord(0, 0) && denials-- > 0 ? RouteStepVerdict.Blocked : null };
        var host = new RouteHost(admission, new(0, 0), Step);
        host.Advance(T(0), []); // Close the watermark: the click input then lands at the frame target.
        var follower = new RouteFollower(source);
        follower.SetTarget(new(5, 0), host.State.Time);
        host.Frames(follower, 1);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED"), (follower.Status, follower.StopReason));
        Assert.IsTrue(host.State.LatestIntent.Active, "The denial ended the drive; the stale direction is still set.");
        Assert.IsTrue(follower.SetTarget(new(5, 0), host.State.Time));
        host.Frames(follower, 4 * 8);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(5, 0)), (follower.Status, host.State.CommittedCell));
    }

    [TestMethod]
    public void AGoalObservedAfterTheAdvanceTargetWaits()
    {
        var source = GridSource.Open(8, 1);
        var host = new RouteHost(source, new(0, 0), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(3, 0), T(5));
        var early = follower.Next(host.State, T(1));
        Assert.AreEqual((T(1), 0), (early.Until, early.Batches.Count));
        var drive = follower.Drive(host.State, T(10), host.Advance);
        Assert.AreEqual(T(5), host.Events.First(e => e.Kind == MovementEventKind.StepStarted).Time);
        Assert.AreEqual(RouteFollowStatus.Arrived, follower.Status);
        Assert.IsTrue(drive.Completed);
    }

    [TestMethod]
    public void IntentOwnedByAnotherSourceIsNeverCleared()
    {
        var source = GridSource.Open(16, 1);
        var host = new RouteHost(source, new(0, 0), Step);
        host.Advance(T(0), [Batch(0, Press(0))]); // e.g. the stick
        var follower = new RouteFollower(source);
        host.Frames(follower, 4 * 4);
        Assert.AreEqual(new CellCoord(4, 0), host.State.CommittedCell);
        Assert.AreEqual(ActorIntent.Toward(NeighborDirection.XPlus), host.State.LatestIntent);
        Assert.IsFalse(host.Events.Any(e => e.Kind is MovementEventKind.StepCancelled or MovementEventKind.StepRejected));
    }

    [TestMethod]
    public void HostCancelledStepIsReplannedFromItsSourceCell()
    {
        var source = GridSource.Open(8, 1);
        var host = new RouteHost(source, new(0, 0), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(3, 0), host.State.Time);
        host.Frames(follower, 1);
        follower.Observe(host.Advance(T(3, 8), [new InputBatch(T(3, 8), [MovementInput.Set(host.State.LatestInputKey!.Value.Sequence + 1, ActorIntent.Neutral)])]));
        Assert.AreEqual(MovementPhase.Returning, host.State.Phase);
        host.Frames(follower, 4 * 6);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(3, 0)), (follower.Status, host.State.CommittedCell));
        Assert.AreEqual(1, host.Events.Count(e => e.Kind == MovementEventKind.StepCancelled));
    }

    [TestMethod]
    public void RejectedHostAdvanceIsATypedDriveFailure()
    {
        var source = GridSource.Open(8, 1);
        var host = new RouteHost(source, new(0, 0), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(3, 0), host.State.Time);
        var drive = follower.Drive(host.State, T(4), (until, batches) => MovementRuntime.AdvanceTo(null, new(until, batches.ToArray(), [])));
        Assert.AreEqual((false, "ROUTE_ADVANCE_REJECTED", 1), (drive.Completed, drive.FailureCode, drive.Advances.Count));
        Assert.AreSame(host.State, drive.State);
    }
}
