using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One diagnostic composition: terrain commands first, static commands last.</summary>
public sealed class DiagnosticScenePlan
{
    private DiagnosticScenePlan(TerrainScenePlan terrain, StaticSceneryScenePlan? statics, TerrainRect bounds)
    {
        Terrain = terrain;
        Statics = statics;
        WorldBounds = bounds;
    }

    public TerrainScenePlan Terrain { get; }
    public StaticSceneryScenePlan? Statics { get; }
    public TerrainRect WorldBounds { get; }

    public static DiagnosticScenePlan Create(TerrainScenePlan terrain, StaticSceneryScenePlan? statics)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (statics is null) return new DiagnosticScenePlan(terrain, null, terrain.WorldBounds);
        var rectangles = terrain.Commands.Select(command => command.Destination).Concat(statics.Commands.Select(command => command.Destination)).ToArray();
        try
        {
            var left = rectangles.Min(rectangle => rectangle.X);
            var top = rectangles.Min(rectangle => rectangle.Y);
            var right = rectangles.Max(rectangle => checked(rectangle.X + rectangle.Width));
            var bottom = rectangles.Max(rectangle => checked(rectangle.Y + rectangle.Height));
            return new DiagnosticScenePlan(terrain, statics, new TerrainRect(left, top, checked(right - left), checked(bottom - top)));
        }
        catch (OverflowException)
        {
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticPlan));
        }
    }
}
