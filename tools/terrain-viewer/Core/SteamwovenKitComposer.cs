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

/// <summary>A document the kit lays over a window's background in place of an original panel art: its part and master size (the zone's master pixels, 3.6 per original unit).</summary>
public sealed record SteamwovenKitSheetSpec(string Part, int MasterWidth, int MasterHeight);

/// <summary>The windows whose kit background the viewer composes (ARC-UI-2461). Every other window keeps its generated backdrop until its own integration issue lands (AC4).</summary>
public enum SteamwovenKitWindow
{
    Inventory,
    Character,
    Logbook,
    WorldMap,
    Schematic,
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
        [SteamwovenKitWindow.Character] = new([("backdrop.char_maint", 2880)]),
        [SteamwovenKitWindow.Logbook] = new([("backdrop.logbooks_side", 594), ("backdrop.book_main", 2286)]),
        [SteamwovenKitWindow.WorldMap] = new([("backdrop.mapmain", 2880)]),
        [SteamwovenKitWindow.Schematic] = new([("backdrop.schematic_base", 2880)]),
    };

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

    /// <summary>The window of an interface mode stack (bottom first) that has a kit background: the mode nearest the top that is one of the covered windows; a dialog or sleep view on top, or another window (the loot window, the barter window) on top, has none (the generated backdrop stays until its integration issue).</summary>
    public static SteamwovenKitWindow? WindowOf(IReadOnlyList<InterfaceMode> stack)
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (WindowOf(stack[i]) is { } window) return window;
            if (SteamwovenBackdrops.FamilyOf(stack[i]) is not null || stack[i] is InterfaceMode.Dialog or InterfaceMode.Sleep) return null;
        }
        return null;
    }

    public static SteamwovenKitWindow? WindowOf(InterfaceMode mode) => mode switch
    {
        // The identify and repair windows (modes 5 and 6, census G17) stand on the inventory's two arts (221 and 223).
        InterfaceMode.Inventory or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6 => SteamwovenKitWindow.Inventory,
        InterfaceMode.Character => SteamwovenKitWindow.Character,
        InterfaceMode.Logbook => SteamwovenKitWindow.Logbook,
        InterfaceMode.WorldMap => SteamwovenKitWindow.WorldMap,
        InterfaceMode.Schematic => SteamwovenKitWindow.Schematic,
        _ => null,
    };

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
    /// The layers of a bar button the bar shows with <paramref name="art"/>: its face (the plate, or the lit face of a light button) and, over it, the overlay of the look when the set has one (<c>&lt;face&gt;_hover</c> for a hovered button,
    /// <c>&lt;face&gt;_pressed</c> for a pressed one; the original has no disabled frame, SWI-KIT-5). Empty when the set has no face: the button keeps its generated or legacy art. The overlay names are the convention of this runtime until the
    /// buttons' batch ([#2482](https://github.com/Simarglok/arcanum-steamwoven/issues/2482)) fixes the sheets.
    /// </summary>
    public static IReadOnlyList<SteamwovenKitPart> ButtonLayers(SteamwovenKitSet set, int art, AreaBarButtonLook look)
    {
        if (!SteamwovenKitTable.ButtonFaces.TryGetValue(art, out var face) || !set.TryGet(face, out var plate)) return [];
        var layers = new List<SteamwovenKitPart> { plate };
        var suffix = look switch { AreaBarButtonLook.Hovered => "_hover", AreaBarButtonLook.Down => "_pressed", _ => null };
        if (suffix is not null && set.TryGet(face + suffix, out var overlay) && overlay.Width == plate.Width && overlay.Height == plate.Height) layers.Add(overlay);
        return layers;
    }
}

/// <summary>The parts of one strip from the kit: the strip art, its two caps and its hardware layers, each a draw in device pixels.</summary>
public sealed record SteamwovenKitStripPlan(SteamwovenKitDraw Board, IReadOnlyList<SteamwovenKitDraw> Caps, IReadOnlyList<SteamwovenKitDraw> Layers);
