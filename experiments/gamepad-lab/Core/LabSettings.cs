using System.Text.Json;

namespace GamepadLab.Core;

/// <summary>Lab-only v2 preferences; common axis settings remain v1 and WORLD is unchanged.</summary>
public sealed record LabSettings(
    int Version = 2,
    double CameraDeadzone = .20,
    double ActorDeadzone = .20,
    double CameraSensitivity = 1,
    double ActorSensitivity = 1,
    bool InvertCameraX = true,
    bool InvertCameraY = true,
    MovementMode MovementMode = MovementMode.DirectIntent)
{
    public static LabSettings Default => new();
    public string Schema => LabPreferences.Schema;
    public string ActivePresetId { get; init; } = "contextual";
    public Arcanum.Input.BindingProfile? UserCopy { get; init; }
    public LabSlots Slots { get; init; } = LabSlots.Default;
    public int UiScalePercent { get; init; } = 100;
    public Arcanum.Input.BindingProfile ActiveProfile => ActivePresetId == "user-copy" ? UserCopy! : Arcanum.Input.BuiltInGamepadPresets.Get(ActivePresetId);
    public bool IsValid => Version == 2 && Enum.IsDefined(MovementMode) && UiScalePercent is 100 or 150 or 200
        && (Arcanum.Input.GamepadTokens.IsBasePreset(ActivePresetId) || ActivePresetId == "user-copy" && UserCopy is not null)
        && (UserCopy is null || UserCopy.Id == "user-copy" && Arcanum.Input.GamepadBindingValidator.Validate(UserCopy).Count == 0)
        && Slots.IsValid && double.IsFinite(CameraDeadzone) && CameraDeadzone is >= .05 and <= .45
        && double.IsFinite(ActorDeadzone) && ActorDeadzone is >= .05 and <= .45
        && double.IsFinite(CameraSensitivity) && CameraSensitivity is >= .5 and <= 2
        && double.IsFinite(ActorSensitivity) && ActorSensitivity is >= .5 and <= 2;

    public LabSettings Validated()
    {
        if (Version != 2 || !Enum.IsDefined(MovementMode) || !double.IsFinite(CameraDeadzone) || !double.IsFinite(ActorDeadzone)
            || !double.IsFinite(CameraSensitivity) || !double.IsFinite(ActorSensitivity)) return Default;
        var common = new Arcanum.Input.InputSettings(1, CameraDeadzone, ActorDeadzone,
            CameraSensitivity, ActorSensitivity, InvertCameraX, InvertCameraY).Validated();
        var candidate = this with
        {
            CameraDeadzone = common.CameraDeadzone,
            ActorDeadzone = common.ActorDeadzone,
            CameraSensitivity = common.CameraSensitivity,
            ActorSensitivity = common.ActorSensitivity
        };
        return candidate.IsValid ? candidate : Default;
    }
}

public static class LabSettingsCodec
{
    public static string Serialize(LabSettings settings) => LabPreferences.Serialize(settings);
    public static LabSettings DeserializeOrDefault(string? json) => LabPreferences.TryDeserialize(json, out var value, out _) ? value : LabSettings.Default;
}
