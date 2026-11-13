using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
        // ARC-UI-004: items the earlier smoke phases picked up are no longer in the world and are never drawn. The canvas learns the hidden
        // set from the session's per-frame sync (taken items, destroyed or data-off objects, ARC-WORLD-1436), so it can still change after
        // this stage starts (a run of this stage alone has had no frame yet): the coverage check counts a command hidden at any view of a
        // sweep as accounted for (ARC-BUG-1501), and `taken` is read again after the sweeps for the report.
        var taken = _readyArea!.Plan.Commands.Count(command => _canvas!.AreaHiddenOf(command));
        var identified = _readyArea.Plan.Commands.Where(command => command.Identity is not null).ToArray();
        var bounds = old.Bounds; var viewport = WorldViewport;
        // Zoom 1 tiles the envelope with viewport-sized steps; the camera clamp
        // keeps edge views inside the envelope, so every command intersects one. The
        // sweep runs with terrain chunks (production default) and once more with
        // per-command terrain, so both GPU caches must exceed their limits and evict.
        var maxRetained = 0;
        // Every drawn command counts once; a companion drawn where it stands is a moved copy of its plan command (whose own
        // T0 command is hidden). A companion visiting from another map or window (ARC-WORLD-037, ARC-RENDER-024) is drawn from
        // its kept body, whose identity is in no command of this plan: it is not part of the coverage count.
        HashSet<AreaObjectKey>? identities = _readyArea!.Plan.Commands.Where(c => c.Identity is not null).Select(c => c.Identity!.Value).ToHashSet();
        var companions = _areaCompanions?.Companions.Select(c => c.Key).ToHashSet() ?? [];
        async Task<(int Steps, int Swept, int Unaccounted, int MaxVisible, int MaxLive, long MaxBytes, int MaxChunks, long MaxChunkBytes)> Sweep(bool chunked)
        {
            _canvas!.AreaTerrainChunksEnabled = chunked;
            HashSet<AreaDrawCommand> seen = new(ReferenceEqualityComparer.Instance);
            HashSet<AreaDrawCommand> hiddenAtAView = new(ReferenceEqualityComparer.Instance);
            var steps = 0; var maxLive = 0; var maxVisible = 0; long maxBytes = 0; var maxChunks = 0; long maxChunkBytes = 0;
            for (var y = bounds.Y + viewport.Y / 2d; y < bounds.Y + bounds.Height + viewport.Y / 2d; y += viewport.Y)
            for (var x = bounds.X + viewport.X / 2d; x < bounds.X + bounds.Width + viewport.X / 2d; x += viewport.X)
            {
                _camera!.ResetWorld(new Vec2(x, y), viewport, 1); var view = _camera.State;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                // ARC-RENDER-023: the view must still be the one set (the sweep holds the camera).
                Require(_camera.State == view, "CAMERA_MOVED");
                foreach (var command in identified) if (_canvas.AreaHiddenOf(command)) hiddenAtAView.Add(command);
                steps++; seen.UnionWith(_canvas.AreaVisibleCommands.Where(c => c.Identity is not { } key || identities!.Contains(key)));
                // A body of no plan command is a visiting companion's or (ARC-WORLD-063) a junk pile's or placed item's drawn under its own
                // key (ARC-RENDER-033), or (ARC-RENDER-041) a pool of blood's, never anything else. ARC-DEV-089: an item the session
                // holds on the ground under its own row key (an item no holder lists is detached onto the ground, ARC-DEV-085) has no T0
                // body in a window whose plan leaves held items out, so every ground body counts, not only the ones under a placed key.
                Require(_canvas.AreaVisibleCommands.All(c => c.Identity is not { } key || identities!.Contains(key) || companions.Contains(key)
                    || _groundBodies.ContainsKey(key) || _groundOnly.ContainsKey(key) || _bloodPoolKeys.Contains(key)), "UNPLANNED_BODY");
                Require(!_canvas.AreaVisibleCommands.Any(_canvas.AreaHiddenOf), "TAKEN_ITEM_DRAWN");
                maxLive = Math.Max(maxLive, _cache.AreaCount); maxVisible = Math.Max(maxVisible, _canvas.AreaVisibleCount);
                maxBytes = Math.Max(maxBytes, _cache.AreaRgbaBytes);
                // GPU residency is a subset of the retained repository, never more (ARC-COMBAT-005: with the fallen poses added to it).
                var poses = AreaOverlayExtra();
                Require(_cache.AreaCount <= old.CpuFrames + poses.Frames && _cache.AreaRgbaBytes <= old.CpuBytes + poses.Bytes, "GPU_BUDGET");
                // The cache's visible set is exactly the frames this view draws per item;
                // everything else resident stays within the retention allowance.
                // ARC-RENDER-022: the roofs and covered-tile masks of the view are drawn per item too.
                var drawn = _canvas.AreaVisibleCommands.Where(command => _canvas.AreaChunkCount == 0 || command.Identity is not null)
                    .Select(command => command.Frame).Concat(_canvas.AreaVisibleRoofFrames).Distinct().Count();
                var retainedFrames = _cache.AreaCount - _cache.AreaVisibleFrames; var retainedBytes = _cache.AreaRgbaBytes - _cache.AreaVisibleBytes;
                maxRetained = Math.Max(maxRetained, retainedFrames);
                Require(_cache.AreaVisibleFrames == drawn && retainedFrames >= 0 && retainedBytes >= 0
                    && retainedFrames <= _cache.AreaRetainFrames && retainedBytes <= _cache.AreaRetainBytes, "GPU_RETENTION");
                // Terrain chunk textures are a separate bounded LRU, never beyond its caps.
                maxChunks = Math.Max(maxChunks, _canvas.AreaChunkResidentCount); maxChunkBytes = Math.Max(maxChunkBytes, _canvas.AreaChunkResidentBytes);
                Require(_canvas.AreaChunkResidentCount <= AreaChunkTextureCache.MaxChunks && _canvas.AreaChunkResidentBytes <= AreaChunkTextureCache.MaxBytes, "CHUNK_BUDGET");
                Require(chunked ? _canvas.AreaTerrainChunked : _canvas.AreaChunkResidentCount == 0, "TERRAIN_MODE");
            }
            // Every plan command is accounted for: drawn in some view, hidden (no longer in the world) at some view, or (a moved or changed body is drawn as a
            // copy of its plan command, whose own T0 command is then not drawn and not hidden) stood for by a drawn copy under the same identity.
            var seenKeys = seen.Where(command => command.Identity is not null).Select(command => command.Identity!.Value).ToHashSet();
            var unaccounted = _readyArea!.Plan.Commands.Count(command => !seen.Contains(command) && !hiddenAtAView.Contains(command)
                && !(command.Identity is { } key && seenKeys.Contains(key)));
            return (steps, seen.Count, unaccounted, maxVisible, maxLive, maxBytes, maxChunks, maxChunkBytes);
        }
        var chunkCreated = _canvas!.AreaChunkCreatedCount; var chunkDisposed = _canvas.AreaChunkDisposedCount;
        // The exterior is sized to overflow every cache; a smaller whole interior (ARC-RENDER-017) may fit in them, so
        // there eviction is not required, while every per-view budget and the ownership balance still are.
        var mustEvict = _readyMapName is null;
        var chunkedSweep = await WithAreaSmokeCameraHeld(() => Sweep(true));
        if (chunkedSweep.Unaccounted != 0)
            GD.Print($"ARC-RENDER-007 resources detail swept={chunkedSweep.Swept} unaccounted={chunkedSweep.Unaccounted} commands={old.Commands} steps={chunkedSweep.Steps}");
        Require(chunkedSweep.Unaccounted == 0, "SWEEP_COVERAGE");
        // The envelope needs more chunks than the chunk cap, so the chunk LRU must have
        // evicted, and its own create/dispose accounting must balance.
        var chunkComposed = _canvas.AreaChunkCreatedCount - chunkCreated; var chunkEvictions = _canvas.AreaChunkDisposedCount - chunkDisposed;
        Require(chunkComposed > 0 && (chunkEvictions > 0 || !mustEvict)
            && _canvas.AreaChunkCreatedCount - _canvas.AreaChunkDisposedCount == _canvas.AreaChunkResidentCount, "CHUNK_OWNERSHIP");
        var created = _cache.CreatedCount; var disposed = _cache.DisposedCount;
        var commandSweep = await WithAreaSmokeCameraHeld(() => Sweep(false));
        identities = null; // the reload below must find the retired generation unreferenced
        _canvas.AreaTerrainChunksEnabled = true;
        if (commandSweep.Unaccounted != 0)
            GD.Print($"ARC-RENDER-007 resources detail command sweep swept={commandSweep.Swept} unaccounted={commandSweep.Unaccounted} commands={old.Commands} steps={commandSweep.Steps} chunked={chunkedSweep.Swept}");
        Require(commandSweep.Unaccounted == 0, "SWEEP_COVERAGE");
        taken = _readyArea!.Plan.Commands.Count(command => _canvas!.AreaHiddenOf(command));
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

        // ARC-RENDER-025: the last collection of each sample compacts the large-object heap. Before, on a 64-sector map whose
        // large-object heap held 96 to 208 MB of free space, samples differed from the live bytes by up to tens of megabytes
        // from run to run; compacted, they came within about 10 KB of the live bytes there (observed with GC.GetTotalMemory, not a runtime guarantee).
        // ARC-RENDER-1538: the sample is what that blocking collection left, its heap size less its free space (GCMemoryInfo),
        // not GC.GetTotalMemory, the runtime's approximate sum of the space each generation has allocated: on the town sector after
        // the map battery it read 25 to 44 MB above the live bytes, in steps of about 4 MB (a GC region's size on .NET 8; which
        // space it counted was not traced) that changed from one sample to the next while no thread allocated; that was the
        // 16 MB alternation between reloads (#1538).
        static long Managed()
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            var prior = GC.GetGCMemoryInfo(GCKind.FullBlocking).Index;
            GC.Collect();
            var collected = GC.GetGCMemoryInfo(GCKind.FullBlocking);
            // The info must be of a blocking full collection made after `prior`: this sample's own (or, as good, another thread's).
            Require(collected.Index > prior && collected.Generation == 2, "MANAGED_SAMPLE");
            return collected.HeapSizeBytes - collected.FragmentedBytes;
        }
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
        // ARC-DEV-375: this stage proves a reload runs on a fresh owner with fresh counters and that the retired generation is collectable, so the reload must
        // read everything again: the process-resident frame store and visual memo (which a map load fills and a later load is served from) start empty here.
        // ARC-COMBAT-027: the whole-file hash memo (ARC-DEV-374) is the same kind of process-resident store: a file hashed by the first load is not read again, so the
        // reload's read counters (ReadReserved/ReadActual: 969790599 at the first load, 16567076 after) would differ; the memo starts empty here as well.
        AreaResidentFrames.Shared.Clear(); AreaResidentVisuals.Shared.Clear(); AreaResidentEditorRecords.Shared.Clear(); VerifiedFileMemo.Reset();
        var baselineSamples = await ManagedSamples();
        var baseline = (Managed: baselineSamples.Min(), Resident: Resident(), Handles: Handles());
        // ARC-DEV-1426 (#1426): a full scene clear must retire the map's AI host, which holds the map's AreaLoadResult through its scene and its _aiHostArea (no world session
        // means no area frame, so no later AreaAiHostNow to do it; every load begins with a clear). Today ClearReadyMetrics does it through StopAreaTransitions ->
        // UnbindAreaInventory -> UnbindAreaCombat; this probe pins that: it turns the AI on, builds the host, clears the scene and reads the host's fields; it runs after the baseline and loads nothing, the
        // reload below is the stage's own (its own first step is the same clear, so the baseline's loaded map is retired either way). A run without a hero (this stage alone, no --hero) builds no host and the probe says so.
        {
            var aiBefore = _aiForSmoke; _aiForSmoke = true;
            for (var wait = 0; wait < 60 && AreaAiHostNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_aiHost is not null)
            {
                ClearPublishedScene();
                var held = _aiHost is not null || _aiHostArea is not null || _aiHostStamp is not null || _aiLineOfFire is not null || _aiPcOccupancy is not null;
                GD.Print($"ARC-DEV-1426 resources ai-host-clear built=true heldAfterClear={held}");
                Require(!held, "AI_HOST_HELD_AFTER_CLEAR");
            }
            else GD.Print($"ARC-DEV-1426 resources ai-host-clear built=false (the probe did not run: hero={_areaSession?.QueryHero() is not null} combat={_combatHost is not null})");
            _aiForSmoke = aiBefore;
        }
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
        // growth by at least 8 MiB, and on a large map (tarant sewers-02: 64 sectors, about 540 MB live heap, up to +26 MB in one uncompacted sample)
        // by at least 4 % of the heap before the reload (ARC-RENDER-018); the retired generation itself is proven
        // collected above.
        var growth = after.Managed - baseline.Managed;
        var limit = mustEvict ? old.CpuBytes / 2 : Math.Max(Math.Max(old.CpuBytes / 2, 8L << 20), baseline.Managed / 25);
        // ARC-DEV-060: after the whole map chain the first reloads also fill the content the earlier stages had not yet made, once
        // (elven ruins, one run: 14.2 MB after the overlapped pair, 20.8 MB after one more reload, 20.8 MB after the next; observed).
        // Over the limit, one more reload tells the two apart: a retention every replaced generation adds grows the heap again by as
        // much, a one-time fill does not. The bound then holds one generation's retention, not the pair's: a retention between half
        // the bound and the bound per generation, which the pair alone showed, now passes.
        // ARC-BUG-1501 saw the heap alternate by about 16 MB between reloads on the town sector and made up to two repeated reloads; that was
        // GC.GetTotalMemory's region accounting, not live objects (ARC-RENDER-1538, see Managed above: the live heap stayed within about
        // 10 KB over six reloads), so one repeated reload is made again.
        long? repeated = null;
        if (growth >= limit)
        {
            var published = CaptureAreaResourceBaseline();
            await StartLoadAsync(arguments, false);
            Require(_readyArea is not null && _readyArea.Generation == _generation && _generation > published.Generation, "STALE_PUBLICATION");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var againSamples = await ManagedSamples();
            repeated = againSamples.Min() - after.Managed;
            GD.Print($"ARC-RENDER-007 resources reload again generation={_generation} managed={againSamples.Min()} samples={string.Join(',', againSamples)} growth={growth} repeated={repeated} limit={limit}");
            Require(!published.Result.IsAlive && !published.Frames.IsAlive, "RETIRED_GENERATION_ALIVE");
        }
        Require(Math.Min(growth, repeated ?? growth) < limit, "MANAGED_RETENTION");
        var resources = new { Outcome = "PASS", SweepSteps = steps, SweepCommands = sweptCommands, SweepTaken = taken, MaxVisible = maxVisible, MaxGpuFrames = maxLive,
            MaxGpuBytes = maxBytes, Reuploads = reuploads, Evictions = evictions,
            ChunksComposed = chunkComposed, ChunkEvictions = chunkEvictions, MaxResidentChunks = maxChunks, MaxResidentChunkBytes = maxChunkBytes, CpuFrames = old.CpuFrames, CpuBytes = old.CpuBytes,
            LoadMilliseconds = new[] { old.ElapsedMilliseconds, reloadMilliseconds },
            ManagedBytes = new[] { baseline.Managed, after.Managed }, RepeatedGrowth = repeated,
            ResidentBytes = new[] { baseline.Resident, after.Resident },
            Handles = new[] { baseline.Handles, after.Handles },
            RetiredGenerationCollected = true, CancelledGenerationPublished = false };
        var reportPath = System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            System.IO.File.WriteAllText(reportPath + ".resources.json", System.Text.Json.JsonSerializer.Serialize(resources));
        GD.Print("ARC-RENDER-007 resources reload PASS");
    }
}
