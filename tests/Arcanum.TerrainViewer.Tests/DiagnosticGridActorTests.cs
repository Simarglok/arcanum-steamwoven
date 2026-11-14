using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class DiagnosticGridActorTests
{
    private static InputBatch Batch(int t,long sequence,NeighborDirection? direction) => new(SimulationTime.At(t),[MovementInput.Set(sequence,direction is { } d?ActorIntent.Toward(d):ActorIntent.Neutral)]);
    private static ActorDiagnosticScenePlan Plan()
    {
        var frames=new List<ActorDiagnosticFrame>();
        foreach(var (selector,ordinal,count,fps) in new[]{(0x20000000u,2667,1,6),(0x20000040u,2668,10,17)})
        foreach(var group in new[]{0,4,5,6,7}) for(var frame=0;frame<count;frame++)
            frames.Add(new(new(ordinal,selector,group,frame,0),10,9,3,7,29,-13,count,fps));
        return ActorDiagnosticScenePlan.CreateSynthetic(new(6,1,0,0,0,0,ActorDiagnosticRenderBranch.NormalUncached,100,false,ActorDiagnosticAction.Idle,5,0,10,9,3,7,29,-13),frames,ActorDiagnosticProfile.HumanFixed);
    }
    [TestMethod]
    [DataRow(NeighborDirection.XPlus,5,5,false,1,7,true)]
    [DataRow(NeighborDirection.YPlus,3,5,true,7,7,false)]
    [DataRow(NeighborDirection.YMinus,7,7,false,3,5,true)]
    public void RuntimeCardinalAndCancellationSelectLiteralHumanRows(NeighborDirection direction,int octant,int group,bool flip,int reverseOctant,int reverseGroup,bool reverseFlip)
    {
        using var fixture=new GridWireFixture();var actor=new DiagnosticGridActor();actor.Configure(fixture.Snapshot,SimulationTime.At(0),StepDuration.Of(8));var plan=Plan();
        actor.Advance(SimulationTime.At(1),[Batch(0,0,direction)],input=>plan.Select(input));
        var selected=plan.Select(actor.Placement()).Current;
        Assert.AreEqual(ActorDiagnosticAction.Walk,selected.Action);Assert.AreEqual(octant,selected.RequestedFacing);Assert.AreEqual(group,selected.PhysicalGroup);Assert.AreEqual(flip,selected.FlipX);Assert.AreEqual(7,selected.Frame);
        actor.Advance(SimulationTime.At(3),[Batch(2,1,null)],input=>plan.Select(input));selected=plan.Select(actor.Placement()).Current;
        Assert.AreEqual(MovementPhase.Returning,actor.Session!.State.Phase);Assert.AreEqual(ActorDiagnosticAction.Walk,selected.Action);
        Assert.AreEqual(reverseOctant,selected.RequestedFacing);Assert.AreEqual(reverseGroup,selected.PhysicalGroup);Assert.AreEqual(reverseFlip,selected.FlipX);Assert.AreEqual(1,selected.Frame);
        actor.Advance(SimulationTime.At(4),[],input=>plan.Select(input));selected=plan.Select(actor.Placement()).Current;
        Assert.AreEqual(ActorDiagnosticAction.Idle,selected.Action);Assert.AreEqual(reverseOctant,selected.RequestedFacing);Assert.AreEqual(0,selected.Frame);
    }
    [TestMethod]
    public void PostEntryNeutralWalkGeometryAndSelectionFailureAreTransactional()
    {
        using var fixture=new GridWireFixture();var actor=new DiagnosticGridActor();actor.Configure(fixture.Snapshot,SimulationTime.At(0),StepDuration.Of(8));var plan=Plan();
        actor.Advance(SimulationTime.At(1),[Batch(0,0,NeighborDirection.XPlus)],input=>plan.Select(input));
        var selected=plan.Select(actor.Placement()).Current;
        Assert.AreEqual(-205L,selected.AnchorX);Assert.AreEqual(162L,selected.AnchorY);Assert.AreEqual(-208,selected.Destination.X);Assert.AreEqual(155,selected.Destination.Y);
        Assert.AreEqual(ExactScalar.Create(49,8),actor.Session!.State.Anchor.U);Assert.AreEqual(new CellCoord(6,1),actor.Session.State.CommittedCell);
        var before=actor.Session.State;var frame=actor.CurrentFrame;
        Assert.Throws<InvalidOperationException>(()=>actor.Advance(SimulationTime.At(3),[Batch(2,1,null)],_=>throw new InvalidOperationException("selection")));
        Assert.AreSame(before,actor.Session.State);Assert.AreEqual(frame,actor.CurrentFrame);
        actor.Advance(SimulationTime.At(6),[Batch(4,1,null)],input=>plan.Select(input));
        Assert.AreEqual(ActorDiagnosticAction.Walk,actor.State.Action);Assert.AreEqual(MovementPhase.OutboundAfterEntry,actor.Session.State.Phase);
        Assert.AreEqual(new CellCoord(7,1),actor.Session.State.CommittedCell);Assert.AreEqual(ExactScalar.Create(27,4),actor.Session.State.Anchor.U);
        var runtime=actor.Session.State; for(var zoom=1;zoom<=3;zoom++) _=plan.Select(actor.Placement()); Assert.AreSame(runtime,actor.Session.State);
        actor.Advance(SimulationTime.At(8),[],input=>plan.Select(input));Assert.AreEqual(ActorDiagnosticAction.Idle,actor.State.Action);
    }
    [TestMethod]
    public void InputResolutionHasLiteralAxesInclusiveHalfTiesNoDiagonalFallback()
    {
        var vectors=new[]{new Vec2(-1,0),new(1,0),new(0,-1),new(0,1),new(1,.5),new(1,.499),new(-.5,-1),default(Vec2)};
        NeighborDirection?[] expected=[NeighborDirection.XPlusYMinus,NeighborDirection.XMinusYPlus,NeighborDirection.XMinusYMinus,NeighborDirection.XPlusYPlus,NeighborDirection.YPlus,NeighborDirection.XMinusYPlus,NeighborDirection.YMinus,null];
        CollectionAssert.AreEqual(expected,vectors.Select(v => DiagnosticGridActor.Resolve(v)).ToArray());
        Assert.Throws<ArgumentException>(()=>DiagnosticGridActor.Resolve(new(double.NaN,1)));
    }
    [TestMethod]
    public void FrontierKeepsNewestOpenAndPreservesLiteralBetweenFrameTranscript()
    {
        using var fixture = new GridWireFixture();
        var session = new DiagnosticGridSession(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        var queue = new GridInputQueue();
        Assert.IsFalse(queue.HasPending);
        queue.Observe(session, SimulationTime.At(0), ActorIntent.Toward(NeighborDirection.XPlus));
        Assert.IsNull(queue.CompleteFrontier);
        Assert.IsTrue(queue.HasPending, "An input at the newest, still open tick is pending.");
        queue.Observe(session, SimulationTime.At(0), ActorIntent.Toward(NeighborDirection.XPlus));
        Assert.IsNull(queue.CompleteFrontier);
        queue.Observe(session, SimulationTime.At(2), ActorIntent.Neutral);
        Assert.AreEqual(SimulationTime.At(0), queue.CompleteFrontier);
        queue.Observe(session, SimulationTime.At(3), ActorIntent.Toward(NeighborDirection.YPlus));
        queue.Observe(session, SimulationTime.At(3), ActorIntent.Neutral);
        Assert.AreEqual(SimulationTime.At(2), queue.CompleteFrontier);
        queue.Observe(session, SimulationTime.At(4), ActorIntent.Neutral);
        Assert.AreEqual(SimulationTime.At(3), queue.CompleteFrontier);
        var result = session.Advance(queue.CompleteFrontier!.Value, queue.CompleteThrough(queue.CompleteFrontier.Value));
        Assert.AreEqual(MovementStatus.Completed, result.Movement.Status, System.Text.Json.JsonSerializer.Serialize(result.Movement));
        Assert.AreEqual("StepStarted:0/1|ActionChanged:0/1|StepCancelled:2/1|FacingChanged:2/1",
            string.Join("|", result.Movement.Events.Select(e => $"{e.Kind}:{e.Time.Value.Numerator}/{e.Time.Value.Denominator}")));
        Assert.AreEqual(ExactScalar.Create(49,8), session.State.Anchor.U);
        Assert.AreEqual(new CellCoord(6,1), session.State.CommittedCell);
        queue.PublishedThrough(queue.CompleteFrontier.Value);
        Assert.IsFalse(queue.HasPending);
        queue.Observe(session, SimulationTime.At(5), ActorIntent.Neutral);
        result = session.Advance(queue.CompleteFrontier!.Value, queue.CompleteThrough(queue.CompleteFrontier.Value));
        Assert.AreEqual("CenterArrived:4/1|ActionChanged:4/1",
            string.Join("|", result.Movement.Events.Select(e => $"{e.Kind}:{e.Time.Value.Numerator}/{e.Time.Value.Denominator}")));
        Assert.AreEqual(MovementPhase.Settled, session.State.Phase);
        Assert.AreEqual(ExactScalar.Integer(6), session.State.Anchor.U);
        Assert.Throws<InvalidOperationException>(() => queue.Observe(session, SimulationTime.At(4), ActorIntent.Toward(NeighborDirection.XPlus)));
        var successor = new DiagnosticGridSession(fixture.Snapshot, SimulationTime.At(5));
        Assert.Throws<InvalidOperationException>(() => queue.Observe(successor, SimulationTime.At(6), ActorIntent.Neutral));
    }
    [TestMethod]
    public void QueueCoalescesCompleteTimestampsAndRejectsLateObservationsWithoutRetagging()
    {
        using var fixture=new GridWireFixture();var session=new DiagnosticGridSession(fixture.Snapshot,SimulationTime.At(0),StepDuration.Of(8));var queue=new GridInputQueue();
        queue.Observe(session,SimulationTime.At(0),ActorIntent.Toward(NeighborDirection.XPlus));
        queue.Observe(session,SimulationTime.At(0),ActorIntent.Neutral);
        queue.Observe(session,SimulationTime.At(0),ActorIntent.Toward(NeighborDirection.YPlus));
        var batches=queue.CompleteThrough(SimulationTime.At(1));Assert.AreEqual(1,batches.Length);Assert.AreEqual(3,batches[0].Entries.Count);
        var result=session.Advance(SimulationTime.At(1),batches);queue.PublishedThrough(SimulationTime.At(1));Assert.AreEqual(0,queue.CompleteThrough(SimulationTime.At(1)).Length);
        Assert.AreEqual(NeighborDirection.YPlus,result.Movement.Events.First().Key.Direction);
        Assert.Throws<InvalidOperationException>(()=>queue.Observe(session,SimulationTime.At(1),ActorIntent.Neutral));
        Assert.Throws<InvalidOperationException>(()=>queue.Observe(session,SimulationTime.At(0),ActorIntent.Neutral));
        queue.Observe(session,SimulationTime.At(2),ActorIntent.Neutral);session.Advance(SimulationTime.At(3),queue.CompleteThrough(SimulationTime.At(3)));
        Assert.AreEqual(MovementPhase.Returning,session.State.Phase);
    }

    [TestMethod]
    public void SelfTestFrameClockNeverFallsBehindTheNewestObservation()
    {
        // ARC-DEV-1251: the session's time is an older wall-clock stamp; a quarantine read the clock later, 0.25 s on.
        using var fixture = new GridWireFixture();
        var session = new DiagnosticGridSession(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        var queue = new GridInputQueue();
        Assert.IsNull(queue.Newest);
        var late = new SimulationTime(ExactScalar.Create(1, 4));
        queue.Observe(session, late, ActorIntent.Neutral);
        Assert.AreEqual(late, queue.Newest);
        var naive = new SimulationTime(session.State.Time.Value + SelfTestFrameClock.Step);
        Assert.Throws<InvalidOperationException>(() => queue.Observe(session, naive, ActorIntent.Neutral));
        var next = SelfTestFrameClock.Next(session.State.Time, queue.Newest);
        Assert.AreEqual(new SimulationTime(late.Value + SelfTestFrameClock.Step), next);
        queue.Observe(session, next, ActorIntent.Neutral);
        // The clock keeps stepping by a tenth, and without a newer observation it steps from its own basis.
        Assert.AreEqual(new SimulationTime(next.Value + SelfTestFrameClock.Step), SelfTestFrameClock.Next(next, queue.Newest));
        Assert.AreEqual(new SimulationTime(ExactScalar.Create(1, 10)), SelfTestFrameClock.Next(SimulationTime.At(0), null));
    }

    [TestMethod]
    public void SelfTestLiteralFrameIsLiftedToWhatWasObservedButNotAcrossAWrongClock()
    {
        // ARC-DEV-2005: a click between a stage's frames observed at a test time a little ahead of the stage's next literal frame.
        using var fixture = new GridWireFixture();
        var session = new DiagnosticGridSession(fixture.Snapshot, SimulationTime.At(0), StepDuration.Of(8));
        var queue = new GridInputQueue();
        var ahead = new SimulationTime(ExactScalar.Create(1347, 20));
        var literal = new SimulationTime(ExactScalar.Create(1341, 20));
        queue.Observe(session, ahead, ActorIntent.Neutral);
        Assert.AreEqual("GRID_OBSERVATION_NONMONOTONIC", Assert.Throws<InvalidOperationException>(() => queue.Observe(session, literal, ActorIntent.Neutral)).Message);
        var lifted = SelfTestFrameClock.Frame(literal, queue.Newest, session.State.Time);
        Assert.AreEqual(ahead, lifted);
        queue.Observe(session, lifted, ActorIntent.Neutral);
        // A literal frame that is not behind keeps its time; so does one with nothing observed yet.
        var later = new SimulationTime(ExactScalar.Create(1350, 20));
        Assert.AreEqual(later, SelfTestFrameClock.Frame(later, queue.Newest, session.State.Time));
        Assert.AreEqual(literal, SelfTestFrameClock.Frame(literal, null, null));
        // The session's time lifts it too (the late refusal).
        var sessionAhead = new SimulationTime(ExactScalar.Create(3, 1));
        Assert.AreEqual(sessionAhead, SelfTestFrameClock.Frame(SimulationTime.At(2), null, sessionAhead));
        // Both floors in one call: the later of the queue's newest and the session's time.
        Assert.AreEqual(sessionAhead, SelfTestFrameClock.Frame(SimulationTime.At(2), SimulationTime.At(1), sessionAhead));
        Assert.AreEqual(new SimulationTime(ExactScalar.Create(7, 2)), SelfTestFrameClock.Frame(SimulationTime.At(3), new SimulationTime(ExactScalar.Create(7, 2)), sessionAhead));
        // An invalid literal is handed on as it is (the queue refuses it by name), never an exception of the clock.
        Assert.AreEqual(default(SimulationTime), SelfTestFrameClock.Frame(default, queue.Newest, session.State.Time));
        // An observation at the frame count read as seconds (1347 s while the stage is at 67 s) is a wrong clock, not slack: it stays refused.
        var wrongScale = SimulationTime.At(1347);
        var stageFrame = new SimulationTime(ExactScalar.Create(1341, 20));
        Assert.AreEqual(stageFrame, SelfTestFrameClock.Frame(stageFrame, wrongScale, null));
        var wrongQueue = new GridInputQueue(); wrongQueue.Observe(session, wrongScale, ActorIntent.Neutral);
        Assert.AreEqual("GRID_OBSERVATION_NONMONOTONIC", Assert.Throws<InvalidOperationException>(() => wrongQueue.Observe(session, stageFrame, ActorIntent.Neutral)).Message);
        // The edge: exactly the limit is lifted, a tick over is not.
        var edge = new SimulationTime(stageFrame.Value + SelfTestFrameClock.MaxLift);
        Assert.AreEqual(edge, SelfTestFrameClock.Frame(stageFrame, edge, null));
        var over = new SimulationTime(edge.Value + ExactScalar.Create(1, 20));
        Assert.AreEqual(stageFrame, SelfTestFrameClock.Frame(stageFrame, over, null));
    }
}
