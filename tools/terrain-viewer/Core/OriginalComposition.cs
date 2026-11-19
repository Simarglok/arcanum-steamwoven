using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2936 (epic #2446, owner decision 2026-11-19): the Original look (HD interface option off, Legacy art) is one centred 4:3 composition of the original's 800 × 600 screen, scaled to the window with
/// <c>s = min(W / 800, H / 600)</c> device pixels per original pixel, the same rule as the Steamwoven column. It differs from the Steamwoven look in three ways: the sides (and above and below a window narrower than
/// 4:3) are black, never the world, so the play view shows exactly the original's 800 × 400 area; the Legacy art is scaled sharply: the screen is drawn at the next whole factor <c>n = ceil(s)</c> (a virtual screen of
/// 800 n × 600 n pixels, so every pixel of the art is a whole n × n block) and that picture is then smoothly reduced to <c>s</c>; at a whole <c>s</c> it is exactly the nearest-pixel picture. The viewer asks Godot for the virtual
/// screen (the window's content scale, viewport mode, aspect keep), so every interface part keeps its whole-factor placement at <c>n</c>. Godot-free: the plan, the black rectangles, the pointer mapping and the original's
/// fixed 800 × 400 out-of-sight window are checked as plain numbers.
/// </summary>
public static class OriginalComposition
{
    public const int ScreenWidth = AreaMenuArtLayout.ScreenWidth;
    public const int ScreenHeight = AreaMenuArtLayout.ScreenHeight;
    /// <summary>The original's play view in world pixels (the area between the strips): the out-of-sight rule of the Original rules keeps this fixed window (decision 2026-11-07, option b).</summary>
    public const int ViewWidth = 800;
    public const int ViewHeight = 400;

    /// <summary>The composition's scale <c>s</c> for a window of <paramref name="width"/> × <paramref name="height"/> device pixels (not rounded).</summary>
    public static double Scale(int width, int height) => SteamwovenColumnMenu.Fit(width, height).Factor;

    /// <summary>The whole factor <c>n</c> of the virtual screen: the next whole number at or above <c>s</c> (at least 1). A whole <c>s</c> gives <c>n = s</c> and no reduction.</summary>
    public static int WholeFactor(int width, int height)
    {
        var scale = Scale(width, height);
        return scale <= 1 ? 1 : (int)Math.Ceiling(scale - 1e-9);
    }

    /// <summary>The virtual screen's size in its own pixels: 800 n × 600 n.</summary>
    public static (int Width, int Height) VirtualSize(int width, int height)
    {
        var whole = WholeFactor(width, height);
        return (ScreenWidth * whole, ScreenHeight * whole);
    }

    /// <summary>The drawn composition's rectangle in device pixels (centred; see <see cref="SteamwovenColumnMenu.Fit"/> for the rounding).</summary>
    public static PixelRect Zone(int width, int height) => SteamwovenColumnMenu.Fit(width, height).Zone;

    /// <summary>The black rectangles around the composition: left and right over the window's whole height, above and below over the composition's width (a window narrower than 4:3).</summary>
    public static IReadOnlyList<PixelRect> BlackRects(int width, int height)
    {
        var zone = Zone(width, height);
        var rects = new[]
        {
            PixelRect.FromEdges(0, 0, zone.X, height),
            PixelRect.FromEdges(zone.Right, 0, width, height),
            PixelRect.FromEdges(zone.X, 0, zone.Right, zone.Y),
            PixelRect.FromEdges(zone.X, zone.Bottom, zone.Right, height),
        };
        return [.. rects.Where(rect => rect.Width > 0 && rect.Height > 0)];
    }

    /// <summary>A device point of the window as a point of the original 800 × 600 screen (units, not rounded): what the pointer is over. Points outside <see cref="Zone"/> map outside 0..800 × 0..600 (black).</summary>
    public static (double X, double Y) ToScreen(int width, int height, double deviceX, double deviceY)
    {
        var (factor, zone) = SteamwovenColumnMenu.Fit(width, height);
        return ((deviceX - zone.X) / factor, (deviceY - zone.Y) / factor);
    }

    /// <summary>A point of the original 800 × 600 screen as a device point of the window.</summary>
    public static (double X, double Y) ToDevice(int width, int height, double screenX, double screenY)
    {
        var (factor, zone) = SteamwovenColumnMenu.Fit(width, height);
        return (zone.X + screenX * factor, zone.Y + screenY * factor);
    }

    /// <summary>True when a device point lies in the drawn composition (not on a black rectangle).</summary>
    public static bool Contains(int width, int height, double deviceX, double deviceY)
    {
        var zone = Zone(width, height);
        return deviceX >= zone.X && deviceX < zone.Right && deviceY >= zone.Y && deviceY < zone.Bottom;
    }

    /// <summary>The camera zoom factor of the Original look relative to the player's zoom setting: the setting's default (<see cref="WorldZoomPolicy.DefaultZoom"/>) shows exactly the original's 800 × 400 view in the virtual screen
    /// (world zoom <paramref name="wholeFactor"/>); any other setting scales from there, so the player's zoom option keeps working (owner decision 2026-11-19, #2937).</summary>
    public static double ZoomScale(int wholeFactor) => Math.Max(1, wholeFactor) / WorldZoomPolicy.DefaultZoom;

    /// <summary>The play view's middle on the original's screen (the top strip's 41 units plus half of the 400-unit view), in units from the screen's top.</summary>
    public const double PlayViewCentreY = AreaGameBarLayout.TopHeight + ViewHeight / 2.0;

    /// <summary>The camera's shift in virtual screen pixels at the whole factor <paramref name="wholeFactor"/>: how far above the screen's middle the middle of the play view stands, so the camera's focus is drawn in the play view's middle.</summary>
    public static double ViewShift(int wholeFactor) => (ScreenHeight / 2.0 - PlayViewCentreY) * wholeFactor;

    /// <summary>The out-of-sight window of the Original rules in world pixels: the fixed 800 × 400 rectangle centred on <paramref name="centreX"/>, <paramref name="centreY"/> (the view's centre).</summary>
    public static (double X, double Y, double Width, double Height) OutOfSightWindow(double centreX, double centreY) =>
        (centreX - ViewWidth / 2.0, centreY - ViewHeight / 2.0, ViewWidth, ViewHeight);
}
