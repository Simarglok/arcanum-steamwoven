using Godot;
using System.Collections.Generic;

namespace Arcanum.Experiments.DisplayUiLab;

public enum UiScalePreset
{
    Scale100 = 100,
    Scale125 = 125,
    Scale150 = 150,
    Scale175 = 175,
    Scale200 = 200,
}

public enum LabPanel { Dialogue, Inventory, Settings }
public enum ResponsiveClass { Wide, Medium, Compact }

public sealed record DisplayState(UiScalePreset UiScale, float CameraZoom, LabPanel ActivePanel, bool DiagnosticsVisible, bool PanelVisible = true);
public sealed record LayoutSnapshot(
    ResponsiveClass ResponsiveClass,
    Vector2 Viewport,
    DisplayState State,
    IReadOnlyDictionary<string, Rect2> Rects,
    Vector2 WorldMarkerViewport);

internal sealed record MatrixCase(Vector2 Viewport, UiScalePreset Scale, LabPanel Panel, bool Smoke = false);
internal sealed record MatrixResult(string Name, bool Passed, string Detail);
internal sealed record MatrixCaseResult(Vector2I Viewport, int UiScale, string Panel, bool Smoke, bool Passed, IReadOnlyList<MatrixResult> Assertions);
internal sealed record MatrixTransitionResult(bool Passed, IReadOnlyList<MatrixResult> Assertions, object Evidence);
