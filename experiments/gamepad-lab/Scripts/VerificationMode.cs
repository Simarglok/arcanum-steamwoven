using Godot;

namespace GamepadLab;
/// <summary>Command-line verification is deliberately synthetic; it never claims physical-controller evidence.</summary>
public static class VerificationMode
{
    public const string Simulation = "SIMULATION";
    public static bool Has(IReadOnlyList<string> arguments, string flag) => arguments.Contains(flag);
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static string SmokeJson() => "{\"status\":\"passed\",\"provenance\":\"SIMULATION\",\"physicalDeviceEvidence\":{\"xboxBluetoothAxesButtons\":\"Not verified\",\"disconnectReconnect\":\"Not verified\",\"drift\":\"Not verified\",\"comfort\":\"Not verified\"}}";
}
