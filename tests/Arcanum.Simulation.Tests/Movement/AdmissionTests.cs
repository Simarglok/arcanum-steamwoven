using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class AdmissionTests
{
    [TestMethod]
    [DataRow(AssessmentCoverage.Supported, AssessmentDisposition.Blocked)]
    [DataRow(AssessmentCoverage.Unknown, null)]
    [DataRow(AssessmentCoverage.Unsupported, null)]
    public void M4_CanonicalDenialsRetainEvidenceAndCannotRotate(AssessmentCoverage coverage, AssessmentDisposition? disposition)
    {
        var k=Key(to:new(1,1),direction:NeighborDirection.XPlusYPlus);
        var a=new StepAssessmentTiming(k,coverage,disposition,null,["DIAGONAL_RULE_UNPROVED"],["SYNTHETIC_POLICY"]);
        var r=Complete(Initial(),Request(100,[Batch(0,Press(0,NeighborDirection.XPlusYPlus)),Batch(1,Press(1,NeighborDirection.XPlusYPlus))],[a]));
        Transcript(r.Events,[a],"0:D:1");
        Assert.AreEqual(new CellCoord(0,0),r.State!.CommittedCell);
        Assert.AreEqual(new WorldAnchor(Q(0),Q(0)),r.State.Anchor);
        Assert.AreEqual(NeighborDirection.XPlus,r.State.Facing); Assert.AreEqual(MovementAction.Idle,r.State.Action);
        Assert.AreEqual(2L,r.State.NextOpportunityId); Assert.AreEqual(1L,r.State.NextStepId);
        Assert.AreEqual(new InputKey(T(0),0),r.State.MaterialIntentKey); Assert.AreEqual(new InputKey(T(1),1),r.State.LatestInputKey);
        CollectionAssert.AreEqual(new[]{k},r.ConsumedAssessmentKeys.ToArray());
        Assert.AreEqual(0,Complete(r.State,Request(200)).Events.Count);
    }

    [TestMethod]
    public void M4_MissingIsOnePolicyRejectionAndExplicitNewerReassessment()
    {
        var r=Complete(Initial(),Request(2,[Batch(0,Press(0)),Batch(1,Press(1))]));
        Assert.AreEqual(new MovementEvent(T(0),1,MovementEventKind.StepRejected,Key(),"MissingAssessment",null),r.Events.Single());
        CollectionAssert.AreEqual(new[]{"PROJECT_POLICY"},r.Events[0].Provenance.ToArray());
        Assert.AreEqual(2L,r.State!.NextOpportunityId); Assert.AreEqual(1L,r.State.NextStepId);
        Assert.AreEqual(0,Complete(r.State,Request(3,[Batch(3,Press(2))])).Events.Count);
        var key=Key(2,1,start:4,revision:1);
        var a=Open(key);
        var reassessed=Complete(r.State,Request(4,[Batch(4,MovementInput.Reassess(2,1))],[a]));
        Transcript(reassessed.Events,[a],"4:S:1 4:A:1:Idle>Walk",2);
        Assert.AreEqual(1L,reassessed.State!.EvidenceRevision);
        Invalid(r.State,Request(4,[Batch(4,MovementInput.Reassess(2,0))]),MovementFailureKind.InvalidRevision);
        Invalid(reassessed.State,Request(5,[Batch(5,MovementInput.Reassess(3,2))]),MovementFailureKind.WrongReassessmentPhase);
        var returning=Complete(Initial(),Request(2,[Batch(0,Press(0)),Batch(2,Neutral(1))],[Open(Key())])).State!;
        Invalid(returning,Request(3,[Batch(3,MovementInput.Reassess(2,1))]),MovementFailureKind.WrongReassessmentPhase);
        var material=Open(Key(2,1,to:new(0,1),direction:NeighborDirection.YPlus,start:4,materialTime:4,materialSequence:2));
        var turn=Complete(r.State,Request(4,[Batch(4,Press(2,NeighborDirection.YPlus))],[material]));
        Transcript(turn.Events,[material],"4:S:1 4:F:1:XPlus>YPlus 4:A:1:Idle>Walk",2);
        var repress=Open(Key(2,1,start:4,materialTime:4,materialSequence:3));
        var again=Complete(r.State,Request(4,[Batch(4,Neutral(2),Press(3))],[repress]));
        Transcript(again.Events,[repress],"4:S:1 4:A:1:Idle>Walk",2);
    }

    [TestMethod]
    public void M4_ArrivalMaterialAndReassessmentCoalesceOneOpportunity()
    {
        var a=Open(Key());
        var b=Open(Key(2,2,new(1,0),new(1,1),NeighborDirection.YPlus,8,8,2,1));
        var r=Complete(Initial(),Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1),Press(2,NeighborDirection.YPlus),MovementInput.Reassess(3,1))],[a,b]));
        Transcript(r.Events,[a,b],"0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:S:2 8:F:2:XPlus>YPlus");
        Assert.AreEqual(3L,r.State!.NextOpportunityId); Assert.AreEqual(3L,r.State.NextStepId);
        Assert.AreEqual(new InputKey(T(8),2),r.State.MaterialIntentKey); Assert.AreEqual(new InputKey(T(8),3),r.State.LatestInputKey);
        var denied=new StepAssessmentTiming(Key(2,2,new(1,0),new(2,0),start:8),AssessmentCoverage.Unknown,null,null,["UNKNOWN"],["SYNTHETIC_POLICY"]);
        var d=Complete(Initial(),Request(8,[Batch(0,Press(0))],[a,denied]));
        Transcript(d.Events,[a,denied],"0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:D:2 8:A:2:Walk>Idle");
        Assert.AreEqual(2L,d.State!.NextStepId);
    }

    [TestMethod]
    public void M4_AllIdentityFieldsMutatedIndividuallyAreAtomicNotCoverage()
    {
        var key=Key(); var b=Binding;
        var mutations=new[]{
            key with{From=new(-1,0),To=new(0,0)},key with{To=new(0,1),Direction=NeighborDirection.YPlus},
            key with{Direction=NeighborDirection.XMinus,To=new(-1,0)},
            key with{Binding=b with{ActorId="OTHER"}},key with{Binding=b with{ChartId="OTHER"}},
            key with{Binding=b with{ContextId="OTHER"}},key with{Binding=b with{CapabilitiesProfileId="OTHER"}},
            key with{Binding=b with{CapabilitiesFingerprint="OTHER"}},key with{Binding=b with{AssessmentProfileId="OTHER"}},
            key with{Binding=b with{InputFingerprint="OTHER"}},key with{Binding=b with{EvidenceEpoch=8}},
            key with{OpportunityId=2},key with{ProposedStepId=2},key with{StartTime=T(1)},
            key with{MaterialIntentKey=new(T(0),1)},key with{EvidenceRevision=1}
        };
        foreach(var mutated in mutations)
            Invalid(Initial(),Request(8,[Batch(0,Press(0))],[Open(mutated)]),MovementFailureKind.IdentityMismatch);
        // Malformed endpoint-only mutation must still reject, not be recast as missing evidence.
        Invalid(Initial(),Request(8,[Batch(0,Press(0))],[Open(key with{To=new(2,0)})]),MovementFailureKind.InvalidAssessment);
    }

    [TestMethod]
    public void M4_AllCanonicalPairingsTimingAndUnusedValidation()
    {
        foreach(var coverage in Enum.GetValues<AssessmentCoverage>())
        foreach(var disposition in new AssessmentDisposition?[]{null,AssessmentDisposition.Open,AssessmentDisposition.Blocked})
        {
            var a=new StepAssessmentTiming(Key(),coverage,disposition,StepDuration.Of(8),["FIXTURE"],["SYNTHETIC_POLICY"]);
            var canonical=coverage==AssessmentCoverage.Supported?disposition!=null:disposition==null;
            if(canonical) Complete(Initial(),Request(0,[Batch(0,Press(0))],[a]));
            else Invalid(Initial(),Request(0,[Batch(0,Press(0))],[a]),MovementFailureKind.InvalidAssessment);
        }
        var malformed=new[]{
            new StepAssessmentTiming(Key(),AssessmentCoverage.Supported,AssessmentDisposition.Open,null,["F"],["P"]),
            new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,StepDuration.Of(0),["F"],["P"]),
            new StepAssessmentTiming(Key(),(AssessmentCoverage)99,null,null,["F"],["P"]),
            new StepAssessmentTiming(Key(),AssessmentCoverage.Supported,(AssessmentDisposition)99,null,["F"],["P"]),
            new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,[],["P"]),
            new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,["F"],[]),
            new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,[""],["P"]),
            new StepAssessmentTiming(default,AssessmentCoverage.Unknown,null,null,["F"],["P"])
        };
        foreach(var a in malformed) Invalid(Initial(),Request(8,assessments:[a])); // UNUSED malformed still invalid
        var first=Open(Key()); var prospective=Open(Key(2,2,new(1,0),new(2,0),start:8));
        var r=Complete(Initial(),Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[first,prospective]));
        CollectionAssert.AreEqual(new[]{first.Key},r.ConsumedAssessmentKeys.ToArray());
        CollectionAssert.AreEqual(new[]{prospective},r.UnusedAssessments.ToArray());
        Invalid(r.State,Request(9,assessments:[prospective]),MovementFailureKind.InvalidAssessment);
        Invalid(Initial(),Request(8,assessments:[Open(prospective.Key with{Binding=Binding with{ActorId="FOREIGN"}})]),MovementFailureKind.IdentityMismatch);
        Invalid(Initial(),Request(0,assessments:[first,first]),MovementFailureKind.InvalidAssessment);
        Invalid(Initial(),Request(7,assessments:[prospective]),MovementFailureKind.InvalidAssessment);
        var started=Complete(Initial(),Request(0,[Batch(0,Press(0))],[first])).State!;
        Invalid(started,Request(1,assessments:[first]),MovementFailureKind.InvalidAssessment);
    }

    [TestMethod]
    public void M4_UnusedDispositionPartitionEqualityAndNoRecordRetention()
    {
        var a=Open(Key());var b=Open(Key(2,2,new(1,0),new(2,0),start:8));var initial=Initial();
        var whole=Complete(initial,Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[a,b]));
        var p1=Complete(initial,Request(4,[Batch(0,Press(0))],[a]));
        var p2=Complete(p1.State!,Request(8,[Batch(8,Neutral(1))],[b]));
        CollectionAssert.AreEqual(whole.Events.ToArray(),p1.Events.Concat(p2.Events).ToArray());
        CollectionAssert.AreEqual(whole.ConsumedAssessmentKeys.ToArray(),p1.ConsumedAssessmentKeys.Concat(p2.ConsumedAssessmentKeys).ToArray());
        CollectionAssert.AreEqual(whole.UnusedAssessments.ToArray(),p1.UnusedAssessments.Concat(p2.UnusedAssessments).ToArray());
        Assert.AreEqual(whole.State!.Descriptor,p2.State!.Descriptor);
        Assert.AreEqual(0,Complete(p2.State,Request(9)).UnusedAssessments.Count);
    }
}
