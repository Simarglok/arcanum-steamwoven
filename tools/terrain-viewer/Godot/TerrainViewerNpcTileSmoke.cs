using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const int AgentExitTileScript = 1724, AgentName = 6627, AgentStoryFlag = 2323;

    // ARC-DEV-206 (B25, GR12, smoke plan SM28) native observation (stage "npc-tile", only when named, map mode with --hero, AI on, the altar's window: --sector 1452,1291): the original runs a
    // tile's script for every NPC that moves onto it, so an NPC the AI host walks onto a tile of script 1724 (the Molochean agent's exit: stop attacking, then destroy the triggerer) is destroyed
    // there. The stage orders the walk of the nearest living NPC that is no follower to such a tile and watches the host's location notice reach the tile script (ARC-DEV-206 npc-tile) and the
    // session record the destroy. Script 1724 reads: global flag 2323 is set (the story step the agent comes after), and the triggerer is named 6627 (the agent), then destroy; anything else
    // returns at once. The stage therefore sets the flag and picks the NPC named 6627 of the window. The agent's own heartbeat (script 1717) is not modelled yet (set day standpoint, get
    // location, run to), so the walk is ordered here.
    private async Task RunAreaNpcTileSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-206 npc-tile smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_TILE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null || _mapTransfers is not { } transfers)
        { GD.Print($"ARC-DEV-206 native-smoke npc-tile NONE map={_readyMapName} (needs a hero, combat values and map transfers)"); return; }
        var exits = transfers.ScriptTiles.Where(c => transfers.TileScript(c)?.Script == AgentExitTileScript).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        if (exits.Length == 0)
        { GD.Print($"ARC-DEV-206 native-smoke npc-tile NONE map={_readyMapName} (no tile of script {AgentExitTileScript} among {transfers.ScriptTiles.Count} script tiles)"); return; }
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
            if (host is null) { GD.Print("ARC-DEV-206 native-smoke npc-tile NONE (no AI host)"); return; }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 31_000_000L;
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
            // The agent: the living NPC named 6627 that is no follower, with the exit tile it is nearest to.
            var scripts = UseScriptHost(_readyArea.Snapshot);
            var agents = scripts is null ? [] : host.Scene.Npcs.Where(n => !host.RunCompanions.Contains(n.Entity) && !host.Gone(n.Entity) && host.Tile(n.Entity) is not null
                && scripts.NameOf(new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, n.Entity)) == AgentName).ToArray();
            if (agents.Length == 0)
            { GD.Print($"ARC-DEV-206 native-smoke npc-tile NONE map={_readyMapName} (no NPC named {AgentName} among {host.Scene.Npcs.Count} NPCs of the window)"); return; }
            var entity = agents.OrderBy(n => exits.Min(t => AiDistance(host.Tile(n.Entity)!.Value, new(checked((int)t.X), checked((int)t.Y))))).First().Entity;
            Require(exits.Length >= 2, $"two tiles of script {AgentExitTileScript} in the window (found {exits.Length})");
            // The exit tile nearest the NPC that is not the one it stands on: the walk's first steps enter script tiles.
            Arcanum.Data.Traversal.AreaStepCell Nearest()
            {
                var at = host.Tile(entity)!.Value;
                return exits.Where(t => !(t.X == at.X && t.Y == at.Y)).OrderBy(t => AiDistance(at, new(checked((int)t.X), checked((int)t.Y)))).First();
            }
            // One leg: the NPC walks to the nearest other exit tile until the tile script has run for it (or it is gone); the runs of the leg.
            (string Order, int Frames, (string Npc, Arcanum.Data.Traversal.AreaStepCell Cell, AreaScriptedUseOutcome Outcome, string? Detail)[] Runs) Leg()
            {
                _npcTileRuns.Clear();
                var target = Nearest();
                var order = host.OrderWalk(entity, new CellCoord(checked((int)target.X), checked((int)target.Y)));
                var frames = 0;
                if (order is "started" or "retargeted")
                    for (; frames < 3000 && !_npcTileRuns.Any(r => r.Npc == entity) && !host.Gone(entity); frames++) Frames(1);
                return (order, frames, [.. _npcTileRuns.Where(r => r.Npc == entity)]);
            }
            string Show(IEnumerable<(string Npc, Arcanum.Data.Traversal.AreaStepCell Cell, AreaScriptedUseOutcome Outcome, string? Detail)> runs) =>
                string.Join(" | ", runs.Select(r => $"{r.Cell.X},{r.Cell.Y} {r.Outcome} {r.Detail}"));
            var start = host.Tile(entity)!.Value;
            // Control: the story flag is not set, so script 1724 returns at its first line and the NPC stays.
            Require(session.Flags.Read(new(new(Guid.NewGuid()), session.Flags.Version, new(AgentStoryFlag))).Value != true, $"the story flag {AgentStoryFlag} starts clear");
            var control = Leg();
            GD.Print($"ARC-DEV-206 npc-tile control npc={entity} from={start.X},{start.Y} order={control.Order} frames={control.Frames} gone={host.Gone(entity)} runs={Show(control.Runs)}");
            Require(control.Order == "started", $"the walk of {entity} from {start.X},{start.Y} started: {control.Order}");
            Require(control.Runs.Length > 0, $"the exit tile's script ran for {entity} when the walk entered a tile of script {AgentExitTileScript} (frames {control.Frames}, at {host.Tile(entity)?.X},{host.Tile(entity)?.Y})");
            Require(!host.Gone(entity), $"with the story flag clear the script did not destroy {entity} ({Show(control.Runs)})");
            // The control walk goes on to its tile first.
            for (var settle = 0; settle < 1500 && host.RunOf(entity) is not null; settle++) Frames(1);
            // The flag set: the same walk onto another exit tile destroys the agent.
            var set = session.Flags.Write(new(new(Guid.NewGuid()), session.Flags.Version, new(AgentStoryFlag), true));
            Require(session.Flags.Read(new(new(Guid.NewGuid()), session.Flags.Version, new(AgentStoryFlag))).Value == true, $"the story flag {AgentStoryFlag} is set ({set})");
            var destroyed = Leg();
            Frames(10);
            GD.Print($"ARC-DEV-206 npc-tile destroy npc={entity} order={destroyed.Order} frames={destroyed.Frames} gone={host.Gone(entity)} runs={Show(destroyed.Runs)}");
            Require(destroyed.Order is "started" or "retargeted", $"the second walk of {entity}: {destroyed.Order}");
            Require(destroyed.Runs.Length > 0 && host.Gone(entity), $"the script destroyed {entity} at the exit tile (runs: {Show(destroyed.Runs)}; gone {host.Gone(entity)})");
            GD.Print("ARC-DEV-206 native-smoke npc-tile PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Npc = entity, From = new { start.X, start.Y }, ControlRuns = control.Runs.Length, DestroyRuns = destroyed.Runs.Length, Frames = control.Frames + destroyed.Frames }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
        await Task.CompletedTask;
    }

    private static int AiDistance(CellCoord a, CellCoord b) => Arcanum.Simulation.Ai.AiExecutorRules.Distance(a, b);
}
