using Arcanum.Data.Terrain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class TerrainGeometryTests
{
    [TestMethod]
    public void Project_uses_fixed_signed_isometric_formula()
    {
        Assert.AreEqual(new TerrainPoint(0, 0), TerrainGeometry.Project(0, 0));
        Assert.AreEqual(new TerrainPoint(-40, 20), TerrainGeometry.Project(1, 0));
        Assert.AreEqual(new TerrainPoint(40, 20), TerrainGeometry.Project(0, 1));
        Assert.AreEqual(new TerrainPoint(80, 1240), TerrainGeometry.Project(30, 32));
    }

    [TestMethod]
    public void Place_uses_literal_tile_and_facade_footprints_and_crops()
    {
        var viewport = new TerrainRect(-100, -100, 300, 300);
        var tile = TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 78, 40, viewport)!.Value;
        var facade = TerrainGeometry.Place(TerrainKind.Facade, 1, 0, 0xB0000000, 78, 40, viewport)!.Value;
        Assert.AreEqual(new TerrainRect(1, 0, 78, 40), tile.Footprint);
        Assert.AreEqual(new TerrainRect(-39, 20, 80, 40), facade.Footprint);
        Assert.AreEqual(new TerrainRect(0, 0, 78, 40), tile.Source);
        Assert.AreEqual(new TerrainRect(-39, 20, 78, 40), facade.Destination);
    }

    [TestMethod]
    public void Place_returns_no_draw_for_offscreen_zero_area_and_empty_frame_intersections()
    {
        Assert.IsNull(TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainRect(200, 200, 1, 1)));
        Assert.IsNull(TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainRect(0, 0, 0, 20)));
        Assert.IsNull(TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 1, 1, new TerrainRect(77, 39, 2, 2)));
    }

    [TestMethod]
    public void Place_applies_raw_tile_flip_only_and_checked_argument_validation()
    {
        Assert.IsTrue(TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0xC1, 78, 40, new TerrainRect(-1, -1, 100, 100))!.Value.FlipX);
        Assert.IsFalse(TerrainGeometry.Place(TerrainKind.Facade, 0, 0, 0xB00000C1, 78, 40, new TerrainRect(-1, -1, 100, 100))!.Value.FlipX);
        AssertThrows<ArgumentOutOfRangeException>(() => TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 0, 1, new TerrainRect(0, 0, 1, 1)));
        AssertThrows<ArgumentOutOfRangeException>(() => TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 1, 0, new TerrainRect(0, 0, 1, 1)));
        AssertThrows<ArgumentOutOfRangeException>(() => _ = new TerrainRect(0, 0, -1, 0));
        AssertThrows<OverflowException>(() => TerrainGeometry.Project(int.MinValue, int.MaxValue));
        AssertThrows<OverflowException>(() => TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 1, 1, new TerrainRect(int.MaxValue, 0, 1, 1)));
    }

    [TestMethod]
    public void Place_has_literal_adjacent_tile_and_facade_overlap_edges()
    {
        var viewport = new TerrainRect(-100, -100, 300, 300);
        var originTile = TerrainGeometry.Place(TerrainKind.Tile, 0, 0, 0, 78, 40, viewport)!.Value.Footprint;
        var horizontalTile = TerrainGeometry.Place(TerrainKind.Tile, 1, 0, 0, 78, 40, viewport)!.Value.Footprint;
        var verticalTile = TerrainGeometry.Place(TerrainKind.Tile, 0, 1, 0, 78, 40, viewport)!.Value.Footprint;
        var adjacentFacade = TerrainGeometry.Place(TerrainKind.Facade, 1, 0, 0xB0000000, 78, 40, viewport)!.Value.Footprint;

        Assert.AreEqual(new TerrainRect(1, 20, 38, 20), Overlap(originTile, horizontalTile));
        Assert.AreEqual(new TerrainRect(41, 20, 38, 20), Overlap(originTile, verticalTile));
        Assert.AreEqual(new TerrainRect(1, 20, 40, 20), Overlap(originTile, adjacentFacade));
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); Assert.Fail($"Expected {typeof(TException).Name}."); }
        catch (TException) { }
    }

    private static TerrainRect Overlap(TerrainRect first, TerrainRect second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        return new TerrainRect(left, top, right - left, bottom - top);
    }
}
