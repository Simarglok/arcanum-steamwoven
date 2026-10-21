using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Host-local preferences; never reads the lab or original game's settings. An automated run without the test path uses
/// its own isolated file (ARC-DEV-032, <see cref="ViewerAutomatedStorage"/>), never the player's.</summary>
public sealed class DiagnosticInputSettingsStore
{
    public const string DefaultPath = "user://world_diagnostic_input_v1.json";
    private const string TestVariable = "WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH";
    internal static string StoragePath => ViewerAutomatedStorage.Path(OS.GetEnvironment(TestVariable), DefaultPath);
    public bool UsesIsolatedTestStorage => !string.IsNullOrWhiteSpace(OS.GetEnvironment(TestVariable));
    public InputSettings Load()
    {
        if (!global::Godot.FileAccess.FileExists(StoragePath)) return DiagnosticWorldPolicy.DefaultInputSettings;
        using var file = global::Godot.FileAccess.Open(StoragePath, global::Godot.FileAccess.ModeFlags.Read);
        return file is null ? DiagnosticWorldPolicy.DefaultInputSettings : InputSettingsCodec.DeserializeOrDefault(file.GetAsText(), DiagnosticWorldPolicy.DefaultInputSettings);
    }
    public void Save(InputSettings settings)
    {
        using var file = global::Godot.FileAccess.Open(StoragePath, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) throw new InvalidOperationException("Diagnostic preferences could not be saved.");
        file.StoreString(InputSettingsCodec.Serialize(settings.Validated(DiagnosticWorldPolicy.DefaultInputSettings)));
    }
}
