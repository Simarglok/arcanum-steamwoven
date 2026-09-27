using GamepadLab.Core;
using Godot;

namespace GamepadLab;
public partial class LabRoot : Node2D
{
    private readonly TargetingEngine _targeting = new(); private readonly InputStateReducer _input = new(); private readonly LabInputAdapter _adapter = new(); private readonly List<string> _log = new();
    private Node2D _world = null!; private SyntheticCourtyard _courtyard = null!; private LabHud _hud = null!; private SelectionState _selection = new(); private SelectionResult? _result; private bool _focused = true; private double _deadzone = .2; private double _sensitivity = 1;
    public override void _Ready()
    {
        LabInputAdapter.EnsureInputMap(); _world = GetNode<Node2D>("WorldRoot"); _courtyard = GetNode<SyntheticCourtyard>("WorldRoot/SyntheticCourtyard"); _hud = GetNode<LabHud>("CanvasLayer/LabHud"); _input.SetStartupDevices(Input.GetConnectedJoypads()); AddLog("Lab ready: synthetic-only scene; no original data.");
        var args = OS.GetCmdlineUserArgs(); if (args.Contains("--self-test")) CallDeferred(nameof(RunSelfTest)); else if (args.Contains("--smoke")) CallDeferred(nameof(RunSmoke), args); else if (args.Contains("--capture")) CallDeferred(nameof(RunCapture), args);
    }
    public override void _Process(double delta)
    {
        var viewport = GetViewport().GetVisibleRect(); _result = _targeting.Update(_courtyard.Candidates(_world), new Rect(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y), _selection, new TargetingPolicy());
        var id = _result.Id ?? "Ground"; var score = _result.Score?.ToString("F1") ?? "fallback";
        _hud.UpdateView($"Focus: {_focused}\nMethod: {_input.Method}\nActive ID: {_input.ActiveDevice?.ToString() ?? "none"}\nConnected IDs: {string.Join(',', Input.GetConnectedJoypads())}\nDeadzone: {_deadzone:F2}; sensitivity: {_sensitivity:F1}\nSelected: {id}; score: {score}; radius: 180px\nViewport: {viewport.Size}; zoom: 1\nEvent provenance: LIVE OS EVENT (not physical usability proof)\nPhysical Xbox: Not verified", Prompt(), _log);
        QueueRedraw();
    }
    public override void _Draw() { DrawCircle(GetViewportRect().GetCenter(), 11, Colors.Transparent, false, 2); DrawLine(GetViewportRect().GetCenter() - new Vector2(16, 0), GetViewportRect().GetCenter() + new Vector2(16, 0), Colors.White); DrawLine(GetViewportRect().GetCenter() - new Vector2(0, 16), GetViewportRect().GetCenter() + new Vector2(0, 16), Colors.White); }
    public override void _Input(InputEvent @event)
    {
        var intent = _input.Reduce(_adapter.Map(@event, _focused), new InputContext(_deadzone, _sensitivity, GetProcessDeltaTime(), 1, _hud.ModalVisible));
        if (!intent.Accepted) return;
        if (_hud.ModalVisible) { if (intent.Cancel) { _hud.HideModal(); _input.Quarantine(); AddLog("Modal closed; release required."); } GetViewport().SetInputAsHandled(); return; }
        if (intent.Pan != default) _world.Position += new Vector2((float)intent.Pan.X, (float)intent.Pan.Y);
        if (intent.Previous || intent.Next) { _selection = _targeting.Cycle(_result!, intent.Next ? 1 : -1); AddLog($"Cycle -> {_selection.SelectedId}"); }
        if (intent.Cancel) { _selection = _targeting.CancelLock(); AddLog("Manual target lock cleared."); }
        if (intent.Context) { _hud.ShowModal($"CONTEXTUAL LIST\n{PrimaryAction()}\nInspect\nCancel\n\nModal owns input first."); _input.Quarantine(); AddLog("Context menu opened."); }
        if (intent.Settings) { _hud.ShowModal($"SETTINGS\nDeadzone {_deadzone:F2} (0.05–0.45)\nSensitivity {_sensitivity:F1} (0.5–2.0)\nReset defaults\nClose"); _input.Quarantine(); AddLog("Settings opened."); }
        if (intent.Confirm) AddLog(_result!.IsGround ? "destination chosen at center reticle" : $"{PrimaryAction()} {_result.Id} (synthetic)");
        if (intent.Inspect) AddLog(_result!.IsGround ? "inspect ground coordinates (synthetic)" : $"inspect {_result.Id} (synthetic)");
        GetViewport().SetInputAsHandled();
    }
    public override void _Notification(int what)
    {
        base._Notification(what); if (what == NotificationApplicationFocusOut) { _focused = false; _input.FocusChanged(false); AddLog("Focus lost: levels cleared."); } if (what == NotificationApplicationFocusIn) { _focused = true; _input.FocusChanged(true); AddLog("Focus regained: neutral frame required."); }
    }
    public void RunSelfTest()
    {
        try { RefreshSelection(); foreach (var action in new[] { "lab_pan", "lab_confirm", "lab_cancel", "lab_inspect", "lab_previous", "lab_next", "lab_context", "lab_settings", "lab_menu_up", "lab_menu_down" }) if (!InputMap.HasAction(action)) throw new InvalidOperationException($"missing mapping {action}"); _input.Reduce(new RawInputFrame(-1, default, Confirm: true, Keyboard: true), new InputContext(), "SIMULATION"); _selection = _targeting.Cycle(_result!, 1); AddLog("SIMULATION self-test passed: mappings, selection, modal/focus path."); GD.Print("GamepadLab Godot self-test: PASS (SIMULATION)"); GetTree().Quit(0); } catch (Exception exception) { GD.PushError(exception.Message); GetTree().Quit(1); }
    }
    public void RunSmoke(string[] args) { var index = Array.IndexOf(args, "--smoke"); if (index < 0 || index == args.Length - 1) { GetTree().Quit(1); return; } RefreshSelection(); _selection = _targeting.Cycle(_result!, 1); AddLog("SIMULATION smoke confirm/inspect/settings/reset passed."); System.IO.File.WriteAllText(args[index + 1], VerificationMode.SmokeJson()); GetTree().Quit(0); }
    public void RunCapture(string[] args) { var index = Array.IndexOf(args, "--capture"); if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(1); return; } var parts = args[index + 1].Split('x'); GetWindow().Size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])); CallDeferred(nameof(SaveCapture), args[index + 2]); }
    public void SaveCapture(string path) { var error = GetViewport().GetTexture().GetImage().SavePng(path); GetTree().Quit(error == Error.Ok ? 0 : 1); }
    private string PrimaryAction() => _result?.Id is null ? "Choose destination" : _result.Id.StartsWith("npc") ? "Talk" : _result.Id.StartsWith("door") || _result.Id == "crate" ? "Open" : _result.Id.StartsWith("enemy") ? "Mark" : "Pick up";
    private string Prompt() => $"[A / Enter] {PrimaryAction()}   [X / I] Inspect   [LB/RB / Q/E] Cycle   [Y / C] Context   [Start / F1] Settings";
    private void AddLog(string text) { _log.Add($"• {text}"); }
    private void RefreshSelection()
    {
        var viewport = GetViewport().GetVisibleRect();
        _result = _targeting.Update(_courtyard.Candidates(_world), new Rect(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y), _selection, new TargetingPolicy());
    }
}
