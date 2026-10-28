using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-WORLD-073: the attack's approach (ledger AP1–AP4): a route to the target's own tile with the request's flag 0x1, one
/// step short of it.</summary>
[TestClass]
public sealed class RouteApproachTests
{
    [TestMethod]
    public void The_approach_routes_into_the_targets_tile_and_ends_one_step_short()
    {
        // The target stands on a tile no step may enter ('#'): the flag skips the gate and takes the last step untested.
        var source = new GridSource("..........", "..........", "........#.", "..........");
        var plan = RoutePlanner.PlanApproach(source, new(0, 2), new(8, 2));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.Direct, "ROUTE_APPROACH", new CellCoord(7, 2), 7),
            (plan.Outcome, plan.Method, plan.ReasonCode, plan.Target, plan.Steps.Count));
        Assert.AreEqual("ROUTE_NOT_FOUND", RoutePlanner.Plan(source, new(0, 2), new(8, 2)).ReasonCode, "the ground walk's gate refuses the tile");
        // Next to the target there is no route of a step (the original then ends the attack); beyond 32 tiles no route either.
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_APPROACH_ADJACENT"), (RoutePlanner.PlanApproach(source, new(7, 2), new(8, 2)) is var a ? (a.Outcome, a.ReasonCode) : default));
        Assert.AreEqual("ROUTE_TARGET_OUT_OF_RANGE", RoutePlanner.PlanApproach(GridSource.Open(40, 1), new(0, 0), new(33, 0)).ReasonCode);
        Assert.AreEqual("ROUTE_APPROACH_ON_TARGET", RoutePlanner.PlanApproach(source, new(8, 2), new(8, 2)).ReasonCode);
    }

    [TestMethod]
    public void A_blocked_line_is_searched_and_the_untested_last_step_may_cross_a_wall()
    {
        // A wall between walker and target with a gap at the top: the search goes round; the cell it ends on is next to the target.
        var source = new GridSource(
            "..........",
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#....");
        var around = RoutePlanner.PlanApproach(source, new(2, 3), new(8, 3));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.LocalSearch), (around.Outcome, around.Method));
        Assert.AreEqual(1, Math.Max(Math.Abs(around.Target.X - 8), Math.Abs(around.Target.Y - 3)), "one step short of the target");
        var cell = around.From;
        foreach (var step in around.Steps) { Assert.AreEqual(RouteStepVerdict.Open, source.Assess(cell, step)); cell = RoutePlanner.Neighbor(cell, step); }
        // A wall edge between x 5 and x 6 refuses every step onto the target's column from the west: the route still ends at (5, 3),
        // the uncounted last step crossing the edge (ledger AP2); the walker then stands next to a target it cannot step to.
        var edge = GridSource.Open(10, 5);
        edge.Override = (from, direction) => from.X == 5 && RoutePlanner.Neighbor(from, direction).X == 6 ? RouteStepVerdict.Blocked : null;
        var behind = RoutePlanner.PlanApproach(edge, new(2, 3), new(6, 3));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.Direct, new CellCoord(5, 3)), (behind.Outcome, behind.Method, behind.Target));
    }
}
