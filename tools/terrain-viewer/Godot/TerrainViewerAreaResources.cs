using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private sealed record AreaResourceBaseline(long Generation, Arcanum.Data.Terrain.TerrainRect Bounds, int Commands,
        int CpuFrames, long CpuBytes, AreaBudgetUsage InitialUsage, AreaBudgetUsage FinalUsage, long ElapsedMilliseconds,
        WeakReference Result, WeakReference Frames);

    // Scalars and weak references only: the async stage below must not keep
    // the generation it is about to retire reachable through hoisted locals.
    private AreaResourceBaseline CaptureAreaResourceBaseline()
    {
        var area = _readyArea ?? throw new InvalidOperationException("AREA_RESOURCES_READY_REQUIRED");
        return new(area.Generation, area.Plan.WorldBounds, area.Plan.Commands.Count, area.Frames.Count, area.CpuRgbaBytes,
            area.InitialUsage, area.FinalUsage, area.ElapsedMilliseconds, new WeakReference(area), new WeakReference(area.Frames));
    }

    /// <summary>S5 native-smoke resource observation, after the coverage
    /// report: a whole-envelope camera sweep (GPU eviction/reupload from the
    /// retained CPU repository only), then an overlapped production reload whose
    /// cancelled first generation must not publish. The retired generation must
    /// become unreachable; resident size and load time are observations only.
    /// The caller must not hold the published load result.</summary>
    private async Task RunAreaResourcesAsync()
    {
        static void Require(bool value, string code) { if (!value) throw new InvalidOperationException("AREA_RESOURCES_" + code); }
        var old = CaptureAreaResourceBaseline();
        var bounds = old.Bounds; var viewport = WorldViewport;
        var created = _cache.CreatedCount; var disposed = _cache.DisposedCount;
        HashSet<AreaDrawCommand>? seen = new(ReferenceEqualityComparer.Instance);
        var steps = 0; var maxLive = 0; var maxVisible = 0; long maxBytes = 0;
        // Zoom 1 tiles the envelope with viewport-sized steps; the camera clamp
        // keeps edge views inside the envelope, so every command intersects one.
        for (var y = bounds.Y + viewport.Y / 2d; y < bounds.Y + bounds.Height + viewport.Y / 2d; y += viewport.Y)
        for (var x = bounds.X + viewport.X / 2d; x < bounds.X + bounds.Width + viewport.X / 2d; x += viewport.X)
        {
            _camera!.ResetWorld(new Vec2(x, y), viewport, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            steps++; seen.UnionWith(_canvas!.AreaVisibleCommands);
            maxLive = Math.Max(maxLive, _cache.AreaCount); maxVisible = Math.Max(maxVisible, _canvas.AreaVisibleCount);
            maxBytes = Math.Max(maxBytes, _cache.AreaRgbaBytes);
            // GPU residency is a subset of the retained repository, never more.
            Require(_cache.AreaCount <= old.CpuFrames && _cache.AreaRgbaBytes <= old.CpuBytes, "GPU_BUDGET");
        }
        var sweptCommands = seen.Count; seen = null;
        Require(sweptCommands == old.Commands, "SWEEP_COVERAGE");
        Require(ReferenceEquals(old.Result.Target, _readyArea) && _readyArea!.Frames.Count == old.CpuFrames
            && _readyArea.CpuRgbaBytes == old.CpuBytes, "CPU_REPOSITORY");
        var reuploads = _cache.CreatedCount - created; var evictions = _cache.DisposedCount - disposed;
        Require(reuploads > 0 && evictions > 0 && _cache.CreatedCount - _cache.DisposedCount == _cache.Count, "GPU_OWNERSHIP");
        GD.Print($"ARC-RENDER-007 resources sweep steps={steps} commands={sweptCommands} maxVisible={maxVisible} maxGpu={maxLive}/{maxBytes} reuploads={reuploads} evictions={evictions} cpu={old.CpuFrames}/{old.CpuBytes}");

        static long Managed() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
        static long Resident() { using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh(); return process.WorkingSet64; }
        static int Handles() => System.IO.Directory.GetFileSystemEntries("/dev/fd").Length;
        var arguments = _arguments ?? throw new InvalidOperationException("AREA_RESOURCES_ARGUMENTS_REQUIRED");
        var baseline = (Managed: Managed(), Resident: Resident(), Handles: Handles());
        // Overlapped production reloads: the second cancels the first, which
        // must never publish over its successor.
        var first = StartLoadAsync(arguments, false); var firstGeneration = _generation;
        var second = StartLoadAsync(arguments, false); var secondGeneration = _generation;
        await first; await second;
        Require(_readyArea is not null && !ReferenceEquals(_readyArea, old.Result.Target) && secondGeneration > firstGeneration
            && firstGeneration > old.Generation && _readyArea.Generation == secondGeneration
            && _worldActor.Session?.EvidenceEpoch == secondGeneration, "STALE_PUBLICATION");
        Require(_readyArea!.InitialUsage == old.InitialUsage && _readyArea.FinalUsage == old.FinalUsage
            && _readyArea.Frames.Count == old.CpuFrames && _readyArea.CpuRgbaBytes == old.CpuBytes, "FRESH_OWNER_COUNTERS");
        var reloadMilliseconds = _readyArea.ElapsedMilliseconds;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var after = (Managed: Managed(), Resident: Resident(), Handles: Handles());
        var retiredAlive = old.Result.IsAlive || old.Frames.IsAlive;
        GD.Print($"ARC-RENDER-007 resources reload observed generations={old.Generation}>{firstGeneration}>{secondGeneration} loadMs={old.ElapsedMilliseconds}/{reloadMilliseconds} managed={baseline.Managed}/{after.Managed} resident={baseline.Resident}/{after.Resident} handles={baseline.Handles}/{after.Handles} retiredAlive={retiredAlive}");
        Require(_cache.CreatedCount - _cache.DisposedCount == _cache.Count && _cache.AreaCount <= old.CpuFrames, "RELOAD_GPU_OWNERSHIP");
        // One full original load takes minutes, so steady state is one replaced
        // generation. The retired result and its frame repository must be
        // collectable, descriptors must not grow, and the post-GC managed heap
        // may not grow by half a repository. Resident size is recorded only:
        // freed runtime/driver pages need not return to the system.
        Require(!retiredAlive, "RETIRED_GENERATION_ALIVE");
        Require(after.Handles <= baseline.Handles, "HANDLE_GROWTH");
        Require(after.Managed - baseline.Managed < old.CpuBytes / 2, "MANAGED_RETENTION");
        var resources = new { Outcome = "PASS", SweepSteps = steps, SweepCommands = sweptCommands, MaxVisible = maxVisible, MaxGpuFrames = maxLive,
            MaxGpuBytes = maxBytes, Reuploads = reuploads, Evictions = evictions, CpuFrames = old.CpuFrames, CpuBytes = old.CpuBytes,
            LoadMilliseconds = new[] { old.ElapsedMilliseconds, reloadMilliseconds },
            ManagedBytes = new[] { baseline.Managed, after.Managed },
            ResidentBytes = new[] { baseline.Resident, after.Resident },
            Handles = new[] { baseline.Handles, after.Handles },
            RetiredGenerationCollected = true, CancelledGenerationPublished = false };
        var reportPath = System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            System.IO.File.WriteAllText(reportPath + ".resources.json", System.Text.Json.JsonSerializer.Serialize(resources));
        GD.Print("ARC-RENDER-007 resources reload PASS");
    }
}
