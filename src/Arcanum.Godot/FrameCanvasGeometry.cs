namespace Arcanum.Godot;

public enum FrameAlignment
{
    RawHotspot,
    Centered,
}

public readonly record struct FrameHotspot(int X, int Y)
{
    public static FrameHotspot Zero { get; } = new(0, 0);
}

public readonly record struct FrameCanvasFrame(int Width, int Height, FrameHotspot Hotspot)
{
    public FrameCanvasFrame(int width, int height, int hotX, int hotY)
        : this(width, height, new FrameHotspot(hotX, hotY))
    {
    }

    internal void Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Frame dimensions must be positive.");
        }
    }
}

public readonly record struct FrameCanvasBounds(long Left, long Top, long Right, long Bottom)
{
    public long Width => checked(Right - Left);
    public long Height => checked(Bottom - Top);
    public long CenterX => checked(Left + (Width / 2));
    public long CenterY => checked(Top + (Height / 2));

    public FrameCanvasWorldBounds ToWorldBounds() => new(
        checked(Left * 2),
        checked(Top * 2),
        checked(Right * 2),
        checked(Bottom * 2));
}

// Coordinates are stored in exact half-world-unit increments so centered odd-sized frames
// share the same bounds that FrameCanvas renders with texture-size / 2f.
public readonly record struct FrameCanvasWorldBounds(long LeftTwice, long TopTwice, long RightTwice, long BottomTwice)
{
    public double Left => LeftTwice / 2d;
    public double Top => TopTwice / 2d;
    public double Right => RightTwice / 2d;
    public double Bottom => BottomTwice / 2d;
    public double Width => checked(RightTwice - LeftTwice) / 2d;
    public double Height => checked(BottomTwice - TopTwice) / 2d;
    public double CenterX => checked(LeftTwice + RightTwice) / 4d;
    public double CenterY => checked(TopTwice + BottomTwice) / 4d;
}

public sealed class FrameCanvasGeometryLimitException : Exception
{
    public FrameCanvasGeometryLimitException(string message) : base(message)
    {
    }
}

public sealed class FrameCanvasGroupLayout
{
    public const long ViewerCoordinateLimit = 16_777_216;

    public FrameCanvasGroupLayout(IEnumerable<FrameCanvasFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        Frames = frames.ToArray();
        if (Frames.Count == 0)
        {
            throw new ArgumentException("A physical group must contain at least one frame.", nameof(frames));
        }

        foreach (var frame in Frames)
        {
            frame.Validate();
        }

        RawHotspotBounds = CalculateRawHotspotBounds(Frames);
        CenteredBounds = CalculateCenteredBounds(Frames);
    }

    public IReadOnlyList<FrameCanvasFrame> Frames { get; }
    public FrameCanvasBounds RawHotspotBounds { get; }
    public FrameCanvasWorldBounds CenteredBounds { get; }

    public FrameCanvasWorldBounds GetBounds(FrameAlignment alignment) => alignment switch
    {
        FrameAlignment.RawHotspot => RawHotspotBounds.ToWorldBounds(),
        FrameAlignment.Centered => CenteredBounds,
        _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
    };

    private static FrameCanvasBounds CalculateRawHotspotBounds(IReadOnlyList<FrameCanvasFrame> frames)
    {
        long? left = null;
        long? top = null;
        long? right = null;
        long? bottom = null;

        checked
        {
            foreach (var frame in frames)
            {
                var frameLeft = -(long)frame.Hotspot.X;
                var frameTop = -(long)frame.Hotspot.Y;
                var frameRight = frameLeft + frame.Width;
                var frameBottom = frameTop + frame.Height;
                left = left is null ? frameLeft : Math.Min(left.Value, frameLeft);
                top = top is null ? frameTop : Math.Min(top.Value, frameTop);
                right = right is null ? frameRight : Math.Max(right.Value, frameRight);
                bottom = bottom is null ? frameBottom : Math.Max(bottom.Value, frameBottom);
            }
        }

        var bounds = new FrameCanvasBounds(left!.Value, top!.Value, right!.Value, bottom!.Value);
        ValidateViewerBounds(bounds);
        return bounds;
    }

    private static FrameCanvasWorldBounds CalculateCenteredBounds(IReadOnlyList<FrameCanvasFrame> frames)
    {
        long? leftTwice = null;
        long? topTwice = null;
        long? rightTwice = null;
        long? bottomTwice = null;

        checked
        {
            foreach (var frame in frames)
            {
                var frameLeftTwice = -(long)frame.Width;
                var frameTopTwice = -(long)frame.Height;
                var frameRightTwice = (long)frame.Width;
                var frameBottomTwice = (long)frame.Height;
                leftTwice = leftTwice is null ? frameLeftTwice : Math.Min(leftTwice.Value, frameLeftTwice);
                topTwice = topTwice is null ? frameTopTwice : Math.Min(topTwice.Value, frameTopTwice);
                rightTwice = rightTwice is null ? frameRightTwice : Math.Max(rightTwice.Value, frameRightTwice);
                bottomTwice = bottomTwice is null ? frameBottomTwice : Math.Max(bottomTwice.Value, frameBottomTwice);
            }
        }

        var bounds = new FrameCanvasWorldBounds(leftTwice!.Value, topTwice!.Value, rightTwice!.Value, bottomTwice!.Value);
        ValidateViewerBounds(bounds);
        return bounds;
    }

    private static void ValidateViewerBounds(FrameCanvasBounds bounds)
    {
        if (Math.Abs(bounds.Left) > ViewerCoordinateLimit || Math.Abs(bounds.Top) > ViewerCoordinateLimit ||
            Math.Abs(bounds.Right) > ViewerCoordinateLimit || Math.Abs(bounds.Bottom) > ViewerCoordinateLimit ||
            bounds.Width > ViewerCoordinateLimit || bounds.Height > ViewerCoordinateLimit)
        {
            throw new FrameCanvasGeometryLimitException($"Frame group geometry exceeds the viewer precision limit of ±{ViewerCoordinateLimit}.");
        }
    }

    private static void ValidateViewerBounds(FrameCanvasWorldBounds bounds)
    {
        var twiceViewerCoordinateLimit = checked(ViewerCoordinateLimit * 2);
        if (bounds.LeftTwice < -twiceViewerCoordinateLimit || bounds.TopTwice < -twiceViewerCoordinateLimit ||
            bounds.RightTwice > twiceViewerCoordinateLimit || bounds.BottomTwice > twiceViewerCoordinateLimit ||
            bounds.RightTwice - bounds.LeftTwice > twiceViewerCoordinateLimit ||
            bounds.BottomTwice - bounds.TopTwice > twiceViewerCoordinateLimit)
        {
            throw new FrameCanvasGeometryLimitException($"Frame group geometry exceeds the viewer precision limit of ±{ViewerCoordinateLimit}.");
        }
    }
}
