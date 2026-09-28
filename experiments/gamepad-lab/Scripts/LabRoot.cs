using System.Text.Json;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

public partial class LabRoot : Node2D
{
    private readonly TargetingEngine _targeting = new(); private readonly InputStateReducer _input = new(); private readonly LabInputAdapter _adapter = new();
    private readonly MenuRepeat _menuRepeat = new(); private readonly MovementEngine _movement = new(); private readonly MovementPolicy _movementPolicy = new(); private readonly LabSettingsStore _settingsStore = new(); private readonly List<string> _log = new();
    private Node2D _world = null!; private Camera2D _camera = null!; private SyntheticCourtyard _courtyard = null!; private LabHud _hud = null!;
    private SelectionState _selection = new(); private SelectionResult? _result; private MovementState _actor = new(new Vec2(460, 610), new Vec2(1, 0));
    private LabSettings _settings = LabSettings.Default; private bool _focused = true; private RawInputFrame _lastRaw = new(-1, default);
    private string _lastProvenance = "LIVE OS EVENT (not physical usability proof)"; private string _lastEvent = "startup"; private bool _lastAccepted;

    public override void _Ready()
    {
        LabInputAdapter.EnsureInputMap(); _world = GetNode<Node2D>("WorldRoot"); _camera = GetNode<Camera2D>("Camera2D"); _courtyard = GetNode<SyntheticCourtyard>("WorldRoot/SyntheticCourtyard"); _hud = GetNode<LabHud>("CanvasLayer/LabHud");
        _settings = _settingsStore.Load(); _input.SetStartupDevices(Input.GetConnectedJoypads()); Input.JoyConnectionChanged += OnJoyConnectionChanged;
        SyncActor(); AddLog("Lab ready: synthetic-only scene; no original data. Default camera inversion is X=true, Y=true.");
        var args = OS.GetCmdlineUserArgs(); if (args.Contains("--self-test")) CallDeferred(nameof(RunSelfTest)); else if (args.Contains("--smoke")) CallDeferred(nameof(RunSmoke), args); else if (args.Contains("--capture")) CallDeferred(nameof(RunCapture), args);
    }
    public override void _ExitTree() => Input.JoyConnectionChanged -= OnJoyConnectionChanged;

    public override void _Process(double delta)
    {
        _input.EndFrame(); RefreshSelection();
        var intents = new List<IntentFrame>(); var controller = _adapter.SampleController(_input.ActiveDevice, _focused);
        if (controller is RawInputFrame joy) intents.Add(_input.Reduce(joy, Context(delta, _hud.ModalVisible, true)));
        intents.Add(_input.Reduce(_adapter.SampleKeyboard(_focused), Context(delta, _hud.ModalVisible, controller is null)));
        if (!_hud.ModalVisible) ApplyWorldIntents(intents.Where(i => i.Accepted), delta);
        if (_hud.ModalVisible)
        {
            var menu = intents.LastOrDefault(i => i.Accepted && (i.MenuDirection != 0 || Math.Abs(i.MenuAnalog) > .45));
            var move = _menuRepeat.Update(menu?.MenuDirection ?? 0, menu?.MenuAnalog ?? 0, delta); if (move != 0) _hud.Navigate(move);
        }
        RefreshSelection(); _courtyard.SetSelected(_result!.Id); UpdateHud();
    }

    public override void _Input(InputEvent input) => Handle(input, "LIVE OS EVENT (not physical usability proof)");
    private void Handle(InputEvent input, string provenance)
    {
        if (input is InputEventKey recenter && recenter.Pressed && recenter.Keycode == Key.F2) { Recenter(); GetViewport().SetInputAsHandled(); return; }
        var raw = _adapter.Map(input, _focused); _lastRaw = raw; _lastProvenance = provenance; _lastEvent = DescribeEvent(input);
        var intent = _input.Reduce(raw, Context(GetProcessDeltaTime(), _hud.ModalVisible), provenance); _lastAccepted = intent.Accepted;
        if (!intent.Accepted) return;
        if (_hud.ModalVisible)
        {
            if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left) _hud.SelectAt(mouse.Position);
            if (intent.Cancel) CloseModal(); else if (intent.Confirm) ActivateModal(); GetViewport().SetInputAsHandled(); return;
        }
        if (input is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            var picked = _courtyard.PickVisible(click.Position, _world); var ground = _world.MakeCanvasPositionLocal(click.Position);
            _selection = picked is null ? _targeting.PickGround(new Vec2(ground.X, ground.Y)) : _targeting.Pick(picked); RefreshSelection(); AddLog(picked is null ? "Mouse picked ground (targeting only)" : $"Mouse picked {picked}; manual lock.");
            GetViewport().SetInputAsHandled(); return;
        }
        if (intent.Previous || intent.Next) { _selection = _targeting.Cycle(_result!, intent.Next ? 1 : -1); RefreshSelection(); AddLog($"Cycle -> {_selection.SelectedId}"); }
        if (intent.Cancel) { _selection = _targeting.CancelLock(); ClearWorldIntents("Movement cancelled: cancel; both sticks require neutral."); }
        if (intent.Context) { _hud.ShowContext(PrimaryAction()); ClearWorldIntents("Context menu opened."); }
        if (intent.Settings) { _hud.ShowSettings(); ClearWorldIntents("Settings opened."); }
        if (intent.Confirm) AddLog(_result!.IsGround ? "destination selected for targeting only; left stick moves actor" : $"{PrimaryAction()} {_result.Id} (synthetic)");
        if (intent.Inspect) AddLog(_result!.IsGround ? "inspect ground (synthetic)" : $"inspect {_result.Id} (synthetic)");
        GetViewport().SetInputAsHandled();
    }

    private void ApplyWorldIntents(IEnumerable<IntentFrame> intents, double delta)
    {
        var sampled = intents.ToArray();
        var camera = sampled.Aggregate(default(Vec2), (total, intent) => total + intent.CameraPan);
        if (camera != default) _world.Position += ToGodot(camera);

        // Update movement once per frame even when neutral. This is essential: a
        // keyboard companion sample must not hide a controller release, and neutral
        // must clear an actor-relative goal rather than leave stale motion behind.
        var movement = sampled.Aggregate(default(Vec2), (total, intent) => total + intent.Movement);
        if (movement.Length > 1) movement = movement.Normalized();
        var step = _movement.Update(_actor, movement, delta, _settings.MovementMode, _movementPolicy,
            (candidate, radius) => _courtyard.CanOccupy(ToGodot(candidate), (float)radius));
        var wasBlocked = _actor.Outcome == MovementOutcome.Blocked;
        _actor = step.State; SyncActor();
        if (!wasBlocked && _actor.Outcome == MovementOutcome.Blocked) AddLog("Actor blocked; intent latched until neutral or direction changes.");
    }
    private void ActivateModal()
    {
        var choice = _hud.Activate(); if (choice == "" || choice == "Close" || choice == "Cancel") { CloseModal(); return; }
        if (_hud.ModalKind == ModalKind.Context)
        {
            if (choice == "Inspect") AddLog(_result!.IsGround ? "inspect ground (synthetic)" : $"inspect {_result.Id} (synthetic)");
            else AddLog($"{choice} {_result?.Id ?? "ground"} (synthetic)");
            AddLog($"Menu choice: {choice}"); CloseModal(); return;
        }
        _settings = (choice switch
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
        if (choice == "Recenter actor") Recenter();
        _settingsStore.Save(_settings); if (choice == "Reset defaults") ClearWorldIntents("Defaults reset (inverted X/Y); neutral frame required."); else AddLog($"Saved setting: {choice}");
    }
    private void CloseModal() { _hud.HideModal(); ClearWorldIntents("Modal closed; both sticks require neutral."); }
    private void ClearWorldIntents(string log) { CancelMovement(null); _input.Quarantine(); _menuRepeat.Clear(); AddLog(log); }
    private void CancelMovement(string? reason) { _actor = _actor with { Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Idle }; SyncActor(); if (reason is not null) AddLog($"Movement cancelled: {reason}."); }
    private void Recenter() { _courtyard.RecenterActor(); _actor = new MovementState(new Vec2(_courtyard.ActorPosition.X, _courtyard.ActorPosition.Y), new Vec2(1, 0)); AddLog("Actor recentered (F2). "); }
    private void SyncActor() => _courtyard.SetActor(ToGodot(_actor.Position), ToGodot(_actor.Heading), _actor.Goal is Vec2 goal ? ToGodot(goal) : null);
    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected) { _input.Disconnect((int)device); _adapter.ForgetDevice((int)device); CancelMovement(null); AddLog($"Controller {(int)device} disconnected; both world intents cleared."); }
        else AddLog($"Controller {(int)device} connected; neutral sample required after activation.");
    }
    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationApplicationFocusOut) { _focused = false; _input.FocusChanged(false); _adapter.ClearKeyboard(); CancelMovement(null); _menuRepeat.Clear(); AddLog("Focus lost: both sticks and movement cleared."); }
        if (what == NotificationApplicationFocusIn) { _focused = true; _input.FocusChanged(true); CancelMovement(null); AddLog("Focus regained: selected controller must be neutral."); }
    }

    public async void RunSelfTest()
    {
        try
        {
            foreach (var action in new[] { "lab_actor_move", "lab_camera_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" }) VerificationMode.Require(InputMap.HasAction(action) && InputMap.ActionGetEvents(action).Count > 0, $"missing mapping {action}");
            var saved = _settings; var initialActor = _actor.Position; var initialWorld = _world.Position;
            VerificationMode.Require(!_courtyard.CanOccupy(new Vector2(690, 500), (float)_movementPolicy.ActorRadius), "scene wall collision was not active");
            VerificationMode.Require(!_courtyard.CanOccupy(new Vector2(815, 275), (float)_movementPolicy.ActorRadius), "scene corner collision was not active");
            VerificationMode.Require(_courtyard.CanOccupy(new Vector2(950, 580), (float)_movementPolicy.ActorRadius), "scene narrow passage was unexpectedly closed");
            _settingsStore.Save(_settings with { InvertCameraX = false }); VerificationMode.Require(!_settingsStore.Load().InvertCameraX, "user-local settings did not persist/reload");
            _settingsStore.Save(LabSettings.Default); VerificationMode.Require(_settingsStore.Load() == LabSettings.Default, "reset defaults did not persist/restart with inverted axes"); _settingsStore.Save(saved);
            // Self-test injects device 3 through the actual adapter. Pin it only for
            // this synthetic harness, then enforce the same two-frame neutral rearm.
            _input.SetStartupDevices(new[] { 3 }); _input.Reset();
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            var actorBefore = _actor.Position; var worldBefore = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position.X > actorBefore.X && _world.Position == worldBefore, "left stick did not move only actor");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_world.Position.X > worldBefore.X && _actor.Position.X > actorBefore.X, "right stick did not pan camera image independently");
            var combinedActor = _actor.Position; var combinedWorld = _world.Position; _Process(.1);
            VerificationMode.Require(_actor.Position.X > combinedActor.X && _world.Position.X > combinedWorld.X, "both sticks did not run concurrently");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
            _hud.ShowSettings(); ClearWorldIntents("SIMULATION modal gate"); var modalActor = _actor.Position; var modalWorld = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == modalActor && _world.Position == modalWorld, "modal leaked either world intent"); CloseModal();
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            _Notification((int)NotificationApplicationFocusOut); var focusActor = _actor.Position; var focusWorld = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == focusActor && _world.Position == focusWorld, "focus loss retained a stick intent"); _Notification((int)NotificationApplicationFocusIn);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            var contextSettings = _settings; var contextLog = _log.Count;
            _hud.ShowContext("Talk"); ActivateModal();
            VerificationMode.Require(!_hud.ModalVisible && _log.Skip(contextLog).Any(line => line.Contains("Talk", StringComparison.Ordinal)) && _settings == contextSettings && _settingsStore.Load() == contextSettings, "context primary action was not dispatched without settings write");
            _hud.ShowContext("Talk"); _hud.Navigate(1); contextLog = _log.Count; ActivateModal();
            VerificationMode.Require(!_hud.ModalVisible && _log.Skip(contextLog).Any(line => line.Contains("inspect", StringComparison.OrdinalIgnoreCase)) && _settingsStore.Load() == contextSettings, "context Inspect was not dispatched without settings write");
            _hud.ShowContext("Talk"); _hud.Navigate(2); ActivateModal();
            VerificationMode.Require(!_hud.ModalVisible && _settingsStore.Load() == contextSettings, "context Cancel wrote settings or remained visible");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            _settings = _settings with { MovementMode = MovementMode.NearbyGoal };
            Recenter(); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); for (var step = 0; step < 20; step++) _Process(.1);
            VerificationMode.Require(_actor.Position.X < 675 - _movementPolicy.ActorRadius, "actor crossed synthetic wall"); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); Recenter();
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 1 }, VerificationMode.Simulation); for (var step = 0; step < 20; step++) _Process(.1);
            VerificationMode.Require(_actor.Outcome == MovementOutcome.Blocked, "wall did not yield blocked movement outcome"); var heldBlockedReplans = _actor.ReplanCount; for (var step = 0; step < 8; step++) _Process(.1);
            VerificationMode.Require(_actor.ReplanCount == heldBlockedReplans, "held blocked stick replanned without a bound");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); VerificationMode.Require(_actor.Goal is null && _actor.Outcome == MovementOutcome.Idle, "left-stick release did not clear pending goal");
            Recenter(); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1); var cancelActor = _actor.Position; var cancelWorld = _world.Position;
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.B, Pressed = true }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_actor.Position == cancelActor && _world.Position == cancelWorld && _actor.Goal is null, "cancel while both sticks are held did not quarantine both world domains");
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.B, Pressed = false }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1); VerificationMode.Require(_actor.Position.X > cancelActor.X, "neutral rearm did not permit safe movement resume after cancel");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
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
            _settings = LabSettings.Default; _settingsStore.Save(_settings);
            var actorBefore = _actor.Position; Handle(new InputEventKey { Keycode = Key.D, Pressed = true }, VerificationMode.Simulation); _Process(.1); Handle(new InputEventKey { Keycode = Key.D, Pressed = false }, VerificationMode.Simulation);
            var worldBefore = _world.Position; Handle(new InputEventKey { Keycode = Key.Right, Pressed = true }, VerificationMode.Simulation); _Process(.1); Handle(new InputEventKey { Keycode = Key.Right, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(_actor.Position.X > actorBefore.X && _world.Position.X > worldBefore.X, "smoke did not prove actor/camera keyboard separation");
            PressKey(Key.Enter); ReleaseKey(Key.Enter); var confirmed = _log.Any(line => line.Contains("(synthetic)", StringComparison.Ordinal));
            PressKey(Key.I); ReleaseKey(Key.I); var inspected = _log.Any(line => line.Contains("inspect", StringComparison.OrdinalIgnoreCase));
            PressKey(Key.F1); ReleaseKey(Key.F1); _Process(0); VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "smoke did not open settings");
            PressKey(Key.Down); _Process(0); ReleaseKey(Key.Down); _Process(0); PressKey(Key.Enter); ReleaseKey(Key.Enter); VerificationMode.Require(_settings.CameraDeadzone != LabSettings.Default.CameraDeadzone && _settingsStore.Load().CameraDeadzone == _settings.CameraDeadzone, "smoke did not persist changed setting");
            for (var step = 0; step < 11; step++) { PressKey(Key.Down); _Process(0); ReleaseKey(Key.Down); _Process(0); }
            VerificationMode.Require(_hud.SelectedOption == "Reset defaults", "smoke did not navigate to Reset defaults"); PressKey(Key.Enter); ReleaseKey(Key.Enter);
            VerificationMode.Require(confirmed && inspected && _settings == LabSettings.Default && _settingsStore.Load() == LabSettings.Default, "smoke did not prove confirm, inspect, settings change, and reset persistence");
            System.IO.File.WriteAllText(args[index + 1], JsonSerializer.Serialize(new { status = "passed", provenance = VerificationMode.Simulation, movementMode = _settings.MovementMode.ToString(), exercises = new[] { "scene", "actor-displacement", "camera-displacement", "confirm", "inspect", "settings-change", "settings-reset" }, physicalDeviceEvidence = new { xboxBluetoothAxesButtons = "Not verified", disconnectReconnect = "Not verified", drift = "Not verified", comfort = "Not verified" } })); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public void RunCapture(string[] args) { var index = Array.IndexOf(args, "--capture"); if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(1); return; } var parts = args[index + 1].Split('x'); GetWindow().Size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])); CallDeferred(nameof(SaveCaptureAfterLayout), args[index + 2]); }
    public async void SaveCaptureAfterLayout(string path) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); var image = GetViewport().GetTexture().GetImage(); GetTree().Quit(image.SavePng(path) == Error.Ok ? 0 : 1); }
    private void PressKey(Key key) => Handle(new InputEventKey { Keycode = key, Pressed = true }, VerificationMode.Simulation);
    private void ReleaseKey(Key key) => Handle(new InputEventKey { Keycode = key, Pressed = false }, VerificationMode.Simulation);
    private InputContext Context(double delta, bool modal, bool advanceTime = false) => new(_settings.CameraDeadzone, _settings.ActorDeadzone, _settings.CameraSensitivity, _settings.ActorSensitivity, delta, _camera.Zoom.X, _settings.InvertCameraX, _settings.InvertCameraY, modal, advanceTime);
    private string PrimaryAction() => _result?.Id is null ? "Choose destination" : _result.Id.StartsWith("npc") ? "Talk" : _result.Id.StartsWith("door") || _result.Id == "crate" ? "Open" : _result.Id.StartsWith("enemy") ? "Mark" : "Pick up";
    private string Prompt() => _input.Method == InputMethod.Gamepad ? "Left stick: move actor   Right stick: camera   [A] interact [X] inspect [Y] context [Start] settings" : "WASD: actor   arrows: camera/menu   [F2] recenter   [Enter] interact [I] inspect [F1] settings";
    private void UpdateHud()
    {
        var active = _input.ActiveDevice; var name = active is int id ? Input.GetJoyName(id) : "none"; var goal = _actor.Goal is Vec2 g ? $"{g.X:F0},{g.Y:F0}" : "none";
        _hud.UpdateView($"Focus: {_focused}\nMethod: {_input.Method}; active: {active?.ToString() ?? "none"} / {name}\nLeft raw: {_lastRaw.LeftStick.X:F2},{_lastRaw.LeftStick.Y:F2}; right raw: {_lastRaw.RightStick.X:F2},{_lastRaw.RightStick.Y:F2}\nCamera deadzone {_settings.CameraDeadzone:F2}, sensitivity {_settings.CameraSensitivity:F1}; invert X={_settings.InvertCameraX}, Y={_settings.InvertCameraY}\nActor deadzone {_settings.ActorDeadzone:F2}, sensitivity {_settings.ActorSensitivity:F1}; mode={_settings.MovementMode} (PROTOTYPE POLICY)\nActor {_actor.Position.X:F0},{_actor.Position.Y:F0}; goal={goal}; outcome={_actor.Outcome}; replans={_actor.ReplanCount}\nLast event: {_lastEvent}; accepted: {_lastAccepted}; {_lastProvenance}\nPhysical Xbox: Not verified", Prompt(), _log);
    }
    private void AddLog(string text) => _log.Add($"• {text}");
    private void RefreshSelection()
    {
        var viewport = GetViewport().GetVisibleRect(); var focus = viewport.GetCenter(); var ground = _world.MakeCanvasPositionLocal(focus);
        _result = _targeting.Update(_courtyard.Candidates(_world), new Rect(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y), _selection, new TargetingPolicy(), new Vec2(ground.X, ground.Y));
        _selection = new SelectionState(_result.Id, _result.ManualLock, _result.IsGround && _selection.ManualGroundPoint is not null ? _result.GroundPoint : null);
    }
    private static Vector2 ToGodot(Vec2 value) => new((float)value.X, (float)value.Y);
    private static string DescribeEvent(InputEvent input) => input switch { InputEventJoypadButton button => $"JoyButton {button.ButtonIndex} {(button.Pressed ? "pressed" : "released")} device {button.Device}", InputEventJoypadMotion motion => $"JoyAxis {motion.Axis} value {motion.AxisValue:F2} device {motion.Device}", InputEventKey key => $"Key {key.Keycode} {(key.Pressed ? "pressed" : "released")}", InputEventMouseButton mouse => $"MouseButton {mouse.ButtonIndex} {(mouse.Pressed ? "pressed" : "released")}", _ => input.GetClass() };
}