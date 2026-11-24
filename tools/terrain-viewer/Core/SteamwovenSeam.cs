namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2576 (SWI-VER-5): the seam ratio of a join on a captured frame, as defined for the 9-slice side pieces in
/// <c>docs/rendering/hd-interface-art.md</c> (U4): the difference between adjacent columns (or rows) in a ±<see cref="Band"/> px band at the join (the cleanest twentieth of the edge's rows or columns, so parts of the window at its edge do not count as wood), over the same at an interior band of
/// the same width away from it. A join as smooth as the interior is 1.0; R ≤ <see cref="Limit"/> passes. The difference of two neighbouring pixels is the mean of the absolute differences of their
/// red, green and blue values (0 to 255). Godot-free: the smoke reads the viewport back and hands the RGBA bytes.
/// </summary>
public static class SteamwovenSeam
{
    /// <summary>Half the band of a measure, in pixels: 8 px each side of the join.</summary>
    public const int Band = 8;

    /// <summary>The quantile of the edge's lines that stands for the wood (the cleanest twentieth).</summary>
    public const double CleanestShare = 0.05;

    /// <summary>The gate of SWI-VER-5.</summary>
    public const double Limit = 1.5;

    /// <summary>The interior never counts as smoother than this (mean level units), so a flat interior does not make an ordinary join a failure.</summary>
    public const double InteriorFloor = 1.0;

    /// <summary>The ratio at the vertical edge between column <paramref name="joinX"/> − 1 and column <paramref name="joinX"/> over the rows <paramref name="top"/> to <paramref name="bottom"/> (exclusive),
    /// against the interior band around column <paramref name="referenceX"/>; null when a band lies outside the frame or the rows are empty.</summary>
    public static double? ColumnRatio(ReadOnlySpan<byte> rgba, int width, int height, int joinX, int top, int bottom, int referenceX)
    {
        if (rgba.Length != width * height * 4 || top < 0 || bottom > height || bottom <= top) return null;
        if (joinX - Band < 1 || joinX + Band > width || referenceX - Band < 1 || referenceX + Band > width) return null;
        var join = Mean(rgba, width, joinX, top, bottom, columns: true);
        var reference = Mean(rgba, width, referenceX, top, bottom, columns: true);
        return join / Math.Max(reference, InteriorFloor);
    }

    /// <summary>The ratio at the horizontal edge between row <paramref name="joinY"/> − 1 and row <paramref name="joinY"/> over the columns <paramref name="left"/> to <paramref name="right"/> (exclusive).</summary>
    public static double? RowRatio(ReadOnlySpan<byte> rgba, int width, int height, int joinY, int left, int right, int referenceY)
    {
        if (rgba.Length != width * height * 4 || left < 0 || right > width || right <= left) return null;
        if (joinY - Band < 1 || joinY + Band > height || referenceY - Band < 1 || referenceY + Band > height) return null;
        var join = Mean(rgba, width, joinY, left, right, columns: false);
        var reference = Mean(rgba, width, referenceY, left, right, columns: false);
        return join / Math.Max(reference, InteriorFloor);
    }

    // The difference of neighbouring pixels over the 2·Band pairs around `centre` at the `CleanestShare` quantile of the lines (rows for a vertical join, columns for a horizontal one) from `from` to `to`, each line's mean:
    // the lines where the window's own parts (a scale, a button) lie at the edge only raise the difference, so the cleanest lines stand for the wood, and a real seam (a step in tone) shows on every line.
    // The join itself is the pair (centre − 1, centre).
    private static double Mean(ReadOnlySpan<byte> rgba, int width, int centre, int from, int to, bool columns)
    {
        var lines = new double[to - from];
        for (var other = from; other < to; other++)
        {
            long sum = 0;
            for (var p = centre - Band; p < centre + Band; p++)
            {
                var a = columns ? (other * width + p - 1) * 4 : ((p - 1) * width + other) * 4;
                var b = columns ? (other * width + p) * 4 : (p * width + other) * 4;
                sum += Math.Abs(rgba[a] - rgba[b]) + Math.Abs(rgba[a + 1] - rgba[b + 1]) + Math.Abs(rgba[a + 2] - rgba[b + 2]);
            }
            lines[other - from] = (double)sum / (6 * Band);
        }
        Array.Sort(lines);
        return lines[Math.Min(lines.Length - 1, (int)(lines.Length * CleanestShare))];
    }
}
