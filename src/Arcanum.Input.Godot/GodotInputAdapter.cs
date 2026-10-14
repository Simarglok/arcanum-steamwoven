using GamepadLab.Core;
using Godot;

namespace Arcanum.Input.Godot;

/// <summary>Godot event adapter. Every joypad keeps separate state so active-device filtering is meaningful.</summary>
public sealed class GodotInputAdapter
{
    private sealed class DeviceState { public Vector2 LeftStick; public Vector2 RightStick; public float LeftTrigger; public float RightTrigger; public bool Complete; public readonly HashSet<JoyButton> Buttons = new(); }
    private readonly Dictionary<int, DeviceState> _devices = new();
    private readonly HashSet<Key> _keys = new();
    private Vector2 _mouse;
    private bool _keyboardComplete;


    public Vector2 LastMousePosition => _mouse;
    public RawInputFrame Map(InputEvent input, bool focused)
    {
        if (input is InputEventJoypadMotion motion)
        {
            var state = State(motion.Device);
            state.Complete = false;
            if (motion.Axis == JoyAxis.LeftX) state.LeftStick.X = motion.AxisValue;
            if (motion.Axis == JoyAxis.LeftY) state.LeftStick.Y = motion.AxisValue;
            if (motion.Axis == JoyAxis.RightX) state.RightStick.X = motion.AxisValue;
            if (motion.Axis == JoyAxis.RightY) state.RightStick.Y = motion.AxisValue;
            if (motion.Axis == JoyAxis.TriggerLeft) state.LeftTrigger = motion.AxisValue;
            if (motion.Axis == JoyAxis.TriggerRight) state.RightTrigger = motion.AxisValue;
            return FromDevice(motion.Device, focused);
        }
        if (input is InputEventJoypadButton joy)
        {
            var state = State(joy.Device);
            state.Complete = false;
            if (joy.Pressed) state.Buttons.Add(joy.ButtonIndex); else state.Buttons.Remove(joy.ButtonIndex);
            return FromDevice(joy.Device, focused);
        }
        if (input is InputEventKey key)
        {
            _keyboardComplete = false;
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
    public RawInputFrame? SampleController(int? activeDevice, bool focused, long observationFrameId = -1)
    {
        return activeDevice is int device && _devices.ContainsKey(device) ? FromDevice(device, focused) with { FrameObservationId = observationFrameId } : null;
    }
    // Held keyboard levels are sampled for pan/menu routing, but they are not
    // fresh keyboard input-method events. Only Map(InputEventKey) marks one.
    public RawInputFrame SampleKeyboard(bool focused, long observationFrameId = -1) => KeyboardFrame(focused, false, false) with { FrameObservationId = observationFrameId };
    public bool IsKeyHeld(Key key) => _keys.Contains(key);
    public void ForgetDevice(int device) => _devices.Remove(device);
    public void ClearKeyboard() { _keys.Clear(); _keyboardComplete = false; }
    public void Clear() { ClearKeyboard(); _devices.Clear(); }

    private DeviceState State(int device) => _devices.TryGetValue(device, out var state) ? state : _devices[device] = new DeviceState();
    private RawInputFrame FromDevice(int device, bool focused)
    {
        var state = State(device); bool Has(JoyButton button) => state.Buttons.Contains(button);
        return new RawInputFrame(device, ToCore(state.LeftStick), ToCore(state.RightStick),
            Confirm: Has(JoyButton.A), Cancel: Has(JoyButton.B), Inspect: Has(JoyButton.X), Context: Has(JoyButton.Y),
            Previous: Has(JoyButton.LeftShoulder), Next: Has(JoyButton.RightShoulder), Settings: Has(JoyButton.Start),
            MenuUp: Has(JoyButton.DpadUp), MenuDown: Has(JoyButton.DpadDown), Focused: focused)
        {
            LeftTrigger = state.LeftTrigger, RightTrigger = state.RightTrigger, CompleteObservation = state.Complete,
            ControllerButtons = PhysicalButtons.Aggregate(global::Arcanum.Input.ControllerButtons.None,
                (flags, pair) => Has(pair.Button) ? flags | pair.Flag : flags)
        };
    }
    private RawInputFrame KeyboardFrame(bool focused, bool keyboardEvent, bool echo)
    {
        bool Has(Key key) => _keys.Contains(key);
        var actor = new Vec2((Has(Key.D) ? 1 : 0) - (Has(Key.A) ? 1 : 0), (Has(Key.S) ? 1 : 0) - (Has(Key.W) ? 1 : 0));
        var camera = new Vec2((Has(Key.Right) ? 1 : 0) - (Has(Key.Left) ? 1 : 0), (Has(Key.Down) ? 1 : 0) - (Has(Key.Up) ? 1 : 0));
        return new RawInputFrame(-1, default,
            Confirm: Has(Key.Enter) || Has(Key.Space), Cancel: Has(Key.Escape) || Has(Key.Backspace), Inspect: Has(Key.I),
            Previous: Has(Key.Q), Next: Has(Key.E), Context: Has(Key.C), Settings: Has(Key.F1),
            MenuUp: Has(Key.Up) || Has(Key.W), MenuDown: Has(Key.Down) || Has(Key.S), Focused: focused,
            Keyboard: keyboardEvent, Echo: echo, KeyboardActor: actor, KeyboardCamera: camera)
        {
            KeyboardActorHeld = Has(Key.W) || Has(Key.A) || Has(Key.S) || Has(Key.D),
            KeyboardCameraHeld = Has(Key.Up) || Has(Key.Down) || Has(Key.Left) || Has(Key.Right)
            , CompleteObservation = _keyboardComplete
        };
    }
    private static readonly Key[] MappedKeys = { Key.W, Key.A, Key.S, Key.D, Key.Up, Key.Down, Key.Left, Key.Right,
        Key.Enter, Key.Space, Key.Escape, Key.Backspace, Key.I, Key.Q, Key.E, Key.C, Key.F1 };
    private static readonly JoyButton[] MappedButtons = { JoyButton.A, JoyButton.B, JoyButton.X, JoyButton.Y,
        JoyButton.LeftShoulder, JoyButton.RightShoulder, JoyButton.Start, JoyButton.DpadUp, JoyButton.DpadDown };
    private static readonly (JoyButton Button, global::Arcanum.Input.ControllerButtons Flag)[] PhysicalButtons = {
        (JoyButton.A, global::Arcanum.Input.ControllerButtons.A), (JoyButton.B, global::Arcanum.Input.ControllerButtons.B),
        (JoyButton.X, global::Arcanum.Input.ControllerButtons.X), (JoyButton.Y, global::Arcanum.Input.ControllerButtons.Y),
        (JoyButton.LeftShoulder, global::Arcanum.Input.ControllerButtons.LB), (JoyButton.RightShoulder, global::Arcanum.Input.ControllerButtons.RB),
        (JoyButton.DpadUp, global::Arcanum.Input.ControllerButtons.DpadUp), (JoyButton.DpadDown, global::Arcanum.Input.ControllerButtons.DpadDown),
        (JoyButton.DpadLeft, global::Arcanum.Input.ControllerButtons.DpadLeft), (JoyButton.DpadRight, global::Arcanum.Input.ControllerButtons.DpadRight),
        (JoyButton.LeftStick, global::Arcanum.Input.ControllerButtons.L3), (JoyButton.RightStick, global::Arcanum.Input.ControllerButtons.R3),
        (JoyButton.Back, global::Arcanum.Input.ControllerButtons.View), (JoyButton.Start, global::Arcanum.Input.ControllerButtons.Menu) };

    /// <summary>Complete frame observation, not a fresh method/provenance event.</summary>
    public void RefreshKeyboardLevels(Func<Key, bool> readKey)
    {
        var held = MappedKeys.Where(readKey).ToArray();
        _keys.Clear(); foreach (var key in held) _keys.Add(key);
        _keyboardComplete = true;
    }
    public bool RefreshControllerLevels(int device, Func<JoyAxis, float> readAxis, Func<JoyButton, bool> readButton, bool worldZoom = false, bool explicitBindings = false)
    {
        // Failed polling invalidates old provenance, but never partially replaces levels.
        if (_devices.TryGetValue(device, out var old)) old.Complete = false;
        var left = new Vector2(readAxis(JoyAxis.LeftX), readAxis(JoyAxis.LeftY));
        var right = new Vector2(readAxis(JoyAxis.RightX), readAxis(JoyAxis.RightY));
        var lt = worldZoom ? readAxis(JoyAxis.TriggerLeft) : 0;
        var rt = worldZoom ? readAxis(JoyAxis.TriggerRight) : 0;
        if (!InputStateReducer.ValidTrigger(lt) || !InputStateReducer.ValidTrigger(rt)) return false;
        if (!float.IsFinite(left.X) || !float.IsFinite(left.Y) || !float.IsFinite(right.X) || !float.IsFinite(right.Y)) return false;
        var buttons = (explicitBindings ? PhysicalButtons.Select(p => p.Button) : MappedButtons).Where(readButton).ToArray();
        var replacement = new DeviceState { LeftStick = left, RightStick = right, LeftTrigger = lt, RightTrigger = rt, Complete = explicitBindings };
        foreach (var button in buttons) replacement.Buttons.Add(button);
        _devices[device] = replacement;
        return true;
    }
    private static Vec2 ToCore(Vector2 value) => new(value.X, value.Y);
}