using Arcanum.Simulation.Technology;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-142: the original look of the schematic window (<c>T</c>, the dialog's <c>su</c>, script action 129), read from the retail 1.0.7.4
/// executable: the build 0x56D310, the page draw 0x56DDC0, the component slot draw 0x56E190, the event handler 0x56D530, the font build in 0x56CD60
/// and the tables at 0x5CA820..0x5CA97C (ledger SW9–SW14). The window is 800 × 400 on art 365 at the screen's (0, 41), between the interface bars
/// (0x5CA820); every coordinate here is <b>window-relative</b>. The buttons' table 0x5CA850.. holds screen positions (the creator 0x54AA60 subtracts the
/// window's origin, 0 and 41), the draws of 0x56DDC0 and 0x56E190 are window-relative. Godot-free.
/// </summary>
public static class AreaSchematicWindowLayout
{
    public const int Width = 800, Height = 400, WindowTop = 41;
    public const int BackgroundArt = 365, PreviousArt = 372, NextArt = 373, AssembleArt = 357, ListTabArt = 6, FoundOverlayArt = 831, MissingOverlayArt = 830,
        RoundViewArt = 231;
    public const int NameFontArt = 371, DescriptionFontArt = 229, GlyphFontArt = 370, SlotGlyphFontArt = 300;
    public const int Disciplines = 8;

    /// <summary>The previous and next page buttons (table entries 0x5CA850 and 0x5CA860 at the screen's y 57): hidden on the first and the last page (the
    /// button system's redraw 0x537E20 skips a disabled button).</summary>
    public static (int X, int Y) Previous { get; } = (204, 16);
    public static (int X, int Y) Next { get; } = (687, 16);

    /// <summary>The assemble button (0x5CA870, art 357) and the face art by state (0x5CA830: can assemble 360, an item missing 359, expertise lacking 358),
    /// drawn at (43, 338) by the page draw.</summary>
    public static (int X, int Y) Assemble { get; } = (76, 324);
    public static (int X, int Y) AssembleFaceAt { get; } = (43, 338);
    public static int FaceArt(AssemblyButton button) => button switch { AssemblyButton.CanAssemble => 360, AssemblyButton.ItemMissing => 359, _ => 358 };

    /// <summary>The learned tab (0x5CA880) and the found tab (0x5CA890), both art 6: hover messages 4 and 5 of <c>schematic_text.mes</c>; a press acts only
    /// from the other tab (ledger SW4).</summary>
    public static (int X, int Y) ListTab(bool found) => found ? (29, 248) : (29, 160);

    /// <summary>The eight discipline tabs (0x5CA8A0.. step 16 bytes), one radio group: x 707, y 17 and 45 apart, their arts 355, 356, 361..364, 368, 369.</summary>
    public static (int X, int Y) DisciplineTab(int discipline) => (707, 17 + 45 * Check(discipline));

    private static readonly int[] DisciplineArts = [355, 356, 361, 362, 363, 364, 368, 369];

    public static int DisciplineArt(int discipline) => DisciplineArts[Check(discipline)];

    /// <summary>The record's art (the third number of its line in <c>schematic.mes</c>) at its natural size, the record's name (font 371) and the
    /// description (font 229, wrapped in its rectangle), and the discipline glyph (font 370, the character '0' + the product's discipline).</summary>
    public static (int X, int Y) RecordArtAt { get; } = (240, 146);
    public static (int X, int Y) NameAt { get; } = (261, 30);
    public static (int X, int Y, int Width, int Height) Description { get; } = (219, 71, 466, 75);
    public static (int X, int Y) GlyphAt { get; } = (222, 32);

    /// <summary>The two component slots' rectangles (0x5CA920, 0x5CA930): the found or missing overlay (830, 831) at the top-left, the item's picture
    /// fitted inside; a pointer over one shows the item.</summary>
    public static (int X, int Y, int Width, int Height) Slot(int slot) => slot is 0 or 1 ? (579, slot == 0 ? 181 : 277, 99, 67) : throw new ArgumentOutOfRangeException(nameof(slot));

    /// <summary>The discipline glyph of a slot (font 300, '1' + the discipline, 0x5CA940 + 16 slot) and the required expertise number (<c>%d</c> in font
    /// 229, 0x5CA960 + 16 slot), both drawn only when the component needs expertise above 0.</summary>
    public static (int X, int Y) SlotGlyphAt(int slot) => slot is 0 or 1 ? (583, slot == 0 ? 182 : 278) : throw new ArgumentOutOfRangeException(nameof(slot));

    public static (int X, int Y) SlotNumberAt(int slot) => slot is 0 or 1 ? (658, slot == 0 ? 232 : 328) : throw new ArgumentOutOfRangeException(nameof(slot));

    /// <summary>The round view's rectangle (the view-window mode 1 of 0x550DA0 with the rectangle 0x5CA840, art 231 over the copy): a left release inside
    /// it closes the window (0x56D530, the point test 0x551000, a square).</summary>
    public static (int X, int Y, int Width, int Height) RoundView
    {
        get
        {
            var (x, y) = AreaLensPlace.Corner((50, 26)); // ARC-UI-2714: the common place with the generated interface
            return (x, y, AreaLensPlace.Side, AreaLensPlace.Side);
        }
    }

    public static bool Inside((int X, int Y, int Width, int Height) rect, double x, double y) =>
        x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;

    /// <summary>The arts the window needs besides the record arts and the item pictures.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
    [
        BackgroundArt, PreviousArt, NextArt, AssembleArt, ListTabArt, FoundOverlayArt, MissingOverlayArt, RoundViewArt, 358, 359, 360,
        NameFontArt, DescriptionFontArt, GlyphFontArt, SlotGlyphFontArt, .. DisciplineArts,
    ];

    /// <summary>The fitted rectangle of a picture of <paramref name="width"/> × <paramref name="height"/> inside a slot (0x56E45A..0x56E596): a picture
    /// that fits keeps its size; a larger one shrinks by its larger ratio, truncated; the result is centred in the slot.</summary>
    public static (int X, int Y, int Width, int Height) FitPicture((int X, int Y, int Width, int Height) slot, int width, int height)
    {
        if (width <= 0 || height <= 0) return (slot.X, slot.Y, 0, 0);
        var wide = (double)width / slot.Width;
        var tall = (double)height / slot.Height;
        int w = width, h = height;
        if (wide > tall && wide > 1) { h = (int)(height / wide); w = slot.Width; }
        else if (tall > wide && tall > 1) { w = (int)(width / tall); h = slot.Height; }
        else if (width > slot.Width || height > slot.Height) { w = slot.Width; h = slot.Height; }
        return (slot.X + (slot.Width - w) / 2, slot.Y + (slot.Height - h) / 2, w, h);
    }

    private static int Check(int discipline) => discipline is >= 0 and < Disciplines ? discipline : throw new ArgumentOutOfRangeException(nameof(discipline));
}
