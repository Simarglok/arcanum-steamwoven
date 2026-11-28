using Arcanum.TerrainViewer.Core;
using Godot;
using System.Diagnostics;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-3304: the last managed step before Godot tears the engine down. A Godot wrapper that is garbage but not
/// yet finalized (an <c>Image</c>, an input event) is skipped by the bindings' own shutdown disposal, so the .NET finalizer
/// thread releases it while, or after, the engine destroys what that release needs: the process then dies on a signal
/// (SIGSEGV or SIGABRT) and a smoke's exit code is lost. A worker thread still inside an engine call does the same. So the
/// app waits for its background work and then runs every pending finalizer while the engine is whole.</summary>
internal static class ViewerEngineTeardown
{
    /// <summary>The background tasks that call into the engine.</summary>
    public static ViewerBackgroundWork Work { get; } = new();

    /// <summary>Main thread, once, at the end of the main app's <c>_ExitTree</c>.</summary>
    public static void Run()
    {
        var clock = Stopwatch.StartNew();
        var (waited, left) = Work.Close(TimeSpan.FromSeconds(5));
        // The second round takes what the first round's finalizers let go of.
        for (var round = 0; round < 2; round++)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }
        GD.Print($"ARC-DEV-3304 engine teardown background={waited} left={left} ms={clock.ElapsedMilliseconds}");
    }
}
