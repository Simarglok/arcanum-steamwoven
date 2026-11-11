using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-143: the three HD lines of the launcher screen: HD graphics (the visual profile, off or on), HD interface (0 off, 1 on,
/// 2 generated, experimental) and the HD pack source, with the note saying what the next start reads. The same settings as the options
/// window's lines; the host keeps them, the screen only shows and asks.</summary>
public readonly record struct LauncherHdLines(bool Graphics, int Interface, HdPackSourceMode Pack, string Note);

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
    private HBoxContainer _hdGraphics = null!;
    private HBoxContainer _hdInterface = null!;
    private HBoxContainer _hdPack = null!;
    private Label _hdNote = null!;
    private LauncherHdLines _hd = new(false, 0, HdPackSourceMode.Auto, "");
    private Button _play = null!;
    private Button _choose = null!;
    private Button _install = null!;
    private Button _cancelInstall = null!;
    private Button _reset = null!;
    private Label _notice = null!;
    private FileDialog _dialog = null!;
    private FileDialog _installerDialog = null!;
    private FileDialog _targetDialog = null!;
    private string? _installerPath;
    private bool _busy;
    private bool _rendering;
    public event Action<int>? CandidateRequested;
    public event Action<string>? FolderChosen;
    public event Action<RuleMode>? RulesRequested;
    public event Action<bool>? ShowAtStartRequested;
    public event Action<bool>? DebugModeRequested;
    /// <summary>ARC-UI-143: the HD graphics line asked for a profile (true: HD), the HD interface line for 0, 1 or 2, the HD pack line for a source.</summary>
    public event Action<bool>? HdGraphicsRequested;
    public event Action<int>? HdInterfaceRequested;
    public event Action<HdPackSourceMode>? HdPackRequested;
    /// <summary>ARC-DEV-074: "Install from GOG installer…" pressed; the host checks the tool, then calls <see cref="PickInstaller"/>.</summary>
    public event Action? InstallRequested;
    /// <summary>The installer file and the empty folder to unpack into were picked.</summary>
    public event Action<string, string>? InstallChosen;
    public event Action? CancelInstallRequested;
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
        "Installer: " + (_busy ? "unpacking" : "idle") + (_install.Disabled ? ", install off" : ", install on") + (_cancelInstall.Visible ? ", cancel shown" : ""),
        // ARC-UI-143: appended after the lines above, so their numbers stay.
        "HD graphics: " + Chosen(_hdGraphics), "HD interface: " + Chosen(_hdInterface), "HD pack: " + Chosen(_hdPack), _hdNote.Text,
    ];
    private static string Chosen(HBoxContainer row) => row.GetChildren().OfType<Button>().FirstOrDefault(button => button.ButtonPressed)?.Text ?? "none";
    internal bool HdEnabledForSelfTest => !_hdGraphics.GetChildren().OfType<Button>().First().Disabled;
    internal void HdGraphicsForSelfTest(bool on) => HdGraphicsRequested?.Invoke(on);
    internal void HdInterfaceForSelfTest(int value) => HdInterfaceRequested?.Invoke(value);
    internal void HdPackForSelfTest(HdPackSourceMode mode) => HdPackRequested?.Invoke(mode);
    internal bool PlayEnabledForSelfTest => !_play.Disabled;
    internal string CancelTextForSelfTest => _cancelInstall.Text;
    internal void ChooseForSelfTest(string path) => FolderChosen?.Invoke(path);
    internal void RulesForSelfTest(RuleMode mode) => RulesRequested?.Invoke(mode);
    internal void ShowAtStartForSelfTest(bool show) => ShowAtStartRequested?.Invoke(show);
    internal void DebugModeForSelfTest(bool on) => DebugModeRequested?.Invoke(on);
    internal void InstallRequestForSelfTest() => InstallRequested?.Invoke();
    internal void InstallForSelfTest(string installer, string target) => InstallChosen?.Invoke(installer, target);
    internal void CancelInstallForSelfTest() => CancelInstallRequested?.Invoke();
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
        _choose = MakeButton("ChooseFolder", "Choose folder…", () => _dialog.PopupCentered(new Vector2I(720, 480)));
        // ARC-DEV-074: the GOG offline installer, unpacked with innoextract into a folder the player picks; Cancel shows while it runs.
        _install = MakeButton("InstallFromInstaller", "Install from GOG installer…", () => InstallRequested?.Invoke());
        _install.CustomMinimumSize = new Vector2(220, 30);
        _cancelInstall = MakeButton("CancelInstall", "Cancel unpacking", () => CancelInstallRequested?.Invoke());
        _cancelInstall.Visible = false;
        var chooseRow = new HBoxContainer { Name = "ChooseRow" };
        chooseRow.AddChild(_choose); chooseRow.AddChild(_install); chooseRow.AddChild(_cancelInstall);
        _rules = new HBoxContainer { Name = "Rules" };
        _rules.AddChild(new Label { Text = "Rules:", Modulate = dark, CustomMinimumSize = new Vector2(120, 30) });
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
        // ARC-UI-143: the HD lines of the options window (HD graphics, HD interface, HD pack), the same saved settings, before the game starts.
        _hdGraphics = MakeChoiceRow("HdGraphics", "HD graphics:", dark, ["Off", "On"], index => HdGraphicsRequested?.Invoke(index == 1));
        _hdInterface = MakeChoiceRow("HdInterface", "HD interface:", dark, ["Off", "On", "Generated (experimental)"], index => HdInterfaceRequested?.Invoke(index));
        _hdPack = MakeChoiceRow("HdPack", "HD pack:", dark, ["Auto", "Game", "Custom"], index => HdPackRequested?.Invoke((HdPackSourceMode)index));
        _hdNote = Line(new Color(.3f, .3f, .3f));
        var actions = new HBoxContainer();
        _play = MakeButton("Play", "Play", () => PlayRequested?.Invoke());
        actions.AddChild(_play);
        _reset = MakeButton("Reset", "Reset settings", () => ResetRequested?.Invoke());
        actions.AddChild(_reset);
        actions.AddChild(MakeButton("Quit", "Quit", () => QuitRequested?.Invoke()));
        _notice = Line(new Color(.45f, .1f, .05f));
        var hint = Line(new Color(.3f, .3f, .3f));
        hint.Text = "Steamwoven is the original game with the recorded fixes; Original keeps the retail 1.0.7.4 rules, bugs included. "
                    + "The rules can also be switched in play in Options. The HD lines are the options window's: they are saved at once and apply from this start on (the pack source from the next one); Reset settings leaves them alone. Hold Shift while the game starts, or start it with --launcher, to open this screen again. "
                    + "The game folder is only read, never written. An installation that the original game never started has no prototype files; "
                    + "the first Play prepares them from your own Arcanum.exe (about half a minute) and keeps them in Steamwoven's settings folder. "
                    + "Without Windows, \"Install from GOG installer…\" unpacks the GOG offline installer (setup_arcanum_….exe) with the free innoextract tool into a new folder you pick, and chooses it.";
        foreach (var control in new Control[] { _reason, _file, _folder, _candidates, chooseRow, _rules, _hdGraphics, _hdInterface, _hdPack, _hdNote, _showAtStart, _debugMode, actions, _notice, hint }) contents.AddChild(control);
        _dialog = new FileDialog
        {
            Name = "ChooseGameFolder", FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = "Choose the folder of the installed game",
        };
        _dialog.DirSelected += path => FolderChosen?.Invoke(path);
        AddChild(_dialog);
        _installerDialog = new FileDialog
        {
            Name = "ChooseInstaller", FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = "Choose the GOG offline installer of Arcanum (setup_arcanum_….exe)",
        };
        _installerDialog.AddFilter("*.exe", "GOG offline installer");
        _targetDialog = new FileDialog
        {
            Name = "ChooseUnpackFolder", FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = "Choose a new or empty folder to unpack the game into",
        };
        _installerDialog.FileSelected += path =>
        {
            _installerPath = path;
            // Deferred: the next native dialog opens after this one has closed.
            Callable.From(() => _targetDialog.PopupCentered(new Vector2I(720, 480))).CallDeferred();
        };
        _targetDialog.DirSelected += path => { if (_installerPath is { } installer) InstallChosen?.Invoke(installer, path); _installerPath = null; };
        AddChild(_installerDialog); AddChild(_targetDialog);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>Opens the installer file dialog, then the folder dialog; <see cref="InstallChosen"/> follows both choices.</summary>
    public void PickInstaller()
    {
        _installerPath = null;
        _installerDialog.PopupCentered(new Vector2I(720, 480));
    }

    /// <summary>A label and one toggle button for each choice (like the Rules row); pressing one asks for its index.</summary>
    private HBoxContainer MakeChoiceRow(string name, string label, Color color, string[] choices, Action<int> chosen)
    {
        var row = new HBoxContainer { Name = name };
        row.AddChild(new Label { Text = label, Modulate = color, CustomMinimumSize = new Vector2(120, 30) });
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            var button = MakeButton(name + i, choices[i], () => { if (!_rendering) chosen(index); });
            button.ToggleMode = true;
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>The HD lines to show; the next <see cref="Render"/> (or <see cref="RenderHd"/>) draws them.</summary>
    public LauncherHdLines Hd { get => _hd; set { _hd = value; RenderHd(); } }

    private void RenderHd()
    {
        if (_hdGraphics is null) return;
        var was = _rendering; _rendering = true;
        try
        {
            void Mark(HBoxContainer row, int chosen) { var i = 0; foreach (var button in row.GetChildren().OfType<Button>()) { button.ButtonPressed = i++ == chosen; button.Disabled = _busy; } }
            Mark(_hdGraphics, _hd.Graphics ? 1 : 0);
            Mark(_hdInterface, Math.Clamp(_hd.Interface, 0, 2));
            Mark(_hdPack, (int)_hd.Pack);
            _hdNote.Text = _hd.Note;
            _hdNote.Visible = _hd.Note.Length > 0;
        }
        finally { _rendering = was; }
        ResizePanel();
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
            _busy = screen.Busy;
            _play.Disabled = !screen.CanPlay;
            _choose.Disabled = _install.Disabled = _reset.Disabled = screen.Busy;
            _cancelInstall.Visible = screen.Busy;
            _cancelInstall.Text = screen.CancelText;
            foreach (var button in _candidates.GetChildren().OfType<Button>()) button.Disabled = screen.Busy;
            _notice.Text = screen.Notice ?? "";
            RenderHd();
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
