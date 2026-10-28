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
    public bool CanPlay => Folder is { Ready: true, Identity: not null };
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
        Folder = check;
        Notice = check.Ready ? null : "Not a game folder the game can open: " + Describe(check);
    }

    /// <summary>The <paramref name="index"/>-th ready candidate; false for an index out of range.</summary>
    public bool UseCandidate(int index)
    {
        if (index < 0 || index >= _candidates.Count) return false;
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

    /// <summary>The settings to store and open: the ready folder with its source and identity, the rule mode and "show at start";
    /// null (with the notice) when no ready folder is chosen.</summary>
    public LauncherSettings? Play()
    {
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

    public void ResetDone(bool written)
    {
        if (written) FileStatus = LauncherSettingsReadStatus.Read;
        Notice = written ? "Settings reset to the defaults." : "The settings file could not be written; the defaults apply to this start only.";
    }

    public void SaveFailed(LauncherSettingsSaveStatus status) => Notice = status switch
    {
        LauncherSettingsSaveStatus.Saved => null,
        LauncherSettingsSaveStatus.RefusedOtherSchema => "Not saved: the settings file is a newer build's; Reset settings replaces it.",
        LauncherSettingsSaveStatus.RefusedUnreadable => "Not saved: the settings file cannot be read; Reset settings replaces it.",
        _ => "Not saved: the settings file could not be written.",
    };

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

    public static string Origin(GameFolderOrigin origin) => origin switch
    {
        GameFolderOrigin.Registry => "GOG registry",
        GameFolderOrigin.DefaultFolder => "default folder",
        GameFolderOrigin.CrossOverBottle => "CrossOver bottle",
        GameFolderOrigin.Wrapper => "Wine wrapper",
        _ => "chosen",
    };
}
