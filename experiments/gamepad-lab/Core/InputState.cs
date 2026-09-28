namespace GamepadLab.Core;

public enum InputMethod { KeyboardMouse, Gamepad }

/// <summary>One sampled device state. Events are adapted into this state; Core never depends on Godot.</summary>
public sealed record RawInputFrame(
    int DeviceId, Vec2 LeftStick, Vec2 RightStick = default,
    bool Confirm = false, bool Cancel = false, bool Inspect = false, bool Previous = false, bool Next = false,
    bool Context = false, bool Settings = false, bool MenuUp = false, bool MenuDown = false,
    bool Focused = true, bool Connected = true, bool Keyboard = false, bool MouseButton = false,
    double MouseLogicalDistance = 0, bool Echo = false, Vec2 KeyboardActor = default, Vec2 KeyboardCamera = default);

/// <summary>Frame-owned timing and per-domain policy. The two stick deadzones are never applied twice.</summary>
public sealed record InputContext(
    double CameraDeadzone = .2, double ActorDeadzone = .2,
    double CameraSensitivity = 1, double ActorSensitivity = 1,
    double Delta = 1d / 60, double Zoom = 1, bool InvertCameraX = true, bool InvertCameraY = true,
    bool Modal = false, bool AdvanceTime = false);

public sealed record IntentFrame(
    Vec2 CameraPan, Vec2 Movement, bool Confirm, bool Cancel, bool Inspect, bool Previous, bool Next, bool Context, bool Settings,
    int MenuDirection, double MenuAnalog, InputMethod Method, bool Accepted, string Provenance);

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
        _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; _requiresActiveNeutral = _activeDevice is not null;
    }
    public void EndFrame()
    {
        if (_activeDevice is not null || _activationCandidates.Count == 0) return;
        _activeDevice = _activationCandidates.Min(); _activationCandidates.Clear(); Quarantine();
    }

    public IntentFrame Reduce(RawInputFrame raw, InputContext context, string provenance = "LIVE OS EVENT (not physical usability proof)")
    {
        if (!raw.Focused || !raw.Connected) { Quarantine(); return Empty(provenance); }
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
            if (isJoy && Math.Max(left.Length, right.Length) >= .25)
            {
                _gamepadMotionSeconds += context.Delta;
                if (!_gamepadStickQualified && _gamepadMotionSeconds >= .1) { _gamepadStickQualified = true; freshStickQualification = true; }
            }
            else { _gamepadMotionSeconds = 0; _gamepadStickQualified = false; }
            if (_mouseLogicalDistance > 0)
            {
                _mouseMotionSeconds += context.Delta;
                if (_mouseMotionSeconds > .150) { _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; }
            }
        }
        if (!_armed)
        {
            if (_requiresActiveNeutral && (!isJoy || raw.DeviceId != _activeDevice)) return Empty(provenance);
            if (!AnyLevel(raw, left, right))
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
        var freshControllerPress = isJoy && (confirm || cancel || inspect || previous || next || contextPulse || settings || menuUp || menuDown);
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
        var camera = CombineDirection(right, Normalize(raw.KeyboardCamera))
            * (520 * context.CameraSensitivity * context.Delta / Math.Max(context.Zoom, .001));
        // The scene translates its world image. Default inversion makes stick-right move
        // that image right, equivalent to a camera pan left as requested by the owner.
        camera = new Vec2(context.InvertCameraX ? camera.X : -camera.X, context.InvertCameraY ? camera.Y : -camera.Y);
        var result = new IntentFrame(camera, actor, confirm, cancel, inspect, previous, next, contextPulse, settings,
            raw.MenuUp ? -1 : raw.MenuDown ? 1 : 0, raw.LeftStick.Y, Method, true, provenance);
        return context.Modal ? result with { CameraPan = default, Movement = default } : result;
    }

    private static Vec2 Normalize(Vec2 value) => value.Length > 0 ? value.Normalized() : default;
    private static Vec2 CombineDirection(Vec2 first, Vec2 second)
    {
        var combined = first + second;
        return combined.Length > 1 ? combined.Normalized() : combined;
    }
    private static bool Digital(RawInputFrame r) => r.Confirm || r.Cancel || r.Inspect || r.Previous || r.Next || r.Context || r.Settings || r.MenuUp || r.MenuDown;
    private static bool Meaningful(RawInputFrame r, InputContext c) => Digital(r) || Vec2.CircularDeadzone(r.LeftStick, c.ActorDeadzone).Length >= .25 || Vec2.CircularDeadzone(r.RightStick, c.CameraDeadzone).Length >= .25;
    private static bool AnyLevel(RawInputFrame r, Vec2 left, Vec2 right) => Digital(r) || left.Length > 0 || right.Length > 0 || r.KeyboardActor.Length > 0 || r.KeyboardCamera.Length > 0;
    private IntentFrame Empty(string provenance) => new(default, default, false, false, false, false, false, false, false, 0, 0, Method, false, provenance);
}