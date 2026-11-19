using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2891: the full-screen and two-strip menus in column mode (TRD "Column mode", owner decision 2026-11-19): the 800 × 600 plate fills the window's height from edge to edge, centred, everything beside it black; hit tests follow the plate.</summary>
[TestClass]
public sealed class SteamwovenColumnMenuTests
{
    private static SteamwovenFullScreenPlan Original(int width, int height) => SteamwovenColumnMenu.Plan(width, height, SteamwovenLayoutOptions.ColumnOriginal)!;
    private static SteamwovenFullScreenPlan Thin(int width, int height) => SteamwovenColumnMenu.Plan(width, height, SteamwovenLayoutOptions.ColumnThin)!;

    [TestMethod]
    [DataRow(3840, 2160, 3.6, 480, 2880)] [DataRow(2560, 1440, 2.4, 320, 1920)] [DataRow(1920, 1080, 1.8, 240, 1440)] [DataRow(3440, 1440, 2.4, 760, 1920)]
    public void At_16_9_and_wider_the_plate_fills_the_window_height_and_the_sides_are_black(int width, int height, double factor, int left, int plateWidth)
    {
        var plan = Original(width, height);
        Assert.IsTrue(plan.IsColumn);
        Assert.AreEqual(factor, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(left, 0, plateWidth, height), plan.Zone);
        CollectionAssert.AreEqual(new[] { new PixelRect(0, 0, left, height), new PixelRect(left + plateWidth, 0, width - left - plateWidth, height) }, SteamwovenColumnMenu.BlackRects(plan).ToArray());
    }

    [TestMethod]
    public void At_16_10_the_plate_is_scaled_up_to_the_height_instead_of_leaving_a_gap()
    {
        // 1920 x 1200: s = 2, the plate 1600 x 1200. 2560 x 1600: s = 8/3, the plate 2133.3 wide, the top left rounded (213) and the far edge rounded up (2347).
        Assert.AreEqual(new PixelRect(160, 0, 1600, 1200), Original(1920, 1200).Zone);
        var plan = Original(2560, 1600);
        Assert.AreEqual(8.0 / 3, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(213, 0, 2134, 1600), plan.Zone);
        Assert.AreEqual(new PixelRect(107, 0, 1067, 800), Original(1280, 800).Zone);
    }

    [TestMethod]
    public void The_thin_preset_gives_the_same_menu_plan_as_the_original_strips()
    {
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1600), (3440, 1440), (1280, 800) })
        {
            Assert.AreEqual(Original(w, h).Zone, Thin(w, h).Zone, $"{w}x{h}");
            Assert.AreEqual(Original(w, h).Factor, Thin(w, h).Factor, $"{w}x{h}");
        }
    }

    [TestMethod]
    public void A_4_3_window_is_filled_and_a_narrower_one_is_limited_by_its_width_with_black_above_and_below()
    {
        // 1024 x 768 is 4:3: the plate is the window (s = 1.28).
        var four = Original(1024, 768);
        Assert.AreEqual(1.28, four.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(0, 0, 1024, 768), four.Zone);
        Assert.AreEqual(0, SteamwovenColumnMenu.BlackRects(four).Count);
        // 1000 x 1000: s = 1.25 by the width, the plate 1000 x 750 centred, black bars of 125 above and below.
        var square = Original(1000, 1000);
        Assert.AreEqual(1.25, square.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(0, 125, 1000, 750), square.Zone);
        CollectionAssert.AreEqual(new[] { new PixelRect(0, 0, 1000, 125), new PixelRect(0, 875, 1000, 125) }, SteamwovenColumnMenu.BlackRects(square).ToArray());
    }

    [TestMethod]
    [DataRow(2560, 1440)] [DataRow(1920, 1080)] [DataRow(2560, 1600)] [DataRow(1920, 1200)] [DataRow(3440, 1440)] [DataRow(1280, 800)] [DataRow(1024, 768)] [DataRow(3840, 2160)] [DataRow(1000, 1000)] [DataRow(1366, 768)]
    public void The_black_rectangles_and_the_plate_tile_the_window_exactly(int width, int height)
    {
        var plan = Original(width, height);
        var rects = SteamwovenColumnMenu.BlackRects(plan).Append(plan.Zone).ToArray();
        Assert.AreEqual((long)width * height, rects.Sum(rect => (long)rect.Width * rect.Height), $"{width}x{height}");
        for (var a = 0; a < rects.Length; a++)
            for (var b = a + 1; b < rects.Length; b++)
                Assert.IsFalse(rects[a].X < rects[b].Right && rects[b].X < rects[a].Right && rects[a].Y < rects[b].Bottom && rects[b].Y < rects[a].Bottom, $"{width}x{height}: {rects[a]} and {rects[b]}");
        // The plate is centred (each side within a pixel) and in the window; at 16:9 and wider it spans the height.
        Assert.IsTrue(Math.Abs(plan.Zone.X - (width - plan.Zone.Right)) <= 1 && Math.Abs(plan.Zone.Y - (height - plan.Zone.Bottom)) <= 1, $"{width}x{height}");
        if (width * 3 >= height * 4) Assert.IsTrue(plan.Zone.Y == 0 && plan.Zone.Bottom == height, $"{width}x{height}");
    }

    [TestMethod]
    public void The_two_strip_place_is_the_plates_top_left_and_the_unit_scale()
    {
        Assert.AreEqual(new SteamwovenWindowPlace(1.8, 240, 0), SteamwovenColumnMenu.Place(Original(1920, 1080)));
        Assert.AreEqual(new SteamwovenWindowPlace(2.4, 320, 0), SteamwovenColumnMenu.Place(Thin(2560, 1440)));
    }

    [TestMethod]
    public void Hit_testing_follows_the_drawn_plate_and_misses_the_black_sides()
    {
        var buttons = new List<(int X, int Y, int Width, int Height)> { (300, 100, 200, 40), (300, 160, 200, 40) };
        foreach (var plan in new[] { Original(2560, 1600), Thin(2560, 1440), Original(3440, 1440), Thin(1920, 1200), Original(1000, 1000) })
        {
            for (var slot = 0; slot < buttons.Count; slot++)
            {
                var (x, y) = plan.ToDevice(buttons[slot].X + buttons[slot].Width / 2.0, buttons[slot].Y + buttons[slot].Height / 2.0);
                Assert.AreEqual(slot, plan.HitTest(buttons, x, y), $"{plan.Zone} slot {slot}");
            }
            // Left of the plate (a black side) hits nothing.
            Assert.AreEqual(-1, plan.HitTest(buttons, plan.Zone.X - 10, plan.Zone.Y + 120 * plan.Factor));
        }
    }

    [TestMethod]
    public void Without_the_column_option_the_full_width_plan_is_unchanged()
    {
        Assert.IsNull(SteamwovenColumnMenu.Plan(1920, 1080, SteamwovenLayoutOptions.Default));
        var plan = SteamwovenFullScreen.Plan(1920, 1080, SteamwovenLayoutOptions.Default)!;
        Assert.IsFalse(plan.IsColumn);
        Assert.AreEqual(SteamwovenFullScreen.Plan(1920, 1080), plan);
        Assert.AreEqual(new PixelRect(240, 0, 1440, 1080), plan.Zone);
        Assert.AreEqual(plan.Frame, plan.Layout.FullScreenBackdrop.Target);
        // 2560 x 1600 full-width keeps the plate at f · 3.6 with the backdrop above and below, not the fitted height.
        Assert.AreEqual(new PixelRect(320, 80, 1920, 1440), SteamwovenFullScreen.Plan(2560, 1600, SteamwovenLayoutOptions.Default)!.Zone);
    }

    [TestMethod]
    public void No_column_plan_below_the_supported_scale()
    {
        Assert.IsNull(SteamwovenColumnMenu.Plan(800, 450, SteamwovenLayoutOptions.ColumnOriginal));
    }
}
