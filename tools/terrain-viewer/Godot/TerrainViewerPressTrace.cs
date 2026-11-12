using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-INPUT-1400: the log of every mouse button press the world sees (time, frame, device, position, event identity, gap to the
// previous press), so a report (Ctrl+B) shows whether one physical press arrives as several events (the platform) or as one event
// handled twice (the viewer). It only writes a line; no press is changed, delayed or dropped. A press of an automated run (a smoke
// or a self-test pushes its own) is not logged.
public partial class TerrainViewerApp
{
    private readonly MousePressTrace _pressTrace = new();

    private void TraceMousePress(InputEvent input, MousePressTrace.Stage stage)
    {
        if (AutomatedVisualRun || input is not InputEventMouseButton { Pressed: true } press) return;
        GD.Print(_pressTrace.Note(new MousePressTrace.Press(stage, press.ButtonIndex.ToString(), (long)Time.GetTicksUsec(), (long)Engine.GetProcessFrames(), press.Device,
            press.Position.X, press.Position.Y, press.GetInstanceId(), press.DoubleClick, (int)press.ButtonMask)));
    }
}
