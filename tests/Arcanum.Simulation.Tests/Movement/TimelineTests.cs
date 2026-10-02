using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class TimelineTests
{
    private sealed record Scenario(string Name, int Target, InputBatch[] Batches, StepAssessmentTiming[] Records,
        CellCoord FinalCell, NeighborDirection Facing, string Events);

    private static Scenario[] Cases()
    {
        var x = Open(Key());
        var minus = Open(Key(2, 2, new(1, 0), new(0, 0), NeighborDirection.XMinus, 8, 3, 2));
        var yReturn = Open(Key(2, 2, new(0, 0), new(0, 1), NeighborDirection.YPlus, 4, 3, 2));
        var ySame = Open(Key(2, 2, new(0, 0), new(0, 1), NeighborDirection.YPlus, 4, 2, 2));
        var yFinish = Open(Key(2, 2, new(1, 0), new(1, 1), NeighborDirection.YPlus, 8, 6, 2));
        return [
            new("press-release-zero", 0, [Batch(0, Press(0), Neutral(1))], [], new(0,0), NeighborDirection.XPlus, ""),
            new("press-release-long", 8, [Batch(0, Press(0), Neutral(1))], [], new(0,0), NeighborDirection.XPlus, ""),
            new("held-repeat-neutral", 8, [Batch(0, Press(0)), Batch(1, Press(1)), Batch(2, Neutral(2)), Batch(3, Neutral(3))], [x], new(0,0), NeighborDirection.XMinus,
                "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus 4:R:1 4:A:1:Walk>Idle"),
            new("entry-release", 8, [Batch(0, Press(0)), Batch(4, Neutral(1))], [x], new(1,0), NeighborDirection.XPlus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:A:1:Walk>Idle"),
            new("late-release", 16, [Batch(0, Press(0)), Batch(6, Neutral(1))], [x], new(1,0), NeighborDirection.XPlus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:A:1:Walk>Idle"),
            new("arrival-release", 16, [Batch(0, Press(0)), Batch(8, Neutral(1))], [x], new(1,0), NeighborDirection.XPlus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:A:1:Walk>Idle"),
            new("latest-opposite-not-queued", 16, [Batch(0, Press(0)), Batch(2, Press(1, NeighborDirection.YPlus)), Batch(3, Press(2, NeighborDirection.XMinus)), Batch(16, Neutral(3))], [x,minus], new(0,0), NeighborDirection.XMinus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:S:2 8:F:2:XPlus>XMinus 12:C:2 16:R:2 16:A:2:Walk>Idle"),
            new("return-reinput-latched", 12, [Batch(0, Press(0)), Batch(2, Neutral(1)), Batch(3, Press(2, NeighborDirection.YPlus)), Batch(12, Neutral(3))], [x,yReturn], new(0,1), NeighborDirection.YPlus,
                "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus 4:R:1 4:S:2 4:F:2:XMinus>YPlus 8:C:2 12:R:2 12:A:2:Walk>Idle"),
            new("return-reinput-neutral", 8, [Batch(0, Press(0)), Batch(2, Neutral(1)), Batch(3, Press(2, NeighborDirection.YPlus), Neutral(3))], [x], new(0,0), NeighborDirection.XMinus,
                "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus 4:R:1 4:A:1:Walk>Idle"),
            new("finish-reinput", 16, [Batch(0, Press(0)), Batch(4, Neutral(1)), Batch(6, Press(2, NeighborDirection.YPlus)), Batch(16, Neutral(3))], [x,yFinish], new(1,1), NeighborDirection.YPlus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:S:2 8:F:2:XPlus>YPlus 12:C:2 16:R:2 16:A:2:Walk>Idle"),
            new("finish-reinput-neutral", 16, [Batch(0, Press(0)), Batch(6, Neutral(1)), Batch(7, Press(2, NeighborDirection.YPlus), Neutral(3))], [x], new(1,0), NeighborDirection.XPlus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:A:1:Walk>Idle"),
            new("same-time-neutral-then-press", 12, [Batch(0, Press(0)), Batch(2, Neutral(1), Press(2, NeighborDirection.YPlus)), Batch(12, Neutral(3))], [x,ySame], new(0,1), NeighborDirection.YPlus,
                "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus 4:R:1 4:S:2 4:F:2:XMinus>YPlus 8:C:2 12:R:2 12:A:2:Walk>Idle"),
            new("same-time-press-then-neutral", 12, [Batch(0, Press(0)), Batch(2, Press(1, NeighborDirection.YPlus), Neutral(2))], [x], new(0,0), NeighborDirection.XMinus,
                "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus 4:R:1 4:A:1:Walk>Idle"),
            new("continuous-three-step",24,[Batch(0,Press(0)),Batch(24,Neutral(1))],
                [x,Open(Key(2,2,new(1,0),new(2,0),start:8)),Open(Key(3,3,new(2,0),new(3,0),start:16))],new(3,0),NeighborDirection.XPlus,
                "0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:S:2 12:C:2 16:R:2 16:S:3 20:C:3 24:R:3 24:A:3:Walk>Idle"),
            new("heterogeneous-cardinal-diagonal",10,[Batch(0,Press(0)),Batch(1,Press(1,NeighborDirection.XPlusYMinus)),Batch(10,Neutral(2))],
                [Open(Key(),StepDuration.Of(6)),Open(Key(2,2,new(1,0),new(2,-1),NeighborDirection.XPlusYMinus,6,1,1),StepDuration.Of(4))],new(2,-1),NeighborDirection.XPlusYMinus,
                "0:S:1 0:A:1:Idle>Walk 3:C:1 6:R:1 6:S:2 6:F:2:XPlus>XPlusYMinus 8:C:2 10:R:2 10:A:2:Walk>Idle")
        ];
    }

    [TestMethod]
    public void M2_M3_FullLiteralTranscriptsAndEveryIntegerPartition()
    {
        foreach (var scenario in Cases())
        {
            var initial = Initial();
            var r = Complete(initial, Request(scenario.Target, scenario.Batches, scenario.Records));
            Transcript(r.Events, scenario.Records, scenario.Events);
            Assert.AreEqual(scenario.FinalCell, r.State!.CommittedCell, scenario.Name);
            Assert.AreEqual(new WorldAnchor(Q(scenario.FinalCell.X), Q(scenario.FinalCell.Y)), r.State.Anchor);
            Assert.AreEqual(scenario.Facing, r.State.Facing);
            Assert.AreEqual(MovementAction.Idle, r.State.Action);
            Assert.AreEqual(MovementPhase.Settled, r.State.Phase);
            Assert.AreEqual(ActorIntent.Neutral, r.State.LatestIntent);
            Assert.AreEqual(scenario.Records.Length + 1L, r.State.NextStepId);
            Assert.AreEqual(scenario.Records.Length + 1L, r.State.NextOpportunityId);
            Assert.AreEqual(r.Events.Count + 1L, r.State.NextEventOrdinal);
            CollectionAssert.AreEqual(scenario.Records.Select(a => a.Key).ToArray(), r.ConsumedAssessmentKeys.ToArray());
            var split = initial;
            var events = new List<MovementEvent>(); var consumed = new List<AdmissionKey>();
            for (var time = 0; time <= scenario.Target; time++)
            {
                var part = Complete(split, Request(time, scenario.Batches.Where(b => b.Time == T(time)).ToArray(),
                    scenario.Records.Where(a => a.Key.StartTime == T(time)).ToArray()));
                split = part.State!; events.AddRange(part.Events); consumed.AddRange(part.ConsumedAssessmentKeys);
                Assert.AreEqual(0, part.UnusedAssessments.Count);
            }
            CollectionAssert.AreEqual(r.Events.ToArray(), events.ToArray(), scenario.Name);
            CollectionAssert.AreEqual(r.ConsumedAssessmentKeys.ToArray(), consumed.ToArray());
            Assert.AreEqual(r.State.Descriptor, split.Descriptor);
            Assert.AreEqual(initial.Descriptor, Initial(limits: initial.Limits).Descriptor);
        }
    }

    [TestMethod]
    public void M2_ValidatedOpenZeroProgressFixtureUsesProductionValidator()
    {
        var a = Open(Key());
        var raw = Initial().Descriptor with { LatestIntent = ActorIntent.Toward(NeighborDirection.XPlus),
            LatestInputKey = new(T(0),0), MaterialIntentKey = new(T(0),0), Action = MovementAction.Walk,
            Segment = new ActiveSegment(a), Phase = MovementPhase.OutboundBeforeEntry,
            NextStepId = 2, NextOpportunityId = 2, NextEventOrdinal = 3, LastOpportunity = a.Key, LastOpportunityDisposition = "Admitted" };
        var state = MovementState.FromDescriptor(raw);
        Assert.IsTrue(MovementValidation.IsValidState(state));
        var r = Complete(state, Request(0,[Batch(0,Neutral(1))]));
        Transcript(r.Events,[a],"0:X:1 0:R:1 0:A:1:Walk>Idle",3);
        Assert.AreEqual(new WorldAnchor(Q(0),Q(0)),r.State!.Anchor);
        Assert.AreEqual(NeighborDirection.XPlus,r.State.Facing);
        var resumed = Open(Key(2,2,start:0,materialTime:0,materialSequence:2));
        var again = Complete(state,Request(0,[Batch(0,Neutral(1),Press(2))],[resumed]));
        Transcript(again.Events,[a,resumed],"0:X:1 0:R:1 0:S:2",3);
        Assert.AreEqual(MovementAction.Walk,again.State!.Action);
    }

    [TestMethod]
    public void M3_ClosedWatermarksEmptyIdempotenceAndQueryPurity()
    {
        var a = Open(Key());
        var start = Complete(Initial(), Request(0,[Batch(0,Press(0))],[a])).State!;
        Invalid(start,Request(0,[Batch(0,Neutral(999))]),MovementFailureKind.InvalidInput);
        Invalid(start,Request(-1),MovementFailureKind.InvalidTarget);
        var state = start;
        foreach (var target in new[] { T(1),T(399,100),T(4),T(401,100),T(6),T(8) })
        {
            var r = Complete(state,new(target,[],[])); state = r.State!;
            var before = state.Descriptor;
            for (var j = 0; j < 4; j++)
            {
                var snapshot = MovementRuntime.QuerySnapshot(state);
                Assert.AreEqual(before.Anchor,snapshot.Anchor); Assert.AreEqual(before.CommittedCell,snapshot.CommittedCell);
                Assert.AreEqual(before.Phase,snapshot.Phase); Assert.AreEqual(before.Facing,snapshot.Facing);
                Assert.AreEqual(before.Action,snapshot.Action); Assert.AreEqual(before.NextEventOrdinal,snapshot.NextEventOrdinal);
                Assert.AreEqual(before,state.Descriptor);
            }
            var equal = Complete(state,new(target,[],[]));
            Assert.AreEqual(before,equal.State!.Descriptor); Assert.AreEqual(0,equal.Events.Count);
            Invalid(state,new(target,[new(target,[Neutral(999)])],[]),MovementFailureKind.InvalidInput);
        }
        Assert.AreEqual(2L,state.NextStepId); Assert.AreEqual(3L,state.NextOpportunityId); // one missing arrival assessment
    }

    [TestMethod]
    public void M3_OutboundFullOrdinalsAcrossFractionalEntryAndArrivalPartitions()
    {
        var a=Open(Key()); var initial=Initial();
        var whole=Complete(initial,Request(8,[Batch(0,Press(0)),Batch(8,Neutral(1))],[a]));
        var state=initial;var events=new List<MovementEvent>();var consumed=new List<AdmissionKey>();
        foreach(var t in new[]{T(0),T(1,3),T(399,100),T(4),T(401,100),T(799,100),T(8)})
        {
            var r=Complete(state,new(t,t==T(0)?[Batch(0,Press(0))]:t==T(8)?[Batch(8,Neutral(1))]:[],t==T(0)?[a]:[]));
            state=r.State!;events.AddRange(r.Events);consumed.AddRange(r.ConsumedAssessmentKeys);
        }
        Transcript(events,[a],"0:S:1 0:A:1:Idle>Walk 4:C:1 8:R:1 8:A:1:Walk>Idle");
        CollectionAssert.AreEqual(whole.Events.ToArray(),events.ToArray());
        CollectionAssert.AreEqual(whole.ConsumedAssessmentKeys.ToArray(),consumed.ToArray());
        Assert.AreEqual(whole.State!.Descriptor,state.Descriptor);
    }

    [TestMethod]
    public void M3_FractionalPartitionsIncludeEverySideAndReturnObservation()
    {
        var a=Open(Key()); var initial=Initial();
        var longResult=Complete(initial,Request(8,[Batch(0,Press(0)),Batch(2,Neutral(1))],[a]));
        var state=initial; var events=new List<MovementEvent>(); var consumed=new List<AdmissionKey>();
        foreach(var t in new[]{T(0),T(1,3),T(2),T(3),T(399,100),T(4),T(401,100),T(8)})
        {
            var r=Complete(state,new(t,t==T(0)?[Batch(0,Press(0))]:t==T(2)?[Batch(2,Neutral(1))]:[],t==T(0)?[a]:[]));
            state=r.State!; events.AddRange(r.Events); consumed.AddRange(r.ConsumedAssessmentKeys);
        }
        Assert.AreEqual(longResult.State!.Descriptor,state.Descriptor);
        CollectionAssert.AreEqual(longResult.Events.ToArray(),events.ToArray());
        CollectionAssert.AreEqual(longResult.ConsumedAssessmentKeys.ToArray(),consumed.ToArray());
    }
}
