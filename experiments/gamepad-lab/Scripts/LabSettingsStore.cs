using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Only the synthetic lab's user:// storage is touched; no global or game settings are read.</summary>
public sealed class LabSettingsStore
{
    private const string DefaultPath = "user://gamepad_lab_settings_v2.json";
    private const string TestPathVariable = "GAMEPAD_LAB_SETTINGS_TEST_PATH";
    private static string Path => ProjectSettings.GlobalizePath(string.IsNullOrWhiteSpace(OS.GetEnvironment(TestPathVariable)) ? DefaultPath : OS.GetEnvironment(TestPathVariable));
    private string LegacyPath => UsesIsolatedTestStorage ? Path + ".legacy-v1" : ProjectSettings.GlobalizePath("user://gamepad_lab_settings_v1.json");
    public string Diagnostic { get; private set; } = "settings-default";
    public string SelectedPath => Path;

    /// <summary>
    /// Export validation can opt into a per-run lab file. Normal launches always use
    /// the versioned user:// preference and never inherit validation test state.
    /// </summary>
    public bool UsesIsolatedTestStorage => !string.IsNullOrWhiteSpace(OS.GetEnvironment(TestPathVariable));
    public LabSettings Load()
    {
        try
        {
            var exists = System.IO.File.Exists(Path);
            var selected = exists ? Path : LegacyPath;
            if (!System.IO.File.Exists(selected)) return LabSettings.Default;
            if (new System.IO.FileInfo(selected).Length > LabPreferences.MaximumBytes) { Diagnostic = "settings-byte-limit"; return LabSettings.Default; }
            if (!LabPreferences.TryDeserialize(System.IO.File.ReadAllText(selected), out var settings, out var diagnostic, legacy: !exists))
            { Diagnostic = diagnostic; return LabSettings.Default; }
            Diagnostic = diagnostic;
            if (!exists) Save(settings); // Never modify the v1 source or fall back to it if corrupt v2 exists.
            return settings;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { Diagnostic = "settings-read-failed"; return LabSettings.Default; }
    }
    public void Save(LabSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentException("invalid settings", nameof(settings));
        var bytes = System.Text.Encoding.UTF8.GetBytes(LabSettingsCodec.Serialize(settings));
        var temp = Path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new System.IO.FileStream(temp, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            System.IO.File.Move(temp, Path, true);
        }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
    }
}
