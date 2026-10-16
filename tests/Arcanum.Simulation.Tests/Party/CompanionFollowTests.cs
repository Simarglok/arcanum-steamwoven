using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Tests.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Party;

[TestClass]
public sealed class CompanionFollowTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    [TestMethod]
    public void DistancesStartRangeRunAndJumpFollowTheSourceNumbers()
    {
        CellCoord At(int x) => new(x, 0);
        var leader = At(0);
        Assert.AreEqual(FollowAction.Separate, FollowDistanceRules.Decide(leader, leader, false, false));
        Assert.AreEqual(FollowAction.Separate, FollowDistanceRules.Decide(leader, leader, false, true));
        foreach (var d in new[] { 1, 2, 3, 4 }) Assert.AreEqual(FollowAction.Hold, FollowDistanceRules.Decide(At(d), leader, false, false), $"no new goal within 4: {d}");
        Assert.AreEqual(FollowAction.Run, FollowDistanceRules.Decide(At(5), leader, false, false), "a goal starts beyond 4, and beyond range 2 + 2 it is a run");
        Assert.AreEqual(FollowAction.Hold, FollowDistanceRules.Decide(At(2), leader, false, true), "an alive goal holds within range 2");
        Assert.AreEqual(FollowAction.Walk, FollowDistanceRules.Decide(At(3), leader, false, true));
        Assert.AreEqual(FollowAction.Walk, FollowDistanceRules.Decide(At(4), leader, false, true));
        // Spread out: start beyond 7, range 5, walk within 7.
        Assert.AreEqual(FollowAction.Hold, FollowDistanceRules.Decide(At(7), leader, true, false));
        Assert.AreEqual(FollowAction.Run, FollowDistanceRules.Decide(At(8), leader, true, false));
        Assert.AreEqual(FollowAction.Walk, FollowDistanceRules.Decide(At(6), leader, true, true));
        Assert.AreEqual(FollowAction.Hold, FollowDistanceRules.Decide(At(5), leader, true, true));
        // The 30-cell move is strict and uses the Chebyshev distance.
        Assert.AreEqual(FollowAction.Run, FollowDistanceRules.Decide(new(30, -30), leader, false, false));
        Assert.AreEqual(FollowAction.Jump, FollowDistanceRules.Decide(new(-31, 5), leader, false, true));
        Assert.AreEqual(4294967295L, FollowDistanceRules.Distance(new(int.MinValue, 0), new(int.MaxValue, 0)), "no overflow");
    }

    [TestMethod]
    public void AFarLeaderIsApproachedThroughTheRouteAndTheFollowerStopsWithinTwo()
    {
        var source = GridSource.Open(24, 3);
        var host = new RouteHost(source, new(1, 1), Step);
        var follow = new CompanionFollower(source);
        var actions = Frames(host, follow, () => new(12, 1), 4 * 14);
        Assert.AreEqual(new CellCoord(10, 1), host.State.CommittedCell, "stops at distance 2, not on the leader");
        Assert.AreEqual(MovementPhase.Settled, host.State.Phase);
        Assert.AreEqual(RouteFollowStatus.Arrived, follow.Route.Status);
        Assert.AreEqual(FollowAction.Run, actions[0], "11 cells away: the source's run-near goal (walking speed here)");
        Assert.AreEqual(FollowAction.Hold, actions[^1]);
        CollectionAssert.AreEqual(Enumerable.Range(2, 9).Select(x => new CellCoord(x, 1)).ToArray(), host.Committed());
        Assert.IsFalse(host.Events.Any(e => e.Kind is MovementEventKind.StepRejected or MovementEventKind.StepCancelled));

        // Within the start distance nothing starts again.
        var steps = host.Committed().Length;
        Frames(host, follow, () => new(14, 1), 4 * 4);
        Assert.AreEqual(steps, host.Committed().Length, "the leader 4 cells away starts no goal");
        Frames(host, follow, () => new(15, 1), 4 * 6);
        Assert.AreEqual(new CellCoord(13, 1), host.State.CommittedCell, "5 cells away: it closes to 2 again");
    }

    [TestMethod]
    public void AMovingLeaderIsFollowedToItsNewCellWithoutCancellingAStep()
    {
        var source = GridSource.Open(30, 12);
        var host = new RouteHost(source, new(1, 1), Step);
        var follow = new CompanionFollower(source);
        var leader = new CellCoord(10, 1);
        var frame = 0;
        // The leader walks one cell down every unit while the follower approaches.
        Frames(host, follow, () => frame++ % 4 == 0 && leader.Y < 9 ? leader = leader with { Y = leader.Y + 1 } : leader, 4 * 20);
        Assert.AreEqual(new CellCoord(10, 9), leader);
        // It closes to range 2 of the moving leader, then holds once the leader's last steps leave it within 4.
        Assert.IsTrue(FollowDistanceRules.Distance(host.State.CommittedCell, leader) <= FollowDistanceRules.StartDistance, $"ends within 4 of the leader: {host.State.CommittedCell}");
        Assert.IsTrue(host.State.CommittedCell.Y >= 6, $"it followed the leader down, not to the leader's first cell: {host.State.CommittedCell}");
        Assert.AreEqual(MovementPhase.Settled, host.State.Phase);
        Assert.IsFalse(host.Events.Any(e => e.Kind is MovementEventKind.StepRejected or MovementEventKind.StepCancelled));
        Assert.IsTrue(follow.Route.PlannedSegments >= 1);
    }

    [TestMethod]
    public void AnUnreachableLeaderIsAskedOnceUntilACellChanges()
    {
        // The leader stands in a closed pocket at (6,2).
        var source = new GridSource("#########", "#....###.", "#....#.#.", "#....###.", "#########");
        var host = new RouteHost(source, new(1, 2), Step);
        var follow = new CompanionFollower(source);
        var leader = new CellCoord(6, 2);
        Frames(host, follow, () => leader, 8);
        Assert.AreEqual(RouteFollowStatus.Stopped, follow.Route.Status);
        Assert.AreEqual("ROUTE_NOT_FOUND", follow.Route.StopReason);
        Assert.AreEqual((new CellCoord(1, 2), leader), follow.Unreachable);
        var asked = source.Asked.Values.Sum();
        Frames(host, follow, () => leader, 40);
        Assert.AreEqual(asked, source.Asked.Values.Sum(), "no new plan while neither cell changed");
        Assert.AreEqual(new CellCoord(1, 2), host.State.CommittedCell);
        // The leader steps out of the pocket: the follower plans again and closes in.
        leader = new(8, 2);
        Frames(host, follow, () => leader, 4 * 2);
        Assert.IsTrue(source.Asked.Values.Sum() > asked, "a new leader cell is planned again");
    }

    [TestMethod]
    public void BeyondThirtyCellsTheHostIsAskedToJumpThenTheSharedCellIsLeft()
    {
        var source = GridSource.Open(48, 5);
        var host = new RouteHost(source, new(1, 2), Step);
        var follow = new CompanionFollower(source);
        var update = follow.Update(host.State, new(40, 2), false, true, Fixtures.T(1, 4));
        Assert.AreEqual(new CompanionFollowUpdate(FollowAction.Jump, new CellCoord(40, 2)), update);
        Assert.AreNotEqual(RouteFollowStatus.Following, follow.Route.Status, "a jump starts no walk");

        // The host stands the follower on the leader's cell; the next decision steps off it.
        var jumped = new RouteHost(source, new(40, 2), Step);
        var actions = Frames(jumped, follow, () => new(40, 2), 4 * 2);
        Assert.AreEqual(FollowAction.Separate, actions[0]);
        Assert.AreEqual(new CellCoord(39, 1), jumped.State.CommittedCell, "the first Open neighbour in direction order");
        Assert.AreEqual(MovementPhase.Settled, jumped.State.Phase);
        Assert.AreEqual(FollowAction.Hold, actions[^1]);
    }

    [TestMethod]
    public void AWaitingCompanionDropsItsGoalButFinishesTheStartedStep()
    {
        var source = GridSource.Open(24, 3);
        var host = new RouteHost(source, new(1, 1), Step);
        var follow = new CompanionFollower(source);
        Frames(host, follow, () => new(14, 1), 7); // 1.75 units: the second step has just entered (3,1).
        Assert.AreEqual(MovementPhase.OutboundAfterEntry, host.State.Phase);
        Frames(host, follow, () => new(14, 1), 4 * 4, following: false);
        Assert.AreEqual((RouteFollowStatus.Stopped, CompanionFollower.NotFollowingReason), (follow.Route.Status, follow.Route.StopReason));
        Assert.AreEqual(new CellCoord(3, 1), host.State.CommittedCell, "the started step ends at its cell centre");
        Assert.AreEqual(MovementPhase.Settled, host.State.Phase);
        Assert.IsFalse(host.Events.Any(e => e.Kind == MovementEventKind.StepCancelled));
        Assert.IsNull(follow.Unreachable, "a deliberate stop is not an unreachable goal");
        // Following again: it approaches from where it stands.
        Frames(host, follow, () => new(14, 1), 4 * 12);
        Assert.AreEqual(new CellCoord(12, 1), host.State.CommittedCell);
    }

    [TestMethod]
    public void SpreadOutKeepsTheWiderDistances()
    {
        var source = GridSource.Open(24, 3);
        var host = new RouteHost(source, new(1, 1), Step);
        var follow = new CompanionFollower(source);
        Frames(host, follow, () => new(8, 1), 4 * 4, spreadOut: true);
        Assert.AreEqual(new CellCoord(1, 1), host.State.CommittedCell, "7 cells away: no goal when spread out");
        Frames(host, follow, () => new(9, 1), 4 * 6, spreadOut: true);
        Assert.AreEqual(new CellCoord(4, 1), host.State.CommittedCell, "8 cells away: closes to range 5");
    }

    [TestMethod]
    public void RouteRangeIsPartOfTheGoal()
    {
        var source = GridSource.Open(12, 3);
        var host = new RouteHost(source, new(1, 1), Step);
        var route = new RouteFollower(source);
        Assert.IsTrue(route.SetTarget(new(9, 1), host.State.Time, 3));
        Assert.IsFalse(route.SetTarget(new(9, 1), host.State.Time, 3), "the same goal and range is kept");
        Assert.IsTrue(route.SetTarget(new(9, 1), host.State.Time, 2), "another range is another goal");
        host.Frames(route, 4 * 8);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(7, 1)), (route.Status, host.State.CommittedCell));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => route.SetTarget(new(9, 1), host.State.Time, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => route.SetTarget(new(9, 1), host.State.Time, RouteLimits.AxisDistanceMaximum + 1));
    }

    // One host frame per quarter unit: the leader's cell, an update at the frame boundary, then the drive to it.
    private static List<FollowAction> Frames(RouteHost host, CompanionFollower follow, Func<CellCoord> leader, int frames,
        bool spreadOut = false, bool following = true)
    {
        var actions = new List<FollowAction>();
        for (var i = 0; i < frames; i++)
        {
            var target = new SimulationTime(host.State.Time.Value + ExactScalar.Create(1, 4));
            actions.Add(follow.Update(host.State, leader(), spreadOut, following, target).Action);
            var drive = follow.Drive(host.State, target, host.Advance);
            Assert.IsTrue(drive.Completed, drive.FailureCode);
        }
        return actions;
    }
}
