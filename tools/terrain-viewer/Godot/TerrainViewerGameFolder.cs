using Arcanum.Data.Content;
using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-046: a start without command-line inputs (a player's build) reads the game folder from the launcher settings
// (user://launcher_v1.json), or detects one at the fixed GOG candidates when none is stored or the stored one is no longer a game
// folder, writes the folder found and its identity back, and opens map mode on the main map with the main menu, as ./arcanum area
// does. The launcher screen (ARC-RES-074 task 2) is not built yet: where it would show, the start only says so. The developer
// launcher always passes its inputs, so its runs never come here.
public partial class TerrainViewerApp
{
    /// <summary>Isolates a test's launcher settings file; an automated run without it has no file at all.</summary>
    public const string LauncherSettingsTestVariable = "LAUNCHER_SETTINGS_TEST_PATH";
    public const string LauncherSettingsUserPath = "user://" + LauncherSettingsFile.FileName;

    /// <summary>The arguments of the game folder found, or null after showing why none was found.</summary>
    private TerrainViewerArguments? GameFolderArguments()
    {
        var test = OS.GetEnvironment(LauncherSettingsTestVariable);
        var path = !string.IsNullOrWhiteSpace(test) ? test : ViewerAutomatedStorage.Automated ? null : ProjectSettings.GlobalizePath(LauncherSettingsUserPath);
        var read = path is null ? LauncherSettingsCodec.Deserialize(null) : LauncherSettingsFile.Load(path);
        GD.Print($"ARC-DEV-046 launcher settings status={read.Status} file={path ?? "none (automated run)"} defaulted=[{string.Join(',', read.Defaulted)}] unknown=[{string.Join(',', read.UnknownKeys)}]");
        var startup = GameFolderStartup.Resolve(read.Settings, GameFolderPlatform.Current());
        if (startup.StoredCheck is { } stored) PrintGameFolder("stored", stored);
        foreach (var check in startup.Detection?.Checked ?? []) PrintGameFolder("candidate", check);
        if (!startup.Ready)
        {
            GD.Print("ARC-DEV-046 game folder not found: the launcher screen (ARC-RES-074 task 2) is not built yet; store a folder in "
                     + LauncherSettingsFile.FileName + " or install the game at a GOG default folder");
            ShowStatus("Diagnostic mode unresolved | failure stage=GameFolder code=NotFound | terrain 0/0/0 | static census 0/admitted 0/outside-scope 0");
            return null;
        }
        var folder = startup.Folder!;
        if (startup.SettingsChanged)
            GD.Print($"ARC-DEV-046 launcher settings save={(path is null ? "skipped (automated run)" : LauncherSettingsFile.Save(path, startup.Settings).ToString())}");
        GD.Print($"ARC-DEV-046 game folder ready source={startup.Source} origin={folder.Origin} gog={folder.Identity!.GogMarker} "
                 + $"verifiedRelease={folder.Identity.VerifiedRelease} version={folder.Identity.Version ?? "none"} identityChanged={startup.IdentityChanged} "
                 + $"showLauncher={startup.ShowLauncher}{(startup.ShowLauncher ? " (launcher screen not built yet)" : "")}");
        return TerrainViewerArguments.ForGameFolder(folder.Inputs!);
    }

    private static void PrintGameFolder(string kind, GameFolderCheck check)
        => GD.Print($"ARC-DEV-046 game folder {kind} origin={check.Origin} status={check.Status} detail={check.Detail ?? "none"} path={check.Path}");
}
