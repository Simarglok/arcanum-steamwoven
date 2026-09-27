using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

public sealed record TerrainDrawCommand(int Ordinal, TerrainTextureKey TextureKey, TerrainRect Source, TerrainRect Destination,
    bool FlipX, TerrainDiagnosticOrderKey Order);

public sealed class TerrainScenePlan
{
    private TerrainScenePlan(TerrainRect worldBounds, IReadOnlyList<TerrainDrawCommand> commands)
    {
        WorldBounds = worldBounds;
        Commands = commands;
    }

    public TerrainRect WorldBounds { get; }
    public IReadOnlyList<TerrainDrawCommand> Commands { get; }

    public static TerrainScenePlan Create(TerrainSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);
        if (sector.DiagnosticOrder.Count != CrashSiteProfile.CellCount) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, sector.Records.Count));
        var bounds = Bounds(sector.DiagnosticOrder.Select(record => record.DiagnosticFootprint));
        var commands = new List<TerrainDrawCommand>(sector.DiagnosticOrder.Count);
        foreach (var record in sector.DiagnosticOrder.OrderBy(record => record.DiagnosticOrderKey.Layer).ThenBy(record => record.DiagnosticOrderKey.LocalY).ThenBy(record => record.DiagnosticOrderKey.LocalX).ThenBy(record => record.Ordinal))
        {
            var placement = TerrainGeometry.Place(record.Kind, record.LocalX, record.LocalY, record.RawId,
                record.FrameMetadata.Width, record.FrameMetadata.Height, bounds);
            if (placement is null) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure, sector.Records.Count));
            commands.Add(new TerrainDrawCommand(record.Ordinal, TerrainTextureKey.FromRecord(record), placement.Value.Source,
                placement.Value.Destination, placement.Value.FlipX, record.DiagnosticOrderKey));
        }
        return new TerrainScenePlan(bounds, commands.AsReadOnly());
    }

    private static TerrainRect Bounds(IEnumerable<TerrainRect> rectangles)
    {
        var items = rectangles.ToArray();
        if (items.Length == 0) throw new ArgumentException("A terrain scene needs records.", nameof(rectangles));
        var left = items.Min(rectangle => rectangle.X);
        var top = items.Min(rectangle => rectangle.Y);
        var right = items.Max(rectangle => checked(rectangle.X + rectangle.Width));
        var bottom = items.Max(rectangle => checked(rectangle.Y + rectangle.Height));
        return new TerrainRect(left, top, checked(right - left), checked(bottom - top));
    }
}
