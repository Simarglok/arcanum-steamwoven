using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2576 (SWI-BDR-1, -4, -5, -7, SWI-VER-5): the generated backdrop of each census family over the middle band, and the seam ratio that measures its joins.</summary>
[TestClass]
public sealed class SteamwovenBackdropsTests
{
    private static readonly (int Width, int Height)[] Sizes = [(3840, 2160), (2560, 1440), (1920, 1080), (2560, 1600), (1920, 1200), (1280, 800), (1366, 768), (3440, 1440), (1024, 768)];

    [TestMethod]
    public void Each_window_mode_of_the_menu_families_has_its_census_family()
    {
        Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf(InterfaceMode.Inventory));
        foreach (var mode in new[] { InterfaceMode.Loot, InterfaceMode.Steal, InterfaceMode.Barter, InterfaceMode.InventoryKind5, InterfaceMode.InventoryKind6 })
            Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf(mode), mode.ToString());
        Assert.AreEqual(SteamwovenBackdropFamily.CharacterSheet, SteamwovenBackdrops.FamilyOf(InterfaceMode.Character));
        Assert.AreEqual(SteamwovenBackdropFamily.Logbook, SteamwovenBackdrops.FamilyOf(InterfaceMode.Logbook));
        Assert.AreEqual(SteamwovenBackdropFamily.Reading, SteamwovenBackdrops.FamilyOf(InterfaceMode.Book));
        Assert.AreEqual(SteamwovenBackdropFamily.WorldMap, SteamwovenBackdrops.FamilyOf(InterfaceMode.WorldMap));
        Assert.AreEqual(SteamwovenBackdropFamily.Schematic, SteamwovenBackdrops.FamilyOf(InterfaceMode.Schematic));
        // The world stays in view in the dialog and sleep views, and a target mode or the game is no window.
        foreach (var mode in new[] { InterfaceMode.Game, InterfaceMode.Dialog, InterfaceMode.Sleep, InterfaceMode.SpellTarget, InterfaceMode.SkillTarget, InterfaceMode.QuantityPicker, InterfaceMode.ItemTarget, InterfaceMode.FollowerOrder })
            Assert.IsNull(SteamwovenBackdrops.FamilyOf(mode), mode.ToString());
    }

    [TestMethod]
    public void The_window_nearest_the_top_of_the_stack_gives_the_family()
    {
        // A quantity picker over the inventory keeps the inventory's backdrop; a barter over a dialog takes the barter's; a dialog alone has none.
        Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Inventory, InterfaceMode.QuantityPicker]));
        Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Dialog, InterfaceMode.Barter]));
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Dialog]));
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Inventory, InterfaceMode.Dialog]), "a dialog on top keeps the world in view");
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Logbook, InterfaceMode.Sleep]));
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game]));
    }

    [TestMethod]
    public void Only_the_three_two_part_families_split_and_at_the_census_units()
    {
        Assert.AreEqual(358, SteamwovenBackdrops.LeftUnits(SteamwovenBackdropFamily.Inventory));
        Assert.AreEqual(165, SteamwovenBackdrops.LeftUnits(SteamwovenBackdropFamily.Logbook));
        Assert.AreEqual(132, SteamwovenBackdrops.LeftUnits(SteamwovenBackdropFamily.Reading));
        foreach (var family in new[] { SteamwovenBackdropFamily.CharacterSheet, SteamwovenBackdropFamily.WorldMap, SteamwovenBackdropFamily.Schematic })
            Assert.IsNull(SteamwovenBackdrops.LeftUnits(family), family.ToString());
    }

    [TestMethod]
    public void The_pieces_cover_the_middle_band_exactly_and_a_two_part_window_splits_at_the_layout_model_split()
    {
        foreach (var (width, height) in Sizes)
        {
            var plan = SteamwovenHud.Plan(width, height)!;
            var layout = plan.Layout;
            var band = layout.Middle.Band;
            foreach (var family in Enum.GetValues<SteamwovenBackdropFamily>())
            {
                var tag = $"[{width}x{height}] {family}";
                var pieces = SteamwovenBackdrops.Pieces(family, plan);
                Assert.AreEqual(SteamwovenBackdrops.LeftUnits(family) is null ? 1 : 2, pieces.Count, tag);
                // The pieces tile the band: the first starts at its left edge, each starts where the one before ends, the last ends at its right edge, all of the band's height.
                Assert.AreEqual(band.X, pieces[0].Rect.X, tag);
                Assert.AreEqual(band.Right, pieces[^1].Rect.Right, tag);
                for (var i = 0; i < pieces.Count; i++)
                {
                    Assert.AreEqual(band.Y, pieces[i].Rect.Y, tag);
                    Assert.AreEqual(band.Height, pieces[i].Rect.Height, tag);
                    Assert.IsTrue(pieces[i].Rect.Width > 0, tag + " no empty piece");
                    if (i > 0) Assert.AreEqual(pieces[i - 1].Rect.Right, pieces[i].Rect.X, tag + " the parts meet");
                }
                // The band is the frame's width (the sides carry the same material to the frame's edges, SWI-BDR-1).
                Assert.AreEqual(layout.Frame.X, band.X, tag);
                Assert.AreEqual(layout.Frame.Right, band.Right, tag);
            }
            // SWI-BDR-5: the inventory family splits at the layout model's split, its parts are the model's rectangles.
            var inventory = SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.Inventory, plan);
            Assert.AreEqual(layout.Split.SplitX, inventory[0].Rect.Right, $"[{width}x{height}] the split of the layout model");
            Assert.AreEqual(layout.Split.Left, inventory[0].Rect);
            Assert.AreEqual(layout.Split.Right, inventory[1].Rect);
        }
    }

    [TestMethod]
    public void The_split_lies_at_the_left_part_of_the_window_in_the_zone()
    {
        foreach (var (width, height) in Sizes)
        {
            var plan = SteamwovenHud.Plan(width, height)!;
            var zone = plan.Layout.WindowZone;
            var unit = SteamwovenWindowZone.UnitScale(plan);
            foreach (var family in new[] { SteamwovenBackdropFamily.Inventory, SteamwovenBackdropFamily.Logbook, SteamwovenBackdropFamily.Reading })
            {
                var split = SteamwovenBackdrops.SplitX(family, plan)!.Value;
                var wanted = zone.X + SteamwovenBackdrops.LeftUnits(family)!.Value * unit;
                Assert.IsTrue(Math.Abs(split - wanted) <= 1.5, $"[{width}x{height}] {family} splits {split}, the window's left part ends at {wanted:F1}");
            }
            // 1290 px from the centre block's left edge at 3840 x 2160 (SWI-BDR-5; 358 x 3.6 = 1288.8, snapped).
            if (width == 3840 && height == 2160) Assert.AreEqual(plan.Layout.Top.Centre.X + 1290, plan.Layout.Split.SplitX, 2);
        }
    }

    [TestMethod]
    public void Every_piece_has_a_window_art_for_its_tone_and_the_bars_read_it()
    {
        foreach (var family in Enum.GetValues<SteamwovenBackdropFamily>())
            for (var piece = 0; piece < (SteamwovenBackdrops.LeftUnits(family) is null ? 1 : 2); piece++)
                CollectionAssert.Contains(SteamwovenBackdrops.ToneArts.ToArray(), SteamwovenBackdrops.ToneArt(family, piece), $"{family} piece {piece}");
        // The census arts: the paper doll and inventory, the sheet, the logbook's side and book, the reading side bar, the map frame, the schematic base.
        Assert.AreEqual(223, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Inventory, 0));
        Assert.AreEqual(221, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Inventory, 1));
        Assert.AreEqual(264, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Logbook, 0));
        Assert.AreEqual(260, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Logbook, 1));
        foreach (var number in SteamwovenBackdrops.ToneArts) CollectionAssert.Contains(AreaGameBarContent.ArtNumbers.ToArray(), number);
    }

    [TestMethod]
    public void The_grain_of_each_piece_starts_at_the_art_it_continues()
    {
        var plan = SteamwovenHud.Plan(1920, 1080)!;
        var zone = plan.Layout.WindowZone;
        var unit = SteamwovenWindowZone.UnitScale(plan);
        // A one-piece family continues the window's own wood: the grain starts at the window's top left, so at the zone's left edge it is at 0 and the pieces to its left have negative units.
        var map = SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.WorldMap, plan)[0];
        Assert.AreEqual((plan.Layout.Middle.Band.X - zone.X) / unit, map.OriginX, 1e-9);
        Assert.AreEqual((plan.Layout.Middle.Band.Y - zone.Y) / unit, map.OriginY, 1e-9);
        Assert.IsTrue(map.OriginX <= 0 && map.OriginY <= 0);
        // Each half of a two-part window is an art of its own: the right half's grain starts at the split.
        var parts = SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.Inventory, plan);
        Assert.AreEqual((parts[0].Rect.X - zone.X) / unit, parts[0].OriginX, 1e-9);
        Assert.AreEqual(0.0, parts[1].OriginX, 1e-9);
        Assert.AreEqual(parts[0].OriginY, parts[1].OriginY, 1e-9);
        Assert.IsTrue(parts.All(part => part.Material == ChromeMaterial.Wood), "the census's wood under every family of the middle band");
    }

    [TestMethod]
    public void A_plain_texture_has_seam_ratio_one_and_a_step_at_the_join_fails_the_gate()
    {
        const int width = 400, height = 120;
        var plain = Image(width, height, (x, y) => (byte)(100 + (x * 7 + y * 13) % 11));
        foreach (var join in new[] { 150, 250 })
        {
            var ratio = SteamwovenSeam.ColumnRatio(plain, width, height, join, 10, 110, 60)!.Value;
            Assert.IsTrue(ratio is > 0.5 and < SteamwovenSeam.Limit, $"a texture without a join has R near 1 (got {ratio:F2})");
        }
        // The same texture with a lighter right half (a step of 90 levels at x = 200): the join fails, a join away from it does not.
        var stepped = Image(width, height, (x, y) => (byte)(100 + (x * 7 + y * 13) % 11 + (x >= 200 ? 90 : 0)));
        Assert.IsTrue(SteamwovenSeam.ColumnRatio(stepped, width, height, 200, 10, 110, 60)!.Value > SteamwovenSeam.Limit, "a step at the join is a seam");
        Assert.IsTrue(SteamwovenSeam.ColumnRatio(stepped, width, height, 320, 10, 110, 60)!.Value < SteamwovenSeam.Limit, "no step, no seam");
        // The same for a horizontal join.
        var tall = Image(width, height, (x, y) => (byte)(100 + (x * 7 + y * 13) % 11 + (y >= 60 ? 90 : 0)));
        Assert.IsTrue(SteamwovenSeam.RowRatio(tall, width, height, 60, 10, 390, 20)!.Value > SteamwovenSeam.Limit);
        Assert.IsTrue(SteamwovenSeam.RowRatio(tall, width, height, 90, 10, 390, 20)!.Value < SteamwovenSeam.Limit);
    }

    [TestMethod]
    public void A_band_outside_the_frame_or_empty_rows_measure_nothing()
    {
        var image = Image(100, 100, (x, y) => 128);
        Assert.IsNull(SteamwovenSeam.ColumnRatio(image, 100, 100, 4, 0, 100, 60), "no room left of the join");
        Assert.IsNull(SteamwovenSeam.ColumnRatio(image, 100, 100, 50, 0, 100, 98), "no room beside the reference");
        Assert.IsNull(SteamwovenSeam.ColumnRatio(image, 100, 100, 50, 40, 40, 80), "no rows");
        Assert.IsNull(SteamwovenSeam.RowRatio(image, 100, 100, 50, 0, 100, 3), "no room above the reference");
        Assert.IsNull(SteamwovenSeam.ColumnRatio(image.AsSpan(1), 100, 100, 50, 0, 100, 80), "the bytes are the frame's");
        // A flat texture does not turn a flat join into an infinite ratio.
        Assert.AreEqual(0.0, SteamwovenSeam.ColumnRatio(image, 100, 100, 50, 0, 100, 80)!.Value, 1e-9);
    }

    private static byte[] Image(int width, int height, Func<int, int, byte> value)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var o = (y * width + x) * 4;
            var v = value(x, y);
            rgba[o] = v; rgba[o + 1] = v; rgba[o + 2] = v; rgba[o + 3] = 255;
        }
        return rgba;
    }
}
