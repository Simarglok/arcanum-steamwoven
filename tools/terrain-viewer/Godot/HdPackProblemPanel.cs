using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-350: the box that tells the player the HD pack cannot be used (or is incomplete) at startup: the reason, a note that the game goes on
/// with the original (Legacy) graphics, and two buttons, Continue and Close game, over a dimmed screen that takes every click and every key
/// while it is open (Enter or Escape continue). A PROJECT window (the original has no such window); no original art.
/// </summary>
public partial class HdPackProblemPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _message = null!;
    private Button _continue = null!;
    private Button _close = null!;
    public event Action? ContinueRequested;
    public event Action? CloseGameRequested;
    public bool IsOpen => Visible;
    internal string MessageForSelfTest => _message.Text;
    internal void PressContinueForSelfTest() => _continue.EmitSignal(BaseButton.SignalName.Pressed);
    internal void PressCloseGameForSelfTest() => _close.EmitSignal(BaseButton.SignalName.Pressed);

    public override void _Ready()
    {
        // Above every window of the viewer, the HUD and the bug report (20).
        Layer = 22;
        var shade = new ColorRect { Name = "HdPackProblemShade", Color = new Color(0, 0, 0, .55f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        _panel = new PanelContainer { Name = "HdPackProblem", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .98f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var header = new Label { Text = "HD pack problem", Modulate = new Color(.45f, .1f, .05f) };
        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.15f, .1f, .05f), CustomMinimumSize = new Vector2(420, 0) };
        var buttons = new HBoxContainer();
        _continue = new Button { Name = "Continue", Text = "Continue", FocusMode = Control.FocusModeEnum.None };
        _continue.Pressed += () => ContinueRequested?.Invoke();
        _close = new Button { Name = "CloseGame", Text = "Close game", FocusMode = Control.FocusModeEnum.None };
        _close.Pressed += () => CloseGameRequested?.Invoke();
        buttons.AddChild(_continue); buttons.AddChild(_close);
        foreach (var control in new Control[] { header, _message, buttons }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        _panel.MinimumSizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(string message)
    {
        _message.Text = message;
        Show(); ResizePanel();
    }

    public void CloseView() => Hide();

    // While the box is open it owns the keyboard: Enter and Escape continue, every other key is swallowed so the menu behind it does not act.
    public override void _Input(InputEvent input)
    {
        if (!Visible) return;
        if (input is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Enter or Key.KpEnter or Key.Escape) ContinueRequested?.Invoke();
        if (input is InputEventKey or InputEventJoypadButton) GetViewport().SetInputAsHandled();
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(560, size.X - 32));
        _panel.Size = new(width, 0);
        _panel.Position = new((size.X - width) / 2, Math.Max(16, (size.Y - _panel.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizePanel;
        _panel.MinimumSizeChanged -= ResizePanel;
    }
}
