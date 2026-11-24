using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-3060: the four thin bars the HD pack has no frame for, drawn by code under the HD option (synthetic: no game art).</summary>
[TestClass]
public sealed class AreaThinBarCodeTests
{
    private static readonly int[] Arts = [772, 504, 505, 616];

    [TestMethod]
    public void Exactly_the_four_arts_without_a_pack_frame_are_covered_and_all_are_three_rows_tall()
    {
        CollectionAssert.AreEqual(new[] { 504, 505, 616, 772 }, AreaThinBarCode.ArtNumbers.ToArray());
        foreach (var art in Arts)
        {
            Assert.IsTrue(AreaThinBarCode.Covers(art), art.ToString());
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, AreaThinBarCode.Rows(art).Select(r => r.Y).ToArray(), art.ToString());
            Assert.AreEqual(AreaThinBarCode.Height, AreaThinBarCode.Rows(art).Count, art.ToString());
        }
        // The bar arts the pack does cover (the frame, the strip pieces, the buttons) are not drawn by code.
        foreach (var other in new[] { 503, 501, 773, 782, 0 }) Assert.IsFalse(AreaThinBarCode.Covers(other), other.ToString());
        Assert.AreEqual(0, AreaThinBarCode.Rows(503).Count);
        // The experience piece is the strip table's last row.
        Assert.AreEqual(AreaThinBarCode.ExperienceArt, AreaExperienceStripLayout.Rows[^1].Art);
    }

    [TestMethod]
    public void The_colours_are_the_documented_ones_with_the_legacy_row_structure()
    {
        CollectionAssert.AreEqual(new int?[] { 0xFF6A06, 0xFFC87A, 0xDB4E06 }, AreaThinBarCode.Rows(772).Select(r => r.Rgb).ToArray());
        CollectionAssert.AreEqual(new int?[] { 0xFF7070, 0xFF0000, 0xB50000 }, AreaThinBarCode.Rows(AreaFollowerFrameLayout.HealthArt).Select(r => r.Rgb).ToArray());
        CollectionAssert.AreEqual(new int?[] { 0xE6FF70, 0xD3FF00, 0x96B500 }, AreaThinBarCode.Rows(AreaFollowerFrameLayout.PoisonedHealthArt).Select(r => r.Rgb).ToArray());
        // The fatigue art's middle row is the colour key in the Legacy art: transparent, nothing drawn.
        CollectionAssert.AreEqual(new int?[] { 0x7070FF, null, 0x0000B5 }, AreaThinBarCode.Rows(AreaFollowerFrameLayout.FatigueArt).Select(r => r.Rgb).ToArray());
        // Health and poisoned health are told apart by colour: not the same fill.
        Assert.AreNotEqual(AreaThinBarCode.Rows(505)[1].Rgb, AreaThinBarCode.Rows(616)[1].Rgb);
    }

    [TestMethod]
    public void At_the_legacy_scale_each_bar_is_the_legacy_rectangle_and_its_fill_length_is_the_legacy_rule()
    {
        // Follower bars: 34 wide, cropped by PortraitFill; every drawn row spans the same width from the bar's left edge, one unit tall, stacked without gaps.
        for (var percent = -5; percent <= 120; percent++)
        {
            var filled = LifeBarRules.PortraitFill(percent, AreaFollowerFrameLayout.BarWidth);
            foreach (var art in new[] { 504, 505, 616 })
            {
                var rects = AreaThinBarCode.Rects(art, 0, 0, filled, 0, 0, 1, 1);
                if (filled == 0) { Assert.AreEqual(0, rects.Count); continue; }
                Assert.AreEqual(AreaThinBarCode.Rows(art).Count(r => r.Rgb is not null), rects.Count);
                foreach (var (x, y, w, h, _) in rects) { Assert.AreEqual(0f, x); Assert.AreEqual(filled, w, $"{art} at {percent} %"); Assert.AreEqual(1f, h); Assert.IsTrue(y is >= 0 and < 3); }
            }
        }
        // Experience strip: the remainder piece is the table's last row cut to CutWidth of the 372-wide art, at (216, 47).
        var last = AreaExperienceStripLayout.Rows[^1];
        for (var percent = 1; percent < 100; percent++)
        {
            var width = AreaExperienceStripLayout.CutWidth(372, percent);
            var rects = AreaThinBarCode.Rects(772, last.X, last.Y, width, 0, 0, 1, 1);
            Assert.AreEqual(3, rects.Count);
            Assert.IsTrue(rects.All(r => r.X == last.X && r.Width == width && r.Height == 1f), $"{percent} %");
            CollectionAssert.AreEqual(new float[] { last.Y, last.Y + 1, last.Y + 2 }, rects.Select(r => r.Y).ToArray());
        }
        // A real progress: the piece the strip layout picks is the one drawn, with the remainder crop (progress 550 of 1000 -> scaled 605 -> six pieces, 5 % remainder).
        var pieces = AreaExperienceStripLayout.Pieces(550);
        var remainder = pieces[^1];
        Assert.AreEqual(772, remainder.Art);
        Assert.AreEqual(5, remainder.WidthPercent);
        Assert.AreEqual(18, AreaExperienceStripLayout.CutWidth(372, remainder.WidthPercent));
    }

    [TestMethod]
    public void A_whole_scale_changes_no_edge_and_a_fractional_one_puts_every_edge_on_a_device_pixel()
    {
        foreach (var scale in new float[] { 1, 2, 3, 4 })
            foreach (var origin in new float[] { 0, 10, 123 })
            {
                var (start, end) = AreaThinBarCode.SnapSpan(7, 34, origin, scale);
                Assert.AreEqual(7f, start, 1e-4); Assert.AreEqual(41f, end, 1e-4);
            }
        foreach (var scale in new[] { 1.17f, 1.5f, 1.7f, 2.33f, 2.9f })
            foreach (var origin in new[] { 0f, 3.4f, 87.6f })
                for (var span = 1; span <= 40; span += 3)
                {
                    var (start, end) = AreaThinBarCode.SnapSpan(5, span, origin, scale);
                    foreach (var edge in new[] { start, end })
                    {
                        var device = origin + edge * scale;
                        Assert.AreEqual(Math.Round(device), device, 1e-3, $"scale {scale} origin {origin} span {span}");
                    }
                    // Each edge moves by at most half a device pixel.
                    Assert.IsTrue(Math.Abs(origin + start * scale - (origin + 5 * scale)) <= 0.5001 && Math.Abs(origin + end * scale - (origin + (5 + span) * scale)) <= 0.5001);
                }
        // A span that is not finite and positive in scale stays as it is.
        Assert.AreEqual((2f, 5f), AreaThinBarCode.SnapSpan(2, 3, 0, 0));
        Assert.AreEqual((2f, 5f), AreaThinBarCode.SnapSpan(2, 3, 0, float.NaN));
    }

    [TestMethod]
    public void At_a_fractional_scale_the_rows_tile_the_bar_with_whole_device_pixels_and_no_gap()
    {
        foreach (var scale in new[] { 1.0f, 1.17f, 1.5f, 1.7f, 2.0f, 2.4f, 3.3f })
        {
            var rects = AreaThinBarCode.Rects(505, 3, 39, 34, 11.3f, 20.6f, scale, scale);
            Assert.AreEqual(3, rects.Count, $"scale {scale}");
            for (var i = 1; i < rects.Count; i++) Assert.AreEqual(rects[i - 1].Y + rects[i - 1].Height, rects[i].Y, 1e-3, $"scale {scale}");
            var totalDevice = (rects[^1].Y + rects[^1].Height - rects[0].Y) * scale;
            Assert.AreEqual(Math.Round(totalDevice), totalDevice, 1e-3);
            Assert.IsTrue(Math.Abs(totalDevice - 3 * scale) <= 1.0001, $"scale {scale}: {totalDevice}");
            foreach (var r in rects) { var w = r.Width * scale; Assert.AreEqual(Math.Round(w), w, 1e-3); Assert.IsTrue(r.Height * scale >= 0.999f); }
        }
        // Below one device pixel per unit a row can snap to nothing: it is left out, never drawn with a negative height.
        foreach (var rect in AreaThinBarCode.Rects(505, 0, 0, 34, 0, 0, 0.4f, 0.4f)) Assert.IsTrue(rect.Height > 0 && rect.Width > 0);
        // The fatigue bar leaves its middle row out: two rectangles, with a gap where the Legacy art is transparent.
        var fatigue = AreaThinBarCode.Rects(504, 0, 0, 34, 0, 0, 2, 2);
        Assert.AreEqual(2, fatigue.Count);
        Assert.AreEqual(1f, fatigue[1].Y - (fatigue[0].Y + fatigue[0].Height));
    }
}
