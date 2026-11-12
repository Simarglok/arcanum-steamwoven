using Arcanum.Session.Settings;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1130: where the player's settings file (<c>arcanum.cfg</c>, <see cref="ArcanumConfig"/>) is. Every store (game settings, volumes,
/// Always Run, the visual profile, the launcher settings) chooses its path here: an explicit test path (the store's own environment
/// variable, which now names a settings file) wins; an automated run without one has no file at all (nothing is read or written); an ordinary
/// run uses <c>user://arcanum.cfg</c>, the same file for every store. The first ordinary use moves the settings of the files this build kept
/// before (<see cref="LegacySettingsTexts"/>) into it, once and only when the file does not exist yet; the old files stay where they are,
/// unread and unchanged.
/// </summary>
public static class ViewerSettingsFile
{
    public const string UserPath = "user://" + ArcanumConfig.FileName;
    private const long MaxLegacyBytes = 1024 * 1024;
    private static string? _userFile;

    /// <summary>The path a store uses: <paramref name="testVariable"/>'s value when set, else null for an automated run, else <see cref="UserFile"/>.</summary>
    public static string? Choose(string testVariable, bool automated)
    {
        var test = OS.GetEnvironment(testVariable);
        return !string.IsNullOrWhiteSpace(test) ? test : automated ? null : UserFile();
    }

    /// <summary>The player's settings file (a file system path), after the one-time move of the old files.</summary>
    public static string UserFile()
    {
        if (_userFile is { } known) return known;
        var path = ProjectSettings.GlobalizePath(UserPath);
        Migrate(path);
        return _userFile = path;
    }

    private static void Migrate(string path)
    {
        try
        {
            if (File.Exists(path)) return;
            var folder = Path.GetDirectoryName(path)!;
            string? Text(string name)
            {
                var file = Path.Combine(folder, name);
                try { return File.Exists(file) && new FileInfo(file).Length <= MaxLegacyBytes ? File.ReadAllText(file) : null; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
            }
            var legacy = new LegacySettingsTexts(Text(LegacySettingsTexts.GameFile), Text(LegacySettingsTexts.AudioFile), Text(LegacySettingsTexts.AlwaysRunFile),
                Text(LegacySettingsTexts.VisualProfileFile), Text(LegacySettingsTexts.LauncherFile));
            if (!legacy.Any) return;
            var changes = ViewerSettingsMigration.Import(legacy);
            var status = changes.Count == 0 ? "nothing usable" : ArcanumConfig.Save(path, changes).ToString();
            GD.Print($"ARC-UI-1130 settings file from the earlier files status={status} lines={changes.Count} (the earlier files are left as they are)");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            GD.PrintErr($"ARC-UI-1130 settings file from the earlier files FAILED: {error.GetType().Name}");
        }
    }
}
