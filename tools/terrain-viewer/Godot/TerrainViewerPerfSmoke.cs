using System.Diagnostics;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-367 (O10 of the performance diagnosis ARC-RES-102, deleted; see docs/project/status/tools.md): the timing probe's smoke stages. The probe (Arcanum.World.PerfProbe) prints
// one `ARC-DEV-367 perf ...` line per mark and span; these stages drive what the diagnosis measured and print the tables. They measure, they gate
// nothing: PASS means every measurement was taken.
//   perf (ARC_AREA_MAP_SMOKE_ONLY=perf, map mode with --hero, a sector with a map exit: the Crash Site window has none, use --sector 1409,1314 or
//   another that does): the startup marks (process start, load begin, worker, publication, first frame), each window key (I C L T W M) opened
//   six times through the production key route (iteration 0 is the cold open), then a map change out through an exit and back.
//   perf-menu (map mode with --main-menu): the same startup marks to the main menu's first frame, then a new game from the menu.
public partial class TerrainViewerApp
{
    /// <summary>Marks <paramref name="name"/> once the next frame is drawn (the probe off: nothing); a headless run draws none.</summary>
    private void PerfMarkAfterDraw(string name)
    {
        if (!PerfProbe.Enabled || DisplayServer.GetName() == "headless") return;
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw, Callable.From(() => PerfProbe.Mark(name)), (uint)GodotObject.ConnectFlags.OneShot);
    }

    private static double PerfMedian(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string PerfText(double value) => PerfProbe.Text(value);

    /// <summary>The startup table: process start to the load, its worker and publication, the first frame, and the menu when it opened.</summary>
    private static async Task<string> PerfStartupLineAsync(SceneTree tree, bool menu)
    {
        // The first frame after the publication (and after the menu's opening) is a one-shot mark; wait for it.
        for (var i = 0; i < 1200 && (PerfProbe.TimeOf("load.first-frame") is null || (menu && PerfProbe.TimeOf("menu.first-frame") is null)); i++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        double T(string name) => PerfProbe.TimeOf(name) ?? double.NaN;
        string Ms(double value) => double.IsNaN(value) ? "-" : PerfText(value);
        var line = $"{PerfProbe.Prefix} startup process-to-load-begin={Ms(T("load.begin"))} worker={Ms(PerfProbe.MillisecondsOf("load.worker") ?? double.NaN)}" +
            $" decoded-at={Ms(T("load.decoded"))} publish={Ms(PerfProbe.MillisecondsOf("load.publish") ?? double.NaN)} first-frame-at={Ms(T("load.first-frame"))}";
        if (menu) line += $" menu-open-at={Ms(T("menu.open"))} menu-first-frame-at={Ms(T("menu.first-frame"))} start-to-menu={Ms(T("menu.first-frame"))}";
        return line;
    }

    private async Task RunAreaPerfSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-367 perf smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PERF_SMOKE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null)
        { GD.Print($"ARC-DEV-367 native-smoke perf NONE map={_readyMapName} (no hero)"); return; }
        // The stage waits for drawn frames; a headless run draws none.
        if (DisplayServer.GetName() == "headless") { GD.Print("ARC-DEV-367 native-smoke perf NONE (headless: no frame is drawn)"); return; }
        PerfProbe.Enable();
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Draw() { await Frame(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-367-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            _worldFocused = true;
            GD.Print(await PerfStartupLineAsync(GetTree(), menu: false));
            if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await Frame(); }
            for (var i = 0; i < 1200 && _worldMapData is null; i++) { await Frame(); AdoptWorldMapData(); }

            // The spell pane is not on the interface stack; every other window is.
            bool Shown(Key key) => key switch
            {
                Key.M => _spellPane?.Shown == true, Key.I => WindowOpen(InterfaceMode.Inventory), Key.C => WindowOpen(InterfaceMode.Character),
                Key.L => WindowOpen(InterfaceMode.Logbook), Key.T => WindowOpen(InterfaceMode.Schematic), _ => WorldMapOpen,
            };
            // The windows: the first open is cold, the next five are warm.
            var windows = new (Key Key, string Name)[] { (Key.I, "inventory"), (Key.C, "sheet"), (Key.L, "logbook"), (Key.T, "schematics"), (Key.W, "worldmap"), (Key.M, "spells") };
            var missing = new List<string>();
            foreach (var (key, name) in windows)
            {
                List<double> sync = [], frame = [];
                for (var iteration = 0; iteration < 6; iteration++)
                {
                    var start = Stopwatch.GetTimestamp();
                    InventoryKey(key);
                    var openSync = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    await Draw();
                    var toFrame = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var opened = Shown(key);
                    GD.Print($"{PerfProbe.Prefix} window name={name} iteration={iteration} open-sync={PerfText(openSync)} to-first-frame={PerfText(toFrame)} opened={opened}");
                    if (!opened) { missing.Add(name); break; }
                    sync.Add(openSync); frame.Add(toFrame);
                    InventoryKey(key); await Draw();
                    Require(!Shown(key), $"{name}: the key did not close the window again (stack {StackText()})");
                }
                if (sync.Count == 6)
                    GD.Print($"{PerfProbe.Prefix} windows name={name} cold-open-sync={PerfText(sync[0])} cold-to-first-frame={PerfText(frame[0])}" +
                        $" warm-open-sync={PerfText(PerfMedian(sync.Skip(1).ToList()))} warm-to-first-frame={PerfText(PerfMedian(frame.Skip(1).ToList()))}");
            }
            Require(missing.Count == 0, "windows that did not open: " + string.Join(", ", missing));

            // The map change: out through an exit to another map and back; the load's own lines (load.worker.*, publish.*) are in the log.
            var home = _readyMapName; var homeRuntime = CurrentAreaMap;
            async Task<bool> Leg(string leg, Func<Arcanum.Data.Maps.MapTransitionSite, bool> wanted)
            {
                var start = Stopwatch.GetTimestamp();
                var began = PerfProbe.SinceProcessStartMs;
                if (await UseMapExitAsync(wanted) is null) return false;
                await Draw();
                var total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var firstFrame = PerfProbe.TimeOf("load.first-frame");
                GD.Print($"{PerfProbe.Prefix} map-change leg={leg} map={_readyMapName} total={PerfText(total)} worker={PerfText(PerfProbe.MillisecondsOf("load.worker") ?? double.NaN)}" +
                    $" publish={PerfText(PerfProbe.MillisecondsOf("load.publish") ?? double.NaN)} load-begin-to-first-frame={(firstFrame is { } f && PerfProbe.TimeOf("load.begin") is { } b && f >= began ? PerfText(f - b) : "-")}");
                return true;
            }
            Require(_mapWindowCentre is not null || _readyMapName is not null, "a map window is shown");
            Require(await Leg("out", site => site.DestinationRuntime != homeRuntime), $"map {home} has no usable exit to another map in this window (pass --sector with one that has)");
            Require(await Leg("back", site => site.DestinationRuntime == homeRuntime), "no exit back");
            GD.Print("ARC-DEV-367 native-smoke perf PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = home, Windows = windows.Length }));
        }
        finally
        {
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task RunAreaPerfMenuSmokeAsync()
    {
        if (DisplayServer.GetName() == "headless") { GD.Print("ARC-DEV-367 native-smoke perf-menu NONE (headless: no frame is drawn)"); return; }
        PerfProbe.Enable();
        for (var i = 0; i < 1200 && !MainMenuOpen; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!MainMenuOpen) { GD.Print("ARC-DEV-367 native-smoke perf-menu NONE (the main menu did not open: " + _mainMenuStatus + ")"); return; }
        GD.Print(await PerfStartupLineAsync(GetTree(), menu: true));
        // A new game from the menu (the first pre-made hero), through the menu's own start; `game.new` is the synchronous call, a load it starts prints its own lines.
        var refused = StartNewGame(0, sceneFlow: false);
        for (var i = 0; i < 60; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"{PerfProbe.Prefix} new-game ms={PerfText(PerfProbe.MillisecondsOf("game.new") ?? double.NaN)} refused={refused ?? "no"}");
        GD.Print("ARC-DEV-367 native-smoke perf-menu PASS");
    }
}
