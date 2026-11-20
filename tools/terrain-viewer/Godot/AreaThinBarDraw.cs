using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3060: draws the thin bars the HD pack has no frame for (<see cref="AreaThinBarCode"/>) by code. True under the HD option when a pack is
/// open (the same condition the other hand-drawn HD parts use); off, the bars draw their Legacy art exactly as before.
/// </summary>
internal static class AreaThinBarDraw
{
    /// <summary>Whether the four bars draw by code now: the HD interface setting is on and a pack is open.</summary>
    internal static bool Active => InterfaceHd.Enabled && InterfaceHd.Pack is not null;

    /// <summary>The device transform of <paramref name="item"/>: its global transform with the canvas, then the viewport's stretch.</summary>
    internal static Transform2D Device(CanvasItem item) => item.GetViewport().GetStretchTransform() * item.GetGlobalTransformWithCanvas();

    /// <summary>Draws art <paramref name="art"/>'s bar for <paramref name="filled"/> Legacy units from (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="item"/>,
    /// every edge on a whole device pixel. Returns how many rectangles it drew.</summary>
    internal static int Draw(CanvasItem item, int art, float x, float y, int filled)
    {
        var device = Device(item);
        var drawn = 0;
        foreach (var (rx, ry, rw, rh, rgb) in AreaThinBarCode.Rects(art, x, y, filled, device.Origin.X, device.Origin.Y, device.Scale.X, device.Scale.Y))
        {
            item.DrawRect(new Rect2(rx, ry, rw, rh), Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            drawn++;
        }
        return drawn;
    }
}
