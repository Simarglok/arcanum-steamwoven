namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-3059 (owner decision 2026-11-20, #2937 option C): in the Steamwoven look the player chooses the world's zoom in whole steps (1x, 2x, 3x, 4x), capped by the composition's scale <c>s</c> plus a quarter step (device pixels per original unit,
/// <c>min(width / 800, height / 600)</c> in the column). A whole step keeps the 1x world art crisp (nearest sampling is exact at it). The setting is 0 (automatic: the highest step not above <c>s</c>) or a step; a step above the cap
/// reads as the cap, so a smaller window never shows a zoom it does not offer. The Steamwoven out-of-sight rules test the drawn view (decision 2026-11-07), so they follow the zoom: an accepted consequence.
/// </summary>
public static class SteamwovenWorldZoom
{
    /// <summary>The highest step offered, where the screen allows it.</summary>
    public const int MaxStep = 4;
    /// <summary>The setting that picks the default step (the highest step offered).</summary>
    public const int Automatic = 0;

    /// <summary>How far a step may stand above <c>s</c> and still count as "not above" it (a quarter step). The issue's own defaults need it: 1920 × 1080 has s = 1.8 and its default is 2x, while 3840 × 2160 (s = 3.6) stays at 3x. A step is
    /// offered when <c>step ≤ s + Tolerance</c>.</summary>
    public const double Tolerance = .25;

    /// <summary>The highest step offered at scale <paramref name="scale"/>: floor(s + <see cref="Tolerance"/>), at least 1 and at most <see cref="MaxStep"/>. A scale that is not a positive number offers 1x only.</summary>
    public static int Cap(double scale) => !double.IsFinite(scale) || scale < 1 ? 1 : (int)Math.Min(MaxStep, Math.Floor(scale + Tolerance + 1e-9));

    /// <summary>The steps offered at scale <paramref name="scale"/>, ascending (1 to the cap).</summary>
    public static IReadOnlyList<int> Steps(double scale) => Enumerable.Range(1, Cap(scale)).ToArray();

    /// <summary>The default step: the highest one offered (the whole step nearest to <c>s</c> that is not above it).</summary>
    public static int Default(double scale) => Cap(scale);

    /// <summary>The step in force for <paramref name="setting"/> (0 or a step, 1 to <see cref="MaxStep"/>; any other value reads as automatic) at scale <paramref name="scale"/>.</summary>
    public static int Resolve(int setting, double scale) => setting is >= 1 and <= MaxStep ? Math.Min(setting, Cap(scale)) : Default(scale);

    /// <summary>The setting a free zoom value (the palette's spin box, the held zoom trigger) stands for: the nearest offered step, the lower one on a tie.</summary>
    public static int Snap(double zoom, double scale)
    {
        var cap = Cap(scale);
        if (!double.IsFinite(zoom)) return cap;
        return Math.Clamp((int)Math.Ceiling(zoom - .5), 1, cap);
    }

    /// <summary>The settings a player can pick at scale <paramref name="scale"/>: automatic, then the steps offered.</summary>
    public static bool Offered(int setting, double scale) => setting == Automatic || setting >= 1 && setting <= Cap(scale);

    /// <summary>The world rectangle the camera draws for a viewport of <paramref name="viewportWidth"/> × <paramref name="viewportHeight"/> device pixels at <paramref name="zoom"/> centred on (<paramref name="centerX"/>, <paramref name="centerY"/>):
    /// the drawn view, which the Steamwoven out-of-sight rules test (decision 2026-11-07). Returns (x, y, width, height) in world pixels.</summary>
    public static (double X, double Y, double Width, double Height) DrawnView(double viewportWidth, double viewportHeight, double zoom, double centerX, double centerY)
    {
        var width = viewportWidth / zoom; var height = viewportHeight / zoom;
        return (centerX - width / 2, centerY - height / 2, width, height);
    }

    /// <summary>Screen position (device pixels) of a world point at <paramref name="zoom"/> with the view centred on (<paramref name="centerX"/>, <paramref name="centerY"/>): the mapping every overlay follows.</summary>
    public static (double X, double Y) ToScreen(double worldX, double worldY, double viewportWidth, double viewportHeight, double zoom, double centerX, double centerY) =>
        (viewportWidth / 2 + (worldX - centerX) * zoom, viewportHeight / 2 + (worldY - centerY) * zoom);

    /// <summary>World position of a screen point: the inverse of <see cref="ToScreen"/> (hit testing, hover, scrolling).</summary>
    public static (double X, double Y) ToWorld(double screenX, double screenY, double viewportWidth, double viewportHeight, double zoom, double centerX, double centerY) =>
        (centerX + (screenX - viewportWidth / 2) / zoom, centerY + (screenY - viewportHeight / 2) / zoom);
}
