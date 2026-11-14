using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-WORLD-042: corridors to goals beyond the local planner's reach, and the follower walking them.</summary>
[TestClass]
public sealed class RouteCorridorTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    /// <summary>A 120 × 9 chart with walls across it; their only gaps alternate between the top and bottom rows.</summary>
    private static GridSource Maze()
    {
        var rows = Enumerable.Range(0, 9).Select(_ => new char[120]).ToArray();
        foreach (var row in rows) Array.Fill(row, '.');
        foreach (var x in new[] { 20, 50, 80 })
            for (var y = 0; y < 9; y++)
                if (!(x == 50 ? y == 0 : y == 8)) rows[y][x] = '#';
        return new(rows.Select(r => new string(r)).ToArray());
    }

    [TestMethod]
    public void A_corridor_reaches_a_far_goal_in_waypoints_the_local_planner_can_reach()
    {
        var source = Maze();
        var corridor = RouteCorridor.Plan(source, new(0, 4), new(110, 4));
        Assert.AreEqual((RouteCorridorOutcome.Found, "ROUTE_CORRIDOR"), (corridor.Outcome, corridor.ReasonCode));
        Assert.AreEqual(new CellCoord(110, 4), corridor.End);
        var previous = corridor.From;
        foreach (var waypoint in corridor.Waypoints)
        {
            Assert.IsTrue(Math.Max(Math.Abs(waypoint.X - previous.X), Math.Abs(waypoint.Y - previous.Y)) <= RouteCorridorLimits.WaypointStepMaximum);
            Assert.AreEqual(RouteOutcome.Found, RoutePlanner.Plan(source, previous, waypoint).Outcome, $"{previous} -> {waypoint}");
            previous = waypoint;
        }
        // Diagonal steps take the gaps at no extra length: 110 cells (the follower test checks the gap cells).
        Assert.AreEqual(110, corridor.Cells);
        // The local planner alone refuses the goal (beyond its 32-cell reach).
        Assert.AreEqual("ROUTE_TARGET_OUT_OF_RANGE", RoutePlanner.Plan(source, new(0, 4), new(110, 4)).ReasonCode);
    }

    [TestMethod]
    public void A_goal_off_the_source_or_walled_in_gives_a_partial_corridor_to_the_nearest_reached_cell()
    {
        var source = GridSource.Open(40, 5);
        var off = RouteCorridor.Plan(source, new(0, 2), new(100, 2));
        Assert.AreEqual((RouteCorridorOutcome.Partial, "ROUTE_TARGET_OUTSIDE_AREA", new CellCoord(39, 2)), (off.Outcome, off.ReasonCode, off.End));
        var walled = new GridSource(".....#...", ".....#.#.", ".....#...");
        var inside = RouteCorridor.Plan(walled, new(0, 1), new(8, 1));
        Assert.AreEqual((RouteCorridorOutcome.Partial, "ROUTE_CORRIDOR_PARTIAL", new CellCoord(4, 1)), (inside.Outcome, inside.ReasonCode, inside.End));
        // Nothing reached but the start: no route; the start outside the source: refused.
        Assert.AreEqual(RouteCorridorOutcome.NoRoute, RouteCorridor.Plan(new GridSource(".#", "##"), new(0, 0), new(5, 5)).Outcome);
        Assert.AreEqual(RouteCorridorOutcome.Refused, RouteCorridor.Plan(source, new(-1, 0), new(5, 2)).Outcome);
        // A lowered expansion limit ends the search early, partially.
        var limited = RouteCorridor.Plan(GridSource.Open(200, 3), new(0, 1), new(199, 1), new RouteCorridorLimits(expansions: 50));
        Assert.AreEqual((RouteCorridorOutcome.Partial, "ROUTE_CORRIDOR_EXPANSION_LIMIT"), (limited.Outcome, limited.ReasonCode));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteCorridorLimits(expansions: RouteCorridorLimits.ExpansionMaximum + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteCorridorLimits(waypointSteps: 25));
    }

    [TestMethod]
    public void The_follower_walks_a_goal_beyond_its_reach_through_the_maze_and_stops_on_its_centre()
    {
        var source = Maze();
        var host = new RouteHost(source, new(0, 4), Step);
        var follower = new RouteFollower(source, corridorLimits: new());
        Assert.IsTrue(follower.SetTarget(new(110, 4), host.State.Time));
        host.Frames(follower, 4 * 200);
        Assert.AreEqual((RouteFollowStatus.Arrived, "ROUTE_ARRIVED"), (follower.Status, follower.StopReason));
        Assert.AreEqual(new CellCoord(110, 4), host.State.CommittedCell);
        Assert.AreEqual(RouteCorridorOutcome.Found, follower.Corridor!.Outcome);
        Assert.IsFalse(host.Events.Any(e => e.Kind is MovementEventKind.StepRejected or MovementEventKind.StepCancelled));
        // Every gap was passed.
        var cells = host.Committed();
        Assert.IsTrue(cells.Contains(new(20, 8)) && cells.Contains(new(50, 0)) && cells.Contains(new(80, 8)));
        // A new goal within reach drops the corridor.
        follower.SetTarget(new(105, 4), host.State.Time);
        host.Frames(follower, 4 * 8);
        Assert.AreEqual(RouteFollowStatus.Arrived, follower.Status);
        Assert.IsNull(follower.Corridor);
    }

    [TestMethod]
    public void A_walled_in_goal_beyond_reach_is_walked_toward_and_stops_partial_and_one_off_the_source_is_refused()
    {
        // The goal (55, 1) lies behind a wall from top to bottom at x 50: the corridor ends at (49, 1).
        var rows = Enumerable.Range(0, 3).Select(_ => new string('.', 50) + "#" + new string('.', 9)).ToArray();
        var source = new GridSource(rows);
        var host = new RouteHost(source, new(0, 1), Step);
        var follower = new RouteFollower(source, corridorLimits: new());
        follower.SetTarget(new(55, 1), host.State.Time);
        host.Frames(follower, 4 * 60);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_PARTIAL"), (follower.Status, follower.StopReason));
        Assert.AreEqual(new CellCoord(49, 1), host.State.CommittedCell);
        Assert.AreEqual((MovementPhase.Settled, ActorIntent.Neutral), (host.State.Phase, host.State.LatestIntent));
        // A goal outside the source is refused without motion, as before.
        var open = GridSource.Open(60, 3);
        var still = new RouteHost(open, new(0, 1), Step);
        var refused = new RouteFollower(open, corridorLimits: new());
        refused.SetTarget(new(200, 1), still.State.Time);
        still.Frames(refused, 4);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_TARGET_OUTSIDE_AREA", 0), (refused.Status, refused.StopReason, still.Events.Count));
    }

    [TestMethod]
    public void An_occupant_on_a_waypoint_rebuilds_the_corridor_once_and_the_walk_goes_on()
    {
        var source = GridSource.Open(100, 9);
        var host = new RouteHost(source, new(0, 4), Step);
        var follower = new RouteFollower(source, corridorLimits: new());
        follower.SetTarget(new(90, 4), host.State.Time);
        host.Frames(follower, 4);
        var first = follower.Corridor!;
        Assert.AreEqual(RouteCorridorOutcome.Found, first.Outcome);
        // An occupant arrives on the second waypoint: no step enters it any more.
        var occupied = first.Waypoints[1];
        source.Override = (from, direction) => RoutePlanner.Neighbor(from, direction) == occupied ? RouteStepVerdict.Blocked : null;
        host.Frames(follower, 4 * 120);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(90, 4)), (follower.Status, host.State.CommittedCell));
        Assert.AreNotSame(first, follower.Corridor);
        Assert.IsFalse(host.Committed().Contains(occupied));
    }

    [TestMethod]
    public void The_far_click_switch_is_asked_when_a_goal_is_set_and_Original_does_not_move()
    {
        // ARC-WORLD-072 (ledger LR4, LR7): under Original a click more than 32 tiles away starts no walk; Steamwoven follows a corridor.
        foreach (var profile in NavigationRuleProfile.All)
        {
            var source = GridSource.Open(100, 3);
            var host = new RouteHost(source, new(0, 1), Step);
            var asked = 0;
            var follower = new RouteFollower(source, corridorLimits: new(), corridorAllowed: () => { asked++; return !profile.FarClickDoesNotMove; });
            follower.SetTarget(new(90, 1), host.State.Time);
            Assert.AreEqual(1, asked, "asked once, when the click sets the goal");
            host.Frames(follower, 4 * 100);
            Assert.AreEqual(1, asked, "not asked again while walking");
            if (profile.FarClickDoesNotMove)
            {
                Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_TARGET_OUT_OF_RANGE", (RouteCorridor?)null), (follower.Status, follower.StopReason, follower.Corridor));
                Assert.AreEqual(new CellCoord(0, 1), host.State.CommittedCell);
                Assert.IsFalse(host.Events.Any(e => e.Kind == MovementEventKind.StepStarted), "the PC does not move");
                // A goal within 32 tiles (the larger axis) still walks.
                follower.SetTarget(new(32, 2), host.State.Time);
                host.Frames(follower, 4 * 40);
                Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(32, 2)), (follower.Status, host.State.CommittedCell));
            }
            else
            {
                Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(90, 1)), (follower.Status, host.State.CommittedCell));
                Assert.AreEqual(RouteCorridorOutcome.Found, follower.Corridor!.Outcome);
            }
        }
        Assert.IsTrue(NavigationRuleProfile.Original.FarClickDoesNotMove);
        Assert.IsFalse(NavigationRuleProfile.Steamwoven.FarClickDoesNotMove);
        Assert.AreSame(NavigationRuleProfile.Original, NavigationRuleProfile.FromName("original"));
        Assert.IsNull(NavigationRuleProfile.FromName("Original"));
    }

    [TestMethod]
    public void A_near_goal_the_request_cannot_reach_follows_a_corridor_only_under_Steamwoven()
    {
        // ARC-WORLD-073: a goal 25 tiles away behind a wall whose gap lies outside the request's 64-tile window. Steamwoven follows a
        // corridor; Original has none (the original has no long route, LR7): the PC's straight-line retry (LR4) walks toward the goal
        // until the wall refuses a step, and a follower without the retry does not move.
        var rows = Enumerable.Range(0, 200).Select(y => new string('.', 12) + (y == 5 ? "." : "#") + new string('.', 17)).ToArray();
        var source = new GridSource(rows);
        Assert.AreEqual("ROUTE_WINDOW_EXHAUSTED", RoutePlanner.Plan(source, new(0, 150), new(25, 150)).ReasonCode);
        foreach (var profile in NavigationRuleProfile.All)
            foreach (var retry in new[] { false, true })
            {
                var host = new RouteHost(source, new(0, 150), Step);
                var follower = new RouteFollower(source, corridorLimits: new(), corridorAllowed: () => !profile.FarClickDoesNotMove, straightRetry: retry);
                follower.SetTarget(new(25, 150), host.State.Time);
                host.Frames(follower, 4 * 400);
                var at = (follower.Status, follower.StopReason, host.State.CommittedCell);
                if (!profile.FarClickDoesNotMove) Assert.AreEqual((RouteFollowStatus.Arrived, "ROUTE_ARRIVED", new CellCoord(25, 150)), at, profile.Name);
                else if (retry) Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED", new CellCoord(11, 150)), at, $"the straight line up to the wall: {at}");
                else Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_WINDOW_EXHAUSTED", new CellCoord(0, 150)), at, "no path, no move");
            }
    }

    [TestMethod]
    public void A_corridor_leg_the_local_request_cannot_find_is_walked_on_the_corridor_cells_not_on_the_straight_line()
    {
        // ARC-DEV-2100: a wall across the way with its only gap far off the straight line; the local request's budget (here lowered to 20) cannot
        // plan the detour of a leg, and the PC's straight line (LR4) would run into the wall (ROUTE_STEP_DENIED). Steamwoven's corridor walks its
        // own cells there; the walk without a corridor (Original) keeps the straight line.
        var rows = Enumerable.Range(0, 40).Select(y => new string('.', 30) + (y == 30 ? "." : "#") + new string('.', 69)).ToArray();
        var source = new GridSource(rows);
        var limits = new RouteLimits(budget: 20);
        var host = new RouteHost(source, new(20, 10), Step);
        var follower = new RouteFollower(source, limits, corridorLimits: new(), straightRetry: true);
        follower.SetTarget(new(60, 10), host.State.Time);
        var methods = new HashSet<RouteMethod>();
        for (var i = 0; i < 4 * 400 && follower.Status == RouteFollowStatus.Following; i++)
        {
            host.Frames(follower, 1);
            if (follower.LastPlan?.Method is { } method) methods.Add(method);
        }
        Assert.AreEqual((RouteFollowStatus.Arrived, "ROUTE_ARRIVED", new CellCoord(60, 10)), (follower.Status, follower.StopReason, host.State.CommittedCell));
        Assert.IsTrue(methods.Contains(RouteMethod.CorridorWalk), "the leg was walked on the corridor's cells");
        Assert.IsFalse(methods.Contains(RouteMethod.StraightRetry));
        Assert.IsFalse(host.Events.Any(e => e.Kind == MovementEventKind.StepRejected), "no step ran into the wall");
        Assert.IsTrue(host.Committed().Contains(new(30, 30)), "through the gap");
    }

    [TestMethod]
    public void The_same_far_goal_clicked_again_after_a_switch_is_a_new_goal()
    {
        var source = GridSource.Open(200, 3);
        var host = new RouteHost(source, new(0, 1), Step);
        var original = false;
        var follower = new RouteFollower(source, corridorLimits: new(), corridorAllowed: () => !original);
        Assert.IsTrue(follower.SetTarget(new(90, 1), host.State.Time));
        host.Frames(follower, 4 * 4);
        Assert.IsFalse(follower.SetTarget(new(90, 1), host.State.Time), "the same goal, the same answer: kept");
        original = true;
        Assert.IsTrue(follower.SetTarget(new(90, 1), host.State.Time), "the answer changed: a new goal");
        host.Frames(follower, 4 * 10);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_TARGET_OUT_OF_RANGE"), (follower.Status, follower.StopReason));
    }

    [TestMethod]
    public void A_switch_change_applies_at_the_next_click()
    {
        // Read at the decision: a corridor walk already started goes on; the next far click reads the new value.
        var source = GridSource.Open(200, 3);
        var host = new RouteHost(source, new(0, 1), Step);
        var original = false;
        var follower = new RouteFollower(source, corridorLimits: new(), corridorAllowed: () => !original);
        follower.SetTarget(new(90, 1), host.State.Time);
        host.Frames(follower, 4 * 10);
        original = true;
        host.Frames(follower, 4 * 100);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(90, 1)), (follower.Status, host.State.CommittedCell));
        follower.SetTarget(new(190, 1), host.State.Time);
        host.Frames(follower, 4 * 10);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_TARGET_OUT_OF_RANGE", new CellCoord(90, 1)), (follower.Status, follower.StopReason, host.State.CommittedCell));
    }

    [TestMethod]
    public void Without_corridor_limits_a_goal_beyond_reach_is_refused_as_before()
    {
        // A companion follow builds its follower without corridor limits.
        var source = GridSource.Open(100, 3);
        var host = new RouteHost(source, new(0, 1), Step);
        var follower = new RouteFollower(source);
        follower.SetTarget(new(90, 1), host.State.Time);
        host.Frames(follower, 4);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_TARGET_OUT_OF_RANGE", (RouteCorridor?)null), (follower.Status, follower.StopReason, follower.Corridor));
    }

    [TestMethod]
    public void A_second_failed_leg_stops_with_its_reason_and_a_corridor_reaching_nothing_with_its_own()
    {
        var source = GridSource.Open(100, 9);
        var host = new RouteHost(source, new(0, 4), Step);
        var follower = new RouteFollower(source, corridorLimits: new());
        follower.SetTarget(new(90, 4), host.State.Time);
        host.Frames(follower, 4);
        // An occupant on the corridor's next waypoint, then another on the rebuilt corridor's: the second leg fails.
        var occupied = new HashSet<CellCoord> { follower.Corridor!.Waypoints[1] };
        source.Override = (from, direction) => occupied.Contains(RoutePlanner.Neighbor(from, direction)) ? RouteStepVerdict.Blocked : null;
        var first = follower.Corridor;
        for (var frame = 0; frame < 4 * 120 && ReferenceEquals(follower.Corridor, first); frame++) host.Frames(follower, 1);
        Assert.AreNotSame(first, follower.Corridor);
        var rebuilt = follower.Corridor!;
        occupied.Add(rebuilt.Waypoints.First(w => !host.Committed().Contains(w) && w != host.State.CommittedCell));
        host.Frames(follower, 4 * 120);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_NOT_FOUND"), (follower.Status, follower.StopReason));
        Assert.AreSame(rebuilt, follower.Corridor);

        // An actor walled in on its own cell: the corridor reaches no cell and the follower stops with its reason.
        var pocket = new GridSource("##" + new string('.', 98), ".#" + new string('.', 98), "##" + new string('.', 98));
        var stuck = new RouteHost(pocket, new(0, 1), Step);
        var walled = new RouteFollower(pocket, corridorLimits: new());
        walled.SetTarget(new(90, 1), stuck.State.Time);
        stuck.Frames(walled, 4);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_NOT_FOUND", RouteCorridorOutcome.NoRoute), (walled.Status, walled.StopReason, walled.Corridor!.Outcome));
    }
}
