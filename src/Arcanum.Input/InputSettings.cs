using System.Text.Json;

namespace Arcanum.Input;

/// <summary>Common policy only; each host owns its distinct storage.</summary>
public sealed record InputSettings(int Version = 1, double CameraDeadzone = .20, double ActorDeadzone = .20,
    double CameraSensitivity = 1, double ActorSensitivity = 1, bool InvertCameraX = true, bool InvertCameraY = true)
{
    public static InputSettings Default => new();
    public InputSettings Validated()
    {
        if (Version != 1 || !double.IsFinite(CameraDeadzone) || !double.IsFinite(ActorDeadzone)
            || !double.IsFinite(CameraSensitivity) || !double.IsFinite(ActorSensitivity)) return Default;
        return this with
        {
            CameraDeadzone = Math.Clamp(CameraDeadzone, .05, .45), ActorDeadzone = Math.Clamp(ActorDeadzone, .05, .45),
            CameraSensitivity = Math.Clamp(CameraSensitivity, .5, 2), ActorSensitivity = Math.Clamp(ActorSensitivity, .5, 2)
        };
    }
}

public static class InputSettingsCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Serialize(InputSettings settings) => JsonSerializer.Serialize(settings.Validated(), Options);
    public static InputSettings DeserializeOrDefault(string? json)
    {
        try { return JsonSerializer.Deserialize<InputSettings>(json ?? string.Empty, Options)?.Validated() ?? InputSettings.Default; }
        catch (JsonException) { return InputSettings.Default; }
    }
}
