using Arcanum.Input;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Host-local preferences; never reads the lab or original game's settings.</summary>
public sealed class DiagnosticInputSettingsStore
{
    private const string DefaultPath = "user://world_diagnostic_input_v1.json";
    private const string TestVariable = "WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH";
    private static string StoragePath => string.IsNullOrWhiteSpace(OS.GetEnvironment(TestVariable)) ? DefaultPath : OS.GetEnvironment(TestVariable);
    public bool UsesIsolatedTestStorage => !string.IsNullOrWhiteSpace(OS.GetEnvironment(TestVariable));
    public InputSettings Load()
    {
        if (!global::Godot.FileAccess.FileExists(StoragePath)) return InputSettings.Default;
        using var file = global::Godot.FileAccess.Open(StoragePath, global::Godot.FileAccess.ModeFlags.Read);
        return file is null ? InputSettings.Default : InputSettingsCodec.DeserializeOrDefault(file.GetAsText());
    }
    public void Save(InputSettings settings)
    {
        using var file = global::Godot.FileAccess.Open(StoragePath, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) throw new InvalidOperationException("Diagnostic preferences could not be saved.");
        file.StoreString(InputSettingsCodec.Serialize(settings));
    }
}
