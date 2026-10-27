using System.Runtime.InteropServices;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-045: keeps an automated run drawing at speed while its window cannot draw. A minimized macOS
/// window reports <c>window_can_draw()</c> false; the engine then keeps its process frames but skips every draw, so
/// <c>frame_post_draw</c> never fires and each FramePostDraw await of a self-test or smoke waits for its timeout. This
/// node then draws each frame itself where the engine would (main-viewport and SubViewport readback match what was
/// drawn). Two engine and system throttles of such a window are lifted too: the engine sleeps after every frame while no
/// window can draw, and App Nap cut such an app from about 100 to 18 frames per second after about 30 s. It is added
/// only by the process hosting an automated run (a self-test, a native smoke, the WORLD settings validation) and never
/// in a headless run or an interactive viewer.</summary>
internal sealed partial class HiddenWindowDrawKeeper : Node
{
    /// <summary>Frames this process drew while its window could not draw (the self-test stage lines print it).</summary>
    internal static long ForcedDraws { get; private set; }

    private IntPtr _activity;
    private System.Threading.SynchronizationContext? _context;

    public HiddenWindowDrawKeeper()
    {
        Name = "HiddenWindowDrawKeeper";
        // After every other process callback, and also while the tree is paused.
        ProcessPriority = int.MaxValue;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _EnterTree()
    {
        _context = System.Threading.SynchronizationContext.Current;
        // The engine also sleeps this long after every frame while no window can draw (the low-processor sleep, used
        // here only for that: the viewer never turns low-processor mode on).
        OS.LowProcessorUsageModeSleepUsec = 0;
        var appNap = "not-macos";
        if (OperatingSystem.IsMacOS())
        {
            // A user-initiated activity: no App Nap while it is held, and idle system sleep stays allowed. Without the
            // Objective-C runtime the run only keeps App Nap.
            try { _activity = AppNap.Begin("Arcanum Steamwoven automated run"); appNap = _activity != IntPtr.Zero ? "held" : "unavailable"; }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException) { appNap = "unavailable"; }
        }
        GD.Print($"ARC-DEV-045 hidden-window draw keeper on app-nap={appNap}");
    }

    public override void _ExitTree()
    {
        if (_activity == IntPtr.Zero) return;
        AppNap.End(_activity);
        _activity = IntPtr.Zero;
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.WindowCanDraw()) return;
        // The engine draws after the frame's process callbacks, deferred calls, timers and tweens, right before the
        // frame's posted C# continuations run; a draw from here would miss the deferred redraws of this frame. The
        // continuations posted earlier in the frame run before this draw, so their changes show one frame early.
        if (_context is null) Draw(delta);
        else _context.Post(_ => Draw(delta), null);
    }

    private static void Draw(double delta)
    {
        // The engine drew this frame itself if the window could draw again by the time it reached its draw.
        if (DisplayServer.WindowCanDraw()) return;
        // No buffer swap: nothing is shown, but the viewports render and frame_post_draw fires.
        RenderingServer.ForceDraw(swapBuffers: false, frameStep: delta);
        ForcedDraws++;
    }

    /// <summary><c>-[NSProcessInfo beginActivityWithOptions:reason:]</c> through the Objective-C runtime.</summary>
    private static class AppNap
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        // NSActivityUserInitiatedAllowingIdleSystemSleep: no App Nap, while the Mac may still sleep when idle.
        private const ulong UserInitiatedAllowingIdleSystemSleep = 0x00FFFFFFUL & ~(1UL << 20);

        internal static IntPtr Begin(string reason)
        {
            var info = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            var text = SendText(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), reason);
            if (info == IntPtr.Zero || text == IntPtr.Zero) return IntPtr.Zero;
            var token = SendOptions(info, sel_registerName("beginActivityWithOptions:reason:"), UserInitiatedAllowingIdleSystemSleep, text);
            // The token is autoreleased; the activity lasts while it is retained.
            return token == IntPtr.Zero ? IntPtr.Zero : Send(token, sel_registerName("retain"));
        }

        internal static void End(IntPtr token)
        {
            var info = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            if (info != IntPtr.Zero) SendObject(info, sel_registerName("endActivity:"), token);
            Send(token, sel_registerName("release"));
        }

        [DllImport(ObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector, IntPtr argument);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendOptions(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);
    }
}
