using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-3515: the launcher screen's "Install from Steam…": asks Steam for its console, copies the download command, finds the
// downloaded depot at its fixed folders and copies it into the folder the player picks. No Steam account data is read or stored.
public partial class TerrainViewerApp
{
    /// <summary>The self-test's platform (synthetic Steam folders) instead of this machine's.</summary>
    private SteamPlatform? _launcherSteamPlatformForSelfTest;
    /// <summary>The self-test's stand-in for the importer (synthetic files are smaller than the depot and their archives do not open).</summary>
    private Func<SteamDepotImporter>? _launcherImporterForSelfTest;
    /// <summary>The self-test's stand-ins for opening an address, the clipboard and the target folder dialog.</summary>
    private Func<string, bool>? _launcherOpenUriForSelfTest;
    private Action<string>? _launcherClipboardForSelfTest;
    private Action<string>? _launcherSteamPickForSelfTest;

    private void WireSteamInstall(LauncherPanel panel)
    {
        panel.SteamGuideRequested += () => LauncherChange(launcher => launcher.ToggleSteamGuide());
        panel.SteamConsoleRequested += () => LauncherChange(launcher =>
        {
            var opened = _launcherOpenUriForSelfTest?.Invoke(SteamDepot.ConsoleUri) ?? OS.ShellOpen(SteamDepot.ConsoleUri) == Error.Ok;
            GD.Print($"ARC-DEV-3515 steam console opened={opened}");
            launcher.SteamConsoleOpened(opened);
        });
        panel.SteamCopyRequested += () => LauncherChange(launcher =>
        {
            if (_launcherClipboardForSelfTest is { } stand) stand(SteamDepot.ConsoleCommand);
            else DisplayServer.ClipboardSet(SteamDepot.ConsoleCommand);
            launcher.SteamCommandCopied();
        });
        panel.SteamImportRequested += LauncherSteamImportRequested;
        panel.SteamImportChosen += (source, target) => _ = LauncherSteamImportAsync(source, target);
    }

    /// <summary>"Import the download…": the downloaded depot at its fixed folders goes to the target folder dialog; without one the screen
    /// names where it looked.</summary>
    private void LauncherSteamImportRequested()
    {
        if (_launcher is not { Busy: false } launcher) return;
        var platform = _launcherSteamPlatformForSelfTest ?? SteamPlatform.Current();
        var found = SteamDepot.Find(platform);
        GD.Print($"ARC-DEV-3515 steam download folder={found ?? "none"}");
        if (found is null)
        {
            launcher.SteamDownloadNotFound(SteamDepot.Candidates(platform));
            _launcherPanel?.Render(launcher);
            return;
        }
        if (_launcherSteamPickForSelfTest is { } pick) pick(found);
        else _launcherPanel?.PickSteamTarget(found);
    }

    /// <summary>Copies the download <paramref name="source"/> into <paramref name="target"/> off the main thread, shows its progress, and on
    /// success chooses the checked folder like a folder the player picked.</summary>
    private async Task LauncherSteamImportAsync(string source, string target)
    {
        if (_launcher is not { } launcher || launcher.Busy) return;
        var importer = _launcherImporterForSelfTest?.Invoke() ?? new SteamDepotImporter();
        launcher.BeginSteamImport(source, target);
        _launcherPanel?.Render(launcher);
        GD.Print($"ARC-DEV-3515 steam import start source={source} target={target}");
        using var cancel = _installCancel = new CancellationTokenSource();
        SteamImportResult result;
        try
        {
            var stage = (SteamImportStage?)null;
            result = await ViewerEngineTeardown.Work.Track(Task.Run(() => importer.ImportAsync(source, target, progress =>
            {
                // Progress comes from the copying thread: the screen is updated on the main thread, the log line only at a stage change.
                if (stage != progress.Stage) { stage = progress.Stage; GD.Print($"ARC-DEV-3515 steam import stage={progress.Stage} files={progress.FilesTotal} bytes={progress.BytesTotal}"); }
                Callable.From(() =>
                {
                    if (_launcher != launcher || !launcher.Busy) return;
                    launcher.SteamImportProgress(progress);
                    _launcherPanel?.Render(launcher);
                }).CallDeferred();
            }, cancel.Token)));
        }
        catch (OperationCanceledException) { result = new(SteamImportStatus.Cancelled); }
        catch (Exception error)
        {
            // Whatever escapes must still end the busy state, or Play and the folder buttons stay off for good.
            result = new(SteamImportStatus.CopyFailed, error.GetType().Name + ": " + error.Message);
        }
        finally { _installCancel = null; }
        GD.Print($"ARC-DEV-3515 steam import result status={result.Status} detail={result.Detail ?? "none"} folder={result.Folder?.Path ?? "none"}");
        if (_launcher != launcher) return; // the screen closed (Play cannot run while busy, so this is a quit)
        launcher.SteamImportDone(result);
        if (result.Folder is { } folder) PrintGameFolder("imported", folder);
        _launcherPanel?.Render(launcher);
    }
}
