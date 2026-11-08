namespace Arcanum.World;

/// <summary>
/// ARC-UI-099 (MIG16 tail, SA44): the original's scroll direction codes and the screen-edge test, read in the retail 1.0.7.4 executable
/// (the main loop's edge routine 0x401F50, the arrow routine 0x402010 and the scroll function 0x40E0A0 they both call).
/// <para>
/// One scroll function takes a direction code 0 to 7: 0 up, 1 up and right, 2 right, 3 down and right, 4 down, 5 down and left, 6 left,
/// 7 up and left. The arrow keys give a code from the held-key bytes (an up arrow with a left arrow is 7, and so on; opposite arrows are
/// not tested). The edge routine reads the mouse position and gives a code only for the exact border pixels of the screen: column 0 is
/// the west edge, column width − 1 the east edge, row 0 the north edge and row height − 1 the south edge; a corner pixel is a diagonal.
/// There is no margin and no dwell time. The original's screen is 800×600 and the executable compares with 799 and 599; the viewer asks
/// for the border pixels of its own window, so the rule is the same at any size.
/// </para>
/// </summary>
public static class OriginalScroll
{
    /// <summary>The code for "no scroll" (the routine then only restores the plain cursor, 0x40E610).</summary>
    public const int None = -1;

    private static readonly (int X, int Y)[] Directions =
    [
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1),
    ];

    /// <summary>The direction code of the border pixel (<paramref name="x"/>, <paramref name="y"/>) of a screen of
    /// <paramref name="width"/> × <paramref name="height"/> pixels, or <see cref="None"/> (0x401F50 to 0x40200F). A position outside the
    /// screen is no border pixel.</summary>
    public static int EdgeCode(int x, int y, int width, int height)
    {
        if (width < 2 || height < 2 || x < 0 || y < 0 || x >= width || y >= height) return None;
        var column = x == 0 ? 6 : x == width - 1 ? 2 : -1;
        var row = y == 0 ? 0 : y == height - 1 ? 4 : -1;
        if (column < 0) return row;
        if (row < 0) return column;
        return (column, row) switch
        {
            (6, 0) => 7,
            (6, 4) => 5,
            (2, 0) => 1,
            _ => 3,
        };
    }

    /// <summary>The unit step on screen of a direction code (x to the right, y downwards), or (0, 0) for <see cref="None"/> or a code
    /// the function does not know (it only handles 0 to 7).</summary>
    public static (int X, int Y) Direction(int code) => code is >= 0 and <= 7 ? Directions[code] : (0, 0);

    /// <summary>The direction code of a held-arrow vector (x to the right, y downwards, each −1, 0 or 1), as the arrow routine builds it.
    /// An axis with opposite arrows held counts as no arrow on that axis (the viewer's reducer cancels them); the original does not
    /// test that case.</summary>
    public static int CodeOf(int x, int y)
    {
        x = Math.Sign(x); y = Math.Sign(y);
        for (var code = 0; code < Directions.Length; code++) if (Directions[code] == (x, y)) return code;
        return None;
    }

    /// <summary>The edge direction of the border pixel as a screen vector, or (0, 0).</summary>
    public static (int X, int Y) EdgeDirection(int x, int y, int width, int height) => Direction(EdgeCode(x, y, width, height));
}
