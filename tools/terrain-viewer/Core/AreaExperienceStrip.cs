namespace Arcanum.TerrainViewer.Core;

/// <summary>One piece of the experience strip: interface art <paramref name="Art"/> drawn at (<paramref name="X"/>, <paramref name="Y"/>) of the
/// bottom strip, at the art's own height and <paramref name="WidthPercent"/> percent of its width (100 for a whole piece, cut from the
/// art's left edge otherwise).</summary>
public readonly record struct AreaExperiencePiece(int Art, int X, int Y, int WidthPercent);

/// <summary>
/// ARC-UI-1365: the experience progress strip of the original's bottom interface strip, under the hot-key bar (ledger GB13, GB14; the
/// draw 0x556C20 read in the 1.0.7.4 executable). Godot-free. The draw first repaints the strip's art 184 over the rectangle (211, 37)
/// 384 × 14, then asks the hero's progress toward the next level in thousandths (<c>level_progress_towards_next_level</c>, 0x4A6900;
/// <see cref="Arcanum.Simulation.Character.ExperienceRules.Progress"/>), scales it by 11/10 (C integer division) and draws, from an
/// eleven-row table (0x5C71D0, 0x5C7270): one whole piece for each full hundred, in table order, then, for a remainder above 0,
/// <b>always the table's last row</b> (art 772 at (216, 47), not the next piece) cut to remainder percent of its width. A whole piece
/// is the row's art at the art's own size. Both the count and the remainder use C's truncating division, so a negative scaled value
/// draws nothing.
/// </summary>
public static class AreaExperienceStripLayout
{
    /// <summary>The rectangle the draw repaints from the strip's art first (0x556C20: x, y, width, height of the bottom strip, whose
    /// origin is the screen's (0, 441) in the table's coordinates already).</summary>
    public const int ClearX = 211, ClearY = 37, ClearWidth = 384, ClearHeight = 14;

    /// <summary>The table's rows (0x5C71D0 for ten, 0x5C7270 for the eleventh): x, y and interface art, in draw order.</summary>
    public static IReadOnlyList<(int X, int Y, int Art)> Rows { get; } =
    [
        (211, 37, 773), (249, 37, 774), (287, 37, 775), (327, 37, 776), (365, 37, 777),
        (403, 37, 778), (439, 37, 779), (478, 37, 780), (516, 37, 781), (555, 37, 782),
        (216, 47, 772),
    ];

    /// <summary>Every interface art number the strip draws (772 to 782).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. Rows.Select(row => row.Art).Order()];

    /// <summary>The scaled progress of a hero's thousandths: progress × 11 / 10, the product a wrapping 32-bit one and the division
    /// truncating, as the original's (0x556CA0, a LEA product then a multiply-by-reciprocal division).</summary>
    public static int Scaled(int progress) => unchecked(progress * 11) / 10;

    /// <summary>
    /// The pieces to draw for a hero whose progress toward the next level is <paramref name="progress"/> thousandths (null: nothing,
    /// no experience data): the whole pieces of the table's first rows, one for each full hundred of the scaled value (at most the
    /// table's eleven rows: the original reads past its table beyond them, which only a wrapped progress above 1000 could reach, XP3; the viewer stops at the rows), then the last row
    /// cut to the remainder's percent. A scaled value of 0 or below draws nothing.
    /// </summary>
    public static IReadOnlyList<AreaExperiencePiece> Pieces(int? progress)
    {
        if (progress is not { } thousandths) return [];
        var scaled = Scaled(thousandths);
        var pieces = new List<AreaExperiencePiece>();
        var whole = Math.Min(scaled / 100, Rows.Count);
        for (var index = 0; index < whole; index++) pieces.Add(new(Rows[index].Art, Rows[index].X, Rows[index].Y, 100));
        var remainder = scaled % 100;
        if (remainder > 0)
        {
            var last = Rows[^1];
            pieces.Add(new(last.Art, last.X, last.Y, remainder));
        }
        return pieces;
    }

    /// <summary>The width of a piece cut to <paramref name="percent"/> of an art <paramref name="artWidth"/> wide: width × percent / 100
    /// truncated (0x556DD7, an unsigned product).</summary>
    public static int CutWidth(int artWidth, int percent) => (int)((long)artWidth * percent / 100);
}
