using System.Text.Json;
using GamepadLab.Core;
using Godot;
using Arcanum.Input;

namespace GamepadLab;

public partial class LabRoot : Node2D
{
    private readonly TargetingEngine _targeting = new(); private readonly InputStateReducer _input = new(); private readonly LabInputAdapter _adapter = new();
    private readonly MenuRepeat _menuRepeat = new(); private readonly MovementEngine _movement = new(); private readonly MovementPolicy _movementPolicy = new(); private readonly CameraFollowEngine _cameraFollow = new(); private readonly LabSettingsStore _settingsStore = new(); private readonly List<string> _log = new();
    private Node2D _world = null!; private Camera2D _camera = null!; private SyntheticCourtyard _courtyard = null!; private LabHud _hud = null!;
    private SelectionState _selection = new(); private SelectionResult? _result; private MovementState _actor = new(new Vec2(460, 610), new Vec2(1, 0));
    private LabSettings _settings = LabSettings.Default; private CameraFollowState _cameraState = new(); private Vec2 _cameraCenter; private bool _focused = true; private RawInputFrame _lastRaw = new(-1, default);
    private string _lastProvenance = "LIVE OS EVENT (not physical usability proof)"; private string _lastEvent = "startup"; private bool _lastAccepted;
    private readonly GamepadPresetReducer _presets = new();
    private readonly LabActionExecutor _actions = new();
    private PresetState _presetState = new(); private PresetStep? _presetStep;
    private long _epoch; private long _observationFrame;
    private bool _injectedObservations;
    private string? _editingSlot; private string? _editingBinding;
    private LabHighlightLayer _highlight = null!;

    public override void _Ready()
    {
        LabInputAdapter.EnsureInputMap(); _world = GetNode<Node2D>("WorldRoot"); _camera = GetNode<Camera2D>("Camera2D"); _courtyard = GetNode<SyntheticCourtyard>("WorldRoot/SyntheticCourtyard"); _hud = GetNode<LabHud>("CanvasLayer/LabHud");
        _settings = _settingsStore.Load(); _hud.SetScale(_settings.UiScalePercent); _input.SetStartupDevices(Input.GetConnectedJoypads()); ResetCameraCentered(); Input.JoyConnectionChanged += OnJoyConnectionChanged;
        _highlight = new LabHighlightLayer(); GetNode<CanvasLayer>("CanvasLayer").AddChild(_highlight);
        _actions.Boundary(_epoch); AddLog(_settingsStore.Diagnostic);
        SyncActor(); AddLog("Lab ready: synthetic-only scene; no original data. Default camera inversion is X=true, Y=true.");
        var args = OS.GetCmdlineUserArgs();
        if (GetViewport() is not SubViewport)
        {
            if (args.Contains("--self-test")) CallDeferred(nameof(RunSelfTest)); else if (args.Contains("--smoke")) CallDeferred(nameof(RunSmoke), args); else if (args.Contains("--persistence")) CallDeferred(nameof(RunPersistence), args); else if (args.Contains("--capture")) CallDeferred(nameof(RunCapture), args);
            else if (args.Contains("--preset-matrix")) CallDeferred(nameof(RunPresetMatrix), args);
        }
    }
    public override void _ExitTree() => Input.JoyConnectionChanged -= OnJoyConnectionChanged;

    public override void _Process(double delta)
    {
        _input.EndFrame(); RefreshCameraLayout(); RefreshSelection(); _observationFrame++;
        if (!_injectedObservations)
        {
            _adapter.RefreshKeyboardLevels(Input.IsKeyPressed);
            if (_input.ActiveDevice is int device && Input.GetConnectedJoypads().Contains(device))
                _adapter.RefreshControllerLevels(device, axis => Input.GetJoyAxis(device, axis), button => Input.IsJoyButtonPressed(device, button));
        }
        else CompleteInjectedObservation();
        var intents = new List<IntentFrame>(); var controller = _adapter.SampleController(_input.ActiveDevice, _focused, _observationFrame);
        if (controller is RawInputFrame joy) intents.Add(Route(_input.Reduce(joy, Context(delta, _hud.ModalVisible, true)) with { SourceDeviceId = joy.DeviceId }));
        intents.Add(Route(_input.Reduce(_adapter.SampleKeyboard(_focused, _observationFrame), Context(delta, _hud.ModalVisible, true))));
        if (!_hud.ModalVisible && _actions.Pending is null)
        {
            if (_actions.TurnBased)
                _actions.PreviewGrid(intents.Where(i => i.Accepted).Aggregate(default(Vec2), (v, i) => v + i.Movement), delta);
            else ApplyWorldIntents(intents.Where(i => i.Accepted), delta);
        }
        else if (_actions.Pending?.Kind == "ground")
            _actions.MoveGround(intents.Where(i => i.Accepted).Aggregate(default(Vec2), (v, i) => v + i.Movement) * (160 * delta));
        if (_hud.ModalVisible)
        {
            var menu = intents.LastOrDefault(i => i.Accepted && (i.MenuDirection != 0 || Math.Abs(i.MenuAnalog) > .45));
            var move = _menuRepeat.Update(menu?.MenuDirection ?? 0, menu?.MenuAnalog ?? 0, delta); if (move != 0) _hud.Navigate(move);
        }
        RefreshSelection(); _courtyard.SetSelected(_actions.SelectedTargetId ?? _result!.Id); UpdateHud();
        var highlight = _presetStep?.Highlight == true && !_hud.ModalVisible;
        var highlighted = highlight ? _actions.Eligible(_courtyard.WorldTargets) : Array.Empty<LabWorldTarget>();
        _courtyard.SetHighlighted(highlighted.Select(t => t.Id));
        _highlight.UpdateLabels(highlighted, _world, GetViewport().GetVisibleRect(), _settings.UiScalePercent);
    }

    public override void _Input(InputEvent input) => Handle(input, "LIVE OS EVENT (not physical usability proof)");
    private void Handle(InputEvent input, string provenance)
    {
        if (input is InputEventKey recenter && recenter.Pressed && recenter.Keycode == Key.F2) { Recenter(); GetViewport().SetInputAsHandled(); return; }
        var raw = _adapter.Map(input, _focused); _lastRaw = raw; _lastProvenance = provenance; _lastEvent = DescribeEvent(input);
        // Refresh real modifier levels for the selecting event without discarding a short event pulse.
        if (!_injectedObservations && input is InputEventJoypadButton button && Input.GetConnectedJoypads().Contains(button.Device))
        {
            _adapter.RefreshControllerLevels(button.Device, axis => Input.GetJoyAxis(button.Device, axis), b => b == button.ButtonIndex ? button.Pressed : Input.IsJoyButtonPressed(button.Device, b));
            raw = _adapter.SampleController(button.Device, _focused, -1)!;
        }
        var intent = _input.Reduce(raw, Context(GetProcessDeltaTime(), _hud.ModalVisible), provenance); _lastAccepted = intent.Accepted;
        if (!intent.Accepted) return;
        if (!_hud.ModalVisible && input is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            var picked = _courtyard.PickVisible(click.Position, _world); var ground = _world.MakeCanvasPositionLocal(click.Position);
            _selection = picked is null ? _targeting.PickGround(new Vec2(ground.X, ground.Y)) : _targeting.Pick(picked); RefreshSelection(); AddLog(picked is null ? "Mouse picked ground (targeting only)" : $"Mouse picked {picked}; manual lock.");
            _actions.Select(_courtyard.WorldTargets.FirstOrDefault(t => t.Id == picked));
            GetViewport().SetInputAsHandled(); return;
        }
        if (_hud.ModalVisible && input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left) _hud.SelectAt(mouse.Position);
        Route(intent);
        GetViewport().SetInputAsHandled();
    }

    private void ApplyWorldIntents(IEnumerable<IntentFrame> intents, double delta)
    {
        var sampled = intents.ToArray();
        var camera = sampled.Aggregate(default(Vec2), (total, intent) => total + intent.CameraPan);

        // Update movement once per frame even when neutral. This is essential: a
        // keyboard companion sample must not hide a controller release, and neutral
        // must clear an actor-relative goal rather than leave stale motion behind.
        var movement = sampled.Aggregate(default(Vec2), (total, intent) => total + intent.Movement);
        // Reducer output already carries the one linear actor-sensitivity multiplier.
        // A simultaneous keyboard/controller sample cannot bypass that configured cap,
        // but neither can this integration layer erase a 1.0–2.0 effort value.
        if (movement.Length > _settings.ActorSensitivity) movement = movement.Normalized() * _settings.ActorSensitivity;
        var zoom = LabZoomPolicy.Apply(_camera.Zoom.X, sampled.Sum(i => i.ZoomDrive), delta); _camera.Zoom = new Vector2((float)zoom, (float)zoom);
        var step = _movement.Update(_actor, movement, delta, _settings.MovementMode, _movementPolicy,
            (candidate, radius) => _courtyard.CanOccupy(ToGodot(candidate), (float)radius));
        var wasBlocked = _actor.Outcome == MovementOutcome.Blocked;
        _actor = step.State; SyncActor();
        if (_presetState.WheelOpen || _presetState.AwaitingCameraNeutral || _cameraHandoffObservation)
        {
            if (_cameraState.Mode == CameraMode.Follow) { _cameraCenter = CameraBounds().ClampCenter(_actor.Position); ApplyCameraCenter(); }
        }
        else ApplyCameraPolicy(movement, camera);
        _cameraHandoffObservation = false;
        if (!wasBlocked && _actor.Outcome == MovementOutcome.Blocked) AddLog("Actor blocked; intent latched until neutral or direction changes.");
    }
    private void ActivateModal()
    {
        var choice = _hud.Activate(); if (choice == "" || choice == "Close" || choice == "Cancel") { CloseModal(); return; }
        if (ActivatePresetSurface(choice)) return;
        if (_hud.ModalKind == ModalKind.Context)
        {
            if (choice == "Inspect") AddLog(_result!.IsGround ? "inspect ground (synthetic)" : $"inspect {_result.Id} (synthetic)");
            else AddLog($"{choice} {_result?.Id ?? "ground"} (synthetic)");
            AddLog($"Menu choice: {choice}"); CloseModal(); return;
        }
        var next = (choice switch
        {
            "Camera deadzone −" => _settings with { CameraDeadzone = _settings.CameraDeadzone - .05 },
            "Camera deadzone +" => _settings with { CameraDeadzone = _settings.CameraDeadzone + .05 },
            "Actor deadzone −" => _settings with { ActorDeadzone = _settings.ActorDeadzone - .05 },
            "Actor deadzone +" => _settings with { ActorDeadzone = _settings.ActorDeadzone + .05 },
            "Camera sensitivity −" => _settings with { CameraSensitivity = _settings.CameraSensitivity - .1 },
            "Camera sensitivity +" => _settings with { CameraSensitivity = _settings.CameraSensitivity + .1 },
            "Actor sensitivity −" => _settings with { ActorSensitivity = _settings.ActorSensitivity - .1 },
            "Actor sensitivity +" => _settings with { ActorSensitivity = _settings.ActorSensitivity + .1 },
            "Toggle invert X" => _settings with { InvertCameraX = !_settings.InvertCameraX },
            "Toggle invert Y" => _settings with { InvertCameraY = !_settings.InvertCameraY },
            "Toggle comparison mode" => _settings with { MovementMode = _settings.MovementMode == MovementMode.DirectIntent ? MovementMode.NearbyGoal : MovementMode.DirectIntent },
            "Reset defaults" => LabSettings.Default,
            _ => _settings
        }).Validated();
        if (choice == "Recenter camera") ResetCameraCentered();
        // One committed save path: an I/O failure keeps the previous preferences in memory and on disk.
        SavePreferences(next);
        if (choice == "Reset defaults") { ClearWorldIntents(ReferenceEquals(_settings, next) ? "Defaults reset (inverted X/Y); positions/mode/zoom preserved." : "Defaults reset not saved; previous preferences retained."); }
        else if (ReferenceEquals(_settings, next)) AddLog($"Saved setting: {choice}");
    }
    private void CloseModal() { _hud.HideModal(); ClearWorldIntents("Modal closed; both sticks require neutral."); }
    private void ClearWorldIntents(string log) { CancelMovement(null); _input.Quarantine(); _cameraState = _cameraState with { RecenterArmed = false }; _menuRepeat.Clear(); _actions.Boundary(++_epoch); _presetState = new(ContextEpoch: _epoch, NextEdgeId: _presetState.NextEdgeId); AddLog(log); }
    private void CancelMovement(string? reason)
    {
        _actor = _actor with { Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Idle, BlockedIntent = null };
        // macOS can send a focus notification while this node is entering the tree,
        // before child references are ready. State still clears; drawing waits.
        if (_courtyard is not null) SyncActor();
        if (reason is not null) AddLog($"Movement cancelled: {reason}.");
    }
    private void Recenter() { _courtyard.RecenterActor(); _actor = new MovementState(new Vec2(_courtyard.ActorPosition.X, _courtyard.ActorPosition.Y), new Vec2(1, 0)); ResetCameraCentered(); AddLog("Actor and camera recentered (F2). "); }
    private void SyncActor() => _courtyard.SetActor(ToGodot(_actor.Position), ToGodot(_actor.Heading), _actor.Goal is Vec2 goal ? ToGodot(goal) : null);
    private CameraBounds CameraBounds() { var map = SyntheticCourtyard.CameraMapBounds; var viewport = GetViewport().GetVisibleRect(); return new CameraBounds(new(map.Position.X, map.Position.Y), new(map.End.X, map.End.Y), new(viewport.Size.X, viewport.Size.Y), _camera.Zoom.X); }
    private void ResetCameraCentered() { var step = _cameraFollow.ResetCentered(_actor.Position, CameraBounds()); _cameraState = step.State; _cameraCenter = step.Center; ApplyCameraCenter(); }
    private void ApplyCameraPolicy(Vec2 actorInput, Vec2 worldImagePan) { var step = _cameraFollow.Update(_cameraState, _actor.Position, _cameraCenter, actorInput, worldImagePan, CameraBounds()); _cameraState = step.State; _cameraCenter = step.Center; ApplyCameraCenter(); }
    private void ApplyCameraCenter() { _world.Position = _camera.Position - ToGodot(_cameraCenter); }
    private void RefreshCameraLayout() { _cameraCenter = CameraBounds().ClampCenter(_cameraCenter); ApplyCameraCenter(); }
    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected) { var active = _input.ActiveDevice == device; _input.Disconnect((int)device); _adapter.ForgetDevice((int)device); if (active) ClearWorldIntents($"Active controller {device} disconnected."); }
        else AddLog($"Controller {(int)device} connected; neutral sample required after activation.");
    }
    public override void _Notification(int what)
    {
        base._Notification(what);
        // Explicit simulation owns its lifecycle timeline as well as its levels.
        // Live/ordinary use still dispatches every native focus notification.
        if (!_injectedObservations && what == NotificationApplicationFocusOut) ApplyFocus(false);
        if (!_injectedObservations && what == NotificationApplicationFocusIn) ApplyFocus(true);
    }
    private void ApplyFocus(bool focused)
    {
        _focused = focused; _input.FocusChanged(focused);
        if (!focused) _adapter.ClearKeyboard();
        ClearWorldIntents(focused ? "Focus regained: selected controller must be genuinely neutral." : "Focus lost: all pending/chord/world domains cleared.");
    }

    public async void RunSelfTest()
    {
        try
        {
            _injectedObservations = true;
            VerificationMode.Require(_settingsStore.UsesIsolatedTestStorage, "self-test requires isolated storage");
            foreach (var action in new[] { "lab_actor_move", "lab_camera_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" }) VerificationMode.Require(InputMap.HasAction(action) && InputMap.ActionGetEvents(action).Count > 0, $"missing mapping {action}");
            var saved = _settings; var initialActor = _actor.Position; var initialWorld = _world.Position; var initialCameraCenter = _cameraCenter; var initialCameraState = _cameraState;
            var initialSize = GetWindow().Size; var initialScaleSize = GetWindow().ContentScaleSize; var initialZoom = _camera.Zoom;

            // Retain the GPAD001 scene-level projection and responsive-layout checks.
            // These deliberately traverse actual Godot canvas transforms rather than
            // duplicating target selection in the Core harness.
            foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1920, 1080) })
            {
                GetWindow().Size = size; await LayoutFrames();
                // The macOS headless surface can cap a requested 1920x1080 window to
                // the available framebuffer. Reflow against its actual accepted size;
                // on an unconstrained display this remains the requested size.
                var visible = GetViewport().GetVisibleRect(); GetWindow().Size = new Vector2I((int)visible.Size.X, (int)visible.Size.Y); GetWindow().ContentScaleSize = new Vector2I((int)visible.Size.X, (int)visible.Size.Y); await LayoutFrames(); _hud.ShowSettings(); await LayoutFrames();
                VerificationMode.Require(_hud.HasReadableLayout(GetViewport().GetVisibleRect()), $"responsive HUD bounds failed at requested {size}");
                VerificationMode.Require(_hud.ReticleCanvasCenter.DistanceTo(GetViewport().GetVisibleRect().GetCenter()) < .1f, $"reticle is not centered at {size}");
                _hud.HideModal();
                // Projection/reticle regression intentionally uses an accepted
                // synthetic Manual camera center. Follow now centers the actor, so
                // the old named crate is not guaranteed to be inside the reticle's
                // semantic radius at every zoom; target semantics remain viewport-
                // based rather than actor-based.
                _cameraState = new CameraFollowState(CameraMode.Manual, true, false);
                foreach (var zoom in new[] { .5f, 1f, 2f })
                {
                    _camera.Zoom = new Vector2(zoom, zoom); await LayoutFrames();
                    _cameraCenter = CameraBounds().ClampCenter(new Vec2(620, 480)); ApplyCameraCenter();
                    var crate = _courtyard.Candidates(_world).First(candidate => candidate.Id == "crate");
                    Handle(new InputEventMouseButton { Position = ToGodot(crate.Anchor), ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation);
                    VerificationMode.Require(_selection.ManualLock && _result!.Id == "crate", $"canvas target pick failed at {size} zoom {zoom}");
                    var miss = new Vector2(24, 24); var expectedGround = _world.MakeCanvasPositionLocal(miss);
                    Handle(new InputEventMouseButton { Position = miss, ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation);
                    VerificationMode.Require(_result!.IsGround && _result.GroundPoint == new Vec2(expectedGround.X, expectedGround.Y), $"canvas ground conversion failed at {size} zoom {zoom}");
                }
            }
            _camera.Zoom = initialZoom; GetWindow().Size = initialSize; GetWindow().ContentScaleSize = initialScaleSize; _cameraCenter = initialCameraCenter; _cameraState = initialCameraState; _world.Position = initialWorld; await LayoutFrames();

            // Same-frame meaningful device reports must choose the lowest id. Do not
            // pre-pin the selected controller: verify the real candidate arbitration.
            _input.SetStartupDevices(Array.Empty<int>()); _input.Reset(); _adapter.Clear();
            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation);
            _Process(0); VerificationMode.Require(_input.ActiveDevice == 3, "same-frame controller arbitration did not select lowest id");
            _Process(0); _Process(0);
            var arbitrationActor = _actor.Position; var arbitrationWorld = _world.Position; var arbitrationLog = _log.Count;
            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation);
            Handle(new InputEventJoypadButton { Device = 5, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == arbitrationActor && _world.Position == arbitrationWorld && _log.Count == arbitrationLog, "non-active controller leaked actor, camera, or action input");
            Handle(new InputEventJoypadButton { Device = 5, ButtonIndex = JoyButton.A, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(!_courtyard.CanOccupy(new Vector2(690, 500), (float)_movementPolicy.ActorRadius), "scene wall collision was not active");
            VerificationMode.Require(!_courtyard.CanOccupy(new Vector2(815, 275), (float)_movementPolicy.ActorRadius), "scene corner collision was not active");
            VerificationMode.Require(_courtyard.CanOccupy(new Vector2(950, 580), (float)_movementPolicy.ActorRadius), "scene narrow passage was unexpectedly closed");
            _settingsStore.Save(_settings with { InvertCameraX = false }); VerificationMode.Require(!_settingsStore.Load().InvertCameraX, "user-local settings did not persist/reload");
            _settingsStore.Save(LabSettings.Default); VerificationMode.Require(_settingsStore.Load() == LabSettings.Default, "reset defaults did not persist/restart with inverted axes"); _settingsStore.Save(saved);
            // Self-test injects device 3 through the actual adapter. Pin it only for
            // this synthetic harness, then enforce the same two-frame neutral rearm.
            _input.SetStartupDevices(new[] { 3 }); _input.Reset();
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            // Actual scene characterization of the camera sign. GPAD001's positive
            // translated-world baseline rendered stick-right/down as image-right/down.
            // Per the owner amendment, true retains that visual sign and false
            // reverses its own axis; this does not restore the former left-stick pan.
            foreach (var axes in new[] { (true, true), (true, false), (false, true), (false, false) })
            {
                _settings = saved with { InvertCameraX = axes.Item1, InvertCameraY = axes.Item2 };
                ResetCameraCentered(); _input.Quarantine();
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation);
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
                var imageBefore = _world.Position;
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation);
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightY, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
                var imageDelta = _world.Position - imageBefore;
                VerificationMode.Require(Math.Sign(imageDelta.X) == (axes.Item1 ? 1 : -1) && Math.Sign(imageDelta.Y) == (axes.Item2 ? 1 : -1), $"camera image sign did not match owner mapping X={axes.Item1}, Y={axes.Item2}");
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation);
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            }
            _settings = saved;
            // Keep the concurrent-stick readback away from the left camera bound;
            // edge clamping is covered separately by the Core transition table.
            _actor = new MovementState(new Vec2(560, 610), new Vec2(1, 0)); SyncActor(); ResetCameraCentered();
            var actorBefore = _actor.Position; var worldBefore = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position.X > actorBefore.X && _cameraState.Mode == CameraMode.Follow && Math.Abs(_world.Position.X - (_camera.Position.X - _actor.Position.X)) < .1, "left stick did not move actor with Follow camera tracking");
            var followWorld = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_world.Position.X > followWorld.X && _actor.Position.X > actorBefore.X && _cameraState.Mode == CameraMode.Manual, "right stick did not pan camera image independently with Manual priority");
            var combinedActor = _actor.Position; var combinedCenter = _cameraCenter; _Process(.1);
            VerificationMode.Require(_actor.Position.X > combinedActor.X && _cameraState.Mode == CameraMode.Manual && _cameraCenter.X < combinedCenter.X, "both sticks did not run concurrently");
            var heldManualWorld = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_cameraState.Mode == CameraMode.Manual && _world.Position == heldManualWorld, "right release while left remains held snapped or left Manual");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
            VerificationMode.Require(_cameraState.Mode == CameraMode.Manual && _cameraState.RecenterArmed, "both neutral samples did not arm Manual recenter");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_cameraState.Mode == CameraMode.Follow && _actor.Position.X > combinedActor.X && _world.Position.X < heldManualWorld.X, "new actor-only input did not resume Follow after neutral");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
            _hud.ShowSettings(); ClearWorldIntents("SIMULATION modal gate"); var modalActor = _actor.Position; var modalWorld = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == modalActor && _world.Position == modalWorld, "modal leaked either world intent"); CloseModal();
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            ApplyFocus(false); var focusActor = _actor.Position; var focusWorld = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == focusActor && _world.Position == focusWorld, "focus loss retained a stick intent"); ApplyFocus(true);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            var contextSettings = _settings; var contextLog = _log.Count;
            _hud.ShowContext("Talk"); ActivateModal();
            VerificationMode.Require(!_hud.ModalVisible && _log.Skip(contextLog).Any(line => line.Contains("Talk", StringComparison.Ordinal)) && _settings == contextSettings && _settingsStore.Load() == contextSettings, "context primary action was not dispatched without settings write");
            _hud.ShowContext("Talk"); _hud.Navigate(1); contextLog = _log.Count; ActivateModal();
            VerificationMode.Require(!_hud.ModalVisible && _log.Skip(contextLog).Any(line => line.Contains("inspect", StringComparison.OrdinalIgnoreCase)) && _settingsStore.Load() == contextSettings, "context Inspect was not dispatched without settings write");
            _hud.ShowContext("Talk"); _hud.Navigate(2); ActivateModal();
            VerificationMode.Require(!_hud.ModalVisible && _settingsStore.Load() == contextSettings, "context Cancel wrote settings or remained visible");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);

            // GPAD001 provenance and modal-repeat regressions through the actual
            // adapter: held input never oscillates a fresh keyboard method/prompt,
            // and menus consume D-pad/left-stick input without moving either world
            // domain.
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_input.Method == InputMethod.Gamepad && _hud.PromptText.Contains("[A]", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("Method: Gamepad", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("JoyAxis", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("accepted: True", StringComparison.Ordinal), "qualified controller input did not set rendered gamepad provenance/prompt/diagnostics");
            PressKey(Key.D); ReleaseKey(Key.D); _Process(0); VerificationMode.Require(_input.Method == InputMethod.KeyboardMouse && _hud.PromptText.Contains("WASD", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("Method: KeyboardMouse", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("Key D released", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("accepted: True", StringComparison.Ordinal), "fresh keyboard event did not update rendered keyboard provenance/prompt/diagnostics");
            _Process(.1); VerificationMode.Require(_input.Method == InputMethod.KeyboardMouse && _hud.DiagnosticsText.Contains("Method: KeyboardMouse", StringComparison.Ordinal), "held controller input oscillated rendered keyboard provenance");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1); VerificationMode.Require(_input.Method == InputMethod.Gamepad && _hud.DiagnosticsText.Contains("Method: Gamepad", StringComparison.Ordinal) && _hud.DiagnosticsText.Contains("accepted: True", StringComparison.Ordinal), "released/requalified controller input did not restore rendered gamepad provenance");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);

            PressKey(Key.F1); ReleaseKey(Key.F1); VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "settings did not open through adapter");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            var settingsIndex = _hud.SelectedIndex; var menuActor = _actor.Position; var menuWorld = _world.Position;
            PressKey(Key.Down); _Process(0); VerificationMode.Require(_hud.SelectedIndex == (settingsIndex + 1) % _hud.OptionCount, "adapter digital menu repeat did not move immediately"); _Process(.34); VerificationMode.Require(_hud.SelectedIndex == (settingsIndex + 1) % _hud.OptionCount, "adapter digital menu repeated too early"); _Process(.01); VerificationMode.Require(_hud.SelectedIndex == (settingsIndex + 2) % _hud.OptionCount, "adapter digital menu repeat missed threshold"); ReleaseKey(Key.Down); _Process(0);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = .65f }, VerificationMode.Simulation); _Process(0); var analogIndex = _hud.SelectedIndex; _Process(.35); VerificationMode.Require(_hud.SelectedIndex != analogIndex, "adapter analog menu repeat did not advance");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = .45f }, VerificationMode.Simulation); _Process(0); var releasedIndex = _hud.SelectedIndex; _Process(.2); VerificationMode.Require(_hud.SelectedIndex == releasedIndex, "adapter analog menu repeat did not release");
            VerificationMode.Require(_actor.Position == menuActor && _world.Position == menuWorld, "modal menu routing leaked actor or camera movement");
            PressKey(Key.Escape); ReleaseKey(Key.Escape); VerificationMode.Require(!_hud.ModalVisible, "settings cancel through adapter did not close modal");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            PressKey(Key.C); ReleaseKey(Key.C); VerificationMode.Require(_hud.ModalKind == ModalKind.Context, "context did not open through adapter");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            var contextAdapterSettings = _settingsStore.Load(); var contextAdapterLog = _log.Count;
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.DpadDown, Pressed = true }, VerificationMode.Simulation); _Process(0); Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.DpadDown, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(_hud.SelectedOption == "slot-02", "full-ten adapter navigation did not select literal slot02");
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(_hud.ModalKind == ModalKind.Slot && _hud.SelectedOption == "Use", "slot detail did not open through fixed modal A");
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(!_hud.ModalVisible && _actions.Journal.Last().SlotId == "slot-02" && _settingsStore.Load() == contextAdapterSettings, "slot02 use through adapter wrote settings or lost stable ID");
            _actions.Cancel(_presetState.NextEdgeId);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);

            // Measure actual scene displacement after adapter/reducer processing. The
            // actor effort is deliberately checked at both ends of the settings range
            // for both control sources and both comparison modes.
            foreach (var mode in new[] { MovementMode.DirectIntent, MovementMode.NearbyGoal })
            {
                double ControllerDistance(double sensitivity)
                {
                    _settings = saved with { MovementMode = mode, ActorSensitivity = sensitivity }; Recenter(); var start = _actor.Position;
                    Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); _Process(.1);
                    var distance = _actor.Position.X - start.X; Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0); return distance;
                }
                double KeyboardDistance(double sensitivity)
                {
                    _settings = saved with { MovementMode = mode, ActorSensitivity = sensitivity }; Recenter(); var start = _actor.Position;
                    PressKey(Key.D); _Process(.1); ReleaseKey(Key.D); _Process(0); return _actor.Position.X - start.X;
                }
                var controllerLow = ControllerDistance(.5); var controllerHigh = ControllerDistance(2);
                var keyboardLow = KeyboardDistance(.5); var keyboardHigh = KeyboardDistance(2);
                VerificationMode.Require(Math.Abs(controllerHigh / controllerLow - 4) < .001, $"{mode} controller sensitivity did not change scene displacement linearly");
                VerificationMode.Require(Math.Abs(keyboardHigh / keyboardLow - 4) < .001, $"{mode} keyboard sensitivity did not change scene displacement linearly");
            }

            // Exercise the default policy through the actual adapter/scene with a
            // deliberately long .5s frame and its five .1s equivalent. The Nearby
            // marker must not cap this movement or erase the 0.5/2.0 ratio.
            double NearbyDistance(double sensitivity, IReadOnlyList<double> deltas)
            {
                _settings = saved with { MovementMode = MovementMode.NearbyGoal, ActorSensitivity = sensitivity };
                _actor = new MovementState(new Vec2(300, 680), new Vec2(1, 0)); SyncActor(); var start = _actor.Position;
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation);
                foreach (var frameDelta in deltas) _Process(frameDelta);
                var distance = _actor.Position.X - start.X;
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
                return distance;
            }
            var nearbyLowLarge = NearbyDistance(.5, new[] { .5 }); var nearbyLowSplit = NearbyDistance(.5, new[] { .1, .1, .1, .1, .1 });
            var nearbyHighLarge = NearbyDistance(2, new[] { .5 }); var nearbyHighSplit = NearbyDistance(2, new[] { .1, .1, .1, .1, .1 });
            VerificationMode.Require(Math.Abs(nearbyLowLarge - nearbyLowSplit) < .001 && Math.Abs(nearbyHighLarge - nearbyHighSplit) < .001, "default Nearby Goal scene movement changed with frame partition");
            VerificationMode.Require(Math.Abs(nearbyHighLarge / nearbyLowLarge - 4) < .001, "default Nearby Goal scene effort ratio was not 4:1 through a long frame");

            // This is deliberately adapter/scene-level rather than a second Core
            // calculation: Direct immediately applies a changed left-stick direction,
            // while Nearby Goal continues toward its stored marker until its explicit
            // cadence/hysteresis gate permits a fresh actor-relative destination.
            _settings = saved with { MovementMode = MovementMode.DirectIntent, ActorSensitivity = 1 };
            _actor = new MovementState(new Vec2(300, 680), new Vec2(1, 0)); SyncActor(); var directTurnStart = _actor.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); _Process(.05);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 1 }, VerificationMode.Simulation); _Process(.05);
            VerificationMode.Require(_actor.Position.Y > directTurnStart.Y && _actor.Heading.Y > .99, "Direct Intent scene adapter did not apply changed direction immediately");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);

            _settings = saved with { MovementMode = MovementMode.NearbyGoal, ActorSensitivity = 1 };
            _actor = new MovementState(new Vec2(300, 680), new Vec2(1, 0)); SyncActor(); var nearbyTurnStart = _actor.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); _Process(.05);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 1 }, VerificationMode.Simulation); _Process(.05);
            VerificationMode.Require(_actor.Position.X > nearbyTurnStart.X && _actor.Position.Y == nearbyTurnStart.Y && _actor.Goal is not null, "Nearby Goal scene adapter did not retain its stored destination before cadence");
            _Process(.1); VerificationMode.Require(_actor.Position.Y > nearbyTurnStart.Y && _actor.Heading.Y > .99, "Nearby Goal scene adapter did not refresh its destination after cadence/hysteresis");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);

            // Drive the visible actor through the replaceable courtyard callback: each
            // mode must stop at wall/corner sweeps and pass the centered narrow gap.
            foreach (var mode in new[] { MovementMode.DirectIntent, MovementMode.NearbyGoal })
            {
                _settings = saved with { MovementMode = mode, ActorSensitivity = 1 };
                _actor = new MovementState(new Vec2(620, 610), new Vec2(1, 0)); SyncActor();
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); _Process(.5);
                VerificationMode.Require(_actor.Outcome == MovementOutcome.Blocked && _actor.Position.X < 675 - _movementPolicy.ActorRadius && _courtyard.CanOccupy(ToGodot(_actor.Position), (float)_movementPolicy.ActorRadius), $"{mode} actor tunnelled through wall instead of retaining a safe position");
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);

                _actor = new MovementState(new Vec2(760, 420), new Vec2(1, -1).Normalized()); SyncActor();
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .9f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = -.9f }, VerificationMode.Simulation); _Process(1.3);
                VerificationMode.Require(_actor.Outcome == MovementOutcome.Blocked && _courtyard.CanOccupy(ToGodot(_actor.Position), (float)_movementPolicy.ActorRadius), $"{mode} actor did not stop safely at the corner sweep");
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);

                _actor = new MovementState(new Vec2(950, 480), new Vec2(0, 1)); SyncActor();
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 1 }, VerificationMode.Simulation); for (var frame = 0; frame < 8; frame++) _Process(.1);
                VerificationMode.Require(_actor.Position.Y > 656 && _actor.Outcome != MovementOutcome.Blocked && _courtyard.CanOccupy(ToGodot(_actor.Position), (float)_movementPolicy.ActorRadius), $"{mode} actor failed to traverse centered narrow passage to its far side");
                Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            }
            _settings = _settings with { MovementMode = MovementMode.NearbyGoal };
            Recenter(); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); for (var step = 0; step < 20; step++) _Process(.1);
            VerificationMode.Require(_actor.Position.X < 675 - _movementPolicy.ActorRadius, "actor crossed synthetic wall"); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); Recenter();
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); for (var step = 0; step < 20; step++) _Process(.1);
            VerificationMode.Require(_actor.Outcome == MovementOutcome.Blocked && _cameraState.Mode == CameraMode.Follow && _cameraCenter == _actor.Position, "blocked held actor intent did not retain Follow at the actual actor position"); var heldBlockedReplans = _actor.ReplanCount; for (var step = 0; step < 8; step++) _Process(.1);
            VerificationMode.Require(_actor.ReplanCount == heldBlockedReplans && _cameraState.Mode == CameraMode.Follow && _cameraCenter == _actor.Position, "held blocked stick replanned or changed Follow without a bound");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); VerificationMode.Require(_actor.Goal is null && _actor.Outcome == MovementOutcome.Idle, "left-stick release did not clear pending goal");
            Recenter(); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1); var cancelActor = _actor.Position; var cancelWorld = _world.Position;
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.B, Pressed = true }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == cancelActor && _world.Position == cancelWorld && _actor.Goal is null, "cancel while both sticks are held did not quarantine both world domains");
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.B, Pressed = false }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1); VerificationMode.Require(_actor.Position.X > cancelActor.X, "neutral rearm did not permit safe movement resume after cancel");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
            // Exercise the adapter's active-device disconnect path while both world
            // domains are held. Subsequent frames must contain no stale action or
            // movement after OnJoyConnectionChanged forgets that device.
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            OnJoyConnectionChanged(3, false); var disconnectedActor = _actor.Position; var disconnectedWorld = _world.Position; var disconnectLog = _log.Count; _Process(.1);
            VerificationMode.Require(_input.ActiveDevice is null && _actor.Position == disconnectedActor && _world.Position == disconnectedWorld && _actor.Goal is null && _actor.BlockedIntent is null && _log.Count == disconnectLog, "active disconnect retained stale device, movement, goal, or action");
            _settings = saved; _world.Position = initialWorld; _actor = new MovementState(initialActor, new Vec2(1, 0)); SyncActor(); AddLog("SIMULATION self-test passed: independent sticks, persistence, modal/lifecycle gates and obstacle collision."); GD.Print("GamepadLab Godot self-test: PASS (SIMULATION)"); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    public void RunSmoke(string[] args)
    {
        var index = Array.IndexOf(args, "--smoke"); if (index < 0 || index == args.Length - 1) { GetTree().Quit(1); return; }
        try
        {
            // The smoke is repeatable even after a previous local run changed a
            // preference; it then proves an actual non-default write and reset.
            _injectedObservations = true;
            VerificationMode.Require(_settingsStore.UsesIsolatedTestStorage, "smoke requires isolated storage");
            _settings = LabSettings.Default; _settingsStore.Save(_settings);
            var actorBefore = _actor.Position; Handle(new InputEventKey { Keycode = Key.D, Pressed = true }, VerificationMode.Simulation); _Process(.1); Handle(new InputEventKey { Keycode = Key.D, Pressed = false }, VerificationMode.Simulation);
            var worldBefore = _world.Position; Handle(new InputEventKey { Keycode = Key.Right, Pressed = true }, VerificationMode.Simulation); _Process(.1); Handle(new InputEventKey { Keycode = Key.Right, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(_actor.Position.X > actorBefore.X && _world.Position.X > worldBefore.X, "smoke did not prove actor/camera keyboard separation");
            PressKey(Key.Enter); ReleaseKey(Key.Enter); var confirmed = _log.Any(line => line.Contains("(synthetic)", StringComparison.Ordinal));
            PressKey(Key.I); ReleaseKey(Key.I); var inspected = _log.Any(line => line.Contains("inspect", StringComparison.OrdinalIgnoreCase));
            // Opening Settings quarantines input; the synchronous smoke must
            // supply the same two complete neutral observations as ordinary use.
            PressKey(Key.F1); ReleaseKey(Key.F1); _Process(0); _Process(0); VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "smoke did not open settings");
            PressKey(Key.Down); _Process(0); ReleaseKey(Key.Down); _Process(0); PressKey(Key.Enter); ReleaseKey(Key.Enter); VerificationMode.Require(_settings.CameraDeadzone != LabSettings.Default.CameraDeadzone && _settingsStore.Load().CameraDeadzone == _settings.CameraDeadzone, "smoke did not persist changed setting");
            for (var step = 0; step < 11; step++) { PressKey(Key.Down); _Process(0); ReleaseKey(Key.Down); _Process(0); }
            VerificationMode.Require(_hud.SelectedOption == "Reset defaults", "smoke did not navigate to Reset defaults"); PressKey(Key.Enter); ReleaseKey(Key.Enter);
            VerificationMode.Require(confirmed && inspected && _settings == LabSettings.Default && _settingsStore.Load() == LabSettings.Default, "smoke did not prove confirm, inspect, settings change, and reset persistence");
            System.IO.File.WriteAllText(args[index + 1], JsonSerializer.Serialize(new { status = "passed", provenance = VerificationMode.Simulation, isolatedTestStorage = _settingsStore.UsesIsolatedTestStorage, movementMode = _settings.MovementMode.ToString(), exercises = new[] { "scene", "actor-displacement", "camera-displacement", "confirm", "inspect", "settings-change", "settings-reset" }, physicalDeviceEvidence = new { xboxBluetoothAxesButtons = "Not verified", disconnectReconnect = "Not verified", drift = "Not verified", comfort = "Not verified" } })); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    /// <summary>
    /// Export-validation helper: each invocation is a new process, so this proves
    /// user:// persistence across restart without relying on the in-process smoke.
    /// It leaves the lab at the safe inverted defaults on every verification path.
    /// </summary>
    public void RunPersistence(string[] args)
    {
        var index = Array.IndexOf(args, "--persistence");
        if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(1); return; }
        var operation = args[index + 1]; var report = args[index + 2];
        try
        {
            VerificationMode.Require(_settingsStore.UsesIsolatedTestStorage, "persistence requires explicit isolated storage");
            _injectedObservations = true; SetProcess(false); SetProcessInput(false);
            if (RunPresetPersistenceOperation(operation, report)) return;
            var persisted = LabSettings.Default with { InvertCameraX = false, InvertCameraY = true, ActorSensitivity = 1.5, MovementMode = MovementMode.NearbyGoal };
            if (operation == "write")
            {
                _settingsStore.Save(persisted);
                System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", operation, isolatedTestStorage = _settingsStore.UsesIsolatedTestStorage, settings = persisted, provenance = VerificationMode.Simulation }));
            }
            else if (operation == "verify-nondefault")
            {
                VerificationMode.Require(_settingsStore.Load() == persisted, "fresh exported process did not load the non-default user-local settings");
                _settingsStore.Save(LabSettings.Default);
                System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", operation, isolatedTestStorage = _settingsStore.UsesIsolatedTestStorage, loaded = persisted, resetTo = LabSettings.Default, provenance = VerificationMode.Simulation }));
            }
            else if (operation == "verify-default")
            {
                VerificationMode.Require(_settingsStore.Load() == LabSettings.Default, "fresh exported process did not load reset inverted defaults");
                _settingsStore.Save(LabSettings.Default);
                System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", operation, isolatedTestStorage = _settingsStore.UsesIsolatedTestStorage, loaded = LabSettings.Default, provenance = VerificationMode.Simulation }));
            }
            else { GetTree().Quit(1); return; }
            GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public void RunCapture(string[] args) { var index = Array.IndexOf(args, "--capture"); if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(1); return; } var parts = args[index + 1].Split('x'); GetWindow().Size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])); CallDeferred(nameof(SaveCaptureAfterLayout), args[index + 2]); }
    public async void SaveCaptureAfterLayout(string path) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); var image = GetViewport().GetTexture().GetImage(); GetTree().Quit(image.SavePng(path) == Error.Ok ? 0 : 1); }
    private async Task LayoutFrames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void PressKey(Key key) => Handle(new InputEventKey { Keycode = key, Pressed = true }, VerificationMode.Simulation);
    private void ReleaseKey(Key key) => Handle(new InputEventKey { Keycode = key, Pressed = false }, VerificationMode.Simulation);
    private InputContext Context(double delta, bool modal, bool advanceTime = false) => new(_settings.CameraDeadzone, _settings.ActorDeadzone, _settings.CameraSensitivity, _settings.ActorSensitivity, delta, _camera.Zoom.X, _settings.InvertCameraX, _settings.InvertCameraY, modal, advanceTime) { ExplicitGamepadBindingsEnabled = true, WorldZoomEnabled = true };
    private string PrimaryAction() => _result?.Id is null ? "Choose destination" : _result.Id.StartsWith("npc") ? "Talk" : _result.Id.StartsWith("door") || _result.Id == "crate" ? "Open" : _result.Id.StartsWith("enemy") ? "Mark" : "Pick up";
    private string Prompt()
    {
        var bindings = _presetStep?.HudBindings ?? Profile.Bindings.Where(b => b.Context == "world" && b.Modifiers.Count == 0 && b.Button is "a" or "x" or "y" or "b").ToArray();
        var faces = string.Join("  ", bindings.Select(b => $"[{b.Button.ToUpperInvariant()}] {(b.SlotId is null ? b.ActionId : b.SlotId + " " + (_settings.Slots.Assignments[b.SlotId] ?? "EMPTY"))}"));
        var method = _input.Method == InputMethod.Gamepad ? faces : "WASD actor/arrows camera-menu; Enter interact, I inspect, C slots/catalog, F1 settings, F2 diagnostic actor reset";
        var shoulders = $"{Profile.RbModifierButton.ToUpperInvariant()} labels"
            + (Profile.BasePresetId == "radial" ? $"; hold {Profile.LbModifierButton.ToUpperInvariant()} wheel" : $"; {Profile.LbModifierButton.ToUpperInvariant()} slot bank");
        return $"{_settings.ActivePresetId} ({(Profile.Id == "user-copy" ? "user copy" : "template")}) bank {_presetStep?.Bank ?? "Contextual"}; {method}\nLS actor/menu/preview; RS camera/wheel; LT/RT zoom; {shoulders}; D-pad targets/battle/followers; L3 camera recenter; R3 lock; View hub; Menu settings\nLayer {Layer}; target {_actions.SelectedTargetId ?? "ground"}; lock {_actions.Locked}; pending {_actions.Pending?.ActionId ?? "none"}; grid {_actions.GridPreview?.ToString() ?? "none"}; journal {_actions.Journal.Count}; synthetic only / physical Xbox Not verified";
    }
    private void UpdateHud()
    {
        var active = _input.ActiveDevice; var name = active is int id ? Input.GetJoyName(id) : "none"; var goal = _actor.Goal is Vec2 g ? $"{g.X:F0},{g.Y:F0}" : "none";
        _hud.UpdateView($"Focus: {_focused}\nMethod: {_input.Method}; active: {active?.ToString() ?? "none"} / {name}\nLeft raw: {_lastRaw.LeftStick.X:F2},{_lastRaw.LeftStick.Y:F2}; right raw: {_lastRaw.RightStick.X:F2},{_lastRaw.RightStick.Y:F2}\nCamera {_cameraState.Mode}; center {_cameraCenter.X:F0},{_cameraCenter.Y:F0}; neutral armed={_cameraState.RecenterArmed}\nCamera deadzone {_settings.CameraDeadzone:F2}, sensitivity {_settings.CameraSensitivity:F1}; invert X={_settings.InvertCameraX} (right -> image {(_settings.InvertCameraX ? "right" : "left")}), Y={_settings.InvertCameraY} (down -> image {(_settings.InvertCameraY ? "down" : "up")})\nActor deadzone {_settings.ActorDeadzone:F2}, sensitivity {_settings.ActorSensitivity:F1}; mode={_settings.MovementMode} (PROTOTYPE POLICY)\nActor {_actor.Position.X:F0},{_actor.Position.Y:F0}; goal={goal}; outcome={_actor.Outcome}; replans={_actor.ReplanCount}\nLast event: {_lastEvent}; accepted: {_lastAccepted}; {_lastProvenance}\nPhysical Xbox: Not verified", Prompt(), _log);
    }
    private void AddLog(string text) { if (_log.Count == 128) _log.RemoveAt(0); _log.Add($"• {text}"); }
    private void RefreshSelection()
    {
        var viewport = GetViewport().GetVisibleRect(); var focus = viewport.GetCenter(); var ground = _world.MakeCanvasPositionLocal(focus);
        _result = _targeting.Update(_courtyard.Candidates(_world), new Rect(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y), _selection, new TargetingPolicy(), new Vec2(ground.X, ground.Y));
        _selection = new SelectionState(_result.Id, _result.ManualLock, _result.IsGround && _selection.ManualGroundPoint is not null ? _result.GroundPoint : null);
        _actions.ObservePresentation(_courtyard.WorldTargets.FirstOrDefault(t => t.Id == _result.Id));
    }
    private static Vector2 ToGodot(Vec2 value) => new((float)value.X, (float)value.Y);
    private static string DescribeEvent(InputEvent input) => input switch { InputEventJoypadButton button => $"JoyButton {button.ButtonIndex} {(button.Pressed ? "pressed" : "released")} device {button.Device}", InputEventJoypadMotion motion => $"JoyAxis {motion.Axis} value {motion.AxisValue:F2} device {motion.Device}", InputEventKey key => $"Key {key.Keycode} {(key.Pressed ? "pressed" : "released")}", InputEventMouseButton mouse => $"MouseButton {mouse.ButtonIndex} {(mouse.Pressed ? "pressed" : "released")}", _ => input.GetClass() };
}