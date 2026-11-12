using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-051: the original look of the player's own inventory window (mode 0), read from the redraw 0x576520 and the open 0x572340
/// (ledger IW20–IW25). The window is 800 × 400 at (0, 41) of the 800 × 600 screen, the frame of <see cref="AreaInventoryDrag"/>; its
/// background is the paper doll art 223 at (0, 0) and the pack art 221 at (358, 0) (0x5782D0 gives 223 to modes 0, 5 and 6); an empty wear
/// slot shows its cover art at the slot's top-left (table 0x5CAF58 in the slot order of 0x5CAC58); the drop box (224, with the pointer
/// and a held item over it 225) is at (703, 344) and the use box (342 / 343) at (703, 290), their hit areas the art's size (IW23); three
/// text lines from (145, 29) 15 rows apart and the gold and four ammunition counts down the pack's right column (IW24). Godot-free.
/// </summary>
public static class AreaInventoryArtLayout
{
    public const int Width = 800, Height = 400, ScreenY = 41;
    public const int DollArt = 223, PackArt = 221, PackX = 358;
    public const int DropOffArt = 224, DropOnArt = 225, UseOffArt = 342, UseOnArt = 343;
    public static readonly (int X, int Y) DropBox = (703, 344), UseBox = (703, 290);

    /// <summary>ARC-DEV-339 (B54): the arrange button (art 340 <c>tiny_butt</c>, 28 × 27) every mode of the window makes on open at (763, 197) of the
    /// window (0x572815..0x57287C, ledger IW30, IW37): its release calls the arrange function 0x4670A0, which lays the hero's items out again
    /// (<see cref="Arcanum.Simulation.Items.InventoryArrangeRules"/>); the first press after the window opens lays them out row by row, the next
    /// column by column, and so on (the flag 0x6814F0, cleared at every open). The button plays the press sound 3000 and the release sound 3001
    /// (the creator's fields, as the bar's buttons, GB6).</summary>
    public const int ArrangeArt = 340;
    public static readonly (int X, int Y) ArrangeButton = (763, 197);

    /// <summary>Whether a window point is on the arrange button, sized by its art (the button system's rectangle is the art's frame).</summary>
    public static bool ArrangeHit(float x, float y, (int Width, int Height) art) =>
        art.Width > 0 && art.Height > 0 && x >= ArrangeButton.X && x < ArrangeButton.X + art.Width && y >= ArrangeButton.Y && y < ArrangeButton.Y + art.Height;

    /// <summary>The cover art an empty wear slot shows (0x5CAF58).</summary>
    public static IReadOnlyDictionary<EquipmentSlot, int> Covers { get; } = new Dictionary<EquipmentSlot, int>
    {
        [EquipmentSlot.Helmet] = 241, [EquipmentSlot.Ring1] = 246, [EquipmentSlot.Ring2] = 247, [EquipmentSlot.Medallion] = 245,
        [EquipmentSlot.Weapon] = 249, [EquipmentSlot.Shield] = 248, [EquipmentSlot.Armor] = 244, [EquipmentSlot.Gauntlets] = 243,
        [EquipmentSlot.Boots] = 242,
    };

    /// <summary>ARC-UI-1267 (IW25, IW41): the two numbers mode 0 prints under the doll, the hero's displayed damage rating (0x464630) and armour rating (0x464700), in the rectangles
    /// (x, y, width, height) the redraw reads at 0x5CAEF8 and 0x5CAF08 (0x576E2C..0x576EFD, font 0x681390): the damage rating ends at the right edge of its rectangle (x + width −
    /// text width), the armour rating starts at the left edge of its.</summary>
    public static readonly (int X, int Y, int Width, int Height) DamageRatingBox = (80, 358, 23, 35), ArmourRatingBox = (261, 358, 23, 35);

    /// <summary>The weight, encumbrance and speed lines' rectangles (0x5CAE98).</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Lines { get; } = [(145, 29, 185, 15), (145, 44, 185, 15), (145, 59, 185, 15)];

    /// <summary>The gold count's and the four ammunition counts' rectangles (x 0x5CAFC0, y 0x5CAFD4, 58 × 20).</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Counts { get; } =
        [(729, 70, 58, 20), (729, 93, 58, 20), (729, 116, 58, 20), (729, 139, 58, 20), (729, 162, 58, 20)];

    public static IReadOnlyList<int> ArtNumbers { get; } = [DollArt, PackArt, DropOffArt, DropOnArt, UseOffArt, UseOnArt, .. Covers.Values];

    /// <summary>The top-left of a box's art in the window.</summary>
    public static (int X, int Y) Box(AreaInventoryIcon icon) => icon == AreaInventoryIcon.Drop ? DropBox : UseBox;

    /// <summary>The art a box shows: its "on" art while an item is held over it (the move handler 0x573D74, mode 0), else its "off" art.</summary>
    public static int BoxArt(AreaInventoryIcon icon, bool heldOver) => icon == AreaInventoryIcon.Drop
        ? heldOver ? DropOnArt : DropOffArt
        : heldOver ? UseOnArt : UseOffArt;

    /// <summary>The place of an item's art in its rectangle: centred, as the redraw centres a grid item's art in its cells
    /// ((cells × 32 − art) / 2) and a worn item's in its slot. A grid item's cells come from its art's size (0x461780, ARC-UI-055), so its
    /// art fits them. PROJECT: an art larger than the rectangle is scaled down to fit (a grid item sized without its art, when the game's
    /// art did not read for the session, or an art larger than its doll slot).</summary>
    public static (float X, float Y, float Width, float Height) Place((float X, float Y, float Width, float Height) rect, int artWidth, int artHeight)
    {
        if (artWidth <= 0 || artHeight <= 0) throw new ArgumentOutOfRangeException(nameof(artWidth));
        var scale = Math.Min(1f, Math.Min(rect.Width / artWidth, rect.Height / artHeight));
        var (w, h) = (artWidth * scale, artHeight * scale);
        return (rect.X + (int)((rect.Width - w) / 2), rect.Y + (int)((rect.Height - h) / 2), w, h);
    }
}
