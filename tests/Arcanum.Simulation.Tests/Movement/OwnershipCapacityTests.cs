using System.Collections;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class OwnershipCapacityTests
{
    [TestMethod]
    public void M6_DefensiveOwnershipAllNestedInputsAndReturnedViews()
    {
        var entries=new[]{Press(0)};var batch=new InputBatch(T(0),entries);
        var reasons=new[]{"SYNTHETIC_OPEN"};var provenance=new[]{"SYNTHETIC_POLICY"};
        var record=new StepAssessmentTiming(Key(),AssessmentCoverage.Supported,AssessmentDisposition.Open,StepDuration.Of(8),reasons,provenance);
        var batches=new[]{batch,Batch(2,Neutral(1))};var records=new[]{record};var request=Request(4,batches,records);
        entries[0]=Neutral(0);batches[0]=Batch(0,Neutral(0));records[0]=Open(Key() with{EvidenceRevision=99});
        reasons[0]="MUTATED";provenance[0]="MUTATED";
        var previous=Initial();var before=MovementRuntime.QuerySnapshot(previous);
        var r=Complete(previous,request);var again=Complete(previous,request);
        Transcript(r.Events,[record],"0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus 4:R:1 4:A:1:Walk>Idle");
        CollectionAssert.AreEqual(r.Events.ToArray(),again.Events.ToArray());Assert.AreEqual(r.State!.Descriptor,again.State!.Descriptor);
        Assert.AreEqual(before,MovementRuntime.QuerySnapshot(previous));
        foreach(var view in new object[]{request.Batches,request.Assessments,batch.Entries,record.Reasons,record.Provenance,
            r.Events,r.ConsumedAssessmentKeys,r.UnusedAssessments})
        {
            Assert.IsFalse(view is Array);var list=(IList)view;Assert.IsTrue(list.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(()=>list.Clear());
        }
        var snapshot=MovementRuntime.QuerySnapshot(r.State);var detached=snapshot with{Anchor=new(Q(100),Q(100))};
        Assert.AreNotEqual(snapshot.Anchor,detached.Anchor);Assert.AreEqual(snapshot.Anchor,r.State.Anchor);
        Complete(r.State,Request(8));Assert.AreEqual(snapshot,MovementRuntime.QuerySnapshot(r.State));
        Assert.IsFalse(typeof(MovementState).GetProperties().Any(p=>p.SetMethod is {IsPublic:true}));
        Assert.IsFalse(typeof(ActiveSegment).GetProperties().Any(p=>p.SetMethod is {IsPublic:true}));
        Assert.IsFalse(typeof(MovementRequest).GetProperties().Any(p=>p.SetMethod is {IsPublic:true}));
    }

    [TestMethod]
    public void M6_HardOwnershipCapsAndExactBoundaryCounts()
    {
        var neutral=Enumerable.Range(0,4096).Select(i=>Neutral(i)).ToArray();
        var entries=new InputBatch(T(0),neutral);Complete(Initial(),Request(0,[entries]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new InputBatch(T(0),new MovementInput[4097]));
        var batches=Enumerable.Range(0,4096).Select(i=>Batch(i,Neutral(0))).ToArray();
        Complete(Initial(),Request(4095,batches));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>Request(4096,batches.Concat(new[]{Batch(4096,Neutral(0))}).ToArray()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>Request(1,[entries,Batch(1,Neutral(0))]));
        var records=Enumerable.Range(0,1024).Select(i=>Open(Key(i+1,1,start:i))).ToArray();
        var unused=Complete(Initial(),Request(1023,assessments:records));Assert.AreEqual(1024,unused.UnusedAssessments.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>Request(1024,assessments:records.Concat(new[]{Open(Key(1025,1,start:1024))}).ToArray()));
        var codes=Enumerable.Repeat(new string('P',256),16).ToArray();
        var evidence=new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,codes,codes);
        Complete(Initial(),Request(0,assessments:[evidence]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,new string[17],codes));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,codes,new string[17]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,[new string('X',257)],codes));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new StepAssessmentTiming(Key(),AssessmentCoverage.Unknown,null,null,codes,[new string('X',257)]));
        MovementState.CreateSettled(Binding with{ActorId=new string('A',256)},new(0,0),T(0),NeighborDirection.XPlus);
        Assert.ThrowsExactly<ArgumentException>(()=>MovementState.CreateSettled(Binding with{ActorId=new string('A',257)},new(0,0),T(0),NeighborDirection.XPlus));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new MovementLimits(events:8193));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new MovementLimits(opportunities:1025));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new MovementLimits(inputs:4097));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new MovementLimits(batches:4097));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>new MovementLimits(assessments:1025));
        var opportunities=Enumerable.Range(0,1024).Select(i=>i==0?Batch(0,Press(0)):Batch(i,Neutral(0),Press(1))).ToArray();
        var many=Complete(Initial(),Request(1023,opportunities));Assert.AreEqual(1024,many.Events.Count);
        Assert.AreEqual(1025L,many.State!.NextOpportunityId);Assert.AreEqual(1L,many.State.NextStepId);
        Invalid(Initial(),Request(1024,opportunities.Concat(new[]{Batch(1024,Neutral(0),Press(1))}).ToArray()),MovementFailureKind.CapacityExceeded);
    }

    [TestMethod]
    public void M6_LoweredCapsAreWholeAtomicNotPartialAndPartitionsMayFit()
    {
        var a=Open(Key());
        foreach(var limits in new[]{new MovementLimits(batches:1),new MovementLimits(inputs:1)})
            Invalid(Initial(limits:limits),Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[a]),MovementFailureKind.CapacityExceeded);
        var b=Open(Key(2,2,new(1,0),new(2,0),start:8));
        Invalid(Initial(limits:new MovementLimits(assessments:1)),Request(8,[Batch(0,Press(0))],[a,b]),MovementFailureKind.CapacityExceeded);
        var previous=Initial(limits:new MovementLimits(events:4));var request=Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[a]);
        Invalid(previous,request,MovementFailureKind.CapacityExceeded); // after hypothetical commit+arrival, Idle would be event5
        var p1=Complete(previous,Request(4,[Batch(0,Press(0))],[a]));var p2=Complete(p1.State!,Request(8,[Batch(8,Neutral(1))]));
        Transcript(p1.Events.Concat(p2.Events).ToArray(),[a],"0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:A:1:Walk>Idle");
        var exact=Complete(Initial(limits:new MovementLimits(events:5)),request);Assert.AreEqual(5,exact.Events.Count);
        var one=Initial(limits:new MovementLimits(opportunities:1));
        Invalid(one,Request(8,[Batch(0,Press(0))],[a]),MovementFailureKind.CapacityExceeded);
        Complete(one,Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[a]));
        // Returning/re-input output also observes the same hard event budget after prior hypothetical cancellation.
        var ret=Open(Key(2,2,new(0,0),new(0,1),NeighborDirection.YPlus,4,3,2));
        Invalid(Initial(limits:new MovementLimits(events:6)),Request(12,[Batch(0,Press(0)),Batch(2,Neutral(1)),Batch(3,Press(2,NeighborDirection.YPlus)),Batch(12,Neutral(3))],[a,ret]),MovementFailureKind.CapacityExceeded);
    }

    [TestMethod]
    public void M6_TinyDurationHugeTargetFiniteAdmissionAndCarriedWork()
    {
        var a=Open(Key(),StepDuration.Of(1,1000000));
        var r=Complete(Initial(),Request(long.MaxValue,[Batch(0,Press(0))],[a]));
        Assert.AreEqual(6,r.Events.Count);Assert.AreEqual(2L,r.State!.NextStepId);Assert.AreEqual(3L,r.State.NextOpportunityId);
        Assert.AreEqual(T(long.MaxValue),r.State.Time);Assert.AreEqual(new CellCoord(1,0),r.State.CommittedCell);
        Assert.AreEqual("MissingAssessment",r.Events[4].Reason);Assert.AreEqual(MovementAction.Idle,r.State.Action);
        Assert.AreEqual(new MovementEvent(T(1,2000000),3,MovementEventKind.CellCommitted,a.Key,null,a),r.Events[2]);
        Assert.AreEqual(new MovementEvent(T(1,1000000),4,MovementEventKind.CenterArrived,a.Key,null,a),r.Events[3]);
        var carried=Complete(Initial(),Request(0,[Batch(0,Press(0))],[a])).State!;
        var finish=Complete(carried,Request(long.MaxValue));Assert.AreEqual(4,finish.Events.Count);
        Assert.IsNull(finish.State!.Segment);Assert.AreEqual(0,finish.UnusedAssessments.Count);
        Assert.IsFalse(typeof(MovementState).GetProperties().Any(p=>p.PropertyType!=typeof(string)&&typeof(IEnumerable).IsAssignableFrom(p.PropertyType)));
        var assembly=typeof(MovementRuntime).Assembly;
        Assert.IsFalse(assembly.GetReferencedAssemblies().Any(n=>n.Name!.StartsWith("Arcanum.")||n.Name.StartsWith("Godot")));
        foreach(var method in typeof(MovementRuntime).GetMethods().Where(m=>m.DeclaringType==typeof(MovementRuntime)))
            Assert.IsFalse(method.GetParameters().Any(p=>p.ParameterType==typeof(double)||p.ParameterType==typeof(float)));
    }
}
