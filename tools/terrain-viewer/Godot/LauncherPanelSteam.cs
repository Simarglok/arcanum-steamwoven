using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-3515: "Install from Steam…": the steps of the Steam console download (open the console, copy the command, import the
// download) with its two folder dialogs. The steps take the help text's place while they are shown, so the screen keeps its height.
public partial class LauncherPanel
{
    private VBoxContainer _steamGuide = null!;
    private Button _steam = null!;
    private Button _steamImport = null!;
    private Button _steamChooseSource = null!;
    private FileDialog _steamSourceDialog = null!;
    private FileDialog _steamTargetDialog = null!;
    private string? _steamSource;

    private Button MakeSteamButton()
    {
        _steam = MakeButton("InstallFromSteam", "Install from Steam…", () => SteamGuideRequested?.Invoke());
        _steam.CustomMinimumSize = new Vector2(180, 30);
        return _steam;
    }

    private void MakeSteamGuide(Color dark)
    {
        _steamGuide = new VBoxContainer { Name = "SteamGuide", Visible = false };
        _steamGuide.AddChild(new Label { Text = LauncherScreen.SteamGuideText, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = dark });
        var console = new HBoxContainer { Name = "SteamConsoleRow" };
        console.AddChild(MakeButton("SteamConsole", "Open the Steam console", () => SteamConsoleRequested?.Invoke()));
        console.AddChild(new Label { Name = "SteamCommand", Text = SteamDepot.ConsoleCommand, Modulate = dark });
        console.AddChild(MakeButton("SteamCopy", "Copy command", () => SteamCopyRequested?.Invoke()));
        console.AddThemeConstantOverride("separation", 16);
        _steamGuide.AddChild(console);
        var import = new HBoxContainer { Name = "SteamImportRow" };
        _steamImport = MakeButton("SteamImport", "Import the download…", () => SteamImportRequested?.Invoke());
        _steamChooseSource = MakeButton("SteamChooseSource", "Choose the downloaded folder…", () => PickSteamSource());
        import.AddChild(_steamImport); import.AddChild(_steamChooseSource);
        _steamGuide.AddChild(import);
        _steamSourceDialog = new FileDialog
        {
            Name = "ChooseSteamDownload", FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = "Choose the downloaded folder depot_" + SteamDepot.DepotId,
        };
        _steamTargetDialog = new FileDialog
        {
            Name = "ChooseSteamTarget", FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Title = "Choose a new or empty folder to copy the game into",
        };
        // Deferred: the next native dialog opens after this one has closed.
        _steamSourceDialog.DirSelected += path => Callable.From(() => PickSteamTarget(path)).CallDeferred();
        _steamTargetDialog.DirSelected += path => { if (_steamSource is { } source) SteamImportChosen?.Invoke(source, path); _steamSource = null; };
        AddChild(_steamSourceDialog); AddChild(_steamTargetDialog);
    }

    /// <summary>Opens the dialog of the downloaded folder (a Steam folder in an unusual place); the target dialog follows.</summary>
    private void PickSteamSource()
    {
        _steamSource = null;
        _steamSourceDialog.PopupCentered(new Vector2I(720, 480));
    }

    /// <summary>Opens the folder dialog to copy <paramref name="source"/> into; <see cref="SteamImportChosen"/> follows the choice.</summary>
    public void PickSteamTarget(string source)
    {
        _steamSource = source;
        _steamTargetDialog.PopupCentered(new Vector2I(720, 480));
    }

    private void RenderSteam(LauncherScreen screen)
    {
        _steamGuide.Visible = screen.SteamGuideOpen;
        _hint.Visible = !screen.SteamGuideOpen;
        _steam.Disabled = _steamImport.Disabled = _steamChooseSource.Disabled = screen.Busy;
    }

    internal void SteamGuideForSelfTest() => SteamGuideRequested?.Invoke();
    internal void SteamConsoleForSelfTest() => SteamConsoleRequested?.Invoke();
    internal void SteamCopyForSelfTest() => SteamCopyRequested?.Invoke();
    internal void SteamImportRequestForSelfTest() => SteamImportRequested?.Invoke();
    internal void SteamImportForSelfTest(string source, string target) => SteamImportChosen?.Invoke(source, target);
}
