using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-009 over the public strict synthetic AREA fixture (no endpoints): the coordinator's own guards and a
// second chart's effect on the scene plan and the route source. Endpoint acceptance is the RealData gate.
[TestClass]
public sealed class AreaTransitionCoordinatorTests
{
    private static readonly AreaChart Shifted = new(1453 * 64, 1290 * 64, new(-58, 1), "TEST_SECOND_SECTOR_CHART");

    private static (AreaSnapshot Snapshot, AreaStepSnapshot Steps) Load()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var snapshot = source.Owner.LoadStrict();
        return (snapshot, AreaOwnerFixture.Steps(snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7));
    }

    [TestMethod]
    public void TheCoordinatorRefusesDuplicateMapsAndAnUnknownCurrentMap()
    {
        var (snapshot, steps) = Load();
        var map = new AreaMapRuntime(snapshot, steps, AreaChart.Exterior);
        Assert.AreEqual("AREA_TRANSITION_MAPS_INVALID", Assert.ThrowsExactly<ArgumentException>(() => new AreaTransitionCoordinator([map, map], map.RuntimeMap)).Message);
        Assert.AreEqual("AREA_TRANSITION_CURRENT_MAP_REQUIRED", Assert.ThrowsExactly<ArgumentException>(() => new AreaTransitionCoordinator([map], map.RuntimeMap + 1)).Message);
    }

    [TestMethod]
    public void AnObjectWithoutAnEndpointIsNotATriggerAndNothingIsQueued()
    {
        var (snapshot, steps) = Load();
        var transitions = new AreaTransitionCoordinator([new AreaMapRuntime(snapshot, steps, AreaChart.Exterior)], snapshot.RuntimeMap);
        Assert.AreEqual(0, transitions.Triggers().Count, "The fixture publishes no endpoint.");
        var blocker = snapshot.Objects.First(o => o.WorldX is not null);
        var result = transitions.RequestUse(blocker.Key, new(blocker.WorldX!.Value - 1, blocker.WorldY!.Value));
        Assert.AreEqual((AreaUseOutcome.NotATrigger, (AreaEndpoint?)null, "NO_ENDPOINT_FOR_OBJECT"), (result.Outcome, result.Endpoint, result.Reason));
        Assert.IsNull(transitions.Pending);
        Assert.IsNull(transitions.Process(), "Nothing pending, nothing applied.");
        Assert.IsNull(transitions.Cancel(), "Nothing pending, nothing dropped.");
        Assert.IsNull(transitions.Complete(), "ARC-DEV-113: nothing pending, nothing opened.");
        Assert.AreEqual(snapshot.RuntimeMap, transitions.CurrentMap);
    }

    [TestMethod]
    public void ASecondChartShiftsEveryDrawCommandByItsOriginAndKeepsTheirOrder()
    {
        var (snapshot, _) = Load();
        var exterior = AreaScenePlan.Create(snapshot);
        var shifted = AreaScenePlan.Create(snapshot, Shifted);
        Assert.AreSame(Shifted, shifted.Chart);
        Assert.AreSame(AreaChart.Exterior, exterior.Chart);
        var world = new AreaStepCell(92928 + 6, 82561);
        var a = AreaCellPicking.Center(AreaChart.Exterior.ToChart(world)); var b = AreaCellPicking.Center(Shifted.ToChart(world));
        var (dx, dy) = ((long)(b.X - a.X), (long)(b.Y - a.Y));
        Assert.AreNotEqual((0L, 0L), (dx, dy));
        Assert.AreEqual(exterior.Commands.Count, shifted.Commands.Count);
        foreach (var (e, s) in exterior.Commands.Zip(shifted.Commands))
        {
            Assert.AreEqual((e.Identity, e.Frame, e.Phase), (s.Identity, s.Frame, s.Phase));
            Assert.AreEqual((e.Destination.X + dx, e.Destination.Y + dy, e.Destination.Width, e.Destination.Height),
                ((long)s.Destination.X, (long)s.Destination.Y, s.Destination.Width, s.Destination.Height));
        }
    }

    [TestMethod]
    public void ARouteSourceOnASecondChartAssessesTheSameWorldSteps()
    {
        var (_, steps) = Load();
        var exterior = new AreaRouteSource(steps); var shifted = new AreaRouteSource(steps, Shifted);
        Assert.AreSame(Shifted, shifted.Chart);
        for (var x = 92928L; x < 92928 + 12; x++)
        for (var y = 82560L; y < 82560 + 6; y++)
        {
            var world = new AreaStepCell(x, y);
            CellCoord e = AreaChart.Exterior.ToChart(world), s = Shifted.ToChart(world);
            Assert.AreEqual(exterior.Contains(e), shifted.Contains(s), $"admission at {world}");
            foreach (var direction in Enum.GetValues<NeighborDirection>())
                Assert.AreEqual(exterior.Assess(e, direction), shifted.Assess(s, direction), $"{direction} from {world}");
        }
    }
}
