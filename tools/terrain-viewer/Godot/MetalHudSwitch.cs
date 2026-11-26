using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-3389: shows or hides Apple's Metal Performance HUD in this running process with the system's <c>metalperftrace setup</c>
/// tool, so nothing is loaded into the game until the HUD is asked for. A system without the tool (another OS, an older macOS) has no
/// HUD state in the Ctrl+F cycle. Main thread only; the tool answers within milliseconds.
/// </summary>
internal static class MetalHudSwitch
{
    private const int TimeoutMs = 2000;

    public static bool Available => OperatingSystem.IsMacOS() && System.IO.File.Exists(PerfOverlayCycle.HudTool);

    /// <summary>Asks the tool to show or hide the HUD. Null when it accepted, else why not (it accepts any process id, so an accepted
    /// request is not proof the HUD is on screen).</summary>
    public static string? Set(bool shown)
    {
        if (!Available) return "no " + PerfOverlayCycle.HudTool;
        // Read by the HUD when it is first loaded: beside the top bar's clock rather than over it. A variable of the start stands.
        if (shown) SetVariable("MTL_HUD_ALIGNMENT", "centerright", 0);
        try
        {
            var start = new ProcessStartInfo(PerfOverlayCycle.HudTool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in PerfOverlayCycle.HudArguments(shown, System.Environment.ProcessId)) start.ArgumentList.Add(argument);
            using var tool = Process.Start(start);
            if (tool is null) return "the tool did not start";
            if (!tool.WaitForExit(TimeoutMs)) { tool.Kill(); return "the tool did not answer"; }
            return tool.ExitCode == 0 ? null : $"exit {tool.ExitCode}: {tool.StandardError.ReadToEnd().Trim()}";
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or System.IO.IOException) { return failure.Message; }
    }

    // The process's own environment, which the HUD reads: .NET's SetEnvironmentVariable changes only the runtime's copy on Unix.
    [DllImport("libSystem.dylib", EntryPoint = "setenv")]
    private static extern int SetVariable([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    /// <summary>The window server's number of <paramref name="window"/> (an <c>NSWindow*</c>), for the smoke's capture of the HUD.</summary>
    internal static long WindowNumberForSelfTest(long window)
        => OperatingSystem.IsMacOS() && window != 0 ? (long)Send((IntPtr)window, Selector("windowNumber")) : 0;
}
