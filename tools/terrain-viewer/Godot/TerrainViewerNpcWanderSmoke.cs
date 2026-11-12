using Arcanum.Session;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-AI-1313 (VAG2, VA19) native observation (stage "npc-wander", only when named, map mode with --hero, AI on, the altar's window: --sector 1452,1291): the standpoint step starts the wander goal 65 for a wanderer at its
    // standpoint, and the goal draws tiles within four of the tether and walks there (nodes 0x425930, then the walk, goal 7). The map's own NPCs carry no wander flag the stage could rely on, so it gives the first living free NPC the
    // step's start (the host's order, the same starter the step calls) with its own tile as the tether, and watches the run: it is a table run of the NPC (goal 65 at the bottom, its tether and radius), the NPC walks off its tile,
    // every tile it reaches within the run is within four of the tether, and over many cycles (a finished walk starts the next wander, as the step does at the NPC's next tick) it keeps within that radius and reaches several
    // distinct tiles. The dark wander (goal 66) gets the same, with the cell light of the shown map deciding. Numbers only in the output.
    private async Task RunAreaNpcWanderSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-AI-1313 npc-wander smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_WANDER_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null)
        { GD.Print($"ARC-AI-1313 native-smoke npc-wander NONE map={_readyMapName} (needs a hero and combat values)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var grid = (AreaGridSession)_worldActor.Session!;
            var map = new AreaMapRuntime(_readyArea.Snapshot, grid.Snapshot, grid.Chart);
            AreaSmokePlace(map, grid.State.CommittedCell);
            var host = AreaAiHostNow();
            if (host is null) { GD.Print("ARC-AI-1313 native-smoke npc-wander NONE (no AI host)"); return; }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 33_000_000L;
            TickAreaClock(hostMs);
            void Frames(int count)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
            }
            Frames(40);
            host = AreaAiHostNow()!;
            // The NPC: the first living free NPC with a tile and no run, nearest the hero is not needed; the order of the Npcs list is the window's.
            var candidates = host.Scene.Npcs.Where(n => !host.RunCompanions.Contains(n.Entity) && !host.Gone(n.Entity) && host.Tile(n.Entity) is not null && host.RunOf(n.Entity) is null).ToArray();
            if (candidates.Length == 0)
            { GD.Print($"ARC-AI-1313 native-smoke npc-wander NONE map={_readyMapName} (no idle free NPC among {host.Scene.Npcs.Count} NPCs of the window)"); return; }
            var results = new List<object>();
            foreach (var dark in new[] { false, true })
            {
                var entity = candidates[dark ? Math.Min(1, candidates.Length - 1) : 0].Entity;
                for (var settle = 0; settle < 600 && host.RunOf(entity) is not null; settle++) Frames(1);
                var tether = host.Tile(entity)!.Value;
                var goal = dark ? GoalType.WanderSeekDarkness : GoalType.Wander;
                var order = host.OrderWander(entity, tether, dark);
                var run = host.RunOf(entity);
                Require(order.StartsWith("started", StringComparison.Ordinal) && run is not null, $"the wander of {entity} started: {order}");
                Require(run!.Bottom.Goal == goal && run.Bottom.Tile == tether && run.Bottom.Range == 4, $"the run is a table run of goal {(int)goal} tethered to {tether.X},{tether.Y} with radius 4: {run.Bottom.Goal} {run.Bottom.Tile} {run.Bottom.Range}");
                Require(session.QueryGoals().RunsOf(entity).Any(r => r.Handle == run.Handle), "the run is in the session's goal table");
                var tiles = new HashSet<CellCoord> { tether };
                var farthest = 0; var cycles = 0; var walked = 0;
                // Many frames: each time the run has ended, the standpoint step's next start (the order again) draws the next tile.
                for (var frame = 0; frame < 6000 && cycles < 12; frame++)
                {
                    Frames(1);
                    if (host.Tile(entity) is not { } at) continue;
                    if (tiles.Add(at)) walked++;
                    farthest = Math.Max(farthest, AiDistance(at, tether));
                    if (host.RunOf(entity) is null) { cycles++; host.OrderWander(entity, tether, dark); }
                }
                GD.Print($"ARC-AI-1313 npc-wander {(dark ? "dark" : "plain")} npc={entity} tether={tether.X},{tether.Y} cycles={cycles} tiles={tiles.Count} farthest={farthest}");
                Require(tiles.Count > 1, $"the {(dark ? "dark " : "")}wanderer left its tile (tiles {tiles.Count}, order {order})");
                Require(farthest <= 4 + 1, $"the wanderer stayed within four tiles of the tether (one more for a step in flight): {farthest}");
                Require(cycles >= 2 || dark, $"the wander ran in cycles: {cycles}");
                results.Add(new { Goal = (int)goal, Cycles = cycles, Tiles = tiles.Count, Farthest = farthest });
            }
            GD.Print("ARC-AI-1313 native-smoke npc-wander PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Runs = results }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
        await Task.CompletedTask;
    }
}
