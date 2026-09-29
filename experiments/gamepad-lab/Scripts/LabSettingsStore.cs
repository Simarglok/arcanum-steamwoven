using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Only the synthetic lab's user:// storage is touched; no global or game settings are read.</summary>
public sealed class LabSettingsStore
{
    private const string DefaultPath = "user://gamepad_lab_settings_v1.json";
    private const string TestPathVariable = "GAMEPAD_LAB_SETTINGS_TEST_PATH";
    private static string Path => string.IsNullOrWhiteSpace(OS.GetEnvironment(TestPathVariable)) ? DefaultPath : OS.GetEnvironment(TestPathVariable);

    /// <summary>
    /// Export validation can opt into a per-run lab file. Normal launches always use
    /// the versioned user:// preference and never inherit validation test state.
    /// </summary>
    public bool UsesIsolatedTestStorage => !string.IsNullOrWhiteSpace(OS.GetEnvironment(TestPathVariable));
    public LabSettings Load()
    {
        if (!Godot.FileAccess.FileExists(Path)) return LabSettings.Default;
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
        return file is null ? LabSettings.Default : LabSettingsCodec.DeserializeOrDefault(file.GetAsText());
    }
    public void Save(LabSettings settings)
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        if (file is null) throw new InvalidOperationException("user-local lab settings could not be opened for writing");
        file.StoreString(LabSettingsCodec.Serialize(settings));
    }
}
