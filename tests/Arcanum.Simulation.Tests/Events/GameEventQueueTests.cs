using Arcanum.Simulation.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Events.EventFixtures;

namespace Arcanum.Simulation.Tests.Events;

[TestClass]
public sealed class GameEventQueueTests
{
    [TestMethod]
    public void EqualDeadlinesFireFirstScheduledFirst()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        Added(queue.ScheduleAt(Global, Values(30), 100));
        Added(queue.ScheduleAt(Global, Values(10), 50));
        Added(queue.ScheduleAt(Global, Values(31), 100));
        Added(queue.ScheduleAfter(Global, Values(32), 100, Zero));
        Assert.AreEqual(5, queue.NextSequence);

        var result = Run(queue, Pass(Game(100)));
        // Deadline first, then sequence: the source would fire the 100 ms group newest first (TE3).
        CollectionAssert.AreEqual(new long[] { 10, 30, 31, 32 }, recorder.FirstValues);
        CollectionAssert.AreEqual(new long[] { 2, 1, 3, 4 }, recorder.Sequences);
        Assert.AreEqual(4, result.Fired);
        Assert.IsFalse(result.BudgetExhausted);
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void AZeroDelayIsOneMillisecondAndDeadlinesAreAbsolute()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        var immediate = Added(queue.ScheduleAfter(Global, Values(1), 0, Game(1_000)));
        Assert.AreEqual(1_001, immediate.DeadlineMs);
        Assert.AreEqual(GameEventDomain.Game, immediate.Domain);
        Assert.AreEqual(0, immediate.ScopeMap);

        Assert.AreEqual(0, Run(queue, Pass(Game(1_000))).Outcomes.Length);
        var result = Run(queue, Pass(Game(1_001)));
        Assert.AreEqual(1, result.Fired);
        Assert.AreEqual(1_001, recorder.Fired[0].NowMs);
    }

    [TestMethod]
    public void EventsScheduledDuringAPassWaitForTheNextPass()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        recorder.OnFire = (firing, q) =>
        {
            if (firing.Event.Payload.Values[0] == 1)
            {
                // A periodic kind reschedules from now; an immediate and a past absolute deadline are also new.
                Added(q.ScheduleAfter(Global, Values(2), 0, firing.Clocks));
                Added(q.ScheduleAt(Global, Values(3), 0));
            }
            return GameEventHandlerOutcome.Completed;
        };
        Added(queue.ScheduleAt(Global, Values(1), 10));
        Added(queue.ScheduleAt(Global, Values(4), 500));

        var first = Run(queue, Pass(Game(1_000)));
        CollectionAssert.AreEqual(new long[] { 1, 4 }, recorder.FirstValues);
        Assert.AreEqual(2, first.Fired);

        recorder.Fired.Clear();
        Run(queue, Pass(Game(1_000)));
        // The absolute deadline 0 is earlier than 1_001 but was scheduled after it: deadline order wins.
        CollectionAssert.AreEqual(new long[] { 3 }, recorder.FirstValues);
        Run(queue, Pass(Game(1_001)));
        CollectionAssert.AreEqual(new long[] { 3, 2 }, recorder.FirstValues);
    }

    [TestMethod]
    public void HandlerEventsDueNowAheadOfThePassPositionWaitForTheNextPass()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        recorder.OnFire = (firing, q) =>
        {
            if (firing.Event.Payload.Values[0] == 1)
            {
                // A real-time handler scheduling a game event that is already due, and an animation event due now.
                Added(q.ScheduleAfter(Global, Values(6), 0, firing.Clocks with { GameMs = 0 }));
                Added(q.ScheduleAt(Anim, Values(7), 1_000));
            }
            if (firing.Event.Payload.Values[0] == 2)
                // A game handler at 10: same domain, deadline after the pass position and at or before now.
                Added(q.ScheduleAt(Global, Values(5), 500));
            return GameEventHandlerOutcome.Completed;
        };
        Added(queue.ScheduleAt(Ui, Values(1), 10));
        Added(queue.ScheduleAt(Global, Values(2), 10));
        Added(queue.ScheduleAt(Global, Values(3), 900));

        // The budget equals the events due before the pass: nothing created in it is fired or counted as left over.
        var first = Run(queue, Pass(new GameEventClocks(1_000, 1_000, 1_000), budget: 3));
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, recorder.FirstValues);
        Assert.IsFalse(first.BudgetExhausted);
        Assert.AreEqual(3, queue.Count);

        var second = Run(queue, Pass(new GameEventClocks(1_000, 1_000, 1_000), budget: 3));
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 6, 5, 7 }, recorder.FirstValues);
        Assert.IsFalse(second.BudgetExhausted);
    }

    [TestMethod]
    public void TheBudgetCarriesDueEventsOverToLaterPasses()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        for (var i = 1; i <= 5; i++) Added(queue.ScheduleAt(Global, Values(i), i));
        Added(queue.ScheduleAt(Anim, Values(9), 0));

        var first = Run(queue, Pass(Game(10), budget: 2));
        Assert.IsTrue(first.BudgetExhausted);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, recorder.FirstValues);
        var second = Run(queue, Pass(Game(10), budget: 3));
        Assert.IsTrue(second.BudgetExhausted, "the animation event is still due");
        var third = Run(queue, Pass(Game(10), budget: 1));
        Assert.IsFalse(third.BudgetExhausted, "the budget was used exactly with nothing left");
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5, 9 }, recorder.FirstValues);

        Assert.AreEqual(GameEventFailure.InvalidPass, queue.ProcessDue(Pass(Game(10), budget: 0)).Failure);
        Assert.AreEqual(GameEventFailure.InvalidPass, queue.ProcessDue(Pass(Game(10), budget: 501)).Failure);
        var small = Queue(recorder, new GameEventQueueLimits(MaxPending: 8, MaxPerPass: 3));
        Assert.AreEqual(GameEventFailure.InvalidPass, small.ProcessDue(Pass(Game(10), budget: 4)).Failure);
    }

    [TestMethod]
    public void DomainsRunRealThenGameThenAnimationOnTheirOwnClocks()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        Added(queue.ScheduleAt(Anim, Values(3), 5));
        Added(queue.ScheduleAt(Global, Values(2), 5));
        Added(queue.ScheduleAt(Ui, Values(1), 5));

        // Turn-based combat: game time frozen at 0, animation and real time run.
        Run(queue, Pass(new GameEventClocks(5, 0, 5)));
        CollectionAssert.AreEqual(new long[] { 1, 3 }, recorder.FirstValues);
        Assert.AreEqual(1, queue.Count);
        // The round end advances game time; the game event fires late, seeing the final time.
        Run(queue, Pass(new GameEventClocks(6, 1_000, 6)));
        CollectionAssert.AreEqual(new long[] { 1, 3, 2 }, recorder.FirstValues);
        Assert.AreEqual(1_000, recorder.Fired[2].NowMs);

        var deadlines = new GameEventClocks(100, 200, 300);
        Assert.AreEqual(101, Added(queue.ScheduleAfter(Ui, Values(0), 1, deadlines)).DeadlineMs);
        Assert.AreEqual(201, Added(queue.ScheduleAfter(Global, Values(0), 1, deadlines)).DeadlineMs);
        Assert.AreEqual(301, Added(queue.ScheduleAfter(Anim, Values(0), 1, deadlines)).DeadlineMs);
        var order = queue.QueryPending().Select(e => e.Domain).ToArray();
        CollectionAssert.AreEqual(new[] { GameEventDomain.Real, GameEventDomain.Game, GameEventDomain.Animation }, order);
    }

    [TestMethod]
    public void ATimeJumpFiresEachDueEventOnceInDeadlineOrder()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        // A periodic kind every 80 s: after a jump of an hour it fires once and reschedules from now, no replay.
        recorder.OnFire = (firing, q) =>
        {
            if (firing.Event.Kind == Fatigue)
                Added(q.ScheduleAfter(Fatigue, With(Npc("pc"), 0, firing.NowMs), 80_000, firing.Clocks));
            return GameEventHandlerOutcome.Completed;
        };
        Added(queue.ScheduleAt(Fatigue, With(Npc("pc"), 0, 0), 80_000));
        Added(queue.ScheduleAt(Global, Values(7), 3_000_000));

        Run(queue, Pass(Game(3_600_000)));
        Assert.AreEqual(2, recorder.Fired.Count);
        Assert.AreEqual(Fatigue, recorder.Fired[0].Event.Kind);
        Assert.IsTrue(recorder.Fired.All(f => f.NowMs == 3_600_000));
        var next = queue.QueryPending().Single();
        Assert.AreEqual(3_680_000, next.DeadlineMs);
        Assert.AreEqual(3_600_000, next.Payload.Values[1], "the elapsed reference is the firing time");
    }

    [TestMethod]
    public void MapScopedEventsWaitForTheirMap()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        var cave = Added(queue.ScheduleAt(Call, With(Npc("guard", 2), 11), 10));
        var tick = Added(queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil")]), 10));
        Added(queue.ScheduleAt(Recharge, With(Npc("ring", 2), 5), 10));
        Added(queue.ScheduleAt(Global, Values(1), 10));
        Assert.AreEqual(2, cave.ScopeMap);
        Assert.AreEqual(1, tick.ScopeMap);

        // The due event of map 2 is not left over: a budget of exactly the eligible events is not exhausted.
        var first = Run(queue, Pass(Game(10), activeMap: 1, budget: 3));
        // The recharge kind is global although its item is on map 2.
        CollectionAssert.AreEqual(new[] { Tick, Recharge, Global }, first.Outcomes.Select(o => o.Event.Kind).ToArray());
        Assert.IsFalse(first.BudgetExhausted);
        Assert.AreEqual(0, Run(queue, Pass(Game(10_000), activeMap: 0)).Outcomes.Length, "no map: only global events");
        var second = Run(queue, Pass(Game(10_000), activeMap: 2));
        Assert.AreEqual(cave, second.Outcomes.Single().Event);

        // A scoped event names one map; two entities on different maps have none (the session moves them, E2).
        Assert.AreEqual(2, Added(queue.ScheduleAt(Pair, GameEventPayload.Of([Npc("a", 2), Npc("b", 2)]), 1)).ScopeMap);
        Refused(GameEventFailure.InvalidScope, queue.ScheduleAt(Pair, GameEventPayload.Of([Npc("a", 1), Npc("b", 2)]), 1));
        Assert.AreEqual(GameEventFailure.InvalidPass, queue.ProcessDue(Pass(Game(1), activeMap: -1)).Failure);
    }

    [TestMethod]
    public void MissingEntitiesDropTheEventAndRefusalsAreReported()
    {
        var recorder = new Recorder { OnFire = (f, _) => f.Event.Payload.Values[0] == 2 ? GameEventHandlerOutcome.Refused : GameEventHandlerOutcome.Completed };
        var queue = Queue(recorder);
        Added(queue.ScheduleAt(Call, With(Npc("gone"), 1), 1));
        Added(queue.ScheduleAt(Call, With(Npc("here"), 2), 2));
        Added(queue.ScheduleAt(Call, With(Npc("here"), 3), 3));

        var result = Run(queue, Pass(Game(5), exists: e => e.Code != "gone"));
        CollectionAssert.AreEqual(new[] { GameEventOutcomeKind.EntityMissing, GameEventOutcomeKind.HandlerRefused, GameEventOutcomeKind.Fired },
            result.Outcomes.Select(o => o.Kind).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 3 }, recorder.FirstValues, "the dropped event's handler did not run");
        Assert.AreEqual(1, result.Fired);
        Assert.AreEqual(2, result.Dropped);
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void UniquenessPolicies()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);

        // Replace: new deadline and sequence, the old event is reported.
        var first = Added(queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil")]), 100));
        var replaced = queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil")]), 50);
        Assert.AreEqual(GameEventScheduleDisposition.Replaced, replaced.Disposition);
        Assert.AreEqual(first, replaced.Previous);
        Assert.AreEqual((50L, 2L), (replaced.Event!.DeadlineMs, replaced.Event.Sequence));
        Assert.AreEqual(GameEventScheduleDisposition.Added, queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("guard")]), 50).Disposition);

        // Keep existing: per (critter, sub-kind); the third value is not part of the key.
        var fatigue = Added(queue.ScheduleAt(Fatigue, With(Npc("pc"), 0, 10), 500));
        var kept = queue.ScheduleAt(Fatigue, With(Npc("pc"), 0, 20), 10);
        Assert.AreEqual(GameEventScheduleDisposition.KeptExisting, kept.Disposition);
        Assert.AreEqual(fatigue, kept.Event);
        Assert.AreEqual(GameEventScheduleDisposition.Added, queue.ScheduleAt(Fatigue, With(Npc("pc"), 1, 20), 10).Disposition);

        // Merge: the pending event keeps deadline and sequence, the amount adds up.
        var recharge = Added(queue.ScheduleAt(Recharge, With(Npc("ring"), 5), 700));
        var merged = queue.ScheduleAfter(Recharge, With(Npc("ring"), 3), 9_000, Zero);
        Assert.AreEqual(GameEventScheduleDisposition.Merged, merged.Disposition);
        Assert.AreEqual(recharge, merged.Previous);
        Assert.AreEqual(recharge with { Payload = With(Npc("ring"), 8) }, merged.Event);
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Recharge, With(Npc("ring"), -1), 1));

        // At most one of a kind without key slots.
        Added(queue.ScheduleAt(Once, GameEventPayload.Empty, 9));
        Assert.AreEqual(GameEventScheduleDisposition.Replaced, queue.ScheduleAt(Once, GameEventPayload.Empty, 8).Disposition);

        // Many: equal payloads coexist.
        Added(queue.ScheduleAt(Call, With(Npc("pc"), 1), 1));
        Assert.AreEqual(GameEventScheduleDisposition.Added, queue.ScheduleAt(Call, With(Npc("pc"), 1), 1).Disposition);
        Assert.AreEqual(8, queue.Count);
        Assert.AreEqual(11, queue.NextSequence, "kept and merged requests take no sequence");
    }

    [TestMethod]
    public void AMergeThatChangesTheKeyOrTheMapIsRefused()
    {
        var recorder = new Recorder();
        GameEventQueue MergeQueue(GameEventMerge merge) => new(new GameEventRegistry(1,
        [
            // Key: the first value only; the entity (and so the map) is not part of it.
            new("test.merge", GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Merge,
                GameEventScopeRule.EntityMap, 1, 2, 0, 1, recorder, Merge: merge),
        ]));

        // Changes only the key value, on the same map.
        var keyChange = MergeQueue((pending, _) => pending with { Values = [pending.Values[0] + 1, pending.Values[1]] });
        var pending = Added(keyChange.ScheduleAt("test.merge", With(Npc("a"), 1, 0), 5));
        Refused(GameEventFailure.InvalidMerge, keyChange.ScheduleAt("test.merge", With(Npc("a"), 1, 0), 5));
        Assert.AreEqual(pending, keyChange.QueryPending().Single(), "a refused merge changes nothing");
        Assert.AreEqual(GameEventScheduleDisposition.Added, keyChange.ScheduleAt("test.merge", With(Npc("a"), 2, 0), 5).Disposition,
            "the key index still holds the pending event's own key");

        // Changes only the entity's map, keeping the key.
        var mapChange = MergeQueue((pending, incoming) => pending with { Entities = incoming.Entities });
        pending = Added(mapChange.ScheduleAt("test.merge", With(Npc("a", 1), 1, 0), 5));
        Refused(GameEventFailure.InvalidMerge, mapChange.ScheduleAt("test.merge", With(Npc("a", 2), 1, 0), 5));
        Assert.AreEqual(pending, mapChange.QueryPending().Single());
        var sameMap = mapChange.ScheduleAt("test.merge", With(Npc("b", 1), 1, 0), 5);
        Assert.AreEqual(GameEventScheduleDisposition.Merged, sameMap.Disposition, "a merge within the map is accepted");
        Assert.AreEqual("b", sameMap.Event!.Payload.Entities[0].Code);
    }

    [TestMethod]
    public void RemovalAndQueriesGoByKindAndMatch()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        Added(queue.ScheduleAt(Call, With(Npc("a"), 7), 30));
        var early = Added(queue.ScheduleAt(Call, With(Npc("a"), 7), 10));
        Added(queue.ScheduleAt(Call, With(Npc("b"), 7), 20));
        Added(queue.ScheduleAt(Global, Values(7), 5));

        Assert.IsTrue(queue.Any(Call));
        Assert.IsTrue(queue.Any(Call, e => e.Payload.Entities[0].Code == "b"));
        Assert.IsFalse(queue.Any(Tick));
        // Exactly one, the first in queue order (TE4).
        Assert.AreEqual(early, queue.RemoveFirst(Call, e => e.Payload.Entities[0].Code == "a"));
        Assert.AreEqual(3, queue.Count);
        Assert.IsNull(queue.RemoveFirst(Call, e => e.Payload.Entities[0].Code == "z"));
        var removed = queue.RemoveAll(Call, _ => true);
        CollectionAssert.AreEqual(new long[] { 20, 30 }, removed.Select(e => e.DeadlineMs).ToArray());
        Assert.AreEqual(Global, queue.QueryPending().Single().Kind);
        Assert.ThrowsExactly<ArgumentException>(() => queue.Any("test.none"));
        Assert.ThrowsExactly<ArgumentException>(() => queue.RemoveAll("test.none", _ => true));

        // A replace-kind's key slot frees when its event is removed.
        Added(queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("v")]), 1));
        queue.RemoveAll(Tick, _ => true);
        Assert.AreEqual(GameEventScheduleDisposition.Added, queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("v")]), 1).Disposition);
    }

    [TestMethod]
    public void EntityRelocationIsOnlyForGlobalAnyNumberKinds()
    {
        var queue = Queue(new Recorder());
        Added(queue.ScheduleAt(Pair, GameEventPayload.Of([Npc("virgil"), Npc("wolf")]), 30));
        // Scoped or keyed kinds keep the whole-event rule of Relocate (ARC-SCRIPT-010 uses it for its global call kind,
        // tested with the session).
        foreach (var kind in new[] { Pair, Call, Tick, Recharge, "missing" })
            Assert.AreEqual(GameEventFailure.UnknownKind, queue.RelocateEntities(kind, 1, 2, _ => true).Failure, kind);
        Assert.AreEqual(GameEventFailure.InvalidScope, queue.RelocateEntities(Global, 0, 2, _ => true).Failure);
        Assert.AreEqual((GameEventFailure.None, 0), (queue.RelocateEntities(Global, 1, 2, _ => true).Failure, queue.RelocateEntities(Global, 1, 2, _ => true).Moved.Length));
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.RelocateEntities(Global, 1, 2, null!));
    }

    [TestMethod]
    public void RelocationMovesOnlyEventsWhoseEntitiesAllTravel()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        var tick = Added(queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil")]), 10));
        var stays = Added(queue.ScheduleAt(Call, With(Npc("wolf"), 1), 20));
        var pair = Added(queue.ScheduleAt(Pair, GameEventPayload.Of([Npc("virgil"), Npc("wolf")]), 30));
        var global = Added(queue.ScheduleAt(Recharge, With(Npc("virgil"), 5), 40));
        var elsewhere = Added(queue.ScheduleAt(Call, With(Npc("virgil", 3), 1), 50));

        var result = queue.Relocate(1, 2, entity => entity.Code == "virgil");
        Assert.AreEqual(GameEventFailure.None, result.Failure);
        // Same sequence and deadline; a scoped event takes the new map, a global one keeps scope 0.
        CollectionAssert.AreEqual(new[]
        {
            tick with { ScopeMap = 2, Payload = GameEventPayload.Of([Npc("virgil", 2)]) },
            global with { Payload = With(Npc("virgil", 2), 5) },
        }, result.Moved.ToArray());
        CollectionAssert.Contains(queue.QueryPending().ToArray(), stays);
        CollectionAssert.Contains(queue.QueryPending().ToArray(), pair, "one of its entities stays behind");
        CollectionAssert.Contains(queue.QueryPending().ToArray(), elsewhere);
        // The key index follows: the moved tick replaces on its new map, a new one on the old map is added.
        Assert.AreEqual(GameEventScheduleDisposition.Replaced, queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil", 2)]), 1).Disposition);
        Assert.AreEqual(GameEventScheduleDisposition.Added, queue.ScheduleAt(Tick, GameEventPayload.Of([Npc("virgil")]), 1).Disposition);

        // A collision moves nothing.
        var before = queue.QueryPending();
        Assert.AreEqual(GameEventFailure.DuplicateKey, queue.Relocate(1, 2, entity => entity.Code == "virgil").Failure);
        CollectionAssert.AreEqual(before.ToArray(), queue.QueryPending().ToArray());
        Assert.AreEqual(GameEventFailure.None, queue.Relocate(4, 4, _ => true).Failure);
        Assert.AreEqual(GameEventFailure.InvalidScope, queue.Relocate(0, 2, _ => true).Failure);

        recorder.OnFire = (_, q) =>
        {
            Assert.AreEqual(GameEventFailure.PassInProgress, q.Relocate(2, 1, _ => true).Failure);
            return GameEventHandlerOutcome.Completed;
        };
        Run(queue, Pass(Game(1), activeMap: 2));
        Assert.IsTrue(recorder.Fired.Count > 0);
    }

    [TestMethod]
    public void HandlersMayCancelButNotNestPassesOrSnapshots()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        GameEventPassResult? nested = null;
        recorder.OnFire = (firing, q) =>
        {
            nested = q.ProcessDue(Pass(Game(100)));
            Assert.ThrowsExactly<InvalidOperationException>(() => q.CaptureSnapshot());
            q.RemoveAll(Global, e => e.Payload.Values[0] == 2);
            return GameEventHandlerOutcome.Completed;
        };
        Added(queue.ScheduleAt(Global, Values(1), 1));
        Added(queue.ScheduleAt(Global, Values(2), 2));
        Added(queue.ScheduleAt(Global, Values(3), 3));

        var result = Run(queue, Pass(Game(100)));
        Assert.AreEqual(GameEventFailure.PassInProgress, nested!.Failure);
        // The cancelled due event never fires; the pass continues after it.
        CollectionAssert.AreEqual(new long[] { 1, 3 }, recorder.FirstValues);
        Assert.AreEqual(2, result.Fired);

        recorder.OnFire = (_, _) => throw new InvalidOperationException("handler failure");
        Added(queue.ScheduleAt(Global, Values(4), 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => queue.ProcessDue(Pass(Game(100))));
        Assert.AreEqual(0, queue.Count, "the event left the queue before its handler ran");
        recorder.OnFire = null;
        Assert.AreEqual(GameEventFailure.None, queue.ProcessDue(Pass(Game(100))).Failure, "the pass flag was cleared");
    }

    [TestMethod]
    public void SchedulingRefusesBadRequestsWithoutSideEffects()
    {
        var recorder = new Recorder();
        var queue = Queue(recorder);
        Refused(GameEventFailure.UnknownKind, queue.ScheduleAt("test.none", Values(1), 1));
        Refused(GameEventFailure.UnknownKind, queue.ScheduleAfter(null!, Values(1), 1, Zero));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Global, Values(), 1));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Global, Values(1, 2), 1));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Global, null!, 1));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Global, new GameEventPayload(default, [1]), 1));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Call, With(Npc("a", 0), 1), 1));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Call, With(Npc("bad code"), 1), 1));
        Refused(GameEventFailure.InvalidPayload, queue.ScheduleAt(Call, With(Npc(new string('a', 129)), 1), 1));
        Refused(GameEventFailure.InvalidDeadline, queue.ScheduleAt(Global, Values(1), -1));
        Refused(GameEventFailure.InvalidDelay, queue.ScheduleAfter(Global, Values(1), -1, Zero));
        Refused(GameEventFailure.InvalidClocks, queue.ScheduleAfter(Global, Values(1), 1, new GameEventClocks(-1, 0, 0)));
        Refused(GameEventFailure.TimeOverflow, queue.ScheduleAfter(Global, Values(1), long.MaxValue, Game(1)));
        Assert.AreEqual(long.MaxValue, Added(queue.ScheduleAfter(Global, Values(1), long.MaxValue - 1, Game(1))).DeadlineMs);
        Assert.AreEqual(1, queue.Count);
        Assert.AreEqual(2, queue.NextSequence);
        Assert.AreEqual(GameEventFailure.InvalidPass, queue.ProcessDue(Pass(new GameEventClocks(0, -1, 0))).Failure);
        Assert.AreEqual(GameEventFailure.InvalidPass, queue.ProcessDue(null!).Failure);
        Assert.AreEqual(GameEventFailure.InvalidPass, queue.ProcessDue(new GameEventPassRequest(Zero, 1, 1, null!)).Failure);

        var full = Queue(recorder, new GameEventQueueLimits(MaxPending: 2, MaxPerPass: 2));
        Added(full.ScheduleAt(Global, Values(1), 1));
        Added(full.ScheduleAt(Tick, GameEventPayload.Of([Npc("v")]), 1));
        Refused(GameEventFailure.QueueFull, full.ScheduleAt(Global, Values(2), 1));
        Assert.AreEqual(GameEventScheduleDisposition.Replaced, full.ScheduleAt(Tick, GameEventPayload.Of([Npc("v")]), 2).Disposition,
            "a replacement needs no free slot");
    }

    [TestMethod]
    public void RegistryAndLimitsRefuseProgrammingErrors()
    {
        var h = new Recorder();
        GameEventKindDefinition Kind(string name = "k", GameEventUniqueness u = GameEventUniqueness.Many,
            GameEventScopeRule s = GameEventScopeRule.Global, int entities = 0, int values = 0, int keyE = 0, int keyV = 0,
            GameEventMerge? merge = null, GameEventDomain d = GameEventDomain.Game) =>
            new(name, d, GameEventPersistence.Saved, u, s, entities, values, keyE, keyV, h, merge);

        Assert.AreEqual(new GameEventQueueLimits(16_384, 500, 4, 8), GameEventQueueLimits.Default);
        Assert.AreEqual(1, new GameEventRegistry(3, [Kind()]).Kinds.Length);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameEventRegistry(0, [Kind()]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, []));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(), Kind()]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind("a|b")]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(d: (GameEventDomain)4)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(entities: -1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(u: GameEventUniqueness.Replace, keyE: 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(values: 1, keyV: 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(u: GameEventUniqueness.Merge)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(merge: (p, _) => p)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1, [Kind(s: GameEventScopeRule.EntityMap)]));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventRegistry(1,
            [new("k", GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.Global, 0, 0, 0, 0, null!)]));

        var wide = new GameEventRegistry(1, [Kind(values: 9)]);
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventQueue(wide));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventQueue(new GameEventRegistry(1, [Kind()]), new GameEventQueueLimits(MaxPending: 0)));
        Assert.ThrowsExactly<ArgumentException>(() => new GameEventQueue(new GameEventRegistry(1, [Kind()]), new GameEventQueueLimits(MaxPending: 4, MaxPerPass: 5)));
    }

    [TestMethod]
    public void RealSecondDelaysAreCheckedProducts()
    {
        Assert.IsTrue(GameEventDelays.TryFromRealSeconds(0, out var zero));
        Assert.AreEqual(0, zero);
        Assert.IsTrue(GameEventDelays.TryFromRealSeconds(60, out var minute));
        Assert.AreEqual(480_000, minute);
        // The source's 32-bit word wraps above 536,870 s (TE1); the project product does not.
        Assert.IsTrue(GameEventDelays.TryFromRealSeconds(600_000, out var large));
        Assert.AreEqual(4_800_000_000, large);
        Assert.IsTrue(GameEventDelays.TryFromRealSeconds(long.MaxValue / 8_000, out _));
        Assert.IsFalse(GameEventDelays.TryFromRealSeconds(long.MaxValue / 8_000 + 1, out var overflow));
        Assert.AreEqual(0, overflow);
        Assert.IsFalse(GameEventDelays.TryFromRealSeconds(-1, out _));
    }
}
