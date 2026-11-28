using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.World;
using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-3515: the launcher self-test's "Install from Steam…" section, over synthetic Steam folders; no Steam is asked for anything and
// no dialog opens.
public partial class TerrainViewerApp
{
    private async Task LauncherSteamForSelfTest(string directory, string settingsPath, Action<bool, string> require, Func<bool, Task> start,
        Func<Task> play, Func<TerrainViewerArguments?> started)
    {
        string[] Lines() => [.. _launcherPanel!.LinesForSelfTest];
        var home = System.IO.Path.Combine(directory, "steam-home");
        var platform = new SteamPlatform(GameFolderPlatformKind.MacOS, home, null, () => null);
        string? opened = null, copied = null, picked = null;
        _launcherSteamPlatformForSelfTest = platform;
        _launcherOpenUriForSelfTest = uri => { opened = uri; return true; };
        _launcherClipboardForSelfTest = text => copied = text;
        _launcherSteamPickForSelfTest = source => picked = source;
        _launcherImporterForSelfTest = () => new SteamDepotImporter(new SteamImportOptions { MinimumBytes = 1, FreeSpace = _ => long.MaxValue, VerifyArchives = (_, _, _) => null });
        try
        {
            await start(true);
            require(Lines()[14] == "Steam steps: hidden", "the Steam steps are hidden at first: " + Lines()[14]);
            _launcherPanel!.SteamGuideForSelfTest();
            require(Lines()[14] == "Steam steps: shown", "Install from Steam shows the steps: " + Lines()[14]);
            await SteamStepsFit("HD Off", require);
            _launcherPanel.SteamConsoleForSelfTest();
            require(opened == "steam://open/console" && Lines()[5] == "Asked Steam to open its console.", "the console is asked for: " + Lines()[5]);
            _launcherPanel.SteamCopyForSelfTest();
            require(copied == "download_depot 500810 500811" && Lines()[5].StartsWith("Copied", StringComparison.Ordinal), "the command is copied: " + copied);
            _launcherPanel.SteamImportRequestForSelfTest();
            require(picked is null && Lines()[5].StartsWith("No downloaded depot was found", StringComparison.Ordinal), "no download is said: " + Lines()[5]);

            var depot = SteamDepot.Candidates(platform)[0];
            foreach (var name in new[] { "tig.dat", "arcanum1.dat", "arcanum2.dat", "modules/Arcanum.dat" })
            {
                var path = System.IO.Path.Combine(depot, name);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllBytes(path, [1, 2, 3]);
            }
            _launcherPanel.SteamImportRequestForSelfTest();
            require(picked == depot, "the downloaded depot is found: " + picked);
            var target = System.IO.Path.Combine(directory, "from-steam");
            _launcherPanel.SteamImportForSelfTest(depot, target);
            require(_launcher is { Busy: true, CanPlay: false } && !_launcherPanel.PlayEnabledForSelfTest && _launcherPanel.CancelTextForSelfTest == "Cancel copying",
                "while copying Play is off and Cancel shows: " + string.Join(" | ", Lines()));
            for (var frames = 0; _launcher!.Busy; frames++)
            {
                require(frames < 1200, "the copy did not end: " + string.Join(" | ", Lines()));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            require(_launcher is { CanPlay: true } && _launcher.Folder?.Path == target && Lines()[14] == "Steam steps: hidden"
                    && Lines()[5].StartsWith("Copied the Steam download into", StringComparison.Ordinal), "the copied folder is chosen: " + string.Join(" | ", Lines()));
            await play();
            require(started()?.GameRoot is { } root && System.IO.Path.GetFullPath(root) == target && LauncherSettingsFile.Load(settingsPath).Settings.GameRoot == root,
                "Play opens and stores the copied folder: " + started()?.GameRoot);
        }
        finally
        {
            (_launcherSteamPlatformForSelfTest, _launcherOpenUriForSelfTest, _launcherClipboardForSelfTest) = (null, null, null);
            (_launcherSteamPickForSelfTest, _launcherImporterForSelfTest) = (null, null);
        }
    }

    /// <summary>The steps take the help text's place, so the whole screen still fits 1280x800 (ARC-UI-3432).</summary>
    private async Task SteamStepsFit(string state, Action<bool, string> require)
    {
        var window = (SubViewport)GetViewport();
        var was = window.Size;
        window.Size = new(1280, 800);
        for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var (_, content, view) = _launcherPanel!.LayoutForSelfTest;
        require(content.X <= view.X + 1 && content.Y <= view.Y + 1, $"the screen with the Steam steps ({state}) fits 1280x800: lines {content}, shown {view}");
        window.Size = was;
    }
}
