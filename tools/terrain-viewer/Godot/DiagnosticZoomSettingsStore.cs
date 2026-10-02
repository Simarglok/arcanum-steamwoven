using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Separate WORLD camera preference, never shared input/lab settings.</summary>
public sealed class DiagnosticZoomSettingsStore
{
    private static string StoragePath => string.IsNullOrWhiteSpace(OS.GetEnvironment("WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"))
        ? "user://world_diagnostic_zoom_v1.json" : OS.GetEnvironment("WORLD_DIAGNOSTIC_ZOOM_TEST_PATH");
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
