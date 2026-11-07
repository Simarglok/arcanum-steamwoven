using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-034: the terrain overview around a window (an invented 40 × 40-sector grid of grass, water on x = 24).</summary>
[TestClass]
public sealed class AreaTerrainOverviewTests
{
    private const int Width = 40;

    private static WorldTerrainGrid Grid()
    {
        var words = new ushort[Width * Width];
        for (var y = 0; y < Width; y++) words[24 + y * Width] = 1 << 11 | 1 << 6;
        return new WorldTerrainGrid(new(1.2f, false, Width, Width, 0), words);
    }

    [TestMethod]
    public void The_overview_holds_every_sector_around_the_core_with_its_colour_and_diamond()
    {
        var chart = new AreaChart(19 * 64, 19 * 64, new CellCoord(0, 0), "TEST");
        var window = new TerrainRect(-7680, 0, 15360, 7680);
        TerrainColour?[] colours = [new TerrainColour(10, 200, 10), null];
        var overview = AreaTerrainOverview.Create(Grid(), colours, new(20, 20), 1, chart, window);
        Assert.AreEqual(AreaTerrainOverview.MaxSectors - 9, overview.Sectors.Count, "13 × 13 less the 3 × 3 core");
        Assert.IsFalse(overview.Sectors.Any(s => Math.Abs(s.Sector.X - 20) <= 1 && Math.Abs(s.Sector.Y - 20) <= 1), "the window draws its core");
        // Water (type 1, the column x = 24) has no editor colour: the fallback, counted; grass keeps its colour.
        var water = overview.Sectors.Where(s => s.Sector.X == 24).ToArray();
        Assert.AreEqual(13, water.Length);
        Assert.IsTrue(water.All(s => s.Type == 1 && s.Colour == AreaTerrainOverview.FallbackColour));
        Assert.AreEqual(13, overview.MissingColours);
        Assert.IsTrue(overview.Sectors.Where(s => s.Sector.X != 24).All(s => s.Type == 0 && s.Colour == new TerrainColour(10, 200, 10)));
        Assert.AreEqual((66560, 33280), (overview.Bounds.Width, overview.Bounds.Height), "the 13 × 13-sector diamond");
        // A sector's diamond is 5,120 × 2,560 in chart space, the core's first sector starting at the chart origin.
        var first = AreaTerrainOverview.Diamond(chart, 19, 19);
        Assert.AreEqual((40.0, 0.0), first[0], "the top of chart cell (0,0)");
        Assert.AreEqual(5120.0, first[1].X - first[3].X);
        Assert.AreEqual(2560.0, first[2].Y - first[0].Y);
        // Neighbouring diamonds meet: the right corner of (19,19) is the top of (19,20), its bottom the top of (20,20).
        Assert.AreEqual(first[1], AreaTerrainOverview.Diamond(chart, 19, 20)[0]);
        Assert.AreEqual(first[2], AreaTerrainOverview.Diamond(chart, 20, 20)[0]);
        // The bounds hold the window and every diamond.
        Assert.IsTrue(overview.Bounds.X <= window.X && overview.Bounds.Width >= window.Width);
        Assert.IsTrue(overview.Sectors.SelectMany(s => s.Corners).All(c => c.X >= overview.Bounds.X && c.X <= overview.Bounds.X + overview.Bounds.Width
            && c.Y >= overview.Bounds.Y && c.Y <= overview.Bounds.Y + overview.Bounds.Height));
    }

    [TestMethod]
    public void The_floor_keeps_a_view_from_the_core_inside_the_overview()
    {
        var overview = AreaTerrainOverview.Create(Grid(), [new TerrainColour(1, 2, 3)], new(20, 20), 1, new AreaChart(0, 0, new CellCoord(0, 0), "TEST"),
            new TerrainRect(0, 0, 1, 1));
        Assert.AreEqual(.125, overview.Floor(1280, 800));
        Assert.AreEqual(.125, overview.Floor(2560, 1440), "a large display reaches the ordinary 1/8");
        Assert.AreEqual(.25, overview.Floor(3840, 2160), "a 4K display stops at 1/4, before the view leaves the overview");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => overview.Floor(0, 800));
    }

    [TestMethod]
    public void Sectors_off_the_grid_are_not_drawn_and_a_type_without_a_colour_takes_the_fallback()
    {
        var chart = new AreaChart(0, 0, new CellCoord(0, 0), "TEST");
        var overview = AreaTerrainOverview.Create(Grid(), [], new(2, 2), 1, chart, new TerrainRect(0, 0, 1, 1));
        Assert.IsTrue(overview.Sectors.All(s => s.Sector.X >= 0 && s.Sector.Y >= 0));
        Assert.AreEqual(9 * 9 - 9, AreaTerrainOverview.Create(Grid(), [], new(37, 37), 1, chart, new TerrainRect(0, 0, 1, 1)).Sectors.Count, "the upper edge too");
        Assert.AreEqual(9 * 9 - 9, overview.Sectors.Count, "x and y 0..8 around (2,2)");
        Assert.AreEqual(overview.Sectors.Count, overview.MissingColours);
        Assert.IsTrue(overview.Sectors.All(s => s.Colour == AreaTerrainOverview.FallbackColour));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaTerrainOverview.Create(Grid(), [], new(2, 2), AreaTerrainOverview.Radius, chart, new TerrainRect(0, 0, 1, 1)));
    }
}
