using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One diagnostic composition: terrain commands first, static commands last.</summary>
public sealed class DiagnosticScenePlan
{
    private DiagnosticScenePlan(TerrainScenePlan terrain, StaticSceneryScenePlan? statics, ActorDiagnosticScenePlan? actor, TerrainRect bounds)
    {
        Terrain = terrain;
        Statics = statics;
        Actor = actor;
        WorldBounds = bounds;
    }

    public TerrainScenePlan Terrain { get; }
    public StaticSceneryScenePlan? Statics { get; }
    /// <summary>Optional fixed-profile actor. It is mutually exclusive with static diagnostic commands.</summary>
    public ActorDiagnosticScenePlan? Actor { get; }
    public TerrainRect WorldBounds { get; }

    public static DiagnosticScenePlan Create(TerrainScenePlan terrain, StaticSceneryScenePlan? statics,
        TerrainViewerProgress? progress = null) => Create(terrain, statics, null, progress);

    public static DiagnosticScenePlan Create(TerrainScenePlan terrain, StaticSceneryScenePlan? statics, ActorDiagnosticScenePlan? actor,
        TerrainViewerProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (statics is not null && actor is not null) throw new ArgumentException("Static and actor diagnostic modes are mutually exclusive.");
        if (statics is null && actor is null) return new DiagnosticScenePlan(terrain, null, null, terrain.WorldBounds);
        var rectangles = terrain.Commands.Select(command => command.Destination)
            .Concat(statics?.Commands.Select(command => command.Destination) ?? Array.Empty<TerrainRect>())
            .Concat(actor?.AdmittedBounds ?? Array.Empty<TerrainRect>()).ToArray();
        try
        {
            var left = rectangles.Min(rectangle => rectangle.X);
            var top = rectangles.Min(rectangle => rectangle.Y);
            var right = rectangles.Max(rectangle => checked(rectangle.X + rectangle.Width));
            var bottom = rectangles.Max(rectangle => checked(rectangle.Y + rectangle.Height));
            return new DiagnosticScenePlan(terrain, statics, actor, new TerrainRect(left, top, checked(right - left), checked(bottom - top)));
        }
        catch (OverflowException)
        {
            throw new TerrainViewerException(new TerrainViewerError(actor is null ? TerrainViewerErrorCode.StaticPlan : TerrainViewerErrorCode.ActorPlan,
                Stage: actor is null ? TerrainViewerFailureStage.Plan : TerrainViewerFailureStage.ActorPlan, Progress: progress));
        }
    }
}
