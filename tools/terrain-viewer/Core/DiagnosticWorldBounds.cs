using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One immutable full-sector presentation envelope, including every admitted mirror/frame and guides.</summary>
public static class DiagnosticWorldBounds
{
    public static TerrainRect Create(TerrainScenePlan terrain, StaticSceneryScenePlan statics, ActorDiagnosticScenePlan actor)
    {
        var rectangles = terrain.Commands.Select(command => command.Destination)
            .Concat(statics.Commands.Select(command => command.Destination)).ToList();
        try
        {
            foreach (var corner in new[] { new DiagnosticSectorPosition(0, 0), new(0, 63), new(63, 0), new(63, 63) })
            foreach (var action in new[] { ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk })
            foreach (var facing in Enumerable.Range(0, 8))
            foreach (var frame in Enumerable.Range(0, actor.Profile.Resource(action).FramesPerGroup))
            {
                var selected = actor.Select(DiagnosticActorMotion.ToPlacement(new(corner, facing, action), frame)).Current;
                var left = Math.Min(selected.Destination.X, checked((int)selected.AnchorX - 4));
                var top = Math.Min(selected.Destination.Y, checked((int)selected.AnchorY - 4));
                var right = Math.Max(checked(selected.Destination.X + selected.Destination.Width), checked((int)selected.AnchorX + 5));
                var bottom = Math.Max(checked(selected.Destination.Y + selected.Destination.Height), checked((int)selected.AnchorY + 5));
                rectangles.Add(new(checked(left - 1), checked(top - 1), checked(right - left + 2), checked(bottom - top + 2)));
            }
            var x = rectangles.Min(rect => rect.X); var y = rectangles.Min(rect => rect.Y);
            var endX = rectangles.Max(rect => checked(rect.X + rect.Width));
            var endY = rectangles.Max(rect => checked(rect.Y + rect.Height));
            return new(x, y, checked(endX - x), checked(endY - y));
        }
        catch (OverflowException)
        {
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorPlan, Stage: TerrainViewerFailureStage.ActorPlan));
        }
    }
}
