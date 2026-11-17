using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2452: the Steamwoven prototype's plan (strip scale and places from the layout model) and the generated board.</summary>
[TestClass]
public sealed class SteamwovenHudTests
{
    [TestMethod]
    public void The_hud_unit_is_three_point_six_times_f_for_every_pair_of_panel_heights()
    {
        // ARC-UI-2608 (variant B of #2526, SWI-LAY-11): the centre block keeps 2880 px, so a unit is f * 3.6 for the owner's 120 and 456 and for the original's own 150 and 570.
        foreach (var options in new[] { SteamwovenLayoutOptions.Default, SteamwovenHud.Original })
        {
            var plan = SteamwovenHud.Plan(3840, 2160, options)!;
            Assert.AreEqual(3.6, plan.StripScale, 1e-9);
            Assert.AreEqual(480, plan.StripLeft);
            Assert.AreEqual(plan.Layout.Top.Centre.X, plan.StripLeft);
            Assert.AreEqual(2880, plan.Layout.Top.Centre.Width);
            Assert.AreEqual(options.TopPanelMaster, plan.TopInset);
            Assert.AreEqual(options.BottomPanelMaster, plan.BottomInset);
        }
    }

    [TestMethod]
    public void The_strip_frames_follow_the_clock_and_the_message_window_and_the_screen_frame_the_bottom_band()
    {
        // 3840 x 2160 at 120 and 456: the clock is at y 18 (the original's clock y 5 units = 18 px puts the top frame on the band's top), the message window at y 78 of its band
        // (the original's window lies 62 units = 223.2 -> 223 px below the bottom frame), and the screen's y 441 (1587.6 -> 1588 px) is the bottom band's upper edge.
        var plan = SteamwovenHud.Plan(3840, 2160)!;
        Assert.AreEqual(0, plan.TopStripY);
        Assert.AreEqual(0, plan.TopGap);
        Assert.AreEqual(2160 - 456 + 78 - 223, plan.BottomStripY);
        Assert.AreEqual(2160 - 456 - 1588, plan.ScreenOriginY);
        Assert.AreEqual(plan.Layout.Control(SteamwovenControlId.Clock).Device.Y, plan.TopStripY + 18);
        Assert.AreEqual(plan.Layout.Control(SteamwovenControlId.MessageWindow).Device.Y, plan.BottomStripY + 223);
    }

    [TestMethod]
    public void The_strip_scale_follows_f()
    {
        // 1920 x 1080: f = 0.5, a unit is 1.8, 800 units = 1440 px = the centre block, the model's top 60 and bottom 228.
        var plan = SteamwovenHud.Plan(1920, 1080)!;
        Assert.AreEqual(1.8, plan.StripScale, 1e-9);
        Assert.AreEqual(240, plan.StripLeft);
        Assert.AreEqual(60, plan.TopInset);
        Assert.AreEqual(228, plan.BottomInset);
    }

    [TestMethod]
    public void A_letterboxed_window_puts_the_bars_in_the_insets()
    {
        // 1024 x 768: a 1024 x 640 frame, 64 px bars at the top and bottom; the middle band lies between them.
        var plan = SteamwovenHud.Plan(1024, 768)!;
        Assert.AreEqual(64, plan.Layout.Bars.Top);
        Assert.AreEqual(64 + 32, plan.TopInset);
        Assert.AreEqual(64 + 122, plan.BottomInset);
        Assert.AreEqual(plan.Layout.Middle.Band.Height, 768 - plan.TopInset - plan.BottomInset);
    }

    [TestMethod]
    public void Every_control_of_the_layout_lies_inside_its_band_and_the_centre_block()
    {
        foreach (var (width, height) in new[] { (3840, 2160), (2560, 1440), (1920, 1080), (2560, 1600), (1024, 768), (3440, 1440) })
        {
            var layout = SteamwovenHud.Plan(width, height)!.Layout;
            foreach (var control in layout.Controls)
            {
                var band = control.Spec.Band == SteamwovenHudBand.Top ? layout.Top : layout.Bottom;
                Assert.IsTrue(control.Device.X >= band.Centre.X && control.Device.Right <= band.Centre.Right, $"{control.Id} at {width}x{height} lies in the centre block");
                if (control.Id != SteamwovenControlId.ExperienceStrip)
                    Assert.IsTrue(control.Device.Y >= band.Band.Y && control.Device.Bottom <= band.Band.Bottom, $"{control.Id} at {width}x{height} lies in its band");
            }
        }
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

    [TestMethod]
    public void The_layout_models_control_offsets_are_the_original_bars_positions()
    {
        // ARC-UI-2526: SteamwovenControls.OriginalX duplicates the original's unit positions; the viewer's own layout classes are the source.
        var expected = new Dictionary<SteamwovenControlId, int>
        {
            [SteamwovenControlId.Character] = AreaGameBarLayout.Spec(AreaBarButton.Character).X,
            [SteamwovenControlId.Logbook] = AreaGameBarLayout.Spec(AreaBarButton.Logbook).X,
            [SteamwovenControlId.Map] = AreaGameBarLayout.Spec(AreaBarButton.Map).X,
            [SteamwovenControlId.Inventory] = AreaGameBarLayout.Spec(AreaBarButton.Inventory).X,
            [SteamwovenControlId.FatePoints] = AreaGameBarLayout.Spec(AreaBarButton.FatePoints).X,
            [SteamwovenControlId.Sleep] = AreaGameBarLayout.Spec(AreaBarButton.Sleep).X,
            [SteamwovenControlId.Clock] = AreaClockLayout.WindowX,
            [SteamwovenControlId.ExperienceStrip] = AreaExperienceStripLayout.ClearX,
            [SteamwovenControlId.Combat] = AreaGameBarLayout.Spec(AreaBarButton.Combat).X,
            [SteamwovenControlId.Skills] = AreaGameBarLayout.Spec(AreaBarButton.Skills).X,
            [SteamwovenControlId.Spells] = AreaGameBarLayout.Spec(AreaBarButton.Spells).X,
            [SteamwovenControlId.Schematics] = AreaGameBarLayout.Spec(AreaBarButton.Schematics).X,
            [SteamwovenControlId.HealthVial] = AreaGameBarLayout.HealthGauge.X,
            [SteamwovenControlId.FatigueVial] = AreaGameBarLayout.FatigueGauge.X,
            [SteamwovenControlId.MessageWindow] = AreaMessageLayout.Window.X,
        };
        // The unit sizes the model's rule starts from (the buttons' come from art): clock 128 x 30, vials 28 x 88, message window 383 x 82, experience strip 384 x 14.
        Assert.AreEqual((128, 30), (AreaClockLayout.WindowWidth, AreaClockLayout.WindowHeight));
        Assert.AreEqual((28, 88), (AreaGameBarLayout.HealthGauge.Width, AreaGameBarLayout.HealthGauge.Height));
        Assert.AreEqual((28, 88), (AreaGameBarLayout.FatigueGauge.Width, AreaGameBarLayout.FatigueGauge.Height));
        Assert.AreEqual((383, 82), (AreaMessageLayout.Window.Width, AreaMessageLayout.Window.Height));
        Assert.AreEqual((384, 14), (AreaExperienceStripLayout.ClearWidth, AreaExperienceStripLayout.ClearHeight));
        Assert.AreEqual(expected.Count, SteamwovenControls.OriginalX.Count);
        foreach (var (id, x) in SteamwovenControls.OriginalX) Assert.AreEqual(expected[id], x, id.ToString());
    }
}
