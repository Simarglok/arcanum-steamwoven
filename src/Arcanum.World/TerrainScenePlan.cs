using Arcanum.Data.Terrain;

namespace Arcanum.World;

public sealed record TerrainDrawCommand(int Ordinal, TerrainTextureKey TextureKey, TerrainRect Footprint, TerrainRect Source,
    TerrainRect Destination, bool FlipX, TerrainDiagnosticOrderKey Order);

/// <summary>
/// The minimum record information consumed by the scene planner.  Synthetic
/// render tests use this public shape so they exercise the same placement and
/// ordering path as loaded terrain rather than supplying precomputed commands.
/// </summary>
public sealed record TerrainPlanInput(int Ordinal, TerrainTextureKey TextureKey, TerrainKind Kind, int LocalX, int LocalY,
    uint RawId, int FrameWidth, int FrameHeight, TerrainDiagnosticOrderKey Order);

public sealed class TerrainScenePlan
{
    private TerrainScenePlan(TerrainRect worldBounds, IReadOnlyList<TerrainDrawCommand> commands)
    {
        WorldBounds = worldBounds;
        Commands = commands;
    }

    public TerrainRect WorldBounds { get; }
    public IReadOnlyList<TerrainDrawCommand> Commands { get; }

    /// <summary>Publishes one already-planned command without recomputing geometry.</summary>
    public TerrainScenePlan Isolate(int ordinal)
    {
        var command = Commands.Single(candidate => candidate.Ordinal == ordinal);
        return new TerrainScenePlan(command.Destination, [command]);
    }

    public static TerrainScenePlan Create(TerrainSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);
        if (sector.DiagnosticOrder.Count != CrashSiteProfile.CellCount) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, sector.Records.Count));
        var bounds = Bounds(sector.DiagnosticOrder.Select(record => record.DiagnosticFootprint));
        return CreatePlaced(bounds, sector.DiagnosticOrder.Select(record => new TerrainPlanInput(record.Ordinal, TerrainTextureKey.FromRecord(record),
            record.Kind, record.LocalX, record.LocalY, record.RawId, record.FrameMetadata.Width, record.FrameMetadata.Height,
            record.DiagnosticOrderKey)));
    }

    /// <summary>Creates a synthetic plan through the production placement and ordering path.</summary>
    public static TerrainScenePlan CreateSynthetic(TerrainRect worldBounds, IEnumerable<TerrainPlanInput> inputs) => CreatePlaced(worldBounds, inputs);

    private static TerrainScenePlan CreatePlaced(TerrainRect bounds, IEnumerable<TerrainPlanInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        // Layer is a rendering policy derived from the record kind, not a
        // caller-supplied ordering result. Synthetic callers exercise this
        // same path and cannot forge a facade into the tile layer.
        var ordered = inputs.OrderBy(input => LayerFor(input.Kind)).ThenBy(input => input.LocalY)
            .ThenBy(input => input.LocalX).ThenBy(input => input.Ordinal).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("A terrain scene needs commands.", nameof(inputs));
        var commands = new List<TerrainDrawCommand>(ordered.Length);
        foreach (var input in ordered)
        {
            var placement = TerrainGeometry.Place(input.Kind, input.LocalX, input.LocalY, input.RawId,
                input.FrameWidth, input.FrameHeight, bounds);
            if (placement is null) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure, ordered.Length));
            var order = new TerrainDiagnosticOrderKey(input.Order.Mode, LayerFor(input.Kind), input.LocalY, input.LocalX);
            commands.Add(new TerrainDrawCommand(input.Ordinal, input.TextureKey, placement.Value.Footprint, placement.Value.Source,
                placement.Value.Destination, placement.Value.FlipX, order));
        }
        return new TerrainScenePlan(bounds, commands.AsReadOnly());
    }

    private static int LayerFor(TerrainKind kind) => kind switch
    {
        TerrainKind.Tile => 0,
        TerrainKind.Facade => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

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
