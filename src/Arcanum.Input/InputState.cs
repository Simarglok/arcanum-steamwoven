namespace GamepadLab.Core;

public enum InputMethod { KeyboardMouse, Gamepad }

/// <summary>One sampled device state. Events are adapted into this state; Core never depends on Godot.</summary>
public sealed record RawInputFrame(
    int DeviceId, Vec2 LeftStick, Vec2 RightStick = default,
    bool Confirm = false, bool Cancel = false, bool Inspect = false, bool Previous = false, bool Next = false,
    bool Context = false, bool Settings = false, bool MenuUp = false, bool MenuDown = false,
    bool Focused = true, bool Connected = true, bool Keyboard = false, bool MouseButton = false,
    double MouseLogicalDistance = 0, bool Echo = false, Vec2 KeyboardActor = default, Vec2 KeyboardCamera = default)
{
    public bool KeyboardActorHeld { get; init; }
    public bool KeyboardCameraHeld { get; init; }
    public double LeftTrigger { get; init; }
    public double RightTrigger { get; init; }
    public Arcanum.Input.ControllerButtons ControllerButtons { get; init; }
    // An adapter-cleared state or event callback is not a complete native frame.
    public bool CompleteObservation { get; init; }
    public long FrameObservationId { get; init; } = -1;
}

/// <summary>Frame-owned timing and per-domain policy. The two stick deadzones are never applied twice.</summary>
public sealed record InputContext(
    double CameraDeadzone = .2, double ActorDeadzone = .2,
    double CameraSensitivity = 1, double ActorSensitivity = 1,
    double Delta = 1d / 60, double Zoom = 1, bool InvertCameraX = true, bool InvertCameraY = true,
    bool Modal = false, bool AdvanceTime = false)
{
    public bool WorldZoomEnabled { get; init; }
    public bool ExplicitGamepadBindingsEnabled { get; init; }
    /// <summary>Keyboard arrows keep one fixed pan direction (the false/false
    /// image sign); camera inversion then applies to the right stick only.</summary>
    public bool KeyboardCameraFixedSign { get; init; }
}

public sealed record IntentFrame(
    Vec2 CameraPan, Vec2 Movement, bool Confirm, bool Cancel, bool Inspect, bool Previous, bool Next, bool Context, bool Settings,
    int MenuDirection, double MenuAnalog, InputMethod Method, bool Accepted, string Provenance)
{
    public bool ActorActive { get; init; }
    public bool CameraActive { get; init; }
    // MenuDirection remains a level for the lab's timed repeat policy.
    public int MenuPressedDirection { get; init; }
    public double ZoomDrive { get; init; }
    public Arcanum.Input.ControllerButtons ControllerDown { get; init; }
    public Arcanum.Input.ControllerButtons ControllerPressed { get; init; }
    public Arcanum.Input.ControllerButtons ControllerReleased { get; init; }
    public Vec2 RadialSelection { get; init; }
    public bool GenuineCompleteObservation { get; init; }
    public long FrameObservationId { get; init; } = -1;
    public int SourceDeviceId { get; init; } = -1;
}

public sealed class InputStateReducer
{
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private int? _activeDevice;
    private readonly HashSet<int> _activationCandidates = [];
    private bool _armed = true;
    private bool _neutralSeen;
    private bool _requiresActiveNeutral;
    private double _gamepadMotionSeconds;
    private bool _gamepadStickQualified;
    private double _mouseMotionSeconds;
    private double _mouseLogicalDistance;
    private Arcanum.Input.ControllerButtons _physicalHeld;
    private long _lastNeutralFrame = -1;

    public int? ActiveDevice => _activeDevice;
    public InputMethod Method { get; private set; } = InputMethod.KeyboardMouse;
    public void SetStartupDevices(IEnumerable<int> ids)
    {
        _activeDevice = ids.DefaultIfEmpty(-1).Min() is var id && id >= 0 ? id : null;
        _requiresActiveNeutral = false;
    }
    public void Disconnect(int id) { _activationCandidates.Remove(id); if (_activeDevice == id) { _activeDevice = null; Quarantine(); } }
    public void FocusChanged(bool focused) => Quarantine();
    public void Reset() => Quarantine();
    public void Quarantine()
    {
        _held.Clear(); _armed = false; _neutralSeen = false; _gamepadMotionSeconds = 0; _gamepadStickQualified = false;
        _physicalHeld = default; _lastNeutralFrame = -1;
        _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; _requiresActiveNeutral = _activeDevice is not null;
    }
    public void EndFrame()
    {
        if (_activeDevice is not null || _activationCandidates.Count == 0) return;
        _activeDevice = _activationCandidates.Min(); _activationCandidates.Clear(); Quarantine();
    }

    public IntentFrame Reduce(RawInputFrame raw, InputContext context, string provenance = "LIVE OS EVENT (not physical usability proof)")
    {
        if (!raw.Focused || !raw.Connected || (context.WorldZoomEnabled && (!ValidTrigger(raw.LeftTrigger) || !ValidTrigger(raw.RightTrigger)))) { Quarantine(); return Empty(provenance); }
        if (context.ExplicitGamepadBindingsEnabled && (!Finite(raw.LeftStick) || !Finite(raw.RightStick))) { Quarantine(); return Empty(provenance); }
        var isJoy = raw.DeviceId >= 0;
        if (isJoy && _activeDevice is null)
        {
            if (Meaningful(raw, context)) _activationCandidates.Add(raw.DeviceId);
            return Empty(provenance);
        }
        if (isJoy && raw.DeviceId != _activeDevice) return Empty(provenance);

        var left = Vec2.CircularDeadzone(raw.LeftStick, context.ActorDeadzone);
        var right = Vec2.CircularDeadzone(raw.RightStick, context.CameraDeadzone);
        var freshStickQualification = false;
        if (context.AdvanceTime)
        {
            if (isJoy && (Math.Max(left.Length, right.Length) >= .25 || TriggerHeld(raw, context)))
            {
                _gamepadMotionSeconds += context.Delta;
                if (!_gamepadStickQualified && _gamepadMotionSeconds >= .1) { _gamepadStickQualified = true; freshStickQualification = true; }
            }
            else if (!context.ExplicitGamepadBindingsEnabled || isJoy || _activeDevice is null)
            { _gamepadMotionSeconds = 0; _gamepadStickQualified = false; }
            if (_mouseLogicalDistance > 0)
            {
                _mouseMotionSeconds += context.Delta;
                if (_mouseMotionSeconds > .150) { _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; }
            }
        }
        if (!_armed)
        {
            if (_requiresActiveNeutral && (!isJoy || raw.DeviceId != _activeDevice)) return Empty(provenance);
            if (context.ExplicitGamepadBindingsEnabled)
            {
                if (!raw.CompleteObservation || !context.AdvanceTime || raw.FrameObservationId < 0) return Empty(provenance);
                if (raw.FrameObservationId <= _lastNeutralFrame) return Empty(provenance);
                // Skipped/incomplete frames do not establish consecutive neutrality.
                if (_lastNeutralFrame >= 0 && raw.FrameObservationId != _lastNeutralFrame + 1) _neutralSeen = false;
                _lastNeutralFrame = raw.FrameObservationId;
            }
            if (!AnyLevel(raw, left, right, context))
            {
                if (_neutralSeen) { _armed = true; _requiresActiveNeutral = false; } else _neutralSeen = true;
            }
            else _neutralSeen = false;
            return Empty(provenance);
        }

        var pulses = new Dictionary<string, bool> {
            ["confirm"] = raw.Confirm, ["cancel"] = raw.Cancel, ["inspect"] = raw.Inspect, ["previous"] = raw.Previous,
            ["next"] = raw.Next, ["context"] = raw.Context, ["settings"] = raw.Settings, ["menuUp"] = raw.MenuUp, ["menuDown"] = raw.MenuDown };
        bool Edge(string name)
        {
            var key = $"{raw.DeviceId}:{name}"; var down = pulses[name]; var edge = down && !_held.Contains(key);
            if (down) _held.Add(key); else _held.Remove(key); return edge;
        }
        var confirm = Edge("confirm"); var cancel = Edge("cancel"); var inspect = Edge("inspect"); var previous = Edge("previous");
        var next = Edge("next"); var contextPulse = Edge("context"); var settings = Edge("settings"); var menuUp = Edge("menuUp"); var menuDown = Edge("menuDown");
        var physicalDown = context.ExplicitGamepadBindingsEnabled && isJoy ? raw.ControllerButtons : default;
        var physicalPressed = context.ExplicitGamepadBindingsEnabled && isJoy ? physicalDown & ~_physicalHeld : default;
        var physicalReleased = context.ExplicitGamepadBindingsEnabled && isJoy ? _physicalHeld & ~physicalDown : default;
        if (context.ExplicitGamepadBindingsEnabled && isJoy)
        {
            _physicalHeld = physicalDown;
            // Physical and historical controller aliases must never both dispatch.
            confirm = cancel = inspect = previous = next = contextPulse = settings = false;
        }
        var freshControllerPress = isJoy && (confirm || cancel || inspect || previous || next || contextPulse || settings || menuUp || menuDown);
        freshControllerPress |= physicalPressed != default;
        if (!raw.Echo && (raw.Keyboard || raw.MouseButton)) { Method = InputMethod.KeyboardMouse; _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; }
        if (!raw.Echo && raw.MouseLogicalDistance > 0)
        {
            _mouseLogicalDistance += raw.MouseLogicalDistance;
            if (_mouseLogicalDistance >= 6) { Method = InputMethod.KeyboardMouse; _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; }
        }
        if (isJoy && (freshControllerPress || freshStickQualification)) { Method = InputMethod.Gamepad; _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; }

        // Sensitivity is deliberately a single, linear multiplier per domain.  Both
        // physical-stick and keyboard vectors use the same policy; normalization
        // only caps a mixed diagonal command and must not erase sensitivity.
        var actor = CombineDirection(left, Normalize(raw.KeyboardActor)) * context.ActorSensitivity;
        // Fixed-sign keyboard arrows are pre-flipped so the shared inversion
        // below always yields the false/false sign for them (sign-symmetric combine).
        var keyboardCamera = Normalize(raw.KeyboardCamera);
        if (context.KeyboardCameraFixedSign)
            keyboardCamera = new Vec2(context.InvertCameraX ? -keyboardCamera.X : keyboardCamera.X, context.InvertCameraY ? -keyboardCamera.Y : keyboardCamera.Y);
        var cameraDirection = CombineDirection(right, keyboardCamera);
        // WORLD's valid huge finite trigger-only frames must not manufacture
        // NaN from zero * an overflowing pan scale. Keep the historical lab
        // route unchanged, and leave real nonzero overflow for host quarantine.
        var camera = context.WorldZoomEnabled && cameraDirection == default(Vec2) ? default
            : cameraDirection * (520 * context.CameraSensitivity * context.Delta / Math.Max(context.Zoom, .001));
        // The scene translates its world image. The historical GPAD001 positive pan
        // sign also moved that image right/down. The owner explicitly retains that
        // visual sign for the true defaults; false intentionally reverses an axis.
        // This is camera-only: actor and modal directions are calculated above.
        camera = new Vec2(context.InvertCameraX ? camera.X : -camera.X, context.InvertCameraY ? camera.Y : -camera.Y);
        var result = new IntentFrame(camera, actor, confirm, cancel, inspect, previous, next, contextPulse, settings,
            raw.MenuUp ? -1 : raw.MenuDown ? 1 : 0, raw.LeftStick.Y, Method, true, provenance)
        {
            MenuPressedDirection = menuUp ? -1 : menuDown ? 1 : 0,
            ActorActive = left.Length > 0 || raw.KeyboardActorHeld || raw.KeyboardActor.Length > 0,
            CameraActive = right.Length > 0 || raw.KeyboardCameraHeld || raw.KeyboardCamera.Length > 0,
            ZoomDrive = context.WorldZoomEnabled && isJoy && context.AdvanceTime
                ? EffectiveTrigger(raw.RightTrigger) - EffectiveTrigger(raw.LeftTrigger) : 0,
            ControllerDown = physicalDown, ControllerPressed = physicalPressed, ControllerReleased = physicalReleased,
            RadialSelection = right,
            GenuineCompleteObservation = raw.CompleteObservation && context.AdvanceTime && raw.FrameObservationId >= 0,
            FrameObservationId = raw.FrameObservationId, SourceDeviceId = raw.DeviceId
        };
        return context.Modal ? result with { CameraPan = default, Movement = default, ActorActive = false, CameraActive = false, ZoomDrive = 0 } : result;
    }

    private static Vec2 Normalize(Vec2 value) => value.Length > 0 ? value.Normalized() : default;
    private static Vec2 CombineDirection(Vec2 first, Vec2 second)
    {
        var combined = first + second;
        return combined.Length > 1 ? combined.Normalized() : combined;
    }
    private static bool Digital(RawInputFrame r) => r.Confirm || r.Cancel || r.Inspect || r.Previous || r.Next || r.Context || r.Settings || r.MenuUp || r.MenuDown;
    public static bool ValidTrigger(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
    public static double EffectiveTrigger(double value) => ValidTrigger(value) && value > .10 ? (value - .10) / .90 : 0;
    private static bool TriggerHeld(RawInputFrame r, InputContext c) => c.WorldZoomEnabled && (EffectiveTrigger(r.LeftTrigger) > 0 || EffectiveTrigger(r.RightTrigger) > 0);
    private static bool Meaningful(RawInputFrame r, InputContext c) => Digital(r) || (c.ExplicitGamepadBindingsEnabled && r.ControllerButtons != default) || TriggerHeld(r, c) || Vec2.CircularDeadzone(r.LeftStick, c.ActorDeadzone).Length >= .25 || Vec2.CircularDeadzone(r.RightStick, c.CameraDeadzone).Length >= .25;
    public static bool HasAnyLevel(RawInputFrame raw, InputContext context) => AnyLevel(raw,
        Vec2.CircularDeadzone(raw.LeftStick, context.ActorDeadzone), Vec2.CircularDeadzone(raw.RightStick, context.CameraDeadzone), context);
    private static bool AnyLevel(RawInputFrame r, Vec2 left, Vec2 right, InputContext context) => Digital(r) || (context.ExplicitGamepadBindingsEnabled && r.ControllerButtons != default) || TriggerHeld(r, context) || left.Length > 0 || right.Length > 0
        || r.KeyboardActorHeld || r.KeyboardCameraHeld || r.KeyboardActor.Length > 0 || r.KeyboardCamera.Length > 0;
    private static bool Finite(Vec2 vector) => double.IsFinite(vector.X) && double.IsFinite(vector.Y);
    private IntentFrame Empty(string provenance) => new(default, default, false, false, false, false, false, false, false, 0, 0, Method, false, provenance);
}