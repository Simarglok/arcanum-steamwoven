using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-WORLD-073: the follower walks as the original's walk does (ledger LR2–LR4).</summary>
[TestClass]
public sealed class RouteWalkTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    /// <summary>A wall across the chart between walker and goal, its only gap far to the side.</summary>
    private static GridSource Detour() =>
        new(Enumerable.Range(0, 40).Select(y => y == 20 ? new string('#', 30) + "." + new string('#', 9) : new string('.', 40)).ToArray());

    [TestMethod]
    public void Each_plan_has_the_walks_budget_so_a_detour_longer_than_it_fails()
    {
        // The goal 4 rows away, across the wall: the walk's budget min(4 × 4 + 5, 200) = 21 drops the detour through the gap at x 30.
        var source = Detour();
        Assert.AreEqual("ROUTE_BUDGET_EXHAUSTED", RoutePlanner.Plan(source, new(5, 18), new(5, 22), new RouteLimits(budget: RouteLimits.WalkBudget(new(5, 18), new(5, 22)))).ReasonCode);
        Assert.AreEqual(RouteOutcome.Found, RoutePlanner.Plan(source, new(5, 18), new(5, 22)).Outcome, "with the request's full 200 the detour is found");
        foreach (var profile in NavigationRuleProfile.All)
        {
            var host = new RouteHost(source, new(5, 18), Step);
            var follower = new RouteFollower(source, corridorLimits: new(), corridorAllowed: () => !profile.FarClickDoesNotMove);
            follower.SetTarget(new(5, 22), host.State.Time);
            host.Frames(follower, 4 * 200);
            var at = (follower.Status, follower.StopReason, host.State.CommittedCell);
            // Steamwoven follows a corridor past the budget; Original stops where it stands.
            if (profile.FarClickDoesNotMove) Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_BUDGET_EXHAUSTED", new CellCoord(5, 18)), at, $"{at}");
            else Assert.AreEqual((RouteFollowStatus.Arrived, "ROUTE_ARRIVED", new CellCoord(5, 22)), at, $"{at}");
        }
    }

    [TestMethod]
    public void A_short_detour_past_the_budget_is_walked_by_the_corridor_under_Steamwoven()
    {
        // The walk's budget 13 drops the 14-step detour through the gap at x 12; the corridor's single leg asks the whole budget.
        var source = new GridSource(Enumerable.Range(0, 5).Select(y => y == 2 ? new string('#', 12) + "." + new string('#', 7) : new string('.', 20)).ToArray());
        Assert.AreEqual("ROUTE_BUDGET_EXHAUSTED", RoutePlanner.Plan(source, new(5, 1), new(5, 3), new RouteLimits(budget: RouteLimits.WalkBudget(new(5, 1), new(5, 3)))).ReasonCode);
        foreach (var profile in NavigationRuleProfile.All)
        {
            var host = new RouteHost(source, new(5, 1), Step);
            var follower = new RouteFollower(source, corridorLimits: new(), corridorAllowed: () => !profile.FarClickDoesNotMove, straightRetry: true);
            follower.SetTarget(new(5, 3), host.State.Time);
            host.Frames(follower, 4 * 60);
            var at = (follower.Status, follower.StopReason, host.State.CommittedCell);
            // Original has no corridor: the PC's straight line meets the wall.
            if (profile.FarClickDoesNotMove) Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED", new CellCoord(5, 1)), at, $"{at}");
            else Assert.AreEqual((RouteFollowStatus.Arrived, "ROUTE_ARRIVED", new CellCoord(5, 3)), at, $"{at}");
        }
    }

    [TestMethod]
    public void The_pcs_straight_retry_goes_on_in_straight_lines_until_a_step_is_refused()
    {
        // No path (a wall without a gap): the retry heads straight for the goal and is refused at the wall (LR4: every later plan is
        // straight too).
        var source = new GridSource(Enumerable.Range(0, 40).Select(y => y == 20 ? new string('#', 40) : new string('.', 40)).ToArray());
        var host = new RouteHost(source, new(5, 8), Step);
        var follower = new RouteFollower(source, straightRetry: true);
        follower.SetTarget(new(5, 22), host.State.Time);
        host.Frames(follower, 4 * 100);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED", new CellCoord(5, 19)), (follower.Status, follower.StopReason, host.State.CommittedCell));
        Assert.AreEqual(RouteMethod.StraightRetry, follower.LastPlan!.Method);
        Assert.IsTrue(follower.PlannedSegments >= 2, "eleven straight cells: two segments of at most eight");
        // Without the retry (a companion's follow) the walker does not move.
        var still = new RouteHost(source, new(5, 8), Step);
        var plain = new RouteFollower(source);
        plain.SetTarget(new(5, 22), still.State.Time);
        still.Frames(plain, 4 * 10);
        Assert.AreEqual((RouteFollowStatus.Stopped, new CellCoord(5, 8)), (plain.Status, still.State.CommittedCell));
    }
}

/// <summary>ARC-WORLD-073: living critters outside combat mode (the owner's decision 2026-10-21: NPCs do not block the way; ledger
/// LR2, LR5, DC2).</summary>
[TestClass]
public sealed class RouteCritterTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    /// <summary>The movement's admission until ARC-WORLD-074: the plan's source, but a critter's cell refuses the step onto it.</summary>
    private sealed class Admission(GridSource source) : IRouteStepSource
    {
        public bool Contains(CellCoord cell) => source.Contains(cell);
        public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction) =>
            source.Critters.Contains(RoutePlanner.Neighbor(from, direction)) ? RouteStepVerdict.Blocked : source.Assess(from, direction);
    }

    [TestMethod]
    public void The_segment_end_stop_ends_an_Original_walk_and_Steamwoven_walks_on()
    {
        // A straight walk east: the critter stands on the eighth cell, the first segment's last step.
        foreach (var original in new[] { true, false })
        {
            var source = GridSource.Open(20, 3);
            source.Critters.Add(new(8, 1));
            var host = new RouteHost(new Admission(source), new(0, 1), Step);
            var follower = new RouteFollower(source, segmentEndCritterEndsWalk: () => original, replanOnDenial: true);
            follower.SetTarget(new(15, 1), host.State.Time);
            host.Frames(follower, 4 * 60);
            var at = (follower.Status, follower.StopReason, host.State.CommittedCell);
            if (original) Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_SEGMENT_END_CRITTER", new CellCoord(7, 1)), at, $"{at}");
            // Steamwoven: no stop; until the walker steps through critters the refused step plans around the critter's cell.
            else
            {
                Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(15, 1)), (at.Status, at.CommittedCell), $"{at}");
                Assert.IsFalse(host.Committed().Contains(new(8, 1)));
            }
        }
    }

    [TestMethod]
    public void A_critter_on_the_way_is_planned_through_and_one_on_the_goal_refuses_it()
    {
        var source = GridSource.Open(20, 3);
        source.Critters.Add(new(5, 1));
        // The plan passes the critter (its steps are the source's), the goal holding one is refused at the gate.
        var plan = RoutePlanner.Plan(source, new(0, 1), new(10, 1));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.Direct), (plan.Outcome, plan.Method));
        Assert.IsTrue(plan.Cells.Contains(new(5, 1)), "outside combat mode the plan goes through the critter's cell");
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_NOT_FOUND"), (RoutePlanner.Plan(source, new(0, 1), new(5, 1)) is var g ? (g.Outcome, g.ReasonCode) : default));
        // A refused step mid-segment: without re-planning the walk stops with a typed reason, never stalls.
        var host = new RouteHost(new Admission(source), new(0, 1), Step);
        var plain = new RouteFollower(source);
        plain.SetTarget(new(10, 1), host.State.Time);
        host.Frames(plain, 4 * 30);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED", new CellCoord(4, 1)), (plain.Status, plain.StopReason, host.State.CommittedCell));
        // With it, the walk goes around and arrives.
        var around = new RouteHost(new Admission(source), new(0, 1), Step);
        var replanning = new RouteFollower(source, replanOnDenial: true);
        replanning.SetTarget(new(10, 1), around.State.Time);
        around.Frames(replanning, 4 * 40);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(10, 1)), (replanning.Status, around.State.CommittedCell));
    }

    [TestMethod]
    public void A_walk_enclosed_by_refused_steps_ends_after_its_replans()
    {
        // Critters fill the column x = 5: every way round is refused in turn; the walk ends with a typed reason.
        var source = GridSource.Open(20, 3);
        for (var y = 0; y < 3; y++) source.Critters.Add(new(5, y));
        var host = new RouteHost(new Admission(source), new(0, 1), Step);
        var follower = new RouteFollower(source, replanOnDenial: true);
        follower.SetTarget(new(10, 1), host.State.Time);
        host.Frames(follower, 4 * 80);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_NOT_FOUND"), (follower.Status, follower.StopReason), $"{follower.StopReason}");
        Assert.AreEqual(4, host.State.CommittedCell.X);
    }
}
