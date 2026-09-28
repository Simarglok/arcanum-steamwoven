using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Only the synthetic lab's user:// storage is touched; no global or game settings are read.</summary>
public sealed class LabSettingsStore
{
    private const string Path = "user://gamepad_lab_settings_v1.json";
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
