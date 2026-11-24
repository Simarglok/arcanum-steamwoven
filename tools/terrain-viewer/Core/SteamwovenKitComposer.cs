using Arcanum.Presentation.Steamwoven;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One kit part to draw: the part, the device rectangle it fills, the part of that rectangle that lies inside the window (a cap beyond the window's edge is clipped), and whether the draw is exact (the baked pixels
/// are the device pixels: sampled nearest, SWI-LAY-5) or scaled (sampled linear).</summary>
public readonly record struct SteamwovenKitDraw(string PartId, PixelRect Rect, PixelRect Clip, bool Exact);

/// <summary>What a composer returns: the plan, or null with the ids of the parts that were missing or the wrong size, so the caller falls back to the generated chrome (SWI-RUN-3).</summary>
public sealed record SteamwovenKitResult<T>(T? Value, IReadOnlyList<string> Missing) where T : class
{
    public bool Complete => Value is not null;
}

/// <summary>A hardware layer of a strip: the part and where its top left lies, in master pixels from the strip's top left.</summary>
public readonly record struct SteamwovenKitLayer(string Part, int MasterX, int MasterY);

/// <summary>The parts one strip is composed of: the strip art, its two end caps (SWI-LAY-12) and the hardware layers over it. A strip is drawn from the kit only when every one of them is in the set.</summary>
public sealed record SteamwovenKitStripSpec(string Board, string CapLeft, string CapRight, IReadOnlyList<SteamwovenKitLayer> Layers);

/// <summary>The kit window backgrounds a window mode is made of, left to right: part id and master width; the master height is the zone's, 1440 px. The widths add up to the composition's 2880 px (SWI-BDR-3, SWI-BDR-5).</summary>
public sealed record SteamwovenKitWindowSpec(IReadOnlyList<(string Part, int MasterWidth)> Pieces);

/// <summary>A doll cover of the loot window from the kit: its part and master size (the zone's master pixels).</summary>
public sealed record SteamwovenKitCover(string Part, int MasterWidth, int MasterHeight);

/// <summary>A document the kit lays over a window's background in place of an original panel art: its part and master size (the zone's master pixels, 3.6 per original unit).</summary>
public sealed record SteamwovenKitSheetSpec(string Part, int MasterWidth, int MasterHeight);

/// <summary>The windows whose kit background the viewer composes (ARC-UI-2461). Every other window keeps its generated backdrop until its own integration issue lands (AC4).</summary>
public enum SteamwovenKitWindow
{
    Inventory,
    Loot,
    Character,
    Logbook,
    WorldMap,
    Schematic,
    /// <summary>The barter window (census G03, mode 1) with the merchant's plain background (art 235): <c>backdrop.barter</c> 1290 + <c>backdrop.inventor</c> 1590 (ARC-UI-3033).</summary>
    Barter,
    /// <summary>The barter window while the merchant is a follower with the cycle buttons (art 825): <c>backdrop.barter_follower</c> 1290 + <c>backdrop.inventor</c> 1590 (ARC-UI-3033).</summary>
    BarterFollower,
}

/// <summary>A part a window draws over its kit background where its content lies (the doll views, the doll covers), sized in master pixels: the window draws it at its unit position in the unit scale of its frame (ARC-UI-3033). The part is
/// used only when the set holds it at the table's size; else the window keeps its own art for that overlay.</summary>
public sealed record SteamwovenKitOverlay(string Part, int MasterWidth, int MasterHeight)
{
    /// <summary>The overlay's size in original UI units: a unit is 3.6 master pixels (<see cref="SteamwovenGrid"/>).</summary>
    public double UnitWidth => MasterWidth / 3.6;

    public double UnitHeight => MasterHeight / 3.6;
}

/// <summary>
/// ARC-UI-2461: what the kit is made of at run time, keyed by the original's art numbers and the interface modes (the baked manifest names parts, the viewer knows arts and windows; the census rows are the source,
/// <c>tools/steamwoven_ui/kit.json</c> holds the same ids and art numbers). The hardware layers of the strips are placed by the hardware batch ([#2481](https://github.com/Simarglok/arcanum-steamwoven/issues/2481)),
/// which fills <see cref="Strips"/>; until then a strip has its art and caps only.
/// </summary>
public static class SteamwovenKitTable
{
    public const int CompositionMasterWidth = 2880, ZoneMasterHeight = 1440, FullScreenMasterHeight = 2160;

    /// <summary>The strip art and its end caps (kit.json: <c>board.inttop</c> art 185, <c>board.intbotom</c> art 184).</summary>
    public static IReadOnlyDictionary<AreaBarStrip, SteamwovenKitStripSpec> Strips { get; } = new Dictionary<AreaBarStrip, SteamwovenKitStripSpec>
    {
        [AreaBarStrip.Top] = new("board.inttop", "cap.top_strip_left", "cap.top_strip_right", []),
        [AreaBarStrip.Bottom] = new("board.intbotom", "cap.bottom_strip_left", "cap.bottom_strip_right", []),
    };

    /// <summary>The covered windows (census G01 and G17, G05, G11, G12, G15): inventory 1290 + 1590 (the paper doll 223 and the inventory 221), the character sheet, the logbook 594 + 2286 (264 and 260), the map frame and the schematic base.</summary>
    public static IReadOnlyDictionary<SteamwovenKitWindow, SteamwovenKitWindowSpec> Windows { get; } = new Dictionary<SteamwovenKitWindow, SteamwovenKitWindowSpec>
    {
        [SteamwovenKitWindow.Inventory] = new([("backdrop.pdoll", 1290), ("backdrop.inventor", 1590)]),
        // ARC-UI-3032 (census G02, G04): the loot window with the body's doll, the steal window and See Contents share the left half 234 (the loot art) and the inventory's right half 221.
        [SteamwovenKitWindow.Loot] = new([("backdrop.loot", 1290), ("backdrop.inventor", 1590)]),
        [SteamwovenKitWindow.Character] = new([("backdrop.char_maint", 2880)]),
        [SteamwovenKitWindow.Logbook] = new([("backdrop.logbooks_side", 594), ("backdrop.book_main", 2286)]),
        [SteamwovenKitWindow.WorldMap] = new([("backdrop.mapmain", 2880)]),
        [SteamwovenKitWindow.Schematic] = new([("backdrop.schematic_base", 2880)]),
        // ARC-UI-3033 (G03): the barter window, the merchant's side 1290 (art 235, or 825 with the follower's cycle buttons) and the hero's inventory 1590 (art 221).
        [SteamwovenKitWindow.Barter] = new([("backdrop.barter", 1290), ("backdrop.inventor", 1590)]),
        [SteamwovenKitWindow.BarterFollower] = new([("backdrop.barter_follower", 1290), ("backdrop.inventor", 1590)]),
    };

    /// <summary>ARC-UI-3033: the merchant's doll view (art 337, 1290 × 876 master px: it replaces the merchant's grid from y 157 units to the zone's foot) and the hero's doll cover (art 341, 1230 × 1440, over the pack at x 361 units).</summary>
    public static SteamwovenKitOverlay BarterDoll { get; } = new("backdrop.barter_pd", 1290, 876);

    public static SteamwovenKitOverlay HeroDollCover { get; } = new("backdrop.pd_cover4inven", 1230, 1440);

    /// <summary>The overlays a kit window's content draws over its background; they stay resident with it (SWI-RUN-5).</summary>
    public static IReadOnlyList<SteamwovenKitOverlay> OverlaysOf(SteamwovenKitWindow window) =>
        window is SteamwovenKitWindow.Barter or SteamwovenKitWindow.BarterFollower ? BarterOverlays : [];

    private static readonly SteamwovenKitOverlay[] BarterOverlays = [BarterDoll, HeroDollCover];

    /// <summary>The doll covers the loot window lays over its background (census G02, G04; kit.json <c>backdrop.loot_pd</c> art 349 and <c>backdrop.pd_cover4inven</c> art 341): the art number it replaces, the part, the part's master size
    /// (the window draws it at the place of the art it replaces, <see cref="AreaLootArtLayout.DollView"/> and <see cref="AreaHeroDollToggle.Cover"/>). A cover the set lacks
    /// or holds at another size leaves the original art in its place; the background is not held back by it.</summary>
    public static IReadOnlyDictionary<int, SteamwovenKitCover> Covers { get; } = new Dictionary<int, SteamwovenKitCover>
    {
        [AreaLootArtLayout.DollViewArt] = new("backdrop.loot_pd", 1290, 960),
        [AreaHeroDollToggle.CoverArt] = new("backdrop.pd_cover4inven", 1230, 1440),
    };

    /// <summary>
    /// ARC-UI-3098 (owner decision 2026-11-24, option A): the required details the old window arts painted into themselves are kit parts of their own (<c>fixture.*</c> and the two grids, kit.json, drawn by
    /// <c>tools/steamwoven_ui/fixtures.py</c>): part id to its size in original units. The master size is the unit size on the 6 px grid (<see cref="MasterOf"/>); the window draws the part at its place in its own units (the places are
    /// the old art's, TRD "Elements the old arts held"). A test pins the ids and sizes to kit.json.
    /// </summary>
    public static IReadOnlyDictionary<string, (int Width, int Height)> FixtureUnits { get; } = new Dictionary<string, (int, int)>
    {
        ["grid.inventory_cells"] = (32, 32), ["fixture.pack_pit"] = (324, 388), ["fixture.doll_pit"] = (330, 300), ["fixture.holder_pit_loot"] = (324, 260), ["fixture.holder_pit_barter"] = (324, 228),
        ["fixture.name_pit"] = (223, 55), ["fixture.quote_field"] = (267, 36), ["fixture.count_pit"] = (61, 17), ["fixture.count_icons"] = (38, 115), ["fixture.weapon_frame"] = (112, 97),
        ["fixture.shield_frame"] = (114, 97), ["fixture.arrange_legend"] = (78, 32), ["fixture.sheet_name_box"] = (360, 87), ["fixture.sheet_derived_box"] = (452, 113), ["fixture.stat_oval"] = (70, 31),
        ["fixture.align_dial"] = (100, 100), ["fixture.health_disc"] = (76, 56), ["fixture.fatigue_disc"] = (76, 56), ["fixture.mt_ruler"] = (46, 330), ["fixture.schem_page"] = (506, 367),
        ["grid.schematic"] = (506, 367), ["fixture.schem_slot"] = (103, 71), ["fixture.schem_channel"] = (126, 30), ["fixture.gear_hat"] = (90, 70), ["fixture.gear_scroll"] = (96, 72),
    };

    /// <summary>The master pixels of a length in original units: units x 3.6 on the 6 px grid, halves up (the census' rule, <c>kit_check.snap</c>).</summary>
    public static int MasterOf(int units) => 6 * ((6 * units + 5) / 10);

    private static readonly string[] InventoryFixtures = ["grid.inventory_cells", "fixture.pack_pit", "fixture.doll_pit", "fixture.name_pit", "fixture.count_pit", "fixture.count_icons", "fixture.weapon_frame", "fixture.shield_frame", "fixture.arrange_legend"],
        LootFixtures = ["grid.inventory_cells", "fixture.pack_pit", "fixture.holder_pit_loot", "fixture.count_pit", "fixture.count_icons"],
        BarterFixtures = ["grid.inventory_cells", "fixture.pack_pit", "fixture.holder_pit_barter", "fixture.quote_field", "fixture.count_pit", "fixture.count_icons"],
        CharacterFixtures = ["fixture.sheet_name_box", "fixture.sheet_derived_box", "fixture.stat_oval", "fixture.align_dial", "fixture.health_disc", "fixture.fatigue_disc", "fixture.mt_ruler"],
        SchematicFixtures = ["fixture.schem_page", "grid.schematic", "fixture.schem_slot", "fixture.schem_channel", "fixture.gear_hat", "fixture.gear_scroll"];

    /// <summary>The fixtures a covered window draws over its kit background (they stay resident with it, SWI-RUN-5); the logbook and the world map have none here (the map's are <a href="https://github.com/Simarglok/arcanum-steamwoven/issues/3112">#3112</a>).</summary>
    public static IReadOnlyList<string> FixturesOf(SteamwovenKitWindow window) => window switch
    {
        SteamwovenKitWindow.Inventory => InventoryFixtures,
        SteamwovenKitWindow.Loot => LootFixtures,
        SteamwovenKitWindow.Barter or SteamwovenKitWindow.BarterFollower => BarterFixtures,
        SteamwovenKitWindow.Character => CharacterFixtures,
        SteamwovenKitWindow.Schematic => SchematicFixtures,
        _ => [],
    };

    /// <summary>The master pixels of one original unit in the window zone (2880 over the 800-unit frame, SWI-BDR-2).</summary>
    public const double MasterPerUnit = CompositionMasterWidth / 800.0;

    /// <summary>The pages of the character sheet (census G06 to G09, also the NPC sheet G10 and the editor's pages): the art number of the page's own panel (29 skills, 30 tech, 31 spells, 567 auto-level schemes, each 235 x 325 units at the
    /// sheet's (503, 63)) and the kit sheet that replaces it over the kit's <c>backdrop.char_maint</c> (846 x 1170 master px). A sheet the set lacks or holds at another size leaves the page's own art in its place.</summary>
    public static IReadOnlyDictionary<int, SteamwovenKitSheetSpec> Sheets { get; } = new Dictionary<int, SteamwovenKitSheetSpec>
    {
        [29] = new("sheet.skills_window", 846, 1170),
        [30] = new("sheet.disciplines_window", 846, 1170),
        [31] = new("sheet.char_spells", 846, 1170),
        [567] = new("sheet.scheme_rot", 846, 1170),
    };

    /// <summary>The full-screen plates (census BD-H, M01 to M04): the art number of the menu's background and the plate's part id.</summary>
    public static IReadOnlyDictionary<int, string> Plates { get; } = new Dictionary<int, string>
    {
        [329] = "backdrop.mainmenuback",
        [331] = "backdrop.singleplayerback",
        [556] = "backdrop.optionsmenuback",
        [330] = "backdrop.multiplayerback",
    };

    /// <summary>The faces of the bar buttons by the art the bar shows: the plates (idle faces) and the lit faces of the light buttons (census H01, H02).</summary>
    public static IReadOnlyDictionary<int, string> ButtonFaces { get; } = new Dictionary<int, string>
    {
        [472] = "plate.skills_button", [473] = "plate.spells_button", [470] = "plate.combat_button", [471] = "plate.schematics_button",
        [187] = "plate.log_but", [169] = "plate.char_but", [186] = "plate.invn_but", [193] = "plate.tmap_but", [194] = "plate.wmap_but", [137] = "plate.lilgrnbut",
        [561] = "state.char_on", [560] = "state.log_on", [558] = "state.tmap_on", [195] = "state.wmap_on", [559] = "state.invn_on",
    };

    /// <summary>
    /// ARC-UI-3096 (SWI-KIT-5): the glyph of a bar button by the art it shows, one part per button, drawn over the face at the face's size (a symbol on a transparent background); the lit face of a light button takes the glyph of
    /// its idle face. The names are this runtime's convention until the buttons' batch ([#2482](https://github.com/Simarglok/arcanum-steamwoven/issues/2482)) makes the rows: <c>glyph.&lt;plate name&gt;</c>. Art 137 is shared by two
    /// buttons with two symbols, so it has none here: <see cref="SharedArtGlyphs"/> names theirs by button.
    /// </summary>
    public static IReadOnlyDictionary<int, string> ButtonGlyphs { get; } = new Dictionary<int, string>
    {
        [472] = "glyph.skills_button", [473] = "glyph.spells_button", [470] = "glyph.combat_button", [471] = "glyph.schematics_button",
        [187] = "glyph.log_but", [169] = "glyph.char_but", [186] = "glyph.invn_but", [193] = "glyph.tmap_but", [194] = "glyph.wmap_but",
        [561] = "glyph.char_but", [560] = "glyph.log_but", [558] = "glyph.tmap_but", [195] = "glyph.wmap_but", [559] = "glyph.invn_but",
    };

    /// <summary>ARC-UI-3096: the glyphs of the two buttons that stand on art 137 (the Fate Points and Sleep balls), by button.</summary>
    public static IReadOnlyDictionary<AreaBarButton, string> SharedArtGlyphs { get; } = new Dictionary<AreaBarButton, string>
    {
        [AreaBarButton.FatePoints] = "glyph.fate_but", [AreaBarButton.Sleep] = "glyph.sleep_but",
    };

    /// <summary>The glyph part of the bar button <paramref name="button"/> showing <paramref name="art"/>, or null when the art has none.</summary>
    public static string? GlyphOf(AreaBarButton? button, int art) =>
        ButtonGlyphs.TryGetValue(art, out var glyph) ? glyph : button is { } b && SharedArtGlyphs.TryGetValue(b, out glyph) ? glyph : null;
}

/// <summary>
/// ARC-UI-2461 (SWI-RUN-2, SWI-RUN-3): the Godot-free composer of the kit. From the layout plan and a baked set it says which parts are drawn where, in device pixels, with their edges rounded one by one from the master
/// position (SWI-LAY-7), and for each draw whether it is exact. A composition that needs a part the set lacks (or holds at a size the table does not allow) returns no plan and names the part, so the viewer draws the
/// generated chrome for it instead; one missing part never leaves a half-drawn strip or window.
/// </summary>
public static class SteamwovenKitComposer
{
    /// <summary>A master length or position as device pixels at the plan's <c>f</c>: the nearest whole pixel of <c>master · f</c>, halves up.</summary>
    public static int Device(SteamwovenHudPlan plan, int master) => (new Ratio(master) * plan.Layout.Scale).RoundHalfUp();

    /// <summary>The master length of a part of the set: its baked length over the step, exact.</summary>
    public static int Master(SteamwovenKitSet set, int bakedLength)
    {
        var step = SteamwovenGrid.StepScale(set.Step);
        return checked((int)(bakedLength * step.Denominator / step.Numerator));
    }

    private static SteamwovenKitDraw Draw(SteamwovenKitPart part, PixelRect rect, PixelRect clip) =>
        new(part.Id, rect, Intersect(rect, clip), part.Width == rect.Width && part.Height == rect.Height);

    private static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        var (left, top) = (Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        var (right, bottom) = (Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));
        return right <= left || bottom <= top ? new PixelRect(left, top, 0, 0) : PixelRect.FromEdges(left, top, right, bottom);
    }

    /// <summary>The strip's art and caps and layers from the kit, or the missing parts. The art must be the composition's 2880 px wide at the set's step.</summary>
    public static SteamwovenKitResult<SteamwovenKitStripPlan> Strip(SteamwovenHudPlan plan, SteamwovenKitSet set, AreaBarStrip strip, IReadOnlyDictionary<AreaBarStrip, SteamwovenKitStripSpec>? table = null)
    {
        var column = plan.Column;
        var spec = (table ?? SteamwovenKitTable.Strips)[strip];
        var missing = new List<string>();
        var board = Need(set, spec.Board, missing);
        if (board is { } b && b.Width != SteamwovenGrid.BakedLength(SteamwovenKitTable.CompositionMasterWidth, set.Step)) { missing.Add(spec.Board + " (not 2880 px wide at the step)"); board = null; }
        var capLeft = Need(set, spec.CapLeft, missing);
        var capRight = Need(set, spec.CapRight, missing);
        var layers = spec.Layers.Select(layer => (layer, part: Need(set, layer.Part, missing))).ToList();
        if (missing.Count > 0 || board is not { } boardPart || capLeft is not { } left || capRight is not { } right) return new(null, missing);
        var rect = strip == AreaBarStrip.Top ? column.TopStrip : column.BottomStrip;
        var frame = new PixelRect(0, 0, plan.Layout.WindowWidth, plan.Layout.WindowHeight);
        var caps = new List<SteamwovenKitDraw>(2);
        // A cap is as high as its strip and as wide as its own proportion says; the left one ends at the strip's left edge, the right one starts at its right edge.
        var leftWidth = Math.Max(1, (int)Math.Round((double)left.Width * rect.Height / left.Height, MidpointRounding.AwayFromZero));
        var rightWidth = Math.Max(1, (int)Math.Round((double)right.Width * rect.Height / right.Height, MidpointRounding.AwayFromZero));
        caps.Add(Draw(left, new PixelRect(rect.X - leftWidth, rect.Y, leftWidth, rect.Height), frame));
        caps.Add(Draw(right, new PixelRect(rect.Right, rect.Y, rightWidth, rect.Height), frame));
        var placed = new List<SteamwovenKitDraw>(layers.Count);
        foreach (var (layer, part) in layers)
        {
            var p = part!.Value;
            var (masterW, masterH) = (Master(set, p.Width), Master(set, p.Height));
            var edges = PixelRect.FromEdges(rect.X + Device(plan, layer.MasterX), rect.Y + Device(plan, layer.MasterY), rect.X + Device(plan, layer.MasterX + masterW), rect.Y + Device(plan, layer.MasterY + masterH));
            placed.Add(Draw(p, edges, rect));
        }
        return new(new SteamwovenKitStripPlan(Draw(boardPart, rect, rect), caps, placed), []);
    }

    private static SteamwovenKitPart? Need(SteamwovenKitSet set, string id, List<string> missing)
    {
        if (set.TryGet(id, out var part)) return part;
        missing.Add(id);
        return null;
    }

    /// <summary>
    /// The kit background of a window in the window zone: its pieces left to right, each the zone's height, meeting at edges rounded from their master position (the two-part window's split at 1290 px, SWI-BDR-5) and together
    /// exactly the zone (SWI-BDR-2, no gap). A piece the set lacks or holds at a size other than the table's makes the whole background missing.
    /// </summary>
    public static SteamwovenKitResult<IReadOnlyList<SteamwovenKitDraw>> Background(SteamwovenHudPlan plan, SteamwovenKitSet set, SteamwovenKitWindow window, IReadOnlyDictionary<SteamwovenKitWindow, SteamwovenKitWindowSpec>? table = null)
    {
        var spec = (table ?? SteamwovenKitTable.Windows)[window];
        var zone = SteamwovenWindowZone.ZoneOf(plan);
        var missing = new List<string>();
        var parts = new List<SteamwovenKitPart>();
        foreach (var (id, width) in spec.Pieces)
        {
            if (!set.TryGet(id, out var part)) { missing.Add(id); continue; }
            if (part.Width != SteamwovenGrid.BakedLength(width, set.Step) || part.Height != SteamwovenGrid.BakedLength(SteamwovenKitTable.ZoneMasterHeight, set.Step)) { missing.Add(id + " (wrong size)"); continue; }
            parts.Add(part);
        }
        if (missing.Count > 0) return new(null, missing);
        var draws = new List<SteamwovenKitDraw>(parts.Count);
        var (cursor, left) = (0, zone.X);
        for (var i = 0; i < parts.Count; i++)
        {
            cursor += spec.Pieces[i].MasterWidth;
            var right = i == parts.Count - 1 ? zone.Right : zone.X + Device(plan, cursor);
            var rect = PixelRect.FromEdges(left, zone.Y, right, zone.Bottom);
            draws.Add(Draw(parts[i], rect, rect));
            left = right;
        }
        return new(draws, []);
    }

    /// <summary>The window of an interface mode stack (bottom first) that has a kit background: the mode nearest the top that is one of the covered windows; a dialog or sleep view on top, or another window (the loot window) on top, has none (the generated backdrop stays until its integration issue; the barter window has its own, ARC-UI-3033).</summary>
    public static SteamwovenKitWindow? WindowOf(IReadOnlyList<InterfaceMode> stack) => WindowOf(stack, null);

    /// <summary>ARC-UI-3033: as <see cref="WindowOf(IReadOnlyList{InterfaceMode})"/>, told how the barter window stands: the merchant's background art (825 picks the follower's variant) and whether its frame lies in the window zone (the frame
    /// docked in the opening shop's screen, or the diagnostic look, is not in it, so it keeps its generated look and has no kit background). Without <paramref name="barter"/> the barter window is the plain one.</summary>
    public static SteamwovenKitWindow? WindowOf(IReadOnlyList<InterfaceMode> stack, SteamwovenKitBarterState? barter)
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (WindowOf(stack[i]) is { } window)
            {
                if (window == SteamwovenKitWindow.Barter && barter is { } state)
                    return !state.InZone ? null : AreaBarterArtLayout.CycleShown(state.Background) ? SteamwovenKitWindow.BarterFollower : SteamwovenKitWindow.Barter;
                return window;
            }
            if (SteamwovenBackdrops.FamilyOf(stack[i]) is not null || stack[i] is InterfaceMode.Dialog or InterfaceMode.Sleep) return null;
        }
        return null;
    }

    public static SteamwovenKitWindow? WindowOf(InterfaceMode mode) => mode switch
    {
        // ARC-UI-3032: the loot window (with the body's doll), the steal window and See Contents (census G02, G04).
        InterfaceMode.Loot or InterfaceMode.Steal => SteamwovenKitWindow.Loot,
        // The identify and repair windows (modes 5 and 6, census G17) stand on the inventory's two arts (221 and 223).
        InterfaceMode.Inventory or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6 => SteamwovenKitWindow.Inventory,
        InterfaceMode.Character => SteamwovenKitWindow.Character,
        InterfaceMode.Logbook => SteamwovenKitWindow.Logbook,
        InterfaceMode.WorldMap => SteamwovenKitWindow.WorldMap,
        InterfaceMode.Schematic => SteamwovenKitWindow.Schematic,
        InterfaceMode.Barter => SteamwovenKitWindow.Barter,
        _ => null,
    };

    /// <summary>ARC-UI-3033: the part of an overlay when the set holds it at the overlay's size at the step, else null (the window keeps its own art for it).</summary>
    public static SteamwovenKitPart? Overlay(SteamwovenKitSet set, SteamwovenKitOverlay overlay) =>
        set.TryGet(overlay.Part, out var part) && part.Width == SteamwovenGrid.BakedLength(overlay.MasterWidth, set.Step) && part.Height == SteamwovenGrid.BakedLength(overlay.MasterHeight, set.Step) ? part : null;

    /// <summary>
    /// ARC-UI-3032: the kit part that replaces the original doll cover <paramref name="art"/> (349, 341) of the loot window: the part of the set when it is the table's size at the step, else null (the original art stays; the
    /// background still shows from the kit). The part's drawn size in original units is <see cref="CoverUnits"/>.
    /// </summary>
    public static SteamwovenKitPart? Cover(SteamwovenKitSet set, int art) =>
        SteamwovenKitTable.Covers.TryGetValue(art, out var cover) && set.TryGet(cover.Part, out var part)
        && part.Width == SteamwovenGrid.BakedLength(cover.MasterWidth, set.Step) && part.Height == SteamwovenGrid.BakedLength(cover.MasterHeight, set.Step) ? part : null;

    /// <summary>ARC-UI-3098: the kit part of a fixture when the set holds it at the table's size at the step, else null (the window draws its plain stand-in for that detail, <c>AreaKitStandIns</c>).</summary>
    public static SteamwovenKitPart? Fixture(SteamwovenKitSet set, string id) =>
        SteamwovenKitTable.FixtureUnits.TryGetValue(id, out var units) && set.TryGet(id, out var part)
        && part.Width == SteamwovenGrid.BakedLength(SteamwovenKitTable.MasterOf(units.Width), set.Step) && part.Height == SteamwovenGrid.BakedLength(SteamwovenKitTable.MasterOf(units.Height), set.Step) ? part : null;

    /// <summary>The size of a cover in original units: its master size over 3.6 master px per unit.</summary>
    public static (double Width, double Height) CoverUnits(SteamwovenKitCover cover) => (cover.MasterWidth / SteamwovenKitTable.MasterPerUnit, cover.MasterHeight / SteamwovenKitTable.MasterPerUnit);

    /// <summary>
    /// ARC-UI-3032 (AC2): the background arts the loot window draws itself in the loot, steal and See Contents modes: the loot art 234 and the pack art 221 over the whole frame, or none while the kit background shows under
    /// the window (the window is then drawn over it; the view-window art over the round view and every item layer stay as before).
    /// </summary>
    public static IReadOnlyList<int> LootBackgroundArts(bool kitBackground) => kitBackground ? [] : [AreaLootArtLayout.LootArt, AreaInventoryArtLayout.PackArt];

    /// <summary>
    /// ARC-UI-3035 (AC2): the kit sheet that replaces the character sheet page's own panel art <paramref name="art"/> (29, 30, 31, 567): the part of the set when it is the table's size at the step, else null (the page keeps its art).
    /// It is asked for only while the kit background of the character window shows under the sheet (<see cref="WindowOf(InterfaceMode)"/>, the same for the hero's sheet and the NPC sheet of a dialog).
    /// </summary>
    public static SteamwovenKitPart? Sheet(SteamwovenKitSet set, int art) =>
        SteamwovenKitTable.Sheets.TryGetValue(art, out var sheet) && set.TryGet(sheet.Part, out var part)
        && part.Width == SteamwovenGrid.BakedLength(sheet.MasterWidth, set.Step) && part.Height == SteamwovenGrid.BakedLength(sheet.MasterHeight, set.Step) ? part : null;

    /// <summary>The kit plate of a full-screen menu's background art: the part of the set when it is the composition's size (2880 × 2160 at the step), else null (the pack's or the original's plate stays).</summary>
    public static SteamwovenKitPart? Plate(SteamwovenKitSet set, int art) =>
        SteamwovenKitTable.Plates.TryGetValue(art, out var id) && set.TryGet(id, out var part)
        && part.Width == SteamwovenGrid.BakedLength(SteamwovenKitTable.CompositionMasterWidth, set.Step) && part.Height == SteamwovenGrid.BakedLength(SteamwovenKitTable.FullScreenMasterHeight, set.Step) ? part : null;

    /// <summary>
    /// The layers of a bar button the bar shows with <paramref name="art"/>, bottom first: its face (the plate, or the lit face of a light button), its glyph (the symbol on a transparent background at the face's size, ARC-UI-3096)
    /// and, over them, the overlay of the look when the set has one (<c>&lt;face&gt;_hover</c> for a hovered button, <c>&lt;face&gt;_pressed</c> for a pressed one; the original has no disabled frame, SWI-KIT-5). All or nothing: empty
    /// when the set has no face, or no glyph at the face's size, because a plate alone is a button without its symbol; the button then keeps its generated or legacy art. The ring round a button is the strip's hardware layer
    /// (SWI-LAY-12), not a layer of the button. The glyph and overlay names are the convention of this runtime until the buttons' batch ([#2482](https://github.com/Simarglok/arcanum-steamwoven/issues/2482)) fixes the sheets.
    /// <paramref name="button"/> tells the two buttons that share art 137 apart.
    /// </summary>
    public static IReadOnlyList<SteamwovenKitPart> ButtonLayers(SteamwovenKitSet set, int art, AreaBarButtonLook look, AreaBarButton? button = null)
    {
        if (!SteamwovenKitTable.ButtonFaces.TryGetValue(art, out var face) || !set.TryGet(face, out var plate)) return [];
        if (SteamwovenKitTable.GlyphOf(button, art) is not { } glyphId || !set.TryGet(glyphId, out var glyph) || glyph.Width != plate.Width || glyph.Height != plate.Height) return [];
        var layers = new List<SteamwovenKitPart> { plate, glyph };
        var suffix = look switch { AreaBarButtonLook.Hovered => "_hover", AreaBarButtonLook.Down => "_pressed", _ => null };
        if (suffix is not null && set.TryGet(face + suffix, out var overlay) && overlay.Width == plate.Width && overlay.Height == plate.Height) layers.Add(overlay);
        return layers;
    }
}

/// <summary>How the barter window stands for the kit: the merchant's background art (235, or 825 with the follower's cycle buttons) and whether its frame lies in the window zone (the original look, not docked in the opening shop's screen; ARC-UI-3033).</summary>
public readonly record struct SteamwovenKitBarterState(int Background, bool InZone);

/// <summary>The parts of one strip from the kit: the strip art, its two caps and its hardware layers, each a draw in device pixels.</summary>
public sealed record SteamwovenKitStripPlan(SteamwovenKitDraw Board, IReadOnlyList<SteamwovenKitDraw> Caps, IReadOnlyList<SteamwovenKitDraw> Layers);
