using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaRouteSourceTests
{
    // Same strict owner fixture as AreaGridSessionTests: blocking scenery at (5,1), spawn (6,1), sectors 1452..1453 x 1290..1291.
    private static AreaStepSnapshot Steps(bool tileNamesResolved = true)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        return AreaOwnerFixture.Steps(source.Owner.LoadStrict(), source.Streams[new(AreaSourceRole.Module)].Bytes, 7, tileNamesResolved);
    }

    [TestMethod]
    public void ClickRouteWalksAroundTheBlockerThroughTheAuthoritativeSession()
    {
        var steps = Steps();
        var routes = new AreaRouteSource(steps);
        var session = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(1));
        var follower = new RouteFollower(routes);
        Assert.IsTrue(follower.SetTarget(new(3, 1), session.State.Time));
        var evidence = new List<AreaGridStepEvidence>();
        var drive = follower.Drive(session.State, SimulationTime.At(20), (until, batches) =>
        {
            var advance = session.Advance(until, batches.ToArray());
            evidence.AddRange(advance.AreaEvidence);
            return advance.Movement;
        });
        Assert.IsTrue(drive.Completed, drive.FailureCode);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(3, 1)), (follower.Status, session.State.CommittedCell));
        var plan = follower.LastPlan!;
        Assert.AreEqual(RouteMethod.LocalSearch, plan.Method, "The straight step west hits the scenery blocker at (5,1).");
        CollectionAssert.DoesNotContain(plan.Cells.ToArray(), new CellCoord(5, 1));
        // Every step the runtime took is the planned one, admitted Supported Open by the same Data snapshot.
        CollectionAssert.AreEqual(plan.Cells.ToArray(), evidence.Select(e => new CellCoord(
            (int)(e.Assessment.To.X - AreaGridSession.ChartOriginX), (int)(e.Assessment.To.Y - AreaGridSession.ChartOriginY))).ToArray());
        Assert.IsTrue(evidence.All(e => e.Assessment.Coverage == CardinalTraversalCoverage.Supported && e.Assessment.Disposition == CardinalTraversalDisposition.Open));
    }

    [TestMethod]
    public void VerdictsKeepBlockedUnknownAndCoreEdgeApart()
    {
        var routes = new AreaRouteSource(Steps());
        Assert.AreEqual(RouteStepVerdict.Blocked, routes.Assess(new(6, 1), NeighborDirection.XMinus));
        Assert.AreEqual(RouteStepVerdict.Open, routes.Assess(new(7, 1), NeighborDirection.XMinus), "OF_NO_BLOCK canopy is enterable.");
        Assert.AreEqual(RouteStepVerdict.Unsupported, routes.Assess(new(6, 0), NeighborDirection.YMinus), "Absent sector 1452,1289.");
        Assert.IsFalse(routes.Contains(new(6, -1)));
        Assert.AreEqual("ROUTE_TARGET_OUTSIDE_AREA", RoutePlanner.Plan(routes, new(6, 1), new(6, -1)).ReasonCode);
        var unknown = new AreaRouteSource(Steps(tileNamesResolved: false));
        Assert.AreEqual(RouteStepVerdict.Unknown, unknown.Assess(new(6, 1), NeighborDirection.XPlus));
        var plan = RoutePlanner.Plan(unknown, new(6, 1), new(9, 1));
        Assert.AreEqual((RouteOutcome.NoRoute, "ROUTE_EVIDENCE_INCOMPLETE"), (plan.Outcome, plan.ReasonCode));
    }
}
