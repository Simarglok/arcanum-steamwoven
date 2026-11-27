using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.Session.Launcher;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Why the launcher screen shows before the main menu (owner decision 2026-10-28, ARC-RES-074 question 2 A).</summary>
public enum LauncherShowReason { None, Requested, FirstRun, FolderGone, InstallationChanged, ShowAtStart }

/// <summary>
/// ARC-UI-014: the launcher screen of a start without command-line inputs (research ARC-RES-074 task 2), Godot-free. It shows on the
/// first run (no stored folder, or a settings file this build cannot use), when the stored folder is no longer a game folder, when the
/// installation changed, with "show at start", and on request (<c>--launcher</c> or Shift during the start). It holds the game folder
/// (the start's, a detected candidate or one the player chose, each with its check in words), the rule mode (the built-in Steamwoven and
/// Original; custom mixes after alpha) and "show at start". Play is offered only for a ready folder and gives the settings to store:
/// the start leaves a detected folder and a changed identity unwritten (ARC-DEV-046), so Play is where they are confirmed. Reset gives
/// the defaults, which the host writes over any file, a newer build's included. The display at start is kept as stored, not edited.
/// </summary>
public sealed class LauncherScreen
{
    private readonly List<GameFolderCheck> _candidates;

    public LauncherScreen(LauncherSettingsRead read, GameFolderStartup startup, LauncherShowReason reason)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(startup);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
        FileStatus = read.Status;
        Settings = read.Settings;
        Folder = startup.Folder ?? startup.StoredCheck;
        _candidates = (startup.Detection?.Checked ?? []).Where(check => check.Ready).ToList();
    }

    public LauncherShowReason Reason { get; }
    /// <summary>How the settings file read; a file this build cannot use (<see cref="FileUsable"/> false) is replaced only by Reset.</summary>
    public LauncherSettingsReadStatus FileStatus { get; private set; }
    /// <summary>The settings as edited (the rule mode and "show at start"); the folder fields change only on Play.</summary>
    public LauncherSettings Settings { get; private set; }
    /// <summary>The folder Play opens, with its check; null when none was stored, found or chosen.</summary>
    public GameFolderCheck? Folder { get; private set; }
    /// <summary>The ready fixed candidates the start found (GOG registry, default folders, CrossOver bottles).</summary>
    public IReadOnlyList<GameFolderCheck> Candidates => _candidates;
    public bool CanPlay => !Busy && Folder is { Ready: true, Identity: not null };
    /// <summary>ARC-DEV-074: a GOG installer is being unpacked, or (ARC-DEV-069) the game data is being prepared; the folder, Play and Reset are
    /// off until it ends (<see cref="InstallDone"/>, <see cref="PrototypesDone"/>).</summary>
    public bool Busy { get; private set; }
    /// <summary>ARC-DEV-069: the game data (the prototype files) is being prepared for the folder Play opened; the other busy state is the unpack.</summary>
    public bool Preparing { get; private set; }
    /// <summary>The text of the Cancel button while <see cref="Busy"/>.</summary>
    public string CancelText => Preparing ? "Cancel" : "Cancel unpacking";
    /// <summary>The installer being unpacked and its target folder; null when none is.</summary>
    public (string Installer, string Target)? Install { get; private set; }
    public bool FileUsable => FileStatus is LauncherSettingsReadStatus.Missing or LauncherSettingsReadStatus.Read or LauncherSettingsReadStatus.Invalid;
    public string? Notice { get; private set; }

    /// <summary>The start's reason (Godot-free): requested first, then the first of a missing stored folder, a stored folder that is
    /// no longer a game folder, a changed installation and "show at start"; none for a stored ready folder of the same installation.
    /// It is <see cref="LauncherShowReason.None"/> exactly when <see cref="GameFolderStartup.ShowLauncher"/> is false and not requested.</summary>
    public static LauncherShowReason Decide(LauncherSettings settings, GameFolderStartup startup, bool requested)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startup);
        if (requested) return LauncherShowReason.Requested;
        if (settings.GameRoot is null) return LauncherShowReason.FirstRun;
        if (startup.StoredCheck is not { Ready: true }) return LauncherShowReason.FolderGone;
        if (startup.IdentityChanged) return LauncherShowReason.InstallationChanged;
        return settings.ShowAtStart ? LauncherShowReason.ShowAtStart : LauncherShowReason.None;
    }

    /// <summary>A folder the player chose (<see cref="GameFolderLocator.Choose"/>'s check): it becomes the folder Play opens even when
    /// it is not ready, so its refusal is shown in words and Play stays off.</summary>
    public void UseFolder(GameFolderCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (Busy) return;
        Folder = check;
        Notice = check.Ready ? null : "Not a game folder the game can open: " + Describe(check);
    }

    /// <summary>The <paramref name="index"/>-th ready candidate; false for an index out of range.</summary>
    public bool UseCandidate(int index)
    {
        if (Busy || index < 0 || index >= _candidates.Count) return false;
        UseFolder(_candidates[index]);
        return true;
    }

    public void SelectRules(RuleMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Settings = Settings with { Rules = mode };
        Notice = null;
    }

    public void SetShowAtStart(bool show) { Settings = Settings with { ShowAtStart = show }; Notice = null; }

    /// <summary>ARC-DEV-050: the debug mode (every world-map area open to a teleport; never gameplay), stored by Play.</summary>
    public void SetDebugMode(bool on) { Settings = Settings with { DebugMode = on }; Notice = null; }

    /// <summary>ARC-DEV-2270: a held quit key (Cmd+Q) quits without the question; off, the held key asks as a short press does.</summary>
    public void SetHoldToQuit(bool on) { Settings = Settings with { HoldToQuit = on }; Notice = null; }

    /// <summary>ARC-RENDER-3420: the separate rendering thread (experimental), stored by Play, which restarts the game once to apply it.</summary>
    public void SetSeparateRenderThread(bool on) { Settings = Settings with { SeparateRenderThread = on }; Notice = null; }

    /// <summary>ARC-UI-3453: the three start-up movies skipped (off by default), stored by Play.</summary>
    public void SetSkipStartupMovies(bool on) { Settings = Settings with { SkipStartupMovies = on }; Notice = null; }

    /// <summary>The settings to store and open: the ready folder with its source and identity, the rule mode, "show at start" and the debug mode;
    /// null (with the notice) when no ready folder is chosen.</summary>
    public LauncherSettings? Play()
    {
        if (Busy) { Notice = Preparing ? "Wait for the game data to be prepared, or cancel it." : "Wait for the unpacking to finish, or cancel it."; return null; }
        if (!CanPlay)
        {
            Notice = Folder is null ? "Choose the game folder first." : "Not a game folder the game can open: " + Describe(Folder);
            return null;
        }
        Notice = null;
        return Settings.WithGameFolder(Folder!);
    }

    /// <summary>Every setting back to its default; the host writes the defaults over the file (<see cref="LauncherSettingsFile.Reset"/>)
    /// and reports with <see cref="ResetDone"/>. The shown folder stays, to be stored again by Play.</summary>
    public LauncherSettings Reset() => Settings = LauncherSettings.Defaults;

    /// <summary>ARC-DEV-074: no <c>innoextract</c> on this machine; the install option cannot start (nothing was picked yet).</summary>
    public void InstallToolMissing()
    {
        Notice = "Installing from the GOG installer needs the free innoextract tool, which was not found. "
                 + (OperatingSystem.IsMacOS() ? "Install it with: brew install innoextract" : OperatingSystem.IsWindows() ? "Install innoextract and put it on PATH." : "Install the innoextract package of your system.")
                 + " Then open this screen again, or unpack the installer by hand as the alpha instructions say.";
    }

    /// <summary>ARC-DEV-074: the unpack of <paramref name="installer"/> into <paramref name="target"/> starts; false (and nothing changes)
    /// while another one runs.</summary>
    public bool BeginInstall(string installer, string target)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(target);
        if (Busy) return false;
        Busy = true;
        Install = (installer, target);
        Notice = "Checking the installer…";
        return true;
    }

    public void InstallProgress(GogInstallProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!Busy) return;
        Notice = Describe(progress);
    }

    /// <summary>The unpack ended: a verified folder becomes the folder Play opens (stored by Play like any chosen folder); anything else
    /// is named in words and leaves the folder as it was.</summary>
    public void InstallDone(GogInstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var target = Install?.Target;
        Busy = false;
        Install = null;
        if (result.Succeeded && result.Folder is { Ready: true } folder)
        {
            UseFolder(folder);
            Notice = $"Unpacked the GOG installer into {folder.Path}; the folder is chosen. Press Play to start.";
        }
        else Notice = Describe(result, target);
    }

    /// <summary>ARC-DEV-069: Play found no prototype files in the folder and the game data starts to be prepared from the player's own
    /// <c>Arcanum.exe</c> (the first start only); false (and nothing changes) while something else runs.</summary>
    public bool BeginPrototypes()
    {
        if (Busy) return false;
        Busy = Preparing = true;
        Notice = Describe(new PrototypeProvisionProgress(PrototypeProvisionStage.Checking));
        return true;
    }

    public void PrototypesProgress(PrototypeProvisionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!Busy || !Preparing) return;
        Notice = Describe(progress);
    }

    /// <summary>The preparation ended. A success leaves the notice empty (the host starts the game); anything else is named in words and
    /// leaves the screen open, with Play still offered to try again.</summary>
    public void PrototypesDone(PrototypeProvisionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Busy = Preparing = false;
        Notice = result.Succeeded ? null : Describe(result);
    }

    public void ResetDone(bool written)
    {
        if (written) FileStatus = LauncherSettingsReadStatus.Read;
        Notice = written ? "Settings reset to the defaults." : "The settings file could not be written; the defaults apply to this start only.";
    }

    /// <summary>The reason line.</summary>
    public string ReasonText => Describe(Reason);

    public string FileText => FileStatus switch
    {
        LauncherSettingsReadStatus.Missing => "No settings stored yet.",
        LauncherSettingsReadStatus.Read => "Settings read.",
        LauncherSettingsReadStatus.Invalid => "The settings file was damaged; it is replaced when the settings are saved.",
        LauncherSettingsReadStatus.OtherSchema => "The settings file is a newer build's; it is kept until Reset settings replaces it.",
        _ => "The settings file cannot be read; it is kept until Reset settings replaces it.",
    };

    public string FolderText => Folder is null ? "Game folder: none found. Choose the folder of the installed game." : $"Game folder: {Folder.Path} ({Describe(Folder)})";

    public static string Describe(LauncherShowReason reason) => reason switch
    {
        LauncherShowReason.Requested => "Opened on request.",
        LauncherShowReason.FirstRun => "First start: confirm the game folder and the rules.",
        LauncherShowReason.FolderGone => "The stored game folder is no longer a game folder the game can open.",
        LauncherShowReason.InstallationChanged => "The installation in the game folder changed since it was stored.",
        LauncherShowReason.ShowAtStart => "Shown at every start.",
        _ => "",
    };

    /// <summary>A folder check in words: ready with its installation, or what is missing (names only, no bytes).</summary>
    public static string Describe(GameFolderCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var detail = check.Detail is { Length: > 0 } text ? $" ({text})" : "";
        return check.Status switch
        {
            GameFolderStatus.Ready => Installation(check.Identity),
            GameFolderStatus.MissingFolder => "the folder does not exist",
            GameFolderStatus.MissingTig => "no tig.dat here" + detail,
            GameFolderStatus.MissingModule => "no Arcanum module here" + detail,
            GameFolderStatus.AmbiguousName => "two names differ only in letter case" + detail,
            GameFolderStatus.UnsupportedLayout => "a layout the game cannot read" + detail,
            GameFolderStatus.Unreadable => "the folder cannot be read" + detail,
            GameFolderStatus.MissingModuleArchive => "no modules/Arcanum.dat here",
            GameFolderStatus.MissingActorArchive => "no arcanum1.dat here",
            GameFolderStatus.MissingTerrainArchive => "no arcanum2.dat here",
            _ => check.Status + detail,
        };
    }

    private static string Installation(GameFolderIdentity? identity)
    {
        if (identity is null) return "ready";
        var source = identity.GogMarker ? "GOG installation" : "installation";
        var version = identity.Version is { } v ? $", version {v}" : "";
        return $"ready: {source}{version}, " + (identity.VerifiedRelease ? "the verified 1.0.7.4 executable"
            : "not the verified 1.0.7.4 executable (only that release is checked; the game may differ)");
    }

    /// <summary>The unpack's progress line: the stage, the files and the megabytes of the listing.</summary>
    public static string Describe(GogInstallProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var counts = progress.FilesTotal > 0 ? $" {progress.FilesDone}/{progress.FilesTotal} files, {progress.BytesDone / (1024 * 1024)}/{progress.BytesTotal / (1024 * 1024)} MB" : "";
        return progress.Stage switch
        {
            GogInstallStage.Checking => "Checking the installer…",
            GogInstallStage.Verifying => "Verifying the installer's checksums…" + counts,
            GogInstallStage.Unpacking => "Unpacking the game files…" + counts,
            _ => "Checking the unpacked game folder…",
        };
    }

    /// <summary>The preparation of the game data in words: the player is never told to run <c>Arcanum.exe</c>; Steamwoven builds the files itself.</summary>
    public static string Describe(PrototypeProvisionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return progress.Stage switch
        {
            PrototypeProvisionStage.Checking => "Checking the game data…",
            PrototypeProvisionStage.Building => "Preparing the game data from your Arcanum.exe (first start only, about half a minute)…"
                + (progress.Total > 0 ? $" {progress.Done}/{progress.Total}" : ""),
            PrototypeProvisionStage.BuildingMaps => "Preparing the maps from your game files (first start only)…"
                + (progress.Total > 0 ? $" {progress.Done}/{progress.Total}" : ""),
            _ => "Saving the prepared game data…",
        };
    }

    /// <summary>A failed or refused preparation in words (a name or reason, never bytes).</summary>
    public static string Describe(PrototypeProvisionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var detail = result.Detail is { Length: > 0 } text ? $" ({text})" : "";
        return result.Status switch
        {
            PrototypeProvisionStatus.UnverifiedExecutable => "This installation lacks files the original writes at its first start (prototypes, maps) and its Arcanum.exe is not the retail 1.0.7.4 one, the only executable "
                + "the game data can be prepared from" + detail + ". Use the GOG 1.0.7.4 installation.",
            PrototypeProvisionStatus.NoExecutable => "The game data is prepared from the folder's Arcanum.exe, which was not found" + detail + ".",
            PrototypeProvisionStatus.SourceFailed => "The game files could not be read to prepare the game data" + detail + ".",
            PrototypeProvisionStatus.BuildFailed => "The game data could not be prepared from Arcanum.exe" + detail + ".",
            PrototypeProvisionStatus.CacheWriteFailed => "The prepared game data could not be saved in the settings folder" + detail + ".",
            PrototypeProvisionStatus.Cancelled => "Preparing the game data was cancelled. Press Play to start again.",
            _ => result.Status + detail,
        };
    }

    /// <summary>A failed or refused unpack in words (<paramref name="target"/> names where partial files may remain).</summary>
    public static string Describe(GogInstallResult result, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var detail = result.Detail is { Length: > 0 } text ? $" ({text})" : "";
        var partial = target is null ? "" : $" Files may be left in {target}; empty it or choose another folder before trying again.";
        return result.Status switch
        {
            GogInstallStatus.Unpacked => "Unpacked.",
            GogInstallStatus.InvalidPath => "That installer or folder cannot be used" + detail + ".",
            GogInstallStatus.InstallerMissing => "The installer file does not exist" + detail + ".",
            GogInstallStatus.ToolFailed => "innoextract could not be run" + detail + ".",
            GogInstallStatus.NotArcanumInstaller => "That is not the GOG offline installer of Arcanum" + detail + ".",
            GogInstallStatus.MissingParts => "The installer needs more files (.bin parts) next to it" + detail + ". Keep all the downloaded parts in one folder.",
            GogInstallStatus.TargetNotEmpty => "Choose a new or empty folder to unpack into; that one has files in it" + detail + ".",
            GogInstallStatus.NotEnoughSpace => "Not enough free disk space to unpack" + detail + ".",
            GogInstallStatus.VerifyFailed => "The installer failed its checksum test and was not unpacked" + detail + ". Download it again.",
            GogInstallStatus.ExtractFailed => "Unpacking failed" + detail + "." + partial,
            GogInstallStatus.NotAGameFolder => "The unpacked folder is not a game folder the game can open" + detail + "." + partial,
            GogInstallStatus.FingerprintMismatch => "The unpacked Arcanum.exe is not the verified 1.0.7.4 executable" + detail + "." + partial,
            GogInstallStatus.Cancelled => "Unpacking was cancelled." + partial,
            _ => result.Status + detail,
        };
    }

    public static string Origin(GameFolderOrigin origin) => origin switch
    {
        GameFolderOrigin.Registry => "GOG registry",
        GameFolderOrigin.DefaultFolder => "default folder",
        GameFolderOrigin.CrossOverBottle => "CrossOver bottle",
        GameFolderOrigin.Wrapper => "Wine wrapper",
        _ => "chosen",
    };
}
