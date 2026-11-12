namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-GPAD-1377 (epic #1371, concept §0.9): the whole-number scale of the original's windows and the smallest text a window may show on
/// the handheld (the Steam Deck's 7 inch 1280 × 800 screen; Valve's Deck Verified text rule: 9 px at least, 12 px recommended). Godot-free.
/// The original composes every window inside its 800 × 600 screen, so one whole factor serves them all: the largest that keeps the whole
/// screen inside the window. A window of 1280 × 800 has room for 1, a window of 1600 × 1200 for 2. A fractional or per-window factor is not
/// used (owner decision 2026-11-12: whole numbers only; hit areas may exceed the art, the art is not enlarged to make room for them).
/// </summary>
public static class AreaUiScale
{
    /// <summary>The handheld's window (the Steam Deck's screen) in device pixels.</summary>
    public const int HandheldWidth = 1280, HandheldHeight = 800;
    /// <summary>The smallest text, and the recommended one, on the handheld in pixels at the window's factor (owner 2026-11-12).</summary>
    public const int MinimumTextPx = 9, PreferredTextPx = 12;

    /// <summary>
    /// ARC-UI-1428: the text-only whole factor of a line whose original font is under <see cref="MinimumTextPx"/> at the screen's factor: the
    /// smallest whole multiplier (at least 1) that brings <paramref name="fontPx"/> × <paramref name="screenFactor"/> to the minimum. Only the
    /// text is multiplied (Garmond8 at screen factor 1 draws at 16 px, Garmond6 at 12 px); the window art keeps the screen factor. A font at or
    /// above the minimum, or with an unknown size (0 or less), is never multiplied.
    /// </summary>
    public static int TextOnlyFactor(int fontPx, int screenFactor)
    {
        var shown = fontPx * Math.Max(1, screenFactor);
        return fontPx <= 0 || shown >= MinimumTextPx ? 1 : (MinimumTextPx + shown - 1) / shown;
    }

    /// <summary>The whole factor at which the original's 800 × 600 screen fits a window of <paramref name="width"/> × <paramref name="height"/>
    /// device pixels (at least 1: a smaller window still draws the screen once and the window clips it).</summary>
    public static int ScreenFactor(float width, float height) =>
        Math.Max(1, (int)Math.Floor(Math.Min(width / AreaGameBarLayout.ScreenWidth, height / AreaGameBarLayout.ScreenHeight)));
}
