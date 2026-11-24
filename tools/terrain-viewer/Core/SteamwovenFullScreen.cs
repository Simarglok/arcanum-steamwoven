using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2577, ARC-UI-2891 (docs/ui/steamwoven-interface-trd.md "Column mode"): where a full-screen menu (an original 800 × 600 background: the main, single-player,
/// new-game, in-play and options windows) stands in the Steamwoven layout for one window size. The menu's 800 × 600 plate fills the window's height from edge to edge, centred, drawn at
/// <see cref="Factor"/> device pixels per original unit (<see cref="SteamwovenColumnMenu.Fit"/>); the window outside it is black. Godot-free, so the viewer's placement and the hit tests are checked as plain numbers.
/// </summary>
public sealed record SteamwovenFullScreenPlan(SteamwovenLayout Layout, double Factor, PixelRect Zone)
{
    /// <summary>The column plan of the layout (ARC-UI-2891). The menus do not use the column plan's full-screen zone (owner decision 2026-11-19: the plate fills the window's height edge to edge): the zone and factor are those of <see cref="SteamwovenColumnMenu.Fit"/>.</summary>
    public SteamwovenColumnPlan Column => Layout.Column;

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
    /// <summary>
    /// The plan for a window of <paramref name="width"/> × <paramref name="height"/> device pixels, or null where the layout is not supported (below f = 0.25). The plate is fitted to the window's height (<see cref="SteamwovenColumnMenu.Fit"/>).
    /// </summary>
    public static SteamwovenFullScreenPlan? Plan(int width, int height, SteamwovenLayoutOptions? options = null)
    {
        if (SteamwovenLayoutBuilder.Compute(width, height, options ?? SteamwovenLayoutOptions.Default).Layout is not { } layout) return null;
        var (factor, zone) = SteamwovenColumnMenu.Fit(width, height);
        return new SteamwovenFullScreenPlan(layout, factor, zone);
    }
}
