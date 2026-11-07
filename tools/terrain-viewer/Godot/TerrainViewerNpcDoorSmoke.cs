using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-209 (MIG14 S3, GT49, smoke plan SM10 part) native observation (stage "npc-door", only when named, map mode with --hero, AI on, a town: --sector 1409,1314): an NPC the AI host
    // walks across a closed door the NPC can open pushes the opener goals (11 → 12), the door gets its own opening run and is open when the NPC's step enters its cell. The stage finds a closed,
    // openable door of the window and a step across it, brings a free NPC next to the door (a walk to the near cell), then orders it across; it watches the NPC's goal stack, the door's run and frame.
    private async Task RunAreaNpcDoorSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-209 npc-door smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_DOOR_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null || _areaPortals is not { } portals || _areaDoors is not { } doors)
        { GD.Print($"ARC-DEV-209 native-smoke npc-door NONE map={_readyMapName} (needs a hero, combat values and the map's doors)"); return; }
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
            if (host is null) { GD.Print("ARC-DEV-209 native-smoke npc-door NONE (no AI host)"); return; }
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
            // The closed, openable doors of the window and the steps across them: a neighbour step (world cells) near the door that the host reports as crossing exactly that door.
            var candidates = new List<(AreaPortal Door, CellCoord From, CellCoord To)>();
            foreach (var door in portals.All.Where(p => p.OpenFrame != 0 && !p.UnlockScript && portals.Frame(session, p) == 0 && portals.Decide(session, p) == AreaPortalUse.Opened && doors.GoalOf(p) is null))
                for (var dx = -2; dx <= 2; dx++)
                    for (var dy = -2; dy <= 2; dy++)
                    {
                        var from = new CellCoord(checked((int)door.Cell.X) + dx, checked((int)door.Cell.Y) + dy);
                        foreach (var direction in Enum.GetValues<NeighborDirection>())
                        {
                            var to = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(from, direction);
                            if (host.ClosedDoorBetween(from, to) == door.Entity) candidates.Add((door, from, to));
                        }
                    }
            if (candidates.Count == 0)
            { GD.Print($"ARC-DEV-209 native-smoke npc-door NONE map={_readyMapName} (no closed openable door with an open step across it among {portals.All.Count} portals)"); return; }
            var heroTile = grid.Chart.ToWorld(grid.State.CommittedCell);
            var heroCell = new CellCoord(checked((int)heroTile.X), checked((int)heroTile.Y));
            var npcs = host.Scene.Npcs.Where(n => !host.RunCompanions.Contains(n.Entity) && !host.Gone(n.Entity) && host.Tile(n.Entity) is not null).Select(n => n.Entity).ToArray();
            string? Settle(string npc, CellCoord world, int cap)
            {
                var order = host.OrderWalk(npc, world);
                if (order is not ("started" or "retargeted")) return order;
                for (var frame = 0; frame < cap; frame++)
                {
                    Frames(1);
                    if (host.Tile(npc) == world && host.RunOf(npc) is null) return null;
                }
                return "did not arrive";
            }
            var tried = 0;
            foreach (var (door, from, to) in candidates.OrderBy(c => AiExecutorRules.Distance(c.From, heroCell)).Take(6))
            {
                foreach (var npc in npcs.OrderBy(n => AiExecutorRules.Distance(host.Tile(n)!.Value, from)).Take(4))
                {
                    if (Settle(npc, from, 1500) is not null) continue;
                    // Next to the door on the near side, the door still shut: the order across it.
                    if (portals.Frame(session, door) != 0 || doors.GoalOf(door) is not null) break;
                    var order = host.OrderWalk(npc, to);
                    if (order is not ("started" or "retargeted")) continue;
                    tried++;
                    var pushed = false; var runSeen = false; var openAtEntry = true; var entered = false;
                    for (var frame = 0; frame < 600; frame++)
                    {
                        Frames(1);
                        if (host.RunOf(npc) is { } run && run.Frames.Any(f => f.Goal is GoalType.OpenDoor or GoalType.AttemptOpenDoor)) pushed = true;
                        if (doors.GoalOf(door) is not null) runSeen = true;
                        if (!entered && host.Tile(npc) == to) { entered = true; openAtEntry = portals.Frame(session, door) != 0 || doors.GoalOf(door) is not null; }
                        if (host.Tile(npc) == to && host.RunOf(npc) is null) break;
                    }
                    var arrived = host.Tile(npc) == to;
                    // GT49: the walk resumes as soon as goal 12 pops, so the NPC steps in while the door's own run still opens it; the run ends with the door open.
                    for (var frame = 0; frame < 200 && portals.Frame(session, door) == 0; frame++) Frames(1);
                    var doorOpen = portals.Frame(session, door) != 0;
                    GD.Print($"ARC-DEV-209 npc-door walk npc={npc} door={door.Entity} pushed={pushed} doorRun={runSeen} entered={entered} openAtEntry={openAtEntry} arrived={arrived} doorOpen={doorOpen}");
                    if (!pushed && !entered) continue;   // the planner went round the door: try the next pair
                    Require(pushed, $"{npc} pushed the opener goals at the closed door {door.Entity}");
                    Require(runSeen, $"the door {door.Entity} got its own opening run");
                    Require(arrived, $"{npc} walked on across the door to {to.X},{to.Y}");
                    Require(openAtEntry, $"the door {door.Entity} was open or opening when {npc} stepped across it");
                    Require(doorOpen, $"the door {door.Entity} ended open");
                    GD.Print("ARC-DEV-209 native-smoke npc-door PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Npc = npc, Door = door.Entity, Candidates = candidates.Count }));
                    return;
                }
            }
            GD.Print($"ARC-DEV-209 native-smoke npc-door NONE map={_readyMapName} (no free NPC crossed any of the first 6 doors: {tried} walks tried)");
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
