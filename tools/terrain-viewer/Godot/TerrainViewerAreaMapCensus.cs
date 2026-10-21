using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-027 native census (the "census" map-smoke stage, run only when named): every map of the game folder's catalogue
// in turn, through the ordinary map load (the main world map as a window around its map-list start): it must publish, draw a
// frame with something visible, admit its spawn, and its step evidence must leave no terrain cell and no object Unknown. For
// each map the census records the load time, the managed heap after a full collection, the process working set, the CPU
// frames and the frame times of a few drawn frames, and requires them within the census budgets. A catalogued map map mode
// does not open (unlisted, or without a sector file) is reported with its reason, not loaded.
public partial class TerrainViewerApp
{
    // Census budgets, about two to three times the largest values observed (the owner's Mac, Debug source run, 2026-10-21: K'na
    // Tha loads in 20.6 s with a 1.04 GB heap and a 1.48 GB working set; the slowest drawn frame, bmc's, took 127 ms).
    internal const long CensusLoadBudgetMs = 60000, CensusFrameBudgetMs = 250, CensusHeapBudgetBytes = 2L << 30, CensusWorkingSetBudgetBytes = 3L << 30;
    private const int CensusFrames = 8;

    private async Task RunAreaMapCensusAsync()
    {
        var origin = _arguments!;
        var maps = AreaMapCensus.Maps(origin);
        var rows = new List<object>(); var failures = new List<string>();
        long maxLoad = 0, maxFrame = 0, maxHeap = 0, maxWorkingSet = 0; var opened = 0;
        foreach (var entry in maps)
        {
            if (!entry.Openable)
            {
                GD.Print("ARC-RENDER-027 census map NONE " + System.Text.Json.JsonSerializer.Serialize(new { entry.Name, entry.Listed, entry.SectorFiles }));
                rows.Add(new { entry.Name, Opened = false, Reason = entry.Listed ? "NO_SECTOR_FILES" : "UNLISTED" });
                continue;
            }
            var next = origin with { AreaMapName = entry.Name, AreaMapSector = null, AreaMapArrival = null };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await StartLoadAsync(next, false);
            var loadMs = clock.ElapsedMilliseconds;
            string? failure = null;
            if (_exiting) return;
            if (!SameMapName(_readyMapName, entry.Name) || _readyArea is not { } area || _worldActor.Session is not AreaGridSession session)
                failure = "NOT_PUBLISHED";
            else
            {
                var frames = new List<long>();
                for (var i = 0; i < CensusFrames; i++)
                {
                    var frame = System.Diagnostics.Stopwatch.StartNew();
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    frames.Add(frame.ElapsedMilliseconds);
                }
                var visible = _canvas!.AreaVisibleCount;
                var steps = session.Snapshot;
                var spawn = session.Chart.ToWorld(session.State.CommittedCell);
                var heap = GC.GetTotalMemory(true); var workingSet = System.Environment.WorkingSet;
                var frameMax = frames.Skip(1).DefaultIfEmpty(frames[0]).Max(); // the first frame after a publication carries its upload
                failure = visible <= 0 ? "NOTHING_DRAWN"
                    : !steps.IsAdmitted(spawn) ? "SPAWN_NOT_ADMITTED"
                    : steps.TerrainUnknownCells != 0 || steps.UnknownObjects != 0 ? $"UNKNOWN terrain={steps.TerrainUnknownCells} objects={steps.UnknownObjects}"
                    : loadMs > CensusLoadBudgetMs ? $"LOAD_BUDGET {loadMs}"
                    : frameMax > CensusFrameBudgetMs ? $"FRAME_BUDGET {frameMax}"
                    : heap > CensusHeapBudgetBytes ? $"HEAP_BUDGET {heap}"
                    : workingSet > CensusWorkingSetBudgetBytes ? $"WORKING_SET_BUDGET {workingSet}" : null;
                maxLoad = Math.Max(maxLoad, loadMs); maxFrame = Math.Max(maxFrame, frameMax); maxHeap = Math.Max(maxHeap, heap); maxWorkingSet = Math.Max(maxWorkingSet, workingSet);
                var row = new { entry.Name, Opened = failure is null, Window = _mapWindowCentre is { } c ? new { c.X, c.Y } : null, LoadMs = loadMs,
                    FirstFrameMs = frames[0], FrameMaxMs = frameMax, Visible = visible, Bodies = area.Plan.DrawableBodies, Cells = steps.AdmittedCells,
                    area.CpuRgbaBytes, HeapBytes = heap, WorkingSetBytes = workingSet, Failure = failure };
                rows.Add(row);
                GD.Print("ARC-RENDER-027 census map " + (failure is null ? "PASS " : "FAIL ") + System.Text.Json.JsonSerializer.Serialize(row));
            }
            if (failure is null) opened++;
            else failures.Add($"{entry.Name}: {failure}");
        }
        var summary = System.Text.Json.JsonSerializer.Serialize(new { Catalogued = maps.Count, Openable = maps.Count(m => m.Openable), Opened = opened,
            MaxLoadMs = maxLoad, MaxFrameMs = maxFrame, MaxHeapBytes = maxHeap, MaxWorkingSetBytes = maxWorkingSet,
            Budgets = new { LoadMs = CensusLoadBudgetMs, FrameMs = CensusFrameBudgetMs, HeapBytes = CensusHeapBudgetBytes, WorkingSetBytes = CensusWorkingSetBudgetBytes },
            Failures = failures, Parity = "NOT_APPLICABLE (a viewer census)" });
        if (failures.Count > 0)
        {
            GD.Print("ARC-RENDER-027 smoke detail " + summary);
            throw new InvalidOperationException("AREA_MAP_CENSUS_FAILED");
        }
        GD.Print("ARC-RENDER-027 native-smoke census PASS " + summary);
    }
}
