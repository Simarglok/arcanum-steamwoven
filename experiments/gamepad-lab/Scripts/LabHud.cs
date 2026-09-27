using Godot;

namespace GamepadLab;

public enum ModalKind { None, Context, Settings }

public partial class LabHud : Control
{
    private Label _banner = null!; private Label _diagnostics = null!; private Label _prompt = null!; private Label _log = null!;
    private PanelContainer _modal = null!; private Label _modalText = null!;
    private string[] _options = []; private int _selected;
    public ModalKind ModalKind { get; private set; }
    public bool ModalVisible => ModalKind != ModalKind.None;

    public override void _Ready()
    {
        _banner = Label("SYNTHETIC GAMEPAD INPUT LAB — NO ORIGINAL DATA", new Vector2(16, 12)); _banner.AddThemeFontSizeOverride("font_size", 20); AddChild(_banner);
        _diagnostics = Label("Diagnostics pending", new Vector2(16, 54)); _diagnostics.AutowrapMode = TextServer.AutowrapMode.WordSmart; _diagnostics.Size = new Vector2(390, 320); AddChild(_diagnostics);
        _log = Label("Synthetic log", new Vector2(16, 540)); _log.Size = new Vector2(620, 210); _log.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_log);
        _prompt = Label("[A / Enter] Select a target", new Vector2(390, 748)); _prompt.AddThemeFontSizeOverride("font_size", 18); AddChild(_prompt);
        _modal = new PanelContainer { Position = new Vector2(820, 64), Size = new Vector2(430, 360), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _modalText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _modal.AddChild(_modalText); AddChild(_modal);
    }

    public void UpdateView(string diagnostics, string prompt, IEnumerable<string> logs) { _diagnostics.Text = diagnostics; _prompt.Text = prompt; _log.Text = string.Join("\n", logs.TakeLast(8)); }
    public void ShowContext(string primary) { ModalKind = ModalKind.Context; _options = [primary, "Inspect", "Cancel"]; _selected = 0; RenderModal(); }
    public void ShowSettings() { ModalKind = ModalKind.Settings; _options = ["Deadzone −", "Deadzone +", "Sensitivity −", "Sensitivity +", "Reset defaults", "Close"]; _selected = 0; RenderModal(); }
    public void Navigate(int direction) { if (ModalVisible && direction != 0) { _selected = (_selected + Math.Sign(direction) + _options.Length) % _options.Length; RenderModal(); } }
    public string Activate() => ModalVisible ? _options[_selected] : "";
    public void SelectAt(Vector2 position)
    {
        if (!ModalVisible || !_modal.GetGlobalRect().HasPoint(position)) return;
        var titleLines = ModalKind == ModalKind.Settings ? 4 : 3;
        var index = (int)((position.Y - _modal.GlobalPosition.Y - 20) / 28) - titleLines;
        if (index >= 0 && index < _options.Length) { _selected = index; RenderModal(); }
    }
    public void HideModal() { ModalKind = ModalKind.None; _modal.Visible = false; _options = []; }
    private void RenderModal()
    {
        var title = ModalKind == ModalKind.Context ? "CONTEXTUAL ACTIONS\n[A/Enter] choose  [B/Esc] cancel\n" : "SETTINGS\nKeyboard/D-pad/stick: navigate; A confirms\nMouse: choose a row then click\n";
        _modalText.Text = title + string.Join("\n", _options.Select((option, index) => $"{(index == _selected ? ">" : " ")} {option}"));
        _modal.Visible = true;
    }
    private static Label Label(string text, Vector2 position) => new() { Text = text, Position = position };
}