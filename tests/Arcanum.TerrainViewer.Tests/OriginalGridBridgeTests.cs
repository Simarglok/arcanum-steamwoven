using Arcanum.Data.Traversal;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class OriginalGridBridgeTests
{
    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Fail("Requested original grid gate is missing an input role: " + name);
        return value!;
    }
    private static InputBatch Input(int time, long sequence, NeighborDirection? direction) => new(SimulationTime.At(time),
        [MovementInput.Set(sequence, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral)]);
    private static string Trace(GridAdvance result) => string.Join("|",result.Movement.Events.Select(e =>
        $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}"));

    [TestMethod, TestCategory("RealData")]
    public void GuardedProductionLoadAssessmentRuntimeAndHumanSelectionUseActualOriginalBlocker()
    {
        if (Environment.GetEnvironmentVariable("ARCANUM_GRID_ORIGINAL") != "1")
            Assert.Inconclusive("Opt-in original input gate; never a synthetic PASS.");
        var arguments = TerrainViewerArguments.Parse(["--mode","world-diagnostic","--module",Required("ARCANUM_STATIC_MODULE"),
            "--resource",Required("ARCANUM_STATIC_RESOURCE"),"--prototypes",Required("ARCANUM_STATIC_PROTOTYPES"),
            "--actor-resource",Required("ARCANUM_ACTOR_RESOURCE"),"--source-id","ORIGINAL_GRID_TEST"]);
        TerrainLoadResult loaded;
        using (var operation = new TerrainLoadCoordinator.ProductionTerrainLoadOperation()) loaded = operation.Execute(7,arguments,default);
        var snapshot = loaded.TraversalSnapshot!;
        Assert.AreEqual(CardinalTraversalInputKind.AuthenticatedOriginal,snapshot.InputIdentity.Kind);
        Assert.AreEqual(7L,snapshot.EvidenceEpoch); Assert.AreEqual(317,snapshot.TerminalRecordCount);
        Assert.AreEqual(ActorDiagnosticProfileId.HumanFixed,loaded.ActorPlan!.Profile.Id);
        Assert.AreEqual(55,loaded.ActorTextures.Count); Assert.AreEqual(457676L,loaded.ActorTextures.Values.Sum(t=>(long)t.Rgba.Length));
        Assert.AreEqual(loaded.ModuleFingerprint,loaded.ModuleFingerprintAfter);
        Assert.AreEqual(loaded.ResourceFingerprint,loaded.ResourceFingerprintAfter);
        Assert.AreEqual(loaded.ActorResourceFingerprint,loaded.ActorResourceFingerprintAfter);
        Assert.AreEqual(loaded.ActorPrototypeFingerprint,loaded.ActorPrototypeFingerprintAfter);
        var blocker = loaded.StaticPlan!.Commands.Single(c=>c.SourceIndex==1);
        Assert.AreEqual((-163,134),(blocker.AnchorX,blocker.AnchorY));
        Assert.AreEqual(new TerrainRect(-276,-128,204,267),blocker.Destination);
        var texture=loaded.StaticTextures[blocker.TextureKey]; Assert.AreEqual((204,267),(texture.Width,texture.Height));
        foreach(var release in new[]{2,4,6,8})
        {
            var actor=new DiagnosticGridActor();actor.Configure(snapshot,SimulationTime.At(0),StepDuration.Of(8));
            var result=actor.Advance(SimulationTime.At(8),[Input(0,0,NeighborDirection.XPlus),Input(release,1,null)],p=>loaded.ActorPlan.Select(p));
            Assert.AreEqual(MovementStatus.Completed,result.Movement.Status);
            var prefix="StepStarted@0/1:6,1>7,1|ActionChanged@0/1:6,1>7,1";
            Assert.AreEqual(prefix+(release==2
                ? "|StepCancelled@2/1:6,1>7,1|FacingChanged@2/1:6,1>7,1|CenterArrived@4/1:6,1>7,1|ActionChanged@4/1:6,1>7,1"
                : "|CellCommitted@4/1:6,1>7,1|CenterArrived@8/1:6,1>7,1|ActionChanged@8/1:6,1>7,1"),Trace(result));
            Assert.AreEqual(new CellCoord(release==2?6:7,1),actor.Session!.State.CommittedCell);
            Assert.AreEqual(317,result.Evidence.Single().Assessment.RecordAssessments.Count);
            Assert.AreEqual(0,result.Evidence.Single().Assessment.Gaps.Count);
            Assert.AreEqual(CardinalTraversalDisposition.Open,result.Evidence.Single().Assessment.Disposition);
            Assert.AreEqual(actor.Session.State.Binding,result.Evidence.Single().Key.Binding);
        }
        // Independently translated live D=1/2 timeline, including reverse from
        // settled Idle. Neither the report validator nor its literals are imported.
        var reverse = new DiagnosticGridActor();
        reverse.Configure(snapshot, SimulationTime.At(0), StepDuration.Of(1,2));
        InputBatch At(int numerator, int denominator, long sequence, NeighborDirection? direction) =>
            new(SimulationTime.At(numerator,denominator),
                [MovementInput.Set(sequence,direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral)]);
        var roundTrip = reverse.Advance(SimulationTime.At(5,2),
            [At(1,1,0,NeighborDirection.XPlus),At(3,2,1,null),
             At(2,1,2,NeighborDirection.XMinus),At(5,2,3,null)],p=>loaded.ActorPlan.Select(p));
        Assert.AreEqual(MovementStatus.Completed,roundTrip.Movement.Status);
        Assert.AreEqual("StepStarted@1/1:6,1>7,1|ActionChanged@1/1:6,1>7,1"
            + "|CellCommitted@5/4:6,1>7,1|CenterArrived@3/2:6,1>7,1|ActionChanged@3/2:6,1>7,1"
            + "|StepStarted@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1"
            + "|CellCommitted@9/4:7,1>6,1|CenterArrived@5/2:7,1>6,1|ActionChanged@5/2:7,1>6,1",Trace(roundTrip));
        CollectionAssert.AreEqual(new long[]{1,2,3,4,5,6,7,8,9,10,11},roundTrip.Movement.Events.Select(e=>e.Ordinal).ToArray());
        var binding=reverse.Session!.State.Binding;
        AdmissionKey[] expectedKeys = [
            new(binding,new(6,1),new(7,1),NeighborDirection.XPlus,SimulationTime.At(1),1,1,new(SimulationTime.At(1),0),0),
            new(binding,new(7,1),new(6,1),NeighborDirection.XMinus,SimulationTime.At(2),2,2,new(SimulationTime.At(2),2),0)];
        CollectionAssert.AreEqual(expectedKeys,roundTrip.Movement.ConsumedAssessmentKeys.ToArray());
        CollectionAssert.AreEqual(expectedKeys,roundTrip.Evidence.Select(e=>e.Key).ToArray());
        foreach(var e in roundTrip.Movement.Events)
        {
            Assert.AreEqual(expectedKeys[e.Ordinal<=5?0:1],e.Key);
            Assert.AreEqual(e.Key,e.Assessment!.Key);
        }
        var facing=roundTrip.Movement.Events.Single(e=>e.Kind==MovementEventKind.FacingChanged);
        Assert.AreEqual(NeighborDirection.XPlus,facing.OldFacing);
        Assert.AreEqual(NeighborDirection.XMinus,facing.NewFacing);
        CollectionAssert.AreEqual(new[]{MovementAction.Idle,MovementAction.Walk,MovementAction.Idle,MovementAction.Walk},
            roundTrip.Movement.Events.Where(e=>e.Kind==MovementEventKind.ActionChanged).Select(e=>e.OldAction!.Value).ToArray());
        CollectionAssert.AreEqual(new[]{MovementAction.Walk,MovementAction.Idle,MovementAction.Walk,MovementAction.Idle},
            roundTrip.Movement.Events.Where(e=>e.Kind==MovementEventKind.ActionChanged).Select(e=>e.NewAction!.Value).ToArray());
        Assert.IsTrue(roundTrip.Evidence.All(e=>e.Assessment.RecordAssessments.Count==317
            && e.Assessment.Gaps.Count==0 && e.Assessment.Disposition==CardinalTraversalDisposition.Open));
        Assert.AreEqual(new CellCoord(6,1),reverse.Session.State.CommittedCell);
        Assert.AreEqual(new WorldAnchor(ExactScalar.Integer(6),ExactScalar.Integer(1)),reverse.Session.State.Anchor);
        Assert.AreEqual(MovementPhase.Settled,reverse.Session.State.Phase);
        Assert.AreEqual(1,reverse.State.Facing); Assert.AreEqual(ActorDiagnosticAction.Idle,reverse.State.Action);
        var denied=new DiagnosticGridActor();denied.Configure(snapshot,SimulationTime.At(0),StepDuration.Of(8));
        var stop=denied.Advance(SimulationTime.At(8),[Input(0,0,NeighborDirection.XMinus)],p=>loaded.ActorPlan.Select(p));
        Assert.AreEqual("StepRejected@0/1:6,1>5,1",Trace(stop));
        Assert.AreEqual("ADMITTED_DESTINATION_SCENERY_BLOCKER",stop.Evidence.Single().Assessment.ReasonCode);
        Assert.AreEqual(CardinalTraversalDisposition.Blocked,stop.Evidence.Single().Assessment.Disposition);
        Assert.AreEqual(317,stop.Evidence.Single().Assessment.RecordAssessments.Count);
        Assert.AreEqual(new CellCoord(6,1),denied.Session!.State.CommittedCell);
        Assert.AreEqual(5,denied.State.Facing); Assert.AreEqual(ActorDiagnosticAction.Idle,denied.State.Action);
        Assert.AreEqual(0,denied.Advance(SimulationTime.At(10),[Input(9,1,NeighborDirection.XMinus)]).Movement.Events.Count);
    }
}
