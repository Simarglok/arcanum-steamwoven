using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2936 (epic #2446, owner decision 2026-11-19): the Original look as the centred 4:3 composition, s = min(W / 800, H / 600), a virtual screen of 800 n x 600 n pixels with n = ceil(s), black around it, the pointer mapped back to
/// the original's screen, and the fixed 800 x 400 out-of-sight window.</summary>
[TestClass]
public sealed class OriginalCompositionTests
{
    [TestMethod]
    [DataRow(3840, 2160, 3.6, 4)] [DataRow(2560, 1440, 2.4, 3)] [DataRow(1920, 1080, 1.8, 2)] [DataRow(3440, 1440, 2.4, 3)] [DataRow(1280, 800, 4.0 / 3, 2)] [DataRow(1024, 768, 1.28, 2)]
    [DataRow(1280, 1024, 1.6, 2)] [DataRow(1600, 1200, 2.0, 2)] [DataRow(3200, 1800, 3.0, 3)] [DataRow(800, 600, 1.0, 1)] [DataRow(640, 480, 0.8, 1)]
    public void One_scale_from_the_original_screen_and_the_next_whole_factor_for_the_virtual_screen(int width, int height, double scale, int whole)
    {
        Assert.AreEqual(scale, OriginalComposition.Scale(width, height), 1e-9);
        Assert.AreEqual(whole, OriginalComposition.WholeFactor(width, height));
        Assert.AreEqual((800 * whole, 600 * whole), OriginalComposition.VirtualSize(width, height));
    }

    [TestMethod]
    public void The_composition_is_centred_and_everything_outside_it_is_black_at_every_aspect()
    {
        // 16:9, 16:10, 21:9, 4:3, 5:4, and a window narrower than 4:3 (black above and below).
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1600), (3440, 1440), (1600, 1200), (1280, 1024), (800, 900) })
        {
            var zone = OriginalComposition.Zone(width, height);
            var black = OriginalComposition.BlackRects(width, height);
            var total = zone.Width * zone.Height + black.Sum(rect => rect.Width * rect.Height);
            Assert.AreEqual(width * height, total, $"{width}x{height}: the composition and the black tile the window");
            Assert.IsTrue(Math.Abs((zone.X + zone.Right) - width) <= 1 && Math.Abs((zone.Y + zone.Bottom) - height) <= 1, $"{width}x{height}: centred");
            if (width * 3 >= height * 4) Assert.AreEqual(height, zone.Height, $"{width}x{height}: the whole height from 4:3 and wider");
            else Assert.AreEqual(width, zone.Width, $"{width}x{height}: limited by the width");
        }
        Assert.AreEqual(new PixelRect(213, 0, 2134, 1600), OriginalComposition.Zone(2560, 1600));
        Assert.AreEqual(new PixelRect(240, 0, 1440, 1080), OriginalComposition.Zone(1920, 1080));
        Assert.AreEqual(0, OriginalComposition.BlackRects(1600, 1200).Count, "4:3 has no black");
        CollectionAssert.AreEqual(new[] { new PixelRect(0, 0, 240, 1080), new PixelRect(1680, 0, 240, 1080) }, OriginalComposition.BlackRects(1920, 1080).ToArray());
    }

    [TestMethod]
    public void Hit_testing_follows_the_drawn_composition()
    {
        // 1920 x 1080: s = 1.8, the composition at x = 240; its top left is the original's (0, 0), its far corner (800, 600), the black beside it is outside.
        Assert.AreEqual((0.0, 0.0), OriginalComposition.ToScreen(1920, 1080, 240, 0));
        var (x, y) = OriginalComposition.ToScreen(1920, 1080, 240 + 1440, 1080);
        Assert.AreEqual(800.0, x, 1e-9); Assert.AreEqual(600.0, y, 1e-9);
        var (cx, cy) = OriginalComposition.ToScreen(1920, 1080, 960, 540);
        Assert.AreEqual(400.0, cx, 1e-9); Assert.AreEqual(300.0, cy, 1e-9);
        Assert.IsTrue(OriginalComposition.ToScreen(1920, 1080, 100, 500).X < 0, "a point on the black left side is left of the screen");
        Assert.IsFalse(OriginalComposition.Contains(1920, 1080, 100, 500));
        Assert.IsTrue(OriginalComposition.Contains(1920, 1080, 240, 0));
        Assert.IsFalse(OriginalComposition.Contains(1920, 1080, 240 + 1440, 500), "the far edge belongs to the black");
        // The play view's corners: the top strip is 41 units, the bottom strip 159.
        var (_, top) = OriginalComposition.ToDevice(1920, 1080, 0, 41);
        Assert.AreEqual(41 * 1.8, top, 1e-9);
        // A round trip.
        var (dx, dy) = OriginalComposition.ToDevice(2560, 1600, 123.5, 77.25);
        var (sx, sy) = OriginalComposition.ToScreen(2560, 1600, dx, dy);
        Assert.AreEqual(123.5, sx, 1e-9); Assert.AreEqual(77.25, sy, 1e-9);
    }

    [TestMethod]
    public void The_default_zoom_setting_shows_the_original_view_and_other_settings_scale_from_it()
    {
        // The setting's default 2 times the scale is the whole factor: the virtual screen's 800 n pixels hold 800 world pixels.
        foreach (var whole in new[] { 1, 2, 3, 4 })
            Assert.AreEqual(OriginalComposition.ViewWidth, OriginalComposition.ScreenWidth * whole / (WorldZoomPolicy.DefaultZoom * OriginalComposition.ZoomScale(whole)), 1e-9);
        Assert.AreEqual(2.0, OriginalComposition.ZoomScale(4), 1e-9);
        Assert.AreEqual(800 * 3 / (3 * OriginalComposition.ZoomScale(3)), 800 * 4 / (3 * OriginalComposition.ZoomScale(4)), 1e-9, "a setting of 3 shows the same world width at any factor");
    }

    [TestMethod]
    public void The_out_of_sight_window_is_the_fixed_800_by_400_centred_on_the_view()
    {
        Assert.AreEqual((600.0, 800.0, 800.0, 400.0), OriginalComposition.OutOfSightWindow(1000, 1000));
        Assert.AreEqual((-400.0, -200.0, 800.0, 400.0), OriginalComposition.OutOfSightWindow(0, 0));
    }
}
