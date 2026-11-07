using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const int RouteBarrelStarts = 8, RouteBarrelMaxBarrels = 3, RouteBarrelSearchCells = 20000, RouteBarrelFrames = 4000, RouteBarrelSlack = 2, RouteBarrelSettleFrames = 100;

    // ARC-DEV-247 (B43, B44, smoke plan SM17) native observation (stage "route-barrel", only when named, map mode with --hero; the plan's map is "crash site cave"): the hero's click on a
    // barrel (a container whose shown name has "barrel" in it) walks to a cell next to it and opens the search, as the original does from any start. From up to eight start cells of the
    // map (reachable over Open steps, spread over the reachable ground, a different set at each barrel) the stage places the hero, hovers the barrel, presses it and watches every
    // committed cell of the walk until the loot window opens: each cell is a cell the map admits and each step between two committed cells is Open over the same step source the route
    // and the session use (a barrel route the step source rejects, or an approach cell it does not reach, fails here; the step source is the evidence the route and the session use, so a wrongly Open step is not caught), the walk ends within the container reach of the barrel, and its length is at most the shortest
    // Open route to any cell within that reach plus two steps. B44 (the cave's exit has room to walk out beyond the walls) is a question of the original's map and is not decided here:
    // the stage prints the ground reachable from the arrival cell without entering an exit tile (its count and extent), the state to compare with the original.
    private async Task RunAreaRouteBarrelSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-247 route-barrel smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_ROUTE_BARREL_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _inventoryHost is null || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-DEV-247 native-smoke route-barrel NONE map={_readyMapName} (needs a hero, an inventory host and a grid session)"); return; }
        for (var i = 0; i < 1200 && (_hoverNames is null || _inventoryHost?.Map(CurrentAreaMap) is null); i++)
        { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
        Require(_hoverNames?.Status == AreaNamesStatus.Ready, "the object names loaded: " + _hoverNames?.Status);
        if (InventoryOpen) EndInventory();
        var inventoryMap = _inventoryHost!.Map(CurrentAreaMap);
        Require(inventoryMap is not null, "the map has an inventory");
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var barrels = area.Snapshot.Objects
            .Where(row => row.Scope == AreaScope.Core && row.Type == ContainerType && row.WorldX is not null && row.WorldY is not null && commands.ContainsKey(row.Key) && inventoryMap!.TryGetEntity(row.Key, out _))
            .Select(row => (Row: row, Name: AreaHoverIndex.Text(row, _hoverNames!.Catalog) ?? ""))
            .Where(b => b.Name.Contains("barrel", StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Row.Key.ToString(), StringComparer.Ordinal).Take(RouteBarrelMaxBarrels).ToArray();
        if (barrels.Length == 0)
        {
            var named = string.Join(",", area.Snapshot.Objects.Where(r => r.Type == ContainerType).Select(r => AreaHoverIndex.Text(r, _hoverNames!.Catalog)).Distinct().Take(12));
            GD.Print($"ARC-DEV-247 native-smoke route-barrel NONE map={_readyMapName} (no barrel among the containers: {named})");
            return;
        }

        var routes = new AreaRouteSource(grid.Snapshot, grid.Chart, grid.Occupancy, () => grid.Portals?.Invoke());
        var exits = new HashSet<Arcanum.Data.Traversal.AreaStepCell>(_mapTransfers?.TriggerCells ?? []);
        var arrival = grid.State.CommittedCell;
        var directions = Enum.GetValues<NeighborDirection>();
        bool IsExit(CellCoord cell) => exits.Contains(grid.Chart.ToWorld(cell));

        // Breadth-first depth of every cell reachable over Open steps from a cell, never entering an exit tile (a step onto one starts a transfer).
        Dictionary<CellCoord, int> Reach(CellCoord from, int limit, IRouteStepSource? source = null)
        {
            source ??= routes;
            var depth = new Dictionary<CellCoord, int> { [from] = 0 }; var queue = new Queue<CellCoord>([from]);
            while (depth.Count < limit && queue.TryDequeue(out var cell))
                foreach (var direction in directions)
                {
                    var next = RoutePlanner.Neighbor(cell, direction);
                    if (depth.ContainsKey(next) || !source.Contains(next) || source.Assess(cell, direction) != RouteStepVerdict.Open || IsExit(next)) continue;
                    depth[next] = depth[cell] + 1; queue.Enqueue(next);
                }
            return depth;
        }
        NeighborDirection? DirectionOf(CellCoord from, CellCoord to) => directions.Cast<NeighborDirection?>().FirstOrDefault(d => RoutePlanner.Neighbor(from, d!.Value) == to);
        static int Chebyshev(CellCoord a, CellCoord b) => (int)Math.Max(Math.Abs((long)a.X - b.X), Math.Abs((long)a.Y - b.Y));

        var ground = Reach(arrival, RouteBarrelSearchCells);
        Require(ground.Count > 40, $"the arrival cell {arrival} has ground to walk on ({ground.Count} cells)");
        var xs = ground.Keys.Select(c => c.X); var ys = ground.Keys.Select(c => c.Y);
        GD.Print($"ARC-DEV-247 route-barrel ground arrival={grid.Chart.ToWorld(arrival)} cells={ground.Count} x={xs.Min()}..{xs.Max()} y={ys.Min()}..{ys.Max()} exits={exits.Count} (B44: compare with the original's map)");

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var results = new List<object>(); var walks = 0; var longest = 0;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            foreach (var (row, name) in barrels)
            {
                var center = map.Chart.ToChart(new(row.WorldX!.Value, row.WorldY!.Value));
                var target = _inventoryHost.Target(session, CurrentAreaMap, row.Key);
                Require(target is { Kind: AreaInventoryTargetKind.Container }, $"{name} at {center} is a container target");
                // The cells next to the barrel the hero may stand on, and the shortest Open route (in steps) from each start to the nearest of them.
                var nearBarrel = new HashSet<CellCoord>();
                for (var dy = -AreaInventoryHost.Reach; dy <= AreaInventoryHost.Reach; dy++)
                for (var dx = -AreaInventoryHost.Reach; dx <= AreaInventoryHost.Reach; dx++)
                    if ((dx != 0 || dy != 0) && new CellCoord(center.X + dx, center.Y + dy) is var cell && routes.Contains(cell) && grid.Snapshot.IsAdmitted(grid.Chart.ToWorld(cell))) nearBarrel.Add(cell);
                var fromBarrel = Reach(center, RouteBarrelSearchCells);
                var starts = ground.Where(kv => kv.Value >= 3 && kv.Key != center && !nearBarrel.Contains(kv.Key) && fromBarrel.ContainsKey(kv.Key))
                    .OrderBy(kv => kv.Value).ThenBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).Select(kv => kv.Key).ToArray();
                Require(starts.Length > 0, $"{name} at {center}: a start cell reaches it over Open steps");
                var picked = Enumerable.Range(0, Math.Min(RouteBarrelStarts, starts.Length)).Select(i => starts[(int)((long)i * starts.Length / Math.Min(RouteBarrelStarts, starts.Length))]).Distinct().ToArray();
                foreach (var start in picked)
                {
                    if (InventoryOpen) EndInventory();
                    AreaSmokePlace(map, start);
                    // The placement builds a new session, whose occupancy and doors are the ones the walk meets.
                    var live = RouteSourceFor((AreaGridSession)_worldActor.Session!);
                    Require(((AreaGridSession)_worldActor.Session!).State.CommittedCell == start, $"{name}: the placement settled on {start} (at {((AreaGridSession)_worldActor.Session!).State.CommittedCell})");
                    var fromStartDepth = Reach(start, RouteBarrelSearchCells, live);
                    var best = nearBarrel.Where(fromStartDepth.ContainsKey).Select(c => fromStartDepth[c]).DefaultIfEmpty(-1).Min();
                    Require(best >= 0, $"{name} at {center}: a cell next to it is reachable from {start} ({nearBarrel.Count} cells next to it)");
                    var point = await HoverPointAsync(this, commands[row.Key]);
                    Require(point is not null, $"{name} at {center}: visible from {start}");
                    HoverAtForSelfTest(point!.Value);
                    Require(HoverActionForSelfTest is AreaHoverAction.Loot or AreaHoverAction.WalkToLoot, $"hover on {name} from {start}: {HoverActionForSelfTest}");
                    var trail = new List<CellCoord> { start }; var verdicts = new List<RouteStepVerdict>();
                    // A frame is a fiftieth of a second, so that a committed cell is sampled before the next one is entered (the stock frame, a tenth, spans two cells).
                    _areaSmokeTime *= 50;
                    _worldTestTime = SimulationTime.At(_areaSmokeTime, 50);
                    _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point!.Value, GlobalPosition = point.Value });
                    var frames = 0; var still = 0;
                    for (; frames < RouteBarrelFrames && !InventoryOpen && still < 100; frames++)
                    {
                        _areaSmokeTime++;
                        TickAreaClock(_talkSmokeHostMs += 20);
                        WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .02, true, SimulationTime.At(_areaSmokeTime, 50));
                        var at = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                        if (at != trail[^1])
                        {
                            // The step is assessed in the frame that committed it, over the occupancy of that frame (a creature that moves on is not the route's fault later).
                            if (DirectionOf(trail[^1], at) is { } taken) verdicts.Add(live.Assess(trail[^1], taken));
                            else verdicts.Add(RouteStepVerdict.Unsupported);
                            trail.Add(at); still = 0;
                        }
                        else still++;
                    }
                    // The use goal opens the window when the hero's step toward the barrel starts; the step's cell commits a few frames later.
                    var opened = InventoryOpen;
                    for (var settle = 0; settle < RouteBarrelSettleFrames; settle++)
                    {
                        _areaSmokeTime++;
                        TickAreaClock(_talkSmokeHostMs += 20);
                        WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .02, true, SimulationTime.At(_areaSmokeTime, 50));
                        var at = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                        if (at == trail[^1]) continue;
                        verdicts.Add(DirectionOf(trail[^1], at) is { } late ? live.Assess(trail[^1], late) : RouteStepVerdict.Unsupported);
                        trail.Add(at);
                    }
                    var end = trail[^1];
                    GD.Print($"ARC-DEV-247 route-barrel walk barrel={name} at={center} from={start} end={end} steps={trail.Count - 1} shortest={best} frames={frames} opened={InventoryOpen} cells={string.Join(">", trail.Select(c => $"{c.X},{c.Y}"))}");
                    // Every committed cell is admitted; a step between two committed cells is Open over the route's own step source.
                    for (var i = 0; i < trail.Count; i++)
                    {
                        Require(routes.Contains(trail[i]) && grid.Snapshot.IsAdmitted(grid.Chart.ToWorld(trail[i])) && !IsExit(trail[i]), $"{name} from {start}: the walk's cell {trail[i]} (step {i}) is a cell the map admits and no exit tile");
                        if (i == 0) continue;
                        Require(DirectionOf(trail[i - 1], trail[i]) is not null, $"{name} from {start}: committed cells {trail[i - 1]} and {trail[i]} are neighbours");
                        Require(verdicts[i - 1] == RouteStepVerdict.Open, $"{name} from {start}: the step {trail[i - 1]} -> {trail[i]} is {verdicts[i - 1]}, not Open (step {i} of the walk)");
                    }
                    Require(opened && InventoryOpen && _inventoryPanel!.Holder == target!.Entity, $"{name} from {start}: the search opened after {frames} frames ({_inventoryStatus}), the hero ended at {end}");
                    Require(Chebyshev(end, center) <= AreaInventoryHost.Reach, $"{name} from {start}: the walk ended {Chebyshev(end, center)} cells from the barrel (reach {AreaInventoryHost.Reach})");
                    var steps = trail.Count - 1;
                    Require(steps <= best + RouteBarrelSlack, $"{name} from {start}: the walk took {steps} steps, the shortest Open route is {best} (slack {RouteBarrelSlack})");
                    walks++; longest = Math.Max(longest, steps);
                    results.Add(new { Barrel = name, At = center.ToString(), From = start.ToString(), Steps = steps, Shortest = best, Frames = frames });
                    EndInventory();
                }
            }
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            _areaSmokeTime = (_areaSmokeTime + 49) / 50; // the stage counted fiftieths; the stock frames count tenths
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
        Require(walks >= barrels.Length, $"every barrel was walked to ({walks} walks, {barrels.Length} barrels)");
        GD.Print("ARC-DEV-247 native-smoke route-barrel PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, Barrels = barrels.Length, Walks = walks, Longest = longest, Ground = ground.Count, B44 = "NOT_VERIFIED (the cave exit's room is a question of the original's map)", Runs = results.Take(24) }));
    }
}
