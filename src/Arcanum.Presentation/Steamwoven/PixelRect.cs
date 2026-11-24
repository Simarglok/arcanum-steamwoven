namespace Arcanum.Presentation.Steamwoven;

/// <summary>ARC-UI-2450: a rectangle in device pixels with the origin at the window's top-left corner; built from edges, so neighbouring parts share them (SWI-LAY-7).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public override string ToString() => $"{Width}x{Height}@{X},{Y}";
}

/// <summary>ARC-UI-2450: the black bars of a window per side, in device pixels.</summary>
public readonly record struct PixelInsets(int Left, int Top, int Right, int Bottom);
