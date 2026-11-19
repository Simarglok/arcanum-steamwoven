using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-2891: what shows left and right of the column behind a full-screen or two-strip menu (an option of the column mode, owner decision 2026-11-19).</summary>
public enum SteamwovenColumnSides
{
    /// <summary>Plain black.</summary>
    Black,
    /// <summary>A soft vignette: the plate's own dark edge tone, fading to black over <see cref="SteamwovenColumnMenu.VignetteUnits"/> original units.</summary>
    Vignette,
}

/// <summary>
/// ARC-UI-2891 (TRD "Column mode", epic #2446): the full-screen menus (main, single player, new game, in-play, options) and the two-strip screens (save, load, pre-made, new character, editor, shop) in the column. Each draws
/// its 800 × 600 plate in the column plan's full-screen zone (<see cref="SteamwovenColumnPlan.FullScreen"/>: centred in the whole frame at the strip scale `3.6 · k · f`); left and right of the column the window is black or a vignette
/// (<see cref="Sides"/>), with no wood sides and no full-width backdrop; the generated wood fills only a vertical gap the plate leaves in the column (16:10, or k = 0.8). Godot-free, so the places, rectangles and hit tests
/// are checked as plain numbers; the viewer draws them.
/// </summary>
public static class SteamwovenColumnMenu
{
    /// <summary>The width of the vignette's fall-off in original units (the plate's edge to black).</summary>
    public const int VignetteUnits = 160;

    /// <summary>The environment variable that sets <see cref="Sides"/> at start (<c>black</c> or <c>vignette</c>).</summary>
    public const string SidesVariable = "ARC_AREA_MENU_COLUMN_SIDES";

    /// <summary>What shows beside the column. A run-time option of the viewer: the environment variable <see cref="SidesVariable"/> at start (the smoke sets it per case); black by default.</summary>
    public static SteamwovenColumnSides Sides { get; set; } = ParseSides(System.Environment.GetEnvironmentVariable(SidesVariable)) ?? SteamwovenColumnSides.Black;

    /// <summary>Reads <c>black</c> or <c>vignette</c> (any case); null when the text is neither.</summary>
    public static SteamwovenColumnSides? ParseSides(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "black" => SteamwovenColumnSides.Black,
        "vignette" => SteamwovenColumnSides.Vignette,
        _ => null,
    };

    /// <summary>The column plan of the menus for a window, or null when <paramref name="options"/> are not in column mode or the layout is not supported (below f = 0.25).</summary>
    public static SteamwovenFullScreenPlan? Plan(int width, int height, SteamwovenLayoutOptions options) =>
        options.Column is null ? null : SteamwovenFullScreen.Plan(width, height, options) is { IsColumn: true } plan ? plan : null;

    /// <summary>The place of an 800 × 600 two-strip screen in the column's full-screen zone: its top left and the unit scale (the same hook shape as <see cref="SteamwovenWindowZone.Screen(int, int, int, SteamwovenLayoutOptions?)"/>).</summary>
    public static SteamwovenWindowPlace Place(SteamwovenFullScreenPlan plan) => new(plan.Factor, plan.Zone.X, plan.Zone.Y);

    /// <summary>The window's black rectangles around the column: left and right over the whole window height, above and below the column over its width (a letterboxed frame leaves bars there).</summary>
    public static IReadOnlyList<PixelRect> BlackRects(SteamwovenFullScreenPlan plan)
    {
        var column = (plan.Column ?? throw new ArgumentException("not a column plan", nameof(plan))).Column;
        var (width, height) = (plan.Layout.WindowWidth, plan.Layout.WindowHeight);
        var rects = new List<PixelRect>(4)
        {
            PixelRect.FromEdges(0, 0, column.X, height),
            PixelRect.FromEdges(column.Right, 0, width, height),
            PixelRect.FromEdges(column.X, 0, column.Right, column.Y),
            PixelRect.FromEdges(column.X, column.Bottom, column.Right, height),
        };
        return [.. rects.Where(rect => rect.Width > 0 && rect.Height > 0)];
    }

    /// <summary>The vignette's rectangles: <c>Left</c> ends at the plate's left edge and <c>Right</c> starts at its right edge, over the plate's own rows, each as wide as <see cref="VignetteUnits"/> at the plate's scale or the side if that is narrower.</summary>
    public static (PixelRect Left, PixelRect Right) VignetteRects(SteamwovenFullScreenPlan plan)
    {
        var zone = plan.Zone;
        var wide = (int)Math.Round(VignetteUnits * plan.Factor, MidpointRounding.AwayFromZero);
        var left = Math.Min(wide, zone.X);
        var right = Math.Min(wide, plan.Layout.WindowWidth - zone.Right);
        return (PixelRect.FromEdges(zone.X - left, zone.Y, zone.X, zone.Bottom), PixelRect.FromEdges(zone.Right, zone.Y, zone.Right + right, zone.Bottom));
    }

    /// <summary>The vignette's weight at <paramref name="t"/> from the plate's edge (0) to the end of the fall-off (1): 1 at the edge, 0 at the end, a smooth step between.</summary>
    public static double VignetteWeight(double t)
    {
        var x = Math.Clamp(t, 0, 1);
        return 1 - x * x * (3 - 2 * x);
    }
}
