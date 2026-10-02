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
        if (Version != 1 || !Enum.IsDefined(MovementMode) || !double.IsFinite(CameraDeadzone) || !double.IsFinite(ActorDeadzone)
            || !double.IsFinite(CameraSensitivity) || !double.IsFinite(ActorSensitivity)) return Default;
        var common = new Arcanum.Input.InputSettings(Version, CameraDeadzone, ActorDeadzone,
            CameraSensitivity, ActorSensitivity, InvertCameraX, InvertCameraY).Validated();
        return this with
        {
            CameraDeadzone = common.CameraDeadzone,
            ActorDeadzone = common.ActorDeadzone,
            CameraSensitivity = common.CameraSensitivity,
            ActorSensitivity = common.ActorSensitivity
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
