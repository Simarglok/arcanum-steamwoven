using Arcanum.Input.Godot;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Compatibility facade; input policy and sampling have one shared owner.</summary>
public sealed class LabInputAdapter
{
    private readonly GodotInputAdapter _shared = new();
    public Vector2 LastMousePosition => _shared.LastMousePosition;
    public RawInputFrame Map(InputEvent input, bool focused) => _shared.Map(input, focused);
    public RawInputFrame? SampleController(int? activeDevice, bool focused) => _shared.SampleController(activeDevice, focused);
    public RawInputFrame SampleKeyboard(bool focused) => _shared.SampleKeyboard(focused);
    public void ForgetDevice(int device) => _shared.ForgetDevice(device);
    public void ClearKeyboard() => _shared.ClearKeyboard();
    public void Clear() => _shared.Clear();
    public bool IsKeyHeld(Key key) => _shared.IsKeyHeld(key);
    public RawInputFrame? SampleController(int? device, bool focused, long frame) => _shared.SampleController(device, focused, frame);
    public RawInputFrame SampleKeyboard(bool focused, long frame) => _shared.SampleKeyboard(focused, frame);
    public void RefreshKeyboardLevels(Func<Key, bool> read) => _shared.RefreshKeyboardLevels(read);
    public bool RefreshControllerLevels(int device, Func<JoyAxis, float> axes, Func<JoyButton, bool> buttons) => _shared.RefreshControllerLevels(device, axes, buttons, worldZoom: true, explicitBindings: true);
    public static void EnsureInputMap()
    {
        foreach (var action in new[] { "lab_actor_move", "lab_camera_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" })
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        Add("lab_actor_move", new InputEventKey { Keycode = Key.W }, new InputEventKey { Keycode = Key.A }, new InputEventKey { Keycode = Key.S }, new InputEventKey { Keycode = Key.D }, new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1 }, new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1 }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 });
        Add("lab_camera_pan", new InputEventKey { Keycode = Key.Up }, new InputEventKey { Keycode = Key.Left }, new InputEventKey { Keycode = Key.Down }, new InputEventKey { Keycode = Key.Right }, new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = -1 }, new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 1 }, new InputEventJoypadMotion { Axis = JoyAxis.RightY, AxisValue = -1 }, new InputEventJoypadMotion { Axis = JoyAxis.RightY, AxisValue = 1 });
        Add("lab_confirm", new InputEventKey { Keycode = Key.Enter }, new InputEventKey { Keycode = Key.Space }, new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Add("lab_cancel", new InputEventKey { Keycode = Key.Escape }, new InputEventKey { Keycode = Key.Backspace }, new InputEventJoypadButton { ButtonIndex = JoyButton.B });
        Add("lab_inspect", new InputEventKey { Keycode = Key.I }, new InputEventJoypadButton { ButtonIndex = JoyButton.X });
        Add("lab_previous", new InputEventKey { Keycode = Key.Q }, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadLeft });
        Add("lab_next", new InputEventKey { Keycode = Key.E }, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadRight });
        Add("lab_context", new InputEventKey { Keycode = Key.C }, new InputEventJoypadButton { ButtonIndex = JoyButton.Y });
        Add("lab_settings", new InputEventKey { Keycode = Key.F1 }, new InputEventJoypadButton { ButtonIndex = JoyButton.Start });
        Add("lab_menu_up", new InputEventKey { Keycode = Key.Up }, new InputEventKey { Keycode = Key.W }, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadUp }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 });
        Add("lab_menu_down", new InputEventKey { Keycode = Key.Down }, new InputEventKey { Keycode = Key.S }, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 });
    }
    private static void Add(string action, params InputEvent[] events) { if (InputMap.ActionGetEvents(action).Count == 0) foreach (var inputEvent in events) InputMap.ActionAddEvent(action, inputEvent); }
}
