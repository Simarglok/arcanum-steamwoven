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
            new("backdrop.mainmenuback", B(2880), B(2160)),
        };
        foreach (var window in SteamwovenKitTable.Windows.Values)
            foreach (var (id, width) in window.Pieces) parts.Add(new(id, B(width), B(1440)));
        foreach (var sheet in SteamwovenKitTable.Sheets.Values) parts.Add(new(sheet.Part, B(sheet.MasterWidth), B(sheet.MasterHeight)));
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
        // The loot window is another window of the inventory family: it keeps the generated backdrop, even over an inventory.
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.Loot]));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([InterfaceMode.Inventory, InterfaceMode.Dialog]));
        Assert.IsNull(SteamwovenKitComposer.WindowOf([]));
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
    public void A_button_is_its_face_and_the_overlay_of_its_look_and_none_without_the_face()
    {
        var set = Full(SteamwovenStep.Half);
        CollectionAssert.AreEqual(new[] { "plate.char_but" }, SteamwovenKitComposer.ButtonLayers(set, 169, AreaBarButtonLook.Idle).Select(part => part.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "plate.char_but", "plate.char_but_hover" }, SteamwovenKitComposer.ButtonLayers(set, 169, AreaBarButtonLook.Hovered).Select(part => part.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "plate.char_but", "plate.char_but_pressed" }, SteamwovenKitComposer.ButtonLayers(set, 169, AreaBarButtonLook.Down).Select(part => part.Id).ToArray());
        // The lit face of a light button is its own part.
        CollectionAssert.AreEqual(new[] { "state.char_on" }, SteamwovenKitComposer.ButtonLayers(set, 561, AreaBarButtonLook.Idle).Select(part => part.Id).ToArray());
        // No face (a button the kit has not made yet, or an art that is no button): the generated or legacy art stays.
        Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(set, 472, AreaBarButtonLook.Idle).Count);
        Assert.AreEqual(0, SteamwovenKitComposer.ButtonLayers(set, 1, AreaBarButtonLook.Idle).Count);
        // An overlay of another size is not drawn.
        var odd = SteamwovenKitSet.FromParts(SteamwovenStep.Half, "/kit/x1-2", [new("plate.char_but", 60, 60), new("plate.char_but_hover", 30, 30)]);
        Assert.AreEqual(1, SteamwovenKitComposer.ButtonLayers(odd, 169, AreaBarButtonLook.Hovered).Count);
    }

    [TestMethod]
    public void The_table_names_only_ids_that_are_safe_file_names_and_parts_of_the_manifest()
    {
        var ids = SteamwovenKitTable.Strips.Values.SelectMany(strip => new[] { strip.Board, strip.CapLeft, strip.CapRight })
            .Concat(SteamwovenKitTable.Windows.Values.SelectMany(window => window.Pieces.Select(piece => piece.Part)))
            .Concat(SteamwovenKitTable.Plates.Values).Concat(SteamwovenKitTable.ButtonFaces.Values).Concat(SteamwovenKitTable.Sheets.Values.Select(sheet => sheet.Part)).ToList();
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
}
