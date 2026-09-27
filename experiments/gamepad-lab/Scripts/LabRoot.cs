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
        var continuous = _input.Reduce(sample, Context(delta, _hud.ModalVisible));
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
            var move = _menuRepeat.Update(intent.MenuDirection, intent.MenuAnalog, GetProcessDeltaTime()); if (move != 0) _hud.Navigate(move);
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
    public void RunSelfTest()
    {
        try
        {
            RefreshSelection(); foreach (var action in new[] { "lab_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" }) VerificationMode.Require(InputMap.HasAction(action) && InputMap.ActionGetEvents(action).Count > 0, $"missing mapping {action}");
            Handle(new InputEventMouseButton { Position = new Vector2(620, 480), ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation); VerificationMode.Require(_selection.ManualLock, "mouse pick did not create lock");
            Handle(new InputEventMouseButton { Position = new Vector2(20, 20), ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation); VerificationMode.Require(_result!.IsGround && _result.GroundPoint != new Vec2(GetViewport().GetVisibleRect().GetCenter().X, GetViewport().GetVisibleRect().GetCenter().Y), "mouse ground did not retain canvas coordinate");
            var crateBeforePan = _courtyard.Candidates(_world).First(candidate => candidate.Id == "crate").Anchor; var worldBeforePan = _world.Position;
            Handle(new InputEventKey { Keycode = Key.D, Pressed = true }, VerificationMode.Simulation); _Process(.1); Handle(new InputEventKey { Keycode = Key.D, Pressed = false }, VerificationMode.Simulation); _Process(.1);
            var crateAfterPan = _courtyard.Candidates(_world).First(candidate => candidate.Id == "crate").Anchor; VerificationMode.Require(_world.Position != worldBeforePan && crateAfterPan != crateBeforePan, "continuous pan did not displace rendered canvas target");
            var joy = new LabInputAdapter(); VerificationMode.Require(joy.Map(new InputEventJoypadButton { Device = 12, ButtonIndex = JoyButton.A, Pressed = true }, true).Confirm, "joy adapter injection failed");
            Handle(new InputEventKey { Keycode = Key.F1, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.F1, Pressed = false }, VerificationMode.Simulation);
            VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "settings did not open through adapter"); _Process(.01); _Process(.01); Handle(new InputEventKey { Keycode = Key.Down, Pressed = true }, VerificationMode.Simulation); _Process(.35); Handle(new InputEventKey { Keycode = Key.Down, Pressed = false }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true }, VerificationMode.Simulation);
            VerificationMode.Require(_log.Any(line => line.Contains("Menu choice")), "menu activation failed"); OnJoyConnectionChanged(9, false); _input.FocusChanged(false); _input.FocusChanged(true);
            AddLog("SIMULATION self-test passed: mappings, keyboard adapter, menu, selection, focus and disconnect hooks."); GD.Print("GamepadLab Godot self-test: PASS (SIMULATION)"); GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public void RunSmoke(string[] args)
    {
        var index = Array.IndexOf(args, "--smoke"); if (index < 0 || index == args.Length - 1) { GetTree().Quit(1); return; }
        try { RefreshSelection(); Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.I, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.I, Pressed = false }, VerificationMode.Simulation); VerificationMode.Require(_log.Any(line => line.Contains("synthetic")), "smoke did not produce interaction feedback"); Handle(new InputEventKey { Keycode = Key.F1, Pressed = true }, VerificationMode.Simulation); Handle(new InputEventKey { Keycode = Key.F1, Pressed = false }, VerificationMode.Simulation); _Process(.01); _Process(.01); VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "smoke did not open settings through adapter"); for (var step = 0; step < 4; step++) _hud.Navigate(1); VerificationMode.Require(_hud.Activate() == "Reset defaults", "smoke did not navigate to reset"); ActivateModal(); VerificationMode.Require(_log.Any(line => line.Contains("Defaults reset")), "smoke did not reset settings"); System.IO.File.WriteAllText(args[index + 1], JsonSerializer.Serialize(new { status = "passed", provenance = VerificationMode.Simulation, selection = _result?.Id ?? "Ground", exercises = new[] { "scene", "selection", "confirm", "inspect", "settings-reset" }, physicalDeviceEvidence = "Not verified" })); GetTree().Quit(0); }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public void RunCapture(string[] args) { var index = Array.IndexOf(args, "--capture"); if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(1); return; } var parts = args[index + 1].Split('x'); GetWindow().Size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])); CallDeferred(nameof(SaveCaptureAfterLayout), args[index + 2]); }
    public async void SaveCaptureAfterLayout(string path) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); RefreshSelection(); var error = GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(error == Error.Ok ? 0 : 1); }
    private InputContext Context(double delta, bool modal) => new(_deadzone, _sensitivity, delta, _camera.Zoom.X, modal);
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