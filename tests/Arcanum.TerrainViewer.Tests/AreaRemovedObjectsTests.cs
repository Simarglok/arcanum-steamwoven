using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-065: an object the session destroyed or switched off no longer blocks a step through the live overlay
/// (invented sector (0,0), open terrain, a blocking container on (11,10)).</summary>
[TestClass]
public sealed class AreaRemovedObjectsTests
{
    private static readonly AreaObjectKey Chest = new("s", 1, AreaSourceKind.Static, 0, 0, 1, "chest", 1);
    private static readonly AreaObjectKey Companion = new("s", 1, AreaSourceKind.Mobile, 0, 0, 2, "npc", 1);

    private static AreaStepSnapshot Steps()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>
        {
            [new(11, 10)] = [new(Chest, new(11, 10), 2, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "TEST")],
        };
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain, objects, new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    [TestMethod]
    public void ARemovedObjectBlocksNoStepOntoItsTileOrPastItsShoulder()
    {
        var steps = Steps();
        var onto = steps.Assess(steps.Request(new(10, 10), new(11, 10), CardinalTraversalDirection.XPlus));
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", onto.ReasonCode);
        var removed = new HashSet<AreaObjectKey> { Chest };
        var overlay = AreaOccupancy.None.WithRemoved(removed);
        Assert.AreEqual((CardinalTraversalCoverage.Supported, (CardinalTraversalDisposition?)CardinalTraversalDisposition.Open),
            (AreaOccupancyRules.Apply(onto, new(10, 10), NeighborDirection.XPlus, overlay).Coverage, AreaOccupancyRules.Apply(onto, new(10, 10), NeighborDirection.XPlus, overlay).Disposition));
        // A diagonal past it: (10,11) to (11,10) has the chest on its destination; (10,9) to (11,10) too.
        var diagonal = steps.Assess(steps.Request(new(10, 11), new(11, 10), GridReplay.MapDirection(NeighborDirection.XPlusYMinus)));
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, diagonal.Disposition);
        Assert.AreEqual(CardinalTraversalDisposition.Open, AreaOccupancyRules.Apply(diagonal, new(10, 11), NeighborDirection.XPlusYMinus, overlay).Disposition);
        // A standing chest still blocks under a live overlay that does not move it (a companion elsewhere).
        var companionOnly = new AreaOccupancy([Companion], [new CellCoord(30, 30)]);
        Assert.AreEqual(("DESTINATION_OBJECT_BLOCKER", (CardinalTraversalDisposition?)CardinalTraversalDisposition.Blocked),
            (AreaOccupancyRules.Apply(onto, new(10, 10), NeighborDirection.XPlus, companionOnly).ReasonCode,
             AreaOccupancyRules.Apply(onto, new(10, 10), NeighborDirection.XPlus, companionOnly).Disposition));
        Assert.AreEqual(CardinalTraversalDisposition.Open, AreaOccupancyRules.Apply(onto, new(10, 10), NeighborDirection.XPlus, companionOnly.WithRemoved(removed)).Disposition);
    }

    [TestMethod]
    public void WithRemovedKeepsTheActorsAndReturnsItselfWhenNothingIsNew()
    {
        var live = new AreaOccupancy([Companion], [new CellCoord(5, 5)]);
        Assert.AreSame(live, live.WithRemoved(new HashSet<AreaObjectKey>()));
        Assert.AreSame(live, live.WithRemoved(new HashSet<AreaObjectKey> { Companion }));
        var merged = live.WithRemoved(new HashSet<AreaObjectKey> { Chest });
        CollectionAssert.AreEquivalent(new[] { Companion, Chest }, merged.Moved.ToArray());
        CollectionAssert.AreEquivalent(new[] { new CellCoord(5, 5) }, merged.Occupied.ToArray());
    }

    [TestMethod]
    public void ARemovedObjectNoLongerBlocksAFarRoute()
    {
        // ARC-WORLD-060: a wall of blocking objects across the sector at x 20, its only removable piece the chest on (20,30); a goal 40
        // cells away, beyond the local planner's reach, so the click route follows a corridor over the same live overlay the PC steps by.
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>();
        for (var y = 0; y < 64; y++)
        {
            var key = y == 30 ? Chest : new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 100 + y, "wall", 1);
            objects[new(20, y)] = [new(key, new(20, y), 2, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "TEST")];
        }
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain, objects,
            new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var chart = new AreaChart(0, 0, default, "TEST") { MinimumCell = 0, MaximumCell = 63 };
        CellCoord from = new(0, 30), goal = new(40, 30);
        AreaOccupancy? overlay = null;
        var source = new AreaRouteSource(steps, chart, () => overlay);
        var standing = Arcanum.Simulation.Navigation.RouteCorridor.Plan(source, from, goal);
        Assert.AreEqual((Arcanum.Simulation.Navigation.RouteCorridorOutcome.Partial, "ROUTE_CORRIDOR_PARTIAL"), (standing.Outcome, standing.ReasonCode));
        Assert.AreEqual(19, standing.End!.Value.X);
        overlay = AreaOccupancy.None.WithRemoved(new HashSet<AreaObjectKey> { Chest });
        var freed = Arcanum.Simulation.Navigation.RouteCorridor.Plan(source, from, goal);
        Assert.AreEqual((Arcanum.Simulation.Navigation.RouteCorridorOutcome.Found, (CellCoord?)goal), (freed.Outcome, freed.End));
        Assert.AreEqual(40, freed.Cells); // straight through the chest's tile
    }

    [TestMethod]
    public void TheWalkRouteVerdictMovesRemovedObjectsOnly()
    {
        // ARC-WORLD-060: the window route's verdict over the PC's overlay: a removed blocker opens, a standing one and terrain stay
        // blocked; an overlay that moves nothing gives the T0 disposition (null); the direction map inverts GridReplay.MapDirection.
        foreach (var direction in Enum.GetValues<NeighborDirection>())
            Assert.AreEqual(direction, AreaMapWalk.Neighbor[GridReplay.MapDirection(direction)]);
        Assert.IsNull(AreaMapWalk.StepOpen(null));
        Assert.IsNull(AreaMapWalk.StepOpen(new AreaOccupancy([], [new CellCoord(11, 10)])));
        var steps = Steps();
        var onto = steps.Assess(steps.Request(new(10, 10), new(11, 10), CardinalTraversalDirection.XPlus));
        var open = AreaMapWalk.StepOpen(AreaOccupancy.None.WithRemoved(new HashSet<AreaObjectKey> { Chest }))!;
        Assert.IsTrue(open(onto, new(10, 10), CardinalTraversalDirection.XPlus));
        var other = AreaMapWalk.StepOpen(AreaOccupancy.None.WithRemoved(new HashSet<AreaObjectKey> { Companion }))!;
        Assert.IsFalse(other(onto, new(10, 10), CardinalTraversalDirection.XPlus));
        // A step off the admitted sector stays refused whatever the overlay moves.
        var off = steps.Assess(steps.Request(new(63, 10), new(64, 10), CardinalTraversalDirection.XPlus));
        Assert.IsFalse(open(off, new(63, 10), CardinalTraversalDirection.XPlus));
    }
}
