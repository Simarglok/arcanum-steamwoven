using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-RENDER-024 check (DAT_ROOT = the game folder): the main world map loads in map mode as a window
/// around its map-list start (the new-game start), around a sector asked or around a transfer arrival, with a drawable plan,
/// window step evidence and a spawn the walker can leave; and it is a transfer destination of the maps that lead to it.</summary>
[TestClass]
public sealed class AreaMapWindowOriginalTests
{
    private const string MainMap = "Arcanum1-024-fixed";

    [TestMethod, TestCategory("RealData")]
    public void The_main_map_opens_as_a_window_at_the_new_game_start()
    {
        var result = AreaLoadOperation.Execute(5, Arguments(Root(), null), default);
        var area = result.Area!;
        // The map-list start (92958, 82592) of row 5000 lies in the Crash Site sector, and the actor stands on it.
        Assert.AreEqual(new AreaSectorCoordinate(1452, 1290), result.AreaMapWindowCentre);
        Assert.AreEqual("arc-data-032-window-original-rules-v1", area.Snapshot.Profile);
        Assert.AreEqual((5000, 1), (area.Snapshot.CatalogueMap, area.Snapshot.RuntimeMap));
        Assert.AreEqual(9, area.Snapshot.Sectors.Count(s => s.Scope == AreaScope.Core && s.Present));
        Assert.AreEqual(new Arcanum.Data.Traversal.AreaStepCell(92958, 82592), area.Plan.Chart.ToWorld(area.Plan.Chart.Spawn));
        Assert.AreEqual((1451L * 64, 1289L * 64, 0, 191), (area.Plan.Chart.OriginX, area.Plan.Chart.OriginY, area.Plan.Chart.MinimumCell, area.Plan.Chart.MaximumCell));
        Assert.AreEqual(9 * 4096, result.AreaStepSnapshot!.AdmittedCells);
        Assert.IsTrue(area.Plan.DrawableBodies > 0 && area.Lighting is not null);
        // The map's transitions are read, and every listed map with a sector file is a destination, the main map included.
        Assert.IsNotNull(result.MapTransitions);
        Assert.IsTrue(result.MapDestinations!.ContainsKey(1) && result.MapDestinations.ContainsKey(79));
    }

    [TestMethod, TestCategory("RealData")]
    [DataRow(1409, 1314, true, "town")]
    [DataRow(1384, 239, true, "elven forest wilderness (generated)")]
    [DataRow(1450, 1290, false, "generated mountains beside the Crash Site (no open ground of its own)")]
    public void The_main_map_opens_as_a_window_at_a_sector_asked(int x, int y, bool open, string what)
    {
        var sector = new AreaSectorCoordinate(x, y);
        var result = AreaLoadOperation.Execute(5, Arguments(Root(), sector), default);
        var area = result.Area!;
        Assert.AreEqual(sector, result.AreaMapWindowCentre, what);
        var spawn = area.Plan.Chart.ToWorld(area.Plan.Chart.Spawn);
        Assert.IsTrue(result.AreaStepSnapshot!.IsAdmitted(spawn), what);
        // The actor stands in the asked sector when it has open ground, else on the open core tile nearest its middle.
        if (open) Assert.AreEqual((sector.X, sector.Y), (spawn.X >> 6, spawn.Y >> 6), what);
        else Assert.IsTrue(Math.Abs((spawn.X >> 6) - sector.X) <= 1 && Math.Abs((spawn.Y >> 6) - sector.Y) <= 1 && (spawn.X >> 6, spawn.Y >> 6) != (sector.X, sector.Y), what);
        _ = new AreaGridSession(result.AreaStepSnapshot, Arcanum.Simulation.Movement.SimulationTime.At(0), chart: area.Plan.Chart, start: area.Plan.Chart.Spawn);
        Assert.IsTrue(area.Plan.Commands.Count > 0, what);
    }

    [TestMethod, TestCategory("RealData")]
    public void A_transfer_to_the_main_map_opens_the_window_around_its_arrival()
    {
        // The Crash Site cave's exit arrives at (92913, 82598) on the main map (ARC-DATA-036 endpoint data).
        var arguments = Arguments(Root(), null) with { AreaMapArrival = new WorldLocation(92913, 82598) };
        var result = AreaLoadOperation.Execute(5, arguments, default);
        Assert.AreEqual(new AreaSectorCoordinate(1451, 1290), result.AreaMapWindowCentre);
        var chart = result.Area!.Plan.Chart;
        Assert.AreEqual(new Arcanum.Data.Traversal.AreaStepCell(92913, 82598), chart.ToWorld(chart.Spawn));
        // An arrival wins over an opening sector the arguments still carry (a window move or --area-map-sector).
        var stale = AreaLoadOperation.Execute(7, Arguments(Root(), new AreaSectorCoordinate(1409, 1314)) with { AreaMapArrival = new WorldLocation(92913, 82598) }, default);
        Assert.AreEqual(new AreaSectorCoordinate(1451, 1290), stale.AreaMapWindowCentre);
        Assert.AreEqual(new Arcanum.Data.Traversal.AreaStepCell(92913, 82598), stale.Area!.Plan.Chart.ToWorld(stale.Area.Plan.Chart.Spawn));
        // The cave lists the main map as a destination.
        var cave = AreaLoadOperation.Execute(6, Arguments(Root(), null, "crash site cave"), default);
        Assert.IsNull(cave.AreaMapWindowCentre);
        Assert.IsTrue(cave.MapDestinations!.ContainsKey(1));
        Assert.IsTrue(cave.MapTransitions!.Unconditional.Any(s => s.DestinationRuntime == 1 && s.Arrival == new WorldLocation(92913, 82598)));
    }

    [TestMethod, TestCategory("RealData")]
    public void A_transfer_from_a_caladon_interior_opens_the_main_map_window_around_its_arrival()
    {
        // ARC-DATA-038: the Panarii temple's exit arrives at (102647, 116816), by Caladon; that window holds scenery whose ART id
        // is 0xFFFFFFFF, which has no body (ledger NA1) instead of refusing the window.
        var temple = AreaLoadOperation.Execute(8, Arguments(Root(), null, "caladon-panarrii temple"), default);
        Assert.IsTrue(temple.MapTransitions!.Unconditional.Any(s => s.DestinationRuntime == 1 && s.Arrival == new WorldLocation(102647, 116816)));
        var result = AreaLoadOperation.Execute(9, Arguments(Root(), null) with { AreaMapArrival = new WorldLocation(102647, 116816) }, default);
        Assert.AreEqual(new AreaSectorCoordinate(1603, 1825), result.AreaMapWindowCentre);
        var chart = result.Area!.Plan.Chart;
        Assert.AreEqual(new Arcanum.Data.Traversal.AreaStepCell(102647, 116816), chart.ToWorld(chart.Spawn));
        Assert.AreEqual(9 * 4096, result.AreaStepSnapshot!.AdmittedCells);
        var none = result.Area.Snapshot.Objects.Single(o => o.WorldX == 102582 && o.WorldY == 116816 && o.PrototypeNumber == 4031);
        var row = result.Area.Plan.Coverage.Single(c => c.Identity == none.Key);
        Assert.AreEqual((Arcanum.World.AreaDisposition.Suppressed, "NoArt"), (row.Disposition, row.Reason));
        Assert.IsFalse(result.Area.Plan.Commands.Any(c => c.Identity == none.Key));
        Assert.IsTrue(result.Area.Plan.DrawableBodies > 0);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_map_census_lists_every_catalogued_map_and_the_ones_map_mode_opens()
    {
        // ARC-RENDER-027: 81 catalogued maps, all of which map mode opens (the main world map as a window around its map-list
        // start); the two scienticians underground levels have sector files but no map-list row (no transfer leads to them).
        var maps = AreaMapCensus.Maps(Arguments(Root(), null));
        Assert.AreEqual(81, maps.Count);
        Assert.AreEqual(81, maps.Count(m => m.Openable));
        CollectionAssert.AreEquivalent(new[] { "scienticians underground lev 1", "scienticians underground lev 2" },
            maps.Where(m => !m.Listed).Select(m => m.Name).ToArray());
        var main = maps.Single(m => m.Name.Equals(MainMap, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual((true, (WorldLocation?)new WorldLocation(92958, 82592)), (main.Window, main.Start));
        Assert.AreEqual(1, maps.Count(m => m.Window));
    }

    private static string Root()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root) return root;
        Assert.Inconclusive("DAT_ROOT is required for the opt-in main-map window check; this is not a synthetic pass.");
        return string.Empty;
    }

    private static TerrainViewerArguments Arguments(string root, AreaSectorCoordinate? sector, string map = MainMap) => TerrainViewerArguments.Parse([
        "--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"),
        "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map,
        .. sector is { } s ? new[] { "--area-map-sector", $"{s.X},{s.Y}" } : []]);
}
