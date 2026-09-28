using Godot;
using System;
using System.Collections.Generic;

namespace Arcanum.Experiments.DisplayUiLab;

internal static class LayoutAssertions
{
    internal static IEnumerable<MatrixResult> Verify(LabSurface surface, LayoutSnapshot snapshot, Vector2 expectedViewport, UiScalePreset expectedScale, LabPanel expectedPanel)
    {
        yield return new MatrixResult("responsive-class", snapshot.ResponsiveClass == UiScalePolicy.Classify(expectedViewport, expectedScale), snapshot.ResponsiveClass.ToString());
        yield return new MatrixResult("state", snapshot.State.UiScale == expectedScale && snapshot.State.ActivePanel == expectedPanel, snapshot.State.ToString());
        foreach (var (name, rect) in snapshot.Rects)
        {
            var finite = float.IsFinite(rect.Position.X) && float.IsFinite(rect.Position.Y) && float.IsFinite(rect.Size.X) && float.IsFinite(rect.Size.Y);
            yield return new MatrixResult($"finite-{name}", finite && rect.Size.X >= 0 && rect.Size.Y >= 0, rect.ToString());
            if (name is "PanelTitle" or "Close" or "Reset" or "Fullscreen" or "PanelSelector")
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
        var required = expectedPanel switch
        {
            LabPanel.Dialogue => new[] { "ResponseOne", "ResponseTwo", "ResponseThree" },
            LabPanel.Inventory => new[] { "Item01", "Item12" },
            _ => new[] { "Scale100", "Scale200", "Zoom1.50", "Diagnostics" },
        };
        foreach (var name in required)
        {
            var control = surface.ImportantControl(name);
            var accessible = control != null && (control.IsVisibleInTree() || surface.IsInsideIntentionalScroll(control));
            yield return new MatrixResult($"accessible-{name}", accessible, control?.GetGlobalRect().ToString() ?? "missing");
        }
        var longText = expectedPanel == LabPanel.Dialogue ? "Dialogue" : expectedPanel == LabPanel.Inventory ? "Item detail" : "Settings";
        var metrics = surface.TextMetrics(longText);
        yield return new MatrixResult("wrapped-multiline", metrics.wrapped && metrics.lineCount > 1, $"wrapped={metrics.wrapped}; lines={metrics.lineCount}");
        yield return new MatrixResult("no-visible-line-clipping", metrics.lineCount > 0 && metrics.visibleLineCount >= metrics.lineCount, $"visible={metrics.visibleLineCount}; lines={metrics.lineCount}");
        yield return new MatrixResult("configured-contrast", UiScalePolicy.ConfiguredContrastPairsPass(), "foreground/background pairs >= 4.5:1 by configured sRGB values");
        yield return new MatrixResult("fallback-cyrillic", surface.FallbackFontHasCyrillic(), "Godot fallback font has С and я glyphs");
    }

    internal static bool Near(LayoutSnapshot left, LayoutSnapshot right, float tolerance = 0.5f)
        => NearDifference(left, right, tolerance) == null;

    internal static string? NearDifference(LayoutSnapshot left, LayoutSnapshot right, float tolerance = 0.5f)
    {
        var stableRects = new[] { "hud", "actions", "panel", "Fullscreen", "PanelTitle", "Close", "Reset", "PanelSelector" };
        foreach (var key in stableRects)
        {
            if (!left.Rects.TryGetValue(key, out var current) || !right.Rects.TryGetValue(key, out var other))
                return $"{key}: missing from a snapshot";
            if (current.Position.DistanceTo(other.Position) > tolerance || current.Size.DistanceTo(other.Size) > tolerance)
                return $"{key}: {current} / {other}";
        }
        return null;
    }
}
