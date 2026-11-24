using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2890 (AC2, AC3): where the column's generated end caps stand: the parts of the column that border the world (the two strips; the middle band as well while a menu window is open) and the widths of the brass edge
/// and the soft shadow it throws on the world. Godot-free; the viewer draws the rectangles.
/// </summary>
public static class SteamwovenColumnEdges
{
    /// <summary>The brass edge's width in device pixels: about one original unit at the column's unit scale (at least 2 px), drawn over the outermost pixels of the part.</summary>
    public static int BrassWidth(SteamwovenHudPlan plan) => Math.Max(2, (int)Math.Round(1.1 * SteamwovenWindowZone.UnitScale(plan), MidpointRounding.AwayFromZero));

    /// <summary>The soft shadow's width on the world, outside the part: seven original units at the column's unit scale.</summary>
    public static int ShadowWidth(SteamwovenHudPlan plan) => (int)Math.Round(7 * SteamwovenWindowZone.UnitScale(plan), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The parts of the column that carry an end cap: the top strip and the bottom strip, and between them the middle band while <paramref name="menuOpen"/>, so the menu window's sides are capped like the strips'.
    /// The rectangles tile the column's height (the strips and the middle band share their edges).
    /// </summary>
    public static IReadOnlyList<PixelRect> Parts(SteamwovenHudPlan plan, bool menuOpen)
    {
        var column = plan.Column;
        var parts = new List<PixelRect>(3) { column.TopStrip };
        var middle = PixelRect.FromEdges(column.Column.X, column.TopStrip.Bottom, column.Column.Right, column.BottomStrip.Y);
        if (menuOpen && middle.Height > 0) parts.Add(middle);
        parts.Add(column.BottomStrip);
        return parts;
    }

    /// <summary>The rectangles of the world the viewer dims while a menu window is open: the frame left and right of the column (empty ones left out).</summary>
    public static IReadOnlyList<PixelRect> DimmedWorld(SteamwovenHudPlan plan) =>
        [.. new[] { plan.Column.WorldLeft, plan.Column.WorldRight }.Where(rect => rect.Width > 0 && rect.Height > 0)];
}
