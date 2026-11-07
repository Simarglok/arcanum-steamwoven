using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaGridSessionTests
{
    private static InputBatch Input(int time, long sequence, NeighborDirection? direction) => new(SimulationTime.At(time),
        [MovementInput.Set(sequence, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral)]);
    private static string[] Trace(GridAdvance advance) => advance.Movement.Events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}").ToArray();

    // Public strict owner fixture: core sectors 1452..1453 x 1290..1291 with blocking scenery at base-sector cell (5,1) and an OF_NO_BLOCK canopy at (6,1).
    private static AreaStepSnapshot Steps(long epoch = 7)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        return AreaOwnerFixture.Steps(source.Owner.LoadStrict(), source.Streams[new(AreaSourceRole.Module)].Bytes, epoch);
    }

    [TestMethod]
    public void AreaSessionWalksAcrossSectorsAndKeepsWholeDataEvidence()
    {
        var steps = Steps();
        var session = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8));
        Assert.AreEqual(new CellCoord(6, 1), session.State.CommittedCell);
        Assert.AreEqual(steps.EvidenceSha256, session.State.Binding.InputFingerprint);
        Assert.AreEqual(7, session.EvidenceEpoch);
        var blocked = session.Advance(SimulationTime.At(1), [Input(0, 0, NeighborDirection.XMinus)]);
        Assert.AreEqual(MovementStatus.Completed, blocked.Movement.Status);
        Assert.AreEqual(new CellCoord(6, 1), session.State.CommittedCell);
        var denied = blocked.AreaEvidence.Single().Assessment;
        Assert.AreEqual((CardinalTraversalDisposition?)CardinalTraversalDisposition.Blocked, denied.Disposition);
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", denied.ReasonCode);
        Assert.AreEqual(0, blocked.Evidence.Count, "Cardinal evidence stays empty for the area profile.");

        // 58 steps east from (6,1) crosses into sector 1453,1290 at x = 64.
        var inputs = new List<InputBatch> { Input(2, 1, NeighborDirection.XPlus) };
        var walk = session.Advance(SimulationTime.At(2 + 8 * 58), inputs.ToArray());
        Assert.AreEqual(MovementStatus.Completed, walk.Movement.Status);
        Assert.AreEqual(new CellCoord(64, 1), session.State.CommittedCell);
        Assert.IsTrue(walk.AreaEvidence.All(e => e.Assessment.Disposition == CardinalTraversalDisposition.Open));
        Assert.IsTrue(walk.AreaEvidence.Any(e => e.Assessment.From == new AreaStepCell(92991, 82561) && e.Assessment.To == new AreaStepCell(92992, 82561)), "Sector boundary crossing.");
        Assert.AreSame(walk, session.LastAdvance);
    }

    // ARC-DEV-203 (B45): the hero's placement (the tile his shade and draw order go by) is the committed cell, which the original's
    // location_normalize (0x4B9420) moves half way along a step, in both directions; a floor of the position took the new tile at the
    // step's end towards +X and at its start towards -X, so a vault out of a window kept the indoor shade for the whole jump.
    [TestMethod]
    public void TheHeroPlacementTileIsTheCommittedCellAtEveryInstantOfAStepInBothDirections()
    {
        var actor = new DiagnosticGridActor();
        actor.Configure(Steps(), SimulationTime.At(0), StepDuration.Of(8));
        var changes = 0; var last = actor.Session!.State.CommittedCell;
        void Sample(int time, InputBatch[] batches)
        {
            actor.Advance(SimulationTime.At(time), batches, _ => { });
            var state = actor.Session!.State; var placement = actor.Placement();
            Assert.AreEqual((state.CommittedCell.X, state.CommittedCell.Y), (placement.MapX, placement.MapY), $"time {time}");
            if (state.CommittedCell != last) { changes++; last = state.CommittedCell; }
        }
        // East for three steps, released, then run until the actor settles; then west the same way.
        var time = 1;
        Sample(time, [Input(0, 0, NeighborDirection.XPlus)]);
        for (; time < 20; ) Sample(++time, []);
        Sample(++time, [Input(time, 1, null)]);
        for (var guard = 0; actor.Session!.State.Phase != MovementPhase.Settled && guard < 40; guard++) Sample(++time, []);
        Assert.AreEqual(MovementPhase.Settled, actor.Session!.State.Phase, $"time {time} cell {actor.Session.State.CommittedCell} anchor {actor.Session.State.Anchor}");
        var east = actor.Session.State.CommittedCell;
        Sample(++time, [Input(time, 2, NeighborDirection.XMinus)]);
        for (var n = 0; n < 19; n++) Sample(++time, []);
        Sample(++time, [Input(time, 3, null)]);
        for (var guard = 0; actor.Session.State.Phase != MovementPhase.Settled && guard < 40; guard++) Sample(++time, []);
        Assert.IsTrue(changes >= 4, $"the cell committed on both legs: {changes}");
        Assert.IsTrue(east.X > 6, "the first leg walked east");
        Assert.IsTrue(actor.Session.State.CommittedCell.X < east.X, "the second leg walked back west");
    }

    // ARC-COMBAT-020: the step charge is asked once per open step, with its gait, and its reason refuses the step.
    [TestMethod]
    public void TheStepChargeIsAskedOncePerOpenStepAndARefusalBlocksIt()
    {
        var asked = new List<(AdmissionKey Key, MovementGait Gait)>();
        var allowed = 3;
        var session = new AreaGridSession(Steps(), SimulationTime.At(0), StepDuration.Of(8))
        {
            Gait = () => MovementGait.Run,
            StepCharge = (key, gait) => { asked.Add((key, gait)); return asked.Count <= allowed ? null : "TURN_BASED_REFUSED"; },
        };
        var blocked = session.Advance(SimulationTime.At(1), [Input(0, 0, NeighborDirection.XMinus)]);
        Assert.AreEqual(0, asked.Count, "a step the map blocks is not charged");
        var walk = session.Advance(SimulationTime.At(2 + 8 * 10), [Input(2, 1, NeighborDirection.XPlus)]);
        Assert.AreEqual(new CellCoord(9, 1), session.State.CommittedCell, "three steps went, the fourth was refused");
        Assert.AreEqual(4, asked.Count);
        Assert.IsTrue(asked.All(a => a.Gait == MovementGait.Run));
        CollectionAssert.AreEqual(asked.Select(a => a.Key).Distinct().ToArray(), asked.Select(a => a.Key).ToArray(), "each step asked once");
        var refused = walk.AreaEvidence.Last();
        Assert.AreEqual(((CardinalTraversalDisposition?)CardinalTraversalDisposition.Blocked, "TURN_BASED_REFUSED"), (refused.Live!.Value.Disposition, refused.Live.Value.ReasonCode));
        Assert.AreEqual((CardinalTraversalDisposition?)CardinalTraversalDisposition.Open, refused.Assessment.Disposition, "the map itself stays open");
    }

    [TestMethod]
    public void NoBlockCanopyCellIsEnterableAndUnknownTilesAreUnknown()
    {
        var steps = Steps();
        var back = steps.Assess(steps.Request(AreaGridSession.Cell(new(7, 1)), AreaGridSession.Cell(new(6, 1)), CardinalTraversalDirection.XMinus));
        Assert.AreEqual((CardinalTraversalDisposition?)CardinalTraversalDisposition.Open, back.Disposition);
        Assert.AreEqual("NO_BLOCK_FLAG", back.Objects.Single().ReasonCode);
        using var source = new AreaOwnerFixture(motionBodies: true);
        var unknown = AreaOwnerFixture.Steps(source.Owner.LoadStrict(), source.Streams[new(AreaSourceRole.Module)].Bytes, 7, tileNamesResolved: false);
        var step = unknown.Assess(unknown.Request(AreaGridSession.Cell(new(6, 1)), AreaGridSession.Cell(new(7, 1)), CardinalTraversalDirection.XPlus));
        Assert.AreEqual((CardinalTraversalCoverage.Unknown, "TILE_NAME_TABLE_UNSUPPORTED"), (step.Coverage, step.Gaps.Single().ReasonCode));
    }

    [TestMethod]
    public void AreaSessionRefusesTheAdmittedEdgeAsUnsupported()
    {
        var session = new AreaGridSession(Steps(), SimulationTime.At(0), StepDuration.Of(8));
        var advance = session.Advance(SimulationTime.At(1 + 8 * 2), [Input(0, 0, NeighborDirection.YMinus)]);
        // (6,1) -> (6,0) is admitted; (6,-1) lies in absent sector 1452,1289 and is refused like a map edge.
        Assert.AreEqual(new CellCoord(6, 0), session.State.CommittedCell);
        var edge = advance.AreaEvidence.Last().Assessment;
        Assert.AreEqual(CardinalTraversalCoverage.Unsupported, edge.Coverage);
        Assert.AreEqual("DESTINATION_OUTSIDE_ADMITTED_AREA", edge.ReasonCode);
        CollectionAssert.Contains(Trace(advance), "StepRejected@8/1:6,0>6,-1");
    }

    [TestMethod]
    public void ActorPlacementFollowsTheAreaSessionOutsideTheBaseSector()
    {
        var actor = new DiagnosticGridActor(); actor.Configure(Steps(), SimulationTime.At(0), StepDuration.Of(8));
        var placements = new List<ActorDiagnosticPlacementInput>();
        // East across x = 64 (sector 1453,1290) through the production presentation callback.
        var east = actor.Advance(SimulationTime.At(1 + 8 * 58), [Input(0, 0, NeighborDirection.XPlus)], placements.Add);
        Assert.AreEqual(MovementStatus.Completed, east.Movement.Status);
        Assert.AreEqual(new CellCoord(64, 1), actor.Session!.State.CommittedCell);
        Assert.IsTrue(actor.Placement().MapX >= 64 && placements.Count != 0);
        // North into y = -1 lies in absent sector 1452,1289 here: refused by Data, placement stays valid at y = 0.
        actor.Configure(Steps(), SimulationTime.At(0), StepDuration.Of(8));
        var north = actor.Advance(SimulationTime.At(1 + 8 * 2), [Input(0, 0, NeighborDirection.YMinus)], placements.Add);
        Assert.AreEqual(MovementStatus.Completed, north.Movement.Status);
        Assert.AreEqual(0L, actor.Placement().MapY);
        // The chart range admits negative and > 63 cells; one-sector WORLD placement still rejects them.
        var outside = new DiagnosticActorState(new(-3.5, 100), 5, ActorDiagnosticAction.Walk);
        Assert.AreEqual((-4L, 100L), (DiagnosticActorMotion.ToPlacement(outside, 0, -64, 127).MapX, DiagnosticActorMotion.ToPlacement(outside, 0, -64, 127).MapY));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticActorMotion.ToPlacement(outside, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticActorMotion.ToPlacement(outside with { Position = new(-65, 0) }, 0, -64, 127));
    }

    [TestMethod]
    public void AreaPlacementAndFollowAnchorUseTheChartLocationOrigin()
    {
        var actor = new DiagnosticGridActor(); actor.Configure(Steps(), SimulationTime.At(0), StepDuration.Of(8));
        var placement = actor.Placement();
        Assert.AreEqual((40L, 0L, 6L, 1L), (placement.CameraOriginX!.Value, placement.CameraOriginY!.Value, placement.MapX!.Value, placement.MapY!.Value));
        Assert.AreEqual(AreaCellPicking.Center(new(6, 1)), actor.FootAnchor, "AREA follows the diamond centre the actor stands on.");
        // The one-sector WORLD profile keeps its retained origin and foot point.
        using var fixture = new GridWireFixture(); var world = new DiagnosticGridActor(); world.Configure(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        Assert.AreEqual((0L, 0L), (world.Placement().CameraOriginX!.Value, world.Placement().CameraOriginY!.Value));
        Assert.AreEqual(DiagnosticActorMotion.Project(world.State.Position) + new GamepadLab.Core.Vec2(0, 20), world.FootAnchor);
    }

    [TestMethod]
    public void AreaSessionRequiresAnAdmittedSpawnAndValidDuration()
    {
        var steps = Steps();
        Assert.ThrowsExactly<ArgumentException>(() => new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(0)));
        Assert.AreEqual(new AreaStepCell(92928 - 64, 82560), AreaGridSession.Cell(new(-64, 0)));
        var actor = new DiagnosticGridActor(); actor.Configure(steps, SimulationTime.At(0));
        Assert.IsInstanceOfType<AreaGridSession>(actor.Session); Assert.IsNull(actor.CardinalSession);
    }

    [TestMethod]
    public void AnotherChartPlacesTheSessionAtItsStartAndMapsCellsThroughItsOrigin()
    {
        var steps = Steps();
        // A chart whose origin is the second core sector (1453,1290): its cell (0,0) is exterior chart cell (64,0).
        var chart = new AreaChart(1453 * 64, 1290 * 64, new(-58, 1), "TEST_SECOND_SECTOR_CHART");
        Assert.AreEqual(new AreaStepCell(92928 + 6, 82561), chart.ToWorld(new(-58, 1)));
        Assert.AreEqual(new CellCoord(-58, 1), chart.ToChart(new(92928 + 6, 82561)));
        var session = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8), chart: chart);
        Assert.AreEqual((new CellCoord(-58, 1), chart), (session.State.CommittedCell, session.Chart));
        Assert.AreEqual("TEST_SECOND_SECTOR_CHART", session.State.Binding.ChartId);
        var started = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8), start: new(7, 1), facing: NeighborDirection.YPlus);
        Assert.AreEqual((new CellCoord(7, 1), NeighborDirection.YPlus), (started.State.CommittedCell, started.State.Facing));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8), start: new(500, 1)));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8), start: new(-200, 1)));
        Assert.AreSame(AreaChart.Exterior, new AreaGridSession(steps, SimulationTime.At(0)).Chart);
    }
}
