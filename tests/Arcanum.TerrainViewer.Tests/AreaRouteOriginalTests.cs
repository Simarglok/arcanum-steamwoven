using System.Diagnostics;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaRouteOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public async Task ClickRoutesOnTheAuthenticatedWholeCoreArriveThroughTheSession()
    {
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1")
            Assert.Inconclusive("Opt-in authenticated AREA route gate; not a synthetic PASS.");
        string Required(string role) => Environment.GetEnvironmentVariable(role) is { Length: > 0 } value ? value
            : throw new AssertFailedException("Requested original AREA gate is missing an input role: " + role);
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Required("ARC_AREA_MODULE"),
            "--resource", Required("ARC_AREA_RESOURCE2"), "--actor-resource", Required("ARC_AREA_RESOURCE1"),
            "--area-mobile", Required("ARC_AREA_MOBILE"), "--prototypes", Required("ARC_AREA_PROTOTYPES"),
            "--area-effects", Required("ARC_AREA_EFFECTS"), "--area-patch", Required("ARC_AREA_PATCH"),
            "--source-id", "ORIGINAL_AREA_ROUTE"]);
        using var coordinator = new TerrainLoadCoordinator();
        var result = await coordinator.LoadAsync(coordinator.BeginGeneration(), arguments).WaitAsync(TimeSpan.FromMinutes(8));
        var steps = result.AreaStepSnapshot ?? throw new AssertFailedException("No whole-core step evidence.");
        var routes = new AreaRouteSource(steps);

        // (4,1) lies behind the scenery blocker at (5,1) west of spawn; (3,1) is itself occupied at T0.
        var around = RoutePlanner.Plan(routes, AreaGridSession.Spawn, new(4, 1));
        Assert.AreEqual((RouteOutcome.Found, (RouteMethod?)RouteMethod.LocalSearch), (around.Outcome, around.Method));
        CollectionAssert.DoesNotContain(around.Cells.ToArray(), new CellCoord(5, 1), "The scenery blocker at (5,1) is avoided.");
        foreach (var occupied in new CellCoord[] { new(5, 1), new(3, 1) })
        {
            var clock = Stopwatch.StartNew();
            var blocked = RoutePlanner.Plan(routes, AreaGridSession.Spawn, occupied);
            Console.WriteLine($"occupied {occupied.X},{occupied.Y}: {blocked.Outcome} {blocked.ReasonCode} expansions={blocked.Expansions} assessments={blocked.Assessments} {clock.Elapsed.TotalMilliseconds:F1} ms");
            Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_NOT_FOUND"), (blocked.Outcome, blocked.ReasonCode));
            Assert.IsTrue(blocked.Expansions <= RouteLimits.ExpansionMaximum);
        }

        // A ring of goals 20 cells from spawn: each found route is walked by the authoritative session.
        var found = 0; var refused = 0; var worst = TimeSpan.Zero;
        foreach (var (dx, dy) in new[] { (20, 0), (20, 20), (0, 20), (-20, 20), (-20, 0), (-20, -20), (0, -20), (20, -20) })
        {
            var goal = new CellCoord(AreaGridSession.Spawn.X + dx, AreaGridSession.Spawn.Y + dy);
            var clock = Stopwatch.StartNew();
            var plan = RoutePlanner.Plan(routes, AreaGridSession.Spawn, goal);
            if (clock.Elapsed > worst) worst = clock.Elapsed;
            Console.WriteLine($"route {goal.X},{goal.Y}: {plan.Outcome} {plan.ReasonCode} steps={plan.Steps.Count} expansions={plan.Expansions} assessments={plan.Assessments} {clock.Elapsed.TotalMilliseconds:F1} ms");
            if (plan.Outcome != RouteOutcome.Found) { refused++; continue; }
            found++;
            var session = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(1));
            var follower = new RouteFollower(routes);
            follower.SetTarget(goal, session.State.Time);
            var drive = follower.Drive(session.State, SimulationTime.At(4 * plan.Steps.Count + 8), (until, batches) => session.Advance(until, batches.ToArray()).Movement);
            Assert.IsTrue(drive.Completed, drive.FailureCode);
            Assert.AreEqual((RouteFollowStatus.Arrived, goal), (follower.Status, session.State.CommittedCell), $"goal {goal}");
        }
        Assert.IsTrue(found > 0, "At least one ring goal is reachable from spawn.");
        Console.WriteLine($"AREA route PASS: detour around (5,1), occupied goals NoRoute, {found} ring goals walked, {refused} refused; worst plan {worst.TotalMilliseconds:F1} ms. Not original pathfinder parity.");
    }
}
