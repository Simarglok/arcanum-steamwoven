using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-031: the ground under the hero from the shown window's sector adjustments and tile art (<see cref="AreaHeroGround"/>), over invented tile names and arts; the snapshot path over the synthetic
/// fixture area.</summary>
[TestClass]
public sealed class AreaHeroGroundTests
{
    // Outdoor flippable position 0 carries n (0x10), position 1 does not; indoor (not flippable) position 0 carries n.
    private static TileSoundTable Tiles() => TileSoundTable.Parse(new Dictionary<int, string>
    {
        [0] = "stone/n", [1] = "grass/", [100] = "road/", [200] = "floor/", [300] = "wood/n", [400] = "end/",
    }).Table!;

    private static uint Tile(bool outdoor, bool flippable, int position) => (uint)(position << 22) | (outdoor ? 0x100u : 0) | (flippable ? 0x80u : 0);

    [TestMethod]
    public void The_cells_sector_gives_the_adjustment_and_the_cells_tile_gives_the_two_facts()
    {
        var aptitude = new Dictionary<(long X, long Y), int> { [(1749, 1512)] = 40, [(969, 1024)] = -20 };
        var art = new Dictionary<(long X, long Y), uint>
        {
            [(1749 * 64 + 3, 1512 * 64 + 63)] = Tile(true, true, 0), [(1749 * 64 + 4, 1512 * 64 + 63)] = Tile(true, true, 1), [(969 * 64, 1024 * 64)] = Tile(false, false, 0),
        };
        var tiles = Tiles();
        Assert.AreEqual(new(40, true, true), AreaHeroGround.Resolve(new(1749 * 64 + 3, 1512 * 64 + 63), aptitude, art, tiles), "outdoor tile with the n flag in a +40 sector");
        Assert.AreEqual(new(40, true, false), AreaHeroGround.Resolve(new(1749 * 64 + 4, 1512 * 64 + 63), aptitude, art, tiles), "outdoor, no n flag");
        Assert.AreEqual(new(-20, false, true), AreaHeroGround.Resolve(new(969 * 64, 1024 * 64), aptitude, art, tiles), "an indoor tile with the n flag in a -20 sector");
        Assert.AreEqual(new(40, false, false), AreaHeroGround.Resolve(new(1749 * 64 + 5, 1512 * 64), aptitude, art, tiles), "a cell with no tile: neither fact, the sector's adjustment stays");
        Assert.AreEqual(default, AreaHeroGround.Resolve(new(5, 5), aptitude, art, tiles), "outside every indexed sector: neutral");
        Assert.AreEqual(new(40, true, false), AreaHeroGround.Resolve(new(1749 * 64 + 3, 1512 * 64 + 63), aptitude, art, null), "no tile-name table: outdoor by the art, never nature");
    }

    [TestMethod]
    public void The_shown_snapshot_is_indexed_and_a_hero_without_a_cell_or_area_has_the_neutral_ground()
    {
        using var fixture = new AreaOwnerFixture();
        var snapshot = fixture.Owner.LoadStrict();
        var sector = snapshot.Sectors.First(s => s.Present && s.Scope == AreaScope.Core);
        // The hero's cell is chart-relative: a chart whose origin is the sector's puts chart cell (1, 1) on world cell (64X + 1, 64Y + 1).
        var chart = new AreaChart(sector.Coordinate.OriginX, sector.Coordinate.OriginY, new(0, 0), "TEST_CHART");
        (AreaChart, CellCoord)? hero = (chart, new(1, 1));
        var ground = new AreaHeroGround(() => snapshot, () => hero, () => null);
        Assert.AreEqual(new Arcanum.Session.SessionHeroGround(sector.AptitudeAdjustment, false, false), ground.Read(), "the fixture's sector carries no adjustment and its tile art is indoor");
        Assert.AreEqual(default, new AreaHeroGround(() => snapshot, () => null, () => null).Read());
        Assert.AreEqual(default, new AreaHeroGround(() => null, () => hero, () => null).Read());
        // A chart far from the window (the exterior's origin) puts the same chart cell outside every indexed sector: neutral.
        Assert.AreEqual(default, new AreaHeroGround(() => snapshot, () => (new AreaChart(1L << 40, 0, new(0, 0), "FAR"), new CellCoord(1, 1)), () => null).Read());
    }

    [TestMethod]
    public void A_chart_cell_becomes_a_world_cell_through_the_charts_origin_and_a_cell_beyond_the_cell_type_has_none()
    {
        var exterior = new AreaChart(92928, 82560, new(6, 1), "TEST_EXTERIOR");
        Assert.AreEqual(new CellCoord(92934, 82561), AreaHeroGround.WorldCell(exterior, new(6, 1)));
        // The ground of the cell's sector: world (92934, 82561) lies in sector (1452, 1290), the exterior's base sector.
        var aptitude = new Dictionary<(long X, long Y), int> { [(1452, 1290)] = 40 };
        Assert.AreEqual(new Arcanum.Session.SessionHeroGround(40), AreaHeroGround.Resolve(AreaHeroGround.WorldCell(exterior, new(6, 1))!.Value, aptitude, new Dictionary<(long X, long Y), uint>(), null));
        Assert.AreEqual(default, AreaHeroGround.Resolve(new(6, 1), aptitude, new Dictionary<(long X, long Y), uint>(), null), "the chart cell read as a world cell would miss it");
        Assert.IsNull(AreaHeroGround.WorldCell(new AreaChart(1L << 40, 0, new(0, 0), "FAR"), new(1, 1)));
        Assert.IsNull(AreaHeroGround.WorldCell(exterior, new(-100000, 0)), "a negative world cell");
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_adjusted_maps_window_carries_each_sectors_adjustment_from_its_file()
    {
        // ARC-MAGIC-031 on the installed original (DAT_ROOT): the area snapshot's sector adjustment is the sector file's second metadata word, as WorldSector reads it; counts only.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in sector adjustment check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root, "Arcanum");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        // The map with the most adjusted sectors (the original's "k'na tha": 19 sectors of +30), whose window must show them.
        var map = catalog.Maps.OrderByDescending(m => m.SectorFiles.Count(c => catalog.ReadSector(m, c).Sector?.AptitudeAdjustment() is not (null or 0))).First();
        var loaded = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map.Name]), default);
        Assert.IsNotNull(loaded.Area);
        int sectors = 0, nonZero = 0;
        foreach (var sector in loaded.Area.Snapshot.Sectors.Where(s => s.Present && s.Template is null))
        {
            var read = catalog.ReadSector(map, new((int)sector.Coordinate.X, (int)sector.Coordinate.Y));
            Assert.AreEqual(WorldSectorStatus.Read, read.Status, $"{sector.Coordinate.X},{sector.Coordinate.Y}");
            Assert.AreEqual(read.Sector!.AptitudeAdjustment(), sector.AptitudeAdjustment, $"sector {sector.Coordinate.X},{sector.Coordinate.Y}");
            sectors++; if (sector.AptitudeAdjustment != 0) nonZero++;
        }
        Console.WriteLine($"adjusted map window ({map.Name}): {sectors} sectors read, {nonZero} with a non-zero adjustment");
        Assert.IsTrue(sectors > 0 && nonZero > 0);
    }
}
