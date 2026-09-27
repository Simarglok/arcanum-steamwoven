using Godot;
using System;
using System.Collections.Generic;

namespace Arcanum.Experiments.DisplayUiLab;

internal static class LayoutAssertions
{
    internal static IEnumerable<MatrixResult> Verify(LayoutSnapshot snapshot, Vector2 expectedViewport, UiScalePreset expectedScale, LabPanel expectedPanel)
    {
        yield return new MatrixResult("responsive-class", snapshot.ResponsiveClass == UiScalePolicy.Classify(expectedViewport, expectedScale), snapshot.ResponsiveClass.ToString());
        yield return new MatrixResult("state", snapshot.State.UiScale == expectedScale && snapshot.State.ActivePanel == expectedPanel, snapshot.State.ToString());
        foreach (var (name, rect) in snapshot.Rects)
        {
            var finite = float.IsFinite(rect.Position.X) && float.IsFinite(rect.Position.Y) && float.IsFinite(rect.Size.X) && float.IsFinite(rect.Size.Y);
            yield return new MatrixResult($"finite-{name}", finite && rect.Size.X >= 0 && rect.Size.Y >= 0, rect.ToString());
            if (name is "Close" or "Reset" or "Fullscreen")
            {
                var visible = rect.Position.X >= -0.5f && rect.Position.Y >= -0.5f && rect.End.X <= expectedViewport.X + 0.5f && rect.End.Y <= expectedViewport.Y + 0.5f;
                yield return new MatrixResult($"sticky-{name}", visible, rect.ToString());
            }
        }
        var hud = snapshot.Rects["hud"];
        var actions = snapshot.Rects["actions"];
        var panel = snapshot.Rects["panel"];
        yield return new MatrixResult("hud-action-nonoverlap", !hud.Intersects(actions), $"{hud} / {actions}");
        yield return new MatrixResult("panel-action-nonoverlap", !panel.Intersects(actions), $"{panel} / {actions}");
    }

    internal static bool Near(LayoutSnapshot left, LayoutSnapshot right, float tolerance = 0.5f)
    {
        foreach (var key in left.Rects.Keys)
        {
            if (!right.Rects.TryGetValue(key, out var other) || left.Rects[key].Position.DistanceTo(other.Position) > tolerance || left.Rects[key].Size.DistanceTo(other.Size) > tolerance)
                return false;
        }
        return true;
    }
}
