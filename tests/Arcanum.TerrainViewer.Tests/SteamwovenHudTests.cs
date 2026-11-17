using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2452: the Steamwoven prototype's plan (strip scale and places from the layout model) and the generated board.</summary>
[TestClass]
public sealed class SteamwovenHudTests
{
    [TestMethod]
    public void The_TRD_heights_keep_the_strips_at_three_point_six_times_f_and_centred()
    {
        // 3840 x 2160: f = 1, strips 2880 px wide centred (480 each side), the 41-unit top strip (147.6 -> 148 px) stands on the 150 px band's lower edge,
        // the bottom strip hangs from the bottom band's upper edge; the insets are the middle band's.
        var plan = SteamwovenHud.Plan(3840, 2160, SteamwovenLayoutOptions.Default)!;
        Assert.AreEqual(3.6, plan.StripScale, 1e-9);
        Assert.AreEqual(480, plan.StripLeft);
        Assert.AreEqual(2, plan.TopStripY);
        Assert.AreEqual(2, plan.TopGap);
        Assert.AreEqual(1590, plan.BottomStripY);
        Assert.AreEqual(150, plan.TopInset);
        Assert.AreEqual(570, plan.BottomInset);
    }

    [TestMethod]
    public void The_strip_scale_follows_f_and_the_centre_block_is_the_layout_models()
    {
        // 1920 x 1080: f = 0.5, scale 1.8, 800 units = 1440 px, the TRD's table (top 75, bottom 285, side 240).
        var plan = SteamwovenHud.Plan(1920, 1080, SteamwovenLayoutOptions.Default)!;
        Assert.AreEqual(1.8, plan.StripScale, 1e-9);
        Assert.AreEqual(plan.Layout.Top.Centre.X, plan.StripLeft);
        Assert.AreEqual(75, plan.TopInset);
        Assert.AreEqual(285, plan.BottomInset);
        Assert.AreEqual(240, plan.StripLeft);
    }

    [TestMethod]
    public void A_letterboxed_window_puts_the_bars_in_the_insets()
    {
        // 1024 x 768: a 1024 x 640 frame, 64 px bars at the top and bottom; the middle band lies between them.
        var plan = SteamwovenHud.Plan(1024, 768, SteamwovenLayoutOptions.Default)!;
        Assert.AreEqual(64, plan.Layout.Bars.Top);
        Assert.AreEqual(64 + 40, plan.TopInset);
        Assert.AreEqual(64 + 152, plan.BottomInset);
        Assert.AreEqual(plan.Layout.Middle.Band.Height, 768 - plan.TopInset - plan.BottomInset);
    }

    [TestMethod]
    public void A_thinner_pair_of_panels_makes_a_smaller_centre_block_not_a_squashed_strip()
    {
        // 120 and 456 master px are 0.8 of 150 and 570: the strips are drawn at 0.8 of the TRD's scale, so the centre block is 2304 px and the sides take the rest.
        var plan = SteamwovenHud.Plan(3840, 2160, SteamwovenHud.Thin)!;
        Assert.AreEqual(2.88, plan.StripScale, 1e-9);
        Assert.AreEqual((3840 - 2304) / 2, plan.StripLeft);
        Assert.AreEqual(120, plan.TopInset);
        Assert.AreEqual(456, plan.BottomInset);
        Assert.IsTrue(plan.TopStripY >= plan.Layout.Top.Band.Y);
    }

    [TestMethod]
    public void The_smaller_panel_ratio_sets_the_scale_when_the_two_differ()
    {
        // Top 150 (ratio 1) and bottom 456 (0.8): the bottom strip would not fit at the top's scale, so the smaller ratio decides for both.
        var plan = SteamwovenHud.Plan(3840, 2160, new SteamwovenLayoutOptions { TopPanelMaster = 150, BottomPanelMaster = 456 })!;
        Assert.AreEqual(2.88, plan.StripScale, 1e-9);
    }

    [TestMethod]
    public void No_plan_below_the_supported_scale_or_for_options_that_break_the_grid()
    {
        Assert.IsNull(SteamwovenHud.Plan(800, 450, SteamwovenLayoutOptions.Default)); // f = 0.208
        Assert.IsNull(SteamwovenHud.Plan(1920, 1080, new SteamwovenLayoutOptions { TopPanelMaster = 100, BottomPanelMaster = 570 }));
    }

    [TestMethod]
    public void Panel_heights_parse_from_a_top_bottom_pair_only_when_valid()
    {
        var options = SteamwovenHud.ParseOptions("120,456");
        Assert.AreEqual(120, options!.TopPanelMaster);
        Assert.AreEqual(456, options.BottomPanelMaster);
        Assert.IsNull(SteamwovenHud.ParseOptions("120"));
        Assert.IsNull(SteamwovenHud.ParseOptions("100,456"));
        Assert.IsNull(SteamwovenHud.ParseOptions(null));
    }

    [TestMethod]
    public void A_board_is_exactly_the_asked_size_opaque_and_deterministic()
    {
        var first = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.Bottom, 211, 37, 1.8, -12.5, -3, null, [], 0, 0);
        var again = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.Bottom, 211, 37, 1.8, -12.5, -3, null, [], 0, 0);
        Assert.AreEqual(211 * 37 * 4, first.Length);
        CollectionAssert.AreEqual(first, again);
        for (var i = 3; i < first.Length; i += 4) Assert.AreEqual(255, first[i]);
    }

    [TestMethod]
    public void A_board_continues_the_grain_of_a_neighbour_at_its_unit_position()
    {
        // Two boards of one wood: the right one starts 100 units (180 px at 1.8) to the right of the left one; its first column is the left board's column 180.
        var wide = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 400, 20, 1.8, 0, 0, null, [], 0, 0);
        var narrow = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 100, 20, 1.8, 100, 0, null, [], 0, 0);
        for (var y = 0; y < 20; y++)
        for (var c = 0; c < 4; c++)
            Assert.AreEqual(wide[(y * 400 + 180) * 4 + c], narrow[(y * 100) * 4 + c]);
    }

    [TestMethod]
    public void A_rim_lies_on_the_kept_edge_of_the_board()
    {
        // The rim of a board that stands on its bottom (the top strip's) is in the last rows, not the first.
        var bottom = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.Bottom, 50, 40, 1.8, 0, 0, null, [], 0, 0);
        var plain = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 50, 40, 1.8, 0, 0, null, [], 0, 0);
        int Diff(byte[] a, byte[] b, int row) => Enumerable.Range(0, 50 * 4).Count(i => a[row * 200 + i] != b[row * 200 + i]);
        Assert.IsTrue(Diff(bottom, plain, 39) > 100, "the last row carries the rim");
        Assert.AreEqual(0, Diff(bottom, plain, 0), "the first row is not rimmed (the grain is the same as a plain board's)");
    }

    [TestMethod]
    public void A_board_takes_the_wood_colours_of_the_tone_frame()
    {
        // A tone frame of one flat colour with no recipe parts tunes the wood to that colour's mean; the default wood differs.
        var legacy = new byte[16 * 16 * 4];
        for (var i = 0; i < legacy.Length; i += 4) { legacy[i] = 200; legacy[i + 1] = 40; legacy[i + 2] = 40; legacy[i + 3] = 255; }
        var recipe = new ChromeRecipe("tone.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, [], Holes: ChromeHoles.None);
        var tuned = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 20, 20, 1, 0, 0, recipe, legacy, 16, 16);
        var plain = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 20, 20, 1, 0, 0, null, [], 0, 0);
        Assert.IsFalse(tuned.AsSpan().SequenceEqual(plain));
    }
}
