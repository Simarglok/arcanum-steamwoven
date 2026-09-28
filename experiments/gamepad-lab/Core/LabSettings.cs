using System.Text.Json;

namespace GamepadLab.Core;

/// <summary>Versioned, user-local lab preferences. Missing or malformed data always yields safe v1 defaults.</summary>
public sealed record LabSettings(
    int Version = 1,
    double CameraDeadzone = .20,
    double ActorDeadzone = .20,
    double CameraSensitivity = 1,
    double ActorSensitivity = 1,
    bool InvertCameraX = true,
    bool InvertCameraY = true,
    MovementMode MovementMode = MovementMode.DirectIntent)
{
    public static LabSettings Default => new();

    public LabSettings Validated()
    {
        if (Version != 1 || !double.IsFinite(CameraDeadzone) || !double.IsFinite(ActorDeadzone)
            || !double.IsFinite(CameraSensitivity) || !double.IsFinite(ActorSensitivity)) return Default;
        return this with
        {
            CameraDeadzone = Math.Clamp(CameraDeadzone, .05, .45),
            ActorDeadzone = Math.Clamp(ActorDeadzone, .05, .45),
            CameraSensitivity = Math.Clamp(CameraSensitivity, .5, 2),
            ActorSensitivity = Math.Clamp(ActorSensitivity, .5, 2)
        };
    }
}

public static class LabSettingsCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Serialize(LabSettings settings) => JsonSerializer.Serialize(settings.Validated(), Options);
    public static LabSettings DeserializeOrDefault(string? json)
    {
        try { return JsonSerializer.Deserialize<LabSettings>(json ?? string.Empty, Options)?.Validated() ?? LabSettings.Default; }
        catch (JsonException) { return LabSettings.Default; }
    }
}
