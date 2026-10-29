using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-051 bug report window: one text field and Send/Cancel over a dimmed screen that takes every click, above every other
/// window. After a send it shows where the report went and Close. The app owns the report; the window only reports the buttons.
/// A PROJECT window (the original has no such window); no original art.
/// </summary>
public partial class AreaBugReportPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private Label _hint = null!;
    private TextEdit _text = null!;
    private Label _notice = null!;
    private Button _send = null!;
    private Button _cancel = null!;
    private Button _close = null!;
    public event Action? SendRequested;
    public event Action? CancelRequested;
    public event Action? CloseRequested;
    public bool IsOpen => Visible;
    /// <summary>True after a send: the window shows the outcome and only closes.</summary>
    public bool Finished { get; private set; }
    public string Text => _text.Text;
    internal string NoticeForSelfTest => _notice.Text;
    internal void SetTextForSelfTest(string text) => _text.Text = text;
    internal void PressSendForSelfTest() => _send.EmitSignal(BaseButton.SignalName.Pressed);
    internal Rect2 PanelRectForSelfTest => _panel.GetRect();

    public override void _Ready()
    {
        // Above every window of the viewer (the highest is 7) and the HUD.
        Layer = 20;
        var shade = new ColorRect { Name = "BugReportShade", Color = new Color(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        _panel = new PanelContainer { Name = "AreaBugReport", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .98f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Text = "Report a bug (the game is paused)", Modulate = new Color(.2f, .12f, .05f) };
        _hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "What went wrong? Send writes a report folder with your text, a screenshot taken when you pressed Ctrl+B, the last 120 seconds " +
                "of the log and a save of that moment. Ctrl+Enter sends, Escape cancels.",
        };
        _text = new TextEdit
        {
            Name = "BugReportText", PlaceholderText = "describe what you did and what happened", WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(480, 150), SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.All,
        };
        _notice = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.45f, .1f, .05f) };
        var buttons = new HBoxContainer();
        _send = new Button { Name = "Send", Text = "Send", FocusMode = Control.FocusModeEnum.None };
        _send.Pressed += () => SendRequested?.Invoke();
        _cancel = new Button { Name = "Cancel", Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        _cancel.Pressed += () => CancelRequested?.Invoke();
        _close = new Button { Name = "Close", Text = "Close", FocusMode = Control.FocusModeEnum.None };
        _close.Pressed += () => CloseRequested?.Invoke();
        foreach (var button in new[] { _send, _cancel, _close }) buttons.AddChild(button);
        foreach (var control in new Control[] { _header, _hint, _text, _notice, buttons }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        // ARC-DEV-064: a word-wrapped label measures its height at the width it has, and it gets the panel's width only when the
        // container lays it out (after the first ResizePanel), so the first open measured one word per line and the panel kept that
        // height past the bottom of the screen. The panel is fitted again whenever its content's minimum size changes.
        _panel.MinimumSizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>An empty field with the keyboard; <paramref name="notice"/> says what the report could not take (a save, a screenshot).</summary>
    public void Open(string notice)
    {
        Finished = false;
        _text.Text = ""; _text.Editable = true; _text.Visible = true; _hint.Visible = true;
        _notice.Text = notice;
        _send.Visible = true; _cancel.Visible = true; _close.Visible = false;
        _header.Text = "Report a bug (the game is paused)";
        Show(); ResizePanel();
        _text.GrabFocus();
    }

    /// <summary>The outcome of the send: where the report went (or why it failed); only Close stays.</summary>
    public void ShowOutcome(string outcome)
    {
        Finished = true;
        _header.Text = "Bug report";
        _text.Editable = false; _text.Visible = false; _hint.Visible = false;
        _notice.Text = outcome;
        _send.Visible = false; _cancel.Visible = false; _close.Visible = true;
        if (_text.HasFocus()) _text.ReleaseFocus();
        ResizePanel();
    }

    public void CloseView()
    {
        Hide(); Finished = false; _text.Text = "";
        GetViewport().GuiReleaseFocus();
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(640, size.X - 32));
        _panel.Size = new(width, 0);
        _panel.Position = new((size.X - width) / 2, Math.Max(16, (size.Y - _panel.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizePanel;
        _panel.MinimumSizeChanged -= ResizePanel;
    }
}
