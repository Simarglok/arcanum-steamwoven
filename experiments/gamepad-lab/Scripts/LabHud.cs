using Godot;

namespace GamepadLab;
public partial class LabHud : Control
{
    private Label _banner = null!; private Label _diagnostics = null!; private Label _prompt = null!; private Label _log = null!; private PanelContainer _modal = null!; private Label _modalText = null!;
    public override void _Ready()
    {
        _banner = Label("SYNTHETIC GAMEPAD INPUT LAB — NO ORIGINAL DATA", new Vector2(16, 12)); _banner.AddThemeFontSizeOverride("font_size", 20); AddChild(_banner);
        _diagnostics = Label("Diagnostics pending", new Vector2(16, 54)); _diagnostics.AutowrapMode = TextServer.AutowrapMode.WordSmart; _diagnostics.Size = new Vector2(360, 230); AddChild(_diagnostics);
        _log = Label("Synthetic log", new Vector2(16, 540)); _log.Size = new Vector2(520, 190); _log.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_log);
        _prompt = Label("[A / Enter] Select a target", new Vector2(390, 748)); _prompt.AddThemeFontSizeOverride("font_size", 18); AddChild(_prompt);
        _modal = new PanelContainer { Position = new Vector2(845, 70), Size = new Vector2(350, 260), Visible = false }; _modalText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = "" }; _modal.AddChild(_modalText); AddChild(_modal);
    }
    public void UpdateView(string diagnostics, string prompt, IEnumerable<string> logs) { _diagnostics.Text = diagnostics; _prompt.Text = prompt; _log.Text = string.Join("\n", logs.TakeLast(7)); }
    public void ShowModal(string text) { _modalText.Text = text; _modal.Visible = true; }
    public void HideModal() => _modal.Visible = false;
    public bool ModalVisible => _modal is not null && _modal.Visible;
    private static Label Label(string text, Vector2 position) => new() { Text = text, Position = position };
}
