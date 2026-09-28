using Godot;
using System.Collections.Generic;
using System.Linq;

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

// JSON evidence uses only primitives. Godot structs serialize as opaque objects under
// System.Text.Json, which would make a passing report non-evidentiary.
internal sealed record SizeEvidence(float Width, float Height)
{
    internal static SizeEvidence From(Vector2 value) => new(value.X, value.Y);
}

internal sealed record PointEvidence(float X, float Y)
{
    internal static PointEvidence From(Vector2 value) => new(value.X, value.Y);
}

internal sealed record RectEvidence(float X, float Y, float Width, float Height)
{
    internal static RectEvidence From(Rect2 value) => new(value.Position.X, value.Position.Y, value.Size.X, value.Size.Y);
}

internal sealed record StateEvidence(int UiScale, float CameraZoom, string ActivePanel, bool DiagnosticsVisible, bool PanelVisible)
{
    internal static StateEvidence From(DisplayState value) => new((int)value.UiScale, value.CameraZoom, value.ActivePanel.ToString(), value.DiagnosticsVisible, value.PanelVisible);
}

internal sealed record SnapshotEvidence(SizeEvidence Viewport, string ResponsiveClass, StateEvidence State, PointEvidence CameraPosition, PointEvidence WorldMarker, IReadOnlyDictionary<string, RectEvidence> Rects)
{
    internal static SnapshotEvidence From(LayoutSnapshot snapshot, Vector2 cameraPosition) => new(
        SizeEvidence.From(snapshot.Viewport), snapshot.ResponsiveClass.ToString(), StateEvidence.From(snapshot.State),
        PointEvidence.From(cameraPosition), PointEvidence.From(snapshot.WorldMarkerViewport),
        snapshot.Rects.ToDictionary(pair => pair.Key, pair => RectEvidence.From(pair.Value)));
}

internal sealed record MatrixCase(Vector2 Viewport, UiScalePreset Scale, LabPanel Panel, bool Smoke = false);
internal sealed record MatrixResult(string Name, bool Passed, string Detail);
internal sealed record MatrixCaseResult(SizeEvidence Viewport, int UiScale, string Panel, bool Smoke, bool Passed, IReadOnlyList<MatrixResult> Assertions);
internal sealed record MatrixTransitionResult(bool Passed, IReadOnlyList<MatrixResult> Assertions, object Evidence);
