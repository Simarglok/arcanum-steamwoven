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
