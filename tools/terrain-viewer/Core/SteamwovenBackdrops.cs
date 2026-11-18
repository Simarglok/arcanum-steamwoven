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
/// whose wood the piece continues outwards (negative to the left of and above that art), so the grain at the edge of the window zone is the window's own.</summary>
public readonly record struct SteamwovenBackdropPiece(PixelRect Rect, ChromeMaterial Material, double OriginX, double OriginY);

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
        if (family == SteamwovenBackdropFamily.Inventory) return plan.Layout.Split.SplitX;
        return plan.Layout.WindowZone.X + (int)Math.Round(units * SteamwovenWindowZone.UnitScale(plan), MidpointRounding.AwayFromZero);
    }

    /// <summary>The pieces of the family's backdrop over the middle band of the plan: one for a family of one piece, the left and the right part of the split for a two-part family. Together they cover the band exactly (the
    /// frame's width, the band's height) and share the split as one edge.</summary>
    public static IReadOnlyList<SteamwovenBackdropPiece> Pieces(SteamwovenBackdropFamily family, SteamwovenHudPlan plan)
    {
        var layout = plan.Layout;
        var band = layout.Middle.Band;
        var zone = layout.WindowZone;
        var unit = SteamwovenWindowZone.UnitScale(plan);
        // The grain of an art that starts at (artX, artY) device pixels: where the piece's top left lies from it, in units.
        SteamwovenBackdropPiece Piece(int left, int right, double artX) =>
            new(PixelRect.FromEdges(left, band.Y, right, band.Bottom), ChromeMaterial.Wood, (left - artX) / unit, (band.Y - (double)zone.Y) / unit);
        if (SplitX(family, plan) is not { } split)
            return [Piece(band.X, band.Right, zone.X)];
        return [Piece(band.X, split, zone.X), Piece(split, band.Right, split)];
    }
}
