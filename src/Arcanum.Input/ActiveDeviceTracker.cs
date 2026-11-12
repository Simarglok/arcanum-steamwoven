using GamepadLab.Core;

namespace Arcanum.Input;

/// <summary>The device whose prompts the HUD shows (concept §0.2). All devices stay live; this only drives glyphs and prompts.</summary>
public enum ActiveDevice { KeyboardMouse, Pad, Touch }

public enum DeviceSignalKind { PadButton, PadAxis, MouseMotion, MouseClick, Key, Touch }

/// <summary>One raw event with the host's own time in seconds. Positions are screen pixels; <c>Value</c> is an axis deflection.</summary>
public readonly record struct DeviceSignal(DeviceSignalKind Kind, double Time, Vec2 Position = default, Vec2 Delta = default, double Value = 0, int PadDeviceId = -1)
{
    public static DeviceSignal PadButton(double time, int device = -1) => new(DeviceSignalKind.PadButton, time, PadDeviceId: device);
    public static DeviceSignal PadAxis(double time, double value, int device = -1) => new(DeviceSignalKind.PadAxis, time, Value: value, PadDeviceId: device);
    public static DeviceSignal MouseMotion(double time, Vec2 position, Vec2 delta) => new(DeviceSignalKind.MouseMotion, time, position, delta);
    public static DeviceSignal MouseClick(double time, Vec2 position) => new(DeviceSignalKind.MouseClick, time, position);
    public static DeviceSignal Key(double time) => new(DeviceSignalKind.Key, time);
    public static DeviceSignal Touch(double time, Vec2 position) => new(DeviceSignalKind.Touch, time, position);
}

/// <summary>Numbers of concept §0.2; the defaults are the concept's.</summary>
public sealed record ActiveDeviceOptions(double PadDeadzone = .3, double MouseMotionPixels = 12, double MouseMotionWindow = .1,
    double PointerFromPadWindow = 2, double PointerFromPadFade = 3, double Hysteresis = .25,
    bool EmulatedTouchClicks = true, double EmulatedTouchJump = 24, double TouchMouseEchoWindow = .5);

/// <summary>"Last meaningful input wins" with hysteresis, no clocks and no Godot. Meaningful input: a pad button, a stick or trigger beyond the
/// deadzone (drift is ignored), a mouse click or key, mouse motion over a threshold, a touch. Mouse motion alone that follows pad use within the
/// pointer-from-pad window (a Deck trackpad or gyro mapped to a mouse) keeps the pad prompts and only shows the cursor. When emulated touch is on,
/// a click that appears at a new spot with no motion before it counts as touch (the Deck's touch screen under Proton arrives that way).
/// A change of the active device only becomes visible after <see cref="ActiveDeviceOptions.Hysteresis"/>; the tracker holds no window or
/// focus state, so a change never resets an open window.</summary>
public sealed class ActiveDeviceTracker
{
    private readonly ActiveDeviceOptions _options;
    private readonly Queue<(double Time, double Pixels)> _motion = new();
    private double _motionSum;
    private double _lastPadTime = double.NegativeInfinity;
    private double _lastPointerFromPadTime = double.NegativeInfinity;
    private ActiveDevice? _pending;
    private double _pendingSince;
    private bool _hasMouseAt;
    private Vec2 _mouseAt;
    private bool _motionSinceClick;
    private double _lastTouchTime = double.NegativeInfinity;

    public ActiveDeviceTracker(ActiveDeviceOptions? options = null, ActiveDevice initial = ActiveDevice.KeyboardMouse)
    { _options = options ?? new ActiveDeviceOptions(); Active = initial; }

    public ActiveDevice Active { get; private set; }
    /// <summary>The pad device id of the latest pad signal, or -1. Never assumed to be device 0.</summary>
    public int LastPadDeviceId { get; private set; } = -1;
    public event Action<ActiveDevice, ActiveDevice>? Changed;

    /// <summary>True while the cursor should be drawn: the mouse is active, or a trackpad/gyro moved it just now while the pad stays active.</summary>
    public bool CursorVisible(double now) => Active == ActiveDevice.KeyboardMouse || now - _lastPointerFromPadTime <= _options.PointerFromPadFade;

    public void Observe(in DeviceSignal signal)
    {
        if (!double.IsFinite(signal.Time) || !Finite(signal.Position) || !Finite(signal.Delta)) return;
        Update(signal.Time);
        var device = Classify(signal);
        if (device is { } d) Meaningful(d, signal.Time);
        Update(signal.Time);
    }

    /// <summary>Applies a pending change once it has stood for the hysteresis; call with the frame time when no signal arrives.</summary>
    public void Update(double now)
    {
        if (_pending is not { } next || now - _pendingSince < _options.Hysteresis) return;
        _pending = null;
        if (next == Active) return;
        var previous = Active; Active = next;
        Changed?.Invoke(previous, next);
    }

    private ActiveDevice? Classify(in DeviceSignal s)
    {
        switch (s.Kind)
        {
            case DeviceSignalKind.PadButton:
                PadUsed(s); return ActiveDevice.Pad;
            case DeviceSignalKind.PadAxis:
                if (!double.IsFinite(s.Value) || Math.Abs(s.Value) <= _options.PadDeadzone) return null;
                PadUsed(s); return ActiveDevice.Pad;
            case DeviceSignalKind.Key:
                return ActiveDevice.KeyboardMouse;
            case DeviceSignalKind.Touch:
                _mouseAt = s.Position; _hasMouseAt = true; _motionSinceClick = false; _lastTouchTime = s.Time; return ActiveDevice.Touch;
            case DeviceSignalKind.MouseClick:
            {
                // The engine's mouse emulation echoes a real touch as a click or motion at the same spot: not the mouse.
                if (s.Time - _lastTouchTime <= _options.TouchMouseEchoWindow) { _mouseAt = s.Position; _hasMouseAt = true; _motionSinceClick = false; return null; }
                var jumped = _options.EmulatedTouchClicks && _hasMouseAt && !_motionSinceClick
                    && Vec2.Distance(_mouseAt, s.Position) > _options.EmulatedTouchJump;
                _mouseAt = s.Position; _hasMouseAt = true; _motionSinceClick = false;
                if (jumped) _lastTouchTime = s.Time;
                return jumped ? ActiveDevice.Touch : ActiveDevice.KeyboardMouse;
            }
            default:
                return Motion(s);
        }
    }

    private ActiveDevice? Motion(in DeviceSignal s)
    {
        _mouseAt = s.Position; _hasMouseAt = true;
        if (s.Time - _lastTouchTime <= _options.TouchMouseEchoWindow) return null;
        _motionSinceClick = true;
        if (_motion.Count > 0 && s.Time < _motion.Peek().Time) { _motion.Clear(); _motionSum = 0; } // the host clock went backwards
        var pixels = s.Delta.Length;
        if (!double.IsFinite(pixels)) return null;
        _motion.Enqueue((s.Time, pixels)); _motionSum += pixels;
        while (_motion.Count > 0 && s.Time - _motion.Peek().Time > _options.MouseMotionWindow) _motionSum -= _motion.Dequeue().Pixels;
        if (_motion.Count == 0) _motionSum = 0;
        if (_motionSum <= _options.MouseMotionPixels) return null;
        if (s.Time - _lastPadTime <= _options.PointerFromPadWindow) { _lastPointerFromPadTime = s.Time; return null; }
        return ActiveDevice.KeyboardMouse;
    }

    private static bool Finite(Vec2 v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

    private void PadUsed(in DeviceSignal s) { _lastPadTime = s.Time; if (s.PadDeviceId >= 0) LastPadDeviceId = s.PadDeviceId; }

    private void Meaningful(ActiveDevice device, double time)
    {
        if (device == Active) { _pending = null; return; }
        if (_pending != device) { _pending = device; _pendingSince = time; }
    }
}
