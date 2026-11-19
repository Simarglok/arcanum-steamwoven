using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2891: the full-screen and two-strip menus in the column (TRD "Column mode"): the 800 × 600 plate in the column plan's full-screen zone, black or vignette sides, wood only in a vertical gap, hit tests that follow the plate.</summary>
[TestClass]
public sealed class SteamwovenColumnMenuTests
{
    private static SteamwovenFullScreenPlan Original(int width, int height) => SteamwovenColumnMenu.Plan(width, height, SteamwovenLayoutOptions.ColumnOriginal)!;
    private static SteamwovenFullScreenPlan Thin(int width, int height) => SteamwovenColumnMenu.Plan(width, height, SteamwovenLayoutOptions.ColumnThin)!;

    [TestMethod]
    public void At_16_9_with_the_original_strips_the_plate_fills_the_frame_height_and_no_gap_is_left()
    {
        var plan = Original(1920, 1080);
        Assert.IsTrue(plan.IsColumn);
        Assert.AreEqual(1.8, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(240, 0, 1440, 1080), plan.Zone);
        Assert.AreEqual(0, plan.Column!.FullScreen.GapAbove.Height);
        Assert.AreEqual(0, plan.Column.FullScreen.GapBelow.Height);
    }

    [TestMethod]
    public void At_16_9_with_the_thin_strips_the_plate_is_narrower_with_a_gap_of_wood_above_and_below()
    {
        // k = 0.8: s = 1.44, the plate 1152 x 864 centred in 1920 x 1080; the column is as wide as the plate and spans the frame's height.
        var plan = Thin(1920, 1080);
        Assert.AreEqual(1.44, plan.Factor, 1e-9);
        Assert.AreEqual(new PixelRect(384, 108, 1152, 864), plan.Zone);
        Assert.AreEqual(new PixelRect(384, 0, 1152, 108), plan.Column!.FullScreen.GapAbove);
        Assert.AreEqual(new PixelRect(384, 972, 1152, 108), plan.Column.FullScreen.GapBelow);
    }

    [TestMethod]
    public void At_16_10_the_plate_leaves_a_gap_above_and_below()
    {
        // 2560 x 1600: f = 2/3, s = 2.4, the plate 1920 x 1440 leaves 80 px above and below.
        var plan = Original(2560, 1600);
        Assert.AreEqual(new PixelRect(320, 80, 1920, 1440), plan.Zone);
        Assert.AreEqual(new PixelRect(320, 0, 1920, 80), plan.Column!.FullScreen.GapAbove);
        Assert.AreEqual(new PixelRect(320, 1520, 1920, 80), plan.Column.FullScreen.GapBelow);
        // Only the sides are black: the column spans the whole height.
        CollectionAssert.AreEqual(new[] { new PixelRect(0, 0, 320, 1600), new PixelRect(2240, 0, 320, 1600) }, SteamwovenColumnMenu.BlackRects(plan).ToArray());
    }

    [TestMethod]
    public void A_wide_window_keeps_the_column_in_the_frame_and_everything_else_black()
    {
        // 3440 x 1440: f = 2/3, the frame is 2560 wide at x = 440; the column 1920 wide at x = 760.
        var plan = Original(3440, 1440);
        Assert.AreEqual(new PixelRect(760, 0, 1920, 1440), plan.Zone);
        CollectionAssert.AreEqual(new[] { new PixelRect(0, 0, 760, 1440), new PixelRect(2680, 0, 760, 1440) }, SteamwovenColumnMenu.BlackRects(plan).ToArray());
    }

    [TestMethod]
    public void A_tall_window_is_letterboxed_and_the_bars_above_and_below_the_column_are_black()
    {
        // 1024 x 768: the frame is 1024 x 640 at y = 64; the column 768 wide at x = 128 spans the frame's height; the plate (576 high) fills it.
        var plan = Original(1024, 768);
        Assert.AreEqual(new PixelRect(128, 96, 768, 576), plan.Zone);
        Assert.AreEqual(new PixelRect(128, 64, 768, 640), plan.Column!.Column);
        var black = SteamwovenColumnMenu.BlackRects(plan);
        CollectionAssert.Contains(black.ToArray(), new PixelRect(128, 0, 768, 64));
        CollectionAssert.Contains(black.ToArray(), new PixelRect(128, 704, 768, 64));
    }

    [TestMethod]
    [DataRow(2560, 1440)] [DataRow(1920, 1080)] [DataRow(2560, 1600)] [DataRow(1920, 1200)] [DataRow(3440, 1440)] [DataRow(1280, 800)] [DataRow(1024, 768)] [DataRow(3840, 2160)]
    public void The_black_rectangles_the_column_and_nothing_else_tile_the_window_for_both_presets(int width, int height)
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, SteamwovenLayoutOptions.ColumnThin })
        {
            var plan = SteamwovenColumnMenu.Plan(width, height, options)!;
            var rects = SteamwovenColumnMenu.BlackRects(plan).Append(plan.Column!.Column).ToArray();
            var tag = $"{width}x{height} k={options.Column!.K}";
            Assert.AreEqual((long)width * height, rects.Sum(rect => (long)rect.Width * rect.Height), tag);
            for (var a = 0; a < rects.Length; a++)
                for (var b = a + 1; b < rects.Length; b++)
                    Assert.IsFalse(Overlaps(rects[a], rects[b]), $"{tag}: {rects[a]} and {rects[b]}");
            // The plate and its two gaps tile the column; the plate lies in the window.
            var full = plan.Column.FullScreen;
            Assert.AreEqual(plan.Column.Column.Height, full.GapAbove.Height + full.Zone.Height + full.GapBelow.Height, tag);
            Assert.AreEqual(plan.Zone, full.Zone, tag);
            Assert.IsTrue(plan.Zone.X >= 0 && plan.Zone.Right <= width && plan.Zone.Y >= 0 && plan.Zone.Bottom <= height, tag);
        }
    }

    private static bool Overlaps(PixelRect a, PixelRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    [TestMethod]
    public void The_two_strip_place_is_the_plates_top_left_and_the_unit_scale()
    {
        var plan = Original(2560, 1600);
        var place = SteamwovenColumnMenu.Place(plan);
        Assert.AreEqual(new SteamwovenWindowPlace(2.4, 320, 80), place);
    }

    [TestMethod]
    public void Hit_testing_follows_the_drawn_plate_and_misses_the_black_sides()
    {
        var buttons = new List<(int X, int Y, int Width, int Height)> { (300, 100, 200, 40), (300, 160, 200, 40) };
        foreach (var plan in new[] { Original(2560, 1600), Thin(2560, 1600), Original(3440, 1440), Thin(1920, 1200) })
        {
            for (var slot = 0; slot < buttons.Count; slot++)
            {
                var (x, y) = plan.ToDevice(buttons[slot].X + buttons[slot].Width / 2.0, buttons[slot].Y + buttons[slot].Height / 2.0);
                Assert.AreEqual(slot, plan.HitTest(buttons, x, y), $"{plan.Zone} slot {slot}");
            }
            // Left of the plate (a black side) and in the gap above it hit nothing.
            Assert.AreEqual(-1, plan.HitTest(buttons, plan.Zone.X - 10, plan.Zone.Y + 120 * plan.Factor));
            Assert.AreEqual(-1, plan.HitTest(buttons, plan.Zone.X + 400 * plan.Factor, plan.Zone.Y - 5));
        }
    }

    [TestMethod]
    public void The_vignette_hangs_beside_the_plate_over_its_own_rows_and_is_as_wide_as_the_side_allows()
    {
        // 3440 x 1440: the plate 1920 wide at x = 760, scale 2.4: 160 units are 384 px, less than the 760 px side.
        var wide = Original(3440, 1440);
        var (left, right) = SteamwovenColumnMenu.VignetteRects(wide);
        Assert.AreEqual(new PixelRect(376, 0, 384, 1440), left);
        Assert.AreEqual(new PixelRect(2680, 0, 384, 1440), right);
        // 1920 x 1080: the side is 240 px, narrower than the 288 px fall-off: the vignette stops at the window's edge.
        var plan = Original(1920, 1080);
        (left, right) = SteamwovenColumnMenu.VignetteRects(plan);
        Assert.AreEqual(new PixelRect(0, 0, 240, 1080), left);
        Assert.AreEqual(new PixelRect(1680, 0, 240, 1080), right);
        // 2560 x 1600: only the plate's rows (80 .. 1520).
        (left, right) = SteamwovenColumnMenu.VignetteRects(Original(2560, 1600));
        Assert.AreEqual(80, left.Y);
        Assert.AreEqual(1520, left.Bottom);
        Assert.AreEqual(80, right.Y);
    }

    [TestMethod]
    public void The_vignette_fades_from_full_at_the_plate_to_nothing_at_its_end()
    {
        Assert.AreEqual(1.0, SteamwovenColumnMenu.VignetteWeight(0), 1e-12);
        Assert.AreEqual(0.5, SteamwovenColumnMenu.VignetteWeight(0.5), 1e-12);
        Assert.AreEqual(0.0, SteamwovenColumnMenu.VignetteWeight(1), 1e-12);
        Assert.AreEqual(1.0, SteamwovenColumnMenu.VignetteWeight(-3), 1e-12);
        Assert.AreEqual(0.0, SteamwovenColumnMenu.VignetteWeight(7), 1e-12);
        var last = 1.0;
        for (var t = 0.0; t <= 1.0; t += 0.05) { var w = SteamwovenColumnMenu.VignetteWeight(t); Assert.IsTrue(w <= last + 1e-12); last = w; }
    }

    [TestMethod]
    public void The_sides_option_is_read_from_text_and_black_is_the_default()
    {
        Assert.AreEqual(SteamwovenColumnSides.Black, SteamwovenColumnMenu.ParseSides(" Black "));
        Assert.AreEqual(SteamwovenColumnSides.Vignette, SteamwovenColumnMenu.ParseSides("VIGNETTE"));
        Assert.IsNull(SteamwovenColumnMenu.ParseSides("wood"));
        Assert.IsNull(SteamwovenColumnMenu.ParseSides(null));
        Assert.AreEqual(SteamwovenColumnSides.Black, SteamwovenColumnMenu.Sides);
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
    }

    [TestMethod]
    public void No_column_plan_below_the_supported_scale()
    {
        Assert.IsNull(SteamwovenColumnMenu.Plan(800, 450, SteamwovenLayoutOptions.ColumnOriginal));
    }
}
