namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1428 (follow-up of ARC-GPAD-1377, epic #1371): the text rule of the reading windows (telegram, newspaper, note, book, plaque) on the
/// handheld, Godot-free. The original draws them with the fonts named below; two of those are under the 9 px minimum at the whole screen factor
/// 1 of the 1280 × 800 and 1920 × 1080 handhelds: Garmond8 (the newspaper masthead's three small lines, size 8) and Garmond6 (size 6; no
/// installed text uses it, a markup font number may name it). The rule (owner decision 2026-11-12: whole numbers only): the line is drawn with a
/// <b>text-only</b> whole factor, the smallest that reaches 9 px (<see cref="AreaUiScale.TextOnlyFactor"/>: Garmond8 → 16 px, Garmond6 → 12 px);
/// the window art keeps the screen factor. The viewer's diagnostic panel draws these lines with its fallback font at that size; a later drawing
/// with the original fonts applies the same factor to the glyphs. A font at or above 9 px is drawn at its own size times the screen factor.
/// </summary>
public static class AreaReadingText
{
    /// <summary>The size in px a masthead or header line takes when its font's size is not known (the file name has no number).</summary>
    public const int UnknownFontPx = 16;

    /// <summary>The largest size the fallback font draws a header line at (the original's 30 px mastheads fit the panel up to here).</summary>
    public const int MaximumLinePx = 32;

    /// <summary>The fonts a reading window draws, by interface art number: the size in the font's file name (0 = the name has none).
    /// 480 Courier10 (telegram), 497 Pepper20 (note), 485 Zurich20, 484 Georgia30 and 482 Garmond8 (newspaper masthead), 815 Euph30 (the
    /// alternate masthead), 483 Garmond9, 492 Zurich16 and 490 Swiss921 (newspaper text), 481 Garmond6, 478 and 479 (18 px books), 28 pork12,
    /// 37 casablanca16, 171 Cloister18 and 229 Flare12 (book and plaque markup), 744 BookImagesFont (an image font).</summary>
    public static readonly IReadOnlyDictionary<int, int> FontPx = new Dictionary<int, int>
    {
        [480] = 10, [481] = 6, [482] = 8, [483] = 9, [484] = 30, [485] = 20, [492] = 16, [497] = 20, [815] = 30,
        [478] = 18, [479] = 18, [28] = 12, [37] = 16, [171] = 18, [229] = 12, [490] = 0, [744] = 0,
    };

    /// <summary>The size in px of a header line of font <paramref name="fontArt"/> at the screen factor <paramref name="screenFactor"/>: the
    /// font's size times the screen factor times the text-only factor, at least <see cref="AreaUiScale.MinimumTextPx"/>, at most
    /// <see cref="MaximumLinePx"/> times the screen factor. A font the table does not know takes <see cref="UnknownFontPx"/>.</summary>
    public static int HeaderLinePx(int fontArt, int screenFactor)
    {
        var factor = Math.Max(1, screenFactor);
        var nominal = FontPx.TryGetValue(fontArt, out var size) && size > 0 ? size : UnknownFontPx;
        var shown = nominal * factor * AreaUiScale.TextOnlyFactor(nominal, factor);
        return Math.Clamp(shown, AreaUiScale.MinimumTextPx, Math.Max(AreaUiScale.MinimumTextPx, MaximumLinePx * factor));
    }
}
