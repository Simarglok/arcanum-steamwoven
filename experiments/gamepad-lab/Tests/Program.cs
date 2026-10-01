namespace GamepadLab.Tests;
internal static class Program
{
    private static int Main(string[] args)
    {
        try { InputStateTests.Run(); TargetingTests.Run(); SharedInputCompatibilityTests.Run(); Console.WriteLine("GamepadLab core harness: PASS"); return 0; }
        catch (Exception exception) { Console.Error.WriteLine($"GamepadLab core harness: FAIL: {exception.Message}"); return 1; }
    }
}
