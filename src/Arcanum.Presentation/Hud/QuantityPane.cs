namespace Arcanum.Presentation.Hud;

/// <summary>One button of the quantity pane (ledger QW3): what it does, its top left on the original's 800 × 600 screen and its interface art.</summary>
public sealed record QuantityPaneButton(QuantityPaneAction Action, int X, int Y, int Art);

/// <summary>What a button of the quantity pane does (QW4): All sets the maximum, Up and Down step by one, Accept moves the count shown, Cancel moves nothing.</summary>
public enum QuantityPaneAction { All, Up, Down, Accept, Cancel }

/// <summary>
/// ARC-UI-1884: the original's quantity pane, bar mode 9 of the bottom strip (interface mode 13; ledger QW1–QW6, QW3a): the mode's background
/// art, the five buttons, the kind's icon and the count in six digits of the roller font. Coordinates are the original's 800 × 600 screen.
/// Engine-free; the host draws it and carries the presses out (<see cref="QuantityBar.Apply"/> for the arithmetic).
/// </summary>
public static class QuantityPaneRules
{
    /// <summary>The bar mode the pane is (0x551210 case 9; the mode's text area, rectangle table 0x5C6D60).</summary>
    public const int BarMode = 9;
    /// <summary>The mode's background (art 298 <c>MultiMove_Base</c>, 416 × 112; table 0x5C6548 entry 9, drawn by 0x5503F0 at the place every pane uses).</summary>
    public const int BackgroundArt = 298, BackgroundX = 196, BackgroundY = 492;
    /// <summary>The count's font (art 25 <c>rollerfont</c>, the number font built by 0x56A020; its digit cell is the width of "0"): the drawn glyph colour is black (mask index 0).</summary>
    public const int FontArt = 25;
    /// <summary>The count's left edge and top (0x553960: 0x56A300 with x 0x194, y 0x68 of the strip, six digits), the digits right-aligned in cells of "0"'s width.</summary>
    public const int CountX = 404, CountY = 545;
    /// <summary>The kind's icon (0x551210: the window's place (0x10D, 0x5D), which is (269, 534) on the screen).</summary>
    public const int IconX = 269, IconY = 534;

    /// <summary>The buttons by 0x5C6D14 (table 0x5C6D08, four words each; the screen's coordinates, ledger QW3).</summary>
    public static IReadOnlyList<QuantityPaneButton> Buttons { get; } =
    [
        new(QuantityPaneAction.All, 364, 548, 299),
        new(QuantityPaneAction.Up, 479, 547, 805),
        new(QuantityPaneAction.Down, 479, 559, 804),
        new(QuantityPaneAction.Accept, 545, 506, 33),
        new(QuantityPaneAction.Cancel, 545, 557, 32),
    ];

    /// <summary>The interface arts the pane draws beyond the count's font (<see cref="FontArt"/>, read with the text fonts): the background and the five buttons'.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, .. Buttons.Select(button => button.Art)];

    /// <summary>The ammunition types whose icon the pane builds (0x551210: the arts of base ammunition 0xE9..0xEC, types 0..3); the other types (4, 5) leave the icon unset.</summary>
    public const int IconAmmoTypes = 4;

    /// <summary>
    /// The item art id of the kind's icon (0x551210 → 0x5043C0 with place 2): gold is number 0 of type 3, an ammunition type n (0..3) number n of type 1,
    /// subtype 0, palette 0; null for an ammunition type the pane leaves unset (4, 5) or any other kind.
    /// </summary>
    public static uint? IconArtId(bool gold, int ammoType)
    {
        if (!gold && ammoType is < 0 or >= IconAmmoTypes) return null;
        var number = gold ? 0u : (uint)ammoType;
        var type = gold ? 3u : 1u;
        // 0x5043C0: ((((((number & 0x7FF | 0x3000) << 3 | 0) << 2 | place) << 6 | subtype) << 2 | palette) << 4 | type): class 6, place bits 12..13.
        return (((number & 0x7FF) | 0x3000u) << 17) | (2u << 12) | type;
    }

    /// <summary>The digits drawn in the six cells from the right (0x56A300, no zero flag): cell 0 always draws its digit, a higher cell only while the value left is not 0, so
    /// there are no leading zeros and a count over 999 999 shows its low six digits. Null for a cell that draws nothing.</summary>
    public static IReadOnlyList<char?> Cells(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var cells = new char?[QuantityBar.Digits];
        var value = count;
        for (var cell = 0; cell < cells.Length; cell++)
        {
            if (cell == 0 || value != 0) cells[cell] = (char)('0' + value % 10);
            value /= 10;
        }
        return cells;
    }
}
