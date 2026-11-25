using System.Text.Json;
using Arcanum.Presentation.Steamwoven;
using Arcanum.Session;
using Arcanum.World;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2461 (SWI-RUN-2, SWI-RUN-3, SWI-RUN-5, SWI-VER-6): the variant "kit" of the Steamwoven bar smoke (ARC_STEAMWOVEN_VARIANTS=kit, ARC_AREA_BAR_STEAMWOVEN=1 with ARC_HD_PACK). No baked kit exists yet (the production batches
    // have not run), so the smoke writes a synthetic one: every part the runtime composes, flat colours, baked at the step of each size (a manifest and the PNGs, as bake.py writes them), into a temporary folder it points the kit store at.
    // For each size of ARC_STEAMWOVEN_SIZES (the TRD's verification sizes) it checks that the strips and their caps are the kit's parts in the plan's rectangles (read back by pixel), that a window's kit background (shown with no window
    // over it, by the host's smoke override) tiles the zone and is the right pieces, that only that background is resident and it goes with the window, and the bar button's layers. At the first size it also removes a cap and checks the
    // fallback (that strip is the pack's, the other the kit's), and at the end the HD option off leaves no trace of the kit. Captures: PREFIX-hud-steamwoven-kit-WxH[-window].png (local, never in git).
    private (SteamwovenBackdropFamily Family, SteamwovenKitWindow? Window)? _smokeKitBackdrop;

    private static readonly Color KitTop = new(0.85f, 0.15f, 0.15f), KitBottom = new(0.15f, 0.15f, 0.85f), KitCapLeft = new(0.95f, 0.8f, 0.1f), KitCapRight = new(0.1f, 0.8f, 0.95f),
        KitDoll = new(0.15f, 0.6f, 0.15f), KitInventory = new(0.6f, 0.15f, 0.6f), KitPlate = new(0.9f, 0.5f, 0.1f), KitOptionsPlate = new(0.1f, 0.5f, 0.5f), KitHover = new(0.9f, 0.9f, 0.9f), KitGlyph = new(0.05f, 0.05f, 0.05f), KitMap = new(0.8f, 0.75f, 0.1f),
        KitBarter = new(0.55f, 0.35f, 0.15f), KitBarterFollower = new(0.15f, 0.55f, 0.55f),
        KitLoot = new(0.45f, 0.35f, 0.1f), KitLootDoll = new(0.1f, 0.3f, 0.75f), KitDollCover = new(0.75f, 0.1f, 0.4f),
        KitCharacter = new(0.3f, 0.3f, 0.3f), KitSkillsSheet = new(0.8f, 0.2f, 0.2f), KitTechSheet = new(0.2f, 0.8f, 0.2f), KitSpellsSheet = new(0.2f, 0.2f, 0.8f), KitSchemesSheet = new(0.8f, 0.8f, 0.2f),
        KitLogSide = new(0.1f, 0.3f, 0.7f), KitLogBook = new(0.7f, 0.4f, 0.1f), KitSchematic = new(0.1f, 0.5f, 0.3f),
        KitSaveTop = new(0.85f, 0.45f, 0.65f), KitSaveBody = new(0.35f, 0.65f, 0.25f), KitSaveBottom = new(0.25f, 0.35f, 0.75f), KitPremadeBody = new(0.6f, 0.25f, 0.55f), KitNewCharBody = new(0.25f, 0.6f, 0.6f),
        KitFixture = new(0.95f, 0.95f, 0.2f), KitHardware = new(0.5f, 0.45f, 0.3f);

    /// <summary>The synthetic kit: part, master size and colour. The strips are the manifest's provisional 150 and 570 px; the caps are 120 px wide.</summary>
    private static IReadOnlyList<(string Id, int Width, int Height, Color Colour)> SyntheticKitParts { get; } =
    [
        ("board.inttop", 2880, 150, KitTop), ("board.intbotom", 2880, 570, KitBottom),
        ("cap.top_strip_left", 120, 150, KitCapLeft), ("cap.top_strip_right", 120, 150, KitCapRight),
        ("cap.bottom_strip_left", 120, 570, KitCapLeft), ("cap.bottom_strip_right", 120, 570, KitCapRight),
        // ARC-UI-2481: the hardware layers of the two HUD strips (master sizes of tools/steamwoven_ui/hardware_build.py; the smoke lays them at the table's places and checks they stay inside the strip).
        ("hardware.hud_plate_left", 948, 150, KitHardware), ("hardware.hud_plate_right", 948, 150, KitHardware), ("hardware.hud_top_slot", 150, 144, KitHardware),
        ("hardware.hud_top_ring", 108, 108, KitHardware), ("hardware.hud_top_clock", 486, 132, KitHardware), ("hardware.hud_top_infinity", 108, 108, KitHardware),
        ("hardware.hud_top_hourglass", 78, 132, KitHardware), ("hardware.hud_top_rivet", 30, 30, KitHardware), ("hardware.hud_vial", 204, 558, KitHardware),
        ("hardware.hud_vial_r", 204, 558, KitHardware), ("hardware.hud_cylinder", 108, 324, KitHardware), ("hardware.hud_cylinder_r", 108, 324, KitHardware),
        ("hardware.hud_wires", 114, 144, KitHardware), ("hardware.hud_ring", 198, 192, KitHardware), ("hardware.hud_dial", 144, 144, KitHardware), ("hardware.hud_gem", 138, 144, KitHardware),
        ("hardware.hud_slot", 138, 144, KitHardware), ("hardware.hud_rail", 144, 48, KitHardware),
        // ARC-UI-3260: the hardware layers of the two-strip menu screens' strips (the top strip's rivets are the HUD's own part).
        ("hardware.menu_plate_left", 966, 150, KitHardware), ("hardware.menu_plate_right", 966, 150, KitHardware), ("hardware.menu_window", 1476, 384, KitHardware), ("hardware.menu_ring", 258, 258, KitHardware),
        ("backdrop.pdoll", 1290, 1440, KitDoll), ("backdrop.inventor", 1590, 1440, KitInventory),
        // ARC-UI-3033: the barter window's merchant side (plain and with the follower's cycle buttons) and its two overlays, the merchant's doll view and the hero's doll cover.
        ("backdrop.barter", 1290, 1440, KitBarter), ("backdrop.barter_follower", 1290, 1440, KitBarterFollower), ("backdrop.barter_pd", 1290, 876, KitDoll),
        ("backdrop.loot", 1290, 1440, KitLoot), ("backdrop.loot_pd", 1290, 960, KitLootDoll), ("backdrop.pd_cover4inven", 1230, 1440, KitDollCover),
        ("backdrop.mainmenuback", 2880, 2160, KitPlate), ("backdrop.optionsmenuback", 2880, 2160, KitOptionsPlate),
        ("backdrop.mapmain", 2880, 1440, KitMap),
        ("plate.char_but", 120, 120, KitPlate), ("plate.char_but_hover", 120, 120, KitHover),
        // ARC-UI-3096: the Character button's glyph, a symbol (the middle half of the part) on a transparent background at the plate's size.
        ("glyph.char_but", 120, 120, KitGlyph),
        // ARC-UI-3035: the character window's backdrop and the sheets of its four pages (846 x 1170 master px each).
        ("backdrop.char_maint", 2880, 1440, KitCharacter), ("sheet.skills_window", 846, 1170, KitSkillsSheet), ("sheet.disciplines_window", 846, 1170, KitTechSheet),
        ("sheet.char_spells", 846, 1170, KitSpellsSheet), ("sheet.scheme_rot", 846, 1170, KitSchemesSheet),
        // ARC-UI-3037: the logbook's two pieces and the schematic window's backdrop.
        ("backdrop.logbooks_side", 594, 1440, KitLogSide), ("backdrop.book_main", 2286, 1440, KitLogBook), ("backdrop.schematic_base", 2880, 1440, KitSchematic),
        // ARC-UI-3038: the save and load screen's two strips (the provisional 150 and 570 px) and its body.
        ("board.charcreatetopbar", 2880, 150, KitSaveTop), ("backdrop.saveloadbackground", 2880, 1440, KitSaveBody), ("board.charcreatebottombar", 2880, 570, KitSaveBottom),
        // ARC-UI-3039: the pre-made browser's body (its strips are the save screen's).
        ("backdrop.pregencharacterbase", 2880, 1440, KitPremadeBody),
        // ARC-UI-3040: the new character screen's body (its strips are the save screen's).
        ("backdrop.createcharacterbase", 2880, 1440, KitNewCharBody),
        // ARC-UI-3098: the fixtures, flat (a part the windows draw at its place is the one colour; ARC_STEAMWOVEN_KIT_FIXTURES=DIR takes the real ones of tools/steamwoven_ui/fixtures.py from a baked root for a look capture).
        .. SteamwovenKitTable.FixtureUnits.Select(pair => (pair.Key, SteamwovenKitTable.MasterOf(pair.Value.Width), SteamwovenKitTable.MasterOf(pair.Value.Height), KitFixture)),
    ];

    /// <summary>Writes the synthetic set of <paramref name="step"/> under <paramref name="root"/> (parts named in <paramref name="without"/> left out, their records too).</summary>
    private static void WriteSyntheticKitSet(string root, SteamwovenStep step, params string[] without)
    {
        var directory = System.IO.Path.Combine(root, SteamwovenKitSet.FolderName(step));
        System.IO.Directory.CreateDirectory(directory);
        var scale = SteamwovenGrid.StepScale(step);
        var records = new List<object>();
        foreach (var (id, width, height, colour) in SyntheticKitParts)
        {
            if (without.Contains(id)) continue;
            var (baked, bakedHeight) = (SteamwovenGrid.BakedLength(width, step), SteamwovenGrid.BakedLength(height, step));
            var real = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_KIT_FIXTURES") is { Length: > 0 } fixtureRoot && SteamwovenKitTable.FixtureUnits.ContainsKey(id) ? System.IO.Path.Combine(fixtureRoot, SteamwovenKitSet.FolderName(step), id + ".png") : null;
            // ARC-UI-3165: ARC_STEAMWOVEN_KIT_LOOK=DIR puts a composed window surface (DIR/<part id>.png, the master of the part, e.g. backdrop.char_maint.png) in place of the part's flat colour, resized to the step's baked size, for a look capture of the real window text over it.
            var look = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_KIT_LOOK") is { Length: > 0 } lookRoot ? System.IO.Path.Combine(lookRoot, id + ".png") : null;
            if (real is not null && System.IO.File.Exists(real)) System.IO.File.Copy(real, System.IO.Path.Combine(directory, id + ".png"), true);
            else if (look is not null && System.IO.File.Exists(look))
            {
                using var surface = Image.LoadFromFile(look);
                surface.Convert(Image.Format.Rgba8);
                surface.Resize(baked, bakedHeight, Image.Interpolation.Lanczos);
                if (surface.SavePng(System.IO.Path.Combine(directory, id + ".png")) != Error.Ok) throw new InvalidOperationException("ARC-UI-3165 look surface not saved: " + id);
            }
            else
            {
                using var image = Image.CreateEmpty(baked, bakedHeight, false, Image.Format.Rgba8);
                if (id.StartsWith("glyph.", StringComparison.Ordinal) || id.StartsWith("fixture.sheet_stat_names_", StringComparison.Ordinal) || id == "fixture.sheet_dial_letters")  // ARC-UI-3299: a label part is lettering on nothing: transparent round a small mark
                {
                    image.Fill(new Color(0, 0, 0, 0));
                    image.FillRect(new Rect2I(baked / 4, bakedHeight / 4, baked / 2, bakedHeight / 2), colour);
                }
                else image.Fill(colour);
                if (image.SavePng(System.IO.Path.Combine(directory, id + ".png")) != Error.Ok) throw new InvalidOperationException("ARC-UI-2461 synthetic kit part not saved: " + id);
            }
            records.Add(new Dictionary<string, object> { ["id"] = id, ["bake_version"] = 1, ["master"] = new Dictionary<string, object> { ["size"] = new[] { width, height }, ["sha256"] = "synthetic" },
                ["size"] = new[] { baked, bakedHeight }, ["bytes"] = 0, ["sha256"] = "synthetic", ["pixels_sha256"] = "synthetic" });
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new Dictionary<string, object>
        { ["schema"] = 1, ["bake_version"] = 1, ["step"] = SteamwovenKitSet.FolderName(step), ["ratio"] = new[] { scale.Numerator, scale.Denominator }, ["parts"] = records }));
    }

    /// <summary>
    /// ARC-UI-3038 (census M05, M06; ARC_STEAMWOVEN_KIT_SYNTHETIC=1 on the save window's column pass, <c>SaveListColumnForSmoke</c>): the real save or load window over the synthetic kit's three parts. At each size the view draws the composer's
    /// plan (the top strip, the body and the bottom strip tiling the plate, the body sampled nearest exactly where its draw is exact), none of the original's arts 336, 335 and 745, the preview's black stand-in, and the pixels read back are the kit's
    /// colours in each of the three rows and across both seams. With <paramref name="firstSize"/> it also checks the fallback (a set whose bottom strip does not load, after the body did, gives the screen's own three arts whole and no resident backdrop; the kit is back when the set is)
    /// and the HD option off (no kit at all); the body part goes with the screen (SWI-RUN-5) at the caller's close.
    /// </summary>
    private async Task CheckKitSaveScreenAsync(string tag, SteamwovenFullScreenPlan plan, AreaSaveListArtView art, bool firstSize, string kitRoot, Func<int, Task> frames, Action<bool, string> require)
    {
        AreaKitStandIns.ResetForSelfTest();
        art.Place(); art.QueueRedraw();
        await frames(3);
        var set = SteamwovenKitStore.SetFor(plan.Layout.Step);
        var composed = set is null ? null : SteamwovenKitComposer.Screen(plan, set, SteamwovenKitScreen.SaveLoad).Value;
        require(composed is not null, $"[{tag}] the synthetic set composes the save screen");
        var drawn = art.KitScreenForSelfTest;
        require(drawn is not null && composed is not null && drawn.SequenceEqual(composed.AllDraws), $"[{tag}] the save window draws the composer's plan (got {(drawn is null ? "none" : string.Join(" ", drawn.Select(draw => draw.PartId + draw.Rect)))})");
        require(art.OwnBackdropDrawnForSelfTest.Count == 0, $"[{tag}] over the kit the screen draws none of its own arts 336, 335 and 745 (drew {string.Join(",", art.OwnBackdropDrawnForSelfTest)})");
        require(AreaKitStandIns.DrawnScreensForSelfTest.Contains(SteamwovenKitScreen.SaveLoad), $"[{tag}] the preview's stand-in field is drawn over the kit");
        if (composed is null || drawn is null) return;
        require(art.KitSampledNearestForSelfTest(1) == composed.Body.Exact, $"[{tag}] the body is sampled {(composed.Body.Exact ? "nearest" : "linear")} as its draw is {(composed.Body.Exact ? "exact" : "scaled")}");
        require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)) == 1, $"[{tag}] only the screen's body is resident");
        using (var image = GetViewport().GetTexture().GetImage())
        {
            var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
            Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
            Color AtUnit(double x, double y) { var (dx, dy) = plan.ToDevice(x, y); return Pixel((int)Math.Round(dx), (int)Math.Round(dy)); }
            var (top, body, bottom) = (AtUnit(400, 20), AtUnit(250, 41 + 30), AtUnit(400, 465));
            require(Near(top, KitSaveTop), $"[{tag}] the top strip row is the kit's top strip (got {top})");
            require(Near(body, KitSaveBody), $"[{tag}] the body between the lens and the preview is the kit's body (got {body})");
            require(Near(bottom, KitSaveBottom), $"[{tag}] the bottom strip row is the kit's bottom strip (got {bottom})");
            // The seams: two device rows either side of the body's top and bottom edges, clear of the buttons, the lens and the list.
            var x = (int)Math.Round(plan.ToDevice(250, 0).X);
            var xTop = (int)Math.Round(plan.ToDevice(400, 0).X);   // ARC-UI-3260: the top strip's iron sections stand over its two ends, so the row above the seam is read in the middle, between them
            var (aboveTop, belowTop) = (Pixel(xTop, composed.Body.Rect.Y - 2), Pixel(x, composed.Body.Rect.Y + 2));
            var (aboveBottom, belowBottom) = (Pixel(x, composed.Body.Rect.Bottom - 3), Pixel(x, composed.Bottom.Rect.Y + 2));
            require(Near(aboveTop, KitSaveTop) && Near(belowTop, KitSaveBody), $"[{tag}] the top strip meets the body with no gap (above {aboveTop}, below {belowTop})");
            require(Near(aboveBottom, KitSaveBody) && Near(belowBottom, KitSaveBottom), $"[{tag}] the body meets the bottom strip with no gap (above {aboveBottom}, below {belowBottom})");
            var preview = AtUnit(AreaSaveListArtLayout.Preview.X + 4, AreaSaveListArtLayout.Preview.Y + 4);
            require(!Near(preview, KitSaveBody), $"[{tag}] the preview's field is not the kit's body (got {preview})");
            // The message box of the bottom strip's middle is a black stand-in (the details and the questions are written on it), clear of the kit's bottom strip colour.
            var message = AtUnit(AreaKitStandIns.MessageField.X + AreaKitStandIns.MessageField.Width - 10, AreaKitStandIns.MessageField.Y + AreaKitStandIns.MessageField.Height - 10);
            require(message.R8 + message.G8 + message.B8 < 12, $"[{tag}] the bottom strip's message box is a black field (got {message})");
        }
        if (!firstSize) return;
        // Fallback: a set whose bottom strip file is another size than its manifest says (the body loads first, then the strip does not) gives the screen's own arts whole, and the body stays resident with no owner (SWI-RUN-5);
        // the same set whole is the kit again.
        var broken = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-save-broken-").FullName;
        try
        {
            WriteSyntheticKitSet(broken, plan.Layout.Step);
            using (var wrong = Image.CreateEmpty(10, 10, false, Image.Format.Rgba8))
                require(wrong.SavePng(System.IO.Path.Combine(broken, SteamwovenKitSet.FolderName(plan.Layout.Step), "board.charcreatebottombar.png")) == Error.Ok, $"[{tag}] the broken strip file written");
            SteamwovenKitStore.UseRoot(broken);
            art.QueueRedraw();
            await frames(3);
            var ownArts = new[] { AreaSaveListArtLayout.TopStripArt, AreaSaveListArtLayout.BottomStripArt, AreaSaveListArtLayout.BackgroundArt }.Where(number => art.TexturesForSelfTest.ContainsKey(number)).ToArray();
            require(art.KitScreenForSelfTest is null && art.OwnBackdropDrawnForSelfTest.SequenceEqual(ownArts) && ownArts.Length > 0,
                $"[{tag}] a strip that does not load: the screen draws its own arts whole (kit {(art.KitScreenForSelfTest is null ? "none" : "drawn")}, own {string.Join(",", art.OwnBackdropDrawnForSelfTest)} of {string.Join(",", ownArts)})");
            require(!SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)), $"[{tag}] a strip that does not load leaves no kit backdrop resident (the body was loaded first)");
        }
        finally
        {
            SteamwovenKitStore.UseRoot(kitRoot);
            try { System.IO.Directory.Delete(broken, true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-UI-3038 kit smoke: temporary folder not removed: " + exception.Message); }
        }
        art.QueueRedraw();
        await frames(3);
        require(art.KitScreenForSelfTest is not null && art.OwnBackdropDrawnForSelfTest.Count == 0, $"[{tag}] the kit is back with a whole set");
        // The HD option off (the Original look): nothing of the kit is read or drawn, the screen's own arts are the original's.
        var savedHd = _gameSettings.HdInterface;
        _gameSettings = _gameSettings with { HdInterface = false };
        SyncInterfaceHd();
        await frames(4);
        require(art.KitScreenForSelfTest is null && !SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)), $"[{tag}] with the HD option off nothing of the kit is drawn or resident");
        _gameSettings = _gameSettings with { HdInterface = savedHd };
        SyncInterfaceHd();
        await frames(4);
        require(art.KitScreenForSelfTest is not null, $"[{tag}] the kit screen is back with the HD option on");
    }

    /// <summary>The view of a character-family two-strip screen the kit smoke probes: the pre-made browser (ARC-UI-3039) and the new character screen (ARC-UI-3040) expose the same self-test surface.</summary>
    private sealed record KitCharacterView(string Name, SteamwovenKitScreen Screen, string Body, Color BodyColor, int[] OwnArts, Action Place, Action QueueRedraw, Func<IReadOnlyList<SteamwovenKitDraw>?> Kit,
        Func<IReadOnlyList<int>> OwnDrawn, Func<int, bool> Nearest, Func<IReadOnlyDictionary<int, Texture2D[]>> Textures);

    private static KitCharacterView KitPremadeView(AreaPremadeBrowserArtView art) => new("premade", SteamwovenKitScreen.PremadeBrowser, "backdrop.pregencharacterbase", KitPremadeBody,
        [AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt, AreaPremadeBrowserArtLayout.BackgroundArt], art.Place, art.QueueRedraw, () => art.KitScreenForSelfTest,
        () => art.OwnBackdropDrawnForSelfTest, art.KitSampledNearestForSelfTest, () => art.TexturesForSelfTest);

    private static KitCharacterView KitNewCharacterView(AreaNewCharacterArtView art) => new("newcharacter", SteamwovenKitScreen.NewCharacter, "backdrop.createcharacterbase", KitNewCharBody,
        [AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt, AreaNewCharacterArtLayout.BackgroundArt], art.Place, art.QueueRedraw, () => art.KitScreenForSelfTest,
        () => art.OwnBackdropDrawnForSelfTest, art.KitSampledNearestForSelfTest, () => art.TexturesForSelfTest);

    /// <summary>
    /// ARC-UI-3039, ARC-UI-3040 (census M07 and M08; ARC_STEAMWOVEN_KIT_SYNTHETIC=1 on the menu column pass, the "premade" and "newcharacter" screens): the real screen over the synthetic kit's three parts. At each size the view draws the composer's plan (the strips and the
    /// screen's own body tiling the plate, the body sampled nearest exactly where its draw is exact), none of the original's own arts, and the pixels read back are the kit's colours in each of the three rows and across both seams, with the
    /// portrait's and the story's fields black (the character draw fills them). With <paramref name="firstSize"/> it also checks the fallback (a bottom strip that does not load after the body did gives the screen's own three arts whole and
    /// no resident body of its own; the kit is back when the set is) and the HD option off (no kit at all); the body part goes with the screen (SWI-RUN-5) at the caller's close.
    /// </summary>
    private async Task CheckKitCharacterScreenAsync(string tag, SteamwovenFullScreenPlan plan, KitCharacterView art, bool firstSize, string kitRoot, Func<int, Task> frames, Action<bool, string> require)
    {
        var bodyId = art.Body;
        bool BodyResident() => SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith(bodyId, StringComparison.Ordinal));
        art.Place(); art.QueueRedraw();
        await frames(3);
        var set = SteamwovenKitStore.SetFor(plan.Layout.Step);
        var composed = set is null ? null : SteamwovenKitComposer.Screen(plan, set, art.Screen).Value;
        require(composed is not null, $"[{tag}] the synthetic set composes the {art.Name} screen");
        var drawn = art.Kit();
        require(drawn is not null && composed is not null && drawn.SequenceEqual(composed.AllDraws), $"[{tag}] the {art.Name} screen draws the composer's plan (got {(drawn is null ? "none" : string.Join(" ", drawn.Select(draw => draw.PartId + draw.Rect)))})");
        require(art.OwnDrawn().Count == 0, $"[{tag}] over the kit the screen draws none of its own arts (drew {string.Join(",", art.OwnDrawn())})");
        if (composed is null || drawn is null) return;
        require(art.Nearest(1) == composed.Body.Exact, $"[{tag}] the body is sampled {(composed.Body.Exact ? "nearest" : "linear")} as its draw is {(composed.Body.Exact ? "exact" : "scaled")}");
        require(BodyResident(), $"[{tag}] the screen's own body is resident while it is open");
        foreach (var other in SteamwovenKitTable.Screens.Values.Select(screen => screen.Body).OfType<string>().Where(other => other != bodyId))
            require(!SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith(other, StringComparison.Ordinal)), $"[{tag}] the body {other} of another screen is not resident");
        using (var image = GetViewport().GetTexture().GetImage())
        {
            var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
            Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
            Color AtUnit(double x, double y) { var (dx, dy) = plan.ToDevice(x, y); return Pixel((int)Math.Round(dx), (int)Math.Round(dy)); }
            // Clear of every field and button: the left margin of the body, the right margin below the story, the top strip row, the bottom strip row at its right end.
            var (top, body, bodyRight, bottom) = (AtUnit(400, 20), AtUnit(20, 60), AtUnit(775, 420), AtUnit(790, 465));
            require(Near(top, KitSaveTop), $"[{tag}] the top strip row is the kit's top strip (got {top})");
            require(Near(body, art.BodyColor) && Near(bodyRight, art.BodyColor), $"[{tag}] the body's margins are the kit's {art.Name} body (got {body} and {bodyRight})");
            require(Near(bottom, KitSaveBottom), $"[{tag}] the bottom strip row is the kit's bottom strip (got {bottom})");
            var x = (int)Math.Round(plan.ToDevice(20, 0).X);
            var xTop = (int)Math.Round(plan.ToDevice(400, 0).X);   // ARC-UI-3260: the top strip's iron sections stand over its two ends, so the row above the seam is read in the middle, between them
            var (aboveTop, belowTop) = (Pixel(xTop, composed.Body.Rect.Y - 2), Pixel(x, composed.Body.Rect.Y + 2));
            var (aboveBottom, belowBottom) = (Pixel(x, composed.Body.Rect.Bottom - 3), Pixel(x, composed.Bottom.Rect.Y + 2));
            require(Near(aboveTop, KitSaveTop) && Near(belowTop, art.BodyColor), $"[{tag}] the top strip meets the body with no gap (above {aboveTop}, below {belowTop})");
            require(Near(aboveBottom, art.BodyColor) && Near(belowBottom, KitSaveBottom), $"[{tag}] the body meets the bottom strip with no gap (above {aboveBottom}, below {belowBottom})");
            // The character draw fills its fields black itself, so nothing of the kit's body shows in the story's field.
            var story = AtUnit(AreaPremadeBrowserArtLayout.Description.X + AreaPremadeBrowserArtLayout.Description.Width - 6, AreaPremadeBrowserArtLayout.Description.Y + AreaPremadeBrowserArtLayout.Description.Height - 6);
            require(story.R8 + story.G8 + story.B8 < 12, $"[{tag}] the story's field is black (got {story})");
        }
        if (!firstSize) return;
        // Fallback: a set whose bottom strip file is another size than its manifest says (the body loads first, then the strip does not) gives the screen's own arts whole and no resident body (SWI-RUN-5); the same set whole is the kit again.
        var broken = System.IO.Directory.CreateTempSubdirectory($"arc-steamwoven-kit-{art.Name}-broken-").FullName;
        try
        {
            WriteSyntheticKitSet(broken, plan.Layout.Step);
            using (var wrong = Image.CreateEmpty(10, 10, false, Image.Format.Rgba8))
                require(wrong.SavePng(System.IO.Path.Combine(broken, SteamwovenKitSet.FolderName(plan.Layout.Step), "board.charcreatebottombar.png")) == Error.Ok, $"[{tag}] the broken strip file written");
            SteamwovenKitStore.UseRoot(broken);
            art.QueueRedraw();
            await frames(3);
            var ownArts = art.OwnArts.Where(number => art.Textures().ContainsKey(number)).ToArray();
            require(art.Kit() is null && art.OwnDrawn().SequenceEqual(ownArts) && ownArts.Length > 0,
                $"[{tag}] a strip that does not load: the screen draws its own arts whole (kit {(art.Kit() is null ? "none" : "drawn")}, own {string.Join(",", art.OwnDrawn())} of {string.Join(",", ownArts)})");
            require(!BodyResident(), $"[{tag}] a strip that does not load leaves no kit body resident (the body was loaded first)");
        }
        finally
        {
            SteamwovenKitStore.UseRoot(kitRoot);
            try { System.IO.Directory.Delete(broken, true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-UI-3040 kit smoke: temporary folder not removed: " + exception.Message); }
        }
        art.QueueRedraw();
        await frames(3);
        require(art.Kit() is not null && art.OwnDrawn().Count == 0, $"[{tag}] the kit is back with a whole set");
        // The HD option off (the Original look): nothing of the kit is drawn or resident, the screen's own arts are the original's.
        var savedHd = _gameSettings.HdInterface;
        _gameSettings = _gameSettings with { HdInterface = false };
        SyncInterfaceHd();
        await frames(4);
        require(art.Kit() is null && !BodyResident(), $"[{tag}] with the HD option off nothing of the kit is drawn or resident");
        _gameSettings = _gameSettings with { HdInterface = savedHd };
        SyncInterfaceHd();
        await frames(4);
        require(art.Kit() is not null, $"[{tag}] the kit screen is back with the HD option on");
    }

    /// <summary>The view of a window-bodied two-strip screen the kit smoke probes (ARC-UI-3041): the creation editor and the opening shop draw their strips and their window's kit background through the shop's view; the window frame over it is probed by
    /// <paramref name="FrameOwnArt"/> (true while that frame draws its own backdrop art).</summary>
    private sealed record KitWindowScreenView(string Name, SteamwovenKitScreen Screen, IReadOnlyList<string> BodyParts, IReadOnlyList<string> Overlays, Func<IReadOnlyList<Color>> BodyColors, IReadOnlyList<(double X, double Y)> BodyPoints,
        Action Place, Action QueueRedraw, Func<IReadOnlyList<SteamwovenKitDraw>?> Kit, Func<IReadOnlyList<int>> OwnDrawn, Func<int, bool> Nearest, Func<IReadOnlyDictionary<int, Texture2D[]>> Textures, Func<bool> FrameOwnArt, Action ResetFrame);

    private KitWindowScreenView KitEditorView() => new("editor", SteamwovenKitScreen.CharacterEditor, ["backdrop.char_maint"], [], () => [KitCharacter], [(4, 41 + 395)],
        _mainMenuPanel!.ShopArtForSelfTest.Place, _mainMenuPanel.ShopArtForSelfTest.QueueRedraw, () => _mainMenuPanel.ShopArtForSelfTest.KitScreenForSelfTest, () => _mainMenuPanel.ShopArtForSelfTest.OwnBackdropDrawnForSelfTest,
        _mainMenuPanel.ShopArtForSelfTest.KitSampledNearestForSelfTest, () => _mainMenuPanel.ShopArtForSelfTest.TexturesForSelfTest,
        () => _editorPanel!.ArtViewForSelfTest!.OwnBackgroundDrawnForSelfTest,
        () => { AreaKitStandIns.ResetForSelfTest(); _editorPanel!.ArtViewForSelfTest!.QueueRedraw(); });

    private KitWindowScreenView KitShopView() => new("shop", SteamwovenKitScreen.OpeningShop, ["backdrop.barter", "backdrop.inventor"], [SteamwovenKitTable.BarterDoll.Part, SteamwovenKitTable.HeroDollCover.Part], () => [KitBarter, KitInventory],
        [(4, 41 + 395), (796, 41 + 395)], _mainMenuPanel!.ShopArtForSelfTest.Place, _mainMenuPanel.ShopArtForSelfTest.QueueRedraw, () => _mainMenuPanel.ShopArtForSelfTest.KitScreenForSelfTest, () => _mainMenuPanel.ShopArtForSelfTest.OwnBackdropDrawnForSelfTest,
        _mainMenuPanel.ShopArtForSelfTest.KitSampledNearestForSelfTest, () => _mainMenuPanel.ShopArtForSelfTest.TexturesForSelfTest,
        () => _barterPanel!.GridAreaForSelfTest.KitDrawForSelfTest is { Background: true } or { Pack: true },
        () => _barterPanel!.GridAreaForSelfTest.QueueRedraw());

    /// <summary>
    /// ARC-UI-3041 (census M09 and M10; ARC_STEAMWOVEN_KIT_SYNTHETIC=1 on the editor's and the shop's column pass): the real creation editor and the real opening shop over the synthetic kit. At each size the strips view draws the composer's plan (the top
    /// strip, the window's kit background pieces tiling the body, the bottom strip, each sampled nearest exactly where its draw is exact), none of the original's strip arts 336 and 335, the window over it skips only its own backdrop (the sheet's art 22; the barter frame's
    /// background and pack art) and the pixels read back are the kit's colours in the three rows, in the body's margins and across the seams (and across the barter window's two pieces). The kit's body, and its overlays for the barter window, are resident only while the
    /// screen is open and no other screen's body is. With <paramref name="firstSize"/> it also checks the fallback (a bottom strip that does not load after the body did gives the screen's own strips whole, the window over it draws its own backdrop again and no body of
    /// the screen stays resident; the kit is back when the set is) and the HD option off (no kit at all).
    /// </summary>
    private async Task CheckKitWindowScreenAsync(string tag, SteamwovenFullScreenPlan plan, KitWindowScreenView art, bool firstSize, string kitRoot, Func<int, Task> frames, Action<bool, string> require)
    {
        IEnumerable<string> ResidentBackdrops() => SteamwovenKitStore.ResidentForSelfTest.Select(System.IO.Path.GetFileName).OfType<string>().Where(name => name.StartsWith("backdrop.", StringComparison.Ordinal)).Select(name => name[..^4]);
        var expected = art.BodyParts.Concat(art.Overlays).Order().ToList();
        art.Place(); art.QueueRedraw(); art.ResetFrame();
        await frames(3);
        var set = SteamwovenKitStore.SetFor(plan.Layout.Step);
        var composed = set is null ? null : SteamwovenKitComposer.Screen(plan, set, art.Screen).Value;
        require(composed is not null, $"[{tag}] the synthetic set composes the {art.Name} screen");
        var drawn = art.Kit();
        require(drawn is not null && composed is not null && drawn.SequenceEqual(composed.AllDraws), $"[{tag}] the {art.Name} screen draws the composer's plan (got {(drawn is null ? "none" : string.Join(" ", drawn.Select(draw => draw.PartId + draw.Rect)))})");
        require(art.OwnDrawn().Count == 0, $"[{tag}] over the kit the screen draws none of its own strip arts (drew {string.Join(",", art.OwnDrawn())})");
        require(!art.FrameOwnArt(), $"[{tag}] the window over the {art.Name} body draws none of its own backdrop art");
        if (composed is null || drawn is null) return;
        for (var i = 0; i < drawn.Count; i++)
            require(art.Nearest(i) == drawn[i].Exact, $"[{tag}] part {drawn[i].PartId} is sampled {(drawn[i].Exact ? "nearest" : "linear")} as its draw is {(drawn[i].Exact ? "exact" : "scaled")}");
        require(ResidentBackdrops().Order().SequenceEqual(expected), $"[{tag}] only the {art.Name} body and its overlays are resident (got {string.Join(",", ResidentBackdrops().Order())}, expected {string.Join(",", expected)})");
        using (var image = GetViewport().GetTexture().GetImage())
        {
            var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
            Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
            Color AtUnit(double x, double y) { var (dx, dy) = plan.ToDevice(x, y); return Pixel((int)Math.Round(dx), (int)Math.Round(dy)); }
            var (top, bottom) = (AtUnit(400, 20), AtUnit(400, 465));
            require(Near(top, KitSaveTop), $"[{tag}] the top strip row is the kit's top strip (got {top})");
            require(Near(bottom, KitSaveBottom), $"[{tag}] the bottom strip row is the kit's bottom strip (got {bottom})");
            var colours = art.BodyColors();
            for (var i = 0; i < art.BodyPoints.Count; i++)
            {
                var point = AtUnit(art.BodyPoints[i].X, art.BodyPoints[i].Y);
                require(Near(point, colours[Math.Min(i, colours.Count - 1)]), $"[{tag}] the body's margin at {art.BodyPoints[i]} is the kit's {art.Name} body (got {point})");
            }
            // The seams of the strips with the body: two device rows either side of its top and bottom edges, at the body's left margin.
            var x = (int)Math.Round(plan.ToDevice(art.BodyPoints[0].X, 0).X);
            var xTop = (int)Math.Round(plan.ToDevice(400, 0).X);   // ARC-UI-3260: the top strip's iron sections stand over its two ends, so the row above the seam is read in the middle, between them
            var (aboveTop, belowTop) = (Pixel(xTop, composed.Body.Rect.Y - 2), Pixel(x, composed.Body.Rect.Y + 2));
            var (aboveBottom, belowBottom) = (Pixel(x, composed.Body.Rect.Bottom - 3), Pixel(x, composed.Bottom.Rect.Y + 2));
            require(Near(aboveTop, KitSaveTop) && Near(belowTop, colours[0]), $"[{tag}] the top strip meets the body with no gap (above {aboveTop}, below {belowTop})");
            require(Near(aboveBottom, colours[0]) && Near(belowBottom, KitSaveBottom), $"[{tag}] the body meets the bottom strip with no gap (above {aboveBottom}, below {belowBottom})");
            if (composed.BodyPieces.Count > 1)
            {
                // The barter window's two pieces meet at their joint: the pixel row just below the strip's seam, one device pixel either side.
                var joint = composed.BodyPieces[0].Rect.Right;
                var y = composed.Body.Rect.Y + 3;
                var (leftOfJoint, rightOfJoint) = (Pixel(joint - 2, y), Pixel(joint + 1, y));
                require(Near(leftOfJoint, colours[0]) && Near(rightOfJoint, colours[1]), $"[{tag}] the merchant's side meets the inventory's piece with no gap (left {leftOfJoint}, right {rightOfJoint})");
            }
        }
        if (!firstSize) return;
        // Fallback: a set whose bottom strip file is another size than its manifest says (the body loads first, then the strip does not) gives the screen's own strips whole, the window over it its own backdrop and no resident body (SWI-RUN-5); the same
        // set whole is the kit again.
        var broken = System.IO.Directory.CreateTempSubdirectory($"arc-steamwoven-kit-{art.Name}-broken-").FullName;
        try
        {
            WriteSyntheticKitSet(broken, plan.Layout.Step);
            using (var wrong = Image.CreateEmpty(10, 10, false, Image.Format.Rgba8))
                require(wrong.SavePng(System.IO.Path.Combine(broken, SteamwovenKitSet.FolderName(plan.Layout.Step), "board.charcreatebottombar.png")) == Error.Ok, $"[{tag}] the broken strip file written");
            SteamwovenKitStore.UseRoot(broken);
            art.QueueRedraw(); art.ResetFrame();
            await frames(3);
            var ownArts = new[] { AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt }.Where(number => art.Textures().ContainsKey(number)).ToArray();
            require(art.Kit() is null && art.OwnDrawn().SequenceEqual(ownArts) && ownArts.Length > 0,
                $"[{tag}] a strip that does not load: the screen draws its own strips whole (kit {(art.Kit() is null ? "none" : "drawn")}, own {string.Join(",", art.OwnDrawn())} of {string.Join(",", ownArts)})");
            require(art.FrameOwnArt(), $"[{tag}] a strip that does not load: the window over the body draws its own backdrop art again");
            require(!ResidentBackdrops().Any(), $"[{tag}] a strip that does not load leaves no kit body resident (the body was loaded first): {string.Join(",", ResidentBackdrops())}");
        }
        finally
        {
            SteamwovenKitStore.UseRoot(kitRoot);
            try { System.IO.Directory.Delete(broken, true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-UI-3041 kit smoke: temporary folder not removed: " + exception.Message); }
        }
        art.QueueRedraw(); art.ResetFrame();
        await frames(3);
        require(art.Kit() is not null && art.OwnDrawn().Count == 0 && !art.FrameOwnArt(), $"[{tag}] the kit is back with a whole set");
        // The HD option off (the Original look): nothing of the kit is drawn or resident, the screen's own arts are the original's.
        var savedHd = _gameSettings.HdInterface;
        _gameSettings = _gameSettings with { HdInterface = false };
        SyncInterfaceHd();
        art.ResetFrame();
        await frames(4);
        require(art.Kit() is null && !ResidentBackdrops().Any(), $"[{tag}] with the HD option off nothing of the kit is drawn or resident");
        _gameSettings = _gameSettings with { HdInterface = savedHd };
        SyncInterfaceHd();
        art.ResetFrame();
        await frames(4);
        require(art.Kit() is not null, $"[{tag}] the kit screen is back with the HD option on");
    }

    /// <summary>ARC-UI-2461 (SWI-BDR-6): the full-screen plates of the column menu pass (ARC_STEAMWOVEN_KIT_SYNTHETIC=1 on the menu smoke): the synthetic kit's main and options plates are the ones drawn, at the plate's corners, and
    /// the plate is nearest-sampled exactly where its baked pixels are the device pixels.</summary>
    private async Task CheckKitPlateAsync(string tag, SteamwovenFullScreenPlan layout, string screen, AreaMenuArtView art, Func<int, Task> frames, Action<bool, string> require)
    {
        var (expectedId, colour) = screen == "main" ? ("backdrop.mainmenuback", KitPlate) : ("backdrop.optionsmenuback", KitOptionsPlate);
        art.Place(); art.QueueRedraw();
        await frames(3);
        require(art.KitPlateForSelfTest == expectedId, $"[{tag}] the {screen} window draws the kit plate {expectedId}, got {art.KitPlateForSelfTest}");
        var exact = SteamwovenKitStore.SetFor(layout.Layout.Step) is { } set && set.TryGet(expectedId, out var part) && part.Width == layout.Zone.Width && part.Height == layout.Zone.Height;
        require(art.KitPlateExactForSelfTest == exact && art.TextureFilter == CanvasItem.TextureFilterEnum.Linear, $"[{tag}] the plate's own sampling is {(exact ? "nearest" : "linear")} as its draw is {(exact ? "exact" : "scaled")} and the labels keep the view's linear filter");
        using var image = GetViewport().GetTexture().GetImage();
        var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
        var (topLeft, bottomRight) = (Pixel(layout.Zone.X + 8, layout.Zone.Y + 8), Pixel(layout.Zone.Right - 8, layout.Zone.Bottom - 8));
        require(Near(topLeft, colour) && Near(bottomRight, colour), $"[{tag}] the plate's corners are the kit plate's colour (got {topLeft} and {bottomRight})");
    }

    /// <summary>
    /// ARC-UI-3036 (census G12 to G14, AC2, AC3): the real world map window over the synthetic kit's <c>backdrop.mapmain</c> (a map view, a continent view and, when the data has one, a town map are one window, interface mode 5). The kit background shows
    /// under it, the window draws no frame art of its own (art 138), and the frame, the map view's hole and the lens read back by pixel: a bare piece of the frame is the kit's colour, the view's and the lens's squares are not. With the part missing
    /// (<paramref name="withoutMap"/>, at the first size) the window keeps its own art and no kit piece is resident; the window closed leaves no trace of the part (SWI-RUN-5).
    /// </summary>
    private async Task CheckKitWorldMapAsync(string tag, SteamwovenHudPlan plan, AreaGameBarPanel panel, Func<int, Task> frames, Action<bool, string> require, Action? withoutMap, Action restore)
    {
        OpenWorldMap(); await frames(4);
        if (!WorldMapOpen || _worldMapPanel?.ArtViewForSelfTest is not { } view) { GD.Print($"ARC-UI-3036 steamwoven {tag} the world map did not open here (not checked)"); if (WorldMapOpen) CloseWorldMapByPlayer(); return; }
        for (var wait = 0; wait < 600 && !view.KitBackgroundShown; wait++) await frames(1);
        await frames(2);
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        require(AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.WorldMap && panel.KitForSelfTest.Background.Count == 1 && view.KitBackgroundShown, $"[{tag}] the open world map takes the kit's map background (window {AreaGameBarPanel.KitBackgroundWindow}, pieces {panel.KitForSelfTest.Background.Count})");
        require(AreaLensPlace.Unified, $"[{tag}] with the kit's map background published the lenses stand at the kit's common place (ARC-UI-2714, ARC-UI-3143)");
        var piece = panel.KitForSelfTest.Background.Single();
        require(piece.Rect.X == zone.X && piece.Rect.Right == zone.Right && piece.Rect.Y == zone.Y && piece.Rect.Bottom == zone.Bottom, $"[{tag}] the map background tiles the zone {zone}, got {piece.Rect}");
        // Where each probe stands: a bare piece of the frame (left of the zoom button, clear of the buttons and the lens) is the kit's colour; the centre of the map view and of the lens are not (the view draws its own black and picture, the lens the game's view).
        foreach (var (kind, name) in new[] { (AreaWorldMapKind.Map, "map view"), (AreaWorldMapKind.Continent, "continent view"), (AreaWorldMapKind.Town, "town map") })
        {
            if (kind == AreaWorldMapKind.Town && !view.HasTown) continue;
            view.SetKind(kind);
            await frames(3);
            require(view.Kind == kind && view.KitBackgroundShown, $"[{tag}] the {name} is shown over the kit's background");
            using var image = GetViewport().GetTexture().GetImage();
            Color Pixel(Vector2 point) => image.GetPixel(Math.Clamp((int)point.X, 0, image.GetWidth() - 1), Math.Clamp((int)point.Y, 0, image.GetHeight() - 1));
            var frame = Pixel(view.WindowPointForSelfTest(12, 215));
            var inView = Pixel(view.WindowPointForSelfTest(AreaWorldMapArtLayout.View.X + AreaWorldMapArtLayout.View.Width / 2.0, AreaWorldMapArtLayout.View.Y + AreaWorldMapArtLayout.View.Height / 2.0));
            var inLens = Pixel(view.WindowPointForSelfTest(AreaWorldMapArtLayout.Lens.X + AreaWorldMapArtLayout.Lens.Side / 2.0, AreaWorldMapArtLayout.Lens.Y + AreaWorldMapArtLayout.Lens.Side / 2.0));
            require(Near(frame, KitMap), $"[{tag}] the {name}'s frame is the kit's colour (got {frame})");
            require(!Near(inView, KitMap), $"[{tag}] the {name}'s hole is clear of the kit's colour (got {inView})");
            require(!Near(inLens, KitMap), $"[{tag}] the lens's hole is clear of the kit's colour (got {inLens})");
            // The kit's frame has no compass letters: the window writes N E S W in the art's dark red at the places of the original's.
            foreach (var (letter, (cx, cy)) in AreaWorldMapArtLayout.CompassLetters)
            {
                var found = false;
                for (var dy = -6; dy <= 6 && !found; dy++)
                    for (var dx = -6; dx <= 6 && !found; dx++)
                    {
                        var c = Pixel(view.WindowPointForSelfTest(cx + dx, cy + dy));
                        found = c.R > 0.35f && c.G < 0.2f && c.B < 0.2f;
                    }
                require(found, $"[{tag}] the {name} writes the compass letter {letter} near ({cx}, {cy}) of the window");
            }
            if (kind == AreaWorldMapKind.Map) await CaptureHudAsync("steamwoven-" + tag + "-kit-map", frames);
            else if (kind == AreaWorldMapKind.Continent) await CaptureHudAsync("steamwoven-" + tag + "-kit-continent", frames);
            else await CaptureHudAsync("steamwoven-" + tag + "-kit-town", frames);
        }
        view.ShowMapOrTown();
        require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)) == 1, $"[{tag}] only the map's piece is resident while the map is open");
        CloseWorldMapByPlayer(); await frames(4);
        require(!WorldMapOpen && AreaGameBarPanel.KitBackgroundWindow is null && !SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)), $"[{tag}] the map closed leaves no kit background resident");
        require(!AreaLensPlace.Unified, $"[{tag}] the map closed leaves the lenses at the windows' own places");
        if (withoutMap is null) return;
        // SWI-BDR-3: a set without the piece leaves the window's own (pack) art.
        withoutMap();
        panel.Relayout(); OpenWorldMap(); await frames(6);
        require(WorldMapOpen && AreaGameBarPanel.KitBackgroundWindow is null && panel.KitForSelfTest.Background.Count == 0 && !view.KitBackgroundShown, $"[{tag}] with the map piece missing the window keeps its own art");
        require(!AreaLensPlace.Unified, $"[{tag}] with the map piece missing the lens stays at the window's own place");
        CloseWorldMapByPlayer(); await frames(3);
        restore();
        panel.Relayout(); await frames(4);
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 0.02f && Math.Abs(a.G - b.G) < 0.02f && Math.Abs(a.B - b.B) < 0.02f;

    /// <summary>
    /// ARC-UI-3032 (AC2): the real loot window over the kit background: a junk pile of the hero's dropped coin is opened in the loot window and the window draws neither of its background arts (234 and 221) and takes both doll covers
    /// from the kit; with the kit background gone it draws the arts again and no cover. The step is skipped, and says so, when the smoke's hero has no coin to drop, no known tile or the original arts do not read (the loot look).
    /// </summary>
    private async Task CheckKitLootWindowAsync(string tag, AreaGameBarPanel panel, string root, Func<int, Task> frames, Action<bool, string> require)
    {
        string? skip = null;
        string? pile = null;
        var session = _areaSession;
        var goldBefore = session?.QueryGold() ?? 0;
        if (session is null || !InventoryActive || InventoryOpen) skip = "no session or the inventory is already open";
        else if (session.QueryGold() < 2) skip = "the hero has fewer than two coins";
        else if (!HeroLocationKnown()) skip = "the hero's tile is not known";
        else
        {
            // Two coins dropped one after the other lie on the hero's tile as a junk pile (a single dropped item stays a plain ground item).
            var dropped = session.DropHeroGold(session.Version, 1, GoldPileKind());
            var second = session.DropHeroGold(session.Version, 1, GoldPileKind());
            pile = dropped.Disposition == GameSessionDisposition.Changed && second.Disposition == GameSessionDisposition.Changed
                ? session.QueryJunkPiles().FirstOrDefault(p => p.Items.Any(item => item.Entity == dropped.Item || item.Entity == second.Item))?.Entity : null;
            if (pile is null) skip = $"the drop makes no junk pile (dispositions {dropped.Disposition}, {second.Disposition}, piles {session.QueryJunkPiles().Count})";
        }
        if (skip is null)
        {
            // The real host names the kit window from the interface stack: the smoke override stands down while the loot window is open.
            var override_ = _smokeKitBackdrop;
            _smokeKitBackdrop = null;
            // The same window with no kit (the "before" of the owner's composite): the window draws its own arts 234 and 221 and no kit cover.
            SteamwovenKitStore.UseRoot(null);
            panel.Relayout();
            await frames(4);
            OpenInventory(pile);
            await frames(4);
            if (_inventoryPanel?.GridAreaForSelfTest is { LootLook: true } plain)
            {
                plain.QueueRedraw();
                await frames(2);
                var (plainBackgrounds, plainCovers) = plain.LootDrawForSelfTest;
                require(AreaGameBarPanel.KitBackgroundWindow is null && !plain.KitBackgroundShown && plainBackgrounds.SequenceEqual([234, 221]) && plainCovers.Count == 0,
                    $"[{tag}] with no kit the loot window draws its own arts 234 and 221 and no kit cover (drew {string.Join(",", plainBackgrounds)}, kit covers {string.Join(",", plainCovers)})");
                await CaptureHudAsync("steamwoven-" + tag + "-loot-before", frames);
            }
            EndInventory();
            await frames(3);
            SteamwovenKitStore.UseRoot(root);
            panel.Relayout();
            await frames(4);
            OpenInventory(pile);
            await frames(4);
            var area = _inventoryPanel?.GridAreaForSelfTest;
            if (!InventoryOpen || area is null || !area.LootLook) skip = "the loot window did not open in its original look";
            else
            {
                require(AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.Loot, $"[{tag}] the open loot window takes the kit's loot background");
                // Both doll views on (a pile has no body doll, so these show only where the object allows them): the covers drawn are the kit's.
                area.SetBodyDoll(true); area.SetHeroDoll(true);
                area.QueueRedraw();
                await frames(2);
                var (backgrounds, covers) = area.LootDrawForSelfTest;
                require(area.KitBackgroundShown && backgrounds.Count == 0, $"[{tag}] over the kit background the loot window draws no background art of its own (drew {string.Join(",", backgrounds)})");
                // The hero's doll cover shows on a pile too (only the body's doll view needs a body), so it is asserted positively.
                require(!area.HeroDoll || covers.Contains(341), $"[{tag}] the hero's doll cover is the kit's when its view is on (kit covers {string.Join(",", covers)})");
                require(covers.Contains(349) == area.BodyDoll && covers.Contains(341) == area.HeroDoll, $"[{tag}] the doll covers it draws are the kit's (kit covers {string.Join(",", covers)}, body doll {area.BodyDoll}, hero doll {area.HeroDoll})");
                await CaptureHudAsync("steamwoven-" + tag + "-loot-open", frames);
                EndInventory();
                await frames(3);
            }
            _smokeKitBackdrop = override_;
            if (InventoryOpen) EndInventory();
            // The coins come back out of the pile one by one (a pile's items are looted, not picked up from the ground).
            if (pile is not null && _areaSession is { } cleanup)
                foreach (var item in cleanup.QueryJunkPiles().Where(p => p.Entity == pile).SelectMany(p => p.Items).ToList()) _inventoryHost?.Loot(cleanup, pile, item.Entity);
            require(_areaSession is { } after && after.QueryGold() == goldBefore, $"[{tag}] the two dropped coins are back in the hero's purse (had {goldBefore}, has {_areaSession?.QueryGold()})");
        }
        if (skip is not null) GD.Print($"ARC-UI-3032 kit-loot-window {tag} SKIP {skip}");
    }

    /// <summary>
    /// ARC-UI-3035 (census G06 to G09; the NPC sheet G10 is the same window in the same interface mode, read-only): the open sheet takes the kit's character backdrop, and each page lays its kit sheet over it, read back by pixel, in
    /// place of its own panel art (29, 30, 31, 567); the sheets go with the window. With <paramref name="kitSheets"/> false the set lacks the four sheets: the background is still the kit's and every page keeps its own art (the fallback).
    /// </summary>
    private async Task CheckKitSheetPagesAsync(AreaGameBarPanel panel, string tag, Func<int, Task> frames, Action<bool, string> require, bool kitSheets = true)
    {
        OpenSheet(); await frames(5);
        require(SheetOpen && panel.KitForSelfTest.Background is [{ PartId: "backdrop.char_maint" }], $"[{tag}] the open sheet takes the kit's character backdrop (got {string.Join(",", panel.KitForSelfTest.Background.Select(draw => draw.PartId))})");
        require(kitSheets ? AreaKitSheets.ShownForSelfTest.SequenceEqual([29, 30, 31, 567]) : AreaKitSheets.ShownForSelfTest.Count == 0, $"[{tag}] the page sheets are {(kitSheets ? "the kit's" : "left to the pages' own art")} (got {string.Join(",", AreaKitSheets.ShownForSelfTest)})");
        var view = _sheetPanel!.ArtViewForSelfTest!;
        // A corner of the page's rectangle holds no text or button: the probe reads the sheet colour of the page's kit part.
        var pages = new (AreaSheetTab Tab, string Name, Func<bool> Visible, Func<bool> Shown, Func<double, double, Vector2> Point, Color Colour)[]
        {
            (AreaSheetTab.Skills, "skills", () => view.Pages.Visible, () => view.Pages.KitSheetShownForSelfTest, view.Pages.PointForSelfTest, KitSkillsSheet),
            (AreaSheetTab.Tech, "tech", () => view.Pages.Visible, () => view.Pages.KitSheetShownForSelfTest, view.Pages.PointForSelfTest, KitTechSheet),
            (AreaSheetTab.Spells, "spells", () => view.Spells.Visible, () => view.Spells.KitSheetShownForSelfTest, view.Spells.PointForSelfTest, KitSpellsSheet),
            (AreaSheetTab.Schemes, "schemes", () => view.Schemes.Visible, () => view.Schemes.KitSheetShownForSelfTest, view.Schemes.PointForSelfTest, KitSchemesSheet),
        };
        foreach (var page in pages)
        {
            _sheetPanel.SelectTabForSelfTest(page.Tab);
            await frames(4);
            require(page.Visible() && page.Shown() == kitSheets, $"[{tag}] the {page.Name} page shows and draws {(kitSheets ? "its kit sheet" : "its own panel art (no kit sheet)")}");
            // ARC-UI-3101: over the kit's sheet the page's round buttons go through the round-part layer (cut to their gold circle, with the generated shadow); on the page's own art none does.
            var rounds = page.Name switch { "skills" => view.Pages.RoundPartsForSelfTest.ShownForSelfTest, "tech" => view.Pages.RoundPartsForSelfTest.ShownForSelfTest, "spells" => view.Spells.RoundPartsForSelfTest.ShownForSelfTest, _ => [] };
            IReadOnlyList<int> expectedRounds = !kitSheets ? [] : page.Name switch
            {
                "skills" => [.. Enumerable.Range(AreaCharacterSheetPagesLayout.GroupTabArtBase, AreaCharacterSheetPagesLayout.Groups)],
                "tech" => [.. Enumerable.Range(AreaCharacterSheetPagesLayout.DisciplineArtBase, AreaCharacterSheetPagesLayout.Disciplines)],
                "spells" => [.. AreaCharacterSheetSpellsLayout.CollegeArtNumbers],
                _ => [],
            };
            require(rounds.SequenceEqual(expectedRounds), $"[{tag}] the {page.Name} page draws its round buttons {(kitSheets ? "through the round-part layer" : "as they are")} (got {string.Join(",", rounds)})");
            using var shot = GetViewport().GetTexture().GetImage();
            var ratio = new Vector2(shot.GetWidth(), shot.GetHeight()) / GetViewport().GetVisibleRect().Size;
            var at = page.Point(3, 321);
            var probe = shot.GetPixel(Math.Clamp((int)(at.X * ratio.X), 0, shot.GetWidth() - 1), Math.Clamp((int)(at.Y * ratio.Y), 0, shot.GetHeight() - 1));
            require(Near(probe, page.Colour) == kitSheets, $"[{tag}] the {page.Name} page's pixels are {(kitSheets ? "" : "not ")}its kit sheet's colour (got {probe})");
            await CaptureHudAsync("steamwoven-" + tag + "-sheet-" + page.Name + (kitSheets ? "" : "-own"), frames);
        }
        require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("sheet.", StringComparison.Ordinal)) == (kitSheets ? 4 : 0), $"[{tag}] the open sheet holds {(kitSheets ? "its four kit sheets" : "no kit sheet")} in memory");
        EndSheet(); await frames(3);
        require(!SheetOpen && AreaKitSheets.ShownForSelfTest.Count == 0 && panel.KitForSelfTest.Background.Count == 0, $"[{tag}] the kit sheets go with the window");
        // SWI-RUN-5: the open window's sheets are resident while it is open and released with it.
        require(!SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("sheet.", StringComparison.Ordinal)), $"[{tag}] no kit sheet stays resident after the window closes");
    }

    /// <summary>
    /// ARC-UI-3037 (census G01, G05, G10 to G12, G15, G17): each covered window, opened for real, draws no background art of its own over a complete kit background and draws it otherwise. The kit's parts are flat colours, so the share
    /// of the zone's pixels (sampled) that are the window's kit colours says which one shows: most of the zone with the kit, none without (<paramref name="kit"/> false: the set lacks each window's backdrop, the
    /// window's own art stands). The window's controls, text and holes are drawn either way. The identify and repair windows are the inventory's draw path (<c>WindowOf</c> is unit-tested).
    /// </summary>
    private async Task CheckKitWindowBackgroundsAsync(AreaGameBarPanel panel, SteamwovenHudPlan plan, string tag, Func<int, Task> frames, Action<bool, string> require, bool kit, bool fixtures = true)
    {
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        (string Name, Action Open, Func<bool> IsOpen, Action Close, SteamwovenKitWindow Window, Color[] Colours)[] windows =
        [
            ("inventory", () => OpenInventory(), () => InventoryOpen, EndInventory, SteamwovenKitWindow.Inventory, [KitDoll, KitInventory]),
            ("sheet", OpenSheet, () => SheetOpen, EndSheet, SteamwovenKitWindow.Character, [KitCharacter]),
            ("logbook", OpenLogbook, () => LogbookOpen, CloseLogbookByPlayer, SteamwovenKitWindow.Logbook, [KitLogSide, KitLogBook]),
            ("schematics", () => OpenSchematics(), () => SchematicsOpen, CloseSchematicsByPlayer, SteamwovenKitWindow.Schematic, [KitSchematic]),
        ];
        foreach (var (name, open, isOpen, close, window, colours) in windows)
        {
            if (isOpen()) close();
            await frames(3);
            AreaKitStandIns.ResetForSelfTest();
            AreaKitFixtures.ResetForSelfTest();
            open();
            await frames(6);
            require(isOpen(), $"[{tag}] the {name} window opens");
            require(AreaGameBarPanel.KitBackgroundWindow == (kit ? window : null) && (panel.KitForSelfTest.Background.Count > 0) == kit, $"[{tag}] the {name} window is {(kit ? "published as standing over its kit background" : "left to its own art (no complete kit background)")} (got {AreaGameBarPanel.KitBackgroundWindow})");
            require(AreaLensPlace.Unified == kit, $"[{tag}] the {name} window with {(kit ? "its kit background published" : "no kit background")} has the lens at {(kit ? "the kit's common place" : "its own place")} (Unified {AreaLensPlace.Unified})");
            await frames(3);
            // The window's own draw took the kit branch (it drew its stand-ins for the elements the old art held) exactly when the kit's background shows.
            require(AreaKitStandIns.DrawnForSelfTest.Contains(window) == kit, $"[{tag}] the {name} window {(kit ? "drew its stand-in fields over the kit's background" : "drew its own art, no stand-in fields")}");
            // ARC-UI-3098: the window's fixtures that the set holds are the ones published and drawn from the kit; with the set lacking them none is, and the stand-ins are (the pixel probes of the sheet below say which).
            var expectedFixtures = kit && fixtures ? SteamwovenKitTable.FixturesOf(window).Order().ToArray() : [];
            require(AreaKitFixtures.ShownForSelfTest.SequenceEqual(expectedFixtures), $"[{tag}] the {name} window shows {(expectedFixtures.Length > 0 ? "its fixtures" : "no fixture")} from the kit (got {string.Join(", ", AreaKitFixtures.ShownForSelfTest)})");
            require(expectedFixtures.All(id => AreaKitFixtures.DrawnForSelfTest.Contains(id)) && (expectedFixtures.Length > 0 || AreaKitFixtures.DrawnForSelfTest.Count == 0), $"[{tag}] the {name} window draws every fixture the kit has (drawn {string.Join(", ", AreaKitFixtures.DrawnForSelfTest)})");
            // ARC-UI-3101: a surface that carries its lens ring puts the lens at the circle inside it (92 units, its own place), no ring art over it; without the surface the original's 89 units.
            require(AreaLensPlace.Surface == (kit ? AreaKitLens.CornerOf(window) : null), $"[{tag}] the {name} window's lens place is {(kit ? "the surface's" : "its own")} (got {AreaLensPlace.Surface})");
            var lensUnit = SteamwovenWindowZone.UnitScale(plan);
            foreach (var lens in AreaRoundView.VisibleRects()) require(Math.Abs(lens.Size.X - (kit ? AreaLensPlace.SurfaceSide : 89) * lensUnit) <= 1, $"[{tag}] the {name} lens is {(kit ? AreaLensPlace.SurfaceSide : 89)} units wide ({lens.Size.X / lensUnit:F2})");
            using (var shot = GetViewport().GetTexture().GetImage())
            {
                var ratio = new Vector2(shot.GetWidth(), shot.GetHeight()) / GetViewport().GetVisibleRect().Size;
                int total = 0, hits = 0;
                var perColour = new int[colours.Length];
                for (var y = zone.Y + 6; y < zone.Bottom - 6; y += 6)
                    for (var x = zone.X + 6; x < zone.Right - 6; x += 6)
                    {
                        var pixel = shot.GetPixel(Math.Clamp((int)(x * ratio.X), 0, shot.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, shot.GetHeight() - 1));
                        total++;
                        if (colours.Any(colour => Near(pixel, colour))) hits++;
                        for (var c = 0; c < colours.Length; c++) if (Near(pixel, colours[c])) perColour[c]++;
                    }
                var share = total == 0 ? 0 : (double)hits / total;
                // Every piece of the window's background shows (a window that still drew only one of its two old arts would hide the other piece).
                var lookCapture = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_KIT_LOOK") is { Length: > 0 };      // ARC-UI-3165: a look capture puts a real surface in place of the flat colours, so the colour shares are not asked
                require(!kit || lookCapture || perColour.All(count => total > 0 && (double)count / total > 0.02), $"[{tag}] every piece of the {name} window's kit background shows (shares {string.Join(", ", perColour.Select(count => ((double)count / Math.Max(1, total)).ToString("F3")))})");
                GD.Print($"ARC-UI-3037 {tag} {name} kit-colour share {share:F3}");
                require(lookCapture || (kit ? share > 0.10 : share < 0.01), $"[{tag}] the {name} window's pixels are {(kit ? "mostly" : "not")} the kit's colours (share {share:F3}): its own background art is {(kit ? "not drawn" : "drawn")}");
                // ARC-UI-3165 (method A condition 1): the words and numbers art 22 had baked in are the sheet's own text over the kit's background (the eight stat names, the dial's E and G, the ruler's seven divisions), none with the art's own background.
                if (name == "sheet" && _sheetPanel?.ArtViewForSelfTest is { } labelView)
                {
                    var allLabels = AreaKitStandIns.SheetLabels().Select(label => label.Text).ToArray();
                    require(allLabels.Length == 17 && string.Join(" ", allLabels) == "ST IN CN WP DX PE BE CH E G 75 50 25 0 25 50 75", $"[{tag}] the baked-in text of art 22 the sheet owes is 8 stat names, E, G and 7 ruler divisions (got {string.Join(" ", allLabels)})");
                    // ARC-UI-3299: a group whose kit part is in the set is drawn from the part and its text is not written (all parts with the real fixtures, none with the stand-ins); the ruler's divisions are text unless the real fixtures' ruler part (ARC-UI-3321) carries them.
                    var partIds = AreaKitStandIns.SheetLabelPartPlaces.Select(place => place.Id).ToArray();
                    var expectedParts = kit && fixtures ? partIds.Order().ToArray() : [];
                    require(labelView.KitLabelPartsDrawnForSelfTest.SequenceEqual(expectedParts), $"[{tag}] the sheet laid {(expectedParts.Length > 0 ? "the kit's label parts" : "no kit label part")} over the background (got {string.Join(", ", labelView.KitLabelPartsDrawnForSelfTest)})");
                    // ARC-UI-3321: with the real fixtures the ruler part carries its divisions too, so only the stand-ins' pass writes them as text.
                    var carriedParts = expectedParts.ToHashSet();
                    if (kit && fixtures) carriedParts.Add(AreaKitStandIns.RulerPart);
                    var expectedLabels = AreaKitStandIns.SheetLabels(carriedParts).Select(label => label.Text).ToArray();
                    require(kit ? labelView.KitLabelsDrawnForSelfTest.SequenceEqual(expectedLabels) : labelView.KitLabelsDrawnForSelfTest.Count == 0, $"[{tag}] the sheet {(kit ? "wrote its own stat names, dial letters and ruler divisions over the kit's background" : "wrote none of them over its own art")} (drew {string.Join(" ", labelView.KitLabelsDrawnForSelfTest)})");
                }
                // ARC-UI-3037: the sheet's three gauges stand in (a point of each, in window units): the health heart, the fatigue cross, the alignment dial's face and the ruler's parchment.
                if (kit && name == "sheet" && _sheetPanel?.ArtViewForSelfTest is { } sheetView && !(fixtures && System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_KIT_FIXTURES") is { Length: > 0 }))
                    foreach (var (what, wx, wy, expected) in new (string, float, float, Color)[] { ("health heart", 449, 164, fixtures ? KitFixture : new(.72f, .12f, .1f)), ("fatigue cross", 449, 236, fixtures ? KitFixture : new(.1f, .1f, .8f)), ("alignment dial face", 30, 170, fixtures ? KitFixture : new(.8f, .74f, .58f)), ("magic-technology ruler", 783, 300, fixtures ? KitFixture : new(.8f, .74f, .58f)) })
                    {
                        var at = sheetView.GetGlobalTransform() * new Vector2(wx, wy);
                        var probe = shot.GetPixel(Math.Clamp((int)(at.X * ratio.X), 0, shot.GetWidth() - 1), Math.Clamp((int)(at.Y * ratio.Y), 0, shot.GetHeight() - 1));
                        require(Math.Abs(probe.R - expected.R) < .12f && Math.Abs(probe.G - expected.G) < .12f && Math.Abs(probe.B - expected.B) < .12f, $"[{tag}] the sheet's {what} {(fixtures ? "kit part" : "stand-in")} is drawn at ({wx}, {wy}) (got {probe})");
                    }
            }
            await CaptureHudAsync("steamwoven-" + tag + "-" + name + (kit ? (fixtures ? "-kit" : "-kit-standin") : "-own"), frames);
            close();
            await frames(4);
            require(!isOpen() && AreaGameBarPanel.KitBackgroundWindow is null, $"[{tag}] the {name} window's kit background goes with it");
            require(!AreaLensPlace.Unified, $"[{tag}] the {name} window closed leaves the lenses at the windows' own places");
        }
    }

    private async Task CaptureSteamwovenKitAsync(AreaGameBarPanel panel, Window window, Func<int, Task> frames, Action<bool, string> require)
    {
        var root = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-").FullName;
        var fallbackRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-fallback-").FullName;
        var sheetlessRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-sheetless-").FullName;
        var backdroplessRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-backdropless-").FullName;
        var noMapRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-nomap-").FullName;
        var fixturelessRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-fixtureless-").FullName;
        var noGlyphRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-noglyph-").FullName;
        var savedHd = _gameSettings.HdInterface;
        var done = new HashSet<(string, SteamwovenStep)>();
        void Ensure(string where, string folder, SteamwovenStep step, params string[] without)
        {
            if (done.Add((where, step))) WriteSyntheticKitSet(folder, step, without);
        }
        try
        {
            SteamwovenHud.Options = SteamwovenLayoutOptions.ColumnFitted;
            var first = true;
            foreach (var size in SteamwovenSizes())
            {
                var plan0 = SteamwovenHud.Plan(size.X, size.Y, SteamwovenLayoutOptions.ColumnFitted);
                require(plan0 is not null, $"[kit {size.X}x{size.Y}] the layout is supported");
                var step = plan0!.Layout.Step;
                Ensure("kit", root, step);
                SteamwovenKitStore.UseRoot(root);
                window.ContentScaleSize = size;
                await FitWindowToRenderTargetAsync(panel, window, size, frames, require);
                for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                require((Vector2I)window.GetVisibleRect().Size == size, $"[kit {size.X}x{size.Y}] the viewport is {size}, got {window.GetVisibleRect().Size}");
                panel.Relayout();
                await WaitFramesResampledAsync(frames);
                await frames(3);
                var tag = $"kit-{size.X}x{size.Y}";
                var plan = panel.PlanForSelfTest;
                require(plan is not null && plan.Layout == plan0.Layout, $"[{tag}] the panel is laid out by the column plan");
                var column = plan!.Column;
                // AC1 (SWI-LAY-4): the set of the step the layout chose is the one read; the plan's step is the smallest set at least f.
                require(SteamwovenKitStore.SetFor(step) is { Step: var readStep } && readStep == step, $"[{tag}] the kit's {SteamwovenKitSet.FolderName(step)} set is read");
                var (top, bottom, _, boards, caps) = panel.KitForSelfTest;
                require(top is not null && bottom is not null, $"[{tag}] both strips are drawn from the kit");
                require(top!.Board.Rect == column.TopStrip && bottom!.Board.Rect == column.BottomStrip, $"[{tag}] the strips' art is the plan's rectangles {column.TopStrip} and {column.BottomStrip}");
                require(!panel.GetNode<CanvasItem>("TopStrip/Art").Visible && !panel.GetNode<CanvasItem>("BottomStrip/Art").Visible && boards.Visible, $"[{tag}] the pack's strip art gives way to the kit's");
                // SWI-LAY-5: the filter follows exactness (the strips' 150 and 570 master px are not the 148 and 572 device rows: scaled, linear).
                var exactBoards = boards.AllExact;
                require(boards.TextureFilter == (exactBoards ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear), $"[{tag}] the strip art is sampled {(exactBoards ? "nearest" : "linear")} as its draw is {(exactBoards ? "exact" : "scaled")}");
                await frames(2);
                using (var image = GetViewport().GetTexture().GetImage())
                {
                    Color Pixel(int x, int y) => image.GetPixel(Math.Clamp(x, 0, image.GetWidth() - 1), Math.Clamp(y, 0, image.GetHeight() - 1));
                    var (t, b) = (column.TopStrip, column.BottomStrip);
                    // ARC-UI-2481: the hardware layers lie over the strip art, so the art's colour is read where no layer is (the top strip between its plates, 1000 master px from its left; the bottom strip below the right ring, where the bar draws nothing of its own)
                    // and the hardware's colour where a layer is (the top plate's body, the bottom strip's wires: the bar's own bars and slots draw over the vial and the rest). A look capture puts real masters in place of the flat colours and asks no colour.
                    Color AtMaster(Godot.Rect2 strip, int masterX, int masterY, int stripMasterHeight) => Pixel((int)(strip.Position.X + strip.Size.X * masterX / 2880.0), (int)(strip.Position.Y + strip.Size.Y * masterY / stripMasterHeight));
                    var (tRect, bRect) = (new Godot.Rect2(t.X, t.Y, t.Width, t.Height), new Godot.Rect2(b.X, b.Y, b.Width, b.Height));
                    var lookStrips = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_KIT_LOOK") is { Length: > 0 };
                    require(lookStrips || Near(AtMaster(tRect, 1000, 75, 150), KitTop), $"[{tag}] the top strip's pixels between the plates are the kit art's colour (got {AtMaster(tRect, 1000, 75, 150)})");
                    require(lookStrips || Near(AtMaster(bRect, 2600, 560, 570), KitBottom), $"[{tag}] the bottom strip's pixels below the right ring are the kit art's colour (got {AtMaster(bRect, 2600, 560, 570)})");
                    require(lookStrips || Near(AtMaster(tRect, 850, 75, 150), KitHardware), $"[{tag}] the top strip's plate is the hardware layer's colour (got {AtMaster(tRect, 850, 75, 150)})");
                    require(lookStrips || Near(AtMaster(bRect, 1440, 3, 570), KitHardware), $"[{tag}] the bottom strip's wires are the hardware layer's colour (got {AtMaster(bRect, 1440, 3, 570)})");
                    require(top!.Layers.Count == SteamwovenKitTable.Strips[AreaBarStrip.Top].Layers.Count && bottom!.Layers.Count == SteamwovenKitTable.Strips[AreaBarStrip.Bottom].Layers.Count, $"[{tag}] every hardware layer of the table is composed ({top!.Layers.Count} on the top strip, {bottom!.Layers.Count} on the bottom)");
                    // The caps stand beside the strips on the world; at 4:3 there is no side and they are clipped away (SWI-LAY-3).
                    foreach (var draw in caps.DrawsForSelfTest)
                    {
                        var clipped = draw.Clip.Width == 0;
                        var left = draw.PartId.EndsWith("_left", StringComparison.Ordinal);
                        if (clipped) { require(column.Column.X <= 0 && column.Column.Right >= size.X, $"[{tag}] {draw.PartId} is clipped away only where the composition has no side"); continue; }
                        var probe = Pixel(left ? draw.Clip.Right - 1 : draw.Clip.X, draw.Clip.Y + draw.Clip.Height / 2);
                        require(lookStrips || Near(probe, left ? KitCapLeft : KitCapRight), $"[{tag}] {draw.PartId}'s pixels are the kit cap's colour (got {probe})");
                    }
                }
                // The bar button: the Character button is the kit's plate with its glyph over it (the buttons' look overlays need a look the smoke does not press).
                require(panel.KitButtonForSelfTest(AreaBarButton.Character).Count >= 2, $"[{tag}] the Character button is drawn from the kit's plate and glyph");
                // ARC-UI-3096 (AC2): the glyph's pixels lie over the plate in the screen (the middle of the button is the symbol's colour, a point near its corner the plate's), and a button with no glyph in the set is not the kit's.
                await frames(2);
                var (glyphProbe, plateProbe) = panel.KitButtonProbesForSelfTest(AreaBarButton.Character);
                using (var image = GetViewport().GetTexture().GetImage())
                {
                    Color Pixel(Vector2 point) => image.GetPixel(Math.Clamp((int)point.X, 0, image.GetWidth() - 1), Math.Clamp((int)point.Y, 0, image.GetHeight() - 1));
                    require(Near(Pixel(glyphProbe), KitGlyph), $"[{tag}] the Character button's middle is the kit glyph's colour (got {Pixel(glyphProbe)} at {glyphProbe})");
                    require(Near(Pixel(plateProbe), KitPlate), $"[{tag}] the Character button's corner is the kit plate's colour (got {Pixel(plateProbe)} at {plateProbe})");
                }
                // AC: the covered window's background, with no window over it: the pieces tile the zone and meet at the 1290 px split; the world beside is dimmed by the layer as with a window.
                _smokeKitBackdrop = (SteamwovenBackdropFamily.Inventory, SteamwovenKitWindow.Inventory);
                await frames(3);
                var background = panel.KitForSelfTest.Background;
                var zone = SteamwovenWindowZone.ZoneOf(plan);
                require(background.Count == 2 && background[0].Rect.X == zone.X && background[1].Rect.Right == zone.Right && background[0].Rect.Right == background[1].Rect.X
                    && background.All(piece => piece.Rect.Y == zone.Y && piece.Rect.Bottom == zone.Bottom), $"[{tag}] the inventory's kit background tiles the zone {zone} in two pieces, got {string.Join(" + ", background.Select(piece => piece.Rect))}");
                var shown = panel.BackdropsForSelfTest;
                require(shown[0].Visible && shown[1].Visible && !shown[2].Visible && !shown[3].Visible, $"[{tag}] two backdrop pieces show");
                for (var i = 0; i < 2; i++)
                    require(shown[i].Position == new Vector2(background[i].Rect.X, background[i].Rect.Y) && shown[i].Size == new Vector2(background[i].Rect.Width, background[i].Rect.Height)
                        && shown[i].Pixels == new Vector2I(SteamwovenGrid.BakedLength(i == 0 ? 1290 : 1590, step), SteamwovenGrid.BakedLength(1440, step)), $"[{tag}] backdrop piece {i} is the baked part at the draw's rectangle");
                require(shown.Take(2).All(piece => piece.Filter == (background.All(draw => draw.Exact) ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear)), $"[{tag}] the pieces' sampling follows exactness");
                await CaptureHudAsync("steamwoven-" + tag + "-window", frames);
                using (var image = GetViewport().GetTexture().GetImage())
                {
                    Color Pixel(int x, int y) => image.GetPixel(Math.Clamp(x, 0, image.GetWidth() - 1), Math.Clamp(y, 0, image.GetHeight() - 1));
                    var y0 = zone.Y + zone.Height / 2;
                    require(Near(Pixel(zone.X + 16, y0), KitDoll), $"[{tag}] the zone's left is the paper-doll piece (got {Pixel(zone.X + 16, y0)})");
                    require(Near(Pixel(zone.Right - 16, y0), KitInventory), $"[{tag}] the zone's right is the inventory piece (got {Pixel(zone.Right - 16, y0)})");
                    require(Near(Pixel(background[1].Rect.X - 1, y0), KitDoll) && Near(Pixel(background[1].Rect.X, y0), KitInventory), $"[{tag}] the pieces meet at the split with no gap");
                }
                // SWI-RUN-5: only the open window's background and only the step in use are in memory.
                require(SteamwovenKitStore.ResidentForSelfTest.All(path => path.StartsWith(System.IO.Path.Combine(root, SteamwovenKitSet.FolderName(step)) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)), $"[{tag}] only the {SteamwovenKitSet.FolderName(step)} set is resident");
                require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)) == 2, $"[{tag}] only the two pieces of the open window are resident");
                _smokeKitBackdrop = null;
                await frames(3);
                require(panel.KitForSelfTest.Background.Count == 0 && !SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)), $"[{tag}] the background goes with the window and is released");
                // ARC-UI-3032 (AC1): the loot, steal and See Contents windows' kit background, shown with no window over it: the loot part left, the inventory's part right, tiling the zone; the doll covers are resident with it.
                _smokeKitBackdrop = (SteamwovenBackdropFamily.Inventory, SteamwovenKitWindow.Loot);
                await frames(3);
                var lootBackground = panel.KitForSelfTest.Background;
                require(lootBackground.Count == 2 && lootBackground.Select(piece => piece.PartId).SequenceEqual(["backdrop.loot", "backdrop.inventor"]) && lootBackground[0].Rect.X == zone.X && lootBackground[1].Rect.Right == zone.Right
                    && lootBackground[0].Rect.Right == lootBackground[1].Rect.X, $"[{tag}] the loot window's kit background is the loot part and the inventory's tiling the zone, got {string.Join(" + ", lootBackground.Select(piece => piece.PartId + piece.Rect))}");
                await CaptureHudAsync("steamwoven-" + tag + "-loot-window", frames);
                using (var image = GetViewport().GetTexture().GetImage())
                {
                    Color Pixel(int x, int y) => image.GetPixel(Math.Clamp(x, 0, image.GetWidth() - 1), Math.Clamp(y, 0, image.GetHeight() - 1));
                    var y0 = zone.Y + zone.Height / 2;
                    require(Near(Pixel(zone.X + 16, y0), KitLoot) && Near(Pixel(zone.Right - 16, y0), KitInventory), $"[{tag}] the loot zone's left is the loot piece and its right the inventory piece (got {Pixel(zone.X + 16, y0)} and {Pixel(zone.Right - 16, y0)})");
                }
                require(AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.Loot && AreaGameBarPanel.KitCover(349) is not null && AreaGameBarPanel.KitCover(341) is not null,
                    $"[{tag}] the loot window's kit background and both doll covers are published for the window over it");
                require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)) == 4, $"[{tag}] the loot window holds its two pieces and its two covers");
                await CheckKitLootWindowAsync(tag, panel, root, frames, require);
                _smokeKitBackdrop = null;
                await frames(3);
                require(AreaGameBarPanel.KitBackgroundWindow is null && AreaGameBarPanel.KitCover(349) is null && !SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)),
                    $"[{tag}] the loot window's kit background and covers go with it");
                // The real window path: the inventory on the interface stack names the kit window through the host.
                OpenInventory(); await frames(4);
                require(InventoryOpen && panel.KitForSelfTest.Background.Count == 2, $"[{tag}] the open inventory takes its kit background (got {panel.KitForSelfTest.Background.Count} pieces)");
                EndInventory(); await frames(3);
                // ARC-UI-3033: the real barter window of the map's trading NPC through the host: it takes the kit's background, its frame draws no background art of its own, and the rest of its content (grids, portrait, name,
                // counters, buttons) is drawn over it as before; captured as PREFIX-hud-steamwoven-kit-WxH-barter-window.png.
                for (var wait = 0; wait < 1800 && _barterData is null; wait++) { AdoptBarterData(); await frames(1); }
                if (SteamwovenBarter() is { } barterModel)
                {
                    OpenBarterWindow(barterModel); await frames(6);
                    await WaitFramesResampledAsync(frames);
                    var frame = _barterPanel?.GridAreaForSelfTest;
                    require(BarterOpen && _barterPanel is { KitState.InZone: true } && frame is { KitBackdrop: not null, BarterLook: true } && frame.KitDrawForSelfTest is { Background: false, Pack: false }, $"[{tag}] the open barter window takes the kit background and its frame draws no background art (frame {frame?.KitDrawForSelfTest}, kit {frame?.KitBackdrop is not null})");
                    require(panel.KitForSelfTest.Background.Count == 2 && panel.KitForSelfTest.Background[0].PartId is "backdrop.barter" or "backdrop.barter_follower", $"[{tag}] the barter window's kit background is the merchant piece and the inventory piece");
                    await CaptureHudAsync("steamwoven-" + tag + "-barter-window", frames);
                    CloseBarterByPlayer(); await frames(3);
                    require(!BarterOpen && frame!.KitBackdrop is null, $"[{tag}] closing the barter window drops the kit background from its frame");
                }
                else require(false, $"[{tag}] the map has a trading NPC for the barter window");
                // ARC-UI-3033 (G03): the barter window's kit background, with no window over it: the merchant's side (plain, or the follower's variant) and the hero's inventory tile the zone at the 1290 px split, the
                // kit's doll view and doll cover are resident with them, and the window's frame is told to draw no background of its own while it shows (the frame's own draw is checked by the barter stage).
                foreach (var (kitWindow, leftId, leftColour, name) in new[] { (SteamwovenKitWindow.Barter, "backdrop.barter", KitBarter, "barter"), (SteamwovenKitWindow.BarterFollower, "backdrop.barter_follower", KitBarterFollower, "barter-follower") })
                {
                    _smokeKitBackdrop = (SteamwovenBackdropFamily.Inventory, kitWindow);
                    await frames(3);
                    var pieces = panel.KitForSelfTest.Background;
                    require(pieces.Count == 2 && pieces[0].PartId == leftId && pieces[1].PartId == "backdrop.inventor" && pieces[0].Rect.X == zone.X && pieces[1].Rect.Right == zone.Right && pieces[0].Rect.Right == pieces[1].Rect.X
                        && pieces.All(piece => piece.Rect.Y == zone.Y && piece.Rect.Bottom == zone.Bottom), $"[{tag}] the {name} window's kit background is {leftId} + backdrop.inventor tiling the zone, got {string.Join(" + ", pieces.Select(piece => piece.PartId + piece.Rect))}");
                    require(panel.KitWindowBackdrop is { Doll: not null, HeroDollCover: not null } overlays && overlays.Doll.UnitWidth > 358 && overlays.Doll.UnitWidth < 359 && Math.Abs(overlays.HeroDollCover.UnitHeight - 400) < 1e-6,
                        $"[{tag}] the {name} window names the kit's doll view and doll cover for its frame");
                    var barterBackdrops = panel.BackdropsForSelfTest;
                    require(barterBackdrops[0].Visible && barterBackdrops[1].Visible && !barterBackdrops[2].Visible && !barterBackdrops[3].Visible, $"[{tag}] two backdrop pieces show for the {name} window");
                    require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)) == 4, $"[{tag}] the {name} window holds its two pieces and its two overlays resident");
                    if (kitWindow == SteamwovenKitWindow.Barter) await CaptureHudAsync("steamwoven-" + tag + "-barter", frames);
                    else
                    {
                        using var image = GetViewport().GetTexture().GetImage();
                        Color Pixel(int x, int y) => image.GetPixel(Math.Clamp(x, 0, image.GetWidth() - 1), Math.Clamp(y, 0, image.GetHeight() - 1));
                        var yMid = zone.Y + zone.Height / 2;
                        require(Near(Pixel(zone.X + 16, yMid), leftColour) && Near(Pixel(zone.Right - 16, yMid), KitInventory), $"[{tag}] the {name} window's zone is the merchant piece on the left (got {Pixel(zone.X + 16, yMid)}) and the inventory piece on the right (got {Pixel(zone.Right - 16, yMid)})");
                    }
                }
                _smokeKitBackdrop = null;
                await frames(3);
                require(panel.KitWindowBackdrop is null && panel.KitForSelfTest.Background.Count == 0 && !SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)),
                    $"[{tag}] the barter window's background and overlays are released with it");
                await CheckKitSheetPagesAsync(panel, tag, frames, require);
                // AC2 (fallback): a set without the four sheets keeps the kit's background and every page's own panel art.
                Ensure("sheetless", sheetlessRoot, step, [.. SteamwovenKitTable.Sheets.Values.Select(sheet => sheet.Part)]);
                SteamwovenKitStore.UseRoot(sheetlessRoot);
                panel.Relayout();
                await frames(4);
                await CheckKitSheetPagesAsync(panel, tag, frames, require, kitSheets: false);
                // ARC-UI-3037: the covered windows draw none of their own background art over the kit's, and all of it without.
                SteamwovenKitStore.UseRoot(root);
                panel.Relayout();
                await frames(4);
                await CheckKitWindowBackgroundsAsync(panel, plan, tag, frames, require, kit: true);
                // ARC-UI-3098: a set without the fixtures keeps the kit's background and every window draws its stand-ins for them (no fixture shown).
                Ensure("fixtureless", fixturelessRoot, step, [.. SteamwovenKitTable.FixtureUnits.Keys]);
                SteamwovenKitStore.UseRoot(fixturelessRoot);
                panel.Relayout();
                await frames(4);
                await CheckKitWindowBackgroundsAsync(panel, plan, tag, frames, require, kit: true, fixtures: false);
                SteamwovenKitStore.UseRoot(root);
                panel.Relayout();
                await frames(4);
                Ensure("backdropless", backdroplessRoot, step, "backdrop.pdoll", "backdrop.char_maint", "backdrop.book_main", "backdrop.schematic_base");
                SteamwovenKitStore.UseRoot(backdroplessRoot);
                panel.Relayout();
                await frames(4);
                await CheckKitWindowBackgroundsAsync(panel, plan, tag, frames, require, kit: false);
                SteamwovenKitStore.UseRoot(root);
                panel.Relayout();
                await frames(4);
                await CheckKitWorldMapAsync(tag, plan, panel, frames, require, first ? () => { Ensure("nomap", noMapRoot, step, "backdrop.mapmain"); SteamwovenKitStore.UseRoot(noMapRoot); } : null, () => SteamwovenKitStore.UseRoot(root));
                await CaptureHudAsync("steamwoven-" + tag, frames);
                if (first)
                {
                    first = false;
                    // SWI-RUN-3: a strip with a part missing is the pack's art, whole; the other strip is the kit's.
                    Ensure("fallback", fallbackRoot, step, "cap.top_strip_left");
                    SteamwovenKitStore.UseRoot(fallbackRoot);
                    panel.Relayout();
                    await frames(4);
                    var (fallbackTop, fallbackBottom, _, _, _) = panel.KitForSelfTest;
                    require(fallbackTop is null && fallbackBottom is not null, $"[{tag}] with a top cap missing the top strip is the pack's and the bottom strip the kit's");
                    require(panel.GetNode<CanvasItem>("TopStrip/Art").Visible && !panel.GetNode<CanvasItem>("BottomStrip/Art").Visible, $"[{tag}] the fallback shows the pack's top art and the kit's bottom art");
                    SteamwovenKitStore.UseRoot(null);
                    panel.Relayout();
                    await frames(4);
                    require(panel.KitForSelfTest is { Top: null, Bottom: null }, $"[{tag}] with no kit folder both strips are the pack's art");
                    SteamwovenKitStore.UseRoot(root);
                    panel.Relayout();
                    await frames(4);
                    // ARC-UI-3096 (AC1): a face whose glyph is missing is not drawn from the kit (the pack's art stays), the strips still are.
                    Ensure("noglyph", noGlyphRoot, step, "glyph.char_but");
                    SteamwovenKitStore.UseRoot(noGlyphRoot);
                    panel.Relayout();
                    await frames(4);
                    require(panel.KitButtonForSelfTest(AreaBarButton.Character).Count == 0 && panel.KitForSelfTest is { Top: not null, Bottom: not null }, $"[{tag}] with the glyph missing the Character button is not the kit's plate and the strips still are");
                    SteamwovenKitStore.UseRoot(root);
                    panel.Relayout();
                    await frames(4);
                    require(panel.KitButtonForSelfTest(AreaBarButton.Character).Count >= 2, $"[{tag}] with the glyph back the Character button is drawn from the kit again");
                }
                GD.Print($"ARC-UI-2461 steamwoven {tag} step={step} exact={plan.Layout.ExactStep} runtime={plan.Layout.RuntimeScale.ToDouble():F4} top={column.TopStrip} bottom={column.BottomStrip} zone={zone}");
            }
            // AC2 (SWI-RUN-4): with the HD option off the kit is not read and nothing of it shows.
            _gameSettings = _gameSettings with { HdInterface = false };
            SyncInterfaceHd();
            panel.Relayout();
            await frames(4);
            require(!AreaGameBarPanel.SteamwovenOn && panel.KitForSelfTest is { Top: null, Bottom: null } kitOff && kitOff.Background.Count == 0 && !kitOff.Boards.Visible && !kitOff.Caps.Visible
                && panel.GetNode<CanvasItem>("TopStrip/Art").Visible && panel.GetNode<CanvasItem>("BottomStrip/Art").Visible && panel.KitButtonForSelfTest(AreaBarButton.Character).Count == 0,
                "[kit] with the HD option off nothing of the kit shows and the strips are the original's");
        }
        finally
        {
            _smokeKitBackdrop = null;
            SteamwovenKitStore.ClearOverride();
            _gameSettings = _gameSettings with { HdInterface = savedHd };
            SyncInterfaceHd();
            panel.Relayout();
            await frames(3);
            foreach (var folder in new[] { root, fallbackRoot, sheetlessRoot, noMapRoot, backdroplessRoot, noGlyphRoot, fixturelessRoot })
                try { System.IO.Directory.Delete(folder, true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-UI-2461 synthetic kit not removed: " + folder); }
        }
    }
}
