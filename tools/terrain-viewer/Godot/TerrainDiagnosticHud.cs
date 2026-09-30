using Godot;

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

    internal string Text => $"{_sticky?.Text}\n{_detail?.Text}";
    internal Vector2 GlobalPosition => _panel?.GlobalPosition ?? Vector2.Zero;
    internal float Height => _panel?.Size.Y ?? 0;
    internal bool IsScrollable => true;

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
        _sticky = new Label
        {
            Name = "DiagnosticSticky",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "DIAGNOSTIC — not gameplay/original parity",
        };
        contents.AddChild(_sticky);
        var scroll = new ScrollContainer { Name = "DiagnosticDetails", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        contents.AddChild(scroll);
        _detail = new Label { Name = "DiagnosticDetail", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        scroll.AddChild(_detail);
        GetViewport().SizeChanged += ResizeForViewport;
        ResizeForViewport();
    }

    internal void SetDiagnosticText(string detail, bool automatedTestMode)
    {
        if (_sticky is not null)
            _sticky.Text = automatedTestMode
                ? "AUTOMATED TEST MODE\nDIAGNOSTIC — not gameplay/original parity"
                : "DIAGNOSTIC — not gameplay/original parity";
        if (_detail is not null) _detail.Text = detail;
    }

    private void ResizeForViewport()
    {
        if (_panel is null) return;
        var viewportHeight = GetViewport().GetVisibleRect().Size.Y;
        var boundedHeight = Mathf.Clamp(viewportHeight * 0.35f, 110f, 360f);
        _panel.OffsetBottom = 16 + boundedHeight;
    }
}
