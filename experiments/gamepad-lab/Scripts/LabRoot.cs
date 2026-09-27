using System.Text.Json;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;
public partial class LabRoot : Node2D
{
    private readonly TargetingEngine _targeting = new(); private readonly InputStateReducer _input = new(); private readonly LabInputAdapter _adapter = new(); private readonly MenuRepeat _menuRepeat = new(); private readonly List<string> _log = new();
    private Node2D _world = null!; private Camera2D _camera = null!; private SyntheticCourtyard _courtyard = null!; private LabHud _hud = null!; private SelectionState _selection = new(); private SelectionResult? _result;
    private bool _focused = true; private double _deadzone = .2; private double _sensitivity = 1; private RawInputFrame _lastRaw = new(-1, default); private string _lastProvenance = "LIVE OS EVENT (not physical usability proof)"; private string _lastEvent = "startup";

    public override void _Ready()
    {
        LabInputAdapter.EnsureInputMap(); _world = GetNode<Node2D>("WorldRoot"); _camera = GetNode<Camera2D>("Camera2D"); _courtyard = GetNode<SyntheticCourtyard>("WorldRoot/SyntheticCourtyard"); _hud = GetNode<LabHud>("CanvasLayer/LabHud");
        _input.SetStartupDevices(Input.GetConnectedJoypads()); Input.JoyConnectionChanged += OnJoyConnectionChanged;
        AddLog("Lab ready: synthetic-only scene; no original data.");
        var args = OS.GetCmdlineUserArgs(); if (args.Contains("--self-test")) CallDeferred(nameof(RunSelfTest)); else if (args.Contains("--smoke")) CallDeferred(nameof(RunSmoke), args); else if (args.Contains("--capture")) CallDeferred(nameof(RunCapture), args);
    }
    public override void _ExitTree() => Input.JoyConnectionChanged -= OnJoyConnectionChanged;
    public override void _Process(double delta)
    {
        _input.EndFrame();
        RefreshSelection();
        var sample = _adapter.Sample(_input.ActiveDevice, _focused);
        var continuous = _input.Reduce(sample, Context(delta, _hud.ModalVisible, advanceTime: true));
        if (!_hud.ModalVisible && continuous.Accepted && continuous.Pan != default) _world.Position += ToGodot(continuous.Pan);
        if (_hud.ModalVisible && continuous.Accepted)
        {
            var move = _menuRepeat.Update(continuous.MenuDirection, continuous.MenuAnalog, delta);
            if (move != 0) _hud.Navigate(move);
        }
        RefreshSelection(); _courtyard.SetSelected(_result!.Id);
        var active = _input.ActiveDevice; var name = active is int id ? Input.GetJoyName(id) : "none"; var guid = active is int guidId ? Input.GetJoyGuid(guidId) : "n/a";
        var viewport = GetViewport().GetVisibleRect(); var score = _result.Score?.ToString("F1") ?? "ground";
        var known = active is int knownId && Input.IsJoyKnown(knownId);
        _hud.UpdateView($"Focus: {_focused}\nMethod: {_input.Method}\nActive: {active?.ToString() ?? "none"} / {name}\nGUID: {guid}; SDL mapping known: {known}\nConnected IDs: {string.Join(',', Input.GetConnectedJoypads().Order())}\nRaw stick: {_lastRaw.Stick.X:F2},{_lastRaw.Stick.Y:F2}; processed: {Vec2.CircularDeadzone(_lastRaw.Stick, _deadzone).X:F2},{Vec2.CircularDeadzone(_lastRaw.Stick, _deadzone).Y:F2}\nDeadzone: {_deadzone:F2}; sensitivity: {_sensitivity:F1}\nSelected: {_result.Id ?? "Ground"}; score: {score}; radius: 180px\nViewport: {viewport.Size}; zoom: {_camera.Zoom.X:F2}\nLast raw event: {_lastEvent}; {_lastProvenance}\nPhysical Xbox: Not verified", Prompt(), _log);
        QueueRedraw();
    }
    public override void _Draw() { var center = GetViewportRect().GetCenter(); DrawCircle(center, 11, Colors.Transparent, false, 2); DrawLine(center - new Vector2(16, 0), center + new Vector2(16, 0), Colors.White); DrawLine(center - new Vector2(0, 16), center + new Vector2(0, 16), Colors.White); }
    public override void _Input(InputEvent @event) => Handle(@event, "LIVE OS EVENT (not physical usability proof)");
    private void Handle(InputEvent @event, string provenance)
    {
        var raw = _adapter.Map(@event, _focused); _lastRaw = raw; _lastProvenance = provenance; _lastEvent = @event.GetClass();
        var intent = _input.Reduce(raw, Context(GetProcessDeltaTime(), _hud.ModalVisible), provenance);
        if (!intent.Accepted) return;
        if (_hud.ModalVisible)
        {
            if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left) _hud.SelectAt(mouse.Position);
            if (intent.Cancel) CloseModal(); else if (intent.Confirm) ActivateModal();
            GetViewport().SetInputAsHandled(); return;
        }
        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            var picked = _courtyard.PickVisible(click.Position, _world); var ground = _world.MakeCanvasPositionLocal(click.Position); _selection = picked is null ? _targeting.PickGround(new Vec2(ground.X, ground.Y)) : _targeting.Pick(picked); RefreshSelection(); AddLog(picked is null ? $"Mouse picked ground {_result!.GroundPoint.X:F0},{_result.GroundPoint.Y:F0}" : $"Mouse picked {picked}; manual lock.");
            GetViewport().SetInputAsHandled(); return;
        }
        if (intent.Previous || intent.Next) { _selection = _targeting.Cycle(_result!, intent.Next ? 1 : -1); RefreshSelection(); AddLog($"Cycle -> {_selection.SelectedId}"); }
        if (intent.Cancel) { _selection = _targeting.CancelLock(); AddLog("Manual target lock cleared."); }
        if (intent.Context) { _hud.ShowContext(PrimaryAction()); _input.Quarantine(); _menuRepeat.Clear(); AddLog("Context menu opened."); }
        if (intent.Settings) { _hud.ShowSettings(); _input.Quarantine(); _menuRepeat.Clear(); AddLog("Settings opened."); }
        if (intent.Confirm) AddLog(_result!.IsGround ? $"destination chosen at {_result.GroundPoint.X:F0},{_result.GroundPoint.Y:F0}" : $"{PrimaryAction()} {_result.Id} (synthetic)");
        if (intent.Inspect) AddLog(_result!.IsGround ? $"inspect ground {_result.GroundPoint.X:F0},{_result.GroundPoint.Y:F0} (synthetic)" : $"inspect {_result.Id} (synthetic)");
        GetViewport().SetInputAsHandled();
    }
    private void ActivateModal()
    {
        var choice = _hud.Activate(); if (choice == "") return;
        if (choice == "Cancel" || choice == "Close") { CloseModal(); return; }
        if (choice == "Deadzone −") _deadzone = Math.Max(.05, _deadzone - .05); else if (choice == "Deadzone +") _deadzone = Math.Min(.45, _deadzone + .05);
        else if (choice == "Sensitivity −") _sensitivity = Math.Max(.5, _sensitivity - .1); else if (choice == "Sensitivity +") _sensitivity = Math.Min(2, _sensitivity + .1);
        else if (choice == "Reset defaults") { _deadzone = .2; _sensitivity = 1; _input.Reset(); _menuRepeat.Clear(); AddLog("Defaults reset; neutral frame required."); return; }
        else if (choice == "Inspect") AddLog(_result!.IsGround ? "inspect ground (synthetic)" : $"inspect {_result.Id} (synthetic)");
        else AddLog($"{choice} {_result?.Id ?? "ground"} (synthetic)");
        AddLog($"Menu choice: {choice}");
    }
    private void CloseModal() { _hud.HideModal(); _input.Quarantine(); _menuRepeat.Clear(); AddLog("Modal closed; release required."); }
    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected) { _input.Disconnect((int)device); _adapter.ForgetDevice((int)device); AddLog($"Controller {(int)device} disconnected; intents cleared."); }
        else AddLog($"Controller {(int)device} connected; diagnostic-only until lowest meaningful candidate is armed.");
    }
    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationApplicationFocusOut) { _focused = false; _input.FocusChanged(false); _adapter.Clear(); _menuRepeat.Clear(); AddLog("Focus lost: levels cleared."); }
        if (what == NotificationApplicationFocusIn) { _focused = true; _input.FocusChanged(true); AddLog("Focus regained: neutral frame required."); }
    }
    public async void RunSelfTest()
    {
        try
        {
            RefreshSelection(); foreach (var action in new[] { "lab_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" }) VerificationMode.Require(InputMap.HasAction(action) && InputMap.ActionGetEvents(action).Count > 0, $"missing mapping {action}");
            var initialSize = GetWindow().Size; var initialZoom = _camera.Zoom; var initialWorld = _world.Position;
            foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1920, 1080) })
            {
                GetWindow().Size = size; await LayoutFrames(); _hud.ShowSettings(); await LayoutFrames();
                VerificationMode.Require(_hud.HasReadableLayout(GetViewport().GetVisibleRect()), $"responsive HUD bounds failed at {size}");
                _hud.HideModal();
                foreach (var zoom in new[] { .5f, 1f, 2f })
                {
                    _camera.Zoom = new Vector2(zoom, zoom); await LayoutFrames();
                    var crate = _courtyard.Candidates(_world).First(candidate => candidate.Id == "crate");
                    Handle(new InputEventMouseButton { Position = ToGodot(crate.Anchor), ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation);
                    VerificationMode.Require(_selection.ManualLock && _result!.Id == "crate", $"canvas target pick failed at {size} zoom {zoom}");
                    var miss = new Vector2(24, 24); var expectedGround = _world.MakeCanvasPositionLocal(miss);
                    Handle(new InputEventMouseButton { Position = miss, ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation);
                    VerificationMode.Require(_result!.IsGround && _result.GroundPoint == new Vec2(expectedGround.X, expectedGround.Y), $"canvas ground conversion failed at {size} zoom {zoom}");
                }
            }
            _camera.Zoom = initialZoom; GetWindow().Size = initialSize; _world.Position = initialWorld; await LayoutFrames();

            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation);
            _Process(0); VerificationMode.Require(_input.ActiveDevice == 3, "same-frame controller arbitration did not select lowest id");
            _Process(0); _Process(0); var beforeNonActive = _world.Position;
            Handle(new InputEventJoypadMotion { Device = 5, Axis = JoyAxis.LeftX, AxisValue = -.8f }, VerificationMode.Simulation); _Process(.1);
            VerificationMode.Require(_world.Position == beforeNonActive, "non-active controller moved world");
            var canvasBefore = _courtyard.Candidates(_world).First(candidate => candidate.Id == "crate").Anchor;
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1);
            var canvasAfter = _courtyard.Candidates(_world).First(candidate => candidate.Id == "crate").Anchor;
            VerificationMode.Require(_world.Position != beforeNonActive && canvasAfter != canvasBefore, "active controller hold did not displace canvas target");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, VerificationMode.Simulation); _Process(0);
            var stepStart = _world.Position; PressKey(Key.D); _Process(.1); ReleaseKey(Key.D); var oneStep = _world.Position.X - stepStart.X;
            _world.Position = stepStart; PressKey(Key.D); _Process(.04); _Process(.06); ReleaseKey(Key.D); var splitStep = _world.Position.X - stepStart.X;
            VerificationMode.Require(Math.Abs(oneStep - splitStep) < .001, "keyboard pan is not delta-scaled");
            _camera.Zoom = Vector2.One; _world.Position = stepStart; PressKey(Key.D); _Process(.1); ReleaseKey(Key.D); var zoomOneWorldStep = _world.Position.X - stepStart.X;
            _camera.Zoom = new Vector2(2, 2); _world.Position = stepStart; PressKey(Key.D); _Process(.1); ReleaseKey(Key.D); var zoomTwoWorldStep = _world.Position.X - stepStart.X;
            VerificationMode.Require(Math.Abs(zoomOneWorldStep / 2 - zoomTwoWorldStep) < .001, "pan does not scale by camera zoom"); _camera.Zoom = initialZoom;

            PressKey(Key.F1); ReleaseKey(Key.F1); VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "settings did not open through adapter");
            _Process(0); _Process(0); var selectedBefore = _hud.SelectedIndex; PressKey(Key.Down); _Process(0); VerificationMode.Require(_hud.SelectedIndex == (selectedBefore + 1) % 6, "digital menu did not move immediately in process");
            _Process(.34); VerificationMode.Require(_hud.SelectedIndex == (selectedBefore + 1) % 6, "digital menu repeated before .35s"); _Process(.01); VerificationMode.Require(_hud.SelectedIndex == (selectedBefore + 2) % 6, "digital menu did not repeat at .35s"); ReleaseKey(Key.Down); _Process(0);
            var modalWorld = _world.Position; PressKey(Key.D); _Process(.1); ReleaseKey(Key.D); VerificationMode.Require(_world.Position == modalWorld, "modal leaked held world pan");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = .65f }, VerificationMode.Simulation); _Process(0); var analogSelection = _hud.SelectedIndex; _Process(.35); VerificationMode.Require(_hud.SelectedIndex != analogSelection, "analog menu repeat did not advance");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = .45f }, VerificationMode.Simulation); _Process(0); var releasedSelection = _hud.SelectedIndex; _Process(.2); VerificationMode.Require(_hud.SelectedIndex == releasedSelection, "analog menu did not release at .45");
            PressKey(Key.Escape); ReleaseKey(Key.Escape); VerificationMode.Require(!_hud.ModalVisible, "modal cancel did not close exactly once");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }, VerificationMode.Simulation); _Process(0); _Process(0);
            Handle(new InputEventKey { Keycode = Key.C, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.C, Pressed = false }, VerificationMode.Simulation); VerificationMode.Require(_hud.ModalKind == ModalKind.Context, "context menu did not open through adapter"); CloseModal();

            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = .8f }, VerificationMode.Simulation); _Process(.1); var focusBefore = _world.Position; _Notification((int)NotificationApplicationFocusOut); _Process(.1); VerificationMode.Require(_world.Position == focusBefore, "focus loss retained held pan"); _Notification((int)NotificationApplicationFocusIn); _Process(0); _Process(0); OnJoyConnectionChanged(3, false); var disconnectBefore = _world.Position; _Process(.1); VerificationMode.Require(_world.Position == disconnectBefore, "active disconnect retained stale action");
            AddLog("SIMULATION self-test passed: actual adapter arbitration, canvas projection, responsive bounds, modal repeat and lifecycle clearing."); GD.Print("GamepadLab Godot self-test: PASS (SIMULATION)"); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public void RunSmoke(string[] args)
    {
        var index = Array.IndexOf(args, "--smoke"); if (index < 0 || index == args.Length - 1) { GetTree().Quit(1); return; }
        try
        {
            RefreshSelection(); PressKey(Key.Enter); ReleaseKey(Key.Enter); var confirmed = _log.Any(line => line.Contains("(synthetic)")); PressKey(Key.I); ReleaseKey(Key.I); var inspected = _log.Any(line => line.Contains("inspect", StringComparison.OrdinalIgnoreCase));
            PressKey(Key.F1); ReleaseKey(Key.F1); _Process(0); _Process(0); VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "smoke did not open settings through adapter");
            PressKey(Key.Down); _Process(0); ReleaseKey(Key.Down); _Process(0); PressKey(Key.Enter); ReleaseKey(Key.Enter); VerificationMode.Require(_deadzone != .2, "smoke did not apply nondefault setting");
            for (var step = 0; step < 3; step++) { PressKey(Key.Down); _Process(0); ReleaseKey(Key.Down); _Process(0); }
            VerificationMode.Require(_hud.SelectedOption == "Reset defaults", "smoke did not navigate to reset"); PressKey(Key.Enter); ReleaseKey(Key.Enter);
            VerificationMode.Require(confirmed && inspected && _deadzone == .2 && _sensitivity == 1 && _log.Any(line => line.Contains("Defaults reset")), "smoke did not prove confirm, inspect, and reset transitions");
            System.IO.File.WriteAllText(args[index + 1], JsonSerializer.Serialize(new { status = "passed", provenance = VerificationMode.Simulation, selection = _result?.Id ?? "Ground", exercises = new[] { "scene", "selection", "confirm", "inspect", "settings-change", "settings-reset" }, physicalDeviceEvidence = "Not verified" })); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public void RunCapture(string[] args) { var index = Array.IndexOf(args, "--capture"); if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(1); return; } var parts = args[index + 1].Split('x'); GetWindow().Size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])); CallDeferred(nameof(SaveCaptureAfterLayout), args[index + 2]); }
    public async void SaveCaptureAfterLayout(string path) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); RefreshSelection(); var error = GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(error == Error.Ok ? 0 : 1); }
    private async Task LayoutFrames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void PressKey(Key key) => Handle(new InputEventKey { Keycode = key, Pressed = true }, VerificationMode.Simulation);
    private void ReleaseKey(Key key) => Handle(new InputEventKey { Keycode = key, Pressed = false }, VerificationMode.Simulation);
    private InputContext Context(double delta, bool modal, bool advanceTime = false) => new(_deadzone, _sensitivity, delta, _camera.Zoom.X, modal, advanceTime);
    private string PrimaryAction() => _result?.Id is null ? "Choose destination" : _result.Id.StartsWith("npc") ? "Talk" : _result.Id.StartsWith("door") || _result.Id == "crate" ? "Open" : _result.Id.StartsWith("enemy") ? "Mark" : "Pick up";
    private string Prompt() => $"[A / Enter] {PrimaryAction()}   [X / I] Inspect   [LB/RB / Q/E] Cycle   [Y / C] Context   [Start / F1] Settings";
    private void AddLog(string text) => _log.Add($"• {text}");
    private void RefreshSelection()
    {
        var viewport = GetViewport().GetVisibleRect(); var focus = viewport.GetCenter(); var ground = _world.MakeCanvasPositionLocal(focus);
        _result = _targeting.Update(_courtyard.Candidates(_world), new Rect(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y), _selection, new TargetingPolicy(), new Vec2(ground.X, ground.Y));
        _selection = new SelectionState(_result.Id, _result.ManualLock, _result.IsGround && _selection.ManualGroundPoint is not null ? _result.GroundPoint : null);
    }
    private static Vector2 ToGodot(Vec2 value) => new((float)value.X, (float)value.Y);
}