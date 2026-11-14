using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-042 native observation (the "walk" map-smoke stage, run only when named): from the new-game start on the main
// map, the walk to the middle of the Shrouded Hills town sector (1409, 1314) through the generated wilderness. The route is
// planned over the map's windows in the background (AreaWindowRoute); the walk is the production one: the route follower,
// given the farthest route cell of the shown window each time it arrives or the window moves (its corridor carries it
// beyond its own 32-cell reach), and the window streaming as the actor goes. A smoke frame is one second of walk; while a
// window loads, one frame a second of wall time, so the actor walks at its own speed meanwhile, as in play. No seam: the
// follower never stops short (a denied step, no route or a partial corridor fails the smoke at once), the actor's cell
// changes by no more than a second's run a frame, every window move publishes it on the cell it stood on, inside the new
// core and drawn, and no move is given up. Named on another map, the stage fails (it would prove nothing).
public partial class TerrainViewerApp
{
    private const string WalkMap = "Arcanum1-024-fixed";
    private CancellationTokenSource? _walkPlanCancellation;
    private static readonly AreaStepCell WalkGoal = new(1409 * 64 + 32, 1314 * 64 + 32);
    // A second's walk is at most 6 steps: the run takes 9 steps in 1.578 s (ARC-WORLD-020 native smoke).
    // ARC-DEV-395: unless ARC_SMOKE_ALL=1 (`./arcanum smoke ... --all`) the stage plans and walks only to WalkSampleGoal, a cell of the full route about 10 window moves from the start (the full route has 71), which
    // leaves out the route's tail and the planner on the whole 88-sector route (225 s of the 525 s).
    private const int WalkSampleMinMoves = 8;
    private static readonly AreaStepCell WalkSampleGoal = new(92613, 82756); // route cell 700 of 3,106 on the full walk, about 10 of its 71 window moves from the start (--all logs which route cell it is)
    private const int WalkFrameLimit = 20000, WalkStallFrames = 300, WalkStepsPerFrame = 6, ProgressAhead = 24, ProgressNear = 3;

    private async Task RunAreaMapWalkSmokeAsync()
    {
        static void Require(bool value, string code)
        {
            if (value) return;
            var space = code.IndexOf(' ');
            if (space > 0) GD.Print("ARC-WORLD-042 smoke detail " + code);
            throw new InvalidOperationException("AREA_WALK_SMOKE_" + (space > 0 ? code[..space] : code));
        }
        Require(_mapWindowCentre is not null && SameMapName(_readyMapName, WalkMap), $"NOT_MAIN_MAP {_readyMapName}");
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest(); _mapStreamSmokeActive = true;
            var carried = _areaSession;
            var all = SmokeAll;
            var goal = all ? WalkGoal : WalkSampleGoal;
            var start = (AreaGridSession)_worldActor.Session!;
            AreaSmokePlace(new AreaMapRuntime(_readyArea!.Snapshot, start.Snapshot, start.Chart), start.State.CommittedCell);
            var placed = (AreaGridSession)_worldActor.Session!;
            var from = placed.Chart.ToWorld(placed.State.CommittedCell);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var arguments = _arguments!;
            // ARC-WORLD-060: the route reads the steps the PC walks by (removed objects free their tiles).
            var overlay = AreaPcOccupancy();
            // The plan runs in the background for minutes; a viewer that exits meanwhile cancels it (_ExitTree).
            using var cancel = new CancellationTokenSource();
            _walkPlanCancellation = cancel;
            AreaWindowRouteResult route;
            try { route = await Task.Run(() => AreaMapWalk.Plan(arguments, from, goal, cancel.Token, overlay), cancel.Token); }
            finally { _walkPlanCancellation = null; }
            var planSeconds = clock.Elapsed.TotalSeconds;
            GD.Print($"ARC-WORLD-042 walk route {route.Status} {route.Detail} cells={route.Cells.Count} windows={route.WindowLoads} expansions={route.Expansions} seconds={planSeconds:F0}");
            Require(route.Status == AreaWindowRouteStatus.Found, "NO_ROUTE " + route.Detail);
            var path = route.Cells;
            // The sample goal is a cell of the full route; the full run says whether it still is (the map data or the planner changed otherwise).
            if (all) GD.Print($"ARC-DEV-395 walk sample goal {WalkSampleGoal} is route cell {path.Select((c, i) => (c, i)).Where(x => x.c == WalkSampleGoal).Select(x => x.i).DefaultIfEmpty(-1).First()} of {path.Count}");
            var index = new Dictionary<AreaStepCell, int>(path.Count);
            for (var i = 0; i < path.Count; i++) index.TryAdd(path[i], i);
            _mapStreamLog.Clear();
            var givenUp = _mapStreamGivenUp;
            var managedBefore = GC.GetTotalMemory(true);
            var reached = 0; AreaGridSession? steered = null; var retargets = 0; var lastProgress = 0; var frames = 0;
            var previous = from; var drawnMoves = 0; var minVisible = int.MaxValue; var maxLoad = 0L; var maxSteps = 0L; var maxFrameMs = 0.0;
            // Each frame: on a new window or an arrived walk, the follower heads for the farthest route cell of the window that
            // the route reaches without leaving it.
            void Steer()
            {
                if (_worldActor.Session is not AreaGridSession walking || WorldRoute(walking) is not { } follower) return;
                var here = walking.Chart.ToWorld(walking.State.CommittedCell);
                if (follower.Status == RouteFollowStatus.Arrived && follower.Target is { } arrived && index.TryGetValue(walking.Chart.ToWorld(arrived), out var at) && at > reached)
                { reached = at; lastProgress = frames; }
                // The follower's own path may leave the route's cells (another path as short; since ARC-WORLD-073 the original's
                // search breaks ties its own way): progress is the route cell it stands on, the route cell it arrived at, or a later
                // route cell within ProgressNear cells of it (among the next ProgressAhead).
                if (index.TryGetValue(here, out var on) && on > reached) { reached = on; lastProgress = frames; }
                for (var i = Math.Min(path.Count - 1, reached + ProgressAhead); i > reached; i--)
                    if (Math.Max(Math.Abs(path[i].X - here.X), Math.Abs(path[i].Y - here.Y)) <= ProgressNear) { reached = i; lastProgress = frames; break; }
                Require(follower.Status != RouteFollowStatus.Stopped, $"FOLLOWER_STOPPED {follower.StopReason} at={here} target={follower.Target} hereIndex={(index.TryGetValue(here, out var hereAt) ? hereAt : -1)} reached={reached} targetIndex={(follower.Target is { } stopTarget && index.TryGetValue(walking.Chart.ToWorld(stopTarget), out var targetAt) ? targetAt : -1)} {DescribeWorldRoute(follower)} planMethod={follower.LastPlan?.Method} planFrom={follower.LastPlan?.From} planSteps={follower.LastPlan?.Steps.Count} corridor={follower.Corridor is not null}");
                if (here == goal || ReferenceEquals(walking, steered) && follower.Status == RouteFollowStatus.Following) return;
                bool Shown(int i) => walking.Chart.Contains(walking.Chart.ToChart(path[i]));
                // The route's first cell inside the window from the progress on, then as far as the route stays inside it.
                var first = reached;
                while (first < path.Count - 1 && !Shown(first)) first++;
                var next = first;
                while (next + 1 < path.Count && Shown(next + 1)) next++;
                steered = walking;
                if (!Shown(next) || path[next] == here) return;
                follower.SetTarget(walking.Chart.ToChart(path[next]), WorldTime); retargets++;
            }
            Steer();
            Require(WorldRoute((AreaGridSession)_worldActor.Session!)?.Status == RouteFollowStatus.Following, "ROUTE_NOT_SET");
            while (frames < WalkFrameLimit)
            {
                // The frame's main-thread cost: the walk, its route plans (a corridor over the window included) and the steering.
                var frameClock = System.Diagnostics.Stopwatch.StartNew();
                AreaSmokeFrame(); frames++; Steer();
                maxFrameMs = Math.Max(maxFrameMs, frameClock.Elapsed.TotalMilliseconds);
                var now = (AreaGridSession)_worldActor.Session!;
                var cell = now.Chart.ToWorld(now.State.CommittedCell);
                // ARC-DEV-150: the session holds the committed cell at the end of every frame, across the window moves too.
                if (_areaSession?.QueryActor() is { } held)
                    Require(held.Cell.X == cell.X && held.Cell.Y == cell.Y, $"HERO_LOCATION frame={frames} session={held.Cell} grid={cell}");
                var steps = Math.Max(Math.Abs(cell.X - previous.X), Math.Abs(cell.Y - previous.Y));
                Require(steps <= WalkStepsPerFrame, $"SEAM frame={frames} from={previous} to={cell}");
                previous = cell; maxSteps = Math.Max(maxSteps, steps);
                var moves = _mapStreamLog.Count;
                if (MapStreamInFlight) await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                else if (frames % 4 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(_readyMapName is not null && _mapStreamFailedAt is null, "WINDOW_NOT_LOADED " + _mapStreamStatus);
                if (_mapStreamLog.Count > moves)
                {
                    // A window moved while no frame ran: it stands the actor on the cell it stood on, is drawn and holds it in
                    // its core.
                    var (_, to, actor, _, _, loadMs) = _mapStreamLog[^1];
                    var shown = (AreaGridSession)_worldActor.Session!;
                    Require(actor == cell && shown.Chart.ToWorld(shown.State.CommittedCell) == cell, $"SEAM_AT_MOVE {actor} {cell}");
                    Require(Math.Abs(to.X - (cell.X >> 6)) <= 1 && Math.Abs(to.Y - (cell.Y >> 6)) <= 1, $"ACTOR_OUTSIDE_CORE {to} {cell}");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Require(_canvas!.AreaVisibleCount > 0, $"NOTHING_DRAWN move={_mapStreamLog.Count}");
                    drawnMoves++; minVisible = Math.Min(minVisible, _canvas.AreaVisibleCount); maxLoad = Math.Max(maxLoad, loadMs);
                }
                if (!MapStreamInFlight && now.State.Phase == MovementPhase.Settled && cell == goal && ReferenceEquals(now, _worldActor.Session)) break;
                Require(frames - lastProgress < WalkStallFrames, $"STALLED frame={frames} at={cell} reached={reached}/{path.Count - 1} route={_worldRouteDiagnostic} status={_mapStreamStatus}");
            }
            var end = (AreaGridSession)_worldActor.Session!;
            var at = end.Chart.ToWorld(end.State.CommittedCell);
            Require(at == goal, $"GOAL_NOT_REACHED frames={frames} at={at} reached={reached}/{path.Count - 1}");
            foreach (var (fromCentre, to, _, _, session, _) in _mapStreamLog)
            {
                Require(to != fromCentre, "CENTRE");
                Require(carried is null || ReferenceEquals(session, carried), "SESSION_NOT_CARRIED");
            }
            Require(carried is null || ReferenceEquals(_areaSession, carried), "SESSION_NOT_CARRIED");
            Require(_mapStreamLog.Count >= (all ? 10 : WalkSampleMinMoves) && drawnMoves == _mapStreamLog.Count, $"MOVES {_mapStreamLog.Count}");
            Require(_mapStreamGivenUp == givenUp, $"MOVE_GIVEN_UP {_mapStreamGivenUp - givenUp}");
            var managedAfter = GC.GetTotalMemory(true);
            GD.Print("ARC-WORLD-042 native-smoke walk PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, From = from, To = goal, Sampled = !all, RouteCells = path.Count, RouteSectors = path.Select(c => (c.X >> 6, c.Y >> 6)).Distinct().Count(),
                PlanWindows = route.WindowLoads, PlanExpansions = route.Expansions, PlanSeconds = Math.Round(planSeconds),
                Frames = frames, MaxStepsPerFrame = maxSteps, MaxFrameMs = Math.Round(maxFrameMs, 1), Retargets = retargets, Moves = _mapStreamLog.Count, MaxLoadMs = maxLoad, MinVisible = minVisible,
                Centre = new { _mapWindowCentre!.Value.X, _mapWindowCentre.Value.Y }, Session = _areaSession?.Version.Revision, Carried = carried is not null,
                ManagedBefore = managedBefore, ManagedAfter = managedAfter }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime); _mapStreamSmokeActive = false;
        }
    }
}
