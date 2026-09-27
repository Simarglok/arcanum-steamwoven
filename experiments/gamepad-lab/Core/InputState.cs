namespace GamepadLab.Core;

public enum InputMethod { KeyboardMouse, Gamepad }

/// <summary>One sampled device state. Events are adapted into this state; Core never depends on Godot.</summary>
public sealed record RawInputFrame(
    int DeviceId, Vec2 Stick,
    bool Confirm = false, bool Cancel = false, bool Inspect = false, bool Previous = false, bool Next = false,
    bool Context = false, bool Settings = false, bool MenuUp = false, bool MenuDown = false,
    bool Focused = true, bool Connected = true, bool Keyboard = false, bool MouseButton = false,
    double MouseLogicalDistance = 0, bool Echo = false, Vec2 KeyboardPan = default);

/// <summary>
/// An event observes levels and mouse travel; the scene's once-per-frame sample is
/// the sole clock owner. This prevents a burst of OS events from ageing either
/// method-switch window more than one rendered frame.
/// </summary>
public sealed record InputContext(double Deadzone = .2, double Sensitivity = 1, double Delta = 1d / 60, double Zoom = 1, bool Modal = false, bool AdvanceTime = false);
public sealed record IntentFrame(Vec2 Pan, bool Confirm, bool Cancel, bool Inspect, bool Previous, bool Next, bool Context, bool Settings, int MenuDirection, double MenuAnalog, InputMethod Method, bool Accepted, string Provenance);

public sealed class InputStateReducer
{
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private int? _activeDevice;
    private readonly HashSet<int> _activationCandidates = [];
    private bool _armed = true;
    private bool _neutralSeen;
    // Focus/reset/menu quarantine must be discharged by the selected controller
    // itself. Keyboard/no-device frames cannot make a held controller safe.
    private bool _requiresActiveNeutral;
    private double _gamepadMotionSeconds;
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
    public void FocusChanged(bool focused) { Quarantine(); }
    public void Reset() => Quarantine();
    public void Quarantine()
    {
        _held.Clear(); _armed = false; _neutralSeen = false; _gamepadMotionSeconds = 0;
        _mouseMotionSeconds = 0; _mouseLogicalDistance = 0;
        _requiresActiveNeutral = _activeDevice is not null;
    }

    /// <summary>Chooses exactly one controller after all events in the input frame have been observed.</summary>
    public void EndFrame()
    {
        if (_activeDevice is not null || _activationCandidates.Count == 0) return;
        _activeDevice = _activationCandidates.Min();
        _activationCandidates.Clear();
        Quarantine(); // The activation event is intentionally consumed; a neutral frame is required next.
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

        var processed = Vec2.CircularDeadzone(raw.Stick, context.Deadzone);
        if (context.AdvanceTime)
        {
            _gamepadMotionSeconds = isJoy && processed.Length >= .25 ? _gamepadMotionSeconds + context.Delta : 0;
            if (_mouseLogicalDistance > 0)
            {
                _mouseMotionSeconds += context.Delta;
                if (_mouseMotionSeconds > .150) { _mouseMotionSeconds = 0; _mouseLogicalDistance = 0; }
            }
        }
        if (!_armed)
        {
            if (_requiresActiveNeutral && (!isJoy || raw.DeviceId != _activeDevice)) return Empty(provenance);
            if (!AnyLevel(raw, processed))
            {
                if (_neutralSeen) { _armed = true; _requiresActiveNeutral = false; }
                else _neutralSeen = true;
            }
            else _neutralSeen = false;
            return Empty(provenance);
        }

        if (!raw.Echo && (raw.Keyboard || raw.MouseButton))
        {
            Method = InputMethod.KeyboardMouse;
            _mouseMotionSeconds = 0; _mouseLogicalDistance = 0;
        }
        if (!raw.Echo && raw.MouseLogicalDistance > 0)
        {
            _mouseLogicalDistance += raw.MouseLogicalDistance;
            if (_mouseLogicalDistance >= 6)
            {
                Method = InputMethod.KeyboardMouse;
                _mouseMotionSeconds = 0; _mouseLogicalDistance = 0;
            }
        }
        if (isJoy && (Digital(raw) || processed.Length >= .25))
        {
            if (Digital(raw) || _gamepadMotionSeconds >= .1)
            {
                Method = InputMethod.Gamepad;
                _mouseMotionSeconds = 0; _mouseLogicalDistance = 0;
            }
        }

        var pulses = new Dictionary<string, bool> {
            ["confirm"] = raw.Confirm, ["cancel"] = raw.Cancel, ["inspect"] = raw.Inspect, ["previous"] = raw.Previous,
            ["next"] = raw.Next, ["context"] = raw.Context, ["settings"] = raw.Settings
        };
        bool Edge(string name)
        {
            var sourceName = $"{raw.DeviceId}:{name}";
            var down = pulses[name]; var edge = down && !_held.Contains(sourceName);
            if (down) _held.Add(sourceName); else _held.Remove(sourceName);
            return edge;
        }

        var keyboardPan = raw.KeyboardPan.Length > 0 ? raw.KeyboardPan.Normalized() : default;
        var pan = (processed + keyboardPan) * (520 * context.Sensitivity * context.Delta / Math.Max(context.Zoom, .001));
        var menu = raw.MenuUp ? -1 : raw.MenuDown ? 1 : 0;
        // Menu hysteresis is a physical stick threshold (.65 engage/.45 release),
        // independent of the movement deadzone's radial rescale.
        var result = new IntentFrame(pan, Edge("confirm"), Edge("cancel"), Edge("inspect"), Edge("previous"), Edge("next"), Edge("context"), Edge("settings"), menu, raw.Stick.Y, Method, true, provenance);
        // The caller routes modal pulses to UI first; only world pan is suppressed here.
        return context.Modal ? result with { Pan = default } : result;
    }

    private static bool Digital(RawInputFrame r) => r.Confirm || r.Cancel || r.Inspect || r.Previous || r.Next || r.Context || r.Settings || r.MenuUp || r.MenuDown;
    private static bool Meaningful(RawInputFrame r, InputContext c) => Digital(r) || Vec2.CircularDeadzone(r.Stick, c.Deadzone).Length >= .25;
    private static bool AnyLevel(RawInputFrame r, Vec2 processed) => Digital(r) || processed.Length > 0 || r.KeyboardPan.Length > 0;
    private IntentFrame Empty(string provenance) => new(default, false, false, false, false, false, false, false, 0, 0, Method, false, provenance);
}