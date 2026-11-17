using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-008 diagnostic logbook: the built tabs of the session's PC (<see cref="AreaLogbook"/>), read only, as a scrolled list.
/// Each entry is drawn in the original's colour for its state (struck through for a quelled rumor, a completed, botched or elsewhere
/// completed quest). ARC-UI-074: with the game's art (<see cref="SetArt"/>) the logbook shows the original's book instead
/// (<see cref="AreaLogbookArtView"/>) and this list is one click away ("Diagnostic view"); the synthetic self-tests keep the list.
/// The app rebuilds it whenever the session or the tab changes.
/// </summary>
public partial class AreaLogbookPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private RichTextLabel _body = null!;
    private Label _hint = null!;
    private Button _rumors = null!;
    private Button _quests = null!;
    private Button _close = null!;
    private AreaLogbookArtView _artView = null!;
    // ARC-UI-098: the lens's round view of the game, on a layer under the window (the component the inventory and barter windows share).
    private AreaRoundView _roundView = null!;
    private Button _toDiagnostic = null!, _toOriginal = null!;
    private bool _diagnosticChosen;
    private readonly ButtonGroup _tabs = new() { AllowUnpress = false };
    private AreaLogbookView? _view;
    public event Action? CloseRequested;
    public event Action<AreaLogbookTab>? TabRequested;
    /// <summary>ARC-UI-098: a tab's hover hint, or null when the pointer left the tab (the app writes it to the message window, LB23).</summary>
    public event Action<string?>? HintRequested;
    /// <summary>ARC-UI-098: the PC lens's holder, the hero: its foot point in canvas units and the game view's zoom, or null for none (no lens view).</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }
    /// <summary>An interface sound of the book (a turned page).</summary>
    public event Action<int>? SoundRequested;
    public bool IsOpen => Visible;
    internal AreaLogbookView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _body.GetParsedText();
    /// <summary>The window as laid out: the book (the game windows' 800 × 400 at the screen's (0, 41), scaled) in the original look, else the list panel.</summary>
    internal Rect2 PanelRectForSelfTest => ArtMode
        ? new Rect2(_artView.GlobalPosition, new Vector2(AreaLogbookArtLayout.Window.Width, AreaLogbookArtLayout.Window.Height) * _artView.Scale)
        : _panel.GetGlobalRect();
    /// <summary>True when the original's book is the shown look (its art is set and the diagnostic list was not chosen).</summary>
    public bool ArtMode => _artView.ArtSet && !_diagnosticChosen;
    public bool ArtSet => _artView.ArtSet;
    internal AreaLogbookArtView ArtViewForSelfTest => _artView;
    /// <summary>ARC-UI-098: the lens's round view is shown under the window.</summary>
    internal bool RoundViewShown => _roundView.Visible;
    internal Button LookToggleForSelfTest => ArtMode ? _toDiagnostic : _toOriginal;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaLogbook", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Text = "Logbook", Modulate = new Color(.2f, .12f, .05f) };
        var tabs = new HBoxContainer();
        _rumors = TabButton("Rumors", AreaLogbookTab.Rumors);
        _quests = TabButton("Quests", AreaLogbookTab.Quests);
        _close = new Button { Name = "Close", Text = "Close", FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        foreach (var button in new[] { _rumors, _quests, _close }) tabs.AddChild(button);
        _body = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollActive = true, FitContent = false, SelectionEnabled = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 360),
        };
        _body.AddThemeColorOverride("default_color", Colors.Black);
        _hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "L, Escape or cancel closes; Tab or confirm switches the tab | PROJECT diagnostic window, read only: the built tabs only " +
                "(rumors and quests); the original's book is the \"Original look\" when the game's art is read; original UI parity NOT_VERIFIED",
        };
        _toOriginal = new Button { Name = "OriginalLook", Text = "Original look", Flat = true, Visible = false, FocusMode = Control.FocusModeEnum.All };
        _toOriginal.Pressed += () => ChooseLook(diagnostic: false);
        foreach (var control in new Control[] { _header, tabs, _body, _hint, _toOriginal }) contents.AddChild(control);
        // ARC-UI-074: the original's book, docked at the screen's (0, 41); its switch back to the list sits at the side panel's foot.
        _artView = new AreaLogbookArtView(); AddChild(_artView);
        _artView.TabRequested += tab => TabRequested?.Invoke(tab);
        _artView.SoundRequested += id => SoundRequested?.Invoke(id);
        _artView.HintRequested += text => HintRequested?.Invoke(text);
        _artView.CloseRequested += () => CloseRequested?.Invoke();
        _roundView = new AreaRoundView(); AddChild(_roundView);
        _toDiagnostic = new Button { Name = "DiagnosticView", Text = "Diagnostic view", Flat = true, FocusMode = Control.FocusModeEnum.All, Position = new(6, 380) };
        _toDiagnostic.AddThemeColorOverride("font_color", Colors.White);
        // In art pixels the switch must stay inside the window's 400 rows: a small font.
        _toDiagnostic.AddThemeFontSizeOverride("font_size", 9);
        _toDiagnostic.Pressed += () => ChooseLook(diagnostic: true);
        _artView.AddChild(_toDiagnostic);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private Button TabButton(string text, AreaLogbookTab tab)
    {
        // One group: a click on the shown tab keeps it pressed instead of releasing it.
        var button = new Button { Name = text, Text = text, ToggleMode = true, ButtonGroup = _tabs, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += () => TabRequested?.Invoke(tab);
        return button;
    }

    /// <summary>Gives the logbook the original's book art and fonts; without them (or after "Diagnostic view") the list is shown.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaTextFonts fonts) => _artView.SetArt(art, fonts);

    /// <summary>A new game session: the book's first spread again.</summary>
    public void ResetPages() => _artView.ResetPage();

    /// <summary>Takes the book's art back: the list is shown.</summary>
    public void ClearArt() { _artView.ClearArt(); if (_view is { } view) Render(view); }

    internal bool ListShownForSelfTest => _panel.Visible;

    private void ChooseLook(bool diagnostic)
    {
        _diagnosticChosen = diagnostic;
        if (_view is { } view) Render(view);
        Callable.From(() => (ArtMode ? _toDiagnostic : _toOriginal).GrabFocus()).CallDeferred();
    }

    public void Open(AreaLogbookView view)
    {
        Show(); Render(view);
        (ArtMode ? _toDiagnostic : _close).GrabFocus();
    }

    /// <summary>Turns the original book's pages (PROJECT keys and wheel; the buttons do the same); false in the list or at an end.</summary>
    public bool Turn(int direction) => ArtMode && _artView.Turn(direction);

    public void CloseView()
    {
        _view = null; _artView.CloseView(); _roundView.Visible = false; Hide();
        GetViewport().GuiReleaseFocus();
    }

    public void Render(AreaLogbookView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        _toOriginal.Visible = _artView.ArtSet;
        if (ArtMode)
        {
            _panel.Visible = false;
            _artView.Render(view);
            UpdateRoundView();
            return;
        }
        _panel.Visible = true; _artView.Visible = false; UpdateRoundView();
        _rumors.SetPressedNoSignal(view.Tab == AreaLogbookTab.Rumors);
        _quests.SetPressedNoSignal(view.Tab == AreaLogbookTab.Quests);
        _header.Text = $"Logbook | {AreaLogbook.TabName(view.Tab)} | {view.Entries.Count} entr{(view.Entries.Count == 1 ? "y" : "ies")}" +
            (view.LowIntelligence ? " | low-intelligence texts" : string.Empty) + (view.Note is null ? string.Empty : " | " + view.Note);
        _body.Text = view.Entries.Count == 0 ? "(no entries)" : string.Join("\n\n", view.Entries.Select(Entry));
        _body.ScrollToLine(0);
        ResizePanel();
    }

    /// <summary>Confirm (a controller confirm or Tab) switches to the other tab.</summary>
    public void Confirm()
    {
        if (_view is null) return;
        TabRequested?.Invoke(AreaLogbook.Next(_view.Tab));
    }

    private static string Entry(AreaLogbookEntry entry)
    {
        var text = Escape(AreaLogbook.Line(entry));
        return entry.Style switch
        {
            AreaLogbookStyle.Struck => $"[s]{text}[/s]",
            AreaLogbookStyle.Active => $"[color=#000096]{text}[/color]",
            AreaLogbookStyle.Completed => $"[color=#007800][s]{text}[/s][/color]",
            AreaLogbookStyle.Failed => $"[color=#960000][s]{text}[/s][/color]",
            AreaLogbookStyle.Blessing => $"[color=#000096]{text}[/color]",
            AreaLogbookStyle.Curse => $"[color=#960000]{text}[/color]",
            _ => text,
        };
    }

    // Data text is shown literally: an opening bracket would start a BBCode tag.
    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    private void ResizePanel()
    {
        if (_panel is null) return;
        if (ArtMode) _artView.Place();
        // ARC-UI-018: inside the part the interface bars leave free.
        var (top, size) = AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size);
        var width = Math.Max(1, Math.Min(900, size.X - 48));
        var height = Math.Max(1, Math.Min(640, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new(AreaGameBarPanel.FreeLeft + (size.X - width) / 2, top + Math.Max(24, (size.Y - height) / 2));
    }

    /// <summary>ARC-UI-098: the lens shows the game view around the hero while the original look is up (LB23, IW33).</summary>
    private void UpdateRoundView()
    {
        var holder = IsOpen && ArtMode && _view is not null ? HolderView?.Invoke() : null;
        var (origin, side) = _artView.LensOnScreen;
        _roundView.Update(origin, side, holder);
        _artView.LensCut = holder is not null;
    }

    public override void _Process(double delta) { if (IsOpen) { ResizePanel(); UpdateRoundView(); } }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
