using Arcanum.Data.Written;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-049 diagnostic reading window (<see cref="AreaReading"/>): the header lines (a telegram's form, a newspaper's masthead), the
/// left and right pages of a book spread or the one page of any other kind, the previous and next buttons of a book (the original's
/// arts 495 and 496 turn two pages), a close button (the original's hotspot on the frame) and a note. A plain layout instead of the
/// original's background and frame art (interface arts 477..633 and 494, not drawn here) and fonts; the text is the data's.
/// ARC-UI-1877: when the window's art and fonts read, the original's window (<see cref="AreaReadingArtView"/>, with the round view's lens) replaces this layout; the
/// diagnostic layout stays as the fallback and for the synthetic self-tests.
/// </summary>
public partial class AreaReadingPanel : CanvasLayer
{
    // ARC-UI-1428: every text of the panel has an explicit size (the fallback font), none under AreaUiScale.MinimumTextPx at the screen factor;
    // a header line takes its original font's size with the text-only factor (AreaReadingText.HeaderLinePx).
    private const int HeaderFontSize = 14, BodyFontSize = 16, NoteFontSize = 14, HintFontSize = 12, ButtonFontSize = 14;

    private PanelContainer _panel = null!;
    private Label _header = null!;
    private VBoxContainer _masthead = null!;
    private RichTextLabel _left = null!, _right = null!;
    private Label _note = null!;
    private Button _previous = null!, _next = null!, _close = null!;
    private AreaReadingView? _view;
    private AreaReadingArtView _artView = null!;
    private AreaRoundView _roundView = null!;

    public event Action? CloseRequested;
    public event Action<int>? PageRequested;
    /// <summary>ARC-UI-1877: the round view's holder (the foot point and the zoom of the game view around the hero), or null for none.</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }
    /// <summary>ARC-UI-1877: whether the window shows in the original's look (its art and fonts read).</summary>
    public bool ArtMode => !DiagnosticLook && _artView.ArtSet;
    /// <summary>PROJECT: shows the diagnostic layout although the art has read (the smoke's before-and-after capture); set before the next render.</summary>
    public bool DiagnosticLook { get; set; }
    public AreaReadingArtView ArtView => _artView;
    internal AreaReadingArtView ArtViewForSelfTest => _artView;
    internal bool RoundViewShown => _roundView.Visible;
    public bool IsOpen => Visible;
    public AreaReadingView? View => _view;
    internal AreaReadingView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _left.GetParsedText() + "\n" + _right.GetParsedText();
    internal string HeaderForSelfTest => string.Join("  ·  ", _masthead.GetChildren().OfType<Label>().Select(label => label.Text));
    internal IReadOnlyList<int> HeaderSizesForSelfTest => _masthead.GetChildren().OfType<Label>().Select(label => label.GetThemeFontSize("font_size")).ToArray();

    public override void _Ready()
    {
        Layer = 4;
        _artView = new AreaReadingArtView(); AddChild(_artView);
        _artView.CloseRequested += () => CloseRequested?.Invoke();
        _artView.PageRequested += by => PageRequested?.Invoke(by);
        _roundView = new AreaRoundView(); AddChild(_roundView);
        _panel = new AreaFallbackBox { Name = "AreaReading", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(226 / 255f, 216 / 255f, 192 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.15f, .1f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _header.AddThemeFontSizeOverride("font_size", HeaderFontSize);
        _masthead = new VBoxContainer();
        var buttons = new HBoxContainer();
        _previous = Plain("Previous", () => PageRequested?.Invoke(-1));
        _next = Plain("Next", () => PageRequested?.Invoke(1));
        _close = Plain("Close", () => CloseRequested?.Invoke());
        foreach (var button in new[] { _previous, _next, _close }) buttons.AddChild(button);
        var pages = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _left = Page(); _right = Page();
        pages.AddChild(_left); pages.AddChild(_right);
        _note = new Label { Modulate = new Color(.35f, .05f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _note.AddThemeFontSizeOverride("font_size", NoteFontSize);
        var hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Escape or cancel closes; Left/Right or Page Up/Down turn a book's pages | PROJECT diagnostic window: the original's " +
                "background, frame and page art and its fonts are not drawn; pages break by characters, not pixels; layout parity NOT_VERIFIED",
        };
        hint.AddThemeFontSizeOverride("font_size", HintFontSize);
        foreach (var control in new Control[] { _header, buttons, _masthead, pages, _note, hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private static Button Plain(string text, Action pressed)
    {
        var button = new Button { Name = text, Text = text, FocusMode = Control.FocusModeEnum.All };
        button.AddThemeFontSizeOverride("font_size", ButtonFontSize);
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
        page.AddThemeFontSizeOverride("normal_font_size", BodyFontSize);
        return page;
    }

    public void Open(AreaReadingView view, string title)
    {
        Show(); Render(view, title);
        if (!ArtMode) _close.GrabFocus();
    }

    public void CloseView() { Hide(); _view = null; _artView.CloseView(); _roundView.Visible = false; GetViewport().GuiReleaseFocus(); }

    public void Render(AreaReadingView view, string title)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        if (ArtMode && _artView.Render(view))
        {
            _panel.Visible = false;
            UpdateRoundView();
            return;
        }
        _panel.Visible = true; _artView.Visible = false; UpdateRoundView();
        var book = view.Subtype == WrittenSubtype.Book;
        _previous.Visible = book; _next.Visible = book;
        _previous.Disabled = !view.HasPrevious; _next.Disabled = !view.HasNext;
        _header.Text = $"{title} | {view.Subtype} | background art {(view.BackgroundArt < 0 ? "none" : view.BackgroundArt)}, frame art {WrittenTexts.FrameArt}"
            + (book ? $" | spread {view.Spread + 1}/{view.Spreads}" : "");
        foreach (var child in _masthead.GetChildren()) { _masthead.RemoveChild(child); child.QueueFree(); }
        var factor = AreaGameBarPanel.ScreenFactor(GetViewport().GetVisibleRect().Size);
        for (var i = 0; i < view.Header.Count; i++)
        {
            var line = new Label
            {
                Text = view.Header[i], Modulate = new Color(.1f, .1f, .1f), HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            line.AddThemeFontSizeOverride("font_size", AreaReadingText.HeaderLinePx(view.HeaderFont(i), factor));
            _masthead.AddChild(line);
        }
        _masthead.Visible = view.Header.Count > 0;
        _left.Text = view.Left; _right.Text = view.Right;
        _right.Visible = book;
        _note.Text = view.Note ?? "";
    }

    /// <summary>ARC-UI-1877: turns the original-look book by a spread (−1, 1); false when it is not shown or there is none.</summary>
    public bool TurnArt(int by) => ArtMode && _artView.Visible && _artView.Turn(by);

    /// <summary>ARC-UI-1877: the lens shows the game view around the hero while the original look is up (the view-window mode 1 of 0x550DA0).</summary>
    private void UpdateRoundView()
    {
        var holder = IsOpen && ArtMode && _artView.Visible && _view is not null ? HolderView?.Invoke() : null;
        var (origin, side) = _artView.LensOnScreen;
        _roundView.Update(origin, side, holder);
        _artView.LensCut = holder is not null;
    }

    public override void _Process(double delta) { if (IsOpen && ArtMode && _artView.Visible) { _artView.Place(); UpdateRoundView(); } }

    /// <summary>Controller confirm: the focused button.</summary>
    public void Confirm()
    {
        if (ArtMode) return;
        foreach (var button in new[] { _previous, _next, _close })
            if (button.HasFocus() && button.Visible && !button.Disabled) { button.EmitSignal(BaseButton.SignalName.Pressed); return; }
    }

    public void Navigate(int direction)
    {
        // PROJECT: in the original look the menu left and right turn a book's pages (it has no focus ring).
        if (ArtMode) { if (direction != 0) PageRequested?.Invoke(Math.Sign(direction)); return; }
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
