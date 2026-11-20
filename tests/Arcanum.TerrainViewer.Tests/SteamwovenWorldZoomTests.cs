using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-3059: the Steamwoven world zoom in whole steps, its cap and default, and the screen to world mapping every overlay follows at each step.</summary>
[TestClass]
public sealed class SteamwovenWorldZoomTests
{
    [TestMethod]
    [DataRow(1920, 1080, 1.8, 2)]
    [DataRow(2560, 1440, 2.4, 2)]
    [DataRow(3840, 2160, 3.6, 3)]
    [DataRow(3024, 1964, 3.2733, 3)]
    [DataRow(800, 600, 1.0, 1)]
    [DataRow(1280, 800, 1.3333, 1)]
    public void The_default_step_is_the_issues_for_each_screen(int width, int height, double scale, int step)
    {
        Assert.AreEqual(scale, Math.Min(width / 800d, height / 600d), 1e-3);
        Assert.AreEqual(step, SteamwovenWorldZoom.Default(scale));
        Assert.AreEqual(step, SteamwovenWorldZoom.Resolve(SteamwovenWorldZoom.Automatic, scale));
    }

    [TestMethod]
    public void Steps_above_the_scale_are_not_offered_and_four_needs_three_and_three_quarters()
    {
        CollectionAssert.AreEqual(new[] { 1 }, SteamwovenWorldZoom.Steps(1.5).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, SteamwovenWorldZoom.Steps(1.8).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, SteamwovenWorldZoom.Steps(3.6).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, SteamwovenWorldZoom.Steps(3.75).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, SteamwovenWorldZoom.Steps(9).ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, SteamwovenWorldZoom.Steps(0.4).ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, SteamwovenWorldZoom.Steps(double.NaN).ToArray());
        Assert.IsFalse(SteamwovenWorldZoom.Offered(3, 2.4));
        Assert.IsTrue(SteamwovenWorldZoom.Offered(2, 2.4));
        Assert.IsTrue(SteamwovenWorldZoom.Offered(SteamwovenWorldZoom.Automatic, 2.4));
    }

    [TestMethod]
    public void A_chosen_step_above_the_cap_reads_as_the_cap_and_a_bad_setting_as_automatic()
    {
        Assert.AreEqual(1, SteamwovenWorldZoom.Resolve(1, 3.6));
        Assert.AreEqual(2, SteamwovenWorldZoom.Resolve(4, 2.4));
        Assert.AreEqual(3, SteamwovenWorldZoom.Resolve(-2, 3.6));
        Assert.AreEqual(3, SteamwovenWorldZoom.Resolve(9, 3.6) , "9 is no step: automatic");
    }

    [TestMethod]
    [DataRow(.4, 1)] [DataRow(1.5, 1)] [DataRow(1.51, 2)] [DataRow(2, 2)] [DataRow(2.6, 3)] [DataRow(7, 3)]
    public void A_free_zoom_snaps_to_the_nearest_offered_step(double zoom, int step) => Assert.AreEqual(step, SteamwovenWorldZoom.Snap(zoom, 3.6));

    [TestMethod]
    public void The_layout_scale_is_the_composition_scale_the_steps_cap_against()
    {
        // The column's unit scale (the plan the viewer takes it from) is s = min(w / 800, h / 600) at each screen of the issue.
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440), (3840, 2160), (3024, 1964) })
        {
            var s = SteamwovenHud.Plan(w, h, SteamwovenLayoutOptions.ColumnFitted)?.StripScale ?? throw new AssertFailedException($"{w}x{h}: no plan");
            Assert.AreEqual(Math.Min(w / 800d, h / 600d), s, 1e-6, $"{w}x{h} scale {s}");
        }
    }

    [TestMethod]
    [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public void Screen_to_world_and_back_agree_at_each_step_and_a_whole_step_lands_on_whole_pixels(int step)
    {
        const double w = 3840, h = 2160, cx = 1234, cy = 987;
        foreach (var (sx, sy) in new[] { (0d, 0d), (1920d, 1080d), (3839d, 2159d), (777d, 1500d) })
        {
            var (wx, wy) = SteamwovenWorldZoom.ToWorld(sx, sy, w, h, step, cx, cy);
            var (bx, by) = SteamwovenWorldZoom.ToScreen(wx, wy, w, h, step, cx, cy);
            Assert.AreEqual(sx, bx, 1e-9); Assert.AreEqual(sy, by, 1e-9);
        }
        // The centre of the screen is the camera centre; one world pixel is `step` screen pixels.
        Assert.AreEqual((cx, cy), SteamwovenWorldZoom.ToWorld(w / 2, h / 2, w, h, step, cx, cy));
        var (ax, _) = SteamwovenWorldZoom.ToScreen(cx + 1, cy, w, h, step, cx, cy);
        Assert.AreEqual(w / 2 + step, ax, 1e-9);
    }

    [TestMethod]
    [DataRow(1920, 1080, 2)] [DataRow(2560, 1440, 2)] [DataRow(3840, 2160, 1)] [DataRow(3840, 2160, 2)] [DataRow(3840, 2160, 3)]
    public void The_out_of_sight_view_is_the_drawn_view_at_the_step(int width, int height, int step)
    {
        // The rule's rectangle (decision 2026-11-07) is what the camera draws: the viewport over the step, centred on the camera.
        var (x, y, vw, vh) = SteamwovenWorldZoom.DrawnView(width, height, step, 1000, 500);
        Assert.AreEqual((double)width / step, vw); Assert.AreEqual((double)height / step, vh);
        Assert.AreEqual(1000, x + vw / 2, 1e-9); Assert.AreEqual(500, y + vh / 2, 1e-9);
        // Its corners are the screen's corners through the mapping.
        var (sx, sy) = SteamwovenWorldZoom.ToScreen(x, y, width, height, step, 1000, 500);
        Assert.AreEqual((0d, 0d), (sx, sy));
        // A higher step shows less world, so the rules reach less far (the accepted consequence).
        if (step > 1) Assert.IsTrue(vw < SteamwovenWorldZoom.DrawnView(width, height, step - 1, 1000, 500).Width);
    }
}
