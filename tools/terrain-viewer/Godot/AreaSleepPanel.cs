using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// The sleep and wait window (mode 6, <see cref="Arcanum.World.AreaRestRules"/>): one button for each length of the rest (eight, seven in the wait window) with the
/// labels of the game's <c>sleepUI.mes</c> (the project's English text when it does not read), and the screen fade the rest starts (a black cover under the windows and the bars). ARC-UI-1363: the window is
/// drawn in the original's look (<see cref="AreaSleepView"/>: art 565 at (573, 42), buttons of art 293, font art 229) once its art has read; until then (and in the self-test, which has no game folder)
/// a plain project panel stands in with the same buttons. The window takes no keys of its own (S, a window key or Escape come through the game's global keys). A press hides nothing (the original's
/// handler, 0x57B710): the buttons stay, a second press restarts the rest, and the window stays until the rest ends.
/// </summary>
public partial class AreaSleepPanel : CanvasLayer
{
    private CanvasLayer _fadeLayer = null!;
    private ColorRect _fade = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private VBoxContainer _buttons = null!;
    private readonly List<Button> _buttonList = [];
    private AreaSleepView _view = null!;
    private IReadOnlyList<string> _labels = [];

    /// <summary>A button was pressed (0-based).</summary>
    public event Action<int>? ButtonPressed;
    /// <summary>An interface sound of the original-look buttons (3000 press, 3001 release).</summary>
    public event Action<int>? SoundRequested;
    public bool IsOpen => Visible;
    /// <summary>The original's art and font are set (the window is drawn in the original's look when it opens).</summary>
    public bool ArtSet => _view.ArtSet;
    /// <summary>The open window is drawn in the original's look (art 565), not as the plain stand-in panel.</summary>
    public bool OriginalLook => IsOpen && _view.Visible;
    internal AreaSleepView ViewForSelfTest => _view;
    internal int ButtonCountForSelfTest => _labels.Count;
    internal string ButtonTextForSelfTest(int button) => _labels[button];
    internal double FadeForSelfTest => _fade.Color.A;

    public override void _Ready()
    {
        Layer = 4;
        // The cover sits under the windows (4) and the bars (5): it darkens the game view only.
        _fadeLayer = new CanvasLayer { Layer = 3 };
        _fade = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _fadeLayer.AddChild(_fade);
        AddChild(_fadeLayer);
        _panel = new AreaFallbackBox { Name = "AreaSleep", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(226 / 255f, 216 / 255f, 192 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _title = new Label { Modulate = new Color(.15f, .1f, .05f), HorizontalAlignment = HorizontalAlignment.Center };
        _buttons = new VBoxContainer();
        foreach (var control in new Control[] { _title, _buttons }) contents.AddChild(control);
        _view = new AreaSleepView();
        AddChild(_view);
        _view.RowPressed += row => ButtonPressed?.Invoke(row);
        _view.SoundRequested += sound => SoundRequested?.Invoke(sound);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
        _fadeLayer.Visible = false;
    }

    /// <summary>Shows the window with the labels of its buttons (the sleep window or, with <paramref name="wait"/>, the wait window).</summary>
    public void Open(IReadOnlyList<string> labels, bool wait)
    {
        foreach (var button in _buttonList) button.QueueFree();
        _buttonList.Clear();
        _labels = labels;
        Show(); _fadeLayer.Visible = _fade.Color.A > 0;
        if (_view.ArtSet)
        {
            _panel.Visible = false;
            _view.Open(labels);
            return;
        }
        _view.CloseView();
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var button = new Button { Name = "Rest" + i, Text = labels[i], FocusMode = Control.FocusModeEnum.All };
            button.Pressed += () => ButtonPressed?.Invoke(index);
            _buttons.AddChild(button); _buttonList.Add(button);
        }
        _title.Text = wait ? "Wait" : "Rest";
        _buttons.Visible = true;
        _panel.Visible = true;
        if (_buttonList.Count > 0) _buttonList[0].GrabFocus();
        CallDeferred(nameof(Fit));
    }

    /// <summary>Sets the original's art and font (the viewer has read them); the next opening is drawn in the original's look.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFontFace font) => _view.SetArt(art, font);

    /// <summary>Forgets the original's art (the module changed).</summary>
    public void ClearArt() { _view.CloseView(); _view.ClearArt(); }

    /// <summary>The original-look window follows the viewport (its place and factor); the plain panel does it itself.</summary>
    public void PlaceView() { if (_view.Visible) _view.Place(); }

    /// <summary>The window closes (the fade goes with it: the viewer fades the screen back in on its own cover until it is clear).</summary>
    public void CloseView() { Hide(); _view.CloseView(); GetViewport().GuiReleaseFocus(); }

    private void Fit() => _panel.Size = new Vector2(_panel.CustomMinimumSize.X, 0);

    /// <summary>The cover's alpha, 0 to 1, and whether the window's own panel is shown: the cover outlives the window while it fades in.</summary>
    public void SetFade(double alpha)
    {
        _fade.Color = new Color(0, 0, 0, (float)Math.Clamp(alpha, 0, 1));
        _fadeLayer.Visible = alpha > 0;
    }

    /// <summary>Controller confirm: the focused button.</summary>
    public void Confirm()
    {
        if (_view.Visible) { _view.Confirm(); return; }
        foreach (var button in _buttonList)
            if (button.HasFocus() && _buttons.Visible) { button.EmitSignal(BaseButton.SignalName.Pressed); return; }
    }

    public void Navigate(int direction)
    {
        if (_view.Visible)
        {
            if (_labels.Count > 0) _view.Focus = Math.Clamp(_view.Focus is { } at ? at + direction : 0, 0, _labels.Count - 1);
            return;
        }
        if (!_buttons.Visible || _buttonList.Count == 0) return;
        var focus = _buttonList.FindIndex(b => b.HasFocus());
        _buttonList[Math.Clamp(focus < 0 ? 0 : focus + direction, 0, _buttonList.Count - 1)].GrabFocus();
    }

    private void ResizePanel()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Min(360, size.X * .6f);
        // The wrapped note's height follows the width, so the panel is given its width first and then shrunk to its content (a Control keeps the largest size it had).
        _panel.CustomMinimumSize = new Vector2(width, 0);
        _panel.Size = new Vector2(width, 0);
        _panel.Position = new Vector2((size.X - width) / 2, size.Y * .12f);
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
