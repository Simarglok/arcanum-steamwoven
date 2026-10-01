using Arcanum.Input;
using Arcanum.Input.Godot;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

using Input = global::Godot.Input;

public partial class TerrainViewerApp
{
    private readonly GodotInputAdapter _worldAdapter = new();
    private readonly InputStateReducer _worldReducer = new();
    private readonly DiagnosticInputSafetyGate _worldGate = new();
    private readonly DiagnosticActorController _worldActor = new();
    private readonly DiagnosticInputSettingsStore _worldStore = new();
    private DiagnosticInputSettingsPanel? _worldPanel;
    private InputSettings _worldSettings = InputSettings.Default;
    private Vec2 _worldDrag;
    private bool _worldFocused = true;
    private bool _worldInitialized;
    private bool _worldBoundaryLimited;
    private bool _worldUiFocused;
    private int _worldMenuAnalogDirection;
    private bool IsWorld => _arguments?.Mode == TerrainViewerMode.WorldDiagnostic || _readyMode == TerrainViewerMode.WorldDiagnostic;
    private bool WorldModal => _worldPanel?.IsOpen == true;
    private bool WorldUiFocus => !WorldModal && GetViewport().GuiGetFocusOwner() is not null;
    private Vec2 WorldAnchor => DiagnosticActorMotion.Project(_worldActor.State.Position) + new Vec2(0, 20);
    private Vector2 WorldViewport => GetViewport().GetVisibleRect().Size;

    private void InitializeWorld()
    {
        if (_worldInitialized) return;
        _worldInitialized = true;
        // WORLD uses actual viewport pixels for bounds/HUD; a fixed virtual
        // 1280x800 stretch viewport would conceal native resize observations.
        // Do not change the host window for the isolated SubViewport harness.
        if (GetViewport() == GetWindow()) GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        _worldSettings = _worldStore.Load();
        _worldPanel = new DiagnosticInputSettingsPanel(); AddChild(_worldPanel);
        _worldPanel.Changed += SaveWorldSettings;
        _worldPanel.CloseRequested += ToggleWorldSettings;
        _worldPanel.ResetRequested += ResetWorldSettings;
        _hud!.SettingsRequested += ToggleWorldSettings;
        Input.JoyConnectionChanged += WorldConnectionChanged;
        _worldReducer.SetStartupDevices(Input.GetConnectedJoypads().Select(id => (int)id));
        _worldFocused = GetWindow().HasFocus();
    }
    private InputContext WorldContext(double delta = 0, bool advance = false) => new(
        CameraDeadzone: _worldSettings.CameraDeadzone, ActorDeadzone: _worldSettings.ActorDeadzone,
        CameraSensitivity: _worldSettings.CameraSensitivity, ActorSensitivity: _worldSettings.ActorSensitivity,
        Delta: delta, Zoom: _camera?.State.Zoom ?? 1, InvertCameraX: _worldSettings.InvertCameraX,
        InvertCameraY: _worldSettings.InvertCameraY, Modal: WorldModal || WorldUiFocus, AdvanceTime: advance);

    private void QuarantineWorld()
    {
        _worldGate.Close(); _worldReducer.Quarantine(); _worldActor.SafetyStop();
        _worldMenuAnalogDirection = 0;
        _dragging = false; _worldDrag = default; _camera?.SafetyResetWorld();
        if (_readyMode == TerrainViewerMode.WorldDiagnostic && _readyActorPlan is not null) PublishWorldSelection();
    }
    private void ResetWorldScene()
    {
        QuarantineWorld(); _worldActor.Reset(); _worldActor.SafetyStop();
        PublishWorldSelection(); _camera?.ResetWorld(WorldAnchor, WorldViewport); RefreshReadyStatus();
    }
    private void ToggleWorldSettings()
    {
        if (!IsWorld || _worldPanel is null) return;
        QuarantineWorld();
        if (_worldPanel.IsOpen) { _worldPanel.Hide(); GetViewport().GuiReleaseFocus(); }
        else _worldPanel.Open(_worldSettings);
        RefreshReadyStatus();
    }
    private void SaveWorldSettings(InputSettings settings)
    {
        _worldSettings = settings.Validated(); QuarantineWorld();
        try { _worldStore.Save(_worldSettings); _worldPanel?.SetSaveStatus(true); }
        catch (InvalidOperationException) { _worldPanel?.SetSaveStatus(false); }
    }
    private void ResetWorldSettings()
    {
        SaveWorldSettings(InputSettings.Default); _worldPanel?.Bind(_worldSettings); ResetWorldScene();
    }
    private void WorldConnectionChanged(long id, bool connected)
    {
        var device = checked((int)id);
        if (!connected)
        {
            var active = _worldReducer.ActiveDevice == device;
            _worldAdapter.ForgetDevice(device); _worldReducer.Disconnect(device);
            if (active) QuarantineWorld();
        }
        // A notification is never a complete neutral observation. Active reconnect
        // and meaningful acquisition close the gate on the next frame below.
    }
    public override void _Notification(int what)
    {
        if (!IsWorld || !_worldInitialized) return;
        if (what == NotificationApplicationFocusOut || what == NotificationApplicationFocusIn)
        {
            _worldFocused = what == NotificationApplicationFocusIn;
            _worldReducer.FocusChanged(_worldFocused); QuarantineWorld(); RefreshReadyStatus();
        }
    }
    private void HandleWorldPulse(IntentFrame intent)
    {
        if (!intent.Accepted) return;
        if (intent.Settings || (WorldModal && intent.Cancel)) { ToggleWorldSettings(); return; }
        if (!WorldModal) return;
        if (intent.MenuPressedDirection != 0) _worldPanel?.Navigate(intent.MenuPressedDirection);
        var analogDirection = Math.Abs(intent.MenuAnalog) >= .6 ? Math.Sign(intent.MenuAnalog) : 0;
        if (analogDirection != 0 && analogDirection != _worldMenuAnalogDirection) _worldPanel?.Navigate(analogDirection);
        _worldMenuAnalogDirection = analogDirection;
        if (intent.Previous) _worldPanel?.Adjust(-1);
        if (intent.Next) _worldPanel?.Adjust(1);
        if (intent.Confirm) _worldPanel?.Confirm();
    }
    private void ProcessWorld(double delta)
    {
        if (_exiting || !_worldInitialized) return;
        if (!double.IsFinite(delta) || delta < 0) { QuarantineWorld(); return; }
        if (WorldUiFocus != _worldUiFocused) { _worldUiFocused = WorldUiFocus; QuarantineWorld(); }
        var complete = _worldFocused;
        var controllers = new List<RawInputFrame>();
        if (complete)
        {
            _worldAdapter.RefreshKeyboardLevels(Input.IsKeyPressed);
            foreach (var id in Input.GetConnectedJoypads())
            {
                var device = (int)id;
                if (!_worldAdapter.RefreshControllerLevels(device, axis => Input.GetJoyAxis(device, axis), button => Input.IsJoyButtonPressed(device, button)))
                { complete = false; break; }
                controllers.Add(_worldAdapter.SampleController(device, true)!);
            }
        }
        ApplyWorldObservedFrame(delta, complete, _worldAdapter.SampleKeyboard(_worldFocused), controllers, "LIVE FRAME LEVELS (not physical usability proof)");
    }
    /// <summary>Production aggregation seam. Tests provide complete simulated levels,
    /// not cached neutral; ordinary input always comes from the pinned OS APIs above.</summary>
    private void ApplyWorldObservedFrame(double delta, bool complete, RawInputFrame keyboard, IReadOnlyList<RawInputFrame> controllers, string provenance)
    {
        // Admit completeness before timing or UI reduction. Cached/partial input
        // must not leave Walk, drag, or Manual-return eligibility latched.
        var observedActive = _worldReducer.ActiveDevice;
        static bool Valid(RawInputFrame raw) => raw.Focused && raw.Connected
            && double.IsFinite(raw.LeftStick.X) && double.IsFinite(raw.LeftStick.Y)
            && double.IsFinite(raw.RightStick.X) && double.IsFinite(raw.RightStick.Y)
            && double.IsFinite(raw.KeyboardActor.X) && double.IsFinite(raw.KeyboardActor.Y)
            && double.IsFinite(raw.KeyboardCamera.X) && double.IsFinite(raw.KeyboardCamera.Y);
        if (!complete || !_worldFocused || !double.IsFinite(delta) || delta < 0 || !Valid(keyboard)
            || controllers.Any(raw => !Valid(raw))
            || (observedActive is int device && !controllers.Any(raw => raw.DeviceId == device)))
        { QuarantineWorld(); RefreshReadyStatus(); return; }
        keyboard = WorldKeyboardRouting(keyboard);
        var context = WorldContext(delta, true);
        var priorActive = _worldReducer.ActiveDevice;
        // Timing belongs to exactly one selected sample per frame, never every
        // connected device plus the keyboard.
        var keyboardIntent = _worldReducer.Reduce(keyboard, context with { AdvanceTime = priorActive is null }, provenance);
        var intents = new List<IntentFrame> { keyboardIntent };
        foreach (var raw in controllers) intents.Add(_worldReducer.Reduce(raw, context with { AdvanceTime = raw.DeviceId == priorActive }, provenance));
        _worldReducer.EndFrame();
        if (priorActive != _worldReducer.ActiveDevice) QuarantineWorld();
        foreach (var intent in intents) HandleWorldPulse(intent);
        var active = _worldReducer.ActiveDevice;
        complete &= active is null || controllers.Any(raw => raw.DeviceId == active);
        var neutral = !InputStateReducer.HasAnyLevel(keyboard, context)
            && controllers.Where(raw => raw.DeviceId == active).All(raw => !InputStateReducer.HasAnyLevel(raw, context)) && !_dragging;
        var wasOpen = _worldGate.IsOpen;
        _worldGate.ObserveCompleteFrame(_worldFocused, WorldModal || WorldUiFocus, complete, neutral);
        if (!wasOpen || !_worldGate.IsOpen || _readyActorPlan is null || _readyMode != TerrainViewerMode.WorldDiagnostic)
        { _worldDrag = default; return; }
        var actor = intents.Aggregate(default(Vec2), (sum, intent) => sum + intent.Movement);
        if (actor.Length > _worldSettings.ActorSensitivity) actor = actor.Normalized() * _worldSettings.ActorSensitivity;
        var pan = intents.Aggregate(_worldDrag, (sum, intent) => sum + intent.CameraPan);
        var actorActive = intents.Any(intent => intent.ActorActive);
        var cameraActive = intents.Any(intent => intent.CameraActive) || _dragging || _worldDrag.Length > 0;
        _worldDrag = default;
        if (!double.IsFinite(pan.X) || !double.IsFinite(pan.Y)) { QuarantineWorld(); return; }
        try
        {
            var step = _worldActor.Update(actor, delta, input => _readyActorPlan.Select(input));
            if (step.Status != DiagnosticMotionStatus.Accepted) { QuarantineWorld(); return; }
            _worldBoundaryLimited = step.BoundaryLimited;
            PublishWorldSelection();
            _camera!.UpdateWorld(WorldAnchor, actor, pan, actorActive, cameraActive, WorldViewport);
            RefreshReadyStatus();
        }
        catch (TerrainViewerException exception) { PublishFailure(TerrainViewerMode.WorldDiagnostic, exception.Error); }
        catch (OverflowException) { PublishFailure(TerrainViewerMode.WorldDiagnostic, new(TerrainViewerErrorCode.ActorPlan, Stage: TerrainViewerFailureStage.ActorPlan)); }
    }
    private void PublishWorldSelection()
    {
        if (_readyActorPlan is null || _readyDiagnosticPlan?.IsWorldDiagnostic != true) return;
        var input = DiagnosticActorMotion.ToPlacement(_worldActor.State, _worldActor.CurrentFrame);
        var actor = _readyActorPlan.Select(input);
        var plan = _readyDiagnosticPlan.WithWorldActor(actor);
        _readyActorInput = input; _readyActorPlan = actor; _readyActor = actor.Current; _readyDiagnosticPlan = plan;
        _canvas!.Publish(plan, _cache);
    }
    private void InputWorld(InputEvent input)
    {
        var wasModal = WorldModal;
        var raw = _worldAdapter.Map(input, _worldFocused);
        if (raw.DeviceId < 0) raw = WorldKeyboardRouting(raw);
        HandleWorldPulse(_worldReducer.Reduce(raw, WorldContext()));
        // Semantic keyboard/controller modal routing is owned above; do not
        // also let focused native controls toggle/navigate the same event.
        var semanticKey = input is InputEventKey keyEvent &&
            (keyEvent.Keycode is Key.F1 or Key.Escape or Key.Up or Key.Down or Key.W or Key.S
            || (GetViewport().GuiGetFocusOwner() is not LineEdit && keyEvent.Keycode is Key.Enter or Key.Space or Key.Backspace or Key.Q or Key.E));
        if ((wasModal || WorldModal) && (semanticKey || input is InputEventJoypadButton or InputEventJoypadMotion))
            GetViewport().SetInputAsHandled();
        if (!_worldFocused || WorldModal || WorldUiFocus || _readyActorPlan is null) return;
        var overHud = input is InputEventMouse mouse && _hud?.Rect.HasPoint(mouse.Position) == true;
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _dragging = button.Pressed && !overHud && _worldGate.IsOpen;
            if (!overHud && button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                _camera?.ZoomWorld(button.ButtonIndex == MouseButton.WheelUp ? 1.25 : .8, WorldAnchor, WorldViewport);
        }
        else if (input is InputEventMouseMotion motion && _dragging && !overHud) _worldDrag += new Vec2(motion.Relative.X, motion.Relative.Y) * (1 / (_camera?.State.Zoom ?? 1));
        else if (input is InputEventKey key && key.Pressed && !key.Echo && GetViewport().GuiGetFocusOwner() is null)
        {
            if (key.Keycode == Key.R) ResetWorldScene();
            else if (key.Keycode == Key.F) { QuarantineWorld(); _camera?.Fit(WorldViewport); }
            else if (key.Keycode == Key.L && _arguments is not null) { QuarantineWorld(); _lastLoad = StartLoadAsync(_arguments, false); }
            else if (key.Keycode is Key.Plus or Key.Minus) _camera?.ZoomWorld(key.Keycode == Key.Plus ? 1.25 : .8, WorldAnchor, WorldViewport);
            else if (key.Keycode == Key.F11) GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
            else if (key.Keycode == Key.Escape && GetWindow().Mode == Window.ModeEnum.Fullscreen) GetWindow().Mode = Window.ModeEnum.Windowed;
        }
        RefreshReadyStatus();
    }
    internal void WorldFrameForSelfTest(RawInputFrame keyboard, IReadOnlyList<RawInputFrame> controllers, double delta, bool complete = true)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Simulation requires an explicit test launch.");
        ApplyWorldObservedFrame(delta, complete, keyboard, controllers, "SIMULATION complete-level frame (not physical evidence)");
    }
    private RawInputFrame WorldKeyboardRouting(RawInputFrame raw)
    {
        // The native numeric editor owns text, Enter, Backspace and Tab. Only
        // explicit Escape cancels here; do not reinterpret Backspace as Cancel.
        if (WorldModal && GetViewport().GuiGetFocusOwner() is LineEdit)
            return raw with { Confirm = false, Cancel = _worldAdapter.IsKeyHeld(Key.Escape), Previous = false, Next = false };
        return raw;
    }
    internal DiagnosticActorState WorldStateForSelfTest => _worldActor.State;
    internal CameraFollowState WorldFollowForSelfTest => _camera!.FollowState;
    internal void WorldFocusForSelfTest(bool focused)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Simulation requires an explicit test launch.");
        _worldFocused = focused; QuarantineWorld();
    }
    private string WorldStatus()
    {
        var s = _worldActor.State;
        return $" | World sector-local cells ({s.Position.X:0.####},{s.Position.Y:0.####}) | anchor domain [0,63] reset (30,32) | free projected speed 240 px/s at sensitivity 1 | Actor: {s.Action} | Frame: {_worldActor.CurrentFrame} | cadence Idle 6 / Walk 10 | Facing mapping: project diagnostic octants; source facing numbers opaque | requested {s.Facing} | Camera: {_camera!.FollowState.Mode} | boundary limited {_worldBoundaryLimited} | neutral gate {(_worldGate.IsOpen ? "open" : "quarantined")} | Layering: terrain -> facades -> statics -> actor -> guides -> HUD | Controls: WASD/left actor, arrows/right image pan, drag camera, wheel/+/- zoom, F fit, R reset, L reload, F1/Start settings";
    }
    private void ExitWorld()
    {
        if (!_worldInitialized) return;
        QuarantineWorld();
        Input.JoyConnectionChanged -= WorldConnectionChanged;
        if (_hud is not null) _hud.SettingsRequested -= ToggleWorldSettings;
        if (_worldPanel is not null)
        {
            _worldPanel.Changed -= SaveWorldSettings; _worldPanel.CloseRequested -= ToggleWorldSettings; _worldPanel.ResetRequested -= ResetWorldSettings;
        }
    }
}
