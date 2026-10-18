using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The WORLD diagnostic palette (ARC-INPUT-001): bounded modal controls for
/// Fit, Reset scene, Reload, exact zoom and input preferences. Storage, camera and
/// lifecycle remain owned by the host; this is a diagnostic tool, not original UI.</summary>
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
    public event Action? ResetSceneRequested;
    public event Action? ReloadRequested;
    public event Action? GameHourRequested;
    public event Action? LightingToggleRequested;
    public event Action? VisualToggleRequested;
    public event Action? AlwaysRunToggleRequested;
    public event Action? SoundTestRequested;
    private Label _sound = null!;
    private Label _gait = null!;
    private Label _visual = null!;
    private Label _lighting = null!;
    private Label _summary = null!;
    private Label _actionStatus = null!;
    private CheckButton _overlay = null!;
    private int _overlayIndex;
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
            Text = "Diagnostic palette — new intent gated; accepted step settles\nBounded adjacent grid — original Data admission, not gameplay\nOriginal parity NOT VERIFIED",
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
        _rows.AddChild(new Label { Text = "Reset scene: actor and Follow return to spawn; preferences kept.\nReload: the current source loads again; the scene resets.\nBoth wait for an accepted step to settle.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddAction("Reset scene", () => ResetSceneRequested?.Invoke());
        AddAction("Reload current source", () => ReloadRequested?.Invoke());
        // ARC-RENDER-010 / ARC-WORLD-010: game time flows by itself and stops while this palette or a dialog is
        // open; this action is an explicit wait of one hour that also works during that pause.
        _rows.AddChild(new Label { Text = "Day/night: game time flows by itself and is paused while this palette or a dialog is open.\n+1 hour waits one game hour.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddAction("Game time +1 hour", () => GameHourRequested?.Invoke());
        AddAction("Ambient lighting on/off", () => LightingToggleRequested?.Invoke());
        _lighting = new Label { Name = "PaletteLighting", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_lighting);
        // ARC-UI-006: the visual profile setting (also the H key); the switch commits once the visible frames are ready.
        AddAction("Visual Legacy/HD (H)", () => VisualToggleRequested?.Invoke());
        _visual = new Label { Name = "PaletteVisual", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_visual);
        // ARC-WORLD-020: the original's Always Run option (Num Lock and Ctrl act on top of it).
        AddAction("Always Run on/off", () => AlwaysRunToggleRequested?.Invoke());
        _gait = new Label { Name = "PaletteGait", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_gait);
        // ARC-UI-040: sound output and the original's fixed interface sounds, played by id from the module's catalogue.
        AddAction("Sound test: next interface sound", () => SoundTestRequested?.Invoke());
        _sound = new Label { Name = "PaletteSound", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_sound);
        // ARC-RENDER-008 overlay checkbox: the host binds availability/state (BindOverlay);
        // without a movement authority it is visibly unavailable, not navigable, not toggleable.
        _overlay = new CheckButton { Name = "PassabilityOverlay", Text = "Show passability (unavailable)", Disabled = true, FocusMode = Control.FocusModeEnum.None };
        _overlay.Toggled += value => { if (!_binding && !_overlay.Disabled) OverlayToggled?.Invoke(value); };
        _rows.AddChild(_overlay); _overlayIndex = _controls.Count;
        _actionStatus = new Label { Name = "PaletteActionStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_actionStatus);
        _summary = new Label { Name = "PaletteSummary", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _rows.AddChild(_summary);
        _rows.AddChild(new Label { Text = "Passability: read-only movement assessor view; legend in the HUD.\nReset preferences: input settings and zoom 2.00; scene unchanged.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddAction("Reset preferences", () => ResetRequested?.Invoke());
        AddAction("Close (Ctrl+Shift+F9/Start)", () => CloseRequested?.Invoke());
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
    public void SetSummary(string text) => _summary.Text = "Diagnostic summary: " + text;
    public void SetActionStatus(string text) => _actionStatus.Text = text;
    public void SetLightingStatus(string text) => _lighting.Text = text;
    public void SetVisualStatus(string text) => _visual.Text = text;
    public void SetGaitStatus(string text) => _gait.Text = text;
    public void SetSoundStatus(string text) => _sound.Text = text;
    /// <summary>The overlay state/action seam for ARC-RENDER-008: the host binds
    /// availability and state; a user toggle is reported once through OverlayToggled.
    /// Unavailable (the ARC-INPUT-001 default), the row is disabled and not navigable.</summary>
    public event Action<bool>? OverlayToggled;
    public bool OverlayAvailable => !_overlay.Disabled;
    public void BindOverlay(bool available, bool on, string text)
    {
        _binding = true;
        _overlay.Disabled = !available; _overlay.ButtonPressed = available && on; _overlay.Text = text;
        _overlay.FocusMode = available ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        // Navigable (keyboard/controller) exactly while available, at its row position.
        if (available && !_controls.Contains(_overlay))
        {
            _controls.Insert(_overlayIndex, _overlay);
            if (_selected >= _overlayIndex && GetViewport()?.GuiGetFocusOwner() != _overlay) _selected++;
        }
        else if (!available && _controls.IndexOf(_overlay) is var removed and >= 0)
        {
            // Keep the selection on the same control (or the row before the checkbox), so a
            // later Confirm can never fall through to an unrelated action such as Reset preferences.
            var focused = GetViewport()?.GuiGetFocusOwner() == _overlay;
            _controls.RemoveAt(removed);
            if (_selected > removed || (_selected == removed && _selected > 0)) _selected--;
            if (focused)
            {
                var control = _controls[_selected];
                if (control is SpinBox spin) spin.GetLineEdit().GrabFocus(); else control.GrabFocus();
            }
        }
        _binding = false;
    }
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
        if (_controls[_selected] is BaseButton { Disabled: true }) return;
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
