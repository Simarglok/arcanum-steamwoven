using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Separate WORLD camera preference, never shared input/lab settings. An automated run without the test path uses its own
/// isolated file (ARC-DEV-032, <see cref="ViewerAutomatedStorage"/>), never the player's.</summary>
public sealed class DiagnosticZoomSettingsStore
{
    public const string UserPath = "user://world_diagnostic_zoom_v1.json";
    internal static string StoragePath => ViewerAutomatedStorage.Path(OS.GetEnvironment("WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"), UserPath);
    public bool UsesIsolatedTestStorage => !string.IsNullOrWhiteSpace(OS.GetEnvironment("WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"));
    public double Load()
    {
        if (!global::Godot.FileAccess.FileExists(StoragePath)) return 2;
        using var file = global::Godot.FileAccess.Open(StoragePath, global::Godot.FileAccess.ModeFlags.Read);
        return file is null ? 2 : WorldZoomCodec.DeserializeOrDefault(file.GetAsText());
    }
    public void Save(double zoom)
    {
        using var file = global::Godot.FileAccess.Open(StoragePath, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) throw new InvalidOperationException("World zoom preferences could not be saved.");
        file.StoreString(WorldZoomCodec.Serialize(zoom));
        file.Flush();
        if (file.GetError() != Error.Ok) throw new InvalidOperationException("World zoom preferences could not be saved.");
    }
}
