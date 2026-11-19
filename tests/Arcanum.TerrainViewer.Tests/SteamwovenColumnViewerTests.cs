using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2890: the viewer's column mode, as plain numbers: the window hook, the wood of the gaps, the end caps and the dimmed world (the Godot side draws these rectangles).</summary>
[TestClass]
public sealed class SteamwovenColumnViewerTests
{
    private static readonly SteamwovenLayoutOptions ThinColumn = SteamwovenLayoutOptions.ForColumn(SteamwovenColumnMode.KThin); // ARC-UI-2925: the model keeps k = 0.8, the viewer no longer offers it
    private static readonly (int W, int H)[] Sizes = [(3840, 2160), (2560, 1440), (1920, 1080), (2560, 1600), (1920, 1200), (3440, 1440), (1280, 800), (1366, 768)];

    private static SteamwovenHudPlan Plan(int w, int h, SteamwovenLayoutOptions options) => SteamwovenHud.Plan(w, h, options)!;

    [TestMethod]
    public void The_hud_plan_of_a_column_has_the_strips_scale_and_the_column_s_zone()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, ThinColumn, SteamwovenLayoutOptions.ColumnFitted })
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, options);
            var column = plan.Column!;
            Assert.AreEqual(column.UnitScale, plan.StripScale, 1e-9, $"{w}x{h}: the strips' scale is the column's unit scale");
            Assert.AreEqual(column.Column.X, plan.StripLeft, $"{w}x{h}: the strip stands in the column");
            Assert.AreEqual(column.Window.Zone, plan.WindowZone);
            Assert.AreEqual(plan.Layout.Middle.Band.Y, column.TopStrip.Bottom, "the middle band starts under the top strip");
        }
    }

    [TestMethod]
    public void A_window_stands_in_the_column_zone_at_the_column_s_unit_scale()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, ThinColumn, SteamwovenLayoutOptions.ColumnFitted })
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, options);
            var zone = plan.Column!.Window.Zone;
            var body = SteamwovenWindowZone.Body(plan, 800, 400);
            var label = $"{w}x{h} k={options.Column!.K} fit={options.Column.FitHeight}";
            Assert.AreEqual(plan.Column.UnitScale, body.Scale, 1e-9, label);
            // An 800 x 400 window fills the zone (within a pixel: the zone's edges are rounded from their own exact position).
            Assert.AreEqual(zone.X, body.X, 1.0, label);
            Assert.AreEqual(zone.Y, body.Y, 1.0, label);
            Assert.IsTrue(body.X >= plan.Column.Column.X - 1 && body.X + 800 * body.Scale <= plan.Column.Column.Right + 1, $"{label}: inside the column");
            // A screen of 800 x 600: its body (41 units down) lies in the zone where the frame allows, it is centred in the column and stays inside the frame.
            var screen = SteamwovenWindowZone.Screen(plan, 41);
            Assert.AreEqual(zone.X, screen.X, 1.0, label);
            Assert.IsTrue(screen.Y >= plan.Layout.Frame.Y - 1 && screen.Y + 600 * screen.Scale <= plan.Layout.Frame.Bottom + 1 || 600 * screen.Scale > plan.Layout.Frame.Height, label);
        }
    }

    [TestMethod]
    public void Without_the_column_the_hook_is_the_full_width_zone_as_before()
    {
        var plan = Plan(2560, 1440, SteamwovenLayoutOptions.Default);
        Assert.IsNull(plan.Column);
        Assert.AreEqual(plan.Layout.WindowZone, plan.WindowZone);
        Assert.AreEqual(2560 / 3840.0 * 3.6, SteamwovenWindowZone.UnitScale(plan), 1e-9);
        Assert.AreEqual(0, SteamwovenColumnEdges.Parts(plan, true).Count);
        Assert.AreEqual(0, SteamwovenColumnEdges.DimmedWorld(plan).Count);
    }

    [TestMethod]
    public void The_gaps_take_the_family_s_wood_and_tile_with_the_window_zone_to_the_column()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, ThinColumn, SteamwovenLayoutOptions.ColumnFitted })
        foreach (var (w, h) in Sizes)
        foreach (var family in Enum.GetValues<SteamwovenBackdropFamily>())
        {
            var plan = Plan(w, h, options);
            var column = plan.Column!;
            var pieces = SteamwovenBackdrops.Pieces(family, plan);
            var label = $"{w}x{h} k={options.Column!.K} {family}";
            // The pieces cover the two gaps exactly: area equals, no overlap with the zone, all inside the column.
            long area = pieces.Sum(piece => (long)piece.Rect.Width * piece.Rect.Height);
            Assert.AreEqual((long)column.Window.GapAbove.Width * column.Window.GapAbove.Height + (long)column.Window.GapBelow.Width * column.Window.GapBelow.Height, area, label);
            foreach (var piece in pieces)
            {
                Assert.IsTrue(piece.Rect.X >= column.Column.X && piece.Rect.Right <= column.Column.Right, $"{label}: inside the column's width");
                Assert.IsTrue(piece.Rect.Bottom <= column.Window.Zone.Y || piece.Rect.Y >= column.Window.Zone.Bottom, $"{label}: not over the window");
                Assert.AreEqual(ChromeMaterial.Wood, piece.Material, label);
            }
            Assert.IsTrue(pieces.Count <= 4, label);
            // Where the window zone fills the middle band (16:9 at k = 1) there is no gap and no piece.
            if (column.Window.GapAbove.Height <= 0 && column.Window.GapBelow.Height <= 0) Assert.AreEqual(0, pieces.Count, label);
        }
    }

    [TestMethod]
    public void The_wood_of_a_gap_continues_the_window_art_s_grain_from_its_top_left()
    {
        // 2560 x 1600 at k = 1: 80 px above and below the 960 px zone; the grain of the upper gap starts 80 px (2.5 / 0.5... units) above the zone's top left.
        var plan = Plan(2560, 1600, SteamwovenLayoutOptions.ColumnOriginal);
        var unit = plan.Column!.UnitScale;
        var zone = plan.Column.Window.Zone;
        var pieces = SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.CharacterSheet, plan);
        Assert.AreEqual(2, pieces.Count);
        Assert.AreEqual(0.0, pieces[0].OriginX, 1e-9);
        Assert.AreEqual((plan.Column.Window.GapAbove.Y - zone.Y) / unit, pieces[0].OriginY, 1e-9);
        Assert.AreEqual(0.0, pieces[1].OriginX, 1e-9);
        Assert.AreEqual((plan.Column.Window.GapBelow.Y - zone.Y) / unit, pieces[1].OriginY, 1e-9);
        Assert.IsTrue(pieces[0].OriginY < 0 && pieces[1].OriginY > 0, "above the window's top is negative, below it is positive");
    }

    [TestMethod]
    public void A_two_part_family_splits_each_gap_at_the_zone_s_split()
    {
        var plan = Plan(2560, 1600, SteamwovenLayoutOptions.ColumnOriginal);
        var zone = plan.Column!.Window.Zone;
        foreach (var (family, units) in new[] { (SteamwovenBackdropFamily.Inventory, 358), (SteamwovenBackdropFamily.Logbook, 165), (SteamwovenBackdropFamily.Reading, 132) })
        {
            var split = zone.X + (int)Math.Round(units * plan.Column.UnitScale, MidpointRounding.AwayFromZero);
            Assert.AreEqual(split, SteamwovenBackdrops.SplitX(family, plan), family.ToString());
            var pieces = SteamwovenBackdrops.Pieces(family, plan);
            Assert.AreEqual(4, pieces.Count, family.ToString());
            Assert.AreEqual(split, pieces[0].Rect.Right);
            Assert.AreEqual(split, pieces[1].Rect.X);
            Assert.AreEqual(plan.Column.Column.X, pieces[0].Rect.X);
            Assert.AreEqual(plan.Column.Column.Right, pieces[1].Rect.Right);
            // The tone art follows the part, not the list index: above-left, above-right, below-left, below-right are parts 0, 1, 0, 1.
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1 }, pieces.Select(piece => piece.Part).ToArray(), family.ToString());
        }
        // A one-piece family is not split.
        Assert.IsNull(SteamwovenBackdrops.SplitX(SteamwovenBackdropFamily.WorldMap, plan));
    }

    [TestMethod]
    public void The_end_caps_stand_on_the_strips_and_on_the_middle_band_only_while_a_menu_is_open()
    {
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, ThinColumn);
            var column = plan.Column!;
            var play = SteamwovenColumnEdges.Parts(plan, false);
            Assert.AreEqual(2, play.Count, $"{w}x{h}");
            Assert.AreEqual(column.TopStrip, play[0]);
            Assert.AreEqual(column.BottomStrip, play[1]);
            var menu = SteamwovenColumnEdges.Parts(plan, true);
            Assert.AreEqual(3, menu.Count);
            Assert.AreEqual(column.TopStrip.Bottom, menu[1].Y, "the middle band shares the strips' edges");
            Assert.AreEqual(column.BottomStrip.Y, menu[1].Bottom);
            Assert.IsTrue(SteamwovenColumnEdges.BrassWidth(plan) >= 2 && SteamwovenColumnEdges.ShadowWidth(plan) > SteamwovenColumnEdges.BrassWidth(plan));
        }
    }

    [TestMethod]
    public void The_dimmed_world_is_the_frame_left_and_right_of_the_column()
    {
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, SteamwovenLayoutOptions.ColumnOriginal);
            var column = plan.Column!;
            var world = SteamwovenColumnEdges.DimmedWorld(plan);
            Assert.AreEqual(2, world.Count, $"{w}x{h}");
            Assert.AreEqual(column.WorldLeft, world[0]);
            Assert.AreEqual(column.WorldRight, world[1]);
            // With the column and the world rectangles together the frame's width is covered.
            Assert.AreEqual(plan.Layout.Frame.Width, world[0].Width + column.Column.Width + world[1].Width, $"{w}x{h}");
        }
    }

    [TestMethod]
    public void The_dimming_values_are_options_with_a_default_and_a_range()
    {
        Assert.IsTrue(SteamwovenWorldDim.Default.IsValid);
        Assert.IsFalse(new SteamwovenWorldDim(1.2, 0).IsValid);
        Assert.IsFalse(new SteamwovenWorldDim(0, -0.1).IsValid);
        var options = SteamwovenLayoutOptions.ForColumn(SteamwovenColumnMode.KOriginal);
        Assert.IsTrue(options.IsValid);
        var changed = options with { Column = options.Column! with { WorldDim = new SteamwovenWorldDim(0.6, 0.2) } };
        Assert.IsTrue(changed.IsValid);
        Assert.AreEqual(0.6, SteamwovenHud.Plan(1920, 1080, changed)!.Column!.Mode.WorldDim.Darken);
        Assert.IsFalse((options with { Column = options.Column! with { WorldDim = new SteamwovenWorldDim(2, 0) } }).IsValid);
    }

    [TestMethod]
    public void The_layout_choices_have_names_and_options()
    {
        Assert.AreEqual("column", SteamwovenLayoutChoices.Name(SteamwovenLayoutChoices.Default));
        foreach (var choice in Enum.GetValues<SteamwovenLayoutChoice>())
            Assert.AreEqual(choice, SteamwovenLayoutChoices.TryParse(SteamwovenLayoutChoices.Name(choice)));
        Assert.IsNull(SteamwovenLayoutChoices.TryParse("wide"));
        Assert.IsNull(SteamwovenLayoutChoices.TryParse(null));
        Assert.AreEqual(SteamwovenLayoutOptions.ColumnFitted, SteamwovenLayoutChoices.Options(SteamwovenLayoutChoice.Column));
        Assert.IsNull(SteamwovenLayoutChoices.TryParse("column-thin"), "ARC-UI-2925: the thin preset is gone");
        Assert.IsNull(SteamwovenLayoutChoices.Options(SteamwovenLayoutChoice.FullWidth).Column);
        Assert.AreEqual(SteamwovenLayoutOptions.Default, SteamwovenLayoutChoices.Options(SteamwovenLayoutChoice.FullWidth));
    }
}
