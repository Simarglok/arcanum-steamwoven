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
    public void AWindowStepEndsTheStraightAttemptAndCostsFiftyInTheSearch()
    {
        // ARC-WORLD-021: the step from (3,y) east is a window jump, Open with extra cost 50.
        static Func<CellCoord, NeighborDirection, int> Window(int y) => (c, d) => c == new CellCoord(3, y) && d == XPlus ? 50 : 0;
        // A one-row corridor: the window is the only way, so the search takes it.
        var corridor = new GridSource("........") { Extra = Window(0) };
        var through = RoutePlanner.Plan(corridor, new(0, 0), new(7, 0));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.LocalSearch, 7), (through.Outcome, through.Method, through.Steps.Count));
        // Open rows around it: a step around the window is far cheaper than +50, so the route avoids the jump.
        var open = new GridSource("........", "........", "........") { Extra = Window(1) };
        var around = RoutePlanner.Plan(open, new(0, 1), new(7, 1));
        Assert.AreEqual(RouteMethod.LocalSearch, around.Method);
        var cells = new[] { around.From }.Concat(around.Cells).ToArray();
        Assert.IsFalse(cells.Zip(cells.Skip(1)).Any(p => p.First == new CellCoord(3, 1) && p.Second == new CellCoord(4, 1)), "the window step is avoided");
        AllOpen(open, around);
        Assert.AreEqual(RouteMethod.Direct, RoutePlanner.Plan(GridSource.Open(8, 3), new(0, 1), new(7, 1)).Method);
        Assert.ThrowsExactly<InvalidOperationException>(() => RoutePlanner.Plan(new GridSource("....") { Extra = (_, _) => -1 }, new(0, 0), new(3, 0)));
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
        Assert.AreEqual("ROUTE_TARGET_OUT_OF_RANGE", RoutePlanner.Plan(GridSource.Open(3, 40), new(0, 0), new(0, 33)).ReasonCode);
        Assert.AreEqual("ROUTE_SOURCE_OUTSIDE_AREA", RoutePlanner.Plan(source, new(-1, 0), new(0, 0)).ReasonCode);
        var same = RoutePlanner.Plan(source, new(2, 2), new(2, 2));
        Assert.AreEqual((RouteOutcome.AlreadyAtTarget, 0, 0), (same.Outcome, same.Steps.Count, same.Segment.Count));
        source.Asked.Clear();
        var refused = RoutePlanner.Plan(source, new(0, 0), new(39, 0));
        Assert.AreEqual((RouteOutcome.Refused, 0, 0), (refused.Outcome, refused.Assessments, source.Asked.Count));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(segmentSteps: 9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(axisDistance: 33));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(budget: RouteLimits.BudgetMaximum + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RouteLimits(budget: 0));
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
        var occupied = RoutePlanner.Plan(walled, new(0, 0), new(1, 1));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_NOT_FOUND", 0, 8), (occupied.Outcome, occupied.ReasonCode, occupied.Expansions, occupied.Assessments),
            "A goal no step can enter is answered from its eight entries.");
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
    public void SearchStaysInsideItsWindowAndBudget()
    {
        // The only gap in the wall lies 40 cells east, outside the 64-cell window around source and target.
        var rows = Enumerable.Range(0, 5).Select(y => new string('.', 100)).ToArray();
        rows[2] = new string('#', 90) + new string('.', 10);
        var source = new GridSource(rows) { OriginX = -50 };
        var far = RoutePlanner.Plan(source, new(0, 0), new(0, 4));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_WINDOW_EXHAUSTED"), (far.Outcome, far.ReasonCode), "A route may exist outside the window.");
        Assert.IsTrue(source.Asked.Keys.All(k => k.Item1.X >= -32 && k.Item1.X < 32), "No cell outside the window is expanded.");
        Assert.IsTrue(far.Expansions <= OriginalPathRequest.Window * OriginalPathRequest.Window);
        // ARC-WORLD-073 (ledger DC4, LR2): a tile whose estimate over 10 exceeds the budget is dropped, so a detour longer than the
        // budget fails; the walk's budget for these 4 cells, min(4 × 4 + 5, 200) = 21, finds the six-step detour.
        var walled = new GridSource(".......", "...#...", "...#...", "...#...", ".......");
        var budget = RoutePlanner.Plan(walled, new(1, 2), new(5, 2), new RouteLimits(budget: 4));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_BUDGET_EXHAUSTED"), (budget.Outcome, budget.ReasonCode));
        Assert.AreEqual(21, RouteLimits.WalkBudget(new(1, 2), new(5, 2)));
        Assert.AreEqual((RouteOutcome.Found, 6), (RoutePlanner.Plan(walled, new(1, 2), new(5, 2), new RouteLimits(budget: 21)) is var walk ? (walk.Outcome, walk.Steps.Count) : default));
        Assert.AreEqual(200, RouteLimits.WalkBudget(new(0, 0), new(60, 0)));
    }

    [TestMethod]
    public void WindowIsClippedToTheCoordinateRange()
    {
        // All open except a wall at x = Max-2, y in [-2,2]; the detour stays below int.MaxValue.
        var max = int.MaxValue;
        var source = new GridSource(Enumerable.Range(0, 9).Select(y => y is >= 2 and <= 6 ? "..#." : "....").ToArray()) { OriginX = max - 3, OriginY = -4 };
        var plan = RoutePlanner.Plan(source, new(max - 3, 0), new(max, 0));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.LocalSearch), (plan.Outcome, plan.Method));
        Assert.IsTrue(plan.Cells.All(c => c.X <= max));
    }

    [TestMethod]
    public void UnsupportedSourceVerdictsAreRejectedNotGuessed()
    {
        var source = new GridSource("....") { Override = (_, _) => (RouteStepVerdict)42 };
        Assert.ThrowsExactly<InvalidOperationException>(() => RoutePlanner.Plan(source, new(0, 0), new(3, 0)));
        Assert.ThrowsExactly<ArgumentNullException>(() => RoutePlanner.Plan(null!, new(0, 0), new(3, 0)));
    }
}
