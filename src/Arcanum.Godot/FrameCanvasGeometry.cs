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

        RawHotspotBounds = CalculateBounds(Frames, FrameAlignment.RawHotspot);
        CenteredBounds = CalculateBounds(Frames, FrameAlignment.Centered);
    }

    public IReadOnlyList<FrameCanvasFrame> Frames { get; }
    public FrameCanvasBounds RawHotspotBounds { get; }
    public FrameCanvasBounds CenteredBounds { get; }

    public FrameCanvasBounds GetBounds(FrameAlignment alignment) => alignment switch
    {
        FrameAlignment.RawHotspot => RawHotspotBounds,
        FrameAlignment.Centered => CenteredBounds,
        _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
    };

    private static FrameCanvasBounds CalculateBounds(IReadOnlyList<FrameCanvasFrame> frames, FrameAlignment alignment)
    {
        long? left = null;
        long? top = null;
        long? right = null;
        long? bottom = null;

        checked
        {
            foreach (var frame in frames)
            {
                var frameLeft = alignment == FrameAlignment.RawHotspot ? -(long)frame.Hotspot.X : -((long)frame.Width / 2);
                var frameTop = alignment == FrameAlignment.RawHotspot ? -(long)frame.Hotspot.Y : -((long)frame.Height / 2);
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

    private static void ValidateViewerBounds(FrameCanvasBounds bounds)
    {
        if (Math.Abs(bounds.Left) > ViewerCoordinateLimit || Math.Abs(bounds.Top) > ViewerCoordinateLimit ||
            Math.Abs(bounds.Right) > ViewerCoordinateLimit || Math.Abs(bounds.Bottom) > ViewerCoordinateLimit ||
            bounds.Width > ViewerCoordinateLimit || bounds.Height > ViewerCoordinateLimit)
        {
            throw new FrameCanvasGeometryLimitException($"Frame group geometry exceeds the viewer precision limit of ±{ViewerCoordinateLimit}.");
        }
    }
}
