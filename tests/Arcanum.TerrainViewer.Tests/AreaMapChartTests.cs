using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-017 whole-map chart and start over the public strict owner fixture
/// (core sectors 1452..1453 × 1290..1291, a blocking body at base-sector cell (5,1)).</summary>
[TestClass]
public sealed class AreaMapChartTests
{
    private const long X0 = 1452 * 64, Y0 = 1290 * 64;

    [TestMethod]
    public void The_whole_map_chart_starts_at_the_first_core_sector_and_covers_every_core_sector()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var snapshot = source.Owner.LoadStrict();
        var chart = AreaMapLoadOperation.Chart(snapshot);
        Assert.AreEqual((X0, Y0, 0, 127), (chart.OriginX, chart.OriginY, chart.MinimumCell, chart.MaximumCell));
        Assert.IsTrue(chart.Contains(new(0, 0)) && chart.Contains(new(127, 127)));
        Assert.IsFalse(chart.Contains(new(-1, 0)) || chart.Contains(new(0, 128)) || chart.Contains(new(128, 5)));
        // The exterior and cave charts keep the nine-sector window around the base sector, and `with` keeps a range.
        Assert.AreEqual((-64, 127), (AreaChart.Exterior.MinimumCell, AreaChart.Exterior.MaximumCell));
        Assert.IsTrue(AreaChart.Exterior.Contains(new(-64, 127)) && !AreaChart.Exterior.Contains(new(-65, 0)));
        Assert.AreEqual(127, (chart with { Spawn = new(3, 4) }).MaximumCell);
        // A map wider than 32 sectors is refused with a typed code, before any chart arithmetic.
        Assert.AreEqual(32 * 64 - 1, AreaMapLoadOperation.Chart([new(1452, 1290), new(1452 + 31, 1290)], 1).MaximumCell);
        Assert.AreEqual("AREA_MAP_CHART_TOO_LARGE", Assert.ThrowsExactly<InvalidOperationException>(
            () => AreaMapLoadOperation.Chart([new(1452, 1290), new(1452, 1290 + 32)], 1)).Message);
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaMapLoadOperation.Chart([], 1));
        // A session on that chart accepts a start anywhere in it and refuses one outside.
        var steps = AreaOwnerFixture.Steps(snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 3);
        _ = new AreaGridSession(steps, SimulationTime.At(0), chart: chart, start: new(70, 70));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaGridSession(steps, SimulationTime.At(0), chart: chart, start: new(-1, 1)));
    }

    [TestMethod]
    public void The_start_is_the_arrival_tile_when_it_can_step_and_otherwise_the_first_open_core_tile()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var snapshot = source.Owner.LoadStrict();
        var steps = AreaOwnerFixture.Steps(snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 3);
        Assert.AreEqual(new AreaStepCell(X0 + 6, Y0 + 1), AreaMapLoadOperation.Spawn(steps, new WorldLocation(X0 + 6, Y0 + 1), snapshot));
        // The blocker's own tile, no arrival, or an arrival off the map: the first core tile by row, then column.
        var first = AreaMapLoadOperation.Spawn(steps, null, snapshot);
        Assert.AreEqual(first, AreaMapLoadOperation.Spawn(steps, new WorldLocation(X0 + 5, Y0 + 1), snapshot));
        Assert.AreEqual(first, AreaMapLoadOperation.Spawn(steps, new WorldLocation(1, 1), snapshot));
        Assert.AreEqual(new AreaStepCell(X0, Y0), first);
    }

    [TestMethod]
    public void A_sector_asked_stands_the_actor_on_the_open_core_tile_nearest_its_middle()
    {
        // ARC-RENDER-024: with a sector asked, the start tile is skipped; the arrival of a transfer still wins.
        using var source = new AreaOwnerFixture(motionBodies: true);
        var snapshot = source.Owner.LoadStrict();
        var steps = AreaOwnerFixture.Steps(snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 3);
        var near = AreaMapLoadOperation.Spawn(steps, new WorldLocation(X0 + 6, Y0 + 1), snapshot, near: new AreaSectorCoordinate(1453, 1291));
        Assert.AreEqual(new AreaStepCell(X0 + 64 + 32, Y0 + 64 + 32), near);
        Assert.AreEqual(new AreaStepCell(X0 + 6, Y0 + 1), AreaMapLoadOperation.Spawn(steps, null, snapshot, new WorldLocation(X0 + 6, Y0 + 1), new AreaSectorCoordinate(1453, 1291)));
        // A sector outside the core: the core tile nearest its middle.
        Assert.AreEqual(new AreaStepCell(X0 + 127, Y0 + 127), AreaMapLoadOperation.Spawn(steps, null, snapshot, near: new AreaSectorCoordinate(1460, 1300)));
    }
}
