using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-042: a route goal carried across a window move: kept when the new window holds it, else approached
/// through the window's cell nearest it and handed on; nothing when no cell is nearer.</summary>
[TestClass]
public sealed class AreaMapGoalHandOffTests
{
    // A 3×3-sector window chart (0..191) at world origin (6400, 6400).
    private static readonly AreaChart Chart = new(6400, 6400, default, "TEST_WINDOW") { MinimumCell = 0, MaximumCell = 191 };

    [TestMethod]
    public void A_goal_the_window_holds_is_followed_as_it_is()
    {
        var resume = AreaMapGoalHandOff.Resume(Open(), Chart, new(10, 10), new(6400 + 150, 6400 + 20));
        Assert.AreEqual(new AreaMapGoalResume(new(150, 20), null), resume);
    }

    [TestMethod]
    public void A_goal_beyond_the_window_is_approached_through_its_nearest_cell_and_handed_on()
    {
        // East of the window on row 20: the nearest reachable cell is on the window's east edge, on that row.
        AreaStepCell goal = new(6400 + 300, 6400 + 20);
        var resume = AreaMapGoalHandOff.Resume(Open(), Chart, new(10, 20), goal)!;
        Assert.AreEqual((new CellCoord(191, 20), (AreaStepCell?)goal), (resume.Target, resume.FarGoal));
        // A wall across the window at x = 100 leaves the actor's side only: the nearest cell is on the wall's west face.
        var walled = AreaMapGoalHandOff.Resume(Open(wallX: 100), Chart, new(10, 20), goal)!;
        Assert.AreEqual((99, (AreaStepCell?)goal), (walled.Target.X, walled.FarGoal));
    }

    [TestMethod]
    public void No_cell_nearer_a_goal_beyond_the_window_ends_the_walk()
    {
        // The actor stands in a one-cell pocket: no step leaves it.
        Assert.IsNull(AreaMapGoalHandOff.Resume(Open(pocket: new(10, 20)), Chart, new(10, 20), new(6400 + 300, 6400 + 20)));
    }

    private static Steps Open(int? wallX = null, CellCoord? pocket = null) => new(wallX, pocket);

    private sealed class Steps(int? wallX, CellCoord? pocket) : IRouteStepSource
    {
        public bool Contains(CellCoord cell) => cell.X is >= 0 and <= 191 && cell.Y is >= 0 and <= 191 && cell.X != wallX;
        public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction) =>
            from == pocket || RoutePlanner.Neighbor(from, direction) == pocket ? RouteStepVerdict.Blocked : RouteStepVerdict.Open;
    }
}
