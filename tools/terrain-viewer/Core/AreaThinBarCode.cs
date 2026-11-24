namespace Arcanum.TerrainViewer.Core;

/// <summary>One row of a code-drawn thin bar: <paramref name="Y"/> rows below the bar's top (a Legacy unit tall) and its RGB colour as 0xRRGGBB;
/// <paramref name="Rgb"/> is null for a row the Legacy art leaves transparent (the colour key), which draws nothing.</summary>
public readonly record struct AreaThinBarRow(int Y, int? Rgb);

/// <summary>
/// ARC-UI-3060 (owner decision 2026-11-20, #2950; kit rule SWI-KIT-6: a gauge's fill comes from the value): the four 3-unit-tall bars the
/// installed HD pack has no redraw for are drawn by code under the HD option instead of as Legacy pixels next to HD neighbours: the experience
/// strip's remainder piece (art 772 <c>XP_GaugeMag</c>, 372 × 3, cropped to the remainder percent) and the follower frames' fatigue (504),
/// health (505) and poisoned health (616) bars (34 × 3, cropped to the follower's percent). Godot-free. The bars keep the Legacy rectangles and
/// fill lengths (<see cref="AreaExperienceStripLayout.CutWidth"/>, <see cref="Arcanum.Presentation.Hud.LifeBarRules.PortraitFill"/>); what changes is
/// that each row is one flat fill in the colour the Legacy frame holds there, and that the viewer snaps each edge to a whole device pixel
/// (<see cref="SnapSpan"/>) at the composition's scale. No art is generated, nothing of the original's pixels is stored beyond these colour values.
/// </summary>
/// <remarks>
/// Colours sampled from the installed Legacy frames (the dominant colour of each row, 2026-11-20): 772 is orange, its three rows
/// ff6a06 / ffc87a / db4e06 (a rounded cap of a few pixels at either end of the Legacy art is not kept); 505 is red, ff7070 / ff0000 / b50000;
/// 616 is green-yellow, e6ff70 / d3ff00 / 96b500; 504 is blue, 7070ff / (pure blue 0000ff, the colour key, so the Legacy art leaves the middle row
/// transparent and the frame's black channel shows through) / 0000b5. The row structure is kept as the Legacy art draws it.
/// </remarks>
public static class AreaThinBarCode
{
    /// <summary>The art number of the experience strip's remainder piece.</summary>
    public const int ExperienceArt = 772;

    /// <summary>The bars' height in Legacy units (all four arts are three rows tall).</summary>
    public const int Height = 3;

    private static readonly Dictionary<int, AreaThinBarRow[]> Bars = new()
    {
        [ExperienceArt] = [new(0, 0xFF6A06), new(1, 0xFFC87A), new(2, 0xDB4E06)],
        [AreaFollowerFrameLayout.FatigueArt] = [new(0, 0x7070FF), new(1, null), new(2, 0x0000B5)],
        [AreaFollowerFrameLayout.HealthArt] = [new(0, 0xFF7070), new(1, 0xFF0000), new(2, 0xB50000)],
        [AreaFollowerFrameLayout.PoisonedHealthArt] = [new(0, 0xE6FF70), new(1, 0xD3FF00), new(2, 0x96B500)],
    };

    /// <summary>Every art number drawn by code under the HD option.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. Bars.Keys.Order()];

    /// <summary>Whether art <paramref name="art"/> is one of the four drawn by code under the HD option.</summary>
    public static bool Covers(int art) => Bars.ContainsKey(art);

    /// <summary>The rows of art <paramref name="art"/> (empty when it is not one of the four).</summary>
    public static IReadOnlyList<AreaThinBarRow> Rows(int art) => Bars.TryGetValue(art, out var rows) ? rows : [];

    /// <summary>
    /// Snaps the span [<paramref name="start"/>, <paramref name="start"/> + <paramref name="length"/>) of a control (in its own units) to whole device pixels:
    /// the device position of each edge (<paramref name="origin"/> + edge × <paramref name="scale"/>) is rounded half up and mapped back to the control's units.
    /// At a whole scale and origin the span is unchanged; at a fractional scale each edge lands on a pixel boundary, so a bar's rows are whole pixels
    /// (never blurred and never a row short of its neighbours by more than one pixel). A scale that is not finite and positive leaves the span as it is.
    /// </summary>
    public static (float Start, float End) SnapSpan(float start, float length, float origin, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0 || !float.IsFinite(origin)) return (start, start + length);
        var first = MathF.Floor(origin + start * scale + 0.5f);
        var last = MathF.Floor(origin + (start + length) * scale + 0.5f);
        return ((first - origin) / scale, (last - origin) / scale);
    }

    /// <summary>
    /// The rectangles (in the control's units, edges snapped by <see cref="SnapSpan"/>) the bar of art <paramref name="art"/> draws for a fill of
    /// <paramref name="filled"/> Legacy units from <paramref name="x"/>, <paramref name="y"/>, each with its colour; transparent rows and rows that snap to nothing are left out.
    /// </summary>
    public static IReadOnlyList<(float X, float Y, float Width, float Height, int Rgb)> Rects(int art, float x, float y, int filled, float originX, float originY, float scaleX, float scaleY)
    {
        if (filled <= 0) return [];
        var (left, right) = SnapSpan(x, filled, originX, scaleX);
        if (right <= left) return [];
        var rects = new List<(float, float, float, float, int)>();
        foreach (var row in Rows(art))
        {
            if (row.Rgb is not { } rgb) continue;
            var (top, bottom) = SnapSpan(y + row.Y, 1, originY, scaleY);
            if (bottom > top) rects.Add((left, top, right - left, bottom - top, rgb));
        }
        return rects;
    }
}
