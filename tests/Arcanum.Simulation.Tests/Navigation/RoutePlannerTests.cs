using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Movement.NeighborDirection;

namespace Arcanum.Simulation.Tests.Navigation;

[TestClass]
public sealed class RoutePlannerTests
{
    private static void AllOpen(GridSource source, RoutePlan plan)
    {
        var cell = plan.From;
        foreach (var step in plan.Steps)
        {
            Assert.AreEqual(RouteStepVerdict.Open, source.Assess(cell, step), $"{cell} {step}");
            cell = RoutePlanner.Neighbor(cell, step);
        }
        Assert.AreEqual(plan.Target, cell);
    }

    [TestMethod]
    public void StraightLineRuleIsDiagonalUnlessTheMinorDeltaIsUnderHalf()
    {
        var plan = RoutePlanner.Plan(GridSource.Open(8, 4), new(0, 0), new(5, 2));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.Direct, "ROUTE_DIRECT"), (plan.Outcome, plan.Method, plan.ReasonCode));
        CollectionAssert.AreEqual(new[] { XPlus, XPlusYPlus, XPlus, XPlusYPlus, XPlus }, plan.Steps.ToArray());
        CollectionAssert.AreEqual(new CellCoord[] { new(1, 0), new(2, 1), new(3, 1), new(4, 2), new(5, 2) }, plan.Cells.ToArray());
        Assert.AreEqual(YMinus, RoutePlanner.Toward(new(0, 0), new(1, -3)));
        Assert.AreEqual(XMinusYMinus, RoutePlanner.Toward(new(0, 0), new(-2, -3)));
        Assert.ThrowsExactly<ArgumentException>(() => RoutePlanner.Toward(new(1, 1), new(1, 1)));
    }

    [TestMethod]
    public void SegmentCapIsNotTheGoalLimit()
    {
        var plan = RoutePlanner.Plan(GridSource.Open(32, 1), new(0, 0), new(20, 0));
        Assert.AreEqual(20, plan.Steps.Count);
        Assert.AreEqual(8, plan.Segment.Count);
        CollectionAssert.AreEqual(plan.Steps.Take(8).ToArray(), plan.Segment.ToArray());
        var lowered = RoutePlanner.Plan(GridSource.Open(32, 1), new(0, 0), new(20, 0), new RouteLimits(segmentSteps: 3));
        Assert.AreEqual((20, 3), (lowered.Steps.Count, lowered.Segment.Count));
    }

    [TestMethod]
    public void WallFallsBackToTheLocalSearchAndUsesOnlyOpenSteps()
    {
        var source = new GridSource(
            ".......",
            "...#...",
            "...#...",
            "...#...",
            ".......");
        var plan = RoutePlanner.Plan(source, new(1, 2), new(5, 2));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.LocalSearch), (plan.Outcome, plan.Method));
        Assert.IsTrue(source.Asked.Values.All(n => n == 1), "Each directed step is assessed at most once per plan.");
        Assert.AreEqual(source.Asked.Count, plan.Assessments);
        AllOpen(source, plan);
        // Around the wall end: two diagonals cannot cut its shoulders, so six steps is the shortest open detour.
        Assert.AreEqual(6, plan.Steps.Count);
        Assert.IsTrue(plan.BlockedSteps > 0);
        var again = RoutePlanner.Plan(source, new(1, 2), new(5, 2));
        CollectionAssert.AreEqual(plan.Steps.ToArray(), again.Steps.ToArray(), "Deterministic.");
    }

    [TestMethod]
    public void RefusalsAreTypedAndNeverSearched()
    {
        var source = GridSource.Open(40, 3);
        Assert.AreEqual("ROUTE_TARGET_OUT_OF_RANGE", RoutePlanner.Plan(source, new(0, 0), new(33, 0)).ReasonCode);
        Assert.AreEqual(RouteOutcome.Found, RoutePlanner.Plan(source, new(0, 0), new(32, 0)).Outcome);
        Assert.AreEqual("ROUTE_TARGET_OUT_OF_RANGE", RoutePlanner.Plan(source, new(0, 0), new(8, 0), new RouteLimits(axisDistance: 7)).ReasonCode);
        Assert.AreEqual("ROUTE_TARGET_OUTSIDE_AREA", RoutePlanner.Plan(source, new(0, 0), new(0, 3)).ReasonCode);
        Assert.AreEqual("ROUTE_SOURCE_OUTSIDE_AREA", RoutePlanner.Plan(source, new(-1, 0), new(0, 0)).ReasonCode);
        var same = RoutePlanner.Plan(source, new(2, 2), new(2, 2));
        Assert.AreEqual((RouteOutcome.AlreadyAtTarget, 0, 0), (same.Outcome, same.Steps.Count, same.Segment.Count));
        source.Asked.Clear();
        var refused = RoutePlanner.Plan(source, new(0, 0), new(39, 0));
        Assert.AreEqual((RouteOutcome.Refused, 0, 0), (refused.Outcome, refused.Assessments, source.Asked.Count));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(segmentSteps: 9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(axisDistance: 33));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(expansions: 4097));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(segmentSteps: 0));
    }

    [TestMethod]
    public void EnclosedTargetIsNoRouteAndUnknownIsNeverOpen()
    {
        var walled = new GridSource(
            ".......",
            ".#####.",
            ".#...#.",
            ".#####.",
            ".......");
        var blocked = RoutePlanner.Plan(walled, new(0, 0), new(3, 2));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_NOT_FOUND", 0), (blocked.Outcome, blocked.ReasonCode, blocked.Steps.Count));
        Assert.IsTrue(blocked.BlockedSteps > 0 && blocked.UnknownSteps == 0);
        var unknown = new GridSource(
            ".......",
            ".?????.",
            ".?...?.",
            ".?????.",
            ".......");
        var gap = RoutePlanner.Plan(unknown, new(0, 0), new(3, 2));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_EVIDENCE_INCOMPLETE"), (gap.Outcome, gap.ReasonCode));
        Assert.IsTrue(gap.UnknownSteps > 0);
    }

    [TestMethod]
    public void SearchStaysInsideItsWindowAndExpansionBudget()
    {
        // The only gap in the wall lies 40 cells east, outside the 64-cell window around source and target.
        var rows = Enumerable.Range(0, 5).Select(y => new string('.', 100)).ToArray();
        rows[2] = new string('#', 90) + new string('.', 10);
        var source = new GridSource(rows) { OriginX = -50 };
        var far = RoutePlanner.Plan(source, new(0, 0), new(0, 4));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_NOT_FOUND"), (far.Outcome, far.ReasonCode));
        Assert.IsTrue(source.Asked.Keys.All(k => k.Item1.X >= -32 && k.Item1.X < 32), "No cell outside the window is expanded.");
        Assert.IsTrue(far.Expansions <= RouteLimits.ExpansionMaximum);
        var walled = new GridSource(".......", "...#...", "...#...", "...#...", ".......");
        var budget = RoutePlanner.Plan(walled, new(1, 2), new(5, 2), new RouteLimits(expansions: 2));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_EXPANSION_LIMIT", 2), (budget.Outcome, budget.ReasonCode, budget.Expansions));
    }

    [TestMethod]
    public void UnsupportedSourceVerdictsAreRejectedNotGuessed()
    {
        var source = new GridSource("....") { Override = (_, _) => (RouteStepVerdict)42 };
        Assert.ThrowsExactly<InvalidOperationException>(() => RoutePlanner.Plan(source, new(0, 0), new(3, 0)));
        Assert.ThrowsExactly<ArgumentNullException>(() => RoutePlanner.Plan(null!, new(0, 0), new(3, 0)));
    }
}
