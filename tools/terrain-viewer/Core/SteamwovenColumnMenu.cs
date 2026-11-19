using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2891 (TRD "Column mode", epic #2446): the full-screen menus (main, single player, new game, in-play, options) and the two-strip screens (save, load, pre-made, new character, editor, shop) in column mode.
/// Owner decision 2026-11-19, after the first composite: the 800 × 600 plate is scaled up to fill the window's height from edge to edge, centred, and everything beside it is black; no wood sides, no vignette, no
/// full-width backdrop, no wood gap (the plate leaves none). The column plan's full-screen zone (a plate at `3.6 · k · f`) is not used by the menus. Godot-free, so the places, rectangles and hit tests are checked as
/// plain numbers; the viewer draws them.
/// </summary>
public static class SteamwovenColumnMenu
{
    /// <summary>
    /// The plate's scale and rectangle for a window of <paramref name="width"/> × <paramref name="height"/> device pixels: `min(width / 800, height / 600)` device pixels per original unit (the height fills the window from
    /// edge to edge at 4:3 and wider; a window narrower than 4:3 is limited by its width), the rectangle centred, each edge rounded from its exact position, halves up.
    /// </summary>
    public static (double Factor, PixelRect Zone) Fit(int width, int height)
    {
        var factor = Math.Min(width / (double)AreaMenuArtLayout.ScreenWidth, height / (double)AreaMenuArtLayout.ScreenHeight);
        var left = (int)Math.Round((width - AreaMenuArtLayout.ScreenWidth * factor) / 2, MidpointRounding.AwayFromZero);
        var top = (int)Math.Round((height - AreaMenuArtLayout.ScreenHeight * factor) / 2, MidpointRounding.AwayFromZero);
        var right = (int)Math.Round((width + AreaMenuArtLayout.ScreenWidth * factor) / 2, MidpointRounding.AwayFromZero);
        var bottom = (int)Math.Round((height + AreaMenuArtLayout.ScreenHeight * factor) / 2, MidpointRounding.AwayFromZero);
        return (factor, PixelRect.FromEdges(left, top, right, bottom));
    }

    /// <summary>The column-mode plan of the menus for a window, or null when <paramref name="options"/> are not in column mode or the layout is not supported (below f = 0.25).</summary>
    public static SteamwovenFullScreenPlan? Plan(int width, int height, SteamwovenLayoutOptions options) =>
        options.Column is null ? null : SteamwovenFullScreen.Plan(width, height, options) is { IsColumn: true } plan ? plan : null;

    /// <summary>The place of an 800 × 600 two-strip screen: its top left and the unit scale (the same hook shape as <see cref="SteamwovenWindowZone.Screen(int, int, int, SteamwovenLayoutOptions?)"/>).</summary>
    public static SteamwovenWindowPlace Place(SteamwovenFullScreenPlan plan) => new(plan.Factor, plan.Zone.X, plan.Zone.Y);

    /// <summary>The window's black rectangles around the plate: left and right over the whole window height, above and below the plate over its width (a window narrower than 4:3 has bars there).</summary>
    public static IReadOnlyList<PixelRect> BlackRects(SteamwovenFullScreenPlan plan)
    {
        var zone = plan.Zone;
        var (width, height) = (plan.Layout.WindowWidth, plan.Layout.WindowHeight);
        var rects = new List<PixelRect>(4)
        {
            PixelRect.FromEdges(0, 0, zone.X, height),
            PixelRect.FromEdges(zone.Right, 0, width, height),
            PixelRect.FromEdges(zone.X, 0, zone.Right, zone.Y),
            PixelRect.FromEdges(zone.X, zone.Bottom, zone.Right, height),
        };
        return [.. rects.Where(rect => rect.Width > 0 && rect.Height > 0)];
    }
}
