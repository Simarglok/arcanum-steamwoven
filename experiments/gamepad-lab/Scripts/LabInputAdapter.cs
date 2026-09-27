using GamepadLab.Core;
using Godot;

namespace GamepadLab;
public sealed class LabInputAdapter
{
    private Vector2 _stick;
    public RawInputFrame Map(InputEvent input, bool focused)
    {
        var raw = new RawInputFrame(-1, ToCore(_stick), Focused: focused);
        if (input is InputEventJoypadMotion motion)
        {
            if (motion.Axis == JoyAxis.LeftX) _stick.X = motion.AxisValue;
            if (motion.Axis == JoyAxis.LeftY) _stick.Y = motion.AxisValue;
            return raw with { DeviceId = motion.Device, Stick = ToCore(_stick) };
        }
        if (input is InputEventJoypadButton joy) return raw with { DeviceId = joy.Device, Confirm = joy.Pressed && joy.ButtonIndex == JoyButton.A, Cancel = joy.Pressed && joy.ButtonIndex == JoyButton.B, Inspect = joy.Pressed && joy.ButtonIndex == JoyButton.X, Context = joy.Pressed && joy.ButtonIndex == JoyButton.Y, Previous = joy.Pressed && joy.ButtonIndex == JoyButton.LeftShoulder, Next = joy.Pressed && joy.ButtonIndex == JoyButton.RightShoulder, Settings = joy.Pressed && joy.ButtonIndex == JoyButton.Start, MenuUp = joy.Pressed && joy.ButtonIndex == JoyButton.DpadUp, MenuDown = joy.Pressed && joy.ButtonIndex == JoyButton.DpadDown };
        if (input is InputEventKey key)
        {
            var pressed = key.Pressed && !key.Echo;
            return raw with { Keyboard = pressed, Echo = key.Echo, Confirm = pressed && (key.Keycode == Key.Enter || key.Keycode == Key.Space), Cancel = pressed && (key.Keycode == Key.Escape || key.Keycode == Key.Backspace), Inspect = pressed && key.Keycode == Key.I, Previous = pressed && key.Keycode == Key.Q, Next = pressed && key.Keycode == Key.E, Context = pressed && key.Keycode == Key.C, Settings = pressed && key.Keycode == Key.F1, MenuUp = pressed && (key.Keycode == Key.Up || key.Keycode == Key.W), MenuDown = pressed && (key.Keycode == Key.Down || key.Keycode == Key.S) };
        }
        if (input is InputEventMouseButton mouse) return raw with { MouseButton = mouse.Pressed, Confirm = mouse.Pressed && mouse.ButtonIndex == MouseButton.Left, Cancel = mouse.Pressed && mouse.ButtonIndex == MouseButton.Right };
        return raw;
    }
    public static void EnsureInputMap()
    {
        foreach (var action in new[] { "lab_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" }) if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }
    private static Vec2 ToCore(Vector2 value) => new(value.X, value.Y);
}
