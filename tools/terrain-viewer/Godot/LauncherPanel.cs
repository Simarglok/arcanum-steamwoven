using Arcanum.Presentation.Steamwoven;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-143, ARC-UI-2174: the HD lines of the launcher screen: HD (Off or On: the HD interface art and fonts and the relit lighting together),
/// the HD pack source (where the interface pack is read from) and the world zoom of the Steamwoven interface (shown only with HD On), with the note
/// saying what the next start reads. ARC-UI-3142: HD On is the Steamwoven interface and HD Off the Original look; the generated-interface row is retired. The host keeps the settings, the screen only shows and asks.</summary>
public readonly record struct LauncherHdLines(bool Hd, HdPackSourceMode Pack, string Note, int WorldZoom = 0, int WorldZoomCap = SteamwovenWorldZoom.MaxStep);

/// <summary>
/// ARC-UI-014: the launcher screen before the original main menu (research ARC-RES-074 task 2): why it shows, the settings file's
/// state, the game folder with its check in words, the ready candidates, "Choose folder…" (the system's folder dialog), the rule mode
/// (Steamwoven, Original), "show this screen at every start", and Play, Reset settings and Quit. ARC-UI-3432: the lines scroll when
/// the viewport is lower or narrower than they are, so every control and the whole help text stay reachable. The app owns the
/// <see cref="LauncherScreen"/> and renders it again after every change. The project's own look; it is not part of the original's menu.
/// </summary>
public partial class LauncherPanel : CanvasLayer
{
    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _contents = null!;
    private Label _hint = null!;
    /// <summary>ARC-UI-3432: wide enough that the whole screen, the help text included, fits a 1280x800 viewport without scrolling.</summary>
    private const float MaxPanelWidth = 960;
    private const int PanelMarginY = 16, ScreenMargin = 24, HintFontSize = 14;
    private Label _reason = null!;
    private Label _file = null!;
    private Label _folder = null!;
    private VBoxContainer _candidates = null!;
    private HBoxContainer _rules = null!;
    private CheckBox _showAtStart = null!;
    private CheckBox _debugMode = null!;
    private CheckBox _holdToQuit = null!;
    private CheckBox _renderThread = null!;
    private CheckBox _skipStartupMovies = null!;
    private HBoxContainer _hd1 = null!;
    private HBoxContainer _hdZoom = null!;
    private HBoxContainer _hdPack = null!;
    private Label _hdNote = null!;
    private LauncherHdLines _hd = new(false, HdPackSourceMode.Auto, "");
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
    public event Action<bool>? HoldToQuitRequested;
    public event Action<bool>? RenderThreadRequested;
    public event Action<bool>? SkipStartupMoviesRequested;
    /// <summary>ARC-UI-143, ARC-UI-2174: the HD line asked for Off or On (true), the HD pack line for a source. ARC-UI-3142: HD On is the Steamwoven interface; there is no generated-interface line.</summary>
    public event Action<bool>? HdRequested;
    public event Action<HdPackSourceMode>? HdPackRequested;
    /// <summary>ARC-UI-3059: the Steamwoven world zoom row asked for 0 (auto) or a whole step.</summary>
    public event Action<int>? HdWorldZoomRequested;
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
        "HD: " + Chosen(_hd1), "HD pack: " + Chosen(_hdPack), _hdNote.Text,
        // ARC-UI-3059: appended, so the numbers above stay. ARC-UI-3142: shown only with HD On.
        "World zoom: " + (_hdZoom.Visible ? Chosen(_hdZoom) : "hidden"),
        // ARC-RENDER-3420: appended, so the numbers above stay.
        "Separate rendering thread: " + (_renderThread.ButtonPressed ? "on" : "off"),
        // ARC-UI-3453: appended, so the numbers above stay.
        "Skip start-up movies: " + (_skipStartupMovies.ButtonPressed ? "on" : "off"),
    ];
    private static string Chosen(HBoxContainer row) => row.GetChildren().OfType<Button>().FirstOrDefault(button => button.ButtonPressed)?.Text ?? "none";
    internal bool HdEnabledForSelfTest => !_hd1.GetChildren().OfType<Button>().First().Disabled;
    internal void HdWorldZoomForSelfTest(int step) => HdWorldZoomRequested?.Invoke(step);
    /// <summary>The labels of the zoom row's buttons that are enabled (the steps the window offers, after Auto).</summary>
    internal IReadOnlyList<string> HdWorldZoomEnabledForSelfTest => _hdZoom.GetChildren().OfType<Button>().Where(button => !button.Disabled).Select(button => button.Text).ToArray();
    internal void HdForSelfTest(bool on) => HdRequested?.Invoke(on);
    internal void HdPackForSelfTest(HdPackSourceMode mode) => HdPackRequested?.Invoke(mode);
    internal bool PlayEnabledForSelfTest => !_play.Disabled;
    internal string CancelTextForSelfTest => _cancelInstall.Text;
    internal void ChooseForSelfTest(string path) => FolderChosen?.Invoke(path);
    internal void RulesForSelfTest(RuleMode mode) => RulesRequested?.Invoke(mode);
    internal void ShowAtStartForSelfTest(bool show) => ShowAtStartRequested?.Invoke(show);
    internal void DebugModeForSelfTest(bool on) => DebugModeRequested?.Invoke(on);
    internal void RenderThreadForSelfTest(bool on) => RenderThreadRequested?.Invoke(on);
    internal void SkipStartupMoviesForSelfTest(bool on) => SkipStartupMoviesRequested?.Invoke(on);
    internal void InstallRequestForSelfTest() => InstallRequested?.Invoke();
    internal void InstallForSelfTest(string installer, string target) => InstallChosen?.Invoke(installer, target);
    internal void CancelInstallForSelfTest() => CancelInstallRequested?.Invoke();
    internal void PlayForSelfTest() => PlayRequested?.Invoke();
    internal void ResetForSelfTest() => ResetRequested?.Invoke();

    /// <summary>ARC-UI-3432: the panel's rectangle, the size its lines ask for and the size shown at once.</summary>
    internal (Rect2 Panel, Vector2 Content, Vector2 View) LayoutForSelfTest => (_panel.GetRect(), _contents.GetCombinedMinimumSize(), _scroll.Size);
    internal void ScrollForSelfTest(bool toEnd) => _scroll.ScrollVertical = toEnd ? (int)_scroll.GetVScrollBar().MaxValue : 0;
    /// <summary>The end of the help text, the screen's last line, is inside the shown part.</summary>
    internal bool LastLineShownForSelfTest => _hint.GetGlobalRect().End.Y <= _scroll.GetGlobalRect().End.Y + 1;

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
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = PanelMarginY, ContentMarginBottom = PanelMarginY,
        });
        AddChild(_panel);
        // Both directions scroll (the default modes), so a row wider than a narrow viewport does not push the panel out of it.
        _scroll = new ScrollContainer { Name = "Scroll" };
        _panel.AddChild(_scroll);
        var contents = _contents = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _scroll.AddChild(contents);
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
        // ARC-UI-3453 (owner request 2026-11-27): the original always plays them, so off by default.
        _skipStartupMovies = new CheckBox { Name = "SkipStartupMovies", Text = "Skip the start-up movies and loading picture", FocusMode = Control.FocusModeEnum.None, Modulate = dark };
        _skipStartupMovies.Toggled += on => { if (!_rendering) SkipStartupMoviesRequested?.Invoke(on); };
        // ARC-DEV-2270: a close request (Cmd+Q, Alt+F4, the window's close button) asks the original's quit question; holding the key quits at once.
        _holdToQuit = new CheckBox { Name = "HoldToQuit", Text = "Hold Cmd+Q (Alt+F4) to quit without the question", FocusMode = Control.FocusModeEnum.None, Modulate = dark };
        _holdToQuit.Toggled += on => { if (!_rendering) HoldToQuitRequested?.Invoke(on); };
        // ARC-RENDER-3420: the engine's separate rendering thread; Play restarts the game once to apply it.
        _renderThread = new CheckBox
        {
            Name = "RenderThread", Text = "Separate rendering thread (experimental, may crash); restarts once", FocusMode = Control.FocusModeEnum.None,
            Modulate = dark,
        };
        _renderThread.Toggled += on => { if (!_rendering) RenderThreadRequested?.Invoke(on); };
        // ARC-DEV-050: a development aid, never gameplay; stored, so it stays on until it is turned off here.
        _debugMode = new CheckBox
        {
            Name = "DebugMode", Text = "Debug mode: every world-map area open, teleport from the world map (W); not gameplay", FocusMode = Control.FocusModeEnum.None,
            Modulate = new Color(.45f, .1f, .05f),
        };
        _debugMode.Toggled += on => { if (!_rendering) DebugModeRequested?.Invoke(on); };
        // ARC-UI-143, ARC-UI-2174, ARC-UI-3142: the HD lines (HD, HD pack, world zoom: the saved settings, no longer in the options window since ARC-UI-146), before the game starts.
        _hd1 = MakeChoiceRow("Hd", "HD:", dark, ["Off", "On"], index => HdRequested?.Invoke(index == 1));
        _hdPack = MakeChoiceRow("HdPack", "HD pack:", dark, ["Auto", "Game", "Custom"], index => HdPackRequested?.Invoke((HdPackSourceMode)index));
        _hdZoom = MakeChoiceRow("HdWorldZoom", "World zoom:", dark, ["Auto", "1x", "2x", "3x", "4x"], index => HdWorldZoomRequested?.Invoke(index));
        _hdNote = Line(new Color(.3f, .3f, .3f));
        // ARC-UI-3453: the two start options share one row, so the screen still fits 1280x800 with HD On (ARC-UI-3432).
        var startRow = new HBoxContainer { Name = "StartRow" };
        startRow.AddThemeConstantOverride("separation", 32);
        startRow.AddChild(_showAtStart); startRow.AddChild(_skipStartupMovies);
        var actions = new HBoxContainer();
        _play = MakeButton("Play", "Play", () => PlayRequested?.Invoke());
        actions.AddChild(_play);
        _reset = MakeButton("Reset", "Reset settings", () => ResetRequested?.Invoke());
        actions.AddChild(_reset);
        actions.AddChild(MakeButton("Quit", "Quit", () => QuitRequested?.Invoke()));
        _notice = Line(new Color(.45f, .1f, .05f));
        _notice.Visible = false;
        var hint = _hint = Line(new Color(.3f, .3f, .3f));
        hint.AddThemeFontSizeOverride("font_size", HintFontSize);
        hint.Text = "Steamwoven is the original game with the recorded fixes; Original keeps the retail 1.0.7.4 rules, bugs included. HD On is the Steamwoven interface (the column, HD fonts, the world zoom); HD Off is the Original look. "
                    + "The rules can also be switched in play in Options. The HD lines (HD: the HD interface art and fonts and the relit night lighting together; HD pack: where the interface pack is read from; World zoom, only while HD is On) are saved at once and apply from this start on (the pack source from the next one); they are set here only, so hold Shift at the start to change them; Reset settings leaves them alone. Hold Shift while the game starts, or start it with --launcher, to open this screen again. "
                    + "The game folder is only read, never written. An installation that the original game never started has no prototype files; "
                    + "the first Play prepares them from your own Arcanum.exe (about half a minute) and keeps them in Steamwoven's settings folder. "
                    + "Without Windows, \"Install from GOG installer…\" unpacks the GOG offline installer (setup_arcanum_….exe) with the free innoextract tool into a new folder you pick, and chooses it.";
        foreach (var control in new Control[] { _reason, _file, _folder, _candidates, chooseRow, _rules, _hd1, _hdPack, _hdZoom, _hdNote, startRow, _holdToQuit, _renderThread, _debugMode, actions, _notice, new Control { Name = "HintGap", CustomMinimumSize = new Vector2(0, 8) }, hint }) contents.AddChild(control);
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
        if (_hd1 is null) return;
        var was = _rendering; _rendering = true;
        try
        {
            void Mark(HBoxContainer row, int chosen) { var i = 0; foreach (var button in row.GetChildren().OfType<Button>()) { button.ButtonPressed = i++ == chosen; button.Disabled = _busy; } }
            Mark(_hd1, _hd.Hd ? 1 : 0);
            Mark(_hdPack, (int)_hd.Pack);
            // ARC-UI-3059, ARC-UI-3142: the world zoom is read only with HD On (the Steamwoven interface), so its row shows only then; the steps above the window's cap are not offered (Auto is index 0, step n is index n).
            _hdZoom.Visible = _hd.Hd;
            Mark(_hdZoom, Math.Clamp(_hd.WorldZoom, 0, SteamwovenWorldZoom.MaxStep));
            var zoomIndex = 0;
            foreach (var button in _hdZoom.GetChildren().OfType<Button>()) { button.Disabled |= zoomIndex > _hd.WorldZoomCap; zoomIndex++; }
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
                var button = MakeButton("Candidate" + i, $"Use {candidate.Path} ({LauncherScreen.Origin(candidate.Origin)})",
                    // Deferred: the render it causes frees this button.
                    () => Callable.From(() => CandidateRequested?.Invoke(index)).CallDeferred());
                button.AutowrapMode = TextServer.AutowrapMode.WordSmart; // ARC-UI-3432: a long path wraps, it never widens the screen
                _candidates.AddChild(button);
            }
            foreach (var button in _rules.GetChildren().OfType<Button>()) button.ButtonPressed = button.Name == screen.Settings.Rules.ToString();
            _showAtStart.ButtonPressed = screen.Settings.ShowAtStart;
            _holdToQuit.ButtonPressed = screen.Settings.HoldToQuit;
            _renderThread.ButtonPressed = screen.Settings.SeparateRenderThread;
            _skipStartupMovies.ButtonPressed = screen.Settings.SkipStartupMovies;
            _debugMode.ButtonPressed = screen.Settings.DebugMode;
            _busy = screen.Busy;
            _play.Disabled = !screen.CanPlay;
            _choose.Disabled = _install.Disabled = _reset.Disabled = screen.Busy;
            _cancelInstall.Visible = screen.Busy;
            _cancelInstall.Text = screen.CancelText;
            foreach (var button in _candidates.GetChildren().OfType<Button>()) button.Disabled = screen.Busy;
            _notice.Text = screen.Notice ?? "";
            _notice.Visible = _notice.Text.Length > 0;
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
        var width = Math.Max(1, Math.Min(MaxPanelWidth, size.X - 2 * ScreenMargin));
        // The scroll box asks for no height of its own: the panel takes the lines' height, at most the viewport's.
        var height = Math.Max(1, Math.Min(_contents.GetCombinedMinimumSize().Y + 2 * PanelMarginY, size.Y - 2 * ScreenMargin));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(ScreenMargin, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
