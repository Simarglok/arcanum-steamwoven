using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2577 (docs/ui/steamwoven-interface-trd.md SWI-BDR-6): where a full-screen menu (an original 800 × 600 background: the main, single-player,
/// new-game, in-play and options windows) stands in the Steamwoven layout for one window size. The menu's 800 × 600 zone is centred in the frame
/// and drawn at <see cref="Factor"/> = `f · 3.6` device pixels per original unit; a generated backdrop covers the whole <see cref="Frame"/> and the
/// window outside it is black. Godot-free, so the viewer's placement and the hit tests are checked as plain numbers.
/// </summary>
public sealed record SteamwovenFullScreenPlan(SteamwovenLayout Layout, double Factor, PixelRect Zone)
{
    /// <summary>The frame (the generated backdrop's rectangle in device pixels of the window).</summary>
    public PixelRect Frame => Layout.Frame;

    /// <summary>The black bars outside the frame.</summary>
    public PixelInsets Bars => Layout.Bars;

    /// <summary>A device point of the window as a point of the original 800 × 600 screen (units, not rounded).</summary>
    public (double X, double Y) ToScreen(double deviceX, double deviceY) => ((deviceX - Zone.X) / Factor, (deviceY - Zone.Y) / Factor);

    /// <summary>A point of the original 800 × 600 screen as a device point of the window.</summary>
    public (double X, double Y) ToDevice(double screenX, double screenY) => (Zone.X + screenX * Factor, Zone.Y + screenY * Factor);

    /// <summary>The button under a device point (an index of <paramref name="buttons"/>, rectangles on the 800 × 600 screen), or -1.</summary>
    public int HitTest(IReadOnlyList<(int X, int Y, int Width, int Height)> buttons, double deviceX, double deviceY)
    {
        var (x, y) = ToScreen(deviceX, deviceY);
        return AreaMenuArtLayout.HitTest(buttons, x, y);
    }
}

/// <summary>ARC-UI-2577: the plan builder of the full-screen menus.</summary>
public static class SteamwovenFullScreen
{
    /// <summary>The plan for a window of <paramref name="width"/> × <paramref name="height"/> device pixels, or null where the layout is not supported (below f = 0.25).</summary>
    public static SteamwovenFullScreenPlan? Plan(int width, int height)
    {
        if (SteamwovenLayoutBuilder.Compute(width, height).Layout is not { } layout) return null;
        var unit = layout.Scale * new Ratio(SteamwovenGrid.UnitNumerator, SteamwovenGrid.UnitDenominator);
        return new SteamwovenFullScreenPlan(layout, unit.ToDouble(), layout.FullScreenBackdrop.Zone);
    }
}
