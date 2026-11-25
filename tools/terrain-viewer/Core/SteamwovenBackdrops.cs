using Arcanum.Presentation.Steamwoven;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2576 (SWI-BDR-4): the backdrop families of the census that an open menu window of the middle band can have. The letters are the census's ids
/// (<c>docs/ui/steamwoven-interface-census.md</c>, "Backdrop families"); the two-strip menus (BD-G, the save, load, pre-made and new character screens) and the full-screen menus (BD-H) hide the
/// bars and are laid out as the fitted plate by ARC-UI-2891 (<see cref="SteamwovenColumnMenu"/>), not here.
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

/// <summary>
/// ARC-UI-2576: which census family an open window belongs to, from the interface mode stack; Godot-free. ARC-UI-3143: the generated wood of the unfitted column's gaps is retired with the generated chrome (the fitted column has no gap: the
/// window zone is the middle band), so the family only picks the window's kit background and the dim of the world beside it.
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
}
