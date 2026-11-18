using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-1398 native observation (stage "soak", only when named, map mode with --hero, a sector with a map exit: the Crash Site window has none,
// use --sector 1409,1314; the zone `soak`, not part of the per-PR scope or of the daily complete run): a long, scripted, play-like loop that
// watches the resource counters for a leak. One cycle opens and closes each window key (I C L T W M and S, the sleep window, which may refuse
// where resting is not allowed), takes a quick save (F7) and loads it back (F8), then leaves the map by an exit and
// returns by the exit back (two whole map loads). The cycles repeat for ARC_SOAK_STAGE_MINUTES (6 by default); a full collection runs and the
// counters are read (ViewerSoakSample, the soak's own record) at the start of every cycle. After the 120 s warm-up (ViewerSoakReport) a counter
// that grew past its allowance (ViewerSoakLimits) fails the stage; the summary line gives each counter's first, last and maximum reading.
// The window moves of the main map are not in the loop (the walk stage walks them; a walk that leaves a sector is no menu-free scripted step).
public partial class TerrainViewerApp
{
    private async Task RunAreaSoakSmokeAsync()
    {
        static void Fail(string why)
        {
            GD.Print("ARC-DEV-1398 soak smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SOAK_SMOKE_FAILED");
        }
        static void Require(bool value, string why) { if (!value) Fail(why); }
        if (_areaSession is not { } session || session.QueryHero() is null)
        { GD.Print($"ARC-DEV-1398 native-smoke soak NONE map={_readyMapName} (no hero)"); return; }
        // The stage waits for drawn frames; a headless run draws none.
        if (DisplayServer.GetName() == "headless") { GD.Print("ARC-DEV-1398 native-smoke soak NONE (headless: no frame is drawn)"); return; }
        var minutes = ViewerSoakSettings.StageMinutes(System.Environment.GetEnvironmentVariable, out var problem);
        if (minutes is null) { Fail(problem ?? "bad length"); return; }

        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Draw() { await Frame(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-1398-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            _worldFocused = true;
            if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await Frame(); }
            for (var i = 0; i < 1200 && _worldMapData is null; i++) { await Frame(); AdoptWorldMapData(); }
            Require(_mapWindowCentre is not null || _readyMapName is not null, "a map window is shown");

            bool Shown(Key key) => key switch
            {
                Key.M => _spellPane?.Shown == true, Key.I => WindowOpen(InterfaceMode.Inventory), Key.C => WindowOpen(InterfaceMode.Character),
                Key.L => WindowOpen(InterfaceMode.Logbook), Key.T => WindowOpen(InterfaceMode.Schematic), Key.S => SleepOpen, _ => WorldMapOpen,
            };
            var windows = new (Key Key, string Name, bool Needed)[]
            {
                (Key.I, "inventory", true), (Key.C, "sheet", true), (Key.L, "logbook", true), (Key.T, "schematics", true), (Key.W, "worldmap", true),
                (Key.M, "spells", true), (Key.S, "sleep", false),
            };
            var samples = new List<ViewerSoakSample>();
            var started = System.Diagnostics.Stopwatch.StartNew();
            var cycle = 0; var sleepRefused = 0;
            async Task<bool> Leg(Func<Arcanum.Data.Maps.MapTransitionSite, bool> wanted)
            {
                if (await UseMapExitAsync(wanted) is null) return false;
                await Draw();
                return true;
            }
            while (cycle == 0 || started.Elapsed.TotalSeconds < minutes.Value * 60)
            {
                // A full collection first, so the live-heap reading is of this moment, not of the last time the runtime chose to collect.
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var sample = ReadResourceSample((int)started.Elapsed.TotalSeconds);
                samples.Add(sample);
                GD.Print($"ARC-DEV-1398 soak cycle={cycle} " + sample.Text()[(ViewerResourceLine.Prefix.Length + 1)..]);

                foreach (var (key, name, needed) in windows)
                {
                    InventoryKey(key); await Draw();
                    if (!Shown(key))
                    {
                        Require(!needed, $"cycle {cycle}: the {name} window did not open (stack {StackText()})");
                        sleepRefused++; continue;
                    }
                    InventoryKey(key); await Draw();
                    Require(!Shown(key), $"cycle {cycle}: the key did not close the {name} window again (stack {StackText()})");
                }

                // A quick save and its load through the load screen, on the map the cycle stands on.
                QuickKey(Key.F7);
                Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, $"cycle {cycle}: F7 did not save: {_quickSaveStatus}");
                QuickKey(Key.F8); // an automated run loads at once (the deferred load screen's frames are the quick-save smoke's synthetic ones, which do not mix with the real frames here)
                Require(_quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, $"cycle {cycle}: F8 did not load: {_quickSaveStatus}");
                await Draw();

                // Out through an exit to another map and back: two whole map loads.
                var homeRuntime = CurrentAreaMap;
                Require(await Leg(site => site.DestinationRuntime != homeRuntime), $"cycle {cycle}: map {_readyMapName} has no usable exit to another map in this window (pass --sector with one that has)");
                Require(await Leg(site => site.DestinationRuntime == homeRuntime), $"cycle {cycle}: no exit back");
                cycle++;
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            samples.Add(ReadResourceSample((int)started.Elapsed.TotalSeconds));
            GD.Print(ViewerSoakReport.Summary(samples));
            var exceeded = ViewerSoakLimits.Exceeded(samples);
            Require(exceeded.Count == 0, "counters past their allowance after the warm-up: " + string.Join("; ", exceeded));
            GD.Print("ARC-DEV-1398 native-smoke soak PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Minutes = minutes, Cycles = cycle, SleepRefused = sleepRefused }));
        }
        finally
        {
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
