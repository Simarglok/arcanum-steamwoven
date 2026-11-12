using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-AI-1315 (VA4, VAG4) native observation (stage "please-move", only when named, map mode with --hero, AI on): two NPCs the AI host holds are placed on one tile. (1) The attack starter
    // (0x434B00) of one of them, asked to attack the hero, meets the tile-sharer finder (0x436720) before its interrupt: the NPC with the larger handle stand-in gets goal 68 (please move,
    // 0x4363E0) and no attack starts; the goal's node 1 (0x424E00) picks a clear neighbouring tile and the run walks there, so the two stand on different, neighbouring tiles. (2) They are put on one
    // tile again and the step apart (0x4364D0), which every refused attack start ends in, separates them the same way.
    private async Task RunAreaPleaseMoveSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-AI-1315 please-move smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PLEASE_MOVE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null)
        { GD.Print("ARC-AI-1315 native-smoke please-move NONE (needs a hero and combat values)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var grid = (AreaGridSession)_worldActor.Session!;
            var map = new AreaMapRuntime(_readyArea.Snapshot, grid.Snapshot, grid.Chart);
            ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
            AreaSmokePlace(map, grid.State.CommittedCell);
            var host = AreaAiHostNow();
            if (host is null) { GD.Print("ARC-AI-1315 native-smoke please-move NONE (no AI host)"); return; }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 32_000_000L;
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
            var pool = host.Scene.Npcs.Where(n => !host.RunCompanions.Contains(n.Entity) && !host.Gone(n.Entity) && !host.DeadCritter(n.Entity) && host.Tile(n.Entity) is not null
                && host.RunOf(n.Entity) is null).ToArray();
            CalmNpcsForSmoke(session, pool.Select(n => n.Entity));
            bool HasFreeNeighbour(string id) => host.Tile(id) is { } at && Enumerable.Range(0, 8).Any(r => host.StepClear(id, OriginalPathRequest.Neighbor(at, r)));
            // Two NPCs close together: the first with a clear neighbouring tile, the second the nearest other one.
            AreaAiNpc? first = null, second = null;
            foreach (var candidate in pool.Where(n => HasFreeNeighbour(n.Entity)))
            {
                var other = pool.Where(n => n.Entity != candidate.Entity).OrderBy(n => AiDistance(host.Tile(n.Entity)!.Value, host.Tile(candidate.Entity)!.Value)).FirstOrDefault();
                if (other is null) continue;
                (first, second) = (candidate, other);
                break;
            }
            if (first is null || second is null)
            { GD.Print($"ARC-AI-1315 native-smoke please-move NONE map={_readyMapName} (no two idle NPCs with a clear neighbouring tile among {pool.Length} of the window)"); return; }

            void StackOnFirst()
            {
                var tile = host.Tile(first.Entity)!.Value;
                host.Movers.Restore(second.Entity, second.Row.Key, second.Origin, tile, host.Movers.FacingOf(second.Entity, NeighborDirection.XPlus));
                Require(host.Tile(second.Entity) == tile, $"{second.Entity} stands on {first.Entity}'s tile {tile.X},{tile.Y}");
            }
            // Which of the two the rules ask to move: the larger handle stand-in, whoever attacks; the other stays.
            string Mover()
            {
                var order = host.TileCritters(first.Entity).Where(c => c.Id == first.Entity || c.Id == second.Entity).ToArray();
                return order.MaxBy(c => c.Order).Id;
            }
            string Stayer(string mover) => mover == first.Entity ? second.Entity : first.Entity;
            // The goal's run until it ends; the tile the mover stood on while it ran is where the goal put it (the NPC's own routine walks it home afterwards, which is no part of the goal).
            (int Frames, string Where, CellCoord? Landed) Separate(string mover, string stay)
            {
                var frames = 0; CellCoord? landed = null;
                for (; frames < 1500 && host.RunOf(mover) is { Bottom.Goal: GoalType.PleaseMove }; frames++)
                {
                    landed = host.Tile(mover);
                    Frames(1);
                }
                return (frames, $"{mover} landed {landed?.X},{landed?.Y}, {stay} at {host.Tile(stay)?.X},{host.Tile(stay)?.Y}", landed);
            }
            // Idle again: the walk home the routine gave the moved NPC has ended.
            void Settle()
            {
                for (var frame = 0; frame < 3000 && (host.RunOf(first.Entity) is not null || host.RunOf(second.Entity) is not null); frame++) Frames(1);
                Require(host.RunOf(first.Entity) is null && host.RunOf(second.Entity) is null, "both settle to no run");
            }

            // (1) The attack starter meets the finder.
            StackOnFirst();
            var shared = host.Tile(first.Entity)!.Value;
            var mover = Mover();
            var stayer = Stayer(mover);
            Require(host.RunOf(first.Entity) is null && host.RunOf(second.Entity) is null, "both start with no run");
            host.PerformAttack(first.Entity, AiSchedulerRules.Pc);
            var started = host.RunOf(mover);
            GD.Print($"ARC-AI-1315 please-move finder attacker={first.Entity} mover={mover} run={started?.Bottom.Goal} away={started?.Bottom.Target} trace={host.TraceOf(first.Entity)?.Perform}");
            Require(started is { Bottom.Goal: GoalType.PleaseMove } && started.Bottom.Target == stayer, $"{mover} got goal 68 away from {stayer}: {started?.Bottom.Goal}");
            Require(host.RunOf(stayer) is null, $"{stayer} started nothing (no attack from the shared tile): {host.RunOf(stayer)?.Bottom.Goal}");
            var (frames, where, landed) = Separate(mover, stayer);
            Require(landed is { } l1 && l1 != shared, $"{mover} left the shared tile after {frames} frames: {where}");
            Require(host.Tile(stayer) == shared, $"{stayer} stayed on {shared.X},{shared.Y}: {where}");
            Require(AiDistance(landed!.Value, shared) == 1, $"{mover} stepped to a neighbouring tile: {where}");
            var finderFrames = frames; var finderWhere = where;

            // (2) The step apart, which a refused attack start ends in.
            Settle();
            StackOnFirst();
            mover = Mover();
            stayer = Stayer(mover);
            Require(host.RunOf(first.Entity) is null && host.RunOf(second.Entity) is null, "both are idle again");
            var asked = AiTileSharerRules.StepApart(host, first.Entity);
            Require(asked == 1 && host.RunOf(mover) is { Bottom.Goal: GoalType.PleaseMove }, $"the step apart asked {asked} critter(s); {mover}: {host.RunOf(mover)?.Bottom.Goal}");
            (frames, where, landed) = Separate(mover, stayer);
            Require(landed is { } l2 && AiDistance(l2, host.Tile(stayer)!.Value) == 1, $"the step apart separated them after {frames} frames: {where}");
            GD.Print("ARC-AI-1315 native-smoke please-move PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, First = first.Entity, Second = second.Entity, Mover = mover, FinderFrames = finderFrames, FinderWhere = finderWhere, StepApartFrames = frames, StepApartWhere = where }));
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
