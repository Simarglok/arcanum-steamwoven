using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-170 (B06, smoke plan SM05) native observation (stage "npc-resident", only when named, map mode with --hero, AI on, a town: --sector 1409,1314): the NPCs of the
    // town that keep a waypoint list or a standpoint are leaderless route NPCs; the AI host's pass starts their walks (the unforced waypoint and standpoint steps, goal 7), so at
    // least one of them, within the AI's reach of the hero, stands on a cell three or more cells from its first one after ninety seconds of game time. F7 and F8 then bring every
    // recorded NPC back on the cell it was saved on.
    private async Task RunAreaNpcResidentSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-170 npc-resident smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_RESIDENT_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null)
        { GD.Print($"ARC-DEV-170 native-smoke npc-resident NONE map={_readyMapName} (needs a hero and combat values)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-170-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps } ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            var host = AreaAiHostNow();
            if (host is null) { GD.Print("ARC-DEV-170 native-smoke npc-resident NONE (no AI host)"); return; }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 31_000_000L;
            TickAreaClock(hostMs);
            long Seconds() => session.QueryClockSnapshot().GameTimestampMs / 1000;
            // The NPCs a frame saw with a walk started by the routine (the trace shows the last step only, so each frame is looked at).
            var walkStarted = new HashSet<string>(StringComparer.Ordinal);
            string[] watched = [];
            async Task Run(long gameSeconds, int cap)
            {
                var until = Seconds() + gameSeconds;
                for (var frame = 0; frame < cap && Seconds() < until; frame++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                    foreach (var e in watched) if (host.TraceOf(e)?.Route?.Contains("StartWalk to", StringComparison.Ordinal) == true) walkStarted.Add(e);
                    if (frame % 200 == 199) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            // The first ticks of the sectors the map opened fold (the forced steps put route NPCs on their waypoints); the observation starts after them.
            await Run(30, 4000);
            host = AreaAiHostNow()!;
            var hero = session.QueryActor().Cell;
            var routed = host.Scene.Npcs.Where(n => host.RouteIndexOf(n.Entity) is not null && AreaLife.Of(session, _combatHost, CurrentAreaMap, n.Entity) is not null && host.Tile(n.Entity) is { } at
                && AiExecutorRules.Distance(at, new(hero.X, hero.Y)) <= 24).Select(n => n.Entity).Take(12).ToArray();
            if (routed.Length == 0) { GD.Print($"ARC-DEV-170 native-smoke npc-resident NONE map={_readyMapName} (no route NPC within 24 cells of the hero)"); return; }
            var first = routed.ToDictionary(e => e, e => host.Tile(e)!.Value);
            watched = routed;
            await Run(90, 6000);
            watched = [];
            var after = routed.ToDictionary(e => e, e => host.Tile(e)!.Value);
            var moved = routed.Where(e => AiExecutorRules.Distance(first[e], after[e]) >= 3).ToArray();
            var started = walkStarted.Count;
            var movedByWalk = moved.Where(walkStarted.Contains).ToArray();
            GD.Print($"ARC-DEV-170 npc-resident trace routed={routed.Length} moved={moved.Length} started={started} " + string.Join(" | ", routed.Take(6).Select(e => $"{first[e].X},{first[e].Y}->{after[e].X},{after[e].Y} {host.TraceOf(e)?.Route}")));
            Require(movedByWalk.Length >= 1, $"at least one of {routed.Length} route NPCs started a walk and moved three cells in ninety seconds of game time (moved {moved.Length}, walks started {started})");
            // F7, then F8 on the same map: every recorded NPC stands on its saved cell.
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            var saved = routed.ToDictionary(e => e, e => host.Tile(e)!.Value);
            await Run(20, 2000);
            var running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            // A load on the same window publishes at once (no handoff); one that moves the window is awaited.
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            for (var i = 0; i < 5; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var reloaded = AreaAiHostNow() ?? throw new InvalidOperationException("ARC-DEV-170 smoke: no host after the load");
            var wrong = routed.Where(e => reloaded.Tile(e) != saved[e]).ToArray();
            Require(wrong.Length == 0, "after the load every recorded NPC stands on its saved cell; off: " + string.Join(" ", wrong.Select(e => $"{reloaded.Tile(e)} not {saved[e]}")));
            GD.Print("ARC-DEV-170 native-smoke npc-resident PASS " + System.Text.Json.JsonSerializer.Serialize(new { Routed = routed.Length, Moved = moved.Length, MovedByWalk = movedByWalk.Length, Started = started, Restored = routed.Length }));
        }
        finally
        {
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
    }
}
