using GamepadLab.Core;

namespace GamepadLab.Tests;

internal static class SharedInputCompatibilityTests
{
    public static void Run()
    {
        var lab = typeof(LabSettings).Assembly;
        var shared = typeof(Vec2).Assembly;
        Assertions.Equal(shared.GetName().Name!, "Arcanum.Input", "single shared input owner");
        string[] expected = ["Vec2", "InputMethod", "RawInputFrame", "InputContext", "IntentFrame", "InputStateReducer",
            "CameraMode", "CameraFollowState", "CameraBounds", "CameraFollowStep", "CameraFollowEngine"];
        var forwarded = lab.GetForwardedTypes();
        Assertions.Equal(forwarded.Length, 11, "exact historical forwarding surface");
        foreach (var name in expected)
        {
            var type = forwarded.Single(t => t.FullName == "GamepadLab.Core." + name);
            Assertions.True(type.Assembly == shared, "forward resolves to one shared definition");
            Assertions.True(lab.GetType(type.FullName!) == type, "historical assembly-qualified lookup");
        }
        Assertions.Equal(typeof(RawInputFrame).GetMethod("Deconstruct")!.GetParameters().Length, 20, "v1 raw deconstruction");
        Assertions.Equal(typeof(IntentFrame).GetMethod("Deconstruct")!.GetParameters().Length, 14, "v1 intent deconstruction");
        const string defaults = "{\"version\":1,\"cameraDeadzone\":0.2,\"actorDeadzone\":0.2,\"cameraSensitivity\":1,\"actorSensitivity\":1,\"invertCameraX\":true,\"invertCameraY\":true,\"movementMode\":0}";
        Assertions.True(LabPreferences.TryDeserialize(defaults, out var migrated, out _, legacy: true), "literal historical JSON migration");
        Assertions.Equal(migrated, LabSettings.Default, "v1 fixture retains axes and mode as v2 defaults");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assertions.Equal(new LabSettings(CameraSensitivity: value, InvertCameraX: false).Validated(), LabSettings.Default, "lab all-or-default preserved");
        Assertions.True(!shared.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("GamepadLab", StringComparison.Ordinal)), "shared never references experiment");
        Console.WriteLine("Shared input compatibility: forwarding/API/JSON/dependency PASS");
    }
}
