using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-027 native census (the "census" map-smoke stage, run only when named, on a map-mode start): every map of the game
// folder's catalogue map mode opens, in turn, through the ordinary map load (the main world map as a window around its map-list
// start). Each must publish, draw a frame with visible draw commands, and stand its walker on a tile it can step from; its step
// evidence must leave no terrain cell, no object and no edge Unknown but the edges Unknown by contract (an unlock script, an
// hour lock: their rule is a script's or the hour's). For each map the census records the load time, the frame periods of the
// seven frames after the publication's first, the managed heap after a full collection, the process working set and the CPU
// frame bytes, and requires them within the census budgets. A load that fails is recorded for its map and the census goes on;
// a catalogued map map mode does not open (no sector file, or a window without a map-list start) is reported, not loaded.
public partial class TerrainViewerApp
{
    // Census budgets, about two to three times the largest values observed over five runs (the owner's Mac, Debug source run,
    // 2026-10-21: K'na Tha loads in 21–22 s with a heap of 1.03–1.06 GB and a working set of 1.46–1.57 GB; the slowest frame
    // period 121–169 ms, once 301 ms, wheel clan's). The frame budget bounds stalls (a visible window; frames are paced at the display rate, and
    // other processes on the machine move it), not a frame rate.
    internal const long CensusLoadBudgetMs = 60000, CensusFrameBudgetMs = 750, CensusHeapBudgetBytes = 2L << 30, CensusWorkingSetBudgetBytes = 3L << 30;
    private const int CensusFrames = 8;
    // Edge reasons Unknown by contract (ARC-WORLD-018, ARC-RES-049): the census reports them; any other reason fails it.
    private static readonly HashSet<string> CensusContractEdges = new(StringComparer.Ordinal) { "PORTAL_UNLOCK_SCRIPT", "PORTAL_LOCK_DEPENDS_ON_HOUR" };
    private bool _censusLoading;
    private string? _censusLoadFailure;

    /// <summary>While the census loads a map, a load failure is recorded for that map instead of ending the native smoke.</summary>
    private bool CensusLoadFailed(string frontier)
    {
        if (!_censusLoading) return false;
        _censusLoadFailure = frontier;
        GD.Print("ARC-RENDER-027 census load failed " + frontier);
        return true;
    }

    private async Task RunAreaMapCensusAsync()
    {
        if (_readyMapName is null) throw new InvalidOperationException("AREA_MAP_CENSUS_NOT_MAP_MODE");
        var origin = _arguments!;
        var maps = AreaMapCensus.Maps(origin);
        var failures = new List<string>(); var contractEdges = new Dictionary<string, int>(StringComparer.Ordinal);
        long maxLoad = 0, maxFrame = 0, maxHeap = 0, maxWorkingSet = 0; var published = 0; var passed = 0;
        try
        {
            foreach (var entry in maps)
            {
                if (!entry.Openable)
                {
                    GD.Print("ARC-RENDER-027 census map NONE " + System.Text.Json.JsonSerializer.Serialize(new { entry.Name, entry.Listed, entry.SectorFiles,
                        Reason = entry.SectorFiles == 0 ? "NO_SECTOR_FILES" : "WINDOW_NO_START" }));
                    continue;
                }
                GD.Print("ARC-RENDER-027 census map BEGIN " + entry.Name);
                var next = origin with { AreaMapName = entry.Name, AreaMapSector = null, AreaMapArrival = null };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                _censusLoading = true; _censusLoadFailure = null;
                try { await StartLoadAsync(next, false); }
                finally { _censusLoading = false; }
                var loadMs = clock.ElapsedMilliseconds;
                if (_exiting) return;
                string? failure; object row;
                if (_censusLoadFailure is not null || !SameMapName(_readyMapName, entry.Name) || _readyArea is not { } area || _worldActor.Session is not AreaGridSession session)
                {
                    // A failure after the publication (a session refused while the scene binds) fails the map too.
                    failure = (_censusLoadFailure is null ? "NOT_PUBLISHED " : "LOAD_FAILED ") + (_censusLoadFailure ?? "no failure reported");
                    row = new { entry.Name, entry.Listed, LoadMs = loadMs, Failure = failure };
                }
                else
                {
                    published++; _arguments = next;
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
                    var frameMax = frames.Skip(1).Max(); // the first period after a publication carries its upload
                    var gaps = steps.UnknownEdges.Where(e => !CensusContractEdges.Contains(e.Key)).ToArray();
                    foreach (var (reason, count) in steps.UnknownEdges.Where(e => CensusContractEdges.Contains(e.Key)))
                        contractEdges[reason] = contractEdges.GetValueOrDefault(reason) + count;
                    var startSector = entry.Start is { } start ? new Arcanum.Data.Area.AreaSectorCoordinate(start.X >> 6, start.Y >> 6) : (Arcanum.Data.Area.AreaSectorCoordinate?)null;
                    failure = visible <= 0 ? "NOTHING_DRAWN"
                        : !AreaMapCensus.Stands(steps, spawn) ? $"SPAWN_CANNOT_STEP {spawn}"
                        : entry.Window && _mapWindowCentre != startSector ? $"WINDOW_NOT_AT_START {_mapWindowCentre}"
                        : steps.TerrainUnknownCells != 0 || steps.UnknownObjects != 0 || gaps.Length > 0
                            ? $"UNKNOWN terrain={steps.TerrainUnknownCells} objects={steps.UnknownObjects} edges={string.Join(",", gaps.Select(g => $"{g.Key}:{g.Value}"))}"
                        : loadMs > CensusLoadBudgetMs ? $"LOAD_BUDGET {loadMs}"
                        : frameMax > CensusFrameBudgetMs ? $"FRAME_BUDGET {frameMax}"
                        : heap > CensusHeapBudgetBytes ? $"HEAP_BUDGET {heap}"
                        : workingSet > CensusWorkingSetBudgetBytes ? $"WORKING_SET_BUDGET {workingSet}" : null;
                    maxLoad = Math.Max(maxLoad, loadMs); maxFrame = Math.Max(maxFrame, frameMax); maxHeap = Math.Max(maxHeap, heap); maxWorkingSet = Math.Max(maxWorkingSet, workingSet);
                    row = new { entry.Name, entry.Listed, Window = _mapWindowCentre is { } c ? new { c.X, c.Y } : null, LoadMs = loadMs,
                        FirstFrameMs = frames[0], FrameMaxMs = frameMax, VisibleCommands = visible, Bodies = area.Plan.DrawableBodies, Cells = steps.AdmittedCells,
                        ContractEdges = steps.UnknownEdges.Where(e => CensusContractEdges.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value),
                        area.CpuRgbaBytes, HeapBytes = heap, WorkingSetBytes = workingSet, Failure = failure };
                }
                GD.Print("ARC-RENDER-027 census map " + (failure is null ? "PASS " : "FAIL ") + System.Text.Json.JsonSerializer.Serialize(row));
                if (failure is null) passed++;
                else failures.Add($"{entry.Name}: {failure}");
            }
        }
        finally { _arguments = origin; } // the run ends on the last census map with the start map's arguments, and quits
        var summary = System.Text.Json.JsonSerializer.Serialize(new { Catalogued = maps.Count, Openable = maps.Count(m => m.Openable),
            Unlisted = maps.Count(m => !m.Listed), Published = published, Passed = passed, ContractEdges = contractEdges,
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
