using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-3389: Apple's Metal Performance HUD of this running process. The first time it is shown, the system's
/// <c>metalperftrace setup --enable hud</c> loads it into the process (nothing is loaded before); from then on the window's
/// <c>CAMetalLayer.developerHUDProperties</c> holds whether it is shown and where, also for a request made before the load has finished.
/// A system without the tool (another OS, an older macOS) or a window without such a layer has no HUD state in the Ctrl+F cycle.
/// Main thread only.
/// </summary>
internal sealed class MetalHudSwitch
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const int ToolTimeoutMs = 2000, NoLoad = 0x10;
    private readonly IntPtr _layer;
    private bool _loadAsked;

    private MetalHudSwitch(IntPtr layer) => _layer = layer;

    /// <summary>The switch for the window whose view is <paramref name="view"/> (an <c>NSView*</c>), null where there is no HUD to
    /// switch.</summary>
    public static MetalHudSwitch? Find(long view)
    {
        if (!OperatingSystem.IsMacOS() || view == 0 || !System.IO.File.Exists(PerfOverlayCycle.HudTool)) return null;
        var layer = Send((IntPtr)view, Selector("layer"));
        return layer != IntPtr.Zero && SendQuery(layer, Selector("respondsToSelector:"), Selector("setDeveloperHUDProperties:")) != 0
            ? new(layer) : null;
    }

    /// <summary>Has the HUD shown with its top left corner at (<paramref name="x"/>, <paramref name="y"/>) in the window's points, once
    /// it is loaded. Asks the system nothing.</summary>
    public void Place(int x, int y) => Apply(PerfOverlayCycle.HudProperties(true, x, y));

    /// <summary><see cref="Place"/>, then loads the HUD when the process has not. Null when that went through, else why the tool did not
    /// load it (the tool accepts any process id, so an accepted request is not proof the HUD is on screen). May wait for the tool: for a
    /// key press, never per frame.</summary>
    public string? Show(int x, int y)
    {
        Place(x, y);
        // Never ask twice: a second request while the first is loading, or for a HUD already there, has left it invisible.
        if (_loadAsked || LoadLibrary("/usr/lib/libMTLHud.dylib", NoLoad) != IntPtr.Zero) return null;
        try
        {
            var start = new ProcessStartInfo(PerfOverlayCycle.HudTool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in PerfOverlayCycle.HudLoadArguments(System.Environment.ProcessId)) start.ArgumentList.Add(argument);
            using var tool = Process.Start(start);
            if (tool is null) return "the tool did not start";
            // Both pipes are drained while waiting: a full one would hold the tool up.
            _ = tool.StandardOutput.ReadToEndAsync();
            var error = tool.StandardError.ReadToEndAsync();
            // A tool that did not answer may still have been heard: it counts as asked.
            if (!tool.WaitForExit(ToolTimeoutMs)) { _loadAsked = true; tool.Kill(); return "the tool did not answer"; }
            _loadAsked = tool.ExitCode == 0;
            return _loadAsked ? null : $"exit {tool.ExitCode}: {(error.Wait(ToolTimeoutMs) ? error.Result.Trim() : "")}";
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or System.IO.IOException) { return failure.Message; }
    }

    public void Hide() => Apply(PerfOverlayCycle.HudProperties(false, 0, 0));

    private void Apply(IReadOnlyList<(string Key, string Value)> properties)
    {
        static IntPtr Text(string value) => SendText(Class("NSString"), Selector("stringWithUTF8String:"), value);
        var dictionary = Send(Class("NSMutableDictionary"), Selector("dictionary"));
        foreach (var (key, value) in properties) Send(dictionary, Selector("setObject:forKey:"), Text(value), Text(key));
        Send(_layer, Selector("setDeveloperHUDProperties:"), dictionary);
    }

    /// <summary>Pixels per point of the window: the layer's contents scale (2 on a Retina screen).</summary>
    public double Scale => SendNumber(_layer, Selector("contentsScale")) is > 0 and var scale ? scale : 1;

    /// <summary>The window server's number of <paramref name="window"/> (an <c>NSWindow*</c>), for the smoke's capture of the HUD.</summary>
    internal static long WindowNumberForSelfTest(long window)
        => OperatingSystem.IsMacOS() && window != 0 ? (long)Send((IntPtr)window, Selector("windowNumber")) : 0;

    [DllImport("libSystem.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    [DllImport(Runtime, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Runtime, EntryPoint = "objc_getClass")]
    private static extern IntPtr Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // objc_msgSend is declared once per signature used: on arm64 a call must match the method's own, never a variadic one.
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern byte SendQuery(IntPtr receiver, IntPtr selector, IntPtr first);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern double SendNumber(IntPtr receiver, IntPtr selector);
}
