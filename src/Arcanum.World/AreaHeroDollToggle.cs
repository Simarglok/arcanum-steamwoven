using Arcanum.Simulation;
using Arcanum.Simulation.Equipment;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-1918 (ledger IW26, IW30, IW14, IW9, IW25): the hero's own doll toggle of the windows of interface modes 1 to 4 (barter, loot, steal, See Contents), read in the open
/// 0x5727B0, the button handler 0x573850, the redraw 0x576520 and the hit test 0x575CB0. The open makes two toggle buttons on the pack's side, 338 <c>PD_lil_butt</c> at (713, 37)
/// and 339 <c>Inven_lil_butt</c> at (751, 37) (a radio pair), and clears the toggle 0x68150C; a release on the doll button sets it, a release on the bag button clears it. With it set
/// the redraw draws 341 <c>PD_Cover4Inven</c> at (361, 0) over the pack, no grid items of the hero, the hero's worn items and the covers of its empty slots in the rectangles of the
/// table 0x5CACE8 (the own window's table 0x5CAC58 moved 348 pixels right, at full size), the three weight lines in the rectangles of 0x5CAEC8 and the two rating numbers in those of
/// 0x5CAF18; the hit test finds a slot of that table (1000 + its index) and no grid cell. Godot-free.
/// </summary>
public static class AreaHeroDollToggle
{
    /// <summary>The doll cover drawn over the pack (art 0x155 in the redraw 0x576520) at its place.</summary>
    public const int CoverArt = 341;
    public static readonly (int X, int Y) Cover = (361, 0);

    /// <summary>How far right the doll's rectangles move (table 0x5CACE8 against 0x5CAC58).</summary>
    public const int Offset = 348;

    /// <summary>The toggle buttons' places (the open 0x5727B0): 338 (the doll, 0x682C80) and 339 (the bag, 0x6814EC); their arts are the other party's toggles' (<see cref="AreaLootArtLayout.DollButtonArt"/>, <see cref="AreaLootArtLayout.BagButtonArt"/>).</summary>
    public static readonly (int X, int Y) DollButton = (713, 37), BagButton = (751, 37);

    /// <summary>The arts this toggle needs besides the window's: the cover. They are optional: a window whose arts do not read keeps the grid and shows no toggle.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [CoverArt, AreaLootArtLayout.DollButtonArt, AreaLootArtLayout.BagButtonArt];

    /// <summary>The nine slot rectangles (x, y, width, height) of the hero's doll with the toggle set (0x5CACE8): the own window's table moved <see cref="Offset"/> pixels right.</summary>
    public static IReadOnlyDictionary<EquipmentSlot, (float X, float Y, float Width, float Height)> SlotRects { get; } =
        AreaInventoryDrag.SlotRects.ToDictionary(entry => entry.Key, entry => (entry.Value.X + Offset, entry.Value.Y, entry.Value.Width, entry.Value.Height));

    /// <summary>The weight, encumbrance and speed lines' rectangles with the toggle set (0x5CAEC8), 3 pixels lower than the own window's.</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Lines { get; } = [(431, 32, 185, 15), (431, 47, 185, 15), (431, 62, 185, 15)];

    /// <summary>The damage and armour rating numbers' rectangles with the toggle set (0x5CAF18, 0x5CAF28), the own window's (0x5CAEF8, 0x5CAF08) moved 345 and 348 pixels right.</summary>
    public static readonly (int X, int Y, int Width, int Height) DamageRatingBox = (425, 358, 23, 35), ArmourRatingBox = (609, 358, 23, 35);

    /// <summary>The toggle buttons are made in the windows of modes 1 to 4 (the open); the own window (mode 0) and the item service windows (5, 6) draw the doll without a toggle.</summary>
    public static bool TogglesShown(AreaInventoryMode mode) => mode is AreaInventoryMode.Barter or AreaInventoryMode.Loot or AreaInventoryMode.Steal or AreaInventoryMode.Contents;

    /// <summary>The toggle button under the point (sized by its art): true the doll button, false the bag button, null none.</summary>
    public static bool? ToggleHit(float x, float y, (int Width, int Height) doll, (int Width, int Height) bag)
    {
        if (x >= DollButton.X && x < DollButton.X + doll.Width && y >= DollButton.Y && y < DollButton.Y + doll.Height) return true;
        if (x >= BagButton.X && x < BagButton.X + bag.Width && y >= BagButton.Y && y < BagButton.Y + bag.Height) return false;
        return null;
    }

    /// <summary>
    /// The place under a point of the hero's side with the toggle set (the hit test): a slot of <see cref="SlotRects"/> first, as the own window's (<see cref="AreaInventoryDrag.HitTest"/>);
    /// the drop and use boxes are the same; no grid cell exists (the grid is not shown).
    /// </summary>
    public static AreaInventoryHit HitTest(float x, float y)
    {
        foreach (var (slot, rect) in SlotRects)
            if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height) return new(null, slot);
        var own = AreaInventoryDrag.HitTest(x, y);
        return own.Icon is { } icon ? new(null, null, icon) : AreaInventoryHit.None;
    }
}
