namespace GamepadLab.Core;

public enum InputMethod { KeyboardMouse, Gamepad }
public sealed record RawInputFrame(int DeviceId, Vec2 Stick, bool Confirm = false, bool Cancel = false, bool Inspect = false, bool Previous = false, bool Next = false, bool Context = false, bool Settings = false, bool MenuUp = false, bool MenuDown = false, bool Focused = true, bool Connected = true, bool Keyboard = false, bool MouseButton = false, double MouseLogicalDistance = 0, bool Echo = false);
public sealed record InputContext(double Deadzone = .2, double Sensitivity = 1, double Delta = 1d / 60, double Zoom = 1, bool Modal = false);
public sealed record IntentFrame(Vec2 Pan, bool Confirm, bool Cancel, bool Inspect, bool Previous, bool Next, bool Context, bool Settings, int MenuDirection, InputMethod Method, bool Accepted, string Provenance);

public sealed class InputStateReducer
{
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private int? _activeDevice;
    private bool _armed = true;
    private bool _neutralSeen;
    private double _gamepadMotionSeconds;
    public int? ActiveDevice => _activeDevice;
    public InputMethod Method { get; private set; } = InputMethod.KeyboardMouse;
    public void SetStartupDevices(IEnumerable<int> ids) => _activeDevice = ids.DefaultIfEmpty(-1).Min() is var id && id >= 0 ? id : null;
    public void Disconnect(int id) { if (_activeDevice == id) { _activeDevice = null; Quarantine(); } }
    public void FocusChanged(bool focused) { if (!focused) Quarantine(); }
    public void Reset() => Quarantine();
    public void Quarantine() { _held.Clear(); _armed = false; _neutralSeen = false; _gamepadMotionSeconds = 0; }
    public IntentFrame Reduce(RawInputFrame raw, InputContext context, string provenance = "LIVE OS EVENT (not physical usability proof)")
    {
        if (!raw.Focused || !raw.Connected) { Quarantine(); return Empty(); }
        if (_activeDevice is null && raw.DeviceId >= 0 && Meaningful(raw, context)) { _activeDevice = raw.DeviceId; Quarantine(); return Empty(); }
        if (raw.DeviceId >= 0 && raw.DeviceId != _activeDevice) return Empty();
        var processed = Vec2.CircularDeadzone(raw.Stick, context.Deadzone);
        if (!_armed) { if (!AnyLevel(raw, processed)) { if (_neutralSeen) _armed = true; else _neutralSeen = true; } else _neutralSeen = false; return Empty(); }
        if (!raw.Echo && (raw.Keyboard || raw.MouseButton || raw.MouseLogicalDistance >= 6)) Method = InputMethod.KeyboardMouse;
        if (raw.DeviceId >= 0 && (Digital(raw) || processed.Length >= .25)) { _gamepadMotionSeconds += context.Delta; if (Digital(raw) || _gamepadMotionSeconds >= .1) Method = InputMethod.Gamepad; } else _gamepadMotionSeconds = 0;
        var pulses = new Dictionary<string, bool> { ["confirm"] = raw.Confirm, ["cancel"] = raw.Cancel, ["inspect"] = raw.Inspect, ["previous"] = raw.Previous, ["next"] = raw.Next, ["context"] = raw.Context, ["settings"] = raw.Settings };
        bool Edge(string name) { var down = pulses[name]; var edge = down && !_held.Contains(name); if (down) _held.Add(name); else _held.Remove(name); return edge; }
        var menu = raw.MenuUp ? -1 : raw.MenuDown ? 1 : 0;
        var result = new IntentFrame(processed * (520 * context.Sensitivity * context.Delta / context.Zoom), Edge("confirm"), Edge("cancel"), Edge("inspect"), Edge("previous"), Edge("next"), Edge("context"), Edge("settings"), menu, Method, true, provenance);
        return context.Modal ? result with { Pan = default, Confirm = false, Inspect = false, Previous = false, Next = false, Context = false, Settings = false } : result;
    }
    private static bool Digital(RawInputFrame r) => r.Confirm || r.Cancel || r.Inspect || r.Previous || r.Next || r.Context || r.Settings || r.MenuUp || r.MenuDown;
    private static bool Meaningful(RawInputFrame r, InputContext c) => Digital(r) || Vec2.CircularDeadzone(r.Stick, c.Deadzone).Length >= .25;
    private static bool AnyLevel(RawInputFrame r, Vec2 processed) => Digital(r) || processed.Length > 0;
    private IntentFrame Empty() => new(default, false, false, false, false, false, false, false, 0, Method, false, "LIVE OS EVENT (not physical usability proof)");
}
