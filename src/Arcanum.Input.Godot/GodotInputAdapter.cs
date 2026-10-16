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
    /// <summary>WORLD route (docs/input/diagnostic-palette.md): keyboard never makes an
    /// actor vector; arrows pan; only the open palette receives keyboard UI pulses.
    /// Off (default) keeps the historical Gamepad Lab keyboard scaffolding.</summary>
    public bool WorldRoute { get; set; }
    /// <summary>The diagnostic palette is the current modal surface (WORLD route only).</summary>
    public bool PaletteOpen { get; set; }
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
            // A key without a WORLD role is not a keyboard input-method event either.
            var routed = !WorldRoute || WorldRole(key.Keycode) is WorldKeyRole.CameraPan or WorldKeyRole.PaletteControl;
            return KeyboardFrame(focused, routed && key.Pressed && !key.Echo, routed && key.Echo);
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
    private WorldKeyRole WorldRole(Key key) => global::Arcanum.Input.OriginalWorldKeyboard.Role(ToWorldKey(key), PaletteOpen);
    private RawInputFrame KeyboardFrame(bool focused, bool keyboardEvent, bool echo)
    {
        bool Has(Key key) => _keys.Contains(key);
        if (WorldRoute)
        {
            bool Pan(Key key) => Has(key) && WorldRole(key) == WorldKeyRole.CameraPan;
            bool Ui(Key key) => Has(key) && WorldRole(key) == WorldKeyRole.PaletteControl;
            var pan = new Vec2((Pan(Key.Right) ? 1 : 0) - (Pan(Key.Left) ? 1 : 0), (Pan(Key.Down) ? 1 : 0) - (Pan(Key.Up) ? 1 : 0));
            return new RawInputFrame(-1, default,
                Confirm: Ui(Key.Enter) || Ui(Key.Space), Cancel: Ui(Key.Escape) || Ui(Key.Backspace),
                MenuUp: Ui(Key.Up), MenuDown: Ui(Key.Down), Focused: focused,
                Keyboard: keyboardEvent, Echo: echo, KeyboardCamera: pan)
            {
                KeyboardCameraHeld = Pan(Key.Up) || Pan(Key.Down) || Pan(Key.Left) || Pan(Key.Right),
                CompleteObservation = _keyboardComplete
            };
        }
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
    /// <summary>Godot logical key to the engine-free WORLD key identity.</summary>
    public static WorldKey ToWorldKey(Key key) => key switch
    {
        Key.Up => WorldKey.Up, Key.Down => WorldKey.Down, Key.Left => WorldKey.Left, Key.Right => WorldKey.Right,
        Key.Home => WorldKey.Home, Key.Enter or Key.KpEnter => WorldKey.Enter, Key.Space => WorldKey.Space,
        Key.Escape => WorldKey.Escape, Key.Backspace => WorldKey.Backspace, Key.Tab => WorldKey.Tab, Key.Numlock => WorldKey.NumLock,
        >= Key.F1 and <= Key.F12 => WorldKey.F1 + (int)(key - Key.F1),
        >= Key.Key0 and <= Key.Key9 => WorldKey.Digit0 + (int)(key - Key.Key0),
        >= Key.A and <= Key.Z => WorldKey.A + (int)(key - Key.A),
        Key.Comma => WorldKey.Comma, Key.Period => WorldKey.Period, Key.Slash => WorldKey.Slash,
        _ => WorldKey.Other,
    };
    private static Vec2 ToCore(Vector2 value) => new(value.X, value.Y);
}