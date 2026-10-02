using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class ValidationTests
{
    [TestMethod]
    public void M5_MalformedInternalDescriptorsUseSameProductionValidator()
    {
        var initial=Initial();var a=Open(Key());
        var before=Complete(initial,Request(1,[Batch(0,Press(0))],[a])).State!.Descriptor;
        var after=Complete(initial,Request(4,[Batch(0,Press(0))],[a])).State!.Descriptor;
        var returning=Complete(initial,Request(3,[Batch(0,Press(0)),Batch(2,Neutral(1))],[a])).State!.Descriptor;
        var malformed=new[]{
            initial.Descriptor with{Time=default},initial.Descriptor with{Phase=(MovementPhase)99},
            initial.Descriptor with{Facing=(NeighborDirection)99},initial.Descriptor with{Anchor=new(default,Q(0))},
            initial.Descriptor with{Action=(MovementAction)99},initial.Descriptor with{LatestInputKey=new(T(0),-1)},
            initial.Descriptor with{Anchor=new(Q(1,2),Q(0))},initial.Descriptor with{Action=MovementAction.Walk},
            initial.Descriptor with{Segment=new ActiveSegment(a)},initial.Descriptor with{NextStepId=0},
            initial.Descriptor with{NextOpportunityId=0},initial.Descriptor with{NextEventOrdinal=0},
            initial.Descriptor with{NextStepId=2},initial.Descriptor with{EvidenceRevision=-1},
            initial.Descriptor with{Binding=Binding with{EvidenceEpoch=-1}},
            initial.Descriptor with{LatestIntent=new(true,null)},initial.Descriptor with{LatestIntent=new(false,NeighborDirection.XPlus)},
            initial.Descriptor with{LatestInputKey=new(T(1),0)},initial.Descriptor with{MaterialIntentKey=new(T(0),0)},
            before with{CommittedCell=new(1,0)},before with{Phase=MovementPhase.OutboundAfterEntry},
            before with{Facing=NeighborDirection.YPlus},before with{Action=MovementAction.Idle},
            before with{Segment=null},before with{NextStepId=1},before with{NextOpportunityId=1},
            before with{Segment=new ActiveSegment(null!)},
            before with{Anchor=new(Q(1,4),Q(0))},before with{MaterialIntentKey=new(T(1),2)},
            before with{Segment=new ActiveSegment(Open(a.Key with{Binding=Binding with{ContextId="OTHER"}}))},
            before with{Segment=new ActiveSegment(Open(a.Key with{To=new(2,0)}))},
            before with{Segment=new ActiveSegment(Open(a.Key,StepDuration.Of(0)))},
            before with{Segment=new ActiveSegment(Open(a.Key with{StartTime=T(2)}))},
            before with{LastOpportunityDisposition="Denied"},before with{LastOpportunity=null},
            after with{CommittedCell=new(0,0)},after with{Phase=MovementPhase.OutboundBeforeEntry},
            returning with{Segment=new ActiveSegment(a)},returning with{CommittedCell=new(1,0)},
            returning with{Facing=NeighborDirection.XPlus},returning with{Action=MovementAction.Idle},
            returning with{Anchor=new(Q(-1,8),Q(0))},returning with{Anchor=new(Q(1,2),Q(0))},
            returning with{Segment=new ActiveSegment(a,T(4))},returning with{Segment=new ActiveSegment(a,T(0))},
            returning with{Time=T(4)},returning with{Time=T(1)},returning with{EvidenceRevision=-1}
        };
        foreach(var descriptor in malformed)
        {
            var bad=MovementState.FromDescriptor(descriptor);
            Assert.IsFalse(MovementValidation.IsValidState(bad),descriptor.ToString());
            Invalid(bad,Request(8),MovementFailureKind.InvalidState);
            Assert.ThrowsExactly<ArgumentException>(()=>MovementRuntime.QuerySnapshot(bad));
        }
        Invalid(null,Request(8),MovementFailureKind.InvalidState);
        Invalid(MovementState.FromDescriptor(null!),Request(8),MovementFailureKind.InvalidState);
        Assert.ThrowsExactly<ArgumentException>(()=>MovementRuntime.QuerySnapshot(null!));
    }

    [TestMethod]
    public void M5_RequestOrderDirectionNumericAndClosedNegatives()
    {
        var state=Initial();var a=Open(Key());
        var requests=new[]{
            new MovementRequest(default,[],[]),Request(-1),Request(1,[Batch(2,Neutral(0))]),
            Request(2,[Batch(1,Neutral(-1))]),Request(2,[Batch(1,Neutral(1),Neutral(1))]),
            Request(2,[Batch(1,Neutral(2),Neutral(1))]),Request(2,[Batch(1,Neutral(0)),Batch(1,Neutral(1))]),
            Request(2,[Batch(2,Neutral(0)),Batch(1,Neutral(1))]),Request(2,[new InputBatch(T(1),[])]),
            Request(2,[new InputBatch(default,[Neutral(0)])]),Request(2,[Batch(0,MovementInput.Set(0,new(true,null)))]),
            Request(2,[Batch(0,MovementInput.Set(0,new(false,NeighborDirection.XPlus)))]),
            Request(2,[Batch(0,Press(0,(NeighborDirection)99))]),
            Request(2,[Batch(0,new MovementInput(0,(MovementInputKind)99,ActorIntent.Neutral,0))]),
            Request(2,[Batch(0,new MovementInput(0,MovementInputKind.SetActorIntent,ActorIntent.Neutral,1))]),
            Request(2,[Batch(0,new MovementInput(0,MovementInputKind.RequestReassessment,ActorIntent.Toward(NeighborDirection.XPlus),1))])
        };
        foreach(var request in requests) Invalid(state,request);
        Assert.IsNull(MovementRuntime.AdvanceTo(state,new(default,[],[])).UnconsumedTime);
        Assert.IsNull(MovementRuntime.AdvanceTo(state,Request(-1)).UnconsumedTime);
        Assert.AreEqual(Q(2),MovementRuntime.AdvanceTo(state,Request(2,[Batch(2,Neutral(-1))])).UnconsumedTime);
        foreach(var duration in new[]{default(StepDuration),StepDuration.Of(0),StepDuration.Of(-1)})
            Invalid(state,Request(0,[Batch(0,Press(0))],[Open(Key(),duration)]),MovementFailureKind.InvalidAssessment);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>ExactScalar.Create(1,0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>ExactScalar.Create(1,-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>SimulationTime.At(1,0));
        Assert.IsFalse(default(ExactScalar).IsValid);
        Assert.AreEqual(Q(0),ExactScalar.Create(0,Int128.MaxValue));
        Assert.AreEqual(Q(-1,2),ExactScalar.Create(-2,4));
        Assert.ThrowsExactly<ArgumentException>(()=>MovementState.CreateSettled(Binding,new(0,0),T(-1),NeighborDirection.XPlus));
        Invalid(Initial(new(int.MaxValue,0)),Request(0,[Batch(0,Press(0))]),MovementFailureKind.NumericOverflow);
        Invalid(Initial(new(int.MinValue,0)),Request(0,[Batch(0,Press(0,NeighborDirection.XMinus))]),MovementFailureKind.NumericOverflow);
        Invalid(Initial(new(0,int.MaxValue)),Request(0,[Batch(0,Press(0,NeighborDirection.YPlus))]),MovementFailureKind.NumericOverflow);
        Invalid(Initial(new(0,int.MinValue)),Request(0,[Batch(0,Press(0,NeighborDirection.YMinus))]),MovementFailureKind.NumericOverflow);
        var closed=Complete(state,Request(0,[Batch(0,Press(0))],[a])).State!;
        Invalid(closed,Request(1,[Batch(0,Neutral(1))]),MovementFailureKind.InvalidInput);
    }

    [TestMethod]
    public void M5_LateMismatchAfterHypotheticalCommitRollsBackEveryFieldAndCorrectedRetry()
    {
        var previous=Initial();var first=Open(Key());var second=Open(Key(2,2,new(1,0),new(2,0),start:8));
        var bad=Open(second.Key with{EvidenceRevision=1});
        Invalid(previous,Request(16,[Batch(0,Press(0)),Batch(16,Neutral(1))],[first,bad]),MovementFailureKind.IdentityMismatch);
        var malformedLate=new StepAssessmentTiming(second.Key,AssessmentCoverage.Unknown,null,null,[],["POLICY"]);
        Invalid(previous,Request(16,[Batch(0,Press(0)),Batch(16,Neutral(1))],[first,malformedLate]),MovementFailureKind.InvalidAssessment);
        // A semantic late input is discovered only after earlier private start/commit/arrival.
        Invalid(previous,Request(16,[Batch(0,Press(0)),Batch(9,MovementInput.Reassess(1,1))],[first,second]),MovementFailureKind.WrongReassessmentPhase);
        var retry=Complete(previous,Request(16,[Batch(0,Press(0)),Batch(16,Neutral(1))],[first,second]));
        Transcript(retry.Events,[first,second],"0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:S:2 12:C:2 16:R:2 16:A:2:Walk>Idle");
        Assert.AreEqual(new CellCoord(2,0),retry.State!.CommittedCell);
        Assert.AreEqual(T(0),previous.Time);Assert.IsFalse(previous.IsClosed);
    }

    [TestMethod]
    public void M5_ReturnArrivalOverflowWithRepresentableReleaseAnchor()
    {
        // Python Fraction search independently supplies literals; all outbound times and q=1/5 fit.
        var start=SimulationTime.At(Int128.Parse("4253529586511730793292182592897102643"),19);
        var duration=StepDuration.Of(Int128.Parse("2835686391007820528861455061931401762"),5);
        var release=SimulationTime.At(Int128.Parse("160216281091941859880672210999124199553"),475);
        var initial=MovementState.CreateSettled(Binding,new(0,0),start,NeighborDirection.XPlus);
        var key=Key() with{StartTime=start,MaterialIntentKey=new(start,0)};
        var a=Open(key,duration);
        var observation=Complete(initial,new(release,[new(start,[Press(0)])],[a]));
        Assert.AreEqual(new WorldAnchor(Q(1,5),Q(0)),observation.State!.Anchor);
        Invalid(initial,new(release,[new(start,[Press(0)]),new(release,[Neutral(1)])],[a]),MovementFailureKind.NumericOverflow);
    }

    [TestMethod]
    public void M5_ExactArithmeticTimeAnchorResidualAndCounterOverflowAreAtomic()
    {
        Assert.ThrowsExactly<OverflowException>(()=>ExactScalar.Integer(Int128.MaxValue)+Q(1));
        Assert.ThrowsExactly<OverflowException>(()=>ExactScalar.Integer(Int128.MinValue)-Q(1));
        var initial=Initial();
        Invalid(initial,new(new(ExactScalar.Create(1,Int128.MaxValue)),[Batch(0,Press(0))],[Open(Key())]),MovementFailureKind.NumericOverflow);
        var atMax=MovementState.CreateSettled(Binding,new(0,0),SimulationTime.At(Int128.MaxValue),NeighborDirection.XPlus);
        var key=Key() with{StartTime=atMax.Time,MaterialIntentKey=new(atMax.Time,0)};
        Invalid(atMax,new(atMax.Time,[new(atMax.Time,[Press(0)])],[Open(key,StepDuration.Of(1))]),MovementFailureKind.NumericOverflow);
        Invalid(initial,Request(0,[Batch(0,Press(0))],[Open(Key(),StepDuration.Of(1,Int128.MaxValue))]),MovementFailureKind.NumericOverflow);
        var missing=Complete(initial,Request(0,[Batch(0,Press(0))])).State!;
        foreach(var descriptor in new[]{
            initial.Descriptor with{NextEventOrdinal=long.MaxValue},
            initial.Descriptor with{NextEventOrdinal=long.MaxValue-3},
            missing.Descriptor with{NextOpportunityId=long.MaxValue,LastOpportunity=Key(long.MaxValue-1)},
            missing.Descriptor with{NextStepId=long.MaxValue,NextOpportunityId=long.MaxValue,LastOpportunity=Key(long.MaxValue-1,long.MaxValue-1)}
        })
        {
            var state=MovementState.FromDescriptor(descriptor);Assert.IsTrue(MovementValidation.IsValidState(state));
            if(descriptor.NextEventOrdinal==long.MaxValue-3)
                Invalid(state,Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[Open(Key())]),MovementFailureKind.NumericOverflow);
            else Invalid(state,Request(1,[Batch(1,Neutral(0),Press(1))]),MovementFailureKind.NumericOverflow);
        }
        var tiny=MovementState.CreateSettled(Binding,new(0,0),SimulationTime.At(1,Int128.MaxValue),NeighborDirection.XPlus);
        var invalid=MovementRuntime.AdvanceTo(tiny,new(SimulationTime.At(Int128.MaxValue),[new(tiny.Time,[Neutral(-1)])],[]));
        Assert.AreEqual(MovementStatus.InvalidRequest,invalid.Status);Assert.AreSame(tiny,invalid.State);
        Assert.IsNull(invalid.UnconsumedTime); // reduced residual numerator exceeds Int128
        // Late anchor overflow after an earlier hypothetical commit: second segment has a different denominator.
        var first=Open(Key());var second=Open(Key(2,2,new(1,0),new(2,0),start:8),StepDuration.Of(32));
        var time=new SimulationTime(ExactScalar.Create(checked(8*(Int128.MaxValue/16)+1),Int128.MaxValue/16));
        Invalid(initial,new(time,[Batch(0,Press(0))],[first,second]),MovementFailureKind.NumericOverflow);
    }
}
