using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Godot event adapter. Every joypad keeps separate state so active-device filtering is meaningful.</summary>
public sealed class LabInputAdapter
{
    private sealed class DeviceState { public Vector2 Stick; public readonly HashSet<JoyButton> Buttons = new(); }
    private readonly Dictionary<int, DeviceState> _devices = new();
    private readonly HashSet<Key> _keys = new();
    private Vector2 _mouse;


    public Vector2 LastMousePosition => _mouse;
    public RawInputFrame Map(InputEvent input, bool focused)
    {
        if (input is InputEventJoypadMotion motion)
        {
            var state = State(motion.Device);
            if (motion.Axis == JoyAxis.LeftX) state.Stick.X = motion.AxisValue;
            if (motion.Axis == JoyAxis.LeftY) state.Stick.Y = motion.AxisValue;
            return FromDevice(motion.Device, focused);
        }
        if (input is InputEventJoypadButton joy)
        {
            var state = State(joy.Device);
            if (joy.Pressed) state.Buttons.Add(joy.ButtonIndex); else state.Buttons.Remove(joy.ButtonIndex);
            return FromDevice(joy.Device, focused);
        }
        if (input is InputEventKey key)
        {
            if (key.Pressed) _keys.Add(key.Keycode); else _keys.Remove(key.Keycode);
            return KeyboardFrame(focused, key.Pressed && !key.Echo, key.Echo);
        }
        if (input is InputEventMouseMotion mouseMotion)
        {
            // Relative is already logical travel and avoids treating the first absolute
            // pointer report as a giant method-switching movement.
            _mouse = mouseMotion.Position;
            return KeyboardFrame(focused, false, false) with { MouseLogicalDistance = mouseMotion.Relative.Length() };
        }
        if (input is InputEventMouseButton mouse)
        {
            _mouse = mouse.Position;
            return KeyboardFrame(focused, false, false) with { MouseButton = mouse.Pressed, Confirm = mouse.Pressed && mouse.ButtonIndex == MouseButton.Left, Cancel = mouse.Pressed && mouse.ButtonIndex == MouseButton.Right };
        }
        return KeyboardFrame(focused, false, false);
    }

    /// <summary>Samples only the selected controller. Keyboard is deliberately
    /// separate so its held levels never acquire controller provenance.</summary>
    public RawInputFrame? SampleController(int? activeDevice, bool focused)
    {
        return activeDevice is int device && _devices.ContainsKey(device) ? FromDevice(device, focused) : null;
    }
    // Held keyboard levels are sampled for pan/menu routing, but they are not
    // fresh keyboard input-method events. Only Map(InputEventKey) marks one.
    public RawInputFrame SampleKeyboard(bool focused) => KeyboardFrame(focused, false, false);
    public void ForgetDevice(int device) => _devices.Remove(device);
    public void ClearKeyboard() => _keys.Clear();
    public void Clear() { _keys.Clear(); _devices.Clear(); }

    private DeviceState State(int device) => _devices.TryGetValue(device, out var state) ? state : _devices[device] = new DeviceState();
    private RawInputFrame FromDevice(int device, bool focused)
    {
        var state = State(device); bool Has(JoyButton button) => state.Buttons.Contains(button);
        return new RawInputFrame(device, ToCore(state.Stick),
            Confirm: Has(JoyButton.A), Cancel: Has(JoyButton.B), Inspect: Has(JoyButton.X), Context: Has(JoyButton.Y),
            Previous: Has(JoyButton.LeftShoulder), Next: Has(JoyButton.RightShoulder), Settings: Has(JoyButton.Start),
            MenuUp: Has(JoyButton.DpadUp), MenuDown: Has(JoyButton.DpadDown), Focused: focused);
    }
    private RawInputFrame KeyboardFrame(bool focused, bool keyboardEvent, bool echo)
    {
        bool Has(Key key) => _keys.Contains(key);
        var pan = new Vec2((Has(Key.D) || Has(Key.Right) ? 1 : 0) - (Has(Key.A) || Has(Key.Left) ? 1 : 0), (Has(Key.S) || Has(Key.Down) ? 1 : 0) - (Has(Key.W) || Has(Key.Up) ? 1 : 0));
        return new RawInputFrame(-1, default,
            Confirm: Has(Key.Enter) || Has(Key.Space), Cancel: Has(Key.Escape) || Has(Key.Backspace), Inspect: Has(Key.I),
            Previous: Has(Key.Q), Next: Has(Key.E), Context: Has(Key.C), Settings: Has(Key.F1),
            MenuUp: Has(Key.Up) || Has(Key.W), MenuDown: Has(Key.Down) || Has(Key.S), Focused: focused,
            Keyboard: keyboardEvent, Echo: echo, KeyboardPan: pan);
    }
    public static void EnsureInputMap()
    {
        foreach (var action in new[] { "lab_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" })
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        Add("lab_pan", new InputEventKey { Keycode = Key.W }, new InputEventKey { Keycode = Key.A }, new InputEventKey { Keycode = Key.S }, new InputEventKey { Keycode = Key.D }, new InputEventKey { Keycode = Key.Up }, new InputEventKey { Keycode = Key.Down }, new InputEventKey { Keycode = Key.Left }, new InputEventKey { Keycode = Key.Right }, new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1 }, new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1 }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 });
        Add("lab_confirm", new InputEventKey { Keycode = Key.Enter }, new InputEventKey { Keycode = Key.Space }, new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Add("lab_cancel", new InputEventKey { Keycode = Key.Escape }, new InputEventKey { Keycode = Key.Backspace }, new InputEventJoypadButton { ButtonIndex = JoyButton.B });
        Add("lab_inspect", new InputEventKey { Keycode = Key.I }, new InputEventJoypadButton { ButtonIndex = JoyButton.X });
        Add("lab_previous", new InputEventKey { Keycode = Key.Q }, new InputEventJoypadButton { ButtonIndex = JoyButton.LeftShoulder });
        Add("lab_next", new InputEventKey { Keycode = Key.E }, new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder });
        Add("lab_context", new InputEventKey { Keycode = Key.C }, new InputEventJoypadButton { ButtonIndex = JoyButton.Y });
        Add("lab_settings", new InputEventKey { Keycode = Key.F1 }, new InputEventJoypadButton { ButtonIndex = JoyButton.Start });
        Add("lab_menu_up", new InputEventKey { Keycode = Key.Up }, new InputEventKey { Keycode = Key.W }, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadUp }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 });
        Add("lab_menu_down", new InputEventKey { Keycode = Key.Down }, new InputEventKey { Keycode = Key.S }, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown }, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 });
    }
    private static void Add(string action, params InputEvent[] events) { if (InputMap.ActionGetEvents(action).Count == 0) foreach (var inputEvent in events) InputMap.ActionAddEvent(action, inputEvent); }
    private static Vec2 ToCore(Vector2 value) => new(value.X, value.Y);
}