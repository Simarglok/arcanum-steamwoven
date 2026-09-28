namespace GamepadLab.Tests;

internal static class Assertions
{
    public static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Equal<T>(T actual, T expected, string message) where T : notnull { if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}"); }
    public static void Near(double actual, double expected, double tolerance, string message) => True(Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, actual {actual}");
}
