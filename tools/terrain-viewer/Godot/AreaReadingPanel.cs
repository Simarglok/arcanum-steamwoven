using Arcanum.Data.Written;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-049 diagnostic reading window (<see cref="AreaReading"/>): the header lines (a telegram's form, a newspaper's masthead), the
/// left and right pages of a book spread or the one page of any other kind, the previous and next buttons of a book (the original's
/// arts 495 and 496 turn two pages), a close button (the original's hotspot on the frame) and a note. A plain layout instead of the
/// original's background and frame art (interface arts 477..633 and 494, not drawn here) and fonts; the text is the data's.
/// </summary>
public partial class AreaReadingPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private Label _masthead = null!;
    private RichTextLabel _left = null!, _right = null!;
    private Label _note = null!;
    private Button _previous = null!, _next = null!, _close = null!;
    private AreaReadingView? _view;

    public event Action? CloseRequested;
    public event Action<int>? PageRequested;
    public bool IsOpen => Visible;
    public AreaReadingView? View => _view;
    internal AreaReadingView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _left.GetParsedText() + "\n" + _right.GetParsedText();
    internal string HeaderForSelfTest => _masthead.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaReading", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(226 / 255f, 216 / 255f, 192 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.15f, .1f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _masthead = new Label { Modulate = new Color(.1f, .1f, .1f), AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        var buttons = new HBoxContainer();
        _previous = Plain("Previous", () => PageRequested?.Invoke(-1));
        _next = Plain("Next", () => PageRequested?.Invoke(1));
        _close = Plain("Close", () => CloseRequested?.Invoke());
        foreach (var button in new[] { _previous, _next, _close }) buttons.AddChild(button);
        var pages = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _left = Page(); _right = Page();
        pages.AddChild(_left); pages.AddChild(_right);
        _note = new Label { Modulate = new Color(.35f, .05f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Escape or cancel closes; Left/Right or Page Up/Down turn a book's pages | PROJECT diagnostic window: the original's " +
                "background, frame and page art and its fonts are not drawn; pages break by characters, not pixels; layout parity NOT_VERIFIED",
        };
        foreach (var control in new Control[] { _header, buttons, _masthead, pages, _note, hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private static Button Plain(string text, Action pressed)
    {
        var button = new Button { Name = text, Text = text, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += pressed;
        return button;
    }

    private static RichTextLabel Page()
    {
        var page = new RichTextLabel
        {
            BbcodeEnabled = false, ScrollActive = true, FitContent = false, SelectionEnabled = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 320),
        };
        page.AddThemeColorOverride("default_color", Colors.Black);
        return page;
    }

    public void Open(AreaReadingView view, string title)
    {
        Show(); Render(view, title);
        _close.GrabFocus();
    }

    public void CloseView() { Hide(); _view = null; GetViewport().GuiReleaseFocus(); }

    public void Render(AreaReadingView view, string title)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        var book = view.Subtype == WrittenSubtype.Book;
        _previous.Visible = book; _next.Visible = book;
        _previous.Disabled = !view.HasPrevious; _next.Disabled = !view.HasNext;
        _header.Text = $"{title} | {view.Subtype} | background art {(view.BackgroundArt < 0 ? "none" : view.BackgroundArt)}, frame art {WrittenTexts.FrameArt}"
            + (book ? $" | spread {view.Spread + 1}/{view.Spreads}" : "");
        _masthead.Text = string.Join("  ·  ", view.Header);
        _masthead.Visible = view.Header.Count > 0;
        _left.Text = view.Left; _right.Text = view.Right;
        _right.Visible = book;
        _note.Text = view.Note ?? "";
    }

    /// <summary>Controller confirm: the focused button.</summary>
    public void Confirm()
    {
        foreach (var button in new[] { _previous, _next, _close })
            if (button.HasFocus() && button.Visible && !button.Disabled) { button.EmitSignal(BaseButton.SignalName.Pressed); return; }
    }

    public void Navigate(int direction)
    {
        var enabled = new[] { _previous, _next, _close }.Where(b => b.Visible && !b.Disabled).ToList();
        var focus = enabled.FindIndex(b => b.HasFocus());
        enabled[Math.Clamp(focus < 0 ? 0 : focus + direction, 0, enabled.Count - 1)].GrabFocus();
    }

    private void ResizePanel()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(size.X * .1f, size.Y * .08f);
        _panel.Size = new Vector2(size.X * .8f, size.Y * .8f);
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
