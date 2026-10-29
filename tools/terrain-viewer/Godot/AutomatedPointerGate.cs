using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-060: keeps the system pointer out of an automated run. Its tests push pointer events into the viewport; the
/// system's own pointer events reached the same GUI through the window, and the key window receives the pointer's moves anywhere
/// on the screen. A move of the owner's pointer, or the motion Godot replays at the system pointer on every cursor shape change,
/// took the GUI's hover off the control a test had just entered (the hotkey bar's hint and the bar smoke's buttons failed this
/// way). The node takes the window's input callback and passes every event as the window would (<c>Window::_window_input</c>:
/// the <c>window_input</c> signal for all but internal events, then the window's input) except pointer events: a mouse or
/// gesture event passes only when a test parsed it with <see cref="TestDevice"/>. The system pointer crossing the window's
/// edge still reaches the window as a window event (not filtered). It is added only by the process hosting an automated run
/// (a self-test, a native smoke, the WORLD settings validation) and never in a headless run or an interactive viewer.</summary>
internal sealed partial class AutomatedPointerGate : Node
{
    /// <summary>The device of a pointer event a test parses through <c>Input.ParseInputEvent</c> (system mouse events carry
    /// <see cref="InputEvent.DeviceIdMouse"/>); no handler of the viewer reads a pointer event's device. Buffered motions merge
    /// without comparing devices, so a parsed test motion must not directly follow a system one (the terrain smoke's follows a
    /// button event).</summary>
    internal const int TestDevice = 48;
    /// <summary><c>InputEvent::DEVICE_ID_INTERNAL</c> (not bound in C#): the window sends no <c>window_input</c> signal for it.</summary>
    private const int InternalDevice = -2;

    /// <summary>System pointer events kept from the window since the gate opened.</summary>
    internal static long Dropped { get; private set; }

    private Window? _window;

    public AutomatedPointerGate() => Name = "AutomatedPointerGate";

    // The callback is not given back: the node lives as long as the process.
    public override void _EnterTree()
    {
        _window = GetWindow();
        DisplayServer.WindowSetInputEventCallback(Callable.From<InputEvent>(Pass), _window.GetWindowId());
        GD.Print($"ARC-DEV-060 automated pointer gate window={_window.GetWindowId()}");
    }

    private void Pass(InputEvent input)
    {
        if (input is InputEventMouse or InputEventGesture && input.Device != TestDevice) { Dropped++; return; }
        if (_window is not { } window || !window.IsInsideTree()) return;
        if (input.Device != InternalDevice) window.EmitSignal(Window.SignalName.WindowInput, input);
        window.PushInput(input);
    }
}
