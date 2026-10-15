using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>
/// Viewport-relative diagnostic HUD. It deliberately describes a bounded editor
/// diagnostic rather than gameplay or original-parity presentation.
/// </summary>
public partial class TerrainDiagnosticHud : CanvasLayer
{
    private PanelContainer? _panel;
    private Label? _sticky;
    private Label? _detail;
    private ScrollContainer? _scroll;
    private TerrainViewerMode _mode;
    private Button? _settings;
    public event Action? SettingsRequested;
    public void SetMode(TerrainViewerMode mode) { _mode = mode; if (_settings is not null) _settings.Visible = mode is TerrainViewerMode.WorldDiagnostic or TerrainViewerMode.CrashSiteArea; }

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
        _panel = new PanelContainer { Name = "TerrainDiagnosticPanel", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.OffsetLeft = 16;
        _panel.OffsetTop = 16;
        _panel.OffsetRight = -16;
        AddChild(_panel);

        var contents = new VBoxContainer { Name = "DiagnosticContents" };
        _panel.AddChild(contents);
        _settings = new Button { Text = "Input settings (F1 / Start)", Visible = false };
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
        GetViewport().SizeChanged += ResizeForViewport;
        ResizeForViewport();
    }

    internal void SetDiagnosticText(string detail, bool automatedTestMode)
    {
        var mode = _mode switch { TerrainViewerMode.CrashSiteArea => "crash-site-area", TerrainViewerMode.WorldDiagnostic => "world-diagnostic", TerrainViewerMode.ActorDiagnostic => "actor-diagnostic",
            TerrainViewerMode.CombinedStaticDiagnostic => "combined", _ => "terrain-only" };
        var limitation = _mode is TerrainViewerMode.WorldDiagnostic or TerrainViewerMode.CrashSiteArea
            ? "\nBounded adjacent grid — original Data admission, not gameplay\nOriginal placement/movement/visual parity: NOT VERIFIED"
            : _mode == TerrainViewerMode.ActorDiagnostic ? "\nOriginal placement parity: NOT VERIFIED" : string.Empty;
        if (_sticky is not null)
            _sticky.Text = $"{(automatedTestMode ? "AUTOMATED TEST MODE\n" : string.Empty)}Mode: {mode}\nDIAGNOSTIC — not gameplay/original parity{limitation}";
        if (_detail is not null) _detail.Text = detail;
    }

    private void ResizeForViewport()
    {
        if (_panel is null) return;
        var viewportHeight = GetViewport().GetVisibleRect().Size.Y;
        var boundedHeight = Math.Min(Math.Max(1, viewportHeight - 32), Mathf.Clamp(viewportHeight * 0.35f, 110f, 360f));
        _panel.OffsetBottom = 16 + boundedHeight;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeForViewport;
}
