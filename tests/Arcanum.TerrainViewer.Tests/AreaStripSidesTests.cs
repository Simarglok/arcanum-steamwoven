using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-089: the fill of a strip's sides on a screen wider than 4:3 (tiles of the strip's own art, repeated outwards).</summary>
[TestClass]
public sealed class AreaStripSidesTests
{
    [TestMethod]
    public void A_four_by_three_screen_has_no_margin_to_fill()
    {
        Assert.AreEqual(0, AreaStripSides.Left(AreaBarStrip.Top, 0).Count);
        Assert.AreEqual(0, AreaStripSides.Right(AreaBarStrip.Bottom, 0.2f).Count, "a quarter unit or less is rounding");
    }

    [TestMethod]
    public void The_left_side_repeats_the_tile_leftwards_from_the_strip_and_cuts_the_far_piece_keeping_the_tiles_right_columns()
    {
        var runs = AreaStripSides.Left(AreaBarStrip.Top, 70);
        CollectionAssert.AreEqual(new[] { new AreaStripSides.Run(-32, 186, 32), new AreaStripSides.Run(-64, 186, 32), new AreaStripSides.Run(-70, 212, 6) }, runs.ToArray());
        Assert.AreEqual(-70, runs.Min(r => r.DestX), "covers the whole margin and no more");
        Assert.AreEqual(70, runs.Sum(r => r.Width));
        for (var i = 1; i < runs.Count; i++) Assert.AreEqual(runs[i - 1].DestX, runs[i].DestX + runs[i].Width, "pieces abut");
    }

    [TestMethod]
    public void The_right_side_repeats_the_tile_rightwards_from_the_strips_last_column()
    {
        var runs = AreaStripSides.Right(AreaBarStrip.Bottom, 20);
        CollectionAssert.AreEqual(new[] { new AreaStripSides.Run(800, 792, 8), new AreaStripSides.Run(808, 792, 8), new AreaStripSides.Run(816, 792, 4) }, runs.ToArray());
        var (left, right) = AreaStripSides.Tiles(AreaBarStrip.Bottom);
        Assert.AreEqual((new AreaStripSides.Tile(0, 8), new AreaStripSides.Tile(792, 8)), (left, right), "the wood rail at each end");
        Assert.AreEqual(AreaStripSides.StripWidth, right.X + right.Width);
    }

    [TestMethod]
    public void A_twenty_one_by_nine_screen_at_two_times_needs_about_a_dozen_top_tiles_per_side()
    {
        // 2800 px wide at scale 2: (2800 / 2 - 800) / 2 = 300 UI units each side.
        Assert.AreEqual(10, AreaStripSides.Left(AreaBarStrip.Top, 300).Count);
        Assert.AreEqual(300, AreaStripSides.Right(AreaBarStrip.Top, 300).Sum(r => r.Width), 0.001);
    }
}
