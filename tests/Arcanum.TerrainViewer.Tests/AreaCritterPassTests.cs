using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-074: outside combat mode the PC steps through living NPCs and companions (the owner's decision 2026-10-21); in
/// combat mode they block it; the Original segment-end stop (ledger LR5). Invented sector (0,0), open terrain, an NPC on (12,10) and
/// (18,30), a chest on (12,14), a companion's live cell (12,20).</summary>
[TestClass]
public sealed class AreaCritterPassTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);
    private static readonly AreaObjectKey Npc = new("s", 1, AreaSourceKind.Mobile, 0, 0, 1, "npc", 1), FarNpc = new("s", 1, AreaSourceKind.Mobile, 0, 0, 3, "far", 1);
    private static readonly AreaObjectKey Chest = new("s", 1, AreaSourceKind.Static, 0, 0, 2, "chest", 1);
    private static readonly CellCoord Companion = new(12, 20);
    private static readonly AreaChart Chart = new(0, 0, default, "TEST") { MinimumCell = 0, MaximumCell = 63 };

    private static AreaStepSnapshot Steps()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        AreaStepObjectEvaluation[] One(AreaObjectKey key, AreaStepCell cell, int type) =>
            [new(key, cell, type, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "TEST")];
        var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>
        {
            [new(12, 10)] = One(Npc, new(12, 10), 16), [new(18, 30)] = One(FarNpc, new(18, 30), 16), [new(12, 14)] = One(Chest, new(12, 14), 2),
        };
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain, objects,
            new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    // The PC walks a click route over the same steps and overlay its session admits.
    private static (RouteFollower Route, AreaGridSession Session) Walk(AreaStepSnapshot steps, CellCoord from, CellCoord to, bool pass, bool segmentEndStop = false,
        bool planPass = true)
    {
        var occupancy = new AreaOccupancy([], [Companion]);
        var session = new AreaGridSession(steps, SimulationTime.At(0), Step, chart: Chart, start: from) { Occupancy = () => occupancy, CrittersPass = () => pass };
        var route = new RouteFollower(new AreaRouteSource(steps, Chart, () => occupancy, crittersPass: () => planPass), segmentEndCritterEndsWalk: () => segmentEndStop);
        route.SetTarget(to, session.State.Time);
        for (var t = 1; t <= 120 && route.Status == RouteFollowStatus.Following; t++)
            route.Drive(session.State, SimulationTime.At(t), (until, batches) => session.Advance(until, batches.ToArray()).Movement);
        return (route, session);
    }

    [TestMethod]
    public void Outside_combat_the_PC_steps_through_an_NPC_and_a_companion()
    {
        var steps = Steps();
        var (throughNpc, at) = Walk(steps, new(10, 10), new(14, 10), pass: true);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(14, 10), 4), (throughNpc.Status, at.State.CommittedCell, throughNpc.LastPlan!.Steps.Count),
            "a straight line through the NPC's tile");
        var (throughCompanion, atCompanion) = Walk(steps, new(10, 20), new(14, 20), pass: true);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(14, 20)), (throughCompanion.Status, atCompanion.State.CommittedCell));
        // Objects other than critters still block, and so does the goal holding an NPC (DC2).
        var onto = steps.Assess(steps.Request(new(11, 14), new(12, 14), CardinalTraversalDirection.XPlus));
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, AreaOccupancyRules.ApplyPassingCritters(onto, new(11, 14), NeighborDirection.XPlus, null).Disposition);
        var (toNpc, _) = Walk(steps, new(10, 10), new(12, 10), pass: true);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_NOT_FOUND"), (toNpc.Status, toNpc.StopReason));
    }

    [TestMethod]
    public void In_combat_the_PC_is_blocked()
    {
        // The admission refuses the step onto the NPC and the companion's cell; a plan that passes them ends with a typed reason.
        var steps = Steps();
        var (npc, at) = Walk(steps, new(10, 10), new(14, 10), pass: false);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED", new CellCoord(11, 10)), (npc.Status, npc.StopReason, at.State.CommittedCell));
        var (companion, atCompanion) = Walk(steps, new(10, 20), new(14, 20), pass: false);
        Assert.AreEqual((RouteFollowStatus.Stopped, new CellCoord(11, 20)), (companion.Status, atCompanion.State.CommittedCell));
        // The click route in combat mode plans with critters blocking and walks around.
        var (around, atAround) = Walk(steps, new(10, 10), new(14, 10), pass: false, planPass: false);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(14, 10)), (around.Status, atAround.State.CommittedCell));
    }

    [TestMethod]
    public void The_segment_end_stop_ends_an_Original_walk_before_an_NPC_and_Steamwoven_walks_through()
    {
        // The NPC on (18,30) is the eighth cell east of (10,30), the first segment's last step.
        var steps = Steps();
        var (original, atOriginal) = Walk(steps, new(10, 30), new(25, 30), pass: true, segmentEndStop: true);
        Assert.AreEqual((RouteFollowStatus.Stopped, "ROUTE_SEGMENT_END_CRITTER", new CellCoord(17, 30)), (original.Status, original.StopReason, atOriginal.State.CommittedCell));
        var (steamwoven, atSteamwoven) = Walk(steps, new(10, 30), new(25, 30), pass: true, segmentEndStop: false);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(25, 30)), (steamwoven.Status, atSteamwoven.State.CommittedCell));
    }
}
