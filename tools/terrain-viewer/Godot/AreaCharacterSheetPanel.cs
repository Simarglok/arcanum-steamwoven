using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-007 diagnostic character sheet: the session hero's values as the original sheet shows them
/// (<see cref="AreaCharacterSheet.Lines"/>), read only, with a close button. The app rebuilds it whenever the session changes.
/// </summary>
public partial class AreaCharacterSheetPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _body = null!;
    private Label _hint = null!;
    private Button _close = null!;
    private AreaCharacterSheetView? _view;
    public event Action? CloseRequested;
    public bool IsOpen => Visible;
    internal AreaCharacterSheetView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _body.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaCharacterSheet", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var header = new Label { Text = "Character sheet", Modulate = new Color(1f, .85f, .55f) };
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _close = new Button { Name = "Close", Text = "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label
        {
            Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "C, Escape or cancel closes | PROJECT diagnostic window, read only: no point spending, skills, tech disciplines or spells; original UI parity NOT_VERIFIED",
        };
        foreach (var control in new Control[] { header, _body, _close, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(AreaCharacterSheetView view)
    {
        Show(); Render(view);
        _close.GrabFocus();
    }

    public void CloseView()
    {
        _view = null; Hide();
        GetViewport().GuiReleaseFocus();
    }

    public void Render(AreaCharacterSheetView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        _body.Text = string.Join("\n", AreaCharacterSheet.Lines(view));
        ResizePanel();
    }

    /// <summary>The close button is the only control: confirm presses it.</summary>
    public void Confirm() => _close.EmitSignal(BaseButton.SignalName.Pressed);

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
