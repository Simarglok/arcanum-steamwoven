using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class DiagnosticGridSessionTests
{
    private static InputBatch Input(int time, long sequence, NeighborDirection? direction) => new(SimulationTime.At(time),
        [MovementInput.Set(sequence, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral)]);
    private static DiagnosticGridSession Session(GridWireFixture fixture, MovementLimits? limits = null) =>
        new(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8), limits);
    private static string[] Trace(GridAdvance advance) => advance.Movement.Events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}").ToArray();

    [TestMethod]
    public void EarlyNeutralReturnsThroughRuntimeWithExactFullTrace()
    {
        using var fixture = new GridWireFixture(); var session = Session(fixture);
        var advance = session.Advance(SimulationTime.At(3), [Input(0,0,NeighborDirection.XPlus),Input(2,1,null)]);
        Assert.AreEqual(MovementStatus.Completed, advance.Movement.Status);
        Assert.AreEqual(new CellCoord(6,1), session.State.CommittedCell);
        Assert.AreEqual(ExactScalar.Create(49,8), session.State.Anchor.U);
        Assert.AreEqual(ExactScalar.Integer(1), session.State.Anchor.V);
        Assert.AreEqual(MovementPhase.Returning, session.State.Phase);
        Assert.AreEqual(MovementAction.Walk, session.State.Action); Assert.AreEqual(NeighborDirection.XMinus, session.State.Facing);
        CollectionAssert.AreEqual(new[]{"StepStarted@0/1:6,1>7,1","ActionChanged@0/1:6,1>7,1","StepCancelled@2/1:6,1>7,1","FacingChanged@2/1:6,1>7,1"},Trace(advance));
        var finish = session.Advance(SimulationTime.At(4), []);
        CollectionAssert.AreEqual(new[]{"CenterArrived@4/1:6,1>7,1","ActionChanged@4/1:6,1>7,1"},Trace(finish));
        Assert.AreEqual(ExactScalar.Integer(6),session.State.Anchor.U); Assert.AreEqual(MovementAction.Idle,session.State.Action);
        Assert.AreEqual(3,advance.Evidence.Single().Assessment.RecordAssessments.Count);
    }
    [TestMethod]
    [DataRow(4)] [DataRow(6)] [DataRow(8)]
    public void EqualityAndLateReleaseCommitBeforeNeutralAndFinish(int release)
    {
        using var fixture=new GridWireFixture(); var session=Session(fixture);
        var result=session.Advance(SimulationTime.At(8),[Input(0,0,NeighborDirection.XPlus),Input(release,1,null)]);
        CollectionAssert.AreEqual(new[]{"StepStarted@0/1:6,1>7,1","ActionChanged@0/1:6,1>7,1","CellCommitted@4/1:6,1>7,1","CenterArrived@8/1:6,1>7,1","ActionChanged@8/1:6,1>7,1"},Trace(result));
        Assert.AreEqual(new CellCoord(7,1),session.State.CommittedCell); Assert.AreEqual(ExactScalar.Integer(7),session.State.Anchor.U);
        Assert.AreEqual(MovementPhase.Settled,session.State.Phase);
    }
    [TestMethod]
    public void LongReplayAdmitsOnlyAuthoritativePrefixAndMatchesCompletePartitions()
    {
        using var fixture=new GridWireFixture(); var whole=Session(fixture); var split=Session(fixture);
        // X+ arrival at7,1 then latest X- returns to6,1. Y+ was replaced, not queued.
        var inputs=new[]{Input(0,0,NeighborDirection.XPlus),Input(2,1,NeighborDirection.YPlus),Input(3,2,NeighborDirection.XMinus),Input(16,3,null)};
        var result=whole.Advance(SimulationTime.At(16),inputs);
        CollectionAssert.AreEqual(new[]{"StepStarted@0/1:6,1>7,1","ActionChanged@0/1:6,1>7,1","CellCommitted@4/1:6,1>7,1","CenterArrived@8/1:6,1>7,1","StepStarted@8/1:7,1>6,1","FacingChanged@8/1:7,1>6,1","CellCommitted@12/1:7,1>6,1","CenterArrived@16/1:7,1>6,1","ActionChanged@16/1:7,1>6,1"},Trace(result));
        var events=new List<MovementEvent>(); var keys=new List<AdmissionKey>();
        for(var t=0;t<=16;t++) {var part=split.Advance(SimulationTime.At(t),inputs.Where(b=>b.Time==SimulationTime.At(t)).ToArray()); events.AddRange(part.Movement.Events);keys.AddRange(part.Movement.ConsumedAssessmentKeys);}
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(result.Movement.Events),System.Text.Json.JsonSerializer.Serialize(events));
        CollectionAssert.AreEqual(result.Movement.ConsumedAssessmentKeys.ToArray(),keys.ToArray());
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(MovementRuntime.QuerySnapshot(whole.State)),System.Text.Json.JsonSerializer.Serialize(MovementRuntime.QuerySnapshot(split.State)));
        Assert.AreEqual(2,result.Evidence.Count);
    }
    [TestMethod]
    public void ReinputCannotUndoCancellationAndBatchNeutralSuppressesStaleStart()
    {
        using var fixture=new GridWireFixture(); var session=Session(fixture);
        var r=session.Advance(SimulationTime.At(12),[Input(0,0,NeighborDirection.XPlus),Input(2,1,null),Input(3,2,NeighborDirection.YPlus),Input(12,3,null)]);
        CollectionAssert.AreEqual(new[]{"StepStarted@0/1:6,1>7,1","ActionChanged@0/1:6,1>7,1","StepCancelled@2/1:6,1>7,1","FacingChanged@2/1:6,1>7,1","CenterArrived@4/1:6,1>7,1","StepStarted@4/1:6,1>6,2","FacingChanged@4/1:6,1>6,2","CellCommitted@8/1:6,1>6,2","CenterArrived@12/1:6,1>6,2","ActionChanged@12/1:6,1>6,2"},Trace(r));
        var other=Session(fixture);
        var batch=new InputBatch(SimulationTime.At(3),[MovementInput.Set(2,ActorIntent.Toward(NeighborDirection.YPlus)),MovementInput.Set(3,ActorIntent.Neutral)]);
        var suppressed=other.Advance(SimulationTime.At(12),[Input(0,0,NeighborDirection.XPlus),Input(1,1,NeighborDirection.XPlus),Input(2,2,null),batch]);
        Assert.AreEqual(1,suppressed.Movement.Events.Count(e=>e.Kind==MovementEventKind.StepStarted));
        Assert.AreEqual(1,suppressed.Movement.Events.Count(e=>e.Kind==MovementEventKind.StepCancelled));
        Assert.AreEqual(new CellCoord(6,1),other.State.CommittedCell);
    }
    [TestMethod]
    [DataRow(NeighborDirection.XMinus, "ADMITTED_DESTINATION_SCENERY_BLOCKER", AssessmentCoverage.Supported)]
    [DataRow(NeighborDirection.XPlusYPlus, "DIAGONAL_RULE_UNPROVED", AssessmentCoverage.Unknown)]
    public void DenialsAreConcreteAndHeldDoesNotRetry(NeighborDirection direction,string reason,AssessmentCoverage coverage)
    {
        using var fixture=new GridWireFixture();var session=Session(fixture);
        var r=session.Advance(SimulationTime.At(8),[Input(0,0,direction)]);
        Assert.AreEqual(MovementEventKind.StepRejected,r.Movement.Events.Single().Kind);Assert.AreEqual("CoverageDenied",r.Movement.Events.Single().Reason);
        Assert.AreEqual(reason,r.Evidence.Single().Assessment.ReasonCode);
        CollectionAssert.AreEqual(new[]{reason},r.Movement.Events.Single().Assessment!.Reasons.ToArray());
        Assert.AreEqual(coverage,r.Movement.Events.Single().Assessment!.Coverage);
        Assert.AreEqual(new CellCoord(6,1),session.State.CommittedCell); Assert.AreEqual(NeighborDirection.XPlus,session.State.Facing);
        Assert.AreEqual(0,session.Advance(SimulationTime.At(10),[Input(9,1,direction)]).Movement.Events.Count);
    }
    [TestMethod]
    public void ManyGapsAndBlockedWithGapsRetainFullDataOutsidePermitCodes()
    {
        using var fixture=new GridWireFixture(names:false,uncertainRecords:40,tileBlocker:true);var session=Session(fixture);
        var result=session.Advance(SimulationTime.At(1),[Input(0,0,NeighborDirection.XPlus)]);
        var evidence=result.Evidence.Single(); Assert.AreEqual(CardinalTraversalDisposition.Blocked,evidence.Assessment.Disposition);
        Assert.AreEqual(43,evidence.Assessment.RecordAssessments.Count);Assert.IsTrue(evidence.Assessment.Gaps.Count>40);
        Assert.AreSame(evidence.Assessment.Gaps,evidence.Assessment.StepEvidence.Gaps);
        Assert.AreEqual(1,result.Movement.Events.Single().Assessment!.Reasons.Count);
        Assert.AreEqual(2,result.Movement.Events.Single().Assessment!.Provenance.Count);
        Assert.AreEqual(evidence.Key,result.Movement.ConsumedAssessmentKeys.Single());
    }
    [TestMethod]
    public void InvalidTimeSplitBatchAndSelectionFailurePublishNothing()
    {
        using var fixture=new GridWireFixture();var session=Session(fixture);var previous=session.State;
        var invalid=session.Advance(SimulationTime.At(2),[Input(1,0,NeighborDirection.XPlus),Input(1,1,null)]);
        Assert.AreEqual(MovementStatus.InvalidRequest,invalid.Movement.Status);Assert.AreSame(previous,session.State);Assert.AreEqual(0,invalid.Evidence.Count);
        Assert.Throws<InvalidOperationException>(()=>session.Advance(SimulationTime.At(2),[Input(0,0,NeighborDirection.XPlus)],(_,_)=>throw new InvalidOperationException("synthetic selection failure")));
        Assert.AreSame(previous,session.State);Assert.IsNull(session.LastAdvance);
        session.Advance(SimulationTime.At(1),[Input(0,0,NeighborDirection.XPlus)]); previous=session.State;
        var late=session.Advance(SimulationTime.At(2),[Input(1,1,null)]);
        Assert.AreEqual(MovementStatus.InvalidRequest,late.Movement.Status);Assert.AreSame(previous,session.State);Assert.AreEqual(0,late.Movement.Events.Count);
    }
    [TestMethod]
    public void ReplayCapacityLeavesPreviousStateAndNoPublication()
    {
        using var fixture=new GridWireFixture();var session=Session(fixture,new MovementLimits(assessments:1,opportunities:2));var previous=session.State;
        Assert.Throws<GridReplayCapacityException>(()=>session.Advance(SimulationTime.At(16),[Input(0,0,NeighborDirection.XPlus),Input(16,1,null)]));
        Assert.AreSame(previous,session.State);Assert.IsNull(session.LastAdvance);
    }
}
