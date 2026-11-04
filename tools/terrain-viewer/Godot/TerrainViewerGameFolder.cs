using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.Session.Launcher;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-046: a start without command-line inputs (a player's build) reads the game folder from the launcher settings
// (user://launcher_v1.json), or detects one at the fixed GOG candidates when none is stored or the stored one is no longer a game
// folder (for this start only), adds the identity of a stored folder that had none, and opens map mode on the main map with the main
// menu, as ./arcanum area does. The developer launcher always passes its inputs, so its runs never come here.
// ARC-UI-014: the launcher screen shows first on the first run, when the stored folder is gone, when the installation changed, with
// "show at start", and on request (--launcher, or Shift held while the start waits for it); its Play stores the folder it opens with
// the rule mode and "show at start". Every ordinary run, with inputs or without, takes its rule mode from the same file, and the
// options window's Rules line writes it there.
public partial class TerrainViewerApp
{
    /// <summary>Isolates a test's launcher settings file; an automated run without it has no file at all.</summary>
    public const string LauncherSettingsTestVariable = "LAUNCHER_SETTINGS_TEST_PATH";
    public const string LauncherSettingsUserPath = "user://" + LauncherSettingsFile.FileName;
    /// <summary>ARC-UI-014: how long a start that would skip the launcher screen waits for Shift (an ordinary run only).</summary>
    private const ulong LauncherShiftWaitMsec = 750;

    private bool _launcherPathResolved;
    private string? _launcherPath;
    private LauncherSettingsRead? _launcherRead;
    private LauncherSettings _launcherSettings = LauncherSettings.Defaults;
    private LauncherPanel? _launcherPanel;
    private LauncherScreen? _launcher;
    private Task? _gameFolderStart;
    /// <summary>ARC-DEV-074: stops the running unpack (Cancel, Quit).</summary>
    private CancellationTokenSource? _installCancel;
    /// <summary>The self-test's stand-in for the unpacker (null result: no tool) instead of this machine's innoextract.</summary>
    private Func<GogInstallerUnpacker?>? _launcherUnpackerForSelfTest;
    /// <summary>The self-test's stand-in for the start Play begins (a synthetic folder cannot load).</summary>
    private Action<TerrainViewerArguments>? _launcherStartForSelfTest;
    /// <summary>The self-test's platform (synthetic candidate folders) instead of this machine's.</summary>
    private GameFolderPlatform? _launcherPlatformForSelfTest;

    /// <summary>The launcher settings file of this run: the test path, none for an automated run without one, else the user's.</summary>
    private string? LauncherSettingsPath()
    {
        if (_launcherPathResolved) return _launcherPath;
        var test = OS.GetEnvironment(LauncherSettingsTestVariable);
        _launcherPath = !string.IsNullOrWhiteSpace(test) ? test : ViewerAutomatedStorage.Automated ? null : ProjectSettings.GlobalizePath(LauncherSettingsUserPath);
        _launcherPathResolved = true;
        return _launcherPath;
    }

    /// <summary>The launcher settings, read once a run (<see cref="_launcherSettings"/> then follows Play, Reset and the Rules line).</summary>
    private LauncherSettingsRead LauncherSettingsRead()
    {
        if (_launcherRead is { } read) return read;
        var path = LauncherSettingsPath();
        read = path is null ? LauncherSettingsCodec.Deserialize(null) : LauncherSettingsFile.Load(path);
        GD.Print($"ARC-DEV-046 launcher settings status={read.Status} file={path ?? "none (automated run)"} defaulted=[{string.Join(',', read.Defaulted)}] unknown=[{string.Join(',', read.UnknownKeys)}]");
        _launcherSettings = read.Settings;
        return _launcherRead = read;
    }

    /// <summary>A start without inputs: the stored or detected folder, through the launcher screen when it shows.</summary>
    private async Task StartFromGameFolderAsync(bool requested)
    {
        // The start runs on after _Ready returns: a failure here would otherwise fault an unobserved task, leaving the window on its
        // loading line (or a smoke waiting for its timeout).
        try
        {
            var read = LauncherSettingsRead();
            var startup = GameFolderStartup.Resolve(read.Settings, _launcherPlatformForSelfTest ?? GameFolderPlatform.Current());
            if (startup.StoredCheck is { } stored) PrintGameFolder("stored", stored);
            foreach (var check in startup.Detection?.Checked ?? []) PrintGameFolder("candidate", check);
            if (startup.SettingsChanged)
            {
                var path = LauncherSettingsPath();
                GD.Print($"ARC-DEV-046 launcher settings save={(path is null ? "skipped (automated run)" : LauncherSettingsFile.Save(path, startup.Settings).ToString())}");
                _launcherSettings = startup.Settings;
                read = read with { Settings = startup.Settings };
            }
            if (startup.Folder is { } folder)
                GD.Print($"ARC-DEV-046 game folder ready source={startup.Source} origin={folder.Origin} gog={folder.Identity!.GogMarker} "
                         + $"verifiedRelease={folder.Identity.VerifiedRelease} version={folder.Identity.Version ?? "none"} identityChanged={startup.IdentityChanged} "
                         + $"showLauncher={startup.ShowLauncher}");
            else GD.Print("ARC-DEV-046 game folder not found");
            var reason = LauncherScreen.Decide(read.Settings, startup, requested);
            if (reason == LauncherShowReason.None && !ViewerAutomatedStorage.Automated && await ShiftDuringStart()) reason = LauncherShowReason.Requested;
            GD.Print($"ARC-UI-014 launcher reason={reason}");
            if (reason == LauncherShowReason.None) { StartFromLauncher(startup.Folder!); return; }
            OpenLauncher(new LauncherScreen(read, startup, reason));
            if (_nativeSmoke) await RunLauncherSmokeAsync();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            GD.PrintErr($"ARC-UI-014 game folder start FAILED: {error.GetType().Name}: {error.Message}");
            ShowFailure("Diagnostic mode unresolved | failure stage=GameFolder code=StartFailed | terrain 0/0/0 | static census 0/admitted 0/outside-scope 0");
            if (_nativeSmoke) GetTree().Quit(1);
        }
    }

    /// <summary>Shift pressed at the start or while the start waits <see cref="LauncherShiftWaitMsec"/> for it.</summary>
    private async Task<bool> ShiftDuringStart()
    {
        ShowStatus("Diagnostic mode unresolved | loading stage=GameFolder | hold Shift to open the launcher");
        var end = Time.GetTicksMsec() + LauncherShiftWaitMsec;
        while (!global::Godot.Input.IsPhysicalKeyPressed(Key.Shift) && !global::Godot.Input.IsKeyPressed(Key.Shift))
        {
            if (Time.GetTicksMsec() >= end || _exiting) return false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return true;
    }

    private void OpenLauncher(LauncherScreen screen)
    {
        _launcher = screen;
        if (_launcherPanel is null)
        {
            _launcherPanel = new LauncherPanel(); AddChild(_launcherPanel);
            _launcherPanel.CandidateRequested += index => LauncherChange(launcher => launcher.UseCandidate(index));
            _launcherPanel.FolderChosen += path => LauncherChange(launcher =>
            {
                var check = GameFolderLocator.Choose(path);
                PrintGameFolder("chosen", check);
                launcher.UseFolder(check);
            });
            _launcherPanel.RulesRequested += mode => LauncherChange(launcher => launcher.SelectRules(mode));
            _launcherPanel.ShowAtStartRequested += show => LauncherChange(launcher => launcher.SetShowAtStart(show));
            _launcherPanel.DebugModeRequested += on => LauncherChange(launcher => launcher.SetDebugMode(on));
            _launcherPanel.InstallRequested += LauncherInstallRequested;
            _launcherPanel.InstallChosen += (installer, target) => _ = LauncherInstallAsync(installer, target);
            _launcherPanel.CancelInstallRequested += () => { GD.Print("ARC-DEV-074 installer unpack cancel requested"); _installCancel?.Cancel(); };
            _launcherPanel.PlayRequested += LauncherPlay;
            _launcherPanel.ResetRequested += () => LauncherChange(LauncherReset);
            _launcherPanel.QuitRequested += () => { GD.Print("ARC-UI-014 launcher quit"); _installCancel?.Cancel(); GetTree().Quit(); };
        }
        _launcherPanel.Render(screen);
        GD.Print($"ARC-UI-014 launcher open reason={screen.Reason} file={screen.FileStatus} folder={screen.Folder?.Status.ToString() ?? "none"} canPlay={screen.CanPlay}");
    }

    private void LauncherChange(Action<LauncherScreen> change)
    {
        if (_launcher is not { } launcher) return;
        change(launcher);
        _launcherPanel?.Render(launcher);
    }

    private void LauncherReset(LauncherScreen launcher)
    {
        if (launcher.Busy) return;
        var defaults = launcher.Reset();
        var path = LauncherSettingsPath();
        var status = path is null ? (LauncherSettingsSaveStatus?)null : LauncherSettingsFile.Reset(path);
        launcher.ResetDone(status == LauncherSettingsSaveStatus.Saved);
        if (status == LauncherSettingsSaveStatus.Saved || path is null) _launcherSettings = defaults;
        GD.Print($"ARC-UI-014 launcher reset save={status?.ToString() ?? "skipped (automated run)"}");
    }

    private GogInstallerUnpacker? FindUnpacker()
        => _launcherUnpackerForSelfTest is { } stand ? stand() : GogInstallerUnpacker.FindTool() is { } tool ? new GogInstallerUnpacker(tool) : null;

    /// <summary>ARC-DEV-074: "Install from GOG installer…": without innoextract the screen says how to get it; otherwise the file and
    /// folder dialogs open.</summary>
    private void LauncherInstallRequested()
    {
        if (_launcher is not { Busy: false } launcher) return;
        if (FindUnpacker() is null)
        {
            GD.Print("ARC-DEV-074 installer unpack tool=missing");
            launcher.InstallToolMissing();
            _launcherPanel?.Render(launcher);
            return;
        }
        _launcherPanel?.PickInstaller();
    }

    /// <summary>ARC-DEV-074: unpacks <paramref name="installer"/> into <paramref name="target"/> off the main thread (the tool is a child
    /// process), shows its progress, and on success chooses the verified folder like a folder the player picked.</summary>
    private async Task LauncherInstallAsync(string installer, string target)
    {
        if (_launcher is not { } launcher || launcher.Busy) return;
        var unpacker = FindUnpacker();
        if (unpacker is null) { launcher.InstallToolMissing(); _launcherPanel?.Render(launcher); return; }
        launcher.BeginInstall(installer, target);
        _launcherPanel?.Render(launcher);
        GD.Print($"ARC-DEV-074 installer unpack start installer={installer} target={target}");
        using var cancel = _installCancel = new CancellationTokenSource();
        GogInstallResult result;
        try
        {
            var stage = (GogInstallStage?)null;
            result = await Task.Run(() => unpacker.UnpackAsync(installer, target, progress =>
            {
                // Progress comes from the tool's reader threads: the screen is updated on the main thread, the log line only at a stage change.
                if (stage != progress.Stage) { stage = progress.Stage; GD.Print($"ARC-DEV-074 installer unpack stage={progress.Stage} files={progress.FilesTotal} bytes={progress.BytesTotal}"); }
                Callable.From(() =>
                {
                    if (_launcher != launcher || !launcher.Busy) return;
                    launcher.InstallProgress(progress);
                    _launcherPanel?.Render(launcher);
                }).CallDeferred();
            }, cancel.Token));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            result = new(GogInstallStatus.ExtractFailed, error.GetType().Name + ": " + error.Message);
        }
        finally { _installCancel = null; }
        GD.Print($"ARC-DEV-074 installer unpack result status={result.Status} detail={result.Detail ?? "none"} folder={result.Folder?.Path ?? "none"}");
        if (_launcher != launcher) return; // the screen closed (Play cannot run while busy, so this is a quit)
        launcher.InstallDone(result);
        if (result.Folder is { } folder) PrintGameFolder("unpacked", folder);
        _launcherPanel?.Render(launcher);
    }

    private void LauncherPlay()
    {
        if (_launcher is not { } launcher) return;
        if (launcher.Play() is not { } settings) { _launcherPanel?.Render(launcher); return; }
        var path = LauncherSettingsPath();
        var saved = path is null ? null : (LauncherSettingsSaveStatus?)LauncherSettingsFile.Save(path, settings);
        // A refused or failed save does not stop the start: the chosen folder and rules apply to it.
        GD.Print($"ARC-UI-014 launcher play folder={settings.GameRoot} rules={RuleModeNames.Name(settings.Rules)} showAtStart={settings.ShowAtStart} "
                 + $"debugMode={settings.DebugMode} save={saved?.ToString() ?? "skipped (automated run)"}");
        _launcherSettings = settings;
        var folder = launcher.Folder!;
        _launcher = null;
        _launcherPanel?.CloseView();
        StartFromLauncher(folder);
    }

    /// <summary>The map-mode start over a ready folder (the launcher's Play, or a start the screen does not show for).</summary>
    private void StartFromLauncher(GameFolderCheck folder)
    {
        // ARC-DEV-050: the stored debug mode joins a --debug-mode flag; neither turns the other off.
        if (_launcherSettings.DebugMode) EnableDebugMode("launcher settings");
        var arguments = TerrainViewerArguments.ForGameFolder(folder.Inputs!);
        if (_launcherStartForSelfTest is { } stand) { stand(arguments); return; }
        try { StartWithArguments(arguments); }
        catch (ArgumentException)
        {
            ShowFailure("Diagnostic mode unresolved | failure stage=Arguments code=Usage | terrain 0/0/0 | static census 0/admitted 0/outside-scope 0");
        }
    }

    /// <summary>ARC-UI-014: the options window's Rules line, stored in the launcher settings (one owner per file: this store).
    /// The file is read again first and only its rule mode changes, so a folder another run stored since this one started is kept.</summary>
    private void SaveLauncherRules()
    {
        var path = LauncherSettingsPath();
        if (path is null)
        {
            _launcherSettings = _launcherSettings with { Rules = _ruleMode };
            GD.Print($"ARC-UI-014 launcher settings rules={RuleModeNames.Name(_ruleMode)} save=skipped (automated run)");
            return;
        }
        var current = LauncherSettingsFile.Load(path);
        var settings = current.Settings with { Rules = _ruleMode };
        var saved = LauncherSettingsFile.Save(path, settings);
        if (saved == LauncherSettingsSaveStatus.Saved) _launcherSettings = settings;
        GD.Print($"ARC-UI-014 launcher settings rules={RuleModeNames.Name(_ruleMode)} save={saved} file={current.Status}");
    }

    private static void PrintGameFolder(string kind, GameFolderCheck check)
        => GD.Print($"ARC-DEV-046 game folder {kind} origin={check.Origin} status={check.Status} detail={check.Detail ?? "none"} path={check.Path}");
}
