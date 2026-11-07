using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-072 native far-click check (part of the map stage on a map with a session): the player's click route on a goal more
// than 32 tiles away, reachable over the shown chart, under both rule modes. Original: the goal is refused and the PC does not
// move (ledger LR4, LR7); Steamwoven: the goal is walked along a corridor (ARC-WORLD-042). The session's mode and the actor's
// place are restored afterwards. A scripted conversation a following companion's step would open is held (ARC-WORLD-118).
public partial class TerrainViewerApp
{
    private const int FarClickMinimum = RouteLimits.AxisDistanceMaximum + 8, FarClickSearchCells = 65536, FarClickFrames = 400;

    private async Task RunAreaFarClickSmokeAsync()
    {
        if (_areaSession is not { } session || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-WORLD-072 native-smoke far click NONE map={_readyMapName} (no session)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-072 far-click smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_FAR_CLICK_SMOKE_FAILED");
        }
        // The first cell in breadth-first order over open steps at least FarClickMinimum tiles away on the larger axis, over no exit
        // tile (a step onto one would start a map transfer).
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var start = grid.State.CommittedCell;
        var routes = new AreaRouteSource(grid.Snapshot, grid.Chart, grid.Occupancy, () => grid.Portals?.Invoke());
        var exits = new HashSet<Arcanum.Data.Traversal.AreaStepCell>(_mapTransfers?.TriggerCells ?? []);
        var seen = new HashSet<CellCoord> { start }; var queue = new Queue<CellCoord>([start]); CellCoord? goal = null;
        while (goal is null && seen.Count < FarClickSearchCells && queue.TryDequeue(out var cell))
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var next = RoutePlanner.Neighbor(cell, direction);
                if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(cell, direction) != RouteStepVerdict.Open || exits.Contains(grid.Chart.ToWorld(next))) continue;
                seen.Add(next); queue.Enqueue(next);
                if (Math.Max(Math.Abs((long)next.X - start.X), Math.Abs((long)next.Y - start.Y)) >= FarClickMinimum) { goal = next; break; }
            }
        if (goal is not { } target) { GD.Print($"ARC-WORLD-072 native-smoke far click NONE map={_readyMapName} (no reachable cell {FarClickMinimum} tiles away, searched {seen.Count})"); return; }
        var prior = session.Rules.Mode; var mapBefore = CurrentAreaMap;
        object? original = null, steamwoven = null;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            AreaSmokePlace(map, start);
            var route = WorldRoute((AreaGridSession)_worldActor.Session!) ?? throw new InvalidOperationException("AREA_ROUTE_REQUIRED");
            Require(session.SelectRules(RuleMode.Original) != GameSessionDisposition.Rejected, "Original selected");
            route.SetTarget(target, WorldTime);
            for (var i = 0; i < 4; i++) AreaSmokeFrame();
            var still = (AreaGridSession)_worldActor.Session!;
            Require(route.Status == RouteFollowStatus.Stopped && route.StopReason == "ROUTE_TARGET_OUT_OF_RANGE" && route.Corridor is null,
                $"Original refuses the far goal ({DescribeWorldRoute(route)})");
            Require(still.State.CommittedCell == start && still.State.Phase == MovementPhase.Settled, $"Original: the PC does not move (at {still.State.CommittedCell})");
            original = new { route.StopReason, Moved = false };

            Require(session.SelectRules(RuleMode.Steamwoven) != GameSessionDisposition.Rejected, "Steamwoven selected");
            route.SetTarget(target, WorldTime);
            var frames = 0;
            while (frames < FarClickFrames && route.Status == RouteFollowStatus.Following)
            {
                AreaSmokeFrame(); frames++;
                if (frames % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(CurrentAreaMap == mapBefore && !AreaTransferPending, "the walk stays on the map (no transfer)");
            var walked = (AreaGridSession)_worldActor.Session!;
            Require(route.Status == RouteFollowStatus.Arrived && walked.State.CommittedCell == target,
                $"Steamwoven walks the far goal ({DescribeWorldRoute(route)}, at {walked.State.CommittedCell}, frames {frames})");
            Require(route.Corridor is { Outcome: RouteCorridorOutcome.Found }, "Steamwoven walks it along a corridor");
            steamwoven = new { Frames = frames, Waypoints = route.Corridor!.Waypoints.Count, route.Corridor.Cells };
            AreaSmokePlace(map, start);
        }
        finally
        {
            session.SelectRules(prior);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
        Require(session.Rules.Mode == prior, "the session's mode is as it was");
        GD.Print("ARC-WORLD-072 native-smoke far click PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, From = grid.Chart.ToWorld(start), Goal = grid.Chart.ToWorld(target),
            Distance = Math.Max(Math.Abs((long)target.X - start.X), Math.Abs((long)target.Y - start.Y)), Original = original, Steamwoven = steamwoven,
            Parity = "NOT_VERIFIED (rule read in the original executable, LR4/LR7; no original-runtime run)" }));
    }
}
