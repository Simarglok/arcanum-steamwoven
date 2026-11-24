using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2577, ARC-UI-2891: the full-screen menus' plan in the Steamwoven layout: the 800 × 600 plate fitted to the window's height (the column menus), and hit tests that follow the drawn menu.</summary>
[TestClass]
public sealed class SteamwovenFullScreenTests
{
    [TestMethod]
    public void The_plan_is_the_plate_fitted_to_the_windows_height()
    {
        var plan = SteamwovenFullScreen.Plan(3840, 2160)!;
        Assert.AreEqual(3.6, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(480, 0, 2880, 2160), plan.Zone);
        Assert.AreEqual(new PixelRect(0, 0, 3840, 2160), plan.Frame);
        Assert.AreEqual(new PixelInsets(0, 0, 0, 0), plan.Bars);
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1600), (3440, 1440), (1024, 768), (1280, 1024) })
        {
            var (factor, zone) = SteamwovenColumnMenu.Fit(width, height);
            var fitted = SteamwovenFullScreen.Plan(width, height)!;
            Assert.AreEqual(factor, fitted.Factor, 1e-12, $"{width}x{height}");
            Assert.AreEqual(zone, fitted.Zone, $"{width}x{height}");
            Assert.AreSame(fitted.Layout.Column, fitted.Column);
        }
    }

    [TestMethod]
    public void No_plan_below_the_supported_scale()
    {
        // f = 800 / 3840 is under 0.25: the original look stays.
        Assert.IsNull(SteamwovenFullScreen.Plan(800, 450));
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
