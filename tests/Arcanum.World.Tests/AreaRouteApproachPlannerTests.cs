using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

[TestClass]
public sealed class AreaRouteApproachPlannerTests
{
    [TestMethod]
    public void SelectsTheCheapestReachableApproachInsteadOfTheNearestDisconnectedOrLongDetourCell()
    {
        var actor = new CellCoord(0, 0); var disconnected = new CellCoord(2, 0); var detour = new CellCoord(4, 0); var shortRoute = new CellCoord(3, 1);
        var source = new EdgeSource();
        source.OpenPath([actor, new(0, 1), new(0, 2), new(1, 2), new(2, 2), new(3, 2), new(4, 1), detour]);
        source.OpenPath([actor, new(1, -1), new(2, -1), new(3, 0), shortRoute]);
        source.OpenPath([new(2, 1), disconnected]); // Locally enterable, but in a separate component from the actor.

        var selected = AreaRouteApproachPlanner.Select(source, actor, [disconnected, detour, shortRoute], allowCorridor: false);

        Assert.IsNotNull(selected);
        Assert.AreEqual(shortRoute, selected.Cell);
        Assert.AreEqual(4, selected.Steps.Count);
        Assert.IsFalse(selected.UsesCorridor);
        AssertSafePath(source, actor, selected);
    }

    [TestMethod]
    public void ReturnsNoApproachWhenTheOnlyLocallyEnterableCellCannotBeReachedFromTheActor()
    {
        var actor = new CellCoord(0, 0); var candidate = new CellCoord(2, 0);
        var source = new EdgeSource();
        source.OpenPath([new(2, 1), candidate]);
        source.Include(actor);

        Assert.IsNull(AreaRouteApproachPlanner.Select(source, actor, [candidate], allowCorridor: true));
    }

    [TestMethod]
    public void UsesOnlyACompleteOpenCorridorWhenTheLocalRequestCannotReachTheCandidate()
    {
        var cells = Enumerable.Range(0, 41).Select(x => new CellCoord(x, 0)).ToArray();
        var source = new EdgeSource(); source.OpenPath(cells);
        var actor = cells[0]; var candidate = cells[^1];

        Assert.IsNull(AreaRouteApproachPlanner.Select(source, actor, [candidate], allowCorridor: false));
        var selected = AreaRouteApproachPlanner.Select(source, actor, [candidate], allowCorridor: true);

        Assert.IsNotNull(selected);
        Assert.AreEqual(candidate, selected.Cell);
        Assert.AreEqual(40, selected.Steps.Count);
        Assert.IsTrue(selected.UsesCorridor);
        AssertSafePath(source, actor, selected);
    }

    private static void AssertSafePath(EdgeSource source, CellCoord start, AreaRouteApproach route)
    {
        var cell = start;
        foreach (var direction in route.Steps)
        {
            Assert.AreEqual(RouteStepVerdict.Open, source.Assess(cell, direction), $"{cell} {direction}");
            cell = RoutePlanner.Neighbor(cell, direction);
        }
        Assert.AreEqual(route.Cell, cell);
    }

    private sealed class EdgeSource : IRouteStepSource
    {
        private readonly HashSet<CellCoord> _cells = [];
        private readonly HashSet<(CellCoord From, NeighborDirection Direction)> _open = [];

        public bool Contains(CellCoord cell) => _cells.Contains(cell);
        public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction) =>
            _open.Contains((from, direction)) ? RouteStepVerdict.Open : RouteStepVerdict.Blocked;

        public void Include(CellCoord cell) => _cells.Add(cell);

        public void OpenPath(IEnumerable<CellCoord> cells)
        {
            var path = cells.ToArray();
            foreach (var cell in path) _cells.Add(cell);
            for (var index = 1; index < path.Length; index++)
            {
                var from = path[index - 1]; var to = path[index];
                var direction = Enum.GetValues<NeighborDirection>().Single(item => RoutePlanner.Neighbor(from, item) == to);
                var opposite = (NeighborDirection)(((int)direction + 4) & 7);
                _open.Add((from, direction)); _open.Add((to, opposite));
            }
        }
    }
}
