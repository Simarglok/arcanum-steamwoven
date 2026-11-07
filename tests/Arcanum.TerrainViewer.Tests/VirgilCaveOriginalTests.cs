using Arcanum.Data.Lighting;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-017 on the authenticated exterior and cave: a following Virgil goes with the PC through the cave entrance
/// onto the authored arrival cell, steps off it, and comes back through the exit; while he is in the cave his exterior
/// T0 record is absent. Opt-in like the cave gate (ARC_AREA_ORIGINAL=1, the ARC_AREA_* roles and ARC_CAVE_MOBILE).
/// Structural identifiers and cells only.
/// </summary>
[TestClass]
public sealed class VirgilCaveOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public async Task OriginalVirgilFollowsThePcIntoTheCaveAndBack()
    {
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE")))
            Assert.Inconclusive("Opt-in authenticated AREA gate with ARC_CAVE_MOBILE; not a synthetic PASS.");
        string Required(string role) => Environment.GetEnvironmentVariable(role) is { Length: > 0 } value ? value
            : throw new AssertFailedException("Requested original AREA gate is missing an input role: " + role);
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Required("ARC_AREA_MODULE"),
            "--resource", Required("ARC_AREA_RESOURCE2"), "--actor-resource", Required("ARC_AREA_RESOURCE1"),
            "--area-mobile", Required("ARC_AREA_MOBILE"), "--prototypes", Required("ARC_AREA_PROTOTYPES"),
            "--area-effects", Required("ARC_AREA_EFFECTS"), "--area-patch", Required("ARC_AREA_PATCH"),
            "--area-cave-mobile", Required("ARC_CAVE_MOBILE"), "--source-id", "ORIGINAL_AREA_VIRGIL_CAVE"]);
        using var coordinator = new TerrainLoadCoordinator();
        var result = await coordinator.LoadAsync(coordinator.BeginGeneration(), arguments).WaitAsync(TimeSpan.FromMinutes(8));
        var cave = result.Cave ?? throw new AssertFailedException("The cave was not loaded.");
        var area = result.Area!;
        var exterior = new AreaMapRuntime(area.Snapshot, result.AreaStepSnapshot!, AreaChart.Exterior);
        var caveMap = new AreaMapRuntime(cave.Area.Snapshot, cave.Steps, cave.Chart);
        SessionMapId home = new(1), inside = new(79);

        var templates = AreaCompanionTemplate.From(AreaConversationHost.FindSpeakers(area.Snapshot), area.Plan, area.Snapshot.Visuals, AreaChart.Exterior, home);
        var virgil = templates.Single();
        Assert.AreEqual((new CellCoord(31, 34), 1324), (virgil.HomeOrigin, virgil.Speaker.ScriptId));
        // The cave draws him from its own frame repository once his T0 frames are added.
        var caveFrames = AreaCompanionTemplate.Frames(cave.Area.Frames, templates, area.Frames);
        Assert.IsTrue(virgil.Commands.All(c => caveFrames.ContainsKey(c.Frame)) && caveFrames.Count > cave.Area.Frames.Count);

        using var session = ConversationSessionFixture.Create(1);
        Assert.AreEqual(GameSessionDisposition.Changed, session.JoinParty(session.Version, virgil.Entity,
            AreaCompanionRoster.Facts(AreaCompanionRoster.Virgil, 0, 50), false).Disposition);
        var transitions = new AreaTransitionCoordinator([exterior, caveMap], 1, session);

        // The PC walks to the entrance and uses it; Virgil, at his T0 cell far behind, goes with it in the same step.
        var (entrance, trigger) = transitions.Triggers().Single();
        var near = AreaTransitionOriginalTests.Walk(exterior, AreaGridSession.Spawn, new(trigger.WorldX!.Value, trigger.WorldY!.Value));
        Assert.AreEqual(AreaUseOutcome.Accepted, transitions.RequestUse(trigger.Key, exterior.Chart.ToWorld(near)).Outcome);
        var outside = AreaCompanionGroup.Create(templates, exterior.Steps, exterior.Chart, session, home, SimulationTime.At(0));
        var arrival = new CellCoord((int)entrance.ArrivalX, (int)entrance.ArrivalY);
        var carry = outside.Carry(session, inside, arrival);
        Assert.AreEqual(1, carry.Carried.Count);
        var revision = session.Version.Revision;
        // A session refusal applies nothing: the transfer is dropped with its typed failure and can be requested again.
        Assert.IsNull(transitions.Process(NeighborDirection.XPlus, [carry.Changes[0], carry.Changes[0]]));
        Assert.AreEqual((GameSessionFailure.InvalidChangeKey, 1, revision, (AreaTransition?)null),
            (transitions.LastRefusal, transitions.CurrentMap, session.Version.Revision, transitions.Pending));
        Assert.AreEqual(AreaUseOutcome.Accepted, transitions.RequestUse(trigger.Key, exterior.Chart.ToWorld(near)).Outcome);
        var inbound = transitions.Process(NeighborDirection.XPlus, carry.Changes)!;
        Assert.AreEqual(GameSessionFailure.None, transitions.LastRefusal);
        Assert.AreEqual((79, revision + 1), (transitions.CurrentMap, session.Version.Revision));
        Assert.AreEqual(new AreaCompanionWhere(inside, (new CellCoord(2087, 2018), NeighborDirection.XPlus), null), AreaCompanionPlacement.Where(session, home, virgil.Entity));

        // In the cave he stands on the arrival cell with the PC, then steps off to an Open neighbour.
        var visit = AreaCompanionGroup.Create(templates, caveMap.Steps, caveMap.Chart, session, inside, SimulationTime.At(0));
        var companion = visit.Companions.Single();
        Assert.AreEqual(inbound.ArrivalChartCell, companion.Grid.State.CommittedCell);
        var pc = new AreaGridSession(caveMap.Steps, SimulationTime.At(0), StepDuration.Of(1, 2), chart: caveMap.Chart, start: inbound.ArrivalChartCell)
            { Occupancy = visit.OccupancyForPc };
        for (var frame = 1; frame <= 40; frame++) { pc.Advance(SimulationTime.At(frame, 10), []); visit.Drive(session, pc.State, SimulationTime.At(frame, 10)); }
        var cell = companion.Grid.State.CommittedCell;
        Console.WriteLine($"Virgil in the cave at chart {cell.X},{cell.Y} world {caveMap.Chart.ToWorld(cell)}; {visit.Status()}");
        Assert.AreEqual((1, MovementPhase.Settled), (FollowDistanceRules.Distance(cell, inbound.ArrivalChartCell), companion.Grid.State.Phase));
        var drawn = visit.Commands().Single();
        Assert.AreEqual(((int)caveMap.Chart.ToWorld(cell).X, (int)caveMap.Chart.ToWorld(cell).Y), (drawn.MapX, drawn.MapY));
        // Drawn where the cave plan would draw a body on that cell, and lit by the cave's tile class and lights there.
        var homeAt = Arcanum.Data.Terrain.TerrainGeometry.Project(checked((int)(virgil.Speaker.WorldX - AreaChart.Exterior.OriginX)), checked((int)(virgil.Speaker.WorldY - AreaChart.Exterior.OriginY)));
        var caveAt = Arcanum.Data.Terrain.TerrainGeometry.Project(cell.X, cell.Y);
        var t0 = virgil.Commands.Single();
        Assert.AreEqual((t0.Destination.X - homeAt.X + caveAt.X, t0.Destination.Y - homeAt.Y + caveAt.Y), (drawn.Destination.X, drawn.Destination.Y));
        var visual = virgil.Visuals[drawn.Visual];
        var pair = new AmbientPair(new(200, 150, 100), new(40, 50, 60));
        var local = cave.Area.Lighting!.Local!;
        var expected = local.Point(drawn.MapX, drawn.MapY, visual.ObjectOffsetX, visual.ObjectOffsetY, pair);
        Assert.AreEqual(new AreaLitColors(expected, expected), visit.Light(drawn, pair, local), "lit by the cave's lights where he stands");
        // Meanwhile on the exterior his T0 record is not in the world.
        var behind = AreaCompanionGroup.Create(templates, exterior.Steps, exterior.Chart, session, home, SimulationTime.At(0));
        Assert.AreEqual((0, true), (behind.Companions.Count, behind.Absent.Contains(virgil.Key)));
        Assert.IsTrue(behind.OccupancyForPc().Moved.Contains(virgil.Key));

        // Back through the exit: he comes home onto the authored return cell and the cave keeps nothing of him.
        var (exit, exitTrigger) = transitions.Triggers().Single();
        var exitNear = AreaTransitionOriginalTests.Walk(caveMap, inbound.ArrivalChartCell, new(exitTrigger.WorldX!.Value, exitTrigger.WorldY!.Value));
        Assert.AreEqual(AreaUseOutcome.Accepted, transitions.RequestUse(exitTrigger.Key, caveMap.Chart.ToWorld(exitNear)).Outcome);
        var back = visit.Carry(session, home, new((int)exit.ArrivalX, (int)exit.ArrivalY));
        var outbound = transitions.Process(NeighborDirection.XPlus, back.Changes)!;
        Assert.IsNull(session.QueryMapChange(home, AreaCompanionPlacement.MapKey(virgil.Entity)));
        Assert.AreEqual(0, session.QueryMapChanges(inside).Count);
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var returned = AreaCompanionGroup.Create(templates, exterior.Steps, exterior.Chart, loaded, home, SimulationTime.At(0));
        Assert.AreEqual((outbound.ArrivalChartCell, CompanionStanding.Following, 0),
            (returned.Companions.Single().Grid.State.CommittedCell, returned.Companions.Single().Standing, returned.Absent.Count));
    }
}
