using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-011 main menu: a backdrop over the whole window and a column of the shown buttons of <see cref="AreaMainMenu"/>'s window
/// with their original labels (each with its hotkey letter), the hidden buttons' note and the notice; the pre-made browser shows the
/// browsed hero with Previous, Next, Start and Back. The app owns the state and rebuilds the window after every change; the
/// original's menu art and layout are not drawn. ARC-UI-012: the in-play windows, and the quit box as its question with Yes and No.
/// ARC-UI-013: the options window: the page's shown controls, each with its label, value and arrows, above the page and Done buttons.
/// </summary>
public partial class AreaMainMenuPanel : CanvasLayer
{
    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private VBoxContainer _buttons = null!;
    private VBoxContainer _controls = null!;
    private VBoxContainer _browser = null!;
    private Label _hero = null!;
    private Label _note = null!;
    private Label _notice = null!;
    private Label _hint = null!;
    public event Action<int>? ButtonPressed;
    public event Action<int>? BrowseRequested;
    public event Action? StartRequested;
    public event Action? BackRequested;
    public event Action<bool>? AnswerRequested;
    /// <summary>ARC-UI-013: an arrow of the options window's control (index among the shown ones, direction -1 or 1).</summary>
    public event Action<int, int>? OptionChangeRequested;
    public bool IsOpen => Visible;
    internal IReadOnlyList<string> ButtonsForSelfTest => _buttons.Visible ? _buttons.GetChildren().OfType<Button>().Select(button => button.Text).ToArray() : [];
    internal string HeroForSelfTest => _browser.Visible ? _hero.Text : "";
    internal string NoteForSelfTest => _note.Text;
    internal string NoticeForSelfTest => _notice.Text;
    internal string HeaderForSelfTest => _header.Text;
    /// <summary>ARC-UI-013: the options window's controls as "label: value".</summary>
    internal IReadOnlyList<string> ControlsForSelfTest => _controls.Visible
        ? _controls.GetChildren().OfType<HBoxContainer>().Select(row => row.GetChildren().OfType<Label>().ToArray()).Select(labels => $"{labels[0].Text}: {labels[1].Text}").ToArray() : [];
    internal void ChangeForSelfTest(int index, int direction) => OptionChangeRequested?.Invoke(index, direction);
    internal void PressForSelfTest(int index) => ButtonPressed?.Invoke(index);

    public override void _Ready()
    {
        Layer = 4;
        _backdrop = new ColorRect { Name = "MainMenuBackdrop", Color = new Color(.06f, .05f, .04f, .96f), MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_backdrop);
        _panel = new PanelContainer { Name = "AreaMainMenu", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.2f, .12f, .05f), HorizontalAlignment = HorizontalAlignment.Center };
        _buttons = new VBoxContainer { Name = "Buttons" };
        _controls = new VBoxContainer { Name = "OptionControls" };
        _browser = new VBoxContainer { Name = "Browser" };
        _hero = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.1f, .1f, .1f), CustomMinimumSize = new Vector2(320, 120) };
        var browse = new HBoxContainer();
        foreach (var (name, text, action) in new (string, string, Action)[]
        {
            ("Previous", "< Previous", () => BrowseRequested?.Invoke(-1)), ("Next", "Next >", () => BrowseRequested?.Invoke(1)),
            ("Start", "Start", () => StartRequested?.Invoke()), ("Back", "Back", () => BackRequested?.Invoke()),
        })
        {
            var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += action;
            browse.AddChild(button);
        }
        _browser.AddChild(_hero); _browser.AddChild(browse);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.3f, .3f, .3f) };
        _notice = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.45f, .1f, .05f) };
        _hint = new Label { Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        foreach (var control in new Control[] { _header, _controls, _buttons, _browser, _notice, _note, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>The window for the menu's state; <paramref name="hero"/> is the browsed hero's description (browser only).</summary>
    public void Render(AreaMainMenu menu, string hero)
    {
        ArgumentNullException.ThrowIfNull(menu);
        Show();
        var browser = menu.Window == AreaMenuWindow.PickCharacter;
        _header.Text = menu.QuitPending ? menu.Texts.Text(AreaMainMenuTexts.QuitQuestion) : menu.Window switch
        {
            AreaMenuWindow.Main => "Main menu", AreaMenuWindow.InPlay or AreaMenuWindow.InPlayLimited => "Game menu",
            AreaMenuWindow.SinglePlayer => menu.Texts.Text(10), AreaMenuWindow.NewGame => menu.Texts.Text(50),
            AreaMenuWindow.Options => menu.Texts.Text(12) + ": " + menu.Texts.Text(4000 + (int)menu.Page), _ => menu.Texts.Text(420),
        };
        foreach (var child in _buttons.GetChildren()) { _buttons.RemoveChild(child); child.QueueFree(); }
        RenderControls(menu);
        var options = menu.Window == AreaMenuWindow.Options;
        var focus = options ? menu.OptionsFocus - menu.Controls.Count : menu.Focus;
        var shown = menu.Shown;
        // The quit box (ARC-UI-012): the original's yes/no box with its fixed letters; the project's button texts.
        if (menu.QuitPending)
        {
            foreach (var (text, yes) in new[] { ($"Yes  ({AreaMainMenu.YesLetter})", true), ($"No  ({AreaMainMenu.NoLetter})", false) })
            {
                var answer = new Button { Name = yes ? "Yes" : "No", Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 34) };
                answer.Pressed += () => AnswerRequested?.Invoke(yes);
                _buttons.AddChild(answer);
            }
            shown = [];
        }
        for (var i = 0; i < shown.Count; i++)
        {
            var index = i;
            var letter = menu.Texts.Hotkey(shown[i]);
            var button = new Button
            {
                Name = "Button" + i, Text = menu.Texts.Text(shown[i].Message) + (letter is { } key ? $"  ({key})" : ""),
                FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 34),
                Modulate = i == focus ? new Color(1f, .85f, .6f) : Colors.White,
            };
            button.Pressed += () => ButtonPressed?.Invoke(index);
            _buttons.AddChild(button);
        }
        _buttons.Visible = !browser;
        _browser.Visible = browser;
        _hero.Text = browser ? hero : "";
        var hidden = menu.HiddenNotes;
        _note.Text = hidden.Count == 0 ? "" : "Not offered: " + string.Join("; ", hidden);
        _notice.Text = menu.Notice ?? "";
        _hint.Text = (browser ? "Previous/Next browse the pre-made heroes, Start begins a new game with that hero, Escape or Back goes back"
                : options && !menu.QuitPending ? "the arrows or Left/Right change a value at once (it is saved as a user preference, not in a save); "
                    + "a page's letter shows it, Done closes the window" + (menu.Root == AreaMenuWindow.Main ? "" : " and the menu, as the O key does")
                    + "; Escape does nothing here, as in the original; options texts "
                    + (menu.OptionsTexts.FromGame ? "from the game" : "are the project's (" + menu.OptionsTexts.Detail + ")")
                : menu.QuitPending ? "Y, Return or Yes quits to the main menu; N, No or Escape keeps playing (saves are not touched)"
                : "a button's letter or a click chooses it" + (menu.BackStack.Count > 0 ? ", Escape goes back"
                    : menu.Root != AreaMenuWindow.Main ? ", Escape continues" : menu.Reopened ? ", Escape exits the game" : ""))
            + " | PROJECT window over the original's menu windows; labels "
            + (menu.Texts.FromGame ? "from the game's MainMenu.mes" : "are the project's (" + menu.Texts.Detail + ")") + "; original art and layout NOT_VERIFIED";
        ResizePanel();
    }

    /// <summary>ARC-UI-013: the options window's controls of the shown page (none in other windows).</summary>
    private void RenderControls(AreaMainMenu menu)
    {
        foreach (var child in _controls.GetChildren()) { _controls.RemoveChild(child); child.QueueFree(); }
        _controls.Visible = menu.Window == AreaMenuWindow.Options && !menu.QuitPending;
        if (!_controls.Visible) return;
        var controls = menu.Controls;
        for (var i = 0; i < controls.Count; i++)
        {
            var index = i;
            var control = controls[i];
            var value = menu.Options?.Value(control.Option);
            var row = new HBoxContainer { Name = "Control" + i, Modulate = i == menu.OptionsFocus ? new Color(1f, .85f, .6f) : Colors.White };
            row.AddChild(new Label { Text = menu.OptionsTexts.Label(control), CustomMinimumSize = new Vector2(220, 30), Modulate = new Color(.15f, .1f, .05f) });
            var valueLabel = new Label { Text = value is { } v ? menu.OptionsTexts.Value(control, v) : "?", CustomMinimumSize = new Vector2(150, 30),
                HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(.1f, .1f, .1f) };
            foreach (var (name, text, direction) in new[] { ("Less", "<", -1), ("More", ">", 1) })
            {
                var arrow = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(34, 30) };
                arrow.Pressed += () => OptionChangeRequested?.Invoke(index, direction);
                if (direction < 0) { row.AddChild(arrow); row.AddChild(valueLabel); }
                else row.AddChild(arrow);
            }
            _controls.AddChild(row);
        }
    }

    public void CloseView()
    {
        Hide();
        foreach (var child in _buttons.GetChildren()) { _buttons.RemoveChild(child); child.QueueFree(); }
        foreach (var child in _controls.GetChildren()) { _controls.RemoveChild(child); child.QueueFree(); }
        GetViewport().GuiReleaseFocus();
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _backdrop.Position = Vector2.Zero; _backdrop.Size = size;
        var width = Math.Max(1, Math.Min(560, size.X - 48));
        _panel.Size = new(width, 0);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - _panel.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
