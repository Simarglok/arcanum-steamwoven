using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-014: the launcher screen before the original main menu (research ARC-RES-074 task 2): why it shows, the settings file's
/// state, the game folder with its check in words, the ready candidates, "Choose folder…" (the system's folder dialog), the rule mode
/// (Steamwoven, Original), "show this screen at every start", and Play, Reset settings and Quit. The app owns the
/// <see cref="LauncherScreen"/> and renders it again after every change. The project's own look; it is not part of the original's menu.
/// </summary>
public partial class LauncherPanel : CanvasLayer
{
    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private Label _reason = null!;
    private Label _file = null!;
    private Label _folder = null!;
    private VBoxContainer _candidates = null!;
    private HBoxContainer _rules = null!;
    private CheckBox _showAtStart = null!;
    private CheckBox _debugMode = null!;
    private Button _play = null!;
    private Label _notice = null!;
    private FileDialog _dialog = null!;
    private bool _rendering;
    public event Action<int>? CandidateRequested;
    public event Action<string>? FolderChosen;
    public event Action<RuleMode>? RulesRequested;
    public event Action<bool>? ShowAtStartRequested;
    public event Action<bool>? DebugModeRequested;
    public event Action? PlayRequested;
    public event Action? ResetRequested;
    public event Action? QuitRequested;
    public bool IsOpen => Visible;
    /// <summary>The screen's lines: reason, file, folder, rules (the chosen one in brackets), show at start, notice, then the debug mode.</summary>
    internal IReadOnlyList<string> LinesForSelfTest =>
    [
        _reason.Text, _file.Text, _folder.Text,
        "Rules: " + string.Join(" ", _rules.GetChildren().OfType<Button>().Select(button => button.ButtonPressed ? $"[{button.Text}]" : button.Text)),
        "Show at every start: " + (_showAtStart.ButtonPressed ? "on" : "off"), _notice.Text, "Debug mode: " + (_debugMode.ButtonPressed ? "on" : "off"),
    ];
    internal bool PlayEnabledForSelfTest => !_play.Disabled;
    internal void ChooseForSelfTest(string path) => FolderChosen?.Invoke(path);
    internal void RulesForSelfTest(RuleMode mode) => RulesRequested?.Invoke(mode);
    internal void ShowAtStartForSelfTest(bool show) => ShowAtStartRequested?.Invoke(show);
    internal void DebugModeForSelfTest(bool on) => DebugModeRequested?.Invoke(on);
    internal void PlayForSelfTest() => PlayRequested?.Invoke();
    internal void ResetForSelfTest() => ResetRequested?.Invoke();

    public override void _Ready()
    {
        Layer = 5;
        _backdrop = new ColorRect { Name = "LauncherBackdrop", Color = new Color(.06f, .05f, .04f, 1f), MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_backdrop);
        _backdrop.AddChild(ViewerBuild.MakeLabel()); // ARC-DEV-047: which build this is
        _panel = new PanelContainer { Name = "Launcher", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .98f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var dark = new Color(.15f, .1f, .05f);
        Label Line(Color color) => new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = color };
        contents.AddChild(new Label { Text = "Arcanum Steamwoven", HorizontalAlignment = HorizontalAlignment.Center, Modulate = dark });
        _reason = Line(dark); _file = Line(new Color(.3f, .3f, .3f)); _folder = Line(new Color(.1f, .1f, .1f));
        _candidates = new VBoxContainer { Name = "Candidates" };
        var choose = MakeButton("ChooseFolder", "Choose folder…", () => _dialog.PopupCentered(new Vector2I(720, 480)));
        _rules = new HBoxContainer { Name = "Rules" };
        _rules.AddChild(new Label { Text = "Rules:", Modulate = dark, CustomMinimumSize = new Vector2(80, 30) });
        foreach (var mode in Enum.GetValues<RuleMode>())
        {
            var button = MakeButton(mode.ToString(), mode.ToString(), () => { if (!_rendering) RulesRequested?.Invoke(mode); });
            button.ToggleMode = true;
            _rules.AddChild(button);
        }
        _showAtStart = new CheckBox { Name = "ShowAtStart", Text = "Show this screen at every start", FocusMode = Control.FocusModeEnum.None, Modulate = dark };
        _showAtStart.Toggled += on => { if (!_rendering) ShowAtStartRequested?.Invoke(on); };
        // ARC-DEV-050: a development aid, never gameplay; stored, so it stays on until it is turned off here.
        _debugMode = new CheckBox
        {
            Name = "DebugMode", Text = "Debug mode: every world-map area open, teleport from the world map (W); not gameplay", FocusMode = Control.FocusModeEnum.None,
            Modulate = new Color(.45f, .1f, .05f),
        };
        _debugMode.Toggled += on => { if (!_rendering) DebugModeRequested?.Invoke(on); };
        var actions = new HBoxContainer();
        _play = MakeButton("Play", "Play", () => PlayRequested?.Invoke());
        actions.AddChild(_play);
        actions.AddChild(MakeButton("Reset", "Reset settings", () => ResetRequested?.Invoke()));
        actions.AddChild(MakeButton("Quit", "Quit", () => QuitRequested?.Invoke()));
        _notice = Line(new Color(.45f, .1f, .05f));
        var hint = Line(new Color(.3f, .3f, .3f));
        hint.Text = "Steamwoven is the original game with the recorded fixes; Original keeps the retail 1.0.7.4 rules, bugs included. "
                    + "The rules can also be switched in play in Options. Hold Shift while the game starts, or start it with --launcher, to open this screen again. "
                    + "The game folder is only read, never written.";
        foreach (var control in new Control[] { _reason, _file, _folder, _candidates, choose, _rules, _showAtStart, _debugMode, actions, _notice, hint }) contents.AddChild(control);
        _dialog = new FileDialog
        {
            Name = "ChooseGameFolder", FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = "Choose the folder of the installed game",
        };
        _dialog.DirSelected += path => FolderChosen?.Invoke(path);
        AddChild(_dialog);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private static Button MakeButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 30) };
        button.Pressed += action;
        return button;
    }

    public void Render(LauncherScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _rendering = true;
        try
        {
            Show();
            _reason.Text = screen.ReasonText;
            _file.Text = screen.FileText;
            _folder.Text = screen.FolderText;
            foreach (var child in _candidates.GetChildren()) { _candidates.RemoveChild(child); child.QueueFree(); }
            for (var i = 0; i < screen.Candidates.Count; i++)
            {
                var index = i;
                var candidate = screen.Candidates[i];
                if (candidate.Path == screen.Folder?.Path) continue;
                _candidates.AddChild(MakeButton("Candidate" + i, $"Use {candidate.Path} ({LauncherScreen.Origin(candidate.Origin)})", 
                    // Deferred: the render it causes frees this button.
                    () => Callable.From(() => CandidateRequested?.Invoke(index)).CallDeferred()));
            }
            foreach (var button in _rules.GetChildren().OfType<Button>()) button.ButtonPressed = button.Name == screen.Settings.Rules.ToString();
            _showAtStart.ButtonPressed = screen.Settings.ShowAtStart;
            _debugMode.ButtonPressed = screen.Settings.DebugMode;
            _play.Disabled = !screen.CanPlay;
            _notice.Text = screen.Notice ?? "";
        }
        finally { _rendering = false; }
        ResizePanel();
    }

    public void CloseView()
    {
        Hide();
        foreach (var child in _candidates.GetChildren()) { _candidates.RemoveChild(child); child.QueueFree(); }
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _backdrop.Position = Vector2.Zero; _backdrop.Size = size;
        var width = Math.Max(1, Math.Min(640, size.X - 48));
        _panel.Size = new(width, 0);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - _panel.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
