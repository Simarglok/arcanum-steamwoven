using Arcanum.Data.Content;
using Arcanum.Session.Settings;

namespace Arcanum.Session.Launcher;

public enum LauncherSettingsSaveStatus { Saved, RefusedOtherSchema, RefusedUnreadable, Failed }

/// <summary>ARC-DEV-046, ARC-UI-1130: the launcher's settings on disk, as the <see cref="Line"/> line of the one settings file
/// (<see cref="ArcanumConfig"/>, <c>arcanum.cfg</c>; they were a file of their own, <c>launcher_v1.json</c>, before). The host picks the
/// path (Godot's <c>user://</c> for a player, a test path or none for an automated run). Reading and writing never throw for the file's
/// state: a missing, unreadable or invalid file reads as the defaults with its status; a write changes this line only (every other setting
/// of the file stays), keeps the unknown keys of the object it replaces, and never replaces a launcher object of another schema (a newer
/// build's) or a file it cannot read.</summary>
public static class LauncherSettingsFile
{
    public const string FileName = ArcanumConfig.FileName;
    /// <summary>The name of the settings line that holds the launcher object (compact JSON, see <see cref="LauncherSettingsCodec"/>).</summary>
    public const string Line = "launcher";

    public static LauncherSettingsRead Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var config = ArcanumConfig.Read(path);
        return config.Status switch
        {
            ArcanumConfigReadStatus.Invalid => Invalid(),
            ArcanumConfigReadStatus.Unreadable => new(LauncherSettingsReadStatus.Unreadable, LauncherSettings.Defaults, [], []),
            _ => LauncherSettingsCodec.Deserialize(config.Get(Line)),
        };
    }

    private static LauncherSettingsRead Invalid() => new(LauncherSettingsReadStatus.Invalid, LauncherSettings.Defaults, [], []);

    /// <summary>Writes <paramref name="settings"/>; settings the codec cannot write back throw (a caller error, not a file state).</summary>
    public static LauncherSettingsSaveStatus Save(string path, LauncherSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(settings);
        var current = Load(path);
        if (current.Status == LauncherSettingsReadStatus.OtherSchema) return LauncherSettingsSaveStatus.RefusedOtherSchema;
        if (current.Status == LauncherSettingsReadStatus.Unreadable) return LauncherSettingsSaveStatus.RefusedUnreadable;
        return Write(path, LauncherSettingsCodec.Serialize(settings, current, indented: false), replaceUnreadable: false);
    }

    /// <summary>ARC-UI-014: the player's "reset settings" of the launcher: the defaults replace the launcher line, one of another schema or
    /// a file that cannot be read included (the only write that does), and keep none of its keys. The other settings of the file stay.</summary>
    public static LauncherSettingsSaveStatus Reset(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Write(path, LauncherSettingsCodec.Serialize(LauncherSettings.Defaults, indented: false), replaceUnreadable: true);
    }

    private static LauncherSettingsSaveStatus Write(string path, string text, bool replaceUnreadable)
        => ArcanumConfig.Save(path, [new(Line, text)], replaceUnreadable) switch
        {
            ArcanumConfigSaveStatus.Saved => LauncherSettingsSaveStatus.Saved,
            ArcanumConfigSaveStatus.RefusedUnreadable => LauncherSettingsSaveStatus.RefusedUnreadable,
            _ => LauncherSettingsSaveStatus.Failed,
        };
}

public enum GameFolderStartupStatus { Ready, NotFound }

/// <summary>Where a start's game folder came from: the stored one, or one <see cref="GameFolderLocator.Detect"/> found.</summary>
public enum GameFolderStartupSource { Stored, Detected }

/// <summary>The game folder a start opens: <see cref="Folder"/> (ready) and its source, or <see cref="GameFolderStartupStatus.NotFound"/>.
/// <see cref="StoredCheck"/> is the stored folder's check (null when none was stored); <see cref="Detection"/> the candidates checked
/// (null when the stored folder was ready). <see cref="IdentityChanged"/>: the stored folder's installation differs from the stored
/// identity. <see cref="ShowLauncher"/>: the launcher screen should show before the main menu (first run, stored folder missing or no
/// longer valid, a changed installation, or "show at start"). <see cref="Settings"/> are the settings to write when
/// <see cref="SettingsChanged"/>: the start itself only adds the identity of a stored folder that had none. A detected folder, a
/// changed identity and a stored folder that is gone are the launcher's to confirm (<see cref="LauncherSettings.WithGameFolder"/>),
/// so the start never replaces the player's stored folder or spends the signal before the launcher has shown it.</summary>
public sealed record GameFolderStartup(GameFolderStartupStatus Status, GameFolderCheck? Folder, GameFolderStartupSource? Source,
    GameFolderCheck? StoredCheck, GameFolderDetection? Detection, bool IdentityChanged, bool ShowLauncher, LauncherSettings Settings,
    bool SettingsChanged)
{
    public bool Ready => Status == GameFolderStartupStatus.Ready;

    /// <summary>ARC-DEV-046: the stored folder when it is still a game folder, else the first ready fixed candidate of
    /// <paramref name="platform"/> for this start only. Only reads; the caller writes <see cref="Settings"/>.</summary>
    public static GameFolderStartup Resolve(LauncherSettings settings, GameFolderPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(platform);
        GameFolderCheck? stored = null;
        if (settings.GameRoot is { } root)
        {
            stored = GameFolderLocator.Check(root, GameFolderOrigin.Chosen);
            if (stored.Ready)
            {
                var changed = settings.GameIdentity is { } identity && !identity.SameAs(stored.Identity);
                var first = settings.GameIdentity is null;
                return new(GameFolderStartupStatus.Ready, stored, GameFolderStartupSource.Stored, stored, null, changed,
                    settings.ShowAtStart || changed, first ? settings.WithGameFolder(stored) : settings, first);
            }
        }
        var detection = GameFolderLocator.Detect(platform);
        return detection.Found is { } found
            ? new(GameFolderStartupStatus.Ready, found, GameFolderStartupSource.Detected, stored, detection, false, true, settings, false)
            : new(GameFolderStartupStatus.NotFound, null, null, stored, detection, false, true, settings, false);
    }
}
