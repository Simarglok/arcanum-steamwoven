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
        // ARC-UI-004: items the earlier smoke phases picked up are no longer in the world and are never drawn.
        var taken = _readyArea!.Plan.Commands.Count(command => _canvas!.AreaHiddenOf(command));
        var bounds = old.Bounds; var viewport = WorldViewport;
        // Zoom 1 tiles the envelope with viewport-sized steps; the camera clamp
        // keeps edge views inside the envelope, so every command intersects one. The
        // sweep runs with terrain chunks (production default) and once more with
        // per-command terrain, so both GPU caches must exceed their limits and evict.
        var maxRetained = 0;
        async Task<(int Steps, int Swept, int MaxVisible, int MaxLive, long MaxBytes, int MaxChunks, long MaxChunkBytes)> Sweep(bool chunked)
        {
            _canvas!.AreaTerrainChunksEnabled = chunked;
            HashSet<AreaDrawCommand> seen = new(ReferenceEqualityComparer.Instance);
            var steps = 0; var maxLive = 0; var maxVisible = 0; long maxBytes = 0; var maxChunks = 0; long maxChunkBytes = 0;
            for (var y = bounds.Y + viewport.Y / 2d; y < bounds.Y + bounds.Height + viewport.Y / 2d; y += viewport.Y)
            for (var x = bounds.X + viewport.X / 2d; x < bounds.X + bounds.Width + viewport.X / 2d; x += viewport.X)
            {
                _camera!.ResetWorld(new Vec2(x, y), viewport, 1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                steps++; seen.UnionWith(_canvas.AreaVisibleCommands);
                Require(!_canvas.AreaVisibleCommands.Any(_canvas.AreaHiddenOf), "TAKEN_ITEM_DRAWN");
                maxLive = Math.Max(maxLive, _cache.AreaCount); maxVisible = Math.Max(maxVisible, _canvas.AreaVisibleCount);
                maxBytes = Math.Max(maxBytes, _cache.AreaRgbaBytes);
                // GPU residency is a subset of the retained repository, never more (ARC-COMBAT-005: with the fallen poses added to it).
                var poses = AreaOverlayExtra();
                Require(_cache.AreaCount <= old.CpuFrames + poses.Frames && _cache.AreaRgbaBytes <= old.CpuBytes + poses.Bytes, "GPU_BUDGET");
                // The cache's visible set is exactly the frames this view draws per item;
                // everything else resident stays within the retention allowance.
                var drawn = _canvas.AreaVisibleCommands.Where(command => _canvas.AreaChunkCount == 0 || command.Identity is not null)
                    .Select(command => command.Frame).Distinct().Count();
                var retainedFrames = _cache.AreaCount - _cache.AreaVisibleFrames; var retainedBytes = _cache.AreaRgbaBytes - _cache.AreaVisibleBytes;
                maxRetained = Math.Max(maxRetained, retainedFrames);
                Require(_cache.AreaVisibleFrames == drawn && retainedFrames >= 0 && retainedBytes >= 0
                    && retainedFrames <= _cache.AreaRetainFrames && retainedBytes <= _cache.AreaRetainBytes, "GPU_RETENTION");
                // Terrain chunk textures are a separate bounded LRU, never beyond its caps.
                maxChunks = Math.Max(maxChunks, _canvas.AreaChunkResidentCount); maxChunkBytes = Math.Max(maxChunkBytes, _canvas.AreaChunkResidentBytes);
                Require(_canvas.AreaChunkResidentCount <= AreaChunkTextureCache.MaxChunks && _canvas.AreaChunkResidentBytes <= AreaChunkTextureCache.MaxBytes, "CHUNK_BUDGET");
                Require(chunked ? _canvas.AreaTerrainChunked : _canvas.AreaChunkResidentCount == 0, "TERRAIN_MODE");
            }
            return (steps, seen.Count, maxVisible, maxLive, maxBytes, maxChunks, maxChunkBytes);
        }
        var chunkCreated = _canvas!.AreaChunkCreatedCount; var chunkDisposed = _canvas.AreaChunkDisposedCount;
        // The exterior is sized to overflow every cache; a smaller whole interior (ARC-RENDER-017) may fit in them, so
        // there eviction is not required, while every per-view budget and the ownership balance still are.
        var mustEvict = _readyMapName is null;
        var chunkedSweep = await Sweep(true);
        Require(chunkedSweep.Swept + taken == old.Commands, "SWEEP_COVERAGE");
        // The envelope needs more chunks than the chunk cap, so the chunk LRU must have
        // evicted, and its own create/dispose accounting must balance.
        var chunkComposed = _canvas.AreaChunkCreatedCount - chunkCreated; var chunkEvictions = _canvas.AreaChunkDisposedCount - chunkDisposed;
        Require(chunkComposed > 0 && (chunkEvictions > 0 || !mustEvict)
            && _canvas.AreaChunkCreatedCount - _canvas.AreaChunkDisposedCount == _canvas.AreaChunkResidentCount, "CHUNK_OWNERSHIP");
        var created = _cache.CreatedCount; var disposed = _cache.DisposedCount;
        var commandSweep = await Sweep(false);
        _canvas.AreaTerrainChunksEnabled = true;
        Require(commandSweep.Swept + taken == old.Commands, "SWEEP_COVERAGE");
        Require(ReferenceEquals(old.Result.Target, _readyArea) && _readyArea!.Frames.Count == old.CpuFrames
            && _readyArea.CpuRgbaBytes == old.CpuBytes, "CPU_REPOSITORY");
        // Per-command terrain needs more frames than visible + retained, so the frame
        // LRU must have evicted; textures come only from the retained CPU repository.
        var reuploads = _cache.CreatedCount - created; var evictions = _cache.DisposedCount - disposed;
        Require(((reuploads > 0 && evictions > 0) || !mustEvict) && _cache.CreatedCount - _cache.DisposedCount == _cache.Count, "GPU_OWNERSHIP");
        Require(maxRetained > 0 || !mustEvict, "GPU_RETENTION");
        var steps = chunkedSweep.Steps + commandSweep.Steps; var sweptCommands = commandSweep.Swept;
        var maxVisible = Math.Max(chunkedSweep.MaxVisible, commandSweep.MaxVisible);
        var maxLive = Math.Max(chunkedSweep.MaxLive, commandSweep.MaxLive); var maxBytes = Math.Max(chunkedSweep.MaxBytes, commandSweep.MaxBytes);
        var maxChunks = chunkedSweep.MaxChunks; var maxChunkBytes = chunkedSweep.MaxChunkBytes;
        GD.Print($"ARC-RENDER-007 resources sweep steps={steps} commands={sweptCommands} taken={taken} maxVisible={maxVisible} maxGpu={maxLive}/{maxBytes} reuploads={reuploads} evictions={evictions} maxRetained={maxRetained} cpu={old.CpuFrames}/{old.CpuBytes} chunks={chunkComposed}/{chunkEvictions} maxChunks={maxChunks}/{maxChunkBytes}");

        static long Managed() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
        static long Resident() { using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh(); return process.WorkingSet64; }
        static int Handles() => System.IO.Directory.GetFileSystemEntries("/dev/fd").Length;
        var arguments = _arguments ?? throw new InvalidOperationException("AREA_RESOURCES_ARGUMENTS_REQUIRED");
        // ARC-RENDER-018: the post-GC heap of a large map still varies between frames, so both the baseline and the retained
        // size are the least of three post-GC samples a frame apart; a real retention stays reachable in every sample.
        async Task<long[]> ManagedSamples()
        {
            var samples = new long[3];
            for (var sample = 0; sample < samples.Length; sample++)
            {
                if (sample > 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                samples[sample] = Managed();
            }
            return samples;
        }
        var baselineSamples = await ManagedSamples();
        var baseline = (Managed: baselineSamples.Min(), Resident: Resident(), Handles: Handles());
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
        var afterSamples = await ManagedSamples();
        var after = (Managed: afterSamples.Min(), Resident: Resident(), Handles: Handles());
        var retiredAlive = old.Result.IsAlive || old.Frames.IsAlive;
        GD.Print($"ARC-RENDER-007 resources reload observed generations={old.Generation}>{firstGeneration}>{secondGeneration} loadMs={old.ElapsedMilliseconds}/{reloadMilliseconds} managed={baseline.Managed}/{after.Managed} samples={string.Join(',', baselineSamples)}/{string.Join(',', afterSamples)} resident={baseline.Resident}/{after.Resident} handles={baseline.Handles}/{after.Handles} retiredAlive={retiredAlive}");
        Require(_cache.CreatedCount - _cache.DisposedCount == _cache.Count && _cache.AreaCount <= old.CpuFrames, "RELOAD_GPU_OWNERSHIP");
        // One full original load takes minutes, so steady state is one replaced
        // generation. The retired result and its frame repository must be
        // collectable, descriptors must not grow, and the post-GC managed heap
        // may not grow by half a repository (map mode: see the floors below). Resident size is recorded only:
        // freed runtime/driver pages need not return to the system.
        Require(!retiredAlive, "RETIRED_GENERATION_ALIVE");
        Require(after.Handles <= baseline.Handles, "HANDLE_GROWTH");
        // A small whole map's repository (the cave's is 1.5 MB) is below the heap's ordinary noise, so map mode bounds the
        // growth by at least 8 MiB, and on a large map (tarant sewers-02: 64 sectors, ~450 MB heap, up to +26 MB in one sample)
        // by at least 4 % of the heap before the reload (ARC-RENDER-018); the retired generation itself is proven
        // collected above.
        Require(after.Managed - baseline.Managed < (mustEvict ? old.CpuBytes / 2
            : Math.Max(Math.Max(old.CpuBytes / 2, 8L << 20), baseline.Managed / 25)), "MANAGED_RETENTION");
        var resources = new { Outcome = "PASS", SweepSteps = steps, SweepCommands = sweptCommands, SweepTaken = taken, MaxVisible = maxVisible, MaxGpuFrames = maxLive,
            MaxGpuBytes = maxBytes, Reuploads = reuploads, Evictions = evictions,
            ChunksComposed = chunkComposed, ChunkEvictions = chunkEvictions, MaxResidentChunks = maxChunks, MaxResidentChunkBytes = maxChunkBytes, CpuFrames = old.CpuFrames, CpuBytes = old.CpuBytes,
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
