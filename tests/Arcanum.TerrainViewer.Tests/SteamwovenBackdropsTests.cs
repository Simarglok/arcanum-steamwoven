using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2576, ARC-UI-2890, SWI-VER-5: the generated wood of each census family in the gaps of the unfitted column, and the seam ratio that measures its joins.</summary>
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
    public void The_fitted_column_has_no_wood_and_the_unfitted_one_splits_a_two_part_family_in_each_gap()
    {
        foreach (var (width, height) in Sizes)
        {
            Assert.AreEqual(0, SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.Inventory, SteamwovenHud.Plan(width, height)!).Count, $"[{width}x{height}] the fitted column leaves no gap");
            var plan = SteamwovenHud.Plan(width, height, SteamwovenLayoutOptions.ColumnOriginal)!;
            var column = plan.Column;
            var gaps = new[] { column.Window.GapAbove, column.Window.GapBelow }.Count(gap => gap.Height > 0 && gap.Width > 0);
            foreach (var family in Enum.GetValues<SteamwovenBackdropFamily>())
            {
                var pieces = SteamwovenBackdrops.Pieces(family, plan);
                Assert.AreEqual(gaps * (SteamwovenBackdrops.LeftUnits(family) is null ? 1 : 2), pieces.Count, $"[{width}x{height}] {family}");
                // Every piece lies inside the column and outside the window zone, and no two overlap.
                foreach (var piece in pieces)
                {
                    Assert.IsTrue(piece.Rect.X >= column.Column.X && piece.Rect.Right <= column.Column.Right && piece.Rect.Width > 0, $"[{width}x{height}] {family}");
                    Assert.IsTrue(piece.Rect.Bottom <= column.Window.Zone.Y || piece.Rect.Y >= column.Window.Zone.Bottom, $"[{width}x{height}] {family} lies outside the zone");
                }
            }
        }
    }

    [TestMethod]
    public void The_split_lies_at_the_left_part_of_the_window_in_the_zone()
    {
        foreach (var (width, height) in Sizes)
        {
            var plan = SteamwovenHud.Plan(width, height)!;
            var zone = plan.WindowZone;
            var unit = SteamwovenWindowZone.UnitScale(plan);
            foreach (var family in new[] { SteamwovenBackdropFamily.Inventory, SteamwovenBackdropFamily.Logbook, SteamwovenBackdropFamily.Reading })
            {
                var split = SteamwovenBackdrops.SplitX(family, plan)!.Value;
                var wanted = zone.X + SteamwovenBackdrops.LeftUnits(family)!.Value * unit;
                Assert.IsTrue(Math.Abs(split - wanted) <= 1.5, $"[{width}x{height}] {family} splits {split}, the window's left part ends at {wanted:F1}");
            }
        }
    }

    [TestMethod]
    public void Every_piece_has_a_window_art_for_its_tone_and_the_bars_read_it()
    {
        foreach (var family in Enum.GetValues<SteamwovenBackdropFamily>())
            for (var piece = 0; piece < (SteamwovenBackdrops.LeftUnits(family) is null ? 1 : 2); piece++)
                CollectionAssert.Contains(SteamwovenBackdrops.ToneArts.ToArray(), SteamwovenBackdrops.ToneArt(family, piece), $"{family} piece {piece}");
        // The census arts: the inventory (both halves of its family, ARC-UI-2763), the sheet, the logbook's side and book, the reading side bar, the map frame, the schematic base.
        Assert.AreEqual(221, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Inventory, 0));
        Assert.AreEqual(221, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Inventory, 1));
        Assert.AreEqual(264, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Logbook, 0));
        Assert.AreEqual(260, SteamwovenBackdrops.ToneArt(SteamwovenBackdropFamily.Logbook, 1));
        foreach (var number in SteamwovenBackdrops.ToneArts) CollectionAssert.Contains(AreaGameBarContent.ArtNumbers.ToArray(), number);
    }

    [TestMethod]
    public void The_grain_of_each_piece_starts_at_the_art_it_continues()
    {
        // The unfitted column at 16:10 has a gap above and below the window zone: the wood in them continues the window's own.
        var plan = SteamwovenHud.Plan(1920, 1200, SteamwovenLayoutOptions.ColumnOriginal)!;
        var zone = plan.WindowZone;
        var unit = SteamwovenWindowZone.UnitScale(plan);
        // A one-piece family continues the window's own wood: the grain starts at the window's top left, so at the zone's left edge it is at 0 and the piece above the zone has negative units.
        var map = SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.WorldMap, plan)[0];
        Assert.AreEqual((map.Rect.X - zone.X) / unit, map.OriginX, 1e-9);
        Assert.AreEqual((map.Rect.Y - zone.Y) / unit, map.OriginY, 1e-9);
        Assert.IsTrue(map.OriginX <= 0 && map.OriginY <= 0);
        // Each half of a two-part window is an art of its own: the right half's grain starts at the split.
        var parts = SteamwovenBackdrops.Pieces(SteamwovenBackdropFamily.Inventory, plan).Take(2).ToList();
        Assert.AreEqual((parts[0].Rect.X - zone.X) / unit, parts[0].OriginX, 1e-9);
        // ARC-UI-2763: the inventory family is one wood, so its right piece continues the left one's grain from the zone's left edge (the pack art starts at the split's 358 units).
        Assert.AreEqual((parts[1].Rect.X - zone.X) / unit, parts[1].OriginX, 1e-9);
        Assert.AreEqual(parts[0].OriginY, parts[1].OriginY, 1e-9);
        Assert.IsTrue(parts.All(part => part.Material == ChromeMaterial.Wood), "the census's wood under every family");
        // The logbook's and the reading window's halves are arts of their own: the right half's grain starts at the split.
        foreach (var family in new[] { SteamwovenBackdropFamily.Logbook, SteamwovenBackdropFamily.Reading })
            Assert.AreEqual(0.0, SteamwovenBackdrops.Pieces(family, plan)[1].OriginX, 1e-9, family.ToString());
    }

    private static byte[] Flat(int w, int h, (byte R, byte G, byte B) colour)
    {
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]) = (colour.R, colour.G, colour.B, 255);
        return pixels;
    }

    [TestMethod]
    public void The_inventory_family_is_one_wood_in_every_window_art_and_in_both_halves_of_the_backdrop()
    {
        // ARC-UI-2763: whatever the Legacy frame's own wood is (a flat dark and a flat light one here), the family's recipes paint the same wood, and the backdrop tuned to either gives the same board.
        string[] family = ["inventor.art", "pdoll.art", "loot.art", "barter.art", "barter_follower.art", "loot_pd.art", "barter_pd.art", "pd_cover4inven.art"];
        foreach (var file in family)
        {
            var recipe = InterfaceChromeRecipes.All.Single(r => r.File == file);
            Assert.IsTrue(recipe.FamilyWood, file);
            var dark = InterfaceChromeRenderer.Render(recipe with { Parts = [] }, 24, 24, Flat(24, 24, (50, 40, 30)), [], 0, 2);
            var light = InterfaceChromeRenderer.Render(recipe with { Parts = [] }, 24, 24, Flat(24, 24, (90, 70, 60)), [], 0, 2);
            CollectionAssert.AreEqual(dark, light, $"{file} paints the family's wood, not its Legacy frame's");
        }
        var pack = InterfaceChromeRecipes.All.Single(r => r.File == "inventor.art");
        Assert.AreEqual(InterfaceChromeRenderer.SteamwovenFamilySplitUnits, pack.OriginX, "the pack art continues the doll's grain");
        var left = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 60, 20, 1.8, 0, 0, pack, Flat(24, 24, (50, 40, 30)), 24, 24);
        var right = InterfaceChromeRenderer.RenderBoard(ChromeMaterial.Wood, ChromeSides.None, 60, 20, 1.8, 0, 0, pack, Flat(24, 24, (90, 70, 60)), 24, 24);
        CollectionAssert.AreEqual(left, right, "the backdrop's tone is the family's, whatever the tone art's pixels are");
        Assert.AreEqual(Arcanum.World.AreaInventoryArtLayout.PackX, SteamwovenBackdrops.LeftUnits(SteamwovenBackdropFamily.Inventory));
        foreach (var lens in new[] { "lns_bart.art", "lns_loot.art", "lns_papr.art" }) Assert.IsTrue(InterfaceChromeRecipes.All.Single(r => r.File == lens).FamilyWood, lens);
        // A recipe outside the family still reads its own frame's wood.
        var sheet = InterfaceChromeRecipes.All.First(r => r.File == "char_maint.art");
        Assert.IsFalse(sheet.FamilyWood);
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
