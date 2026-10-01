using Arcanum.Input;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Bounded modal controls; storage and lifecycle remain owned by the host.</summary>
public partial class DiagnosticInputSettingsPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private readonly List<Control> _controls = new();
    private int _selected;
    private Label _status = null!;
    private bool _binding;
    public event Action<InputSettings>? Changed;
    public event Action? ResetRequested;
    public event Action? CloseRequested;
    public InputSettings Settings { get; private set; } = InputSettings.Default;
    public bool IsOpen => Visible;
    public override void _Ready()
    {
        Layer = 3;
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _panel.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_rows);
        _rows.AddChild(new Label { Text = "Diagnostic input settings — world paused", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddNumber("Camera deadzone", .05, .45, .01, s => s.CameraDeadzone, (s, v) => s with { CameraDeadzone = v });
        AddNumber("Actor deadzone", .05, .45, .01, s => s.ActorDeadzone, (s, v) => s with { ActorDeadzone = v });
        AddNumber("Camera sensitivity", .5, 2, .1, s => s.CameraSensitivity, (s, v) => s with { CameraSensitivity = v });
        AddNumber("Actor sensitivity", .5, 2, .1, s => s.ActorSensitivity, (s, v) => s with { ActorSensitivity = v });
        AddToggle("Camera image X inversion", s => s.InvertCameraX, (s, v) => s with { InvertCameraX = v });
        AddToggle("Camera image Y inversion", s => s.InvertCameraY, (s, v) => s with { InvertCameraY = v });
        var reset = new Button { Text = "Reset defaults and scene" }; reset.Pressed += () => ResetRequested?.Invoke();
        _rows.AddChild(reset); _controls.Add(reset);
        var close = new Button { Text = "Close (F1 / Start / Cancel)" }; close.Pressed += () => CloseRequested?.Invoke();
        _rows.AddChild(close); _controls.Add(close);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_status);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }
    private void AddNumber(string title, double min, double max, double step, Func<InputSettings, double> get, Func<InputSettings, double, InputSettings> set)
    {
        _rows.AddChild(new Label { Text = title });
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        spin.ValueChanged += v => { if (!_binding) Apply(set(Settings, v)); };
        _rows.AddChild(spin); _controls.Add(spin);
    }
    private void AddToggle(string title, Func<InputSettings, bool> get, Func<InputSettings, bool, InputSettings> set)
    {
        var check = new CheckButton { Text = title };
        check.Toggled += value => { if (!_binding) Apply(set(Settings, value)); };
        _rows.AddChild(check); _controls.Add(check);
    }
    private void Apply(InputSettings settings) { Settings = settings.Validated(); Changed?.Invoke(Settings); }
    public void Bind(InputSettings settings)
    {
        Settings = settings.Validated(); _binding = true;
        var numbers = new[] { Settings.CameraDeadzone, Settings.ActorDeadzone, Settings.CameraSensitivity, Settings.ActorSensitivity };
        for (var i = 0; i < numbers.Length; i++) ((SpinBox)_controls[i]).Value = numbers[i];
        ((CheckButton)_controls[4]).ButtonPressed = Settings.InvertCameraX;
        ((CheckButton)_controls[5]).ButtonPressed = Settings.InvertCameraY;
        _binding = false;
    }
    public void Open(InputSettings settings) { Bind(settings); Show(); _selected = 0; ((SpinBox)_controls[0]).GetLineEdit().GrabFocus(); }
    public void SetSaveStatus(bool saved) => _status.Text = saved ? "Preferences saved locally." : "Preferences save failed.";
    public void Navigate(int direction)
    {
        SynchronizeSelection();
        _selected = Math.Clamp(_selected + direction, 0, _controls.Count - 1);
        var control = _controls[_selected];
        if (control is SpinBox spin) spin.GetLineEdit().GrabFocus(); else control.GrabFocus();
    }
    public void Confirm()
    {
        SynchronizeSelection();
        if (_controls[_selected] is CheckButton check) check.ButtonPressed = !check.ButtonPressed;
        else if (_selected == 6) ResetRequested?.Invoke();
        else if (_selected == 7) CloseRequested?.Invoke();
    }
    public void Adjust(int direction)
    {
        SynchronizeSelection();
        if (_controls[_selected] is SpinBox spin) spin.Value += direction * spin.Step;
    }
    private void SynchronizeSelection()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        var selected = _controls.FindIndex(control => control == focus || (focus is not null && control.IsAncestorOf(focus)));
        if (selected >= 0) _selected = selected;
    }
    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _panel.Position = new(24, Math.Min(180, size.Y * .25f));
        _panel.Size = new(Math.Max(1, Math.Min(480, size.X - 48)), Math.Max(1, size.Y - _panel.Position.Y - 24));
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
