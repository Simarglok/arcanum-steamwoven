namespace Arcanum.Presentation.Hud;

/// <summary>
/// ARC-UI-2368: how the bottom strip's number slots are written (the digit writer 0x54AD00, ledger BC1, BC5, BC9; slots 0 health, 1 fatigue, 3 counter 3,
/// 4 counter 4, 5 poison of the table 0x5C63F8). The slot's rectangle is filled with opaque black (slots 0..4, the height plus one row; slot 5 gets no
/// fill), then the text is written in the bar font (interface art 171) in white with a shadow of (10, 10, 10) one pixel right and down (the font's
/// flag 8, which also adds one to the measured width and height). Coordinates are the original's 800 × 600 screen. Engine-free.
/// </summary>
public static class BarNumberRules
{
    /// <summary>The bar font's interface art (global 0x64C670, built at the end of 0x54A330).</summary>
    public const int FontArt = 171;

    /// <summary>The slot's fill (the tables' entry 0 in every pixel format, 0x52CB20) and the font's colours (the default font 0x5351D0 and the shadow global 0x630D50).</summary>
    public static (byte R, byte G, byte B) FillColour { get; } = (0, 0, 0);
    public static (byte R, byte G, byte B) TextColour { get; } = (255, 255, 255);
    public static (byte R, byte G, byte B) ShadowColour { get; } = (10, 10, 10);

    /// <summary>The shadow's offset in x and y; the measured text is that much wider and taller.</summary>
    public const int ShadowOffset = 1;

    public const int Health = 0, Fatigue = 1, Counter3 = 3, Counter4 = 4, Poison = 5;

    /// <summary>The slot rectangles (x, y, width, height) of the table 0x5C63F8 that hold a number the viewer draws.</summary>
    public static (int X, int Y, int Width, int Height) Slot(int slot) => slot switch
    {
        Health => (15, 578, 29, 12),
        Fatigue => (755, 578, 27, 12),
        Counter3 => BarCounterRules.Counter3Rect,
        Counter4 => BarCounterRules.Counter4Rect,
        Poison => (15, 500, 29, 12),
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>Whether the slot is filled before its text is written (every slot but the poison, 0x54AD00).</summary>
    public static bool Filled(int slot) => slot != Poison;

    /// <summary>The fill's rectangle: the slot with one more row (the writer raises the height by one for the fill call only).</summary>
    public static (int X, int Y, int Width, int Height) FillRect((int X, int Y, int Width, int Height) slot) => (slot.X, slot.Y, slot.Width, slot.Height + 1);

    /// <summary>
    /// Where the text starts, from the slot's top left, for a text of the measured size (<paramref name="textWidth"/> and <paramref name="textHeight"/>
    /// already include the shadow's one pixel): a narrower text starts at the free width halved toward zero; a text as wide as the slot or wider starts at
    /// the slot's left and is not drawn when the slot is lower than it (the original then measures it again at the slot's width, the wrap of which is not
    /// reproduced); the top is the free height halved toward zero. Null: not drawn.
    /// </summary>
    public static (int X, int Y)? Place(int slotWidth, int slotHeight, int textWidth, int textHeight)
    {
        if (textWidth < slotWidth) return ((slotWidth - textWidth) / 2, (slotHeight - textHeight) / 2);
        return slotHeight < textHeight ? null : (0, (slotHeight - textHeight) / 2);
    }
}
