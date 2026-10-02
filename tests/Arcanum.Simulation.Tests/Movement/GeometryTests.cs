using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class GeometryTests
{
    [TestMethod]
    [DataRow(NeighborDirection.XPlus, NeighborDirection.XMinus, 1, 0)]
    [DataRow(NeighborDirection.XMinus, NeighborDirection.XPlus, -1, 0)]
    [DataRow(NeighborDirection.YPlus, NeighborDirection.YMinus, 0, 1)]
    [DataRow(NeighborDirection.YMinus, NeighborDirection.YPlus, 0, -1)]
    [DataRow(NeighborDirection.XPlusYPlus, NeighborDirection.XMinusYMinus, 1, 1)]
    [DataRow(NeighborDirection.XPlusYMinus, NeighborDirection.XMinusYPlus, 1, -1)]
    [DataRow(NeighborDirection.XMinusYPlus, NeighborDirection.XPlusYMinus, -1, 1)]
    [DataRow(NeighborDirection.XMinusYMinus, NeighborDirection.XPlusYPlus, -1, -1)]
    public void M1_AllEightSignedBeforeEqualAfterFullEvents(NeighborDirection direction, NeighborDirection reverse, int x, int y)
    {
        // Literal signed component numerators; no production Delta/Reverse/Center helper used.
        foreach (var release in new[] { 2, 4, 6 })
        {
            var record = Open(Key(to: new(x, y), direction: direction));
            var r = Complete(Initial(facing: direction), Request(release,
                [Batch(0, Press(0, direction)), Batch(release, Neutral(1))], [record]));
            var state = r.State!;
            var numerator = release == 2 ? 1 : release == 4 ? 2 : 3;
            Assert.AreEqual(new WorldAnchor(Q(x * numerator, 4), Q(y * numerator, 4)), state.Anchor);
            Assert.AreEqual(release == 2 ? new CellCoord(0, 0) : new CellCoord(x, y), state.CommittedCell);
            Assert.AreEqual(release == 2 ? MovementPhase.Returning : MovementPhase.OutboundAfterEntry, state.Phase);
            Assert.AreEqual(ActorIntent.Neutral, state.LatestIntent);
            Assert.AreEqual(new InputKey(T(release), 1), state.MaterialIntentKey);
            Assert.AreEqual(release == 2 ? reverse : direction, state.Facing);
            Assert.AreEqual(MovementAction.Walk, state.Action);
            Assert.AreEqual(2L, state.NextOpportunityId); Assert.AreEqual(2L, state.NextStepId);
            Transcript(r.Events, [record], release == 2
                ? $"0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:{direction}>{reverse}"
                : "0:S:1 0:A:1:Idle>Walk 4:C:1");
            CollectionAssert.AreEqual(new[] { record.Key }, r.ConsumedAssessmentKeys.ToArray());
            var end = Complete(state, Request(8));
            Transcript(end.Events, [record], release == 2 ? "4:R:1 4:A:1:Walk>Idle" : "8:R:1 8:A:1:Walk>Idle", state.NextEventOrdinal);
            Assert.AreEqual(MovementPhase.Settled, end.State!.Phase);
            Assert.AreEqual(new WorldAnchor(Q(end.State.CommittedCell.X), Q(end.State.CommittedCell.Y)), end.State.Anchor);
            Assert.AreEqual(MovementAction.Idle, end.State.Action);
        }
    }

    [TestMethod]
    public void M1_NegativeOriginAtomicCornerAndContinuousReturn()
    {
        var record = Open(Key(from: new(-2, -3), to: new(-1, -2), direction: NeighborDirection.XPlusYPlus));
        var r = Complete(Initial(new(-2, -3)), Request(4, [Batch(0, Press(0, NeighborDirection.XPlusYPlus)), Batch(4, Neutral(1))], [record]));
        Assert.AreEqual(new WorldAnchor(Q(-3, 2), Q(-5, 2)), r.State!.Anchor);
        Assert.AreEqual(new CellCoord(-1, -2), r.State.CommittedCell);
        Transcript(r.Events, [record], "0:S:1 0:F:1:XPlus>XPlusYPlus 0:A:1:Idle>Walk 4:C:1");
        var a = Open(Key());
        var returned = Complete(Initial(), Request(3, [Batch(0, Press(0)), Batch(2, Neutral(1)), Batch(3, Neutral(2))], [a]));
        Assert.AreEqual(new WorldAnchor(Q(1, 8), Q(0)), returned.State!.Anchor);
        Transcript(returned.Events, [a], "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus");
        var finish = Complete(returned.State, Request(4));
        Assert.AreEqual(new WorldAnchor(Q(0), Q(0)), finish.State!.Anchor);
        Transcript(finish.Events, [a], "4:R:1 4:A:1:Walk>Idle", 5);
    }

    [TestMethod]
    [DataRow(399, 100, 399, 800, MovementPhase.Returning)]
    [DataRow(4, 1, 1, 2, MovementPhase.OutboundAfterEntry)]
    [DataRow(401, 100, 401, 800, MovementPhase.OutboundAfterEntry)]
    public void M1_ExactAroundTie(long rn, long rd, long an, long ad, MovementPhase phase)
    {
        var a = Open(Key());
        var r = Complete(Initial(), new(T(rn, rd), [Batch(0, Press(0)), new(T(rn, rd), [Neutral(1)])], [a]));
        Assert.AreEqual(new WorldAnchor(Q(an, ad), Q(0)), r.State!.Anchor);
        Assert.AreEqual(phase, r.State.Phase);
        Assert.AreEqual(phase == MovementPhase.Returning ? new CellCoord(0, 0) : new CellCoord(1, 0), r.State.CommittedCell);
        Assert.AreEqual(phase == MovementPhase.Returning ? 1 : 0, r.Events.Count(e => e.Kind == MovementEventKind.StepCancelled));
        Assert.AreEqual(phase == MovementPhase.Returning ? 0 : 1, r.Events.Count(e => e.Kind == MovementEventKind.CellCommitted));
    }

    [TestMethod]
    public void M1_OddFractionalDurationsAndLargeTimeSmallDuration()
    {
        foreach (var duration in new[] { StepDuration.Of(3), StepDuration.Of(3, 2) })
        {
            // Independently computed literal half-time and quarter progress fixtures.
            var half = duration.Value.Denominator == 1 ? T(3, 2) : T(3, 4);
            var quarter = duration.Value.Denominator == 1 ? T(3, 4) : T(3, 8);
            var record = Open(Key(to: new(-1, 0), direction: NeighborDirection.XMinus), duration);
            var r = Complete(Initial(), new(half, [Batch(0, Press(0, NeighborDirection.XMinus)), new(half, [Neutral(1)])], [record]));
            Assert.AreEqual(new WorldAnchor(Q(-1, 2), Q(0)), r.State!.Anchor);
            Assert.AreEqual(new CellCoord(-1, 0), r.State.CommittedCell);
            var q = Complete(Initial(), new(quarter, [Batch(0, Press(0, NeighborDirection.XMinus)), new(quarter, [Neutral(1)])], [record]));
            Assert.AreEqual(new WorldAnchor(Q(-1, 4), Q(0)), q.State!.Anchor);
            var end = Complete(q.State, new(half, [], []));
            Assert.AreEqual(new WorldAnchor(Q(0), Q(0)), end.State!.Anchor);
        }
        var initial = MovementState.CreateSettled(Binding, new(int.MinValue, int.MaxValue), SimulationTime.At(1000000000000000000), NeighborDirection.XPlus);
        var key = new AdmissionKey(Binding, new(int.MinValue, int.MaxValue), new(-2147483647, int.MaxValue), NeighborDirection.XPlus,
            SimulationTime.At(1000000000000000000), 1, 1, new(SimulationTime.At(1000000000000000000), 0), 0);
        var target = SimulationTime.At(2000000000000000001, 2);
        var result = Complete(initial, new(target, [new(key.StartTime, [Press(0)]), new(target, [Neutral(1)])], [Open(key, StepDuration.Of(1))]));
        Assert.AreEqual(new WorldAnchor(Q(-4294967295, 2), Q(2147483647)), result.State!.Anchor);
        Assert.AreEqual(new CellCoord(-2147483647, int.MaxValue), result.State.CommittedCell);
    }
}
