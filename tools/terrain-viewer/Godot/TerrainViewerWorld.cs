using Arcanum.Input;
using Arcanum.Input.Godot;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

using Input = global::Godot.Input;

public partial class TerrainViewerApp
{
    private GodotInputAdapter _worldAdapter = new();
    private InputStateReducer _worldReducer = new();
    private DiagnosticInputSafetyGate _worldGate = new();
    private DiagnosticGridActor _worldActor = new();
    private GridInputQueue _worldGridInput = new();
    private bool _worldResetPending;
    private bool _worldReloadPending;
    private DiagnosticGridSession? _worldLifecycleSession;
    private string _worldGridDiagnostic = "No step assessment yet";
    private SimulationTime? _worldTestTime;
    private SimulationTime? _worldObservationTime;
    private SimulationTime WorldTime => _worldObservationTime ?? _worldTestTime ?? SimulationTime.At(Time.GetTicksUsec(), 1_000_000);
    private void WorldObservation(Action action)
    {
        if (_worldObservationTime.HasValue) { action(); return; }
        _worldObservationTime = WorldTime;
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or OverflowException or TerrainViewerException or ArgumentException)
        {
            // Fault disposal is distinct from motion settlement. Do not retry
            // quarantine on an invalid/closed clock or publish trial events.
            _worldGate.Close(); _worldReducer.Quarantine();
            GD.Print("ARC-WORLD-003 lifecycle=observation-fault-disposal (not CenterArrived)");
            // Only our closed numeric input/runtime category is safe here;
            // arbitrary exception text can contain protected input paths.
            if (error.Message.StartsWith("GRID_", StringComparison.Ordinal)
                && error.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                GD.Print("ARC-WORLD-003 observation-failure=" + error.Message);
            PublishFailure(TerrainViewerMode.WorldDiagnostic, new(TerrainViewerErrorCode.ActorPlan, Stage: TerrainViewerFailureStage.ActorPlan));
            if (SelfTestBootstrap || _nativeSmoke) throw;
        }
        finally { _worldObservationTime = null; }
    }
    private void RetireWorldLifecycle()
    {
        _worldResetPending = false; _worldReloadPending = false; _worldLifecycleSession = null;
    }
    private readonly DiagnosticInputSettingsStore _worldStore = new();
    private readonly DiagnosticZoomSettingsStore _worldZoomStore = new();
    private double _worldPreferredZoom = 2;
    private bool _worldFitView;
    private bool _worldZoomPending;
    private bool _worldZoomSaveFailed;
    private double _worldZoomSettled;
    private DiagnosticInputSettingsPanel? _worldPanel;
    private InputSettings _worldSettings = DiagnosticWorldPolicy.DefaultInputSettings;
    private Vec2 _worldDrag;
    private bool _worldFocused = true;
    private bool _worldInitialized;

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
        _worldPreferredZoom = _worldZoomStore.Load();
        _worldPanel = new DiagnosticInputSettingsPanel(); AddChild(_worldPanel);
        _worldPanel.Changed += SaveWorldSettings;
        _worldPanel.CloseRequested += ToggleWorldSettings;
        _worldPanel.ResetRequested += ResetWorldSettings;
        _worldPanel.ZoomRequested += SetPreferredWorldZoom;
        _worldPanel.FitRequested += FitWorldView;
        _hud!.SettingsRequested += ToggleWorldSettings;
        Input.JoyConnectionChanged += WorldConnectionChanged;
        _worldReducer.SetStartupDevices(Input.GetConnectedJoypads().Select(id => (int)id));
        _worldFocused = GetWindow().HasFocus();
    }
    private InputContext WorldContext(double delta = 0, bool advance = false) => new(
        CameraDeadzone: _worldSettings.CameraDeadzone, ActorDeadzone: _worldSettings.ActorDeadzone,
        CameraSensitivity: _worldSettings.CameraSensitivity, ActorSensitivity: _worldSettings.ActorSensitivity,
        Delta: delta, Zoom: _camera?.State.Zoom ?? 1, InvertCameraX: _worldSettings.InvertCameraX,
        InvertCameraY: _worldSettings.InvertCameraY, Modal: WorldModal || WorldUiFocus, AdvanceTime: advance) { WorldZoomEnabled = true };

    private void QuarantineWorld() => WorldObservation(QuarantineWorldObserved);
    private void QuarantineWorldObserved()
    {
        _worldGate.Close(); _worldReducer.Quarantine();
        if (_worldActor.Session is { } session && !_exiting)
            _worldGridInput.Observe(session, WorldTime, ActorIntent.Neutral);
        _worldMenuAnalogDirection = 0;
        _dragging = false; _worldDrag = default; _camera?.SafetyResetWorld();
        if (_readyMode == TerrainViewerMode.WorldDiagnostic && _readyActorPlan is not null) PublishWorldSelection();
    }
    private void ResetWorldScene() => WorldObservation(() => RequestWorldLifecycle(false));
    private void RequestWorldLifecycle(bool reload)
    {
        QuarantineWorld();
        if (_worldActor.Session is null) return;
        _worldLifecycleSession = _worldActor.Session;
        _worldReloadPending |= reload; _worldResetPending = !_worldReloadPending;
        FlushWorldZoom(); _worldFitView = false;
        RefreshReadyStatus();
    }
    private void ToggleWorldSettings()
    {
        if (!IsWorld || _worldPanel is null) return;
        QuarantineWorld();
        if (_worldPanel.IsOpen) { _worldPanel.Hide(); GetViewport().GuiReleaseFocus(); }
        else { _worldPanel.Open(_worldSettings); BindWorldZoom(); }
        RefreshReadyStatus();
    }
    private void SaveWorldSettings(InputSettings settings)
    {
        _worldSettings = settings.Validated(DiagnosticWorldPolicy.DefaultInputSettings); QuarantineWorld();
        try { _worldStore.Save(_worldSettings); _worldPanel?.SetSaveStatus(true); }
        catch (InvalidOperationException) { _worldPanel?.SetSaveStatus(false); }
    }
    private void ResetWorldSettings()
    {
        SaveWorldSettings(DiagnosticWorldPolicy.DefaultInputSettings); SetPreferredWorldZoom(2);
        _worldPanel?.Bind(_worldSettings); ResetWorldScene();
    }
    private void BindWorldZoom() => _worldPanel?.BindZoom(_camera?.State.Zoom ?? _worldPreferredZoom, _worldPreferredZoom, _worldFitView, _worldZoomPending, _worldZoomSaveFailed);
    private void SetPreferredWorldZoom(double value) => ApplyPreferredWorldZoom(value, false);
    private void ApplyPreferredWorldZoom(double value, bool continuous)
    {
        if (!double.IsFinite(value) || _camera is null) return;
        _camera.SetWorldZoom(value, WorldAnchor, WorldViewport); _worldFitView = false;
        _worldPreferredZoom = _camera.State.Zoom;
        _worldZoomPending = true; _worldZoomSaveFailed = false; _worldZoomSettled = 0;
        if (!continuous) FlushWorldZoom();
        BindWorldZoom(); RefreshReadyStatus();
    }
    private void StepPreferredWorldZoom(double factor) => SetPreferredWorldZoom(WorldZoomPolicy.Step(_camera!.State.Zoom, factor));
    private void FitWorldView()
    {
        QuarantineWorld(); _camera?.Fit(WorldViewport); _worldFitView = true;
        BindWorldZoom(); RefreshReadyStatus();
    }
    private void FlushWorldZoom()
    {
        if (!_worldZoomPending) return;
        try { _worldZoomStore.Save(_worldPreferredZoom); _worldZoomPending = false; _worldZoomSaveFailed = false; }
        catch (InvalidOperationException) { _worldZoomSaveFailed = true; }
        BindWorldZoom();
    }
    private void SettleWorldZoom(double delta, bool driving)
    {
        if (!_worldZoomPending || _worldZoomSaveFailed || driving) return;
        _worldZoomSettled = Math.Min(.25, _worldZoomSettled + Math.Min(delta, .25));
        if (_worldZoomSettled >= .25) FlushWorldZoom();
    }
    private void WorldConnectionChanged(long id, bool connected) => WorldObservation(() => WorldConnectionObserved(id, connected));
    private void WorldConnectionObserved(long id, bool connected)
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
        // Only the explicit navigation simulation owns focus while its native
        // GUI runs. Ordinary/lifecycle routes always retain OS focus quarantine.
        if (_worldFocusSimulationForSelfTest && (SelfTestBootstrap || _nativeSmoke)) return;
        if (what == NotificationApplicationFocusOut || what == NotificationApplicationFocusIn)
        {
            WorldObservation(() =>
            {
                _worldFocused = what == NotificationApplicationFocusIn;
                _worldReducer.FocusChanged(_worldFocused); QuarantineWorld(); RefreshReadyStatus();
            });
        }
    }
    private void HandleWorldPulse(IntentFrame intent, bool ownsMenuAnalog)
    {
        if (!intent.Accepted) return;
        if (intent.Settings || (WorldModal && intent.Cancel)) { ToggleWorldSettings(); return; }
        if (!WorldModal) return;
        if (intent.MenuPressedDirection != 0) _worldPanel?.Navigate(intent.MenuPressedDirection);
        // Only the selected device can release its analog menu latch. A neutral
        // keyboard reduction in the same frame is not controller-neutral proof.
        if (ownsMenuAnalog)
        {
            var analogDirection = Math.Abs(intent.MenuAnalog) >= .6 ? Math.Sign(intent.MenuAnalog) : 0;
            if (analogDirection != 0 && analogDirection != _worldMenuAnalogDirection) _worldPanel?.Navigate(analogDirection);
            _worldMenuAnalogDirection = analogDirection;
        }
        if (intent.Previous) _worldPanel?.Adjust(-1);
        if (intent.Next) _worldPanel?.Adjust(1);
        if (intent.Confirm) _worldPanel?.Confirm();
    }
    private void ProcessWorld(double delta)
    {
        if (_exiting || !_worldInitialized) return;
        if (_worldTestTime.HasValue && (SelfTestBootstrap || _nativeSmoke)) return;
        WorldObservation(() => ProcessWorldObserved(delta));
    }
    private void ProcessWorldObserved(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) { QuarantineWorld(); AdvanceWorldGrid(default); return; }
        if (WorldUiFocus != _worldUiFocused) { _worldUiFocused = WorldUiFocus; QuarantineWorld(); }
        var complete = _worldFocused;
        var controllers = new List<RawInputFrame>();
        if (complete)
        {
            _worldAdapter.RefreshKeyboardLevels(Input.IsKeyPressed);
            foreach (var id in Input.GetConnectedJoypads())
            {
                var device = (int)id;
                if (!_worldAdapter.RefreshControllerLevels(device, axis => Input.GetJoyAxis(device, axis), button => Input.IsJoyButtonPressed(device, button), worldZoom: true))
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
            && double.IsFinite(raw.KeyboardCamera.X) && double.IsFinite(raw.KeyboardCamera.Y)
            && InputStateReducer.ValidTrigger(raw.LeftTrigger) && InputStateReducer.ValidTrigger(raw.RightTrigger);
        if (!complete || !_worldFocused || !double.IsFinite(delta) || delta < 0 || !Valid(keyboard)
            || controllers.Any(raw => !Valid(raw))
            || (observedActive is int device && !controllers.Any(raw => raw.DeviceId == device)))
        { QuarantineWorld(); AdvanceWorldGrid(default); RefreshReadyStatus(); return; }
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
        for (var i = 0; i < intents.Count; i++)
            HandleWorldPulse(intents[i], i == 0 ? _worldReducer.ActiveDevice is null : controllers[i - 1].DeviceId == _worldReducer.ActiveDevice);
        var active = _worldReducer.ActiveDevice;
        complete &= active is null || controllers.Any(raw => raw.DeviceId == active);
        var neutral = !InputStateReducer.HasAnyLevel(keyboard, context)
            && controllers.Where(raw => raw.DeviceId == active).All(raw => !InputStateReducer.HasAnyLevel(raw, context)) && !_dragging;
        var wasOpen = _worldGate.IsOpen;
        _worldGate.ObserveCompleteFrame(_worldFocused, WorldModal || WorldUiFocus || _worldLifecycleSession is not null, complete, neutral);
        if (!wasOpen || !_worldGate.IsOpen || _readyActorPlan is null || _readyMode != TerrainViewerMode.WorldDiagnostic)
        { _worldDrag = default; AdvanceWorldGrid(default); SettleWorldZoom(delta, false); RefreshReadyStatus(); return; }
        var actor = intents.Aggregate(default(Vec2), (sum, intent) => sum + intent.Movement);
        if (actor.Length > _worldSettings.ActorSensitivity) actor = actor.Normalized() * _worldSettings.ActorSensitivity;
        var pan = intents.Aggregate(_worldDrag, (sum, intent) => sum + intent.CameraPan);
        var actorActive = intents.Any(intent => intent.ActorActive);
        var cameraActive = intents.Any(intent => intent.CameraActive) || _dragging || _worldDrag.Length > 0;
        _worldDrag = default;
        if (!double.IsFinite(pan.X) || !double.IsFinite(pan.Y)) { QuarantineWorld(); return; }
        try
        {
            AdvanceWorldGrid(actor);
            var triggerHeld = controllers.Where(raw => raw.DeviceId == active)
                .Any(raw => InputStateReducer.EffectiveTrigger(raw.LeftTrigger) > 0 || InputStateReducer.EffectiveTrigger(raw.RightTrigger) > 0);
            // Zoom-only is neither pan nor a neutral opportunity to arm Manual
            // return. Actor/pan behavior otherwise keeps the existing contract.
            if (actorActive || cameraActive || !triggerHeld)
                _camera!.UpdateWorld(WorldAnchor, actor, pan, actorActive, cameraActive, WorldViewport);
            var drive = intents.Sum(intent => intent.ZoomDrive);
            var zoom = WorldZoomPolicy.Advance(_camera!.State.Zoom, drive, delta);
            if (zoom != _camera.State.Zoom) ApplyPreferredWorldZoom(zoom, true);
            SettleWorldZoom(delta, drive != 0);
            RefreshReadyStatus();
        }
        catch (TerrainViewerException exception) { PublishFailure(TerrainViewerMode.WorldDiagnostic, exception.Error); }
        catch (OverflowException) { PublishFailure(TerrainViewerMode.WorldDiagnostic, new(TerrainViewerErrorCode.ActorPlan, Stage: TerrainViewerFailureStage.ActorPlan)); }
    }
    private void PublishWorldSelection()
    {
        if (_readyActorPlan is null || _readyDiagnosticPlan?.IsWorldDiagnostic != true) return;
        if (_worldActor.Session is null) return;
        var input = _worldActor.Placement();
        var actor = _readyActorPlan.Select(input);
        var plan = _readyDiagnosticPlan.WithWorldActor(actor);
        _readyActorInput = input; _readyActorPlan = actor; _readyActor = actor.Current; _readyDiagnosticPlan = plan;
        _canvas!.Publish(plan, _cache);
    }
    private void AdvanceWorldGrid(Vec2 actor)
    {
        if (_worldActor.Session is not { } session || _readyActorPlan is null || _readyMode != TerrainViewerMode.WorldDiagnostic) return;
        var time = WorldTime;
        var direction = _worldResetPending || _worldReloadPending ? null : DiagnosticGridActor.Resolve(actor, _worldSettings.InvertActorX, _worldSettings.InvertActorY);
        _worldGridInput.Observe(session, time, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral);
        if (_worldGridInput.CompleteFrontier is not { } frontier) return;
        var advance = _worldActor.Advance(frontier, _worldGridInput.CompleteThrough(frontier), input => _readyActorPlan.Select(input));
        if (advance.Movement.Status != MovementStatus.Completed)
            throw new InvalidOperationException($"GRID_RUNTIME_{advance.Movement.Status}_{advance.Movement.Failure?.Kind}");
        _worldGridInput.PublishedThrough(frontier);
        foreach (var evidence in advance.Evidence)
        {
            var assessment = evidence.Assessment;
            _worldGridDiagnostic = $"{assessment.Coverage}/{assessment.Disposition} {assessment.ReasonCode} records={assessment.RecordAssessments.Count} gaps={assessment.Gaps.Count} epoch={session.Snapshot.EvidenceEpoch}";
            // Numeric, path-free production diagnostic evidence; no archive bytes/pixels.
            GD.Print("ARC-WORLD-003 evidence=" + System.Text.Json.JsonSerializer.Serialize(new {
                evidence.Key, coverage=assessment.Coverage.ToString(), disposition=assessment.Disposition?.ToString(),
                assessment.ReasonCode, records=assessment.RecordAssessments.Count, gaps=assessment.Gaps.Count,
                selectedEvidenceSha256=assessment.StepEvidence.SelectedEvidenceSha256
            }));
        }
        foreach (var item in advance.Movement.Events)
            GD.Print("ARC-WORLD-003 event=" + System.Text.Json.JsonSerializer.Serialize(item));
        PublishWorldSelection();
        if (ReferenceEquals(_worldLifecycleSession, session) && session.State.Phase == MovementPhase.Settled)
        {
            if (_worldReloadPending && _arguments is not null)
            {
                RetireWorldLifecycle();
                _lastLoad = StartLoadAsync(_arguments, false);
            }
            else
            {
                if (_worldReloadPending) throw new InvalidOperationException("GRID_RELOAD_ARGUMENTS_REQUIRED");
                RetireWorldLifecycle();
                _worldActor.Configure(session.Snapshot, time); _worldGridInput = new(); _worldGate.Close();
                PublishWorldSelection(); _camera?.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
                GD.Print("ARC-WORLD-003 lifecycle=explicit-settled-reset");
            }
        }
    }
    private void InputWorld(InputEvent input) => WorldObservation(() => InputWorldObserved(input));
    private void InputWorldObserved(InputEvent input)
    {
        var wasModal = WorldModal;
        var raw = _worldAdapter.Map(input, _worldFocused);
        if (input is InputEventMouseButton)
        {
            // The shared adapter's mouse Confirm/Cancel is historical lab
            // behavior. WORLD mouse controls belong to native GUI dispatch;
            // retain real keyboard levels/provenance, never counterfeit a
            // semantic activation on the previously focused control.
            var keyboard = _worldAdapter.SampleKeyboard(_worldFocused);
            raw = raw with { Confirm = keyboard.Confirm, Cancel = keyboard.Cancel };
        }
        if (raw.DeviceId < 0) raw = WorldKeyboardRouting(raw);
        var intent = _worldReducer.Reduce(raw, WorldContext());
        if (input is not InputEventMouse) HandleWorldPulse(intent, _worldReducer.ActiveDevice is null || raw.DeviceId == _worldReducer.ActiveDevice);
        CaptureWorldCallbackActor(raw, intent);
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
            if (_worldGate.IsOpen && !overHud && button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                StepPreferredWorldZoom(button.ButtonIndex == MouseButton.WheelUp ? 1.25 : .8);
        }
        else if (input is InputEventMouseMotion motion && _dragging && !overHud) _worldDrag += new Vec2(motion.Relative.X, motion.Relative.Y) * (1 / (_camera?.State.Zoom ?? 1));
        else if (input is InputEventKey key && key.Pressed && !key.Echo && GetViewport().GuiGetFocusOwner() is null)
        {
            if (key.Keycode == Key.R) ResetWorldScene();
            else if (key.Keycode == Key.F) FitWorldView();
            else if (key.Keycode == Key.L && _arguments is not null) RequestWorldLifecycle(true);
            else if (_worldGate.IsOpen && key.Keycode is Key.Plus or Key.Minus) StepPreferredWorldZoom(key.Keycode == Key.Plus ? 1.25 : .8);
            else if (key.Keycode == Key.F11) GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
            else if (key.Keycode == Key.Escape && GetWindow().Mode == Window.ModeEnum.Fullscreen) GetWindow().Mode = Window.ModeEnum.Windowed;
        }
        RefreshReadyStatus();
    }
    private void CaptureWorldCallbackActor(RawInputFrame raw, IntentFrame reduced)
    {
        if (_worldActor.Session is not { } session) return;
        var actor = default(Vec2);
        if (_worldGate.IsOpen && _worldFocused && !WorldModal && !WorldUiFocus && _worldLifecycleSession is null)
        {
            // Reduce the other domain too; a mouse/keyboard event must not
            // release the active left stick. Timing/pulses remain callback-only.
            actor = raw.DeviceId < 0 ? reduced.Movement
                : _worldReducer.Reduce(WorldKeyboardRouting(_worldAdapter.SampleKeyboard(_worldFocused)), WorldContext()).Movement;
            var selected = _worldAdapter.SampleController(_worldReducer.ActiveDevice, _worldFocused);
            if (selected is not null)
                actor += raw.DeviceId == _worldReducer.ActiveDevice ? reduced.Movement
                    : _worldReducer.Reduce(selected, WorldContext()).Movement;
            if (actor.Length > _worldSettings.ActorSensitivity) actor = actor.Normalized() * _worldSettings.ActorSensitivity;
        }
        var direction = DiagnosticGridActor.Resolve(actor, _worldSettings.InvertActorX, _worldSettings.InvertActorY);
        _worldGridInput.Observe(session, WorldTime, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral);
    }
    internal void WorldFrameForSelfTest(RawInputFrame keyboard, IReadOnlyList<RawInputFrame> controllers, double delta, bool complete = true, SimulationTime? observationTime = null)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Simulation requires an explicit test launch.");
        // The script clock is independent of camera delta, including zero,
        // invalid and huge zoom deltas. Exact motion tests supply literal times.
        _worldTestTime = observationTime ?? new SimulationTime((_worldTestTime ?? _worldActor.Session?.State.Time ?? WorldTime).Value + ExactScalar.Create(1, 10));
        WorldObservation(() => ApplyWorldObservedFrame(delta, complete, keyboard, controllers, "SIMULATION complete-level frame (not physical evidence)"));
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
    internal SimulationTime WorldTimeForSelfTest => _worldTestTime ?? WorldTime;
    internal void WorldSettleForSelfTest(params RawInputFrame[] controllers)
    {
        // Bounded independent receipt observations, not camera-delta integration.
        // Live D=1/2 means eight 1/10-second receipts suffice for a fresh neutral
        // to seal, return/finish and close its settlement frontier.
        for (var i = 0; i < 8; i++) WorldFrameForSelfTest(new(-1, default), controllers, .1);
        if (_worldActor.Session?.State.Phase != MovementPhase.Settled)
            throw new InvalidOperationException("GRID_TEST_SETTLEMENT_REQUIRED");
    }
    internal void WorldZoomForSelfTest(double zoom)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        SetPreferredWorldZoom(zoom);
    }
    internal void WorldFitFollowForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        _camera!.Fit(WorldViewport);
        _camera.ResetWorld(WorldAnchor, WorldViewport, _camera.State.Zoom);
    }
    internal CameraFollowState WorldFollowForSelfTest => _camera!.FollowState;
    internal void WorldFocusForSelfTest(bool focused)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Simulation requires an explicit test launch.");
        _worldFocused = focused; QuarantineWorld();
    }
    private string WorldStatus()
    {
        var s = _worldActor.State;
        var zoomStatus = $" | World zoom {_camera!.State.Zoom:0.#####} ({(_worldFitView ? "Fit session view" : "camera ratio")}); preferred {_worldPreferredZoom:0.#####}; "
            + (_worldZoomSaveFailed ? "zoom save FAILED, session only" : _worldZoomPending ? "zoom save pending" : "zoom preference current")
            + $" | wanted center ({_camera.WantedCenter.X:0.###},{_camera.WantedCenter.Y:0.###}) bounded ({_camera.State.X:0.###},{_camera.State.Y:0.###}) bound limited {_camera.BoundLimited} | LT out / RT in";
        var profile = _worldActor.Profile;
        var binding = profile.Facing(s.Action, s.Facing);
        var runtime = _worldActor.Session?.State;
        return zoomStatus + $" | Grid committed={runtime?.CommittedCell} continuous anchor=({runtime?.Anchor.U},{runtime?.Anchor.V}) phase={runtime?.Phase} time={runtime?.Time.Value} step={runtime?.Segment?.Key.ProposedStepId} opportunity={runtime?.LastOpportunity?.OpportunityId} | small diagnostic test area x4..8/y0..3 sector1452,1290 spawn(6,1); movement may stop without visible obstacles; D=1/2s diagnostic NOT original speed | {_worldGridDiagnostic} | lifecycle={(_worldReloadPending ? "reload pending settled" : _worldResetPending ? "reset pending settled" : "active")} | Actor: {profile.Id} {s.Action} Frame: {_worldActor.CurrentFrame} | ART clock Idle6/Walk17 | Facing: octant{s.Facing} source{binding.SourceFacing} group{binding.PhysicalGroup} flip{binding.FlipX} | Camera: {_camera!.FollowState.Mode} | neutral gate {(_worldGate.IsOpen ? "open" : "quarantined")} | Controls WASD/left stick: eight screen sectors; actor-axis inversion separate from camera (F1); arrows/right stick pan; LT/RT zoom; F fit R reset L reload F1 settings | original motion/spawn/occlusion parity NOT_VERIFIED";
    }
    private void ExitWorld()
    {
        if (!_worldInitialized) return;
        // Consume this initialization before any callbacks. Retirement and tree
        // exit may both arrive; neither may disconnect another generation's
        // subscriptions or dispose the same session twice.
        _worldInitialized = false;
        try
        {
            FlushWorldZoom();
            QuarantineWorld();
            GD.Print($"ARC-WORLD-003 lifecycle=disposed phase={_worldActor.Session?.State.Phase} time={_worldActor.Session?.State.Time.Value} (not CenterArrived)");
        }
        finally
        {
            _worldActor.DisposeSession();
            RetireWorldLifecycle(); _worldGridInput = new();
            Input.JoyConnectionChanged -= WorldConnectionChanged;
            if (_hud is not null) _hud.SettingsRequested -= ToggleWorldSettings;
            if (_worldPanel is { } panel)
            {
                _worldPanel = null;
                panel.Changed -= SaveWorldSettings; panel.CloseRequested -= ToggleWorldSettings; panel.ResetRequested -= ResetWorldSettings;
                panel.ZoomRequested -= SetPreferredWorldZoom; panel.FitRequested -= FitWorldView;
                panel.Hide(); RemoveChild(panel); panel.QueueFree();
            }
        }
    }
}
