using Godot;

namespace GamepadLab;

public enum ModalKind { None, Context, Settings }

/// <summary>Responsive HUD: only the visible modal captures pointer input.</summary>
public partial class LabHud : Control
{
    private Label _banner = null!; private Label _diagnostics = null!; private Label _prompt = null!; private Label _log = null!;
    private PanelContainer _modal = null!; private VBoxContainer _modalRows = null!;
    private readonly List<Label> _choiceRows = [];
    private string[] _options = []; private int _selected;
    public ModalKind ModalKind { get; private set; }
    public bool ModalVisible => ModalKind != ModalKind.None;
    public int SelectedIndex => _selected;
    public string SelectedOption => ModalVisible ? _options[_selected] : "";
    public string PromptText => _prompt.Text;
    public string DiagnosticsText => _diagnostics.Text;
    /// <summary>The reticle is drawn in this CanvasLayer, not in world space.</summary>
    public Vector2 ReticleCanvasCenter => GetGlobalTransformWithCanvas() * (Size * .5f);
    public IReadOnlyDictionary<string, Rect2> LayoutBounds => new Dictionary<string, Rect2>
    {
        ["banner"] = _banner.GetGlobalRect(), ["diagnostics"] = _diagnostics.GetGlobalRect(),
        ["modal"] = _modal.GetGlobalRect(), ["log"] = _log.GetGlobalRect(), ["prompt"] = _prompt.GetGlobalRect()
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var margins = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margins.AddThemeConstantOverride("margin_left", 16); margins.AddThemeConstantOverride("margin_top", 12);
        margins.AddThemeConstantOverride("margin_right", 16); margins.AddThemeConstantOverride("margin_bottom", 12); AddChild(margins);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10); margins.AddChild(column);

        _banner = TextLabel("SYNTHETIC GAMEPAD INPUT LAB — NO ORIGINAL DATA", 20); _banner.CustomMinimumSize = new Vector2(0, 30); column.AddChild(_banner);
        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 16); column.AddChild(body);
        var diagnosticPanel = new PanelContainer { CustomMinimumSize = new Vector2(360, 260), MouseFilter = MouseFilterEnum.Ignore };
        _diagnostics = TextLabel("Diagnostics pending", 16); _diagnostics.AutowrapMode = TextServer.AutowrapMode.WordSmart; diagnosticPanel.AddChild(Padded(_diagnostics)); body.AddChild(diagnosticPanel);
        body.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _modal = new PanelContainer { CustomMinimumSize = new Vector2(360, 260), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _modalRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        _modalRows.AddThemeConstantOverride("separation", 6); _modal.AddChild(Padded(_modalRows)); body.AddChild(_modal);

        var logPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 130), MouseFilter = MouseFilterEnum.Ignore };
        _log = TextLabel("Synthetic log", 15); _log.AutowrapMode = TextServer.AutowrapMode.WordSmart; logPanel.AddChild(Padded(_log)); column.AddChild(logPanel);
        _prompt = TextLabel("[A / Enter] Select a target", 18); _prompt.CustomMinimumSize = new Vector2(0, 28); column.AddChild(_prompt);
    }

    public void UpdateView(string diagnostics, string prompt, IEnumerable<string> logs) { _diagnostics.Text = diagnostics; _prompt.Text = prompt; _log.Text = string.Join("\n", logs.TakeLast(8)); QueueRedraw(); }
    public void ShowContext(string primary) { ModalKind = ModalKind.Context; _options = [primary, "Inspect", "Cancel"]; _selected = 0; RenderModal(); }
    public void ShowSettings() { ModalKind = ModalKind.Settings; _options = ["Deadzone −", "Deadzone +", "Sensitivity −", "Sensitivity +", "Reset defaults", "Close"]; _selected = 0; RenderModal(); }
    public void Navigate(int direction) { if (ModalVisible && direction != 0) { _selected = (_selected + Math.Sign(direction) + _options.Length) % _options.Length; RenderModal(); } }
    public string Activate() => SelectedOption;
    public void SelectAt(Vector2 position)
    {
        if (!ModalVisible || !_modal.GetGlobalRect().HasPoint(position)) return;
        for (var index = 0; index < _choiceRows.Count; index++)
            if (_choiceRows[index].GetGlobalRect().HasPoint(position)) { _selected = index; RenderModal(); return; }
    }
    public void HideModal() { ModalKind = ModalKind.None; _modal.Visible = false; _options = []; _choiceRows.Clear(); }
    public bool HasReadableLayout(Rect2 viewport)
    {
        var bounds = LayoutBounds;
        return bounds.Values.All(rect => rect.Size.X >= 20 && rect.Size.Y >= 20 && viewport.Encloses(rect))
            && (!ModalVisible || !bounds["diagnostics"].Intersects(bounds["modal"]))
            && bounds["log"].Position.Y < bounds["prompt"].Position.Y;
    }
    public override void _Draw()
    {
        var center = Size * .5f;
        DrawCircle(center, 11, Colors.Transparent, false, 2);
        DrawLine(center - new Vector2(16, 0), center + new Vector2(16, 0), Colors.White);
        DrawLine(center - new Vector2(0, 16), center + new Vector2(0, 16), Colors.White);
    }
    private void RenderModal()
    {
        foreach (var child in _modalRows.GetChildren()) child.QueueFree();
        _choiceRows.Clear();
        var title = TextLabel(ModalKind == ModalKind.Context ? "CONTEXTUAL ACTIONS\n[A/Enter] choose  [B/Esc] cancel" : "SETTINGS\nKeyboard/D-pad/stick: navigate; A confirms\nMouse: choose a row then click", 16);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; _modalRows.AddChild(title);
        for (var index = 0; index < _options.Length; index++)
        {
            var row = TextLabel($"{(index == _selected ? ">" : " ")} {_options[index]}", 18);
            row.CustomMinimumSize = new Vector2(0, 24); _modalRows.AddChild(row); _choiceRows.Add(row);
        }
        _modal.Visible = true;
    }
    private static Label TextLabel(string text, int fontSize) { var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", fontSize); return label; }
    private static MarginContainer Padded(Control child)
    {
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 10); margin.AddThemeConstantOverride("margin_top", 10); margin.AddThemeConstantOverride("margin_right", 10); margin.AddThemeConstantOverride("margin_bottom", 10); margin.AddChild(child); return margin;
    }
}
