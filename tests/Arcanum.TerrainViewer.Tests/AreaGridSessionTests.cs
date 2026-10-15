using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaGridSessionTests
{
    private static InputBatch Input(int time, long sequence, NeighborDirection? direction) => new(SimulationTime.At(time),
        [MovementInput.Set(sequence, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral)]);
    private static string[] Trace(GridAdvance advance) => advance.Movement.Events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}").ToArray();

    // Public strict owner fixture: core sectors 1452..1453 x 1290..1291 with blocking scenery at base-sector cells (5,1) and (6,1).
    private static AreaStepSnapshot Steps(long epoch = 7)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        using var module = DatArchive.Open(new MemoryStream(source.Streams[new(AreaSourceRole.Module)].Bytes));
        using var names = DatArchive.Open(new MemoryStream(AreaOwnerFixture.Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{200}{flr}{300}{stn/s/n 4}"))))); 
        using var effects = DatArchive.Open(new MemoryStream(AreaOwnerFixture.Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        return AreaStepInspector.Inspect(area, module, names, effects, null, ReadOnlyMemory<byte>.Empty, epoch);
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
    public void AreaSessionRequiresAnAdmittedSpawnAndValidDuration()
    {
        var steps = Steps();
        Assert.ThrowsExactly<ArgumentException>(() => new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(0)));
        Assert.AreEqual(new AreaStepCell(92928 - 64, 82560), AreaGridSession.Cell(new(-64, 0)));
        var actor = new DiagnosticGridActor(); actor.Configure(steps, SimulationTime.At(0));
        Assert.IsInstanceOfType<AreaGridSession>(actor.Session); Assert.IsNull(actor.CardinalSession);
    }
}
