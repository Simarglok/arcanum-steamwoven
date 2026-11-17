using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2577: the full-screen menus' plan in the Steamwoven layout (SWI-BDR-6): the 800 × 600 zone centred in the frame at `f · 3.6`, and hit tests that follow the drawn menu.</summary>
[TestClass]
public sealed class SteamwovenFullScreenTests
{
    [TestMethod]
    public void A_16_9_window_at_the_reference_size_draws_the_zone_at_three_point_six_over_the_whole_frame()
    {
        var plan = SteamwovenFullScreen.Plan(3840, 2160)!;
        Assert.AreEqual(3.6, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(480, 0, 2880, 2160), plan.Zone);
        Assert.AreEqual(new PixelRect(0, 0, 3840, 2160), plan.Frame);
        Assert.AreEqual(new PixelInsets(0, 0, 0, 0), plan.Bars);
        Assert.AreEqual(plan.Frame, plan.Layout.FullScreenBackdrop.Target);
    }

    [TestMethod]
    public void The_zone_follows_f_at_1920_x_1080()
    {
        var plan = SteamwovenFullScreen.Plan(1920, 1080)!;
        Assert.AreEqual(1.8, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(240, 0, 1440, 1080), plan.Zone);
    }

    [TestMethod]
    public void A_16_10_window_centres_the_zone_with_the_backdrop_above_and_below()
    {
        // 2560 x 1600: f = 2/3, the frame is the window (16:10), the zone 1920 x 1440 leaves 80 px above and below for the backdrop.
        var plan = SteamwovenFullScreen.Plan(2560, 1600)!;
        Assert.AreEqual(2.4, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(320, 80, 1920, 1440), plan.Zone);
        Assert.AreEqual(new PixelRect(0, 0, 2560, 1600), plan.Frame);
    }

    [TestMethod]
    public void A_wide_window_is_pillarboxed_and_the_bars_are_outside_the_backdrop()
    {
        // 3440 x 1440: f = 2/3, the frame is 2560 wide at x = 440, black bars on both sides.
        var plan = SteamwovenFullScreen.Plan(3440, 1440)!;
        Assert.AreEqual(new PixelRect(440, 0, 2560, 1440), plan.Frame);
        Assert.AreEqual(new PixelInsets(440, 0, 440, 0), plan.Bars);
        Assert.AreEqual(new PixelRect(760, 0, 1920, 1440), plan.Zone);
        Assert.AreEqual(plan.Frame, plan.Layout.FullScreenBackdrop.Target);
    }

    [TestMethod]
    public void A_tall_window_is_letterboxed()
    {
        // 1024 x 768: f = 4/15, the frame is 2400 f = 640 high with 64 px bars; the zone is 576 high, centred in it.
        var plan = SteamwovenFullScreen.Plan(1024, 768)!;
        Assert.AreEqual(new PixelRect(0, 64, 1024, 640), plan.Frame);
        Assert.AreEqual(new PixelInsets(0, 64, 0, 64), plan.Bars);
        Assert.AreEqual(new PixelRect(128, 96, 768, 576), plan.Zone);
    }

    [TestMethod]
    public void No_plan_below_the_supported_scale()
    {
        // f = 800 / 3840 is under 0.25: the original look stays.
        Assert.IsNull(SteamwovenFullScreen.Plan(800, 450));
    }

    [TestMethod]
    [DataRow(3840, 2160)] [DataRow(2560, 1440)] [DataRow(1920, 1080)] [DataRow(2560, 1600)] [DataRow(1920, 1200)]
    [DataRow(1280, 800)] [DataRow(1366, 768)] [DataRow(3440, 1440)] [DataRow(1024, 768)]
    public void The_zone_is_centred_in_the_frame_at_the_layout_scale_at_every_verification_size(int width, int height)
    {
        var plan = SteamwovenFullScreen.Plan(width, height)!;
        var frame = plan.Frame; var zone = plan.Zone;
        Assert.IsTrue(zone.X >= frame.X && zone.Right <= frame.Right && zone.Y >= frame.Y && zone.Bottom <= frame.Bottom, $"{width}x{height}: the zone {zone} lies in the frame {frame}");
        Assert.IsTrue(Math.Abs(zone.Width - 800 * plan.Factor) <= 1.01 && Math.Abs(zone.Height - 600 * plan.Factor) <= 1.01, $"{width}x{height}: the zone {zone} is 800 x 600 units at {plan.Factor}");
        Assert.IsTrue(Math.Abs((zone.X + zone.Right) - (frame.X + frame.Right)) <= 1 && Math.Abs((zone.Y + zone.Bottom) - (frame.Y + frame.Bottom)) <= 1, $"{width}x{height}: the zone {zone} is centred in the frame {frame}");
        // The frame and the bars cover the window exactly once.
        var bars = plan.Bars;
        Assert.AreEqual((long)width * height, (long)frame.Width * frame.Height + (long)bars.Left * height + (long)bars.Right * height + (long)(width - bars.Left - bars.Right) * (bars.Top + bars.Bottom));
        Assert.AreEqual(frame, plan.Layout.FullScreenBackdrop.Target);
    }

    [TestMethod]
    [DataRow(3840, 2160)] [DataRow(1920, 1080)] [DataRow(2560, 1600)] [DataRow(3440, 1440)] [DataRow(1024, 768)]
    public void Hit_testing_follows_the_drawn_menu(int width, int height)
    {
        var plan = SteamwovenFullScreen.Plan(width, height)!;
        // The main window's labels as the viewer places them (the i-th at (410, 143 + 50 i), centred; the label's own size is its font's: 130 x 33 stands in).
        var buttons = Enumerable.Range(0, 5).Select(slot => AreaMenuArtLayout.Button(AreaMenuWindow.Main, slot, (130, 33))).ToList();
        for (var i = 0; i < buttons.Count; i++)
        {
            var (x, y, w, h) = buttons[i];
            // The centre of the drawn label (device pixels) hits the label; so do its corners (inside), and a point a device pixel outside does not.
            var (cx, cy) = plan.ToDevice(x + w / 2.0, y + h / 2.0);
            Assert.AreEqual(i, plan.HitTest(buttons, cx, cy), $"{width}x{height}: centre of label {i}");
            var (left, top) = plan.ToDevice(x + 0.01, y + 0.01);
            var (right, bottom) = plan.ToDevice(x + w - 0.01, y + h - 0.01);
            Assert.AreEqual(i, plan.HitTest(buttons, left, top));
            Assert.AreEqual(i, plan.HitTest(buttons, right, bottom));
            var (outsideLeft, _) = plan.ToDevice(x - 0.01, y + h / 2.0);
            Assert.AreEqual(-1, plan.HitTest(buttons, outsideLeft, cy));
            var (_, aboveY) = plan.ToDevice(x + w / 2.0, y - 0.01);
            Assert.AreEqual(-1, plan.HitTest(buttons, cx, aboveY));
        }
        // A point on a bar, the frame's corner or the window's corner hits nothing.
        Assert.AreEqual(-1, plan.HitTest(buttons, 0, 0));
        Assert.AreEqual(-1, plan.HitTest(buttons, plan.Frame.X + 1, plan.Frame.Y + 1));
    }

    [TestMethod]
    public void The_screen_and_device_points_map_back_and_forth()
    {
        var plan = SteamwovenFullScreen.Plan(1920, 1200)!;
        foreach (var (x, y) in new[] { (0.0, 0.0), (410.0, 143.0), (800.0, 600.0), (399.5, 300.25) })
        {
            var (dx, dy) = plan.ToDevice(x, y);
            var (sx, sy) = plan.ToScreen(dx, dy);
            Assert.AreEqual(x, sx, 1e-9);
            Assert.AreEqual(y, sy, 1e-9);
        }
        // The zone's top left is unit (0, 0), and its bottom right (800, 600) lies within a pixel of the zone's edge.
        Assert.AreEqual(plan.Zone.X, plan.ToDevice(0, 0).X, 1e-9);
        Assert.AreEqual(plan.Zone.Y, plan.ToDevice(0, 0).Y, 1e-9);
        Assert.AreEqual(plan.Zone.Right, plan.ToDevice(800, 600).X, 1.01);
        Assert.AreEqual(plan.Zone.Bottom, plan.ToDevice(800, 600).Y, 1.01);
    }
}
