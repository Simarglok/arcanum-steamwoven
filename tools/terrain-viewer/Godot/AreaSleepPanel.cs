using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-WORLD-1160 diagnostic sleep and wait window (mode 6, <see cref="Arcanum.World.AreaRestRules"/>): one button for each length of the rest (eight, seven in the wait window) with the
/// labels of the game's <c>sleepUI.mes</c> (the project's English text when it does not read), and the screen fade the rest starts (a black cover under the windows and the bars). A plain
/// layout instead of the original's window art (interface arts 0x235 and 0x23D, not drawn here) and fonts; the window takes no keys of its own (S, a window key or Escape come through the
/// game's global keys). A press hides the buttons (0x57B710) and the window stays until the rest ends.
/// </summary>
public partial class AreaSleepPanel : CanvasLayer
{
    private CanvasLayer _fadeLayer = null!;
    private ColorRect _fade = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!, _note = null!;
    private VBoxContainer _buttons = null!;
    private readonly List<Button> _buttonList = [];

    /// <summary>A button was pressed (0-based).</summary>
    public event Action<int>? ButtonPressed;
    public bool IsOpen => Visible;
    /// <summary>True while the buttons are shown (before the first press).</summary>
    public bool ButtonsShown => _buttons.Visible;
    internal int ButtonCountForSelfTest => _buttonList.Count;
    internal string ButtonTextForSelfTest(int button) => _buttonList[button].Text;
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
        _panel = new PanelContainer { Name = "AreaSleep", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(226 / 255f, 216 / 255f, 192 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _title = new Label { Modulate = new Color(.15f, .1f, .05f), HorizontalAlignment = HorizontalAlignment.Center };
        _buttons = new VBoxContainer();
        _note = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "PROJECT diagnostic window: the original's window art and fonts are not drawn; S, a window key or Escape leave through the game's keys",
        };
        foreach (var control in new Control[] { _title, _buttons, _note }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
        _fadeLayer.Visible = false;
    }

    /// <summary>Shows the window with the labels of its buttons (the sleep window or, with <paramref name="wait"/>, the wait window).</summary>
    public void Open(IReadOnlyList<string> labels, bool wait)
    {
        foreach (var button in _buttonList) button.QueueFree();
        _buttonList.Clear();
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var button = new Button { Name = "Rest" + i, Text = labels[i], FocusMode = Control.FocusModeEnum.All };
            button.Pressed += () => ButtonPressed?.Invoke(index);
            _buttons.AddChild(button); _buttonList.Add(button);
        }
        _title.Text = wait ? "Wait" : "Rest";
        _buttons.Visible = true;
        Show(); _fadeLayer.Visible = _fade.Color.A > 0;
        if (_buttonList.Count > 0) _buttonList[0].GrabFocus();
        CallDeferred(nameof(Fit));
    }

    /// <summary>The window closes (the fade goes with it: the viewer fades the screen back in on its own cover until it is clear).</summary>
    public void CloseView() { Hide(); GetViewport().GuiReleaseFocus(); }

    /// <summary>A press hides the buttons (SWB8).</summary>
    public void HideButtons() { _buttons.Visible = false; GetViewport().GuiReleaseFocus(); CallDeferred(nameof(Fit)); }

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
        foreach (var button in _buttonList)
            if (button.HasFocus() && _buttons.Visible) { button.EmitSignal(BaseButton.SignalName.Pressed); return; }
    }

    public void Navigate(int direction)
    {
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
