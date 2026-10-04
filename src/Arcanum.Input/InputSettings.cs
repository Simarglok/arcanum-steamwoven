using System.Text.Json;

namespace Arcanum.Input;

/// <summary>Common policy only; each host owns its distinct storage.</summary>
public sealed record InputSettings(int Version = 1, double CameraDeadzone = .20, double ActorDeadzone = .20,
    double CameraSensitivity = 1, double ActorSensitivity = 1, bool InvertCameraX = true, bool InvertCameraY = true,
    bool InvertActorX = false, bool InvertActorY = false)
{
    public static InputSettings Default => new();
    public InputSettings Validated() => Validated(Default);
    public InputSettings Validated(InputSettings fallback)
    {
        if (Version != 1 || !double.IsFinite(CameraDeadzone) || !double.IsFinite(ActorDeadzone)
            || !double.IsFinite(CameraSensitivity) || !double.IsFinite(ActorSensitivity)) return fallback;
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
    public static InputSettings DeserializeOrDefault(string? json) => DeserializeOrDefault(json, InputSettings.Default);
    public static InputSettings DeserializeOrDefault(string? json, InputSettings fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var settings = document.RootElement.Deserialize<InputSettings>(Options);
            if (settings is null) return fallback;
            // Hosts may choose different first-launch axes. Explicit saved
            // booleans always win; absent axes use the host's policy, not the
            // historical shared constructor defaults.
            settings = settings with
            {
                InvertCameraX = document.RootElement.TryGetProperty("invertCameraX", out _) ? settings.InvertCameraX : fallback.InvertCameraX,
                InvertCameraY = document.RootElement.TryGetProperty("invertCameraY", out _) ? settings.InvertCameraY : fallback.InvertCameraY,
                InvertActorX = document.RootElement.TryGetProperty("invertActorX", out _) ? settings.InvertActorX : fallback.InvertActorX,
                InvertActorY = document.RootElement.TryGetProperty("invertActorY", out _) ? settings.InvertActorY : fallback.InvertActorY
            };
            return settings.Validated(fallback);
        }
        catch (JsonException) { return fallback; }
    }
}
