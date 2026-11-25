using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2890 (AC3): the part of the world the viewer dims while a menu window is open. Godot-free; the viewer draws the rectangles.
/// ARC-UI-3143: the column's generated end caps (the brass edge and the soft shadow) are retired with the generated chrome; a kit strip carries its own caps.
/// </summary>
public static class SteamwovenColumnEdges
{
    /// <summary>The rectangles of the world the viewer dims while a menu window is open: the frame left and right of the column (empty ones left out).</summary>
    public static IReadOnlyList<PixelRect> DimmedWorld(SteamwovenHudPlan plan) =>
        [.. new[] { plan.Column.WorldLeft, plan.Column.WorldRight }.Where(rect => rect.Width > 0 && rect.Height > 0)];
}
