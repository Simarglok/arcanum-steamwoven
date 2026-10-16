using System.Collections.Immutable;
using Arcanum.Simulation.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Events.EventFixtures;

namespace Arcanum.Simulation.Tests.Events;

[TestClass]
public sealed class GameEventSnapshotTests
{
    private static GameEventQueue Filled(Recorder recorder)
    {
        var queue = Queue(recorder);
        Added(queue.ScheduleAt(Ui, Values(1), 5));
        Added(queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil")]), 300));
        Added(queue.ScheduleAt(Call, With(Npc("guard", 2), 40), 200));
        Added(queue.ScheduleAt(Anim, Values(2), 10));
        Added(queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("guard", 2)]), 100));
        Added(queue.ScheduleAt(Global, Values(3), 300));
        return queue;
    }

    [TestMethod]
    public void ASnapshotIsDetachedOrderedAndLeavesOutUnsavedKinds()
    {
        var recorder = new Recorder();
        var queue = Filled(recorder);
        var snapshot = queue.CaptureSnapshot();
        Assert.AreEqual(1, snapshot.RegistryVersion);
        Assert.AreEqual(7, snapshot.NextSequence);
        CollectionAssert.AreEqual(new long[] { 5, 3, 2, 6, 4 }, snapshot.Events.Select(e => e.Sequence).ToArray(),
            "game events by (deadline, sequence), then animation; the real-time UI event is not saved");

        Run(queue, Pass(new GameEventClocks(1_000, 1_000, 1_000), activeMap: 2));
        Added(queue.ScheduleAt(Global, Values(9), 1));
        Assert.AreEqual(5, snapshot.Events.Length, "later changes do not reach a taken snapshot");
        Assert.AreEqual(7, snapshot.NextSequence);
    }

    [TestMethod]
    public void ARestoredQueueContinuesWithTheSameTimingAndOrder()
    {
        var recorder = new Recorder();
        var original = Filled(recorder);
        var restored = GameEventQueue.Restore(Registry(recorder), original.CaptureSnapshot());
        Assert.AreEqual(GameEventFailure.None, restored.Failure);
        Assert.IsTrue(restored.Rebuild.IsEmpty);
        var queue = restored.Queue!;
        Assert.AreEqual(7, queue.NextSequence);
        CollectionAssert.AreEqual(original.QueryPending().Where(e => e.Kind != Ui).ToArray(), queue.QueryPending().ToArray());

        // An equal deadline scheduled after the load still orders after the saved one (OQ1 across a save).
        Assert.AreEqual(7, Added(queue.ScheduleAt(Global, Values(4), 300)).Sequence);
        Added(original.ScheduleAt(Global, Values(4), 300));
        var a = new Recorder();
        var b = new Recorder();
        var left = GameEventQueue.Restore(Registry(a), queue.CaptureSnapshot()).Queue!;
        var right = GameEventQueue.Restore(Registry(b), original.CaptureSnapshot()).Queue!;
        Run(left, Pass(new GameEventClocks(0, 1_000, 1_000), activeMap: 1));
        Run(right, Pass(new GameEventClocks(0, 1_000, 1_000), activeMap: 1));
        CollectionAssert.AreEqual(b.Sequences, a.Sequences);
        CollectionAssert.AreEqual(new long[] { 2, 6, 7, 4 }, a.Sequences, "the AI tick is persisted (OQ2)");
    }

    [TestMethod]
    public void TheRebuildSwitchHandsBackTicksForAFreshSchedule()
    {
        var recorder = new Recorder();
        var snapshot = Filled(recorder).CaptureSnapshot();
        var result = GameEventQueue.Restore(Registry(recorder), snapshot, GameEventQueueProfile.Original);
        Assert.AreEqual(GameEventFailure.None, result.Failure);
        CollectionAssert.AreEqual(new[] { "guard", "virgil" }, result.Rebuild.Select(e => e.Payload.Entities[0].Code).ToArray());
        var queue = result.Queue!;
        Assert.IsFalse(queue.Any(Tick));
        Assert.AreEqual(7, queue.NextSequence, "the sequence of the dropped ticks is not reused");
        // The owner (slice E2) schedules a new immediate tick for each, as the source does when a sector loads.
        foreach (var tick in result.Rebuild)
            Assert.AreEqual(GameEventScheduleDisposition.Added, queue.ScheduleAfter(Tick, tick.Payload, 0, Game(50)).Disposition);
        CollectionAssert.AreEqual(new long[] { 51, 51 }, queue.QueryPending().Where(e => e.Kind == Tick).Select(e => e.DeadlineMs).ToArray());
    }

    [TestMethod]
    public void TheTwoSettingsAreNamedAndHoldOnlyTheTickSwitch()
    {
        Assert.AreEqual("steamwoven", GameEventQueueProfile.Steamwoven.Name);
        Assert.IsFalse(GameEventQueueProfile.Steamwoven.RebuildOnLoad);
        Assert.AreEqual("original", GameEventQueueProfile.Original.Name);
        Assert.IsTrue(GameEventQueueProfile.Original.RebuildOnLoad);
        CollectionAssert.AreEqual(new[] { GameEventQueueProfile.Steamwoven, GameEventQueueProfile.Original },
            GameEventQueueProfile.All.ToArray());
        Assert.AreSame(GameEventQueueProfile.Original, GameEventQueueProfile.FromName("original"));
        Assert.IsNull(GameEventQueueProfile.FromName("Original"));
        Assert.IsNull(GameEventQueueProfile.FromName(null));

        // The default is the Steamwoven setting; the same snapshot restores under either (D4).
        var recorder = new Recorder();
        var snapshot = Filled(recorder).CaptureSnapshot();
        Assert.AreEqual(2, GameEventQueue.Restore(Registry(recorder), snapshot).Queue!.QueryPending().Count(e => e.Kind == Tick));
        Assert.AreEqual(2, GameEventQueue.Restore(Registry(recorder), snapshot, GameEventQueueProfile.Steamwoven).Queue!
            .QueryPending().Count(e => e.Kind == Tick));
        Assert.AreEqual(2, GameEventQueue.Restore(Registry(recorder), snapshot, GameEventQueueProfile.Original).Rebuild.Length);
    }

    [TestMethod]
    public void RestoreRefusesInconsistentSnapshots()
    {
        var recorder = new Recorder();
        var registry = Registry(recorder);
        var good = Filled(recorder).CaptureSnapshot();
        var events = good.Events;
        var tick = events.First(e => e.Kind == Tick);
        var call = events.First(e => e.Kind == Call);

        void Refuses(GameEventFailure failure, GameEventQueueSnapshot snapshot, GameEventQueueLimits? limits = null)
        {
            var result = GameEventQueue.Restore(registry, snapshot, limits: limits);
            Assert.AreEqual(failure, result.Failure);
            Assert.IsNull(result.Queue);
        }
        GameEventQueueSnapshot With(params GameEvent[] list) => good with { Events = list.ToImmutableArray() };
        GameEventQueueSnapshot Swap(GameEvent from, GameEvent to) => good with { Events = events.Replace(from, to) };

        Assert.AreEqual(GameEventFailure.None, GameEventQueue.Restore(registry, With()).Failure);
        Refuses(GameEventFailure.InvalidSnapshot, null!);
        Refuses(GameEventFailure.InvalidSnapshot, good with { Events = default });
        Refuses(GameEventFailure.RegistryMismatch, good with { RegistryVersion = 2 });
        Refuses(GameEventFailure.InvalidSequence, good with { NextSequence = 0 });
        Refuses(GameEventFailure.QueueFull, good, new GameEventQueueLimits(MaxPending: 4, MaxPerPass: 4));
        Refuses(GameEventFailure.InvalidSnapshot, With(call, null!));
        Refuses(GameEventFailure.UnknownKind, Swap(call, call with { Kind = "test.none" }));
        Refuses(GameEventFailure.NotPersistedKind, With(new GameEvent(1, Ui, GameEventDomain.Real, 5, 0, Values(1))));
        Refuses(GameEventFailure.InvalidSnapshot, Swap(call, call with { Domain = GameEventDomain.Animation }));
        Refuses(GameEventFailure.InvalidSequence, Swap(call, call with { Sequence = 0 }));
        Refuses(GameEventFailure.InvalidSequence, Swap(call, call with { Sequence = 7 }));
        Refuses(GameEventFailure.InvalidSequence, Swap(call, call with { Sequence = tick.Sequence }));
        Refuses(GameEventFailure.InvalidDeadline, With(call with { DeadlineMs = -1 }));
        Refuses(GameEventFailure.InvalidPayload, Swap(call, call with { Payload = Values(1) }));
        Refuses(GameEventFailure.InvalidScope, Swap(call, call with { ScopeMap = 1 }));
        Refuses(GameEventFailure.InvalidSnapshot, With(events[1], events[0]));
        Refuses(GameEventFailure.DuplicateKey, With(tick, tick with { Sequence = 6, DeadlineMs = 301 }));
        // A duplicate key is refused even among events the rebuild switch would drop.
        Assert.AreEqual(GameEventFailure.DuplicateKey, GameEventQueue.Restore(registry,
            With(tick, tick with { Sequence = 6, DeadlineMs = 301 }), GameEventQueueProfile.Original).Failure);
    }

    [TestMethod]
    public void ARestoredQueueAtTheLastSequenceRefusesNewEvents()
    {
        var recorder = new Recorder();
        var registry = Registry(recorder);
        var last = new GameEvent(long.MaxValue - 1, Once, GameEventDomain.Game, 9, 0, GameEventPayload.Empty);
        var queue = GameEventQueue.Restore(registry, new GameEventQueueSnapshot(1, long.MaxValue, [last])).Queue!;
        Refused(GameEventFailure.SequenceExhausted, queue.ScheduleAt(Global, Values(1), 1));
        Refused(GameEventFailure.SequenceExhausted, queue.ScheduleAt(Once, GameEventPayload.Empty, 1));
        Assert.AreEqual(last, queue.QueryPending().Single(), "a refused replace keeps the pending event");
        Run(queue, Pass(Game(9)));
        Assert.AreEqual(last, recorder.Fired.Single().Event, "the watermark of a pass at the last sequence admits it");
    }
}
