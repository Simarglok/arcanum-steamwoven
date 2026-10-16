using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaTransitionOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public async Task WalkingToTheCaveEntranceUsingItAndUsingTheExitReturnsToTheAuthoredCell()
    {
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE")))
            Assert.Inconclusive("Opt-in authenticated AREA gate with ARC_CAVE_MOBILE; not a synthetic PASS.");
        string Required(string role) => Environment.GetEnvironmentVariable(role) is { Length: > 0 } value ? value
            : throw new AssertFailedException("Requested original AREA gate is missing an input role: " + role);
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Required("ARC_AREA_MODULE"),
            "--resource", Required("ARC_AREA_RESOURCE2"), "--actor-resource", Required("ARC_AREA_RESOURCE1"),
            "--area-mobile", Required("ARC_AREA_MOBILE"), "--prototypes", Required("ARC_AREA_PROTOTYPES"),
            "--area-effects", Required("ARC_AREA_EFFECTS"), "--area-patch", Required("ARC_AREA_PATCH"),
            "--area-cave-mobile", Required("ARC_CAVE_MOBILE"), "--source-id", "ORIGINAL_AREA_CAVE"]);
        using var coordinator = new TerrainLoadCoordinator();
        var result = await coordinator.LoadAsync(coordinator.BeginGeneration(), arguments).WaitAsync(TimeSpan.FromMinutes(8));
        var cave = result.Cave ?? throw new AssertFailedException("The cave was not loaded.");
        var exterior = new AreaMapRuntime(result.Area!.Snapshot, result.AreaStepSnapshot!, AreaChart.Exterior);
        var caveMap = new AreaMapRuntime(cave.Area.Snapshot, cave.Steps, cave.Chart);
        Assert.AreEqual((1, 79), (exterior.RuntimeMap, caveMap.RuntimeMap));
        Assert.AreEqual(new CellCoord(39, 34), cave.Chart.Spawn, "The cave chart starts at the entrance's authored arrival (2087,2018).");
        Assert.AreEqual(125, cave.Area.Plan.Commands.Count(c => c.Identity is { } id && cave.Area.Snapshot.Objects.Single(o => o.Key == id).Type == 0), "Every wall is drawn.");
        AssertCaveWallCheck(cave.Area.Snapshot, cave.Area.Plan, cave.Steps);

        var clock = new ProfileBinding(1, AreaAmbientDriver.ClockPolicy, AreaAmbientDriver.ClockSourceRevision, result.Area.Lighting!.Source.ModuleSha256,
            result.Area.Lighting.Source.Startup.PayloadSha256, result.Area.Lighting.Source.Startup.Year, result.Area.Lighting.Source.Startup.Hour);
        var created = AreaTransitionCoordinator.CreateSession(clock, Guid.NewGuid(), Guid.NewGuid(), AreaGridSession.Cell(AreaGridSession.Spawn), 1);
        using var session = created.Owner ?? throw new AssertFailedException("Session: " + created.Failure);
        var transitions = new AreaTransitionCoordinator([exterior, caveMap], 1, session);

        // Exterior: the entrance is not usable from spawn, then it is after walking within range.
        var (entrance, trigger) = transitions.Triggers().Single();
        Assert.AreEqual((5078, 79, 2087L, 2018L), (entrance.CatalogueDestination, entrance.RuntimeDestination, entrance.ArrivalX, entrance.ArrivalY));
        var far = transitions.RequestUse(trigger.Key, AreaGridSession.Cell(AreaGridSession.Spawn));
        Assert.AreEqual(AreaUseOutcome.TooFar, far.Outcome);
        var near = Walk(exterior, AreaGridSession.Spawn, new(trigger.WorldX!.Value, trigger.WorldY!.Value));
        var use = transitions.RequestUse(trigger.Key, exterior.Chart.ToWorld(near));
        Assert.AreEqual(AreaUseOutcome.Accepted, use.Outcome, use.Reason);
        Assert.AreEqual(AreaUseOutcome.TransitionPending, transitions.RequestUse(trigger.Key, exterior.Chart.ToWorld(near)).Outcome, "A second request while pending is refused.");
        // A reset/reload drops the queued transfer unapplied; the use can be made again.
        var dropped = transitions.Cancel() ?? throw new AssertFailedException("Nothing was pending.");
        Assert.AreEqual((1, 79, 1, 0L), (dropped.FromMap, dropped.ToMap, transitions.CurrentMap, session.Version.Revision));
        Assert.AreEqual(AreaUseOutcome.Accepted, transitions.RequestUse(trigger.Key, exterior.Chart.ToWorld(near)).Outcome);
        // Without the destination map loaded the endpoint is unavailable and nothing is queued.
        var alone = new AreaTransitionCoordinator([exterior], 1);
        var unavailable = alone.RequestUse(trigger.Key, exterior.Chart.ToWorld(near));
        Assert.AreEqual((AreaUseOutcome.DestinationUnavailable, "DESTINATION_MAP_NOT_LOADED"), (unavailable.Outcome, unavailable.Reason));
        Assert.IsNull(alone.Pending);
        var inbound = transitions.Process()!;
        Assert.IsNull(transitions.Process(), "The transfer is applied exactly once.");
        Assert.AreEqual((1, 79, new AreaStepCell(2087, 2018), new CellCoord(39, 34)), (inbound.FromMap, inbound.ToMap, inbound.Arrival, inbound.ArrivalChartCell));
        Assert.AreEqual((79, new CellCoord(2087, 2018)), (session.QueryActor().Map.RuntimeMap, session.QueryActor().Cell));
        var caveSession = new AreaGridSession(cave.Steps, SimulationTime.At(0), StepDuration.Of(1), chart: cave.Chart, start: inbound.ArrivalChartCell);
        Assert.AreEqual(inbound.ArrivalChartCell, caveSession.State.CommittedCell);

        // Cave: walk to the exit and use it; the way back is the authored reverse record.
        var (exit, exitTrigger) = transitions.Triggers().Single();
        Assert.AreEqual((5000, 1, 92913L, 82598L), (exit.CatalogueDestination, exit.RuntimeDestination, exit.ArrivalX, exit.ArrivalY));
        var exitNear = Walk(caveMap, inbound.ArrivalChartCell, new(exitTrigger.WorldX!.Value, exitTrigger.WorldY!.Value));
        Assert.AreEqual(AreaUseOutcome.Accepted, transitions.RequestUse(exitTrigger.Key, caveMap.Chart.ToWorld(exitNear)).Outcome);
        var outbound = transitions.Process()!;
        Assert.AreEqual((79, 1, new AreaStepCell(92913, 82598)), (outbound.FromMap, outbound.ToMap, outbound.Arrival));
        Assert.AreEqual(outbound.Arrival, exterior.Chart.ToWorld(outbound.ArrivalChartCell));
        Assert.AreEqual((1, new CellCoord(92913, 82598)), (session.QueryActor().Map.RuntimeMap, session.QueryActor().Cell));
        Assert.AreEqual(2, session.Version.Revision, "Two transfers, two actor changes.");
        var back = new AreaGridSession(result.AreaStepSnapshot!, SimulationTime.At(0), StepDuration.Of(1), start: outbound.ArrivalChartCell);
        Assert.AreEqual(outbound.ArrivalChartCell, back.State.CommittedCell, "The return cell is admitted for movement.");
        Console.WriteLine($"cave transition PASS: entrance used from {near} (exterior chart), exit used from {exitNear} (cave chart), session revision {session.Version.Revision}");
    }

    // ARC-RENDER-011: at the arrival the wall check fades the corridor's front wall row (y 2022, x 2087..2092) and the
    // row behind it (y 2026, x 2078..2082, its corners fading towards the edge); no admitted cell probes a fade-edge tile
    // that holds more than one wall, so the source's head-of-list choice never matters in the cave.
    private static void AssertCaveWallCheck(Arcanum.Data.Area.AreaSnapshot snapshot, AreaScenePlan plan, AreaStepSnapshot steps)
    {
        var check = AreaWallCheck.FromArea(snapshot, plan);
        Assert.AreEqual(125, check.WallCount);
        var arrival = check.Recalculate(2087, 2018);
        Assert.AreEqual((2086L, 2018L, AreaWallRegions.LowY, 11), (arrival.BlockX, arrival.BlockY, arrival.Regions, arrival.Fades.Count));
        var walls = snapshot.Objects.Where(o => o.Type == 0).ToDictionary(o => o.Key);
        string Fade(long x, long y) => string.Join("", walls.Values.Where(w => w.WorldX == x && w.WorldY == y).Select(w => arrival.FadeOf(w.Key) switch
            { AreaWallFade.Both => "B", AreaWallFade.Left => "L", AreaWallFade.Right => "R", _ => "-" }));
        Assert.AreEqual("BBBBBB", string.Concat(Enumerable.Range(2087, 6).Select(x => Fade(x, 2022))));
        Assert.AreEqual("LBBBR", string.Concat(Enumerable.Range(2078, 5).Select(x => Fade(x, 2026))));
        var cells = 0; var faded = 0;
        for (long y = 1984; y < 2048; y++)
            for (long x = 2048; x < 2112; x++)
            {
                if (!steps.IsAdmitted(new(x, y))) continue;
                check.Reset(); var state = check.Recalculate(x, y);
                cells++; if (state.Fades.Count != 0) faded++;
                Assert.AreEqual(0, state.SharedEdgeTiles, $"shared edge tile from ({x},{y})");
            }
        Console.WriteLine($"cave wall check: {cells} admitted cells, {faded} with faded walls, arrival {arrival.Fades.Count} faded");
    }

    // Finds a reachable cell within use range of the target by breadth-first search over the same step
    // evidence, then walks there in route hops of at most 8 cells with the authoritative session.
    private static CellCoord Walk(AreaMapRuntime map, CellCoord start, AreaStepCell target)
    {
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        var goalChart = map.Chart.ToChart(target);
        var previous = new Dictionary<CellCoord, CellCoord> { [start] = start }; var queue = new Queue<CellCoord>([start]);
        CellCoord? reached = null;
        while (queue.TryDequeue(out var cell))
        {
            if (Math.Max(Math.Abs(cell.X - goalChart.X), Math.Abs(cell.Y - goalChart.Y)) is > 0 and <= 1) { reached = cell; break; }
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var next = RoutePlanner.Neighbor(cell, direction);
                if (previous.ContainsKey(next) || !routes.Contains(next) || routes.Assess(cell, direction) != RouteStepVerdict.Open) continue;
                previous[next] = cell; queue.Enqueue(next);
            }
        }
        if (reached is not { } goal) throw new AssertFailedException("No reachable cell next to the trigger.");
        var path = new List<CellCoord> { goal };
        while (path[^1] != start) path.Add(previous[path[^1]]);
        path.Reverse();
        var from = start;
        for (var at = 8; ; at = Math.Min(at + 8, path.Count - 1))
        {
            // One fresh authoritative session and follower per hop, as the route gate walks each goal.
            var hop = path[Math.Min(at, path.Count - 1)];
            var session = new AreaGridSession(map.Steps, SimulationTime.At(0), StepDuration.Of(1), chart: map.Chart, start: from);
            var follower = new RouteFollower(routes);
            follower.SetTarget(hop, session.State.Time);
            var drive = follower.Drive(session.State, SimulationTime.At(4 * 16 + 8), (until, batches) => session.Advance(until, batches.ToArray()).Movement);
            Assert.IsTrue(drive.Completed, drive.FailureCode);
            Assert.AreEqual(hop, session.State.CommittedCell, $"route hop {at}/{path.Count} status={follower.Status} reason={follower.StopReason}");
            if (hop == goal) return goal;
            from = hop;
        }
    }
}
