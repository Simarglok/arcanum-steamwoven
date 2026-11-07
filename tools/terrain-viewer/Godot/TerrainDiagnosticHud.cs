using Godot;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

/// <summary>
/// Viewport-relative diagnostic HUD. It deliberately describes a bounded editor
/// diagnostic rather than gameplay or original-parity presentation.
/// </summary>
public partial class TerrainDiagnosticHud : CanvasLayer
{
    private PanelContainer? _panel;
    private Label? _sticky;
    private string _stickyBase = string.Empty;
    private string? _notice;
    private Label? _detail;
    private ScrollContainer? _scroll;
    private TerrainViewerMode _mode;
    private Button? _settings;
    private Label? _failure;
    private string? _failureText;
    public event Action? SettingsRequested;
    public void SetMode(TerrainViewerMode mode) { _mode = mode; if (_settings is not null) _settings.Visible = mode is TerrainViewerMode.WorldDiagnostic or TerrainViewerMode.Area; }

    internal string Text => $"{_sticky?.Text}\n{_detail?.Text}";
    internal Vector2 GlobalPosition => _panel?.GlobalPosition ?? Vector2.Zero;
    internal float Height => _panel?.Size.Y ?? 0;
    internal bool IsScrollable => _scroll is not null && _scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled
        && _scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Auto;
    internal bool HasVerticalOverflow => _scroll is not null && _scroll.GetVScrollBar().MaxValue > _scroll.GetVScrollBar().Page;
    internal bool DetailWraps => _detail is not null && _detail.GetLineCount() > 1;
    internal float VerticalScrollValue => (float)(_scroll?.GetVScrollBar().Value ?? 0);
    internal float VerticalScrollMaximum => (float)(_scroll?.GetVScrollBar().MaxValue ?? 0);
    internal float VerticalScrollPage => (float)(_scroll?.GetVScrollBar().Page ?? 0);
    internal Rect2 Rect => _panel?.GetGlobalRect() ?? new Rect2();
    internal bool HasHorizontalClipping => _detail is null || _scroll is null || _detail.GetCombinedMinimumSize().X > _scroll.Size.X + 1;
    internal bool StickyVisible => _sticky?.IsVisibleInTree() == true && _sticky.GetGlobalRect().Size.X > 0;
    internal string StickyText => _sticky?.Text ?? string.Empty;
    internal Rect2 StickyRect => _sticky?.GetGlobalRect() ?? new Rect2();
    internal bool StickyFullyShown => _sticky is not null && _sticky.GetVisibleLineCount() >= _sticky.GetLineCount();

    internal void MoveVerticalScroll()
    {
        if (_scroll is null) return;
        var bar = _scroll.GetVScrollBar();
        _scroll.SetVScroll(Mathf.IsEqualApprox(bar.Value, 0) ? (int)bar.MaxValue : 0);
    }

    internal void AddOverflowProbeForSelfTest()
    {
        if (_detail is null) return;
        // Exercise the production label/container range with real wrapped
        // content. This is self-test-only diagnostic text, never a normal
        // launch claim or source-data observation.
        for (var line = 0; line < 48; line++)
            _detail.Text += $"\nWrapped diagnostic overflow probe {line}: viewport layout must preserve the sticky policy summary.";
        _detail.CustomMinimumSize = new Vector2(0, 2000);
    }

    public override void _Ready()
    {
        // ARC-DEV-052: the panel starts hidden; the debug mode or an automated run shows it (ShowPanel).
        _panel = new PanelContainer { Name = "TerrainDiagnosticPanel", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.OffsetLeft = 16;
        _panel.OffsetTop = 16;
        _panel.OffsetRight = -16;
        AddChild(_panel);

        var contents = new VBoxContainer { Name = "DiagnosticContents" };
        _panel.AddChild(contents);
        _settings = new Button { Text = "Diagnostic palette (Ctrl+Shift+F9 / Start)", Visible = false };
        _settings.Pressed += () => SettingsRequested?.Invoke();
        contents.AddChild(_settings);
        _sticky = new Label
        {
            Name = "DiagnosticSticky",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "DIAGNOSTIC — not gameplay/original parity",
        };
        contents.AddChild(_sticky);
        _scroll = new ScrollContainer { Name = "DiagnosticDetails", SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        contents.AddChild(_scroll);
        _detail = new Label { Name = "DiagnosticDetail", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scroll.AddChild(_detail);
        _failure = new Label { Name = "DiagnosticFailure", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        // Without the panel's backing the line keeps an outline, readable over any map.
        _failure.AddThemeConstantOverride("outline_size", 4);
        _failure.AddThemeColorOverride("font_outline_color", Colors.Black);
        _failure.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _failure.OffsetLeft = 16;
        _failure.OffsetRight = -16;
        AddChild(_failure);
        GetViewport().SizeChanged += ResizeForViewport;
        ResizeForViewport();
    }

    /// <param name="failure">ARC-DEV-052: the detail is a load or start failure, kept on screen while the panel is hidden.</param>
    internal void SetDiagnosticText(string detail, bool automatedTestMode, bool failure = false)
    {
        var mode = _mode switch { TerrainViewerMode.Area => "area", TerrainViewerMode.WorldDiagnostic => "world-diagnostic", TerrainViewerMode.ActorDiagnostic => "actor-diagnostic",
            TerrainViewerMode.CombinedStaticDiagnostic => "combined", _ => "terrain-only" };
        var limitation = _mode is TerrainViewerMode.WorldDiagnostic or TerrainViewerMode.Area
            ? "\nBounded adjacent grid — original Data admission, not gameplay\nOriginal placement/movement/visual parity: NOT VERIFIED"
            : _mode == TerrainViewerMode.ActorDiagnostic ? "\nOriginal placement parity: NOT VERIFIED" : string.Empty;
        _stickyBase = $"{(automatedTestMode ? "AUTOMATED TEST MODE\n" : string.Empty)}Mode: {mode}\nDIAGNOSTIC — not gameplay/original parity{limitation}";
        ComposeSticky();
        if (_detail is not null) _detail.Text = detail;
        _failureText = failure ? detail : null;
        ComposeFailure();
    }

    // ARC-DEV-052: the failure line stands alone at the top of the window while the panel is hidden.
    private void ComposeFailure()
    {
        if (_failure is null) return;
        _failure.Text = _failureText ?? string.Empty;
        _failure.Visible = _failureText is not null && !PanelShown;
    }

    // ARC-INPUT-003: a short notice (the last reserved key) shown in the sticky header, above the scrolled detail, so it
    // is visible without scrolling; null removes it.
    internal void SetNotice(string? notice) { _notice = notice; ComposeSticky(); }
    private void ComposeSticky() { if (_sticky is not null) _sticky.Text = _notice is null ? _stickyBase : $"{_stickyBase}\n{_notice}"; }

    // ARC-UI-018: the height the interface bars' top strip takes at the top of the window (0 without the bars).
    private float _topInset;

    /// <summary>ARC-UI-018: moves the panel below the interface bars' top strip, whose buttons it would otherwise cover.</summary>
    internal void SetTopInset(float inset)
    {
        if (_topInset == inset) return;
        _topInset = inset;
        ResizeForViewport();
    }

    /// <summary>ARC-UI-024: starts the panel right of the follower frames down the window's left edge (the window x of their right
    /// edge; 0 without them), which would otherwise cover its left end.</summary>
    internal void SetLeftInset(float inset)
    {
        if (_panel is null || _panel.OffsetLeft == 16 + inset) return;
        _panel.OffsetLeft = 16 + inset;
        if (_failure is not null) _failure.OffsetLeft = 16 + inset;
    }

    private void ResizeForViewport()
    {
        if (_panel is null) return;
        var viewportHeight = GetViewport().GetVisibleRect().Size.Y - _topInset;
        var boundedHeight = Math.Min(Math.Max(1, viewportHeight - 32), Mathf.Clamp(viewportHeight * 0.35f, 110f, 360f));
        _panel.OffsetTop = 16 + _topInset;
        _panel.OffsetBottom = 16 + _topInset + boundedHeight;
        if (_failure is not null) _failure.OffsetTop = 16 + _topInset;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeForViewport;

    /// <summary>ARC-DEV-050: the panel is shown (ARC-DEV-052: it starts hidden; <see cref="ShowPanel"/> and
    /// <see cref="TogglePanel"/> show it).</summary>
    internal bool PanelShown => _panel?.Visible == true;

    /// <summary>ARC-DEV-052: the failure line is on screen (a failure detail while the panel is hidden).</summary>
    internal bool FailureLineShown => _failure?.Visible == true;
    internal string FailureLineText => _failure?.Text ?? string.Empty;

    /// <summary>ARC-DEV-052: shows the panel (the debug mode, an automated run), as the chord would.</summary>
    internal void ShowPanel(string source)
    {
        if (_panel is null || _panel.Visible) return;
        _panel.Visible = true;
        ComposeFailure();
        GD.Print($"ARC-DEV-052 diagnostic panel shown=True source={source}");
    }

    /// <summary>ARC-DEV-050: hides or shows the panel for this run, its text kept up to date while hidden.</summary>
    internal void TogglePanel()
    {
        if (_panel is null) return;
        _panel.Visible = !_panel.Visible;
        ComposeFailure();
        GD.Print($"ARC-DEV-050 diagnostic panel shown={_panel.Visible}");
    }

    /// <summary>ARC-DEV-050: Ctrl+Shift+F10 (next to the palette's Ctrl+Shift+F9; F10 alone stays the original's reserved
    /// interface key) toggles the panel in every mode and window. The chord's press and release are consumed here, before the
    /// world's key routing.</summary>
    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { CtrlPressed: true, ShiftPressed: true, AltPressed: false, MetaPressed: false } key
            || (key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode) != Key.F10) return;
        if (key.Pressed && !key.Echo) TogglePanel();
        GetViewport().SetInputAsHandled();
    }
}
