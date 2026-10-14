using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
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
    internal Action<string>? InputTraceForSelfTest { get; set; }
    private readonly Dictionary<Control, Action> _actions = new();
    private SpinBox _zoom = null!;
    private Label _zoomStatus = null!;
    public event Action<InputSettings>? Changed;
    public event Action? ResetRequested;
    public event Action? CloseRequested;
    public event Action<double>? ZoomRequested;
    public event Action? FitRequested;
    public InputSettings Settings { get; private set; } = DiagnosticWorldPolicy.DefaultInputSettings;
    public bool IsOpen => Visible;
    public override void _Ready()
    {
        Layer = 3;
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(16 / 255f, 20 / 255f, 28 / 255f, 1),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 12, ContentMarginBottom = 12
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        contents.AddChild(new Label
        {
            Name = "WorldSettingsLimitation",
            Text = "Diagnostic input settings — new intent gated; accepted step settles\nBounded adjacent grid — original Data admission, not gameplay\nOriginal parity NOT VERIFIED",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        var scroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        contents.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_rows);

        AddNumber("Camera deadzone", .05, .45, .01, s => s.CameraDeadzone, (s, v) => s with { CameraDeadzone = v });
        AddNumber("Actor deadzone", .05, .45, .01, s => s.ActorDeadzone, (s, v) => s with { ActorDeadzone = v });
        AddNumber("Camera sensitivity", .5, 2, .1, s => s.CameraSensitivity, (s, v) => s with { CameraSensitivity = v });
        AddNumber("Actor sensitivity", .5, 2, .1, s => s.ActorSensitivity, (s, v) => s with { ActorSensitivity = v });
        AddToggle("Camera image X inversion", s => s.InvertCameraX, (s, v) => s with { InvertCameraX = v });
        AddToggle("Camera image Y inversion", s => s.InvertCameraY, (s, v) => s with { InvertCameraY = v });
        AddToggle("Actor screen X inversion", s => s.InvertActorX, (s, v) => s with { InvertActorX = v });
        AddToggle("Actor screen Y inversion", s => s.InvertActorY, (s, v) => s with { InvertActorY = v });
        _rows.AddChild(new Label { Text = "World zoom (camera ratio, not UI or physical 1:1)" });
        _zoom = new SpinBox { Name = "WorldZoom", MinValue = .125, MaxValue = 8, Step = 0, CustomArrowStep = .05, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _zoom.ValueChanged += v => { if (!_binding) ZoomRequested?.Invoke(v); };
        _rows.AddChild(_zoom); _controls.Add(_zoom);
        AddAction("100% (1.00)", () => ZoomRequested?.Invoke(1));
        AddAction("200% (2.00)", () => ZoomRequested?.Invoke(2));
        AddAction("300% (3.00)", () => ZoomRequested?.Invoke(3));
        AddAction("Fit (session view only)", () => FitRequested?.Invoke());
        _zoomStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_zoomStatus);
        _rows.AddChild(new Label { Text = "LT out / RT in: deadzone 0.10, full drive 1 octave/s.\nShoulders adjust selected zoom by 0.05.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddAction("Reset defaults and scene", () => ResetRequested?.Invoke());
        AddAction("Close (F1 / Start / Cancel)", () => CloseRequested?.Invoke());
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
        var check = new CheckButton { Name = title.Replace(" ", ""), Text = title };
        check.Toggled += value => {
            InputTraceForSelfTest?.Invoke($"Toggled control={check.Name} value={value} binding={_binding}");
            if (!_binding) Apply(set(Settings, value));
        };
        _rows.AddChild(check); _controls.Add(check);
    }
    private void Apply(InputSettings settings)
    {
        InputTraceForSelfTest?.Invoke("Apply:before");
        Settings = settings.Validated(DiagnosticWorldPolicy.DefaultInputSettings); Changed?.Invoke(Settings);
        InputTraceForSelfTest?.Invoke("Apply:after");
    }
    private void AddAction(string title, Action action)
    {
        var button = new Button { Text = title }; button.Pressed += action;
        _rows.AddChild(button); _controls.Add(button); _actions.Add(button, action);
    }
    public void BindZoom(double current, double preferred, bool fit, bool pending, bool failed)
    {
        _binding = true; _zoom.Value = current; _binding = false;
        _zoomStatus.Text = $"Current {(fit ? "Fit view" : "world zoom")}: {current:0.#####}; preferred startup: {preferred:0.#####}. "
            + (failed ? "Zoom save failed; session value retained." : pending ? "Zoom save pending." : "Zoom preference current.");
    }
    public void Bind(InputSettings settings)
    {
        Settings = settings.Validated(DiagnosticWorldPolicy.DefaultInputSettings); _binding = true;
        var numbers = new[] { Settings.CameraDeadzone, Settings.ActorDeadzone, Settings.CameraSensitivity, Settings.ActorSensitivity };
        for (var i = 0; i < numbers.Length; i++) ((SpinBox)_controls[i]).Value = numbers[i];
        ((CheckButton)_controls[4]).ButtonPressed = Settings.InvertCameraX;
        ((CheckButton)_controls[5]).ButtonPressed = Settings.InvertCameraY;
        ((CheckButton)_controls[6]).ButtonPressed = Settings.InvertActorX;
        ((CheckButton)_controls[7]).ButtonPressed = Settings.InvertActorY;
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
        InputTraceForSelfTest?.Invoke($"Confirm selected={_selected} control={_controls[_selected].Name}");
        if (_controls[_selected] is CheckButton check) check.ButtonPressed = !check.ButtonPressed;
        else if (_actions.TryGetValue(_controls[_selected], out var action)) action();
    }
    public void Adjust(int direction)
    {
        SynchronizeSelection();
        if (_controls[_selected] is SpinBox spin) spin.Value += direction * (spin == _zoom ? .05 : spin.Step);
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
    // Wrapped labels can lower their minimum height only after the first
    // container layout. Reapply the viewport constraint while open; a native
    // window need not resize just to make its initial modal scrollable.
    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
