using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2461 (SWI-RUN-2, SWI-RUN-3, SWI-LAY-4, SWI-LAY-5): the kit set, its step choice, its composition into strips, caps, window backgrounds, plates and buttons, and the fallback when a part is missing. All with a synthetic kit.</summary>
[TestClass]
public sealed class SteamwovenKitTests
{
    private static SteamwovenHudPlan Plan(int width, int height) => SteamwovenHud.Plan(width, height, SteamwovenLayoutOptions.ColumnFitted)!;

    /// <summary>ARC-UI-2481: the master sizes of the hardware parts of the two HUD strips (kit.json rows <c>hardware.hud_*</c>, laid by <see cref="SteamwovenKitTable.Strips"/>).</summary>
    private static readonly (string Id, int Width, int Height)[] HardwareSizes =
    [
        ("hardware.hud_plate_left", 948, 150), ("hardware.hud_plate_right", 948, 150), ("hardware.hud_top_slot", 150, 144), ("hardware.hud_top_ring", 108, 108),
        ("hardware.hud_top_clock", 486, 132), ("hardware.hud_top_infinity", 108, 108), ("hardware.hud_top_hourglass", 78, 132), ("hardware.hud_top_rivet", 30, 30),
        ("hardware.hud_vial", 204, 558), ("hardware.hud_vial_r", 204, 558), ("hardware.hud_cylinder", 108, 324), ("hardware.hud_cylinder_r", 108, 324), ("hardware.hud_wires", 114, 144),
        ("hardware.hud_ring", 198, 192), ("hardware.hud_dial", 144, 144), ("hardware.hud_gem", 138, 144), ("hardware.hud_slot", 138, 144), ("hardware.hud_rail", 144, 48),
    ];

    /// <summary>A set holding every part of the table at the step's sizes (strips at the manifest's provisional 150 / 570, caps 120 x 150 / 120 x 570).</summary>
    private static SteamwovenKitSet Full(SteamwovenStep step, params string[] without)
    {
        int B(int master) => SteamwovenGrid.BakedLength(master, step);
        var parts = new List<SteamwovenKitPart>
        {
            new("board.inttop", B(2880), B(150)), new("board.intbotom", B(2880), B(570)),
            new("cap.top_strip_left", B(120), B(150)), new("cap.top_strip_right", B(120), B(150)),
            new("cap.bottom_strip_left", B(120), B(570)), new("cap.bottom_strip_right", B(120), B(570)),
            new("plate.char_but", B(120), B(120)), new("plate.char_but_hover", B(120), B(120)), new("plate.char_but_pressed", B(120), B(120)), new("state.char_on", B(120), B(120)),
            new("glyph.char_but", B(120), B(120)), new("glyph.char_but_lit", B(120), B(120)), new("plate.lilgrnbut", B(84), B(84)), new("glyph.fate_but", B(84), B(84)), new("glyph.sleep_but", B(84), B(84)),
            new("backdrop.mainmenuback", B(2880), B(2160)),
        };
        // ARC-UI-2481: the hardware layers of the two HUD strips are required parts of the strips.
        parts.AddRange(HardwareSizes.Select(hardware => new SteamwovenKitPart(hardware.Id, B(hardware.Width), B(hardware.Height))));
        // The inventory's right piece is shared by several windows (ARC-UI-3033): a part is added once.
        foreach (var window in SteamwovenKitTable.Windows.Values)
            foreach (var (id, width) in window.Pieces)
                if (parts.All(part => part.Id != id)) parts.Add(new(id, B(width), B(1440))); // the inventory's right half is shared by the loot window
        foreach (var sheet in SteamwovenKitTable.Sheets.Values) parts.Add(new(sheet.Part, B(sheet.MasterWidth), B(sheet.MasterHeight)));
        // ARC-UI-3038: the two-strip screens' strips (provisional 150 / 570 master px) and bodies.
        foreach (var screen in SteamwovenKitTable.Screens.Values)
        {
            // ARC-UI-3039: the screens share the two strips; a part is added once.
            foreach (var part in new SteamwovenKitPart[] { new(screen.TopStrip, B(2880), B(150)), new(screen.BottomStrip, B(2880), B(570)) })
                if (parts.All(existing => existing.Id != part.Id)) parts.Add(part);
            // ARC-UI-3041: a screen whose body is a game window has no body part of its own (the window's pieces are added above).
            if (screen.Body is { } body && parts.All(existing => existing.Id != body)) parts.Add(new(body, B(2880), B(1440)));
        }
        foreach (var overlay in new[] { SteamwovenKitTable.BarterDoll, SteamwovenKitTable.HeroDollCover }) parts.Add(new(overlay.Part, B(overlay.MasterWidth), B(overlay.MasterHeight)));
        return SteamwovenKitSet.FromParts(step, "/kit/" + SteamwovenKitSet.FolderName(step), parts.Where(part => !without.Contains(part.Id)));
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One, true)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds, true)]
    [DataRow(1920, 1080, SteamwovenStep.Half, true)]
    [DataRow(1280, 720, SteamwovenStep.Third, true)]
    [DataRow(2560, 1600, SteamwovenStep.One, false)]
    [DataRow(1920, 1200, SteamwovenStep.TwoThirds, false)]
    [DataRow(1366, 768, SteamwovenStep.Half, false)]
    [DataRow(3440, 1440, SteamwovenStep.TwoThirds, true)]
    [DataRow(1024, 768, SteamwovenStep.Half, false)]
    public void The_step_is_the_smallest_set_at_least_f_and_exact_only_at_a_set_s_own_scale(int width, int height, SteamwovenStep step, bool exact)
    {
        var layout = Plan(width, height).Layout;
        Assert.AreEqual(step, layout.Step);
        Assert.AreEqual(exact, layout.ExactStep);
    }

    [TestMethod]
    public void A_strip_is_the_art_and_its_two_caps_with_exact_draws_at_the_reference_size()
    {
        var plan = Plan(3840, 2160);
        var set = Full(SteamwovenStep.One);
        var top = SteamwovenKitComposer.Strip(plan, set, AreaBarStrip.Top).Value!;
        Assert.AreEqual(plan.Column.TopStrip, top.Board.Rect);
        Assert.AreEqual(2, top.Caps.Count);
        // The caps stand outside the strip, as high as it: the left one ends at the strip's left edge, the right one starts at its right edge.
        Assert.AreEqual(plan.Column.TopStrip.X, top.Caps[0].Rect.Right);
        Assert.AreEqual(plan.Column.TopStrip.Right, top.Caps[1].Rect.X);
        Assert.AreEqual(plan.Column.TopStrip.Height, top.Caps[0].Rect.Height);
        // The board is 150 px high at the step and the strip 148: not exact (the strip heights are the TRD's open exception); the cap is clipped by the window.
        Assert.IsFalse(top.Board.Exact);
        var bottom = SteamwovenKitComposer.Strip(plan, set, AreaBarStrip.Bottom).Value!;
        Assert.AreEqual(plan.Column.BottomStrip, bottom.Board.Rect);
        Assert.IsTrue(top.Caps.All(cap => cap.Clip.Width <= cap.Rect.Width));
    }

    [TestMethod]
    public void A_cap_beyond_the_window_is_clipped_to_it()
    {
        // 4:3 has no side: the caps lie wholly outside the window and nothing of them is drawn.
        var plan = Plan(1024, 768);
        var top = SteamwovenKitComposer.Strip(plan, Full(SteamwovenStep.Half), AreaBarStrip.Top).Value!;
        Assert.IsTrue(top.Caps.All(cap => cap.Clip.Width == 0));
        // A wide window shows them.
        var wide = SteamwovenKitComposer.Strip(Plan(1920, 1080), Full(SteamwovenStep.Half), AreaBarStrip.Top).Value!;
        Assert.IsTrue(wide.Caps.All(cap => cap.Clip.Width == cap.Rect.Width));
    }

    [TestMethod]
    [DataRow("board.inttop")]
    [DataRow("cap.top_strip_left")]
    [DataRow("cap.top_strip_right")]
    public void A_strip_with_any_part_missing_is_not_drawn_from_the_kit_and_names_it(string missingPart)
    {
        var result = SteamwovenKitComposer.Strip(Plan(1920, 1080), Full(SteamwovenStep.Half, missingPart), AreaBarStrip.Top);
        Assert.IsFalse(result.Complete);
        CollectionAssert.Contains(result.Missing.ToList(), missingPart);
        // The other strip is whole.
        Assert.IsTrue(SteamwovenKitComposer.Strip(Plan(1920, 1080), Full(SteamwovenStep.Half, missingPart), AreaBarStrip.Bottom).Complete);
    }

    [TestMethod]
    public void A_strip_art_of_the_wrong_width_is_missing()
    {
        var narrow = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("board.inttop", 1000, 75), new("cap.top_strip_left", 60, 75), new("cap.top_strip_right", 60, 75)]);
        var result = SteamwovenKitComposer.Strip(Plan(1920, 1080), narrow, AreaBarStrip.Top);
        Assert.IsFalse(result.Complete);
        Assert.IsTrue(result.Missing.Any(text => text.Contains("not 2880")));
    }

    [TestMethod]
    public void Hardware_layers_are_placed_by_master_position_with_rounded_edges_and_are_required()
    {
        var plan = Plan(1920, 1080);
        var table = new Dictionary<AreaBarStrip, SteamwovenKitStripSpec>
        {
            [AreaBarStrip.Top] = new("board.inttop", "cap.top_strip_left", "cap.top_strip_right", [new("hardware.socket", 300, 12)]),
        };
        var without = SteamwovenKitComposer.Strip(plan, Full(SteamwovenStep.Half), AreaBarStrip.Top, table);
        Assert.IsFalse(without.Complete);
        CollectionAssert.Contains(without.Missing.ToList(), "hardware.socket");
        var parts = Full(SteamwovenStep.Half).Ids.Select(id => Full(SteamwovenStep.Half).TryGet(id, out var p) ? p : default).Append(new SteamwovenKitPart("hardware.socket", 63, 63));
        var set = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", parts);
        var layer = SteamwovenKitComposer.Strip(plan, set, AreaBarStrip.Top, table).Value!.Layers.Single();
        var strip = plan.Column.TopStrip;
        // f = 0.5: 300 master px is 150 device px from the strip's left, 12 is 6 from its top; the part is 126 master px = 63 device px: exact.
        Assert.AreEqual(new PixelRect(strip.X + 150, strip.Y + 6, 63, 63), layer.Rect);
        Assert.IsTrue(layer.Exact);
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds)]
    [DataRow(1280, 720, SteamwovenStep.Third)]
    public void A_two_part_window_background_is_exactly_the_zone_split_at_1290_master_px(int width, int height, SteamwovenStep step)
    {
        var plan = Plan(width, height);
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        var pieces = SteamwovenKitComposer.Background(plan, Full(step), SteamwovenKitWindow.Inventory).Value!;
        Assert.AreEqual(2, pieces.Count);
        Assert.AreEqual(zone.X, pieces[0].Rect.X);
        Assert.AreEqual(zone.Right, pieces[1].Rect.Right);
        Assert.AreEqual(pieces[0].Rect.Right, pieces[1].Rect.X, "the pieces share the split");
        Assert.AreEqual(zone.Y, pieces[0].Rect.Y); Assert.AreEqual(zone.Bottom, pieces[0].Rect.Bottom);
        Assert.AreEqual(zone.X + (new Ratio(1290) * plan.Layout.Scale).RoundHalfUp(), pieces[0].Rect.Right);
        // At the exact scale the baked pieces are the device pixels.
        if (plan.Layout.ExactStep) Assert.IsTrue(pieces.All(piece => piece.Exact), "exact at the set's own scale");
    }

    [TestMethod]
    public void A_one_piece_window_fills_the_zone_and_a_window_with_a_missing_or_wrong_sized_piece_has_no_kit_background()
    {
        var plan = Plan(1920, 1080);
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        var whole = SteamwovenKitComposer.Background(plan, Full(SteamwovenStep.Half), SteamwovenKitWindow.Character).Value!.Single();
        Assert.AreEqual(zone, whole.Rect);
        var missing = SteamwovenKitComposer.Background(plan, Full(SteamwovenStep.Half, "backdrop.inventor"), SteamwovenKitWindow.Inventory);
        Assert.IsFalse(missing.Complete);
        CollectionAssert.Contains(missing.Missing.ToList(), "backdrop.inventor");
        var wrong = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("backdrop.char_maint", 1440, 700)]);
        Assert.IsTrue(SteamwovenKitComposer.Background(plan, wrong, SteamwovenKitWindow.Character).Missing.Single().Contains("wrong size"));
    }

    [TestMethod]
    public void The_map_view_the_continent_view_and_the_town_map_are_one_window_of_the_kit()
    {
        // ARC-UI-3036 (census G12 to G14): the three views of the map window are interface mode 5 (WorldMap, AreaWorldMapKind Map, Continent and Town are kinds inside it), so one kit window serves all of them.
        Assert.AreEqual(SteamwovenKitWindow.WorldMap, SteamwovenKitComposer.WindowOf(InterfaceMode.WorldMap));
        Assert.AreEqual(SteamwovenKitWindow.WorldMap, SteamwovenKitComposer.WindowOf([InterfaceMode.WorldMap]));
        Assert.AreEqual(SteamwovenKitWindow.WorldMap, SteamwovenKitComposer.WindowOf([InterfaceMode.Game, InterfaceMode.WorldMap]));
        Assert.AreEqual(SteamwovenBackdropFamily.WorldMap, SteamwovenBackdrops.FamilyOf(InterfaceMode.WorldMap));
        // A dialog over the map (a script opens one while the map is up) takes the window's background away with it.
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.WorldMap, InterfaceMode.Dialog]));
        var spec = SteamwovenKitTable.Windows[SteamwovenKitWindow.WorldMap];
        CollectionAssert.AreEqual(new[] { ("backdrop.mapmain", 2880) }, spec.Pieces.ToArray());
        // The part and its art are the census rows of kit.json (G12 to G14, art 138).
        var kit = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(FindKit())).RootElement;
        var part = kit.GetProperty("parts").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "backdrop.mapmain");
        CollectionAssert.AreEqual(new[] { "G12", "G13", "G14" }, part.GetProperty("used_by").EnumerateArray().Select(u => u.GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { "138" }, part.GetProperty("source_arts").EnumerateArray().Select(u => u.GetString()).ToArray());
    }

    [TestMethod]
    public void Every_covered_window_adds_up_to_the_composition_width()
    {
        foreach (var (window, spec) in SteamwovenKitTable.Windows)
            Assert.AreEqual(SteamwovenKitTable.CompositionMasterWidth, spec.Pieces.Sum(piece => piece.MasterWidth), window.ToString());
    }

    [TestMethod]
    public void The_window_of_a_stack_is_the_nearest_covered_window_and_others_have_none()
    {
        Assert.AreEqual(SteamwovenKitWindow.Inventory, SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory]));
        Assert.AreEqual(SteamwovenKitWindow.Logbook, SteamwovenKitComposer.WindowOf([InterfaceMode.Logbook]));
        Assert.AreEqual(SteamwovenKitWindow.Inventory, SteamwovenKitComposer.WindowOf([InterfaceMode.InventoryKind5]));
        Assert.AreEqual(SteamwovenKitWindow.Inventory, SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.InventoryKind6]));
        // ARC-UI-3032: the loot, steal and See Contents windows (census G02, G04) have a kit background of their own, over an inventory too; the barter window (ARC-UI-3033) has its own two, picked by the frame's state in the other overload.
        Assert.AreEqual(SteamwovenKitWindow.Loot, SteamwovenKitComposer.WindowOf([InterfaceMode.Loot]));
        Assert.AreEqual(SteamwovenKitWindow.Loot, SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.Steal]));
        Assert.AreEqual(SteamwovenKitWindow.Loot, SteamwovenKitComposer.WindowOf([InterfaceMode.Loot, InterfaceMode.QuantityPicker]));
        Assert.AreEqual(SteamwovenKitWindow.Barter, SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.Barter]));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Loot, InterfaceMode.Dialog]));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.Dialog]));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([]));
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(1280, 720, SteamwovenStep.Third)]
    public void The_barter_window_is_the_merchant_s_side_and_the_inventory_split_at_1290_and_its_follower_variant_swaps_only_the_left_piece(int width, int height, SteamwovenStep step)
    {
        // ARC-UI-3033 (G03).
        var plan = Plan(width, height);
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        var plain = SteamwovenKitComposer.Background(plan, Full(step), SteamwovenKitWindow.Barter).Value!;
        CollectionAssert.AreEqual(new[] { "backdrop.barter", "backdrop.inventor" }, plain.Select(draw => draw.PartId).ToList());
        Assert.AreEqual(zone.X, plain[0].Rect.X);
        Assert.AreEqual(zone.Right, plain[1].Rect.Right);
        Assert.AreEqual(plain[0].Rect.Right, plain[1].Rect.X, "the pieces share the split");
        Assert.AreEqual(zone.X + (new Ratio(1290) * plan.Layout.Scale).RoundHalfUp(), plain[0].Rect.Right);
        var follower = SteamwovenKitComposer.Background(plan, Full(step), SteamwovenKitWindow.BarterFollower).Value!;
        CollectionAssert.AreEqual(new[] { "backdrop.barter_follower", "backdrop.inventor" }, follower.Select(draw => draw.PartId).ToList());
        Assert.AreEqual(plain[0].Rect, follower[0].Rect);
        Assert.AreEqual(plain[1].Rect, follower[1].Rect);
    }

    [TestMethod]
    public void A_barter_window_with_a_missing_piece_has_no_kit_background_and_the_other_variant_is_not_held_back()
    {
        var plan = Plan(1920, 1080);
        var noFollower = Full(SteamwovenStep.Half, "backdrop.barter_follower");
        Assert.IsFalse(SteamwovenKitComposer.Background(plan, noFollower, SteamwovenKitWindow.BarterFollower).Complete);
        CollectionAssert.Contains(SteamwovenKitComposer.Background(plan, noFollower, SteamwovenKitWindow.BarterFollower).Missing.ToList(), "backdrop.barter_follower");
        Assert.IsTrue(SteamwovenKitComposer.Background(plan, noFollower, SteamwovenKitWindow.Barter).Complete);
        // The hero's inventory piece is shared with the inventory window: without it neither window has a kit background.
        var noInventor = Full(SteamwovenStep.Half, "backdrop.inventor");
        Assert.IsFalse(SteamwovenKitComposer.Background(plan, noInventor, SteamwovenKitWindow.Barter).Complete);
        Assert.IsFalse(SteamwovenKitComposer.Background(plan, noInventor, SteamwovenKitWindow.BarterFollower).Complete);
        var wrong = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("backdrop.barter", 645, 700), new("backdrop.inventor", 795, 720)]);
        Assert.IsTrue(SteamwovenKitComposer.Background(plan, wrong, SteamwovenKitWindow.Barter).Missing.Single().Contains("wrong size"));
    }

    [TestMethod]
    public void The_barter_window_is_the_plain_or_follower_variant_by_the_merchant_s_art_and_none_out_of_the_zone()
    {
        // ARC-UI-3033: mode 1; art 825 (the follower's cycle buttons) picks the follower variant; a frame out of the window zone (docked in the opening shop, or the diagnostic look) has none.
        Assert.AreEqual(SteamwovenKitWindow.Barter, SteamwovenKitComposer.WindowOf([InterfaceMode.Barter]));
        Assert.AreEqual(SteamwovenKitWindow.Barter, SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.Barter], new(AreaBarterArtLayout.BarterArt, true)));
        Assert.AreEqual(SteamwovenKitWindow.BarterFollower, SteamwovenKitComposer.WindowOf([InterfaceMode.Dialog, InterfaceMode.Barter], new(AreaBarterArtLayout.FollowerArt, true)));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Barter], new(AreaBarterArtLayout.BarterArt, false)));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Barter], new(AreaBarterArtLayout.FollowerArt, false)));
        // A dialog on top of the barter window, or the loot window over it, has none; the barter window over a loot window has its own.
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Barter, InterfaceMode.Dialog], new(AreaBarterArtLayout.BarterArt, true)));
        Assert.AreEqual(SteamwovenKitWindow.Barter, SteamwovenKitComposer.WindowOf([InterfaceMode.Loot, InterfaceMode.Barter], new(AreaBarterArtLayout.BarterArt, true)));
        // The state does not touch the other windows.
        Assert.AreEqual(SteamwovenKitWindow.Inventory, SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory], new(AreaBarterArtLayout.FollowerArt, true)));
    }

    [TestMethod]
    public void A_barter_overlay_is_the_part_at_its_size_at_the_step_and_has_unit_sizes_from_the_master_size()
    {
        // ARC-UI-3033: the merchant's doll 1290 x 876 and the hero's doll cover 1230 x 1440 master px; a unit is 3.6 master px.
        var set = Full(SteamwovenStep.Half);
        Assert.IsNotNull(SteamwovenKitComposer.Overlay(set, SteamwovenKitTable.BarterDoll));
        Assert.IsNotNull(SteamwovenKitComposer.Overlay(set, SteamwovenKitTable.HeroDollCover));
        Assert.IsNull(SteamwovenKitComposer.Overlay(Full(SteamwovenStep.Half, "backdrop.barter_pd"), SteamwovenKitTable.BarterDoll));
        Assert.IsNotNull(SteamwovenKitComposer.Overlay(Full(SteamwovenStep.Half, "backdrop.barter_pd"), SteamwovenKitTable.HeroDollCover), "an overlay does not hold the other back");
        var wrong = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("backdrop.barter_pd", 645, 400)]);
        Assert.IsNull(SteamwovenKitComposer.Overlay(wrong, SteamwovenKitTable.BarterDoll));
        Assert.AreEqual(1290 / 3.6, SteamwovenKitTable.BarterDoll.UnitWidth, 1e-9);
        Assert.AreEqual(400.0, SteamwovenKitTable.HeroDollCover.UnitHeight, 1e-9);
        CollectionAssert.AreEqual(new[] { "backdrop.barter_pd", "backdrop.pd_cover4inven" }, SteamwovenKitTable.OverlaysOf(SteamwovenKitWindow.Barter).Select(overlay => overlay.Part).ToList());
        CollectionAssert.AreEqual(SteamwovenKitTable.OverlaysOf(SteamwovenKitWindow.Barter).ToList(), SteamwovenKitTable.OverlaysOf(SteamwovenKitWindow.BarterFollower).ToList());
        Assert.AreEqual(0, SteamwovenKitTable.OverlaysOf(SteamwovenKitWindow.Inventory).Count);
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(1280, 1024, SteamwovenStep.Third)]
    public void The_loot_background_is_the_loot_part_left_and_the_inventory_part_right_tiling_the_zone(int width, int height, SteamwovenStep step)
    {
        var plan = Plan(width, height);
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        var result = SteamwovenKitComposer.Background(plan, Full(step), SteamwovenKitWindow.Loot);
        var pieces = result.Value!;
        CollectionAssert.AreEqual(new[] { "backdrop.loot", "backdrop.inventor" }, pieces.Select(piece => piece.PartId).ToArray());
        Assert.AreEqual(zone.X, pieces[0].Rect.X); Assert.AreEqual(zone.Right, pieces[1].Rect.Right);
        Assert.AreEqual(pieces[0].Rect.Right, pieces[1].Rect.X, "no gap at the split");
        Assert.AreEqual(zone.X + (new Ratio(1290) * plan.Layout.Scale).RoundHalfUp(), pieces[0].Rect.Right);
        Assert.IsTrue(pieces.All(piece => piece.Rect.Y == zone.Y && piece.Rect.Bottom == zone.Bottom));
        // The right half is the inventory's, one part for both windows.
        Assert.AreEqual("backdrop.inventor", SteamwovenKitComposer.Background(plan, Full(step), SteamwovenKitWindow.Inventory).Value![1].PartId);
    }

    [TestMethod]
    public void A_loot_background_with_a_missing_or_wrong_sized_piece_is_missing_whole_and_the_inventory_is_not_held_back()
    {
        var plan = Plan(1920, 1080);
        var noLoot = SteamwovenKitComposer.Background(plan, Full(SteamwovenStep.Half, "backdrop.loot"), SteamwovenKitWindow.Loot);
        Assert.IsFalse(noLoot.Complete);
        CollectionAssert.Contains(noLoot.Missing.ToList(), "backdrop.loot");
        Assert.IsTrue(SteamwovenKitComposer.Background(plan, Full(SteamwovenStep.Half, "backdrop.loot"), SteamwovenKitWindow.Inventory).Complete);
        var wrong = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("backdrop.loot", 400, 700), new("backdrop.inventor", 795, 720)]);
        Assert.IsTrue(SteamwovenKitComposer.Background(plan, wrong, SteamwovenKitWindow.Loot).Missing.Single().Contains("wrong size"));
    }

    [TestMethod]
    public void The_loot_window_draws_no_background_art_over_a_kit_background_and_both_of_them_without()
    {
        CollectionAssert.AreEqual(new[] { 234, 221 }, SteamwovenKitComposer.LootBackgroundArts(false).ToArray());
        Assert.AreEqual(0, SteamwovenKitComposer.LootBackgroundArts(true).Count);
    }

    [TestMethod]
    public void A_doll_cover_is_the_kit_part_of_the_table_size_and_the_original_art_stays_without_it()
    {
        SteamwovenKitSet With(SteamwovenStep step, params (string Id, int Width, int Height)[] parts) =>
            SteamwovenKitSet.FromParts(step, "/kit/" + SteamwovenKitSet.FolderName(step), parts.Select(part => new SteamwovenKitPart(part.Id, SteamwovenGrid.BakedLength(part.Width, step), SteamwovenGrid.BakedLength(part.Height, step))));
        var set = With(SteamwovenStep.Half, ("backdrop.loot_pd", 1290, 960), ("backdrop.pd_cover4inven", 1230, 1440));
        Assert.AreEqual("backdrop.loot_pd", SteamwovenKitComposer.Cover(set, 349)?.Id);
        Assert.AreEqual("backdrop.pd_cover4inven", SteamwovenKitComposer.Cover(set, 341)?.Id);
        Assert.IsNull(SteamwovenKitComposer.Cover(With(SteamwovenStep.Half, ("backdrop.pd_cover4inven", 1230, 1440)), 349), "a missing cover");
        Assert.IsNull(SteamwovenKitComposer.Cover(With(SteamwovenStep.Half, ("backdrop.loot_pd", 1290, 1440)), 349), "a wrong sized cover");
        Assert.IsNull(SteamwovenKitComposer.Cover(set, 234), "an art that has no cover");
        // The covers are sized in original units from their master size (2880 over 800 units): 358.3 x 266.7 and 341.7 x 400.
        var (w, h) = SteamwovenKitComposer.CoverUnits(SteamwovenKitTable.Covers[349]);
        Assert.AreEqual(358.33, w, .01); Assert.AreEqual(266.67, h, .01);
        Assert.AreEqual(400.0, SteamwovenKitComposer.CoverUnits(SteamwovenKitTable.Covers[341]).Height, .001);
        // A set at another step holds the cover at that step's size.
        Assert.IsNotNull(SteamwovenKitComposer.Cover(With(SteamwovenStep.Third, ("backdrop.loot_pd", 1290, 960)), 349));
    }

    [TestMethod]
    public void The_hero_s_sheet_and_the_NPC_sheet_of_a_dialog_are_one_window_with_the_kit_s_character_background()
    {
        // ARC-UI-3035 AC1 (census G10): the NPC sheet of a dialog opens as the sheet's interface mode (the viewer's OpenNpcSheet is the window of mode Character, read-only), so it is the kit's character window like the hero's sheet.
        Assert.AreEqual(SteamwovenKitWindow.Character, SteamwovenKitComposer.WindowOf(InterfaceMode.Character));
        Assert.AreEqual(SteamwovenKitWindow.Character, SteamwovenKitComposer.WindowOf([InterfaceMode.Character]));
        Assert.AreEqual(SteamwovenKitWindow.Character, SteamwovenKitComposer.WindowOf([InterfaceMode.Game, InterfaceMode.Character]));
        // A dialog left under the sheet does not hide the window; a dialog over it does (the world stays in view, no kit background).
        Assert.AreEqual(SteamwovenKitWindow.Character, SteamwovenKitComposer.WindowOf([InterfaceMode.Dialog, InterfaceMode.Character]));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Character, InterfaceMode.Dialog]));
        CollectionAssert.AreEqual(new[] { "backdrop.char_maint" }, SteamwovenKitTable.Windows[SteamwovenKitWindow.Character].Pieces.Select(piece => piece.Part).ToArray());
    }

    [TestMethod]
    public void The_four_pages_have_a_kit_sheet_of_their_own_panel_size_and_the_page_arts_the_viewer_draws()
    {
        // ARC-UI-3035 AC2: the keys are the arts the page views draw as their own panels (29 skills, 30 tech, 31 spells, 567 schemes), at the panel's 235 x 325 units (3.6 master px each).
        CollectionAssert.AreEquivalent(new[] { AreaCharacterSheetPagesLayout.SkillsBackgroundArt, AreaCharacterSheetPagesLayout.TechBackgroundArt, AreaCharacterSheetSpellsLayout.BackgroundArt, AreaCharacterSheetSchemesLayout.BackgroundArt },
            SteamwovenKitTable.Sheets.Keys.ToArray());
        var (_, _, width, height) = AreaCharacterSheetArtLayout.Panel;
        foreach (var sheet in SteamwovenKitTable.Sheets.Values)
            Assert.AreEqual((width * 3.6, height * 3.6), ((double)sheet.MasterWidth, (double)sheet.MasterHeight), sheet.Part);
    }

    [TestMethod]
    [DataRow(29, "sheet.skills_window")]
    [DataRow(30, "sheet.disciplines_window")]
    [DataRow(31, "sheet.char_spells")]
    [DataRow(567, "sheet.scheme_rot")]
    public void A_page_takes_its_kit_sheet_only_when_the_set_has_it_at_the_table_s_size(int art, string id)
    {
        foreach (var step in new[] { SteamwovenStep.One, SteamwovenStep.Half, SteamwovenStep.Third })
        {
            Assert.AreEqual(id, SteamwovenKitComposer.Sheet(Full(step), art)?.Id);
            // Missing: the page keeps its own art.
            Assert.IsNull(SteamwovenKitComposer.Sheet(Full(step, id), art));
        }
        // Wrong size: the page keeps its own art.
        var wrong = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new(id, 100, 100)]);
        Assert.IsNull(SteamwovenKitComposer.Sheet(wrong, art));
        // An art that is no page panel has no sheet.
        Assert.IsNull(SteamwovenKitComposer.Sheet(Full(SteamwovenStep.Half), 22));
    }

    [TestMethod]
    public void A_plate_needs_the_composition_size_at_the_step()
    {
        Assert.IsNotNull(SteamwovenKitComposer.Plate(Full(SteamwovenStep.Half), 329));
        Assert.IsNull(SteamwovenKitComposer.Plate(Full(SteamwovenStep.Half, "backdrop.mainmenuback"), 329));
        Assert.IsNull(SteamwovenKitComposer.Plate(Full(SteamwovenStep.Half), 12345));
        var wrong = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("backdrop.mainmenuback", 1440, 900)]);
        Assert.IsNull(SteamwovenKitComposer.Plate(wrong, 329));
    }

    [TestMethod]
    public void A_button_is_its_face_its_glyph_and_the_overlay_of_its_look_and_none_without_the_face()
    {
        var set = Full(SteamwovenStep.Half);
        static string[] Ids(IReadOnlyList<SteamwovenKitPart> layers) => [.. layers.Select(part => part.Id)];
        CollectionAssert.AreEqual(new[] { "plate.char_but", "glyph.char_but" }, Ids(SteamwovenKitComposer.ButtonLayers(set, 169, AreaBarButtonLook.Idle)));
        // The overlay of the look lies over the glyph (ARC-UI-3096).
        CollectionAssert.AreEqual(new[] { "plate.char_but", "glyph.char_but", "plate.char_but_hover" }, Ids(SteamwovenKitComposer.ButtonLayers(set, 169, AreaBarButtonLook.Hovered)));
        CollectionAssert.AreEqual(new[] { "plate.char_but", "glyph.char_but", "plate.char_but_pressed" }, Ids(SteamwovenKitComposer.ButtonLayers(set, 169, AreaBarButtonLook.Down)));
        // The lit face of a light button is its own part and takes its button's lit glyph (the lit art's own symbol, ARC-UI-2482).
        CollectionAssert.AreEqual(new[] { "state.char_on", "glyph.char_but_lit" }, Ids(SteamwovenKitComposer.ButtonLayers(set, 561, AreaBarButtonLook.Idle)));
        // Art 137 stands under two buttons with their own symbols: the glyph is told by the button, and none is drawn without it.
        CollectionAssert.AreEqual(new[] { "plate.lilgrnbut", "glyph.fate_but" }, Ids(SteamwovenKitComposer.ButtonLayers(set, 137, AreaBarButtonLook.Idle, AreaBarButton.FatePoints)));
        CollectionAssert.AreEqual(new[] { "plate.lilgrnbut", "glyph.sleep_but" }, Ids(SteamwovenKitComposer.ButtonLayers(set, 137, AreaBarButtonLook.Idle, AreaBarButton.Sleep)));
        Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(set, 137, AreaBarButtonLook.Idle).Count);
        // No face (a button the kit has not made yet, or an art that is no button): the generated or legacy art stays.
        Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(set, 472, AreaBarButtonLook.Idle).Count);
        Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(set, 1, AreaBarButtonLook.Idle).Count);
        // An overlay of another size is not drawn.
        var odd = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("plate.char_but", 60, 60), new("glyph.char_but", 60, 60), new("plate.char_but_hover", 30, 30)]);
        Assert.AreEqual(2, SteamwovenKitComposer.ButtonLayers(odd, 169, AreaBarButtonLook.Hovered).Count);
    }

    [TestMethod]
    public void A_face_without_its_glyph_or_with_one_of_another_size_is_not_drawn_from_the_kit()
    {
        // ARC-UI-3096 (AC1): the plate alone would be a button without its symbol, so the whole button stays the generated or legacy art.
        var without = Full(SteamwovenStep.Half, "glyph.char_but");
        foreach (var look in new[] { AreaBarButtonLook.Idle, AreaBarButtonLook.Hovered, AreaBarButtonLook.Down })
        {
            Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(without, 169, look).Count, look.ToString());
        }
        // The lit face needs the lit glyph (ARC-UI-2482), not the idle one.
        var withoutLit = Full(SteamwovenStep.Half, "glyph.char_but_lit");
        foreach (var look in new[] { AreaBarButtonLook.Idle, AreaBarButtonLook.Hovered, AreaBarButtonLook.Down })
        {
            Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(withoutLit, 561, look).Count, look.ToString());
            Assert.AreEqual(2, SteamwovenKitComposer.ButtonLayers(withoutLit, 169, AreaBarButtonLook.Idle).Count);
        }
        var odd = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("plate.char_but", 60, 60), new("glyph.char_but", 30, 30)]);
        Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(odd, 169, AreaBarButtonLook.Idle).Count);
    }

    [TestMethod]
    public void Every_button_face_has_a_glyph_by_its_art_or_by_its_button()
    {
        foreach (var art in SteamwovenKitTable.ButtonFaces.Keys.Where(art => art != 137)) Assert.IsNotNull(SteamwovenKitTable.GlyphOf(null, art), art.ToString());
        Assert.IsNull(SteamwovenKitTable.GlyphOf(null, 137));
        var shared = AreaGameBarLayout.Buttons.Where(spec => spec.Art == 137).ToList();
        CollectionAssert.AreEquivalent(new[] { AreaBarButton.FatePoints, AreaBarButton.Sleep }, shared.Select(spec => spec.Button).ToArray());
        foreach (var spec in shared) Assert.IsNotNull(SteamwovenKitTable.GlyphOf(spec.Button, 137), spec.Button.ToString());
        // A lit face shows the lit glyph of its idle face's symbol (ARC-UI-2482); the glyph names are safe file names.
        Assert.AreEqual(SteamwovenKitTable.GlyphOf(null, 169) + "_lit", SteamwovenKitTable.GlyphOf(null, 561));
        Assert.IsTrue(SteamwovenKitTable.ButtonGlyphs.Values.Concat(SteamwovenKitTable.SharedArtGlyphs.Values).All(SteamwovenKitSet.ValidId));
    }

    private static SteamwovenFullScreenPlan Screen(int width, int height) => SteamwovenFullScreen.Plan(width, height, SteamwovenLayoutOptions.ColumnOriginal)!;

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(1920, 1200, SteamwovenStep.Half)]
    [DataRow(1280, 1024, SteamwovenStep.Third)]
    [DataRow(1024, 768, SteamwovenStep.Third)]
    public void A_two_strip_screen_is_the_top_strip_the_body_and_the_bottom_strip_tiling_the_plate_with_the_body_exactly_its_400_units(int width, int height, SteamwovenStep step)
    {
        var plan = Screen(width, height);
        Assert.AreEqual(step, plan.Layout.Step, "the set is the step the plan picks for this size");
        var set = Full(step);
        var composed = SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.SaveLoad);
        Assert.IsTrue(composed.Complete, string.Join(", ", composed.Missing));
        var screen = composed.Value!;
        CollectionAssert.AreEqual(new[] { "board.charcreatetopbar", "backdrop.saveloadbackground", "board.charcreatebottombar" }, screen.Draws.Select(draw => draw.PartId).ToArray());
        var zone = plan.Zone;
        Assert.IsTrue(screen.Draws.All(draw => draw.Rect.X == zone.X && draw.Rect.Right == zone.Right), "each part spans the plate's width");
        Assert.AreEqual(zone.Y, screen.Top.Rect.Y); Assert.AreEqual(zone.Bottom, screen.Bottom.Rect.Bottom);
        Assert.AreEqual(screen.Top.Rect.Bottom, screen.Body.Rect.Y, "no gap between the top strip and the body");
        Assert.AreEqual(screen.Body.Rect.Bottom, screen.Bottom.Rect.Y, "no gap between the body and the bottom strip");
        Assert.AreEqual(zone.Y + (int)Math.Round(41 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Y);
        Assert.AreEqual(zone.Y + (int)Math.Round(441 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Bottom);
        Assert.IsTrue(screen.Draws.All(draw => draw.Clip == draw.Rect), "nothing is clipped");
    }

    [TestMethod]
    public void At_the_exact_scale_the_body_is_the_baked_pixels_and_the_strips_are_scaled_into_their_rows()
    {
        // 3840 x 2160: 3.6 device px per unit, the step 1: the body 2880 x 1440 is the device pixels; the strips are 150 and 570 master px baked into 148 and 572 device px rows (SWI-LAY-7's open exception): scaled.
        var plan = Screen(3840, 2160);
        var screen = SteamwovenKitComposer.Screen(plan, Full(SteamwovenStep.One), SteamwovenKitScreen.SaveLoad).Value!;
        Assert.AreEqual(2880, screen.Body.Rect.Width); Assert.AreEqual(1440, screen.Body.Rect.Height);
        Assert.IsTrue(screen.Body.Exact);
        Assert.AreEqual(148, screen.Top.Rect.Height); Assert.AreEqual(572, screen.Bottom.Rect.Height);
        Assert.IsFalse(screen.Top.Exact); Assert.IsFalse(screen.Bottom.Exact);
    }

    [TestMethod]
    [DataRow("board.charcreatetopbar")]
    [DataRow("backdrop.saveloadbackground")]
    [DataRow("board.charcreatebottombar")]
    public void A_two_strip_screen_with_any_part_missing_has_no_kit_screen_and_names_the_part(string missingPart)
    {
        var plan = Screen(1920, 1080);
        var result = SteamwovenKitComposer.Screen(plan, Full(SteamwovenStep.Half, missingPart), SteamwovenKitScreen.SaveLoad);
        Assert.IsFalse(result.Complete);
        CollectionAssert.AreEqual(new[] { missingPart }, result.Missing.ToArray());
    }

    [TestMethod]
    public void A_strip_not_2880_wide_or_a_body_of_another_size_makes_the_screen_missing_whole()
    {
        var plan = Screen(1920, 1080);
        SteamwovenKitSet Set(int stripWidth, int bodyHeight) => SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2",
            [new("board.charcreatetopbar", stripWidth, 75), new("board.charcreatebottombar", 1440, 285), new("backdrop.saveloadbackground", 1440, bodyHeight)]);
        var narrow = SteamwovenKitComposer.Screen(plan, Set(1000, 720), SteamwovenKitScreen.SaveLoad);
        Assert.IsFalse(narrow.Complete);
        Assert.IsTrue(narrow.Missing.Single().Contains("board.charcreatetopbar") && narrow.Missing.Single().Contains("not 2880 px wide"));
        var narrowBottom = SteamwovenKitComposer.Screen(plan, SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("board.charcreatetopbar", 1440, 75), new("board.charcreatebottombar", 700, 285), new("backdrop.saveloadbackground", 1440, 720)]), SteamwovenKitScreen.SaveLoad);
        Assert.IsTrue(!narrowBottom.Complete && narrowBottom.Missing.Single().Contains("board.charcreatebottombar") && narrowBottom.Missing.Single().Contains("not 2880 px wide"));
        var low = SteamwovenKitComposer.Screen(plan, Set(1440, 700), SteamwovenKitScreen.SaveLoad);
        Assert.IsFalse(low.Complete);
        Assert.IsTrue(low.Missing.Single().Contains("backdrop.saveloadbackground") && low.Missing.Single().Contains("wrong size"));
        // The strips' heights are not checked (the provisional 150 and 570 master px move with #2927), only their width.
        Assert.IsTrue(SteamwovenKitComposer.Screen(plan, Set(1440, 720), SteamwovenKitScreen.SaveLoad).Complete);
    }

    [TestMethod]
    public void The_save_and_load_screen_draws_none_of_its_own_backdrop_arts_over_a_kit_screen_and_all_three_without()
    {
        CollectionAssert.AreEqual(new[] { 336, 335, 745 }, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.SaveLoad, false).ToArray());
        Assert.AreEqual(0, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.SaveLoad, true).Count);
        // The arts the table names are the screen's own (strips 336 and 335, the background 745), as the viewer draws them.
        CollectionAssert.AreEquivalent(new[] { AreaSaveListArtLayout.TopStripArt, AreaSaveListArtLayout.BottomStripArt, AreaSaveListArtLayout.BackgroundArt }, SteamwovenKitTable.Screens[SteamwovenKitScreen.SaveLoad].OwnArts.ToArray());
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(1280, 1024, SteamwovenStep.Third)]
    public void The_premade_browser_is_the_same_two_strips_around_its_own_pregen_body_tiling_the_plate(int width, int height, SteamwovenStep step)
    {
        // ARC-UI-3039 (census M07): the strips are the save screen's (the creation family shares them), the body is the pre-made background.
        var plan = Screen(width, height);
        var composed = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.PremadeBrowser);
        Assert.IsTrue(composed.Complete, string.Join(", ", composed.Missing));
        var screen = composed.Value!;
        CollectionAssert.AreEqual(new[] { "board.charcreatetopbar", "backdrop.pregencharacterbase", "board.charcreatebottombar" }, screen.Draws.Select(draw => draw.PartId).ToArray());
        Assert.AreEqual(plan.Zone.Y, screen.Top.Rect.Y); Assert.AreEqual(plan.Zone.Bottom, screen.Bottom.Rect.Bottom);
        Assert.AreEqual(screen.Top.Rect.Bottom, screen.Body.Rect.Y); Assert.AreEqual(screen.Body.Rect.Bottom, screen.Bottom.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(41 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(441 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Bottom);
        // The save screen's plan over the same set differs only in the body part.
        var save = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.SaveLoad).Value!;
        Assert.AreEqual(save.Top, screen.Top); Assert.AreEqual(save.Bottom, screen.Bottom);
        Assert.AreEqual(save.Body.Rect, screen.Body.Rect); Assert.AreNotEqual(save.Body.PartId, screen.Body.PartId);
    }

    [TestMethod]
    [DataRow("board.charcreatetopbar")]
    [DataRow("backdrop.pregencharacterbase")]
    [DataRow("board.charcreatebottombar")]
    public void The_premade_browser_with_any_part_missing_has_no_kit_screen_and_names_the_part(string missingPart)
    {
        var plan = Screen(1920, 1080);
        var set = Full(SteamwovenStep.Half, missingPart);
        var result = SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.PremadeBrowser);
        Assert.IsFalse(result.Complete);
        CollectionAssert.AreEqual(new[] { missingPart }, result.Missing.ToArray());
        // The save screen is held back only by a strip they share, not by the pre-made body.
        Assert.AreEqual(missingPart == "backdrop.pregencharacterbase", SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.SaveLoad).Complete);
    }

    [TestMethod]
    public void The_premade_browser_draws_none_of_its_own_backdrop_arts_over_a_kit_screen_and_all_three_without()
    {
        CollectionAssert.AreEquivalent(new[] { 336, 335, 766 }, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.PremadeBrowser, false).ToArray());
        Assert.AreEqual(0, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.PremadeBrowser, true).Count);
        CollectionAssert.AreEquivalent(new[] { AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt, AreaPremadeBrowserArtLayout.BackgroundArt }, SteamwovenKitTable.Screens[SteamwovenKitScreen.PremadeBrowser].OwnArts.ToArray());
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(1280, 1024, SteamwovenStep.Third)]
    public void The_new_character_screen_is_the_same_two_strips_around_its_own_create_body_tiling_the_plate(int width, int height, SteamwovenStep step)
    {
        // ARC-UI-3040 (census M08): the strips are the creation family's (shared with the save and pre-made screens), the body is the new character background.
        var plan = Screen(width, height);
        var composed = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.NewCharacter);
        Assert.IsTrue(composed.Complete, string.Join(", ", composed.Missing));
        var screen = composed.Value!;
        CollectionAssert.AreEqual(new[] { "board.charcreatetopbar", "backdrop.createcharacterbase", "board.charcreatebottombar" }, screen.Draws.Select(draw => draw.PartId).ToArray());
        Assert.AreEqual(plan.Zone.Y, screen.Top.Rect.Y); Assert.AreEqual(plan.Zone.Bottom, screen.Bottom.Rect.Bottom);
        Assert.AreEqual(screen.Top.Rect.Bottom, screen.Body.Rect.Y); Assert.AreEqual(screen.Body.Rect.Bottom, screen.Bottom.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(41 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(441 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Bottom);
        // The pre-made browser's plan over the same set differs only in the body part.
        var premade = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.PremadeBrowser).Value!;
        Assert.AreEqual(premade.Top, screen.Top); Assert.AreEqual(premade.Bottom, screen.Bottom);
        Assert.AreEqual(premade.Body.Rect, screen.Body.Rect); Assert.AreNotEqual(premade.Body.PartId, screen.Body.PartId);
    }

    [TestMethod]
    [DataRow("board.charcreatetopbar")]
    [DataRow("backdrop.createcharacterbase")]
    [DataRow("board.charcreatebottombar")]
    public void The_new_character_screen_with_any_part_missing_has_no_kit_screen_and_names_the_part(string missingPart)
    {
        var plan = Screen(1920, 1080);
        var set = Full(SteamwovenStep.Half, missingPart);
        var result = SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.NewCharacter);
        Assert.IsFalse(result.Complete);
        CollectionAssert.AreEqual(new[] { missingPart }, result.Missing.ToArray());
        // The pre-made browser and the save screen are held back only by a strip they share, not by the new character body.
        foreach (var other in new[] { SteamwovenKitScreen.PremadeBrowser, SteamwovenKitScreen.SaveLoad })
            Assert.AreEqual(missingPart == "backdrop.createcharacterbase", SteamwovenKitComposer.Screen(plan, set, other).Complete);
    }

    [TestMethod]
    public void The_new_character_screen_draws_none_of_its_own_backdrop_arts_over_a_kit_screen_and_all_three_without()
    {
        CollectionAssert.AreEquivalent(new[] { 336, 335, 765 }, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.NewCharacter, false).ToArray());
        Assert.AreEqual(0, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.NewCharacter, true).Count);
        CollectionAssert.AreEquivalent(new[] { AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt, AreaNewCharacterArtLayout.BackgroundArt }, SteamwovenKitTable.Screens[SteamwovenKitScreen.NewCharacter].OwnArts.ToArray());
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(1280, 1024, SteamwovenStep.Third)]
    public void The_character_editor_is_the_same_two_strips_around_the_character_window_s_kit_background_tiling_the_plate(int width, int height, SteamwovenStep step)
    {
        // ARC-UI-3041 (census M09): the strips are the creation family's, the body is the character sheet window's one-piece kit background in the plate's 800 x 400.
        var plan = Screen(width, height);
        var composed = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.CharacterEditor);
        Assert.IsTrue(composed.Complete, string.Join(", ", composed.Missing));
        var screen = composed.Value!;
        CollectionAssert.AreEqual(new[] { "board.charcreatetopbar", "backdrop.char_maint", "board.charcreatebottombar" }, screen.Draws.Select(draw => draw.PartId).ToArray());
        Assert.AreEqual(1, screen.BodyPieces.Count);
        Assert.AreEqual(plan.Zone.Y, screen.Top.Rect.Y); Assert.AreEqual(plan.Zone.Bottom, screen.Bottom.Rect.Bottom);
        Assert.AreEqual(screen.Top.Rect.Bottom, screen.Body.Rect.Y); Assert.AreEqual(screen.Body.Rect.Bottom, screen.Bottom.Rect.Y);
        Assert.AreEqual(plan.Zone.X, screen.Body.Rect.X); Assert.AreEqual(plan.Zone.Right, screen.Body.Rect.Right);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(41 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(441 * plan.Factor, MidpointRounding.AwayFromZero), screen.Body.Rect.Bottom);
        // The same rectangle as the new character screen's body.
        var newCharacter = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.NewCharacter).Value!;
        Assert.AreEqual(newCharacter.Body.Rect, screen.Body.Rect); Assert.AreEqual(newCharacter.Top, screen.Top); Assert.AreEqual(newCharacter.Bottom, screen.Bottom);
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One, false)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds, false)]
    [DataRow(1920, 1080, SteamwovenStep.Half, true)]
    [DataRow(1280, 1024, SteamwovenStep.Third, true)]
    [DataRow(1024, 768, SteamwovenStep.Third, false)]
    public void The_opening_shop_lays_the_barter_window_s_two_kit_pieces_across_the_body_meeting_at_the_1290_px_joint(int width, int height, SteamwovenStep step, bool follower)
    {
        // ARC-UI-3041 (census M10): the merchant's side (1290 master px) and the inventory's right piece (1590) are the body; a follower merchant's side is the follower background.
        var plan = Screen(width, height);
        var composed = SteamwovenKitComposer.Screen(plan, Full(step), SteamwovenKitScreen.OpeningShop, null, follower ? SteamwovenKitWindow.BarterFollower : null);
        Assert.IsTrue(composed.Complete, string.Join(", ", composed.Missing));
        var screen = composed.Value!;
        CollectionAssert.AreEqual(new[] { "board.charcreatetopbar", follower ? "backdrop.barter_follower" : "backdrop.barter", "backdrop.inventor", "board.charcreatebottombar" }, screen.Draws.Select(draw => draw.PartId).ToArray());
        Assert.AreEqual(2, screen.BodyPieces.Count);
        var (left, right) = (screen.BodyPieces[0], screen.BodyPieces[1]);
        Assert.AreEqual(plan.Zone.X, left.Rect.X); Assert.AreEqual(left.Rect.Right, right.Rect.X, "no gap between the two pieces"); Assert.AreEqual(plan.Zone.Right, right.Rect.Right);
        var plateHeight = plan.Zone.Bottom - plan.Zone.Y;
        Assert.AreEqual(plan.Zone.X + (int)new Ratio(1290 * plateHeight, 2160).RoundHalfUp(), left.Rect.Right, "the joint is at the 1290 px master position, rounded half up exactly");
        Assert.AreEqual(left.Rect.Y, right.Rect.Y); Assert.AreEqual(left.Rect.Bottom, right.Rect.Bottom);
        Assert.AreEqual(screen.Top.Rect.Bottom, left.Rect.Y); Assert.AreEqual(left.Rect.Bottom, screen.Bottom.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(41 * plan.Factor, MidpointRounding.AwayFromZero), left.Rect.Y);
        Assert.AreEqual(plan.Zone.Y + (int)Math.Round(441 * plan.Factor, MidpointRounding.AwayFromZero), left.Rect.Bottom);
    }

    [TestMethod]
    [DataRow(2496, 1404)]
    [DataRow(2624, 1476)]
    [DataRow(3008, 1692)]
    public void The_opening_shop_s_joint_rounds_half_up_exactly_where_the_plate_height_makes_a_tie(int width, int height)
    {
        // ARC-UI-3041: at these plate heights 1290 px master is an exact half device pixel; a double computation lands just below it.
        var plan = Screen(width, height);
        var composed = SteamwovenKitComposer.Screen(plan, Full(plan.Layout.Step), SteamwovenKitScreen.OpeningShop);
        Assert.IsTrue(composed.Complete, string.Join(", ", composed.Missing));
        var screen = composed.Value!;
        var plateHeight = plan.Zone.Bottom - plan.Zone.Y;
        Assert.AreEqual(height, plateHeight);
        Assert.AreEqual(plan.Zone.X + (int)new Ratio(1290 * plateHeight, 2160).RoundHalfUp(), screen.BodyPieces[0].Rect.Right);
    }

    [TestMethod]
    public void A_screen_with_a_body_part_of_its_own_ignores_the_window_variant()
    {
        var plan = Screen(1920, 1080);
        var set = Full(SteamwovenStep.Half);
        Assert.AreEqual(SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.NewCharacter).Value, SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.NewCharacter, null, SteamwovenKitWindow.BarterFollower).Value);
        // The character editor's window is not switchable either way: the variant names the shop's barter window alone.
        Assert.AreEqual("backdrop.char_maint", SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.CharacterEditor).Value!.Body.PartId);
    }

    [TestMethod]
    [DataRow(SteamwovenKitScreen.CharacterEditor, "backdrop.char_maint", null)]
    [DataRow(SteamwovenKitScreen.OpeningShop, "backdrop.barter", null)]
    [DataRow(SteamwovenKitScreen.OpeningShop, "backdrop.inventor", null)]
    [DataRow(SteamwovenKitScreen.OpeningShop, "backdrop.barter_follower", SteamwovenKitWindow.BarterFollower)]
    [DataRow(SteamwovenKitScreen.OpeningShop, "backdrop.inventor", SteamwovenKitWindow.BarterFollower)]
    public void A_window_body_screen_with_any_piece_missing_has_no_kit_screen_and_names_the_piece(SteamwovenKitScreen screen, string missingPart, SteamwovenKitWindow? variant)
    {
        var plan = Screen(1920, 1080);
        var result = SteamwovenKitComposer.Screen(plan, Full(SteamwovenStep.Half, missingPart), screen, null, variant);
        Assert.IsFalse(result.Complete);
        CollectionAssert.AreEqual(new[] { missingPart }, result.Missing.ToArray());
    }

    [TestMethod]
    public void The_opening_shop_with_the_plain_follower_background_missing_is_still_a_kit_screen_for_a_plain_merchant()
    {
        // Only the variant asked for is needed: a set without the follower side composes the plain merchant's shop (the same rule as the barter window itself).
        var plan = Screen(1920, 1080);
        var set = Full(SteamwovenStep.Half, "backdrop.barter_follower");
        Assert.IsTrue(SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.OpeningShop).Complete);
        Assert.IsFalse(SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.OpeningShop, null, SteamwovenKitWindow.BarterFollower).Complete);
    }

    [TestMethod]
    public void A_window_body_screen_with_a_wrong_sized_piece_has_no_kit_screen()
    {
        var plan = Screen(1920, 1080);
        var full = Full(SteamwovenStep.Half);
        var parts = full.Ids.Select(id => { full.TryGet(id, out var part); return id == "backdrop.inventor" ? new SteamwovenKitPart(part.Id, part.Width, part.Height - 1) : part; }).ToList();
        var set = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", parts);
        var result = SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.OpeningShop);
        Assert.IsFalse(result.Complete);
        CollectionAssert.AreEqual(new[] { "backdrop.inventor (wrong size)" }, result.Missing.ToArray());
        Assert.IsTrue(SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.CharacterEditor).Complete, "the editor does not use the inventory's piece");
    }

    [TestMethod]
    public void The_editor_and_the_shop_draw_none_of_their_own_backdrop_arts_over_a_kit_screen_and_the_strips_and_the_window_s_art_without()
    {
        CollectionAssert.AreEquivalent(new[] { 336, 335, 22 }, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.CharacterEditor, false).ToArray());
        CollectionAssert.AreEquivalent(new[] { 336, 335, 235, 221 }, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.OpeningShop, false).ToArray());
        Assert.AreEqual(0, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.CharacterEditor, true).Count);
        Assert.AreEqual(0, SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.OpeningShop, true).Count);
        Assert.IsNull(SteamwovenKitTable.Screens[SteamwovenKitScreen.CharacterEditor].Body);
        Assert.AreEqual(SteamwovenKitWindow.Character, SteamwovenKitTable.Screens[SteamwovenKitScreen.CharacterEditor].BodyWindow);
        Assert.AreEqual(SteamwovenKitWindow.Barter, SteamwovenKitTable.Screens[SteamwovenKitScreen.OpeningShop].BodyWindow);
    }

    [TestMethod]
    public void The_save_screen_s_message_box_is_the_bottom_strip_below_the_middle_window_s_47_rows()
    {
        var box = AreaSaveListArtLayout.MessageBox;
        Assert.AreEqual((200, 488, 400, 112), box);
        // The strip art's middle window is 400 x 47 at the strip's top; the box is the rest of the strip below it, up to the screen's bottom edge.
        var middle = AreaSaveListArtLayout.BottomStripParts[0];
        Assert.AreEqual((middle.X, middle.Y + middle.Height, middle.Width), (box.X, box.Y, box.Width));
        Assert.AreEqual(AreaSaveListArtLayout.ScreenHeight, box.Y + box.Height);
    }

    [TestMethod]
    public void The_hud_strips_lay_the_hardware_parts_on_the_6_px_grid_inside_the_strip_and_every_part_is_sized()
    {
        var sizes = HardwareSizes.ToDictionary(hardware => hardware.Id);
        foreach (var (strip, height) in new[] { (AreaBarStrip.Top, 150), (AreaBarStrip.Bottom, 570) })
        {
            var layers = SteamwovenKitTable.Strips[strip].Layers;
            Assert.IsTrue(layers.Count > 0, strip.ToString());
            Assert.AreEqual(layers.Count, layers.Distinct().Count(), "a part is laid once at a place");
            foreach (var layer in layers)
            {
                Assert.IsTrue(sizes.ContainsKey(layer.Part), layer.Part);
                var (width, partHeight) = (sizes[layer.Part].Width, sizes[layer.Part].Height);
                Assert.AreEqual(0, layer.MasterX % 6, layer.Part + " x on the 6 px grid");
                Assert.AreEqual(0, layer.MasterY % 6, layer.Part + " y on the 6 px grid");
                Assert.AreEqual(0, width % 6, layer.Part + " width on the 6 px grid");
                Assert.AreEqual(0, partHeight % 6, layer.Part + " height on the 6 px grid");
                Assert.IsTrue(layer.MasterX >= 0 && layer.MasterY >= 0 && layer.MasterY + partHeight <= height, layer.Part + " inside the strip's height");
                // Inside the strip's 2880 px, except the top strip's right plate: its 948 px canvas (the original's plate on the 6 px grid) starts at 1938 and its last 6 px are padding the draw clips away.
                Assert.IsTrue(layer.MasterX + width <= SteamwovenKitTable.CompositionMasterWidth + (layer.Part == "hardware.hud_plate_right" ? 6 : 0), layer.Part + " inside the strip's width");
            }
        }
        // Every sized part is laid somewhere, so a part of the manifest never lies unused.
        var laid = SteamwovenKitTable.Strips.Values.SelectMany(spec => spec.Layers).Select(layer => layer.Part).ToHashSet();
        CollectionAssert.AreEquivalent(sizes.Keys.ToList(), laid.ToList());
    }

    [TestMethod]
    [DataRow(3840, 2160, SteamwovenStep.One)]
    [DataRow(1920, 1080, SteamwovenStep.Half)]
    [DataRow(2560, 1440, SteamwovenStep.TwoThirds)]
    public void Each_hud_strip_composes_every_hardware_layer_inside_its_clip_and_a_missing_one_leaves_the_generated_chrome(int width, int height, SteamwovenStep step)
    {
        var plan = Plan(width, height);
        foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
        {
            var spec = SteamwovenKitTable.Strips[strip];
            var composed = SteamwovenKitComposer.Strip(plan, Full(step), strip).Value!;
            Assert.AreEqual(spec.Layers.Count, composed.Layers.Count);
            var rect = strip == AreaBarStrip.Top ? plan.Column.TopStrip : plan.Column.BottomStrip;
            for (var i = 0; i < composed.Layers.Count; i++)
            {
                var (layer, laid) = (composed.Layers[i], spec.Layers[i]);
                Assert.AreEqual(laid.Part, layer.PartId);
                Assert.IsTrue(layer.Clip.Width > 0 && layer.Clip.Height > 0, layer.PartId + " shows");
                Assert.IsTrue(layer.Clip.X >= rect.X && layer.Clip.Right <= rect.Right && layer.Clip.Y >= rect.Y && layer.Clip.Bottom <= rect.Bottom, layer.PartId);
                // The layer starts where its master position says: the strip's corner plus the position in device pixels (rounded from the master pixels at the plan's scale).
                var factor = (double)rect.Width / SteamwovenKitTable.CompositionMasterWidth;
                Assert.IsTrue(Math.Abs(layer.Rect.X - (rect.X + laid.MasterX * factor)) <= 1 && Math.Abs(layer.Rect.Y - (rect.Y + laid.MasterY * factor)) <= 1, $"{layer.PartId} at ({laid.MasterX}, {laid.MasterY}) is at {layer.Rect}");
            }
            var gone = spec.Layers[0].Part;
            var result = SteamwovenKitComposer.Strip(plan, Full(step, gone), strip);
            Assert.IsFalse(result.Complete);
            CollectionAssert.Contains(result.Missing.ToList(), gone);
        }
    }

    [TestMethod]
    public void The_table_names_only_ids_that_are_safe_file_names_and_parts_of_the_manifest()
    {
        var ids = SteamwovenKitTable.Strips.Values.SelectMany(strip => new[] { strip.Board, strip.CapLeft, strip.CapRight }.Concat(strip.Layers.Select(layer => layer.Part)))
            .Concat(SteamwovenKitTable.Windows.Values.SelectMany(window => window.Pieces.Select(piece => piece.Part)))
            .Concat(SteamwovenKitTable.Plates.Values).Concat(SteamwovenKitTable.ButtonFaces.Values).Concat(SteamwovenKitTable.Sheets.Values.Select(sheet => sheet.Part)).Concat(SteamwovenKitTable.Covers.Values.Select(cover => cover.Part))
            .Concat(SteamwovenKitTable.Screens.Values.SelectMany(screen => new[] { screen.TopStrip, screen.Body, screen.BottomStrip }.OfType<string>())).ToList();
        Assert.IsTrue(ids.All(SteamwovenKitSet.ValidId));
        // Every id is a row of the committed kit manifest (identifiers only), so a renamed part cannot leave the table behind.
        var manifest = System.IO.File.ReadAllText(FindKit());
        foreach (var id in ids) Assert.IsTrue(manifest.Contains($"\"id\": \"{id}\""), id);
    }

    private static string FindKit()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "tools", "steamwoven_ui", "kit.json"))) directory = directory.Parent;
        return System.IO.Path.Combine(directory!.FullName, "tools", "steamwoven_ui", "kit.json");
    }

    [TestMethod]
    [DataRow("x1", SteamwovenStep.One)]
    [DataRow("x2-3", SteamwovenStep.TwoThirds)]
    [DataRow("x1-2", SteamwovenStep.Half)]
    [DataRow("x1-3", SteamwovenStep.Third)]
    public void A_step_has_the_bake_tools_folder_name(string name, SteamwovenStep step) => Assert.AreEqual(name, SteamwovenKitSet.FolderName(step));

    [TestMethod]
    [DataRow("board.inttop", true)]
    [DataRow("a/b_c-d.e", true)]
    [DataRow("../x", false)]
    [DataRow("/abs", false)]
    [DataRow(".hidden", false)]
    [DataRow("a\\b", false)]
    [DataRow("a//b", false)]
    [DataRow("", false)]
    [DataRow("a b", false)]
    public void Part_ids_that_could_leave_the_folder_are_refused(string id, bool valid) => Assert.AreEqual(valid, SteamwovenKitSet.ValidId(id));

    private static string Folder() => Directory.CreateTempSubdirectory("arc-kit-").FullName;

    private static void WriteSet(string root, SteamwovenStep step, string manifestParts, params string[] files)
    {
        var directory = System.IO.Path.Combine(root, SteamwovenKitSet.FolderName(step));
        Directory.CreateDirectory(directory);
        var scale = SteamwovenGrid.StepScale(step);
        File.WriteAllText(System.IO.Path.Combine(directory, "manifest.json"), $$"""{"schema":1,"step":"{{SteamwovenKitSet.FolderName(step)}}","ratio":[{{scale.Numerator}},{{scale.Denominator}}],"parts":[{{manifestParts}}]}""");
        foreach (var file in files) File.WriteAllBytes(System.IO.Path.Combine(directory, file + ".png"), [0]);
    }

    [TestMethod]
    public void A_set_reads_its_manifest_and_leaves_out_parts_with_a_bad_size_or_no_file()
    {
        var root = Folder();
        try
        {
            WriteSet(root, SteamwovenStep.Half,
                """{"id":"board.inttop","master":{"size":[2880,150],"sha256":"x"},"size":[1440,75]},{"id":"bad.size","master":{"size":[600,600],"sha256":"x"},"size":[301,300]},{"id":"nofile","master":{"size":[60,60],"sha256":"x"},"size":[30,30]},{"id":"../evil","master":{"size":[60,60],"sha256":"x"},"size":[30,30]},{"id":"off.grid","master":{"size":[61,60],"sha256":"x"},"size":[30,30]}""",
                "board.inttop", "bad.size", "off.grid");
            var set = SteamwovenKitSet.Read(root, SteamwovenStep.Half, out var problem)!;
            Assert.IsNotNull(set);
            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.TryGet("board.inttop", out var part)); Assert.AreEqual((1440, 75), (part.Width, part.Height));
            StringAssert.Contains(problem, "bad.size"); StringAssert.Contains(problem, "nofile"); StringAssert.Contains(problem, "off.grid");
            Assert.AreEqual(System.IO.Path.Combine(root, "x1-2", "board.inttop.png"), set.PathOf("board.inttop"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void A_set_of_another_step_a_missing_or_a_broken_manifest_reads_as_no_set()
    {
        var root = Folder();
        try
        {
            Assert.IsNull(SteamwovenKitSet.Read(root, SteamwovenStep.One, out var missing)); StringAssert.Contains(missing, "manifest");
            WriteSet(root, SteamwovenStep.Half, "");
            // The manifest of the half step sits in the third's folder: the ratio says it is not that set.
            Directory.CreateDirectory(System.IO.Path.Combine(root, "x1-3"));
            File.Copy(System.IO.Path.Combine(root, "x1-2", "manifest.json"), System.IO.Path.Combine(root, "x1-3", "manifest.json"));
            Assert.IsNull(SteamwovenKitSet.Read(root, SteamwovenStep.Third, out var other)); StringAssert.Contains(other, "not the set of this step");
            File.WriteAllText(System.IO.Path.Combine(root, "x1-2", "manifest.json"), "{not json");
            Assert.IsNull(SteamwovenKitSet.Read(root, SteamwovenStep.Half, out var broken)); StringAssert.Contains(broken, "does not parse");
            File.WriteAllText(System.IO.Path.Combine(root, "x1-2", "manifest.json"), """{"schema":2,"parts":[]}""");
            Assert.IsNull(SteamwovenKitSet.Read(root, SteamwovenStep.Half, out var schema)); StringAssert.Contains(schema, "schema 1");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void The_root_is_the_environment_folder_or_the_ignored_baked_folder_found_on_the_way_up_or_none()
    {
        var root = Folder();
        try
        {
            var baked = System.IO.Path.Combine(root, "local", "steamwoven-ui", "baked");
            Directory.CreateDirectory(baked);
            var worktree = Directory.CreateDirectory(System.IO.Path.Combine(root, ".claude", "worktrees", "w", "tools")).FullName;
            Assert.AreEqual(baked, SteamwovenKitRoot.Resolve(null, ["/nonexistent-arc", worktree]));
            Assert.AreEqual(root, SteamwovenKitRoot.Resolve(root, [worktree]));
            Assert.IsNull(SteamwovenKitRoot.Resolve(System.IO.Path.Combine(root, "missing"), [worktree]));
            Assert.IsNull(SteamwovenKitRoot.Resolve("off", [worktree]));
            Assert.IsNull(SteamwovenKitRoot.Resolve(null, ["/nonexistent-arc"]));
            // A packaged game's own folder is the last resort, after the environment and the checkout.
            var installed = Directory.CreateDirectory(System.IO.Path.Combine(root, "user", "steamwoven-ui", "baked")).FullName;
            Assert.AreEqual(installed, SteamwovenKitRoot.Resolve(null, ["/nonexistent-arc"], [System.IO.Path.Combine(root, "missing"), installed]));
            Assert.AreEqual(baked, SteamwovenKitRoot.Resolve(null, [worktree], [installed]));
            Assert.AreEqual(root, SteamwovenKitRoot.Resolve(root, [worktree], [installed]));
        }
        finally { Directory.Delete(root, true); }
    }

    private static System.Text.Json.JsonElement KitManifest()
    {
        for (var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = System.IO.Path.Combine(directory.FullName, "tools", "steamwoven_ui", "kit.json");
            if (System.IO.File.Exists(path)) return System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path)).RootElement.Clone();
        }
        throw new System.IO.FileNotFoundException("tools/steamwoven_ui/kit.json not found above " + AppContext.BaseDirectory);
    }

    /// <summary>ARC-UI-3098: the runtime's fixture table is the manifest's: the same ids, each at the master size of its unit size on the 6 px grid.</summary>
    [TestMethod]
    public void The_fixture_table_is_the_manifest_s_fixtures_at_the_size_of_their_place()
    {
        var parts = KitManifest().GetProperty("parts").EnumerateArray().ToDictionary(part => part.GetProperty("id").GetString()!);
        foreach (var (id, units) in SteamwovenKitTable.FixtureUnits)
        {
            Assert.IsTrue(parts.TryGetValue(id, out var part), id + " is a manifest part");
            Assert.AreEqual("placed", part.GetProperty("size_basis").GetString(), id);
            Assert.AreEqual(SteamwovenKitTable.MasterOf(units.Width), part.GetProperty("size")[0].GetInt32(), id + " width");
            Assert.AreEqual(SteamwovenKitTable.MasterOf(units.Height), part.GetProperty("size")[1].GetInt32(), id + " height");
        }
        var placed = parts.Values.Where(part => part.GetProperty("size_basis").GetString() == "placed").Select(part => part.GetProperty("id").GetString()!).Order().ToArray();
        CollectionAssert.AreEqual(placed, SteamwovenKitTable.FixtureUnits.Keys.Order().ToArray(), "every placed part of the manifest is in the table");
        Assert.AreEqual(1164, SteamwovenKitTable.MasterOf(324));
        Assert.AreEqual(114, SteamwovenKitTable.MasterOf(32));
    }

    [TestMethod]
    public void Each_covered_window_names_the_fixtures_it_draws_and_no_other()
    {
        foreach (var window in Enum.GetValues<SteamwovenKitWindow>())
        {
            var ids = SteamwovenKitTable.FixturesOf(window);
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), window + " lists a fixture twice");
            Assert.IsTrue(ids.All(SteamwovenKitTable.FixtureUnits.ContainsKey), window + " names a fixture the table does not size");
        }
        CollectionAssert.AreEqual(SteamwovenKitTable.FixturesOf(SteamwovenKitWindow.Barter).ToArray(), SteamwovenKitTable.FixturesOf(SteamwovenKitWindow.BarterFollower).ToArray());
        Assert.AreEqual(0, SteamwovenKitTable.FixturesOf(SteamwovenKitWindow.Logbook).Count);
        Assert.IsTrue(SteamwovenKitTable.FixturesOf(SteamwovenKitWindow.Character).Contains("fixture.mt_ruler") && SteamwovenKitTable.FixturesOf(SteamwovenKitWindow.Schematic).Contains("fixture.gear_hat"));
        // ARC-UI-3299: the sheet's label parts (stat names, the dial's E&G) are fixtures of the character window, each on the 5-unit grid (3.6 px a unit, 6 px grid cells) at the corner the viewer draws it at.
        foreach (var id in new[] { "fixture.sheet_stat_names_l", "fixture.sheet_stat_names_r", "fixture.sheet_dial_letters" })
        {
            Assert.IsTrue(SteamwovenKitTable.FixturesOf(SteamwovenKitWindow.Character).Contains(id), id);
            var (width, height) = SteamwovenKitTable.FixtureUnits[id];
            Assert.IsTrue(width % 5 == 0 && height % 5 == 0, id + " is not on the 5-unit grid");
            Assert.AreEqual(width * 36 / 10, SteamwovenKitTable.MasterOf(width), id + " width is the exact 3.6 px a unit");
        }
    }

    [TestMethod]
    public void A_fixture_is_the_set_s_part_at_the_table_s_size_else_nothing()
    {
        var step = SteamwovenStep.Half;
        int B(int units) => SteamwovenGrid.BakedLength(SteamwovenKitTable.MasterOf(units), step);
        var set = SteamwovenKitSet.FromParts(step, "/kit/x1-2", [new("fixture.count_pit", B(61), B(17)), new("fixture.mt_ruler", B(46), B(330) + 1)]);
        Assert.AreEqual("fixture.count_pit", SteamwovenKitComposer.Fixture(set, "fixture.count_pit")?.Id);
        Assert.IsNull(SteamwovenKitComposer.Fixture(set, "fixture.mt_ruler"), "a wrong sized part is left out");
        Assert.IsNull(SteamwovenKitComposer.Fixture(set, "fixture.align_dial"), "a missing part is left out");
        Assert.IsNull(SteamwovenKitComposer.Fixture(set, "fixture.unknown"), "an id the table does not size");
    }
}
