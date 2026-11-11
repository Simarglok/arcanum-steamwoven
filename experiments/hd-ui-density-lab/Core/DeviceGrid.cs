namespace Arcanum.Experiments.HdUiDensityLab.Core;

/// <summary>A rectangle in device pixels.</summary>
public readonly record struct DeviceRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public DeviceRect Inset(int by) => new(X + by, Y + by, Math.Max(0, Width - 2 * by), Math.Max(0, Height - 2 * by));
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>A rectangle in UI units (pixels of the original's 800 × 600 screen).</summary>
public readonly record struct UnitRect(double X, double Y, double Width, double Height);

/// <summary>The device-pixel grid rule: edges are rounded independently (neighbours share edges), line widths are rounded
/// on their own and never drop below one device pixel.</summary>
public static class DeviceGrid
{
    public static int Round(double value) => (int)Math.Floor(value + 0.5);

    public static int Edge(double units, double scale) => Round(units * scale);

    public static int Line(double units, double scale) => Math.Max(1, Round(units * scale));

    /// <summary>Places a unit rectangle relative to a device-pixel origin.</summary>
    public static DeviceRect Place(UnitRect rect, double scale, int originX, int originY)
    {
        var x0 = originX + Edge(rect.X, scale);
        var y0 = originY + Edge(rect.Y, scale);
        var x1 = originX + Edge(rect.X + rect.Width, scale);
        var y1 = originY + Edge(rect.Y + rect.Height, scale);
        return new DeviceRect(x0, y0, x1 - x0, y1 - y0);
    }
}
