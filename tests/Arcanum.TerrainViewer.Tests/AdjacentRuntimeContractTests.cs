using System.Text.Json;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AdjacentRuntimeContractTests
{
    private static InputBatch At(int time,long seq,NeighborDirection? dir)=>new(SimulationTime.At(time),
        [MovementInput.Set(seq,dir is{} d?ActorIntent.Toward(d):ActorIntent.Neutral)]);
    private static DiagnosticGridSession Session(GridWireFixture f,MovementLimits? limits=null)=>new(f.Snapshot,SimulationTime.At(0),StepDuration.Of(8),limits);
    private static string[] Trace(IEnumerable<MovementEvent> events)=>events.Select(e=>$"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}").ToArray();

    [TestMethod]
    [DataRow(2,false)] [DataRow(4,true)] [DataRow(6,true)]
    public void DiagonalEarlyEqualityLateHaveOneSegmentAndNoShoulderCommits(int release,bool committed)
    {
        using var f=new GridWireFixture(policy:CardinalTraversalAdmissionPolicy.BoundedAdjacent);var session=Session(f);
        var advance=session.Advance(SimulationTime.At(8),[At(0,0,NeighborDirection.XPlusYPlus),At(release,1,null)]);
        var prefix=new[]{"StepStarted@0/1:6,1>7,2","FacingChanged@0/1:6,1>7,2","ActionChanged@0/1:6,1>7,2"};
        var suffix=committed?new[]{"CellCommitted@4/1:6,1>7,2","CenterArrived@8/1:6,1>7,2","ActionChanged@8/1:6,1>7,2"}
            :new[]{"StepCancelled@2/1:6,1>7,2","FacingChanged@2/1:6,1>7,2","CenterArrived@4/1:6,1>7,2","ActionChanged@4/1:6,1>7,2"};
        CollectionAssert.AreEqual(prefix.Concat(suffix).ToArray(),Trace(advance.Movement.Events));
        Assert.AreEqual(committed?new CellCoord(7,2):new CellCoord(6,1),session.State.CommittedCell);
        Assert.AreEqual(1,advance.Evidence.Count);Assert.AreEqual(4,advance.Evidence.Single().Assessment.Legs.Count);
        Assert.AreEqual(1,advance.Movement.Events.Select(e=>e.Key).Distinct().Count());
        Assert.AreEqual(MovementPhase.Settled,session.State.Phase);
    }

    [TestMethod]
    public void HeldDiagonalsRenewUntilZoneDenialAndMatchPartitionedKeysAndFullEvents()
    {
        using var f=new GridWireFixture(policy:CardinalTraversalAdmissionPolicy.BoundedAdjacent);var whole=Session(f);var split=Session(f);
        var inputs=new[]{At(0,0,NeighborDirection.XPlusYPlus)};
        var result=whole.Advance(SimulationTime.At(32),inputs);
        CollectionAssert.AreEqual(new[]{"StepStarted@0/1:6,1>7,2","FacingChanged@0/1:6,1>7,2","ActionChanged@0/1:6,1>7,2",
            "CellCommitted@4/1:6,1>7,2","CenterArrived@8/1:6,1>7,2","StepStarted@8/1:7,2>8,3","CellCommitted@12/1:7,2>8,3",
            "CenterArrived@16/1:7,2>8,3","StepRejected@16/1:8,3>9,4","ActionChanged@16/1:8,3>9,4"},Trace(result.Movement.Events));
        Assert.AreEqual("ZONE_UNSUPPORTED",result.Evidence.Last().Assessment.ReasonCode);
        Assert.AreEqual(3,result.Evidence.Count);Assert.IsTrue(result.Evidence.Take(2).All(e=>e.Assessment.Legs.Count==4));
        Assert.IsTrue(result.Evidence.All(e=>e.Key.MaterialIntentKey==new InputKey(SimulationTime.At(0),0)));
        var events=new List<MovementEvent>();var keys=new List<AdmissionKey>();
        for(var time=0;time<=32;time++)
        {
            var part=split.Advance(SimulationTime.At(time),time==0?inputs:[]);
            events.AddRange(part.Movement.Events);keys.AddRange(part.Movement.ConsumedAssessmentKeys);
        }
        Assert.AreEqual(JsonSerializer.Serialize(result.Movement.Events),JsonSerializer.Serialize(events));
        CollectionAssert.AreEqual(result.Movement.ConsumedAssessmentKeys.ToArray(),keys.ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(MovementRuntime.QuerySnapshot(whole.State)),JsonSerializer.Serialize(MovementRuntime.QuerySnapshot(split.State)));
        Assert.AreEqual(0,whole.Advance(SimulationTime.At(40),[At(33,1,NeighborDirection.XPlusYPlus)]).Movement.Events.Count);
    }

    [TestMethod]
    public void CornerDenialAndReplaySelectionFailureLeaveNoPartialDiagonalPublication()
    {
        using var f=new GridWireFixture(policy:CardinalTraversalAdmissionPolicy.BoundedAdjacent);var session=Session(f);
        var denied=session.Advance(SimulationTime.At(8),[At(0,0,NeighborDirection.XMinusYPlus)]);
        Assert.AreEqual(MovementEventKind.StepRejected,denied.Movement.Events.Single().Kind);
        Assert.AreEqual("ADMITTED_ADJACENT_SCENERY_BLOCKER",denied.Evidence.Single().Assessment.ReasonCode);
        Assert.AreEqual(4,denied.Evidence.Single().Assessment.Legs.Count);
        Assert.AreEqual(0,session.Advance(SimulationTime.At(16),[At(9,1,NeighborDirection.XMinusYPlus)]).Movement.Events.Count);
        var selection=Session(f);var before=selection.State;
        Assert.Throws<InvalidOperationException>(()=>selection.Advance(SimulationTime.At(8),[At(0,0,NeighborDirection.XPlusYPlus)],(_,_)=>throw new InvalidOperationException("synthetic selection")));
        Assert.AreSame(before,selection.State);Assert.IsNull(selection.LastAdvance);
        var limited=Session(f,new MovementLimits(assessments:1,opportunities:2));before=limited.State;
        Assert.Throws<GridReplayCapacityException>(()=>limited.Advance(SimulationTime.At(16),[At(0,0,NeighborDirection.XPlusYPlus)]));
        Assert.AreSame(before,limited.State);Assert.IsNull(limited.LastAdvance);
    }
}
