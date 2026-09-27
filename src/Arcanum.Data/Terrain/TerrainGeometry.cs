namespace Arcanum.Data.Terrain;

public static class TerrainGeometry
{
    public static TerrainDiagnosticPlacement? Place(TerrainKind kind, int localX, int localY, uint rawId,
        int frameWidth, int frameHeight, TerrainRect viewport)
    {
        if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
        if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
        var projected = Project(localX, localY);
        var footprint = new TerrainRect(checked(projected.X + 1), projected.Y, kind == TerrainKind.Tile ? 78 : 80, 40);
        var visible = Intersect(footprint, viewport);
        if (visible is null) return null;
        var requestedSource = new TerrainRect(checked(visible.Value.X - footprint.X), checked(visible.Value.Y - footprint.Y), visible.Value.Width, visible.Value.Height);
        var source = Intersect(requestedSource, new TerrainRect(0, 0, frameWidth, frameHeight));
        if (source is null) return null;
        var destination = new TerrainRect(checked(footprint.X + source.Value.X), checked(footprint.Y + source.Value.Y), source.Value.Width, source.Value.Height);
        return new TerrainDiagnosticPlacement(footprint, source.Value, destination, kind == TerrainKind.Tile && (rawId & 0xC1) == 0xC1, false);
    }

    public static TerrainPoint Project(int localX, int localY) => new(checked(40 * checked(localY - localX)), checked(20 * checked(localY + localX)));

    internal static TerrainRect? Intersect(TerrainRect first, TerrainRect second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(checked(first.X + first.Width), checked(second.X + second.Width));
        var bottom = Math.Min(checked(first.Y + first.Height), checked(second.Y + second.Height));
        return right > left && bottom > top ? new TerrainRect(left, top, checked(right - left), checked(bottom - top)) : null;
    }
}
