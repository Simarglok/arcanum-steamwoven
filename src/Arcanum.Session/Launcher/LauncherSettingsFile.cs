using System.Text;
using Arcanum.Data.Content;

namespace Arcanum.Session.Launcher;

public enum LauncherSettingsSaveStatus { Saved, RefusedOtherSchema, RefusedUnreadable, Failed }

/// <summary>ARC-DEV-046: <c>launcher_v1.json</c> on disk. The host picks the path (Godot's <c>user://</c> for a player, a test path or
/// none for an automated run). Reading and writing never throw for the file's state: a missing, unreadable or invalid file reads as
/// the defaults with its status; a write goes to a temporary file in the same folder and replaces the file by a rename, keeps the
/// unknown keys of the file it replaces, and never replaces a file of another schema (a newer build's) or one it cannot read.</summary>
public static class LauncherSettingsFile
{
    public const string FileName = "launcher_v1.json";

    public static LauncherSettingsRead Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return LauncherSettingsCodec.Deserialize(null);
            if (file.Length > LauncherSettingsCodec.MaxBytes) return Invalid();
            return LauncherSettingsCodec.Deserialize(File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true)));
        }
        catch (DecoderFallbackException) { return Invalid(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return new(LauncherSettingsReadStatus.Unreadable, LauncherSettings.Defaults, [], []);
        }
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
        var text = LauncherSettingsCodec.Serialize(settings, current);
        string? temporary = null;
        try
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            temporary = $"{full}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, full, overwrite: true);
            temporary = null;
            return LauncherSettingsSaveStatus.Saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return LauncherSettingsSaveStatus.Failed;
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

public enum GameFolderStartupStatus { Ready, NotFound }

/// <summary>Where a start's game folder came from: the stored one, or one <see cref="GameFolderLocator.Detect"/> found.</summary>
public enum GameFolderStartupSource { Stored, Detected }

/// <summary>The game folder a start opens: <see cref="Folder"/> (ready) and its source, or <see cref="GameFolderStartupStatus.NotFound"/>.
/// <see cref="StoredCheck"/> is the stored folder's check (null when none was stored); <see cref="Detection"/> the candidates checked
/// (null when the stored folder was ready). <see cref="IdentityChanged"/>: the stored folder's installation differs from the stored
/// identity. <see cref="ShowLauncher"/>: the launcher screen should show before the main menu (first run, stored folder missing or no
/// longer valid, a changed installation, or "show at start"). <see cref="Settings"/> carry the folder found and its identity; write
/// them when <see cref="SettingsChanged"/>.</summary>
public sealed record GameFolderStartup(GameFolderStartupStatus Status, GameFolderCheck? Folder, GameFolderStartupSource? Source,
    GameFolderCheck? StoredCheck, GameFolderDetection? Detection, bool IdentityChanged, bool ShowLauncher, LauncherSettings Settings,
    bool SettingsChanged)
{
    public bool Ready => Status == GameFolderStartupStatus.Ready;

    /// <summary>ARC-DEV-046: the stored folder when it is still a game folder, else the first ready fixed candidate of
    /// <paramref name="platform"/>. Only reads; the caller writes <see cref="Settings"/>.</summary>
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
                var updated = settings.WithGameFolder(stored);
                return new(GameFolderStartupStatus.Ready, stored, GameFolderStartupSource.Stored, stored, null, changed,
                    settings.ShowAtStart || changed, updated, !Same(settings, updated));
            }
        }
        var detection = GameFolderLocator.Detect(platform);
        if (detection.Found is { } found)
        {
            var updated = settings.WithGameFolder(found);
            return new(GameFolderStartupStatus.Ready, found, GameFolderStartupSource.Detected, stored, detection, false, true, updated, true);
        }
        return new(GameFolderStartupStatus.NotFound, null, null, stored, detection, false, true, settings, false);
    }

    private static bool Same(LauncherSettings a, LauncherSettings b)
        => a.GameRoot == b.GameRoot && a.GameSource == b.GameSource && (a.GameIdentity?.SameAs(b.GameIdentity) ?? b.GameIdentity is null);
}
