using Arcanum.Presentation.Steamwoven;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2576 (SWI-BDR-4): the backdrop families of the census that an open menu window of the middle band can have. The letters are the census's ids
/// (<c>docs/ui/steamwoven-interface-census.md</c>, "Backdrop families"); the two-strip menus (BD-G, the save, load, pre-made and new character screens) and the full-screen menus (BD-H) hide the
/// bars and are laid out over the whole frame by ARC-UI-2577 (SWI-BDR-6), not here.
/// </summary>
public enum SteamwovenBackdropFamily
{
    /// <summary>BD-A: the inventory, loot, steal, barter and identify and repair windows (and the opening shop): two parts, 358 + 442 units.</summary>
    Inventory,
    /// <summary>BD-B: the character sheet (and the creation editor's): one backdrop.</summary>
    CharacterSheet,
    /// <summary>BD-C: the logbook: two parts, the side panel 165 + the book 635 units.</summary>
    Logbook,
    /// <summary>BD-D: the reading window: two parts, the side bar 132 + the table 668 units.</summary>
    Reading,
    /// <summary>BD-E: the world map frame: one backdrop.</summary>
    WorldMap,
    /// <summary>BD-F: the schematic window: one backdrop.</summary>
    Schematic,
}

/// <summary>One piece of a family's backdrop: the rectangle it fills in the window (device pixels), the generated material, and where the material's grain starts: the origin, in original units, of the window art
/// whose wood the piece continues outwards (negative to the left of and above that art), so the grain at the edge of the window zone is the window's own; <c>Part</c> is 0 for the left part of a two-part family (or the only one) and 1 for the right (the tone art of <see cref="SteamwovenBackdrops.ToneArt"/>: in the column mode the pieces above and below the window share a part).</summary>
public readonly record struct SteamwovenBackdropPiece(PixelRect Rect, ChromeMaterial Material, double OriginX, double OriginY, int Part = 0);

/// <summary>
/// ARC-UI-2576 (SWI-BDR-1, -4, -5, -7): the generated backdrop of each census family over the middle band, from the layout model; Godot-free. Every piece is made after its window's own background
/// (SWI-BDR-7): the census finds one dark streaked wood under all of BD-A to BD-E (the families differ in what is laid on it), so the pieces are <see cref="ChromeMaterial.Wood"/> sampled at the window's unit
/// scale `f · 3.6` from the origin of the art they continue. A two-part window (SWI-BDR-5) has two pieces that meet at the split exactly: the left one runs from the frame's left edge to it, the right one from it to the
/// frame's right edge; each half is a window art of its own, so its grain starts at its own left edge (at the split for the right half) and the halves are not one grain.
/// </summary>
public static class SteamwovenBackdrops
{
    /// <summary>The family of a window mode, or null for a mode without a backdrop of its own (the dialog and sleep views keep the world in view, the pop-ups and target modes are no window).</summary>
    public static SteamwovenBackdropFamily? FamilyOf(InterfaceMode mode) => mode switch
    {
        InterfaceMode.Inventory or InterfaceMode.Loot or InterfaceMode.Steal or InterfaceMode.Barter
            or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6 => SteamwovenBackdropFamily.Inventory,
        InterfaceMode.Character => SteamwovenBackdropFamily.CharacterSheet,
        InterfaceMode.Logbook => SteamwovenBackdropFamily.Logbook,
        InterfaceMode.Book => SteamwovenBackdropFamily.Reading,
        InterfaceMode.WorldMap => SteamwovenBackdropFamily.WorldMap,
        InterfaceMode.Schematic => SteamwovenBackdropFamily.Schematic,
        _ => null,
    };

    /// <summary>The family of the window nearest the top of an interface mode stack (bottom first, as <c>InterfaceModeStack.Modes</c>): an overlay or target mode over a window (the quantity picker over the inventory) keeps the window's
    /// backdrop, a dialog under a barter window gives way to the barter's, but a dialog or sleep view on top of a window has none; null when no mode on it has one.</summary>
    public static SteamwovenBackdropFamily? FamilyOf(IReadOnlyList<InterfaceMode> stack)
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (FamilyOf(stack[i]) is { } family) return family;
            // The dialog and sleep views keep the world in view: a window below them gives no backdrop.
            if (stack[i] is InterfaceMode.Dialog or InterfaceMode.Sleep) return null;
        }
        return null;
    }

    /// <summary>ARC-UI-2643: the original art whose wood a piece of the family continues, so the backdrop takes the tone of its own window's wood (SWI-BDR-7): the inventory (221) for both halves of BD-A (ARC-UI-2763: the windows of the family are one wood, <see cref="InterfaceChromeRenderer.FamilyWoodLow"/>, so no tone seam lies between the halves), the character sheet (22), the logbook's side (264) and book (260), the reading window's side bar (494, for both halves: its backgrounds are sheets on the table), the map frame (138) and the schematic base (365).</summary>
    public static int ToneArt(SteamwovenBackdropFamily family, int piece) => (family, piece) switch
    {
        (SteamwovenBackdropFamily.Inventory, _) => 221,
        (SteamwovenBackdropFamily.CharacterSheet, _) => 22,
        (SteamwovenBackdropFamily.Logbook, 0) => 264,
        (SteamwovenBackdropFamily.Logbook, _) => 260,
        (SteamwovenBackdropFamily.Reading, _) => 494,
        (SteamwovenBackdropFamily.WorldMap, _) => 138,
        _ => 365,
    };

    /// <summary>Every art number <see cref="ToneArt"/> names (the bars' content reads them with its own).</summary>
    public static IReadOnlyList<int> ToneArts { get; } = [221, 22, 264, 260, 494, 138, 365];

    /// <summary>The width in original units of the left part of a two-part family (the census: 358, 165, 132), or null for a family of one piece.</summary>
    public static int? LeftUnits(SteamwovenBackdropFamily family) => family switch
    {
        SteamwovenBackdropFamily.Inventory => 358,
        SteamwovenBackdropFamily.Logbook => 165,
        SteamwovenBackdropFamily.Reading => 132,
        _ => null,
    };

    /// <summary>The x of the split of a two-part family: the layout model's own (<see cref="SteamwovenLayout.Split"/>, SWI-BDR-5) for the inventory family, the same rule for the logbook and the reading window
    /// (the zone's left edge and the left part's units at the unit scale, halves up); null for a family of one piece.</summary>
    public static int? SplitX(SteamwovenBackdropFamily family, SteamwovenHudPlan plan)
    {
        if (LeftUnits(family) is not { } units) return null;
        // ARC-UI-2890: in the column the inventory family's split is the zone's left edge and the left part's units at the unit scale, like the others (the full-width split is the layout's, snapped to the grid of the centre block).
        if (family == SteamwovenBackdropFamily.Inventory && plan.Column is null) return plan.Layout.Split.SplitX;
        return SteamwovenWindowZone.ZoneOf(plan).X + (int)Math.Round(units * SteamwovenWindowZone.UnitScale(plan), MidpointRounding.AwayFromZero);
    }

    /// <summary>The pieces of the family's backdrop over the middle band of the plan: one for a family of one piece, the left and the right part of the split for a two-part family. Together they cover the band exactly (the
    /// frame's width, the band's height) and share the split as one edge.</summary>
    public static IReadOnlyList<SteamwovenBackdropPiece> Pieces(SteamwovenBackdropFamily family, SteamwovenHudPlan plan)
    {
        if (plan.Column is { } column) return ColumnPieces(family, plan, column);
        var layout = plan.Layout;
        var band = layout.Middle.Band;
        var zone = layout.WindowZone;
        var unit = SteamwovenWindowZone.UnitScale(plan);
        // The grain of an art that starts at (artX, artY) device pixels: where the piece's top left lies from it, in units.
        SteamwovenBackdropPiece Piece(int left, int right, double artX) =>
            new(PixelRect.FromEdges(left, band.Y, right, band.Bottom), ChromeMaterial.Wood, (left - artX) / unit, (band.Y - (double)zone.Y) / unit, left == band.X ? 0 : 1);
        if (SplitX(family, plan) is not { } split)
            return [Piece(band.X, band.Right, zone.X)];
        // ARC-UI-2763: the inventory family is one wood and its pack art continues the doll's grain from the split, so the right piece's grain is the left one's continued; the logbook and the reading window keep a grain of their own per half.
        return [Piece(band.X, split, zone.X), Piece(split, band.Right, family == SteamwovenBackdropFamily.Inventory ? zone.X : split)];
    }

    /// <summary>
    /// ARC-UI-2890 (AC3): the pieces of the family's wood in the column: the window's gaps, the column's rectangles above and below the window zone (empty ones left out), each in one piece or, for a two-part family, split
    /// at the split of the zone. The grain starts at the window art's own top left (the zone's), so the wood at the zone's top and bottom edges is the window's wood continued; a gap is as wide as the column and the window.
    /// </summary>
    private static IReadOnlyList<SteamwovenBackdropPiece> ColumnPieces(SteamwovenBackdropFamily family, SteamwovenHudPlan plan, SteamwovenColumnPlan column)
    {
        var zone = column.Window.Zone;
        var unit = SteamwovenWindowZone.UnitScale(plan);
        var split = SplitX(family, plan);
        var pieces = new List<SteamwovenBackdropPiece>();
        SteamwovenBackdropPiece Piece(PixelRect rect, int left, int right, double artX) =>
            new(PixelRect.FromEdges(left, rect.Y, right, rect.Bottom), ChromeMaterial.Wood, (left - artX) / unit, (rect.Y - (double)zone.Y) / unit, split is { } at && left >= at ? 1 : 0);
        foreach (var gap in new[] { column.Window.GapAbove, column.Window.GapBelow })
        {
            if (gap.Height <= 0 || gap.Width <= 0) continue;
            if (split is not { } at) { pieces.Add(Piece(gap, gap.X, gap.Right, zone.X)); continue; }
            at = Math.Clamp(at, gap.X, gap.Right);
            // As in the full-width layout: the inventory family is one wood, its right piece continues the left one's grain; the logbook and the reading window start the right half's grain at the split.
            if (at > gap.X) pieces.Add(Piece(gap, gap.X, at, zone.X));
            if (at < gap.Right) pieces.Add(Piece(gap, at, gap.Right, family == SteamwovenBackdropFamily.Inventory ? zone.X : at));
        }
        return pieces;
    }
}
