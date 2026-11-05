using Arcanum.Simulation.Residency;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Residency.SectorCacheRules;

namespace Arcanum.Simulation.Tests.Residency;

// ARC-DEV-102 (MIG9 S2): the sector cache rules of residency §3.3 and the S2 read of the executable. Sector ids, extents and times
// are invented.
[TestClass]
public sealed class SectorCacheRulesTests
{
    private const long Day = 86_400_000;
    private static readonly SectorMapExtent Extent = new(64, 64);

    private static SectorCacheState Opened(SectorCacheLimits? limits = null)
    {
        var change = Open(SectorCacheState.Initial(limits ?? SectorCacheLimits.Default), Extent);
        Assert.AreEqual(SectorCacheFailure.None, change.Failure);
        return change.State;
    }

    private static SectorCacheState Locked(SectorCacheState state, long sector, long nowMs, SectorLockStatus expected)
    {
        var result = Lock(state, sector, nowMs);
        Assert.AreEqual(expected, result.Outcome.Status, $"sector {sector} at {nowMs}");
        return result.State;
    }

    private static SectorCacheState Filled(SectorCacheState state, int count, long nowMs = 0, long row = 0)
    {
        for (var i = 0; i < count; i++) state = Locked(state, SectorId(i, row), nowMs, SectorLockStatus.Loaded);
        return state;
    }

    private static SectorCacheState Limited(int slots)
    {
        Assert.AreEqual(SectorCacheLimitsFailure.None, SectorCacheLimits.TryCreate(slots, out var limits));
        return Opened(limits);
    }

    [TestMethod]
    public void Limits_accept_8_to_128_slots_and_refuse_the_rest_without_clamping()
    {
        Assert.AreEqual(16, SectorCacheLimits.Default.Slots);
        Assert.AreEqual(32, SectorCacheLimits.Default.HistoryCapacity);
        foreach (var bad in new[] { int.MinValue, -1, 0, 7, 129, int.MaxValue })
        {
            Assert.AreEqual(SectorCacheLimitsFailure.SlotsOutOfRange, SectorCacheLimits.TryCreate(bad, out var none), $"{bad}");
            Assert.IsNull(none);
        }
        foreach (var good in new[] { 8, 16, 128 })
        {
            Assert.AreEqual(SectorCacheLimitsFailure.None, SectorCacheLimits.TryCreate(good, out var limits));
            Assert.AreEqual(good, limits!.Slots);
            Assert.AreEqual(2 * good, limits.HistoryCapacity);
        }
        SectorCacheLimits.TryCreate(16, out var sixteen);
        Assert.AreSame(SectorCacheLimits.Default, sixteen);
    }

    [TestMethod]
    public void Sector_ids_pack_x_and_y_as_the_original()
    {
        Assert.AreEqual(0L, SectorId(0, 0));
        Assert.AreEqual(5L | (7L << 26), SectorId(5, 7));
        Assert.AreEqual((1L << 52) - 1, SectorId((1 << 26) - 1, (1 << 26) - 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SectorId(-1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SectorId(0, 1L << 26));
    }

    [TestMethod]
    public void A_miss_loads_and_folds_without_a_record_and_a_hit_refreshes_the_age()
    {
        var state = Opened();
        var first = Lock(state, SectorId(3, 4), 1_000);
        Assert.AreEqual(new SectorLockOutcome(SectorId(3, 4), SectorLockStatus.Loaded, SectorCacheFailure.None, null, true, null), first.Outcome);
        Assert.IsTrue(first.State.IsLoaded(SectorId(3, 4)));
        Assert.AreEqual(1, first.State.Slots.Single().Age);

        var again = Lock(first.State, SectorId(3, 4), 1_005);
        Assert.AreEqual(new SectorLockOutcome(SectorId(3, 4), SectorLockStatus.Hit, SectorCacheFailure.None, null, false, null), again.Outcome);
        Assert.AreEqual(2, again.State.Slots.Single().Age);
        Assert.AreEqual(2, again.State.AgeCounter);
        Assert.AreEqual(0, again.State.History.Length);
    }

    [TestMethod]
    public void Slots_stay_in_ascending_id_order()
    {
        var state = Opened();
        foreach (var sector in new[] { SectorId(9, 1), SectorId(2, 0), SectorId(5, 3), SectorId(0, 2) })
            state = Locked(state, sector, 0, SectorLockStatus.Loaded);
        CollectionAssert.AreEqual(new[] { SectorId(2, 0), SectorId(9, 1), SectorId(0, 2), SectorId(5, 3) },
            state.Slots.Select(s => s.Sector).ToArray());
    }

    [TestMethod]
    public void A_miss_on_a_full_cache_evicts_the_slot_of_least_age_into_the_history()
    {
        var state = Filled(Opened(), 16, 100);
        // Sector 0 was locked first; touching it makes sector 1 the least recent.
        state = Locked(state, SectorId(0, 0), 200, SectorLockStatus.Hit);
        var result = Lock(state, SectorId(20, 0), 300);
        Assert.AreEqual(SectorLockStatus.Loaded, result.Outcome.Status);
        Assert.AreEqual(SectorId(1, 0), result.Outcome.Evicted);
        Assert.IsTrue(result.Outcome.Fold);
        Assert.AreEqual(16, result.State.Slots.Length);
        Assert.IsFalse(result.State.IsLoaded(SectorId(1, 0)));
        Assert.IsTrue(result.State.IsLoaded(SectorId(0, 0)));
        Assert.AreEqual(new SectorEvictionRecord(SectorId(1, 0), 300), result.State.History.Single());
        // The next miss takes the next least recent.
        var next = Lock(result.State, SectorId(21, 0), 400);
        Assert.AreEqual(SectorId(2, 0), next.Outcome.Evicted);
    }

    [TestMethod]
    public void A_return_folds_at_3000_ms_away_and_not_at_2999()
    {
        foreach (var (away, fold) in new[] { (2_999L, false), (3_000L, true), (60_000L, true), (0L, false) })
        {
            var state = Locked(Opened(), SectorId(1, 1), 10_000, SectorLockStatus.Loaded);
            state = EvictUnlocked(state, 20_000).State;
            var back = Lock(state, SectorId(1, 1), 20_000 + away);
            Assert.AreEqual(SectorLockStatus.Loaded, back.Outcome.Status);
            Assert.AreEqual(fold, back.Outcome.Fold, $"away {away}");
            Assert.AreEqual(20_000L, back.Outcome.EvictedAtMs);
            // The load takes the record out of the history.
            Assert.AreEqual(0, back.State.History.Length, $"away {away}");
        }
    }

    [TestMethod]
    public void FoldDue_compares_the_day_and_millisecond_words_separately()
    {
        Assert.IsFalse(FoldDue(5 * Day + 1_000, 5 * Day + 3_999));
        Assert.IsTrue(FoldDue(5 * Day + 1_000, 5 * Day + 4_000));
        // Across midnight the day words differ: a short absence folds.
        Assert.IsTrue(FoldDue(6 * Day - 1_000, 6 * Day + 500));
        Assert.IsTrue(FoldDue(6 * Day - 1, 6 * Day));
        // A longer absence on the next day folds too, whatever the millisecond words.
        Assert.IsTrue(FoldDue(5 * Day + 50_000, 6 * Day + 10));
        // A time before the eviction (never in play) wraps the millisecond word and folds.
        Assert.IsTrue(FoldDue(5 * Day + 4_000, 5 * Day + 3_000));
        Assert.IsTrue(FoldDue(6 * Day, 5 * Day + 3_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FoldDue(-1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FoldDue(0, -1));
    }

    [TestMethod]
    public void A_return_across_midnight_folds_through_the_lock()
    {
        var state = Locked(Opened(), SectorId(1, 1), 0, SectorLockStatus.Loaded);
        state = EvictUnlocked(state, 3 * Day - 1_000).State;
        Assert.IsTrue(Lock(state, SectorId(1, 1), 3 * Day + 500).Outcome.Fold);
    }

    [TestMethod]
    public void The_history_keeps_twice_the_slots_and_drops_the_oldest()
    {
        var state = Opened();
        // Three passes of sixteen sectors each: 48 records offered, the last 32 kept, oldest first.
        for (var pass = 0; pass < 3; pass++)
        {
            for (var i = 0; i < 16; i++) state = Locked(state, SectorId(i, pass), pass * 10_000, SectorLockStatus.Loaded);
            state = EvictUnlocked(state, pass * 10_000 + 1).State;
        }
        Assert.AreEqual(32, state.History.Length);
        CollectionAssert.AreEqual(Enumerable.Range(0, 16).Select(i => SectorId(i, 1)).Concat(Enumerable.Range(0, 16).Select(i => SectorId(i, 2))).ToArray(),
            state.History.Select(r => r.Sector).ToArray());
        // A dropped sector folds as one with no record.
        var dropped = Lock(state, SectorId(0, 0), 30_000);
        Assert.IsNull(dropped.Outcome.EvictedAtMs);
        Assert.IsTrue(dropped.Outcome.Fold);
    }

    [TestMethod]
    public void A_miss_frees_its_victim_before_the_lookup_so_a_full_history_can_drop_the_new_sectors_record()
    {
        var state = Opened();
        // History: sector (0, 9) first, then 31 others; full.
        state = Locked(state, SectorId(0, 9), 0, SectorLockStatus.Loaded);
        state = EvictUnlocked(state, 1_000).State;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < 15; i++) state = Locked(state, SectorId(i, pass), 1_000, SectorLockStatus.Loaded);
            state = EvictUnlocked(state, 1_000).State;
        }
        Assert.AreEqual(31, state.History.Length);
        state = Locked(state, SectorId(15, 5), 1_000, SectorLockStatus.Loaded);
        state = EvictUnlocked(state, 1_000).State;
        Assert.AreEqual(32, state.History.Length);
        Assert.AreEqual(SectorId(0, 9), state.History[0].Sector);
        // Fill the cache with sectors that have no record, then load (0, 9) one second later: the victim's record goes in first
        // and pushes (0, 9)'s own record out, so it folds although it was away only a second.
        state = Filled(state, 16, 1_500, row: 3);
        Assert.AreEqual(32, state.History.Length);
        var back = Lock(state, SectorId(0, 9), 2_000);
        Assert.AreEqual(SectorId(0, 3), back.Outcome.Evicted);
        Assert.IsNull(back.Outcome.EvictedAtMs);
        Assert.IsTrue(back.Outcome.Fold);
    }

    [TestMethod]
    public void A_loaded_sector_never_keeps_a_record_across_a_game_load_close()
    {
        var state = Locked(Opened(), SectorId(2, 2), 0, SectorLockStatus.Loaded);
        state = Close(state, 1_000, SectorMapClose.GameLoad).State;
        Assert.AreEqual(new SectorEvictionRecord(SectorId(2, 2), 1_000), state.History.Single());
        state = Open(state, Extent).State;
        var back = Lock(state, SectorId(2, 2), 3_500);
        Assert.AreEqual(1_000L, back.Outcome.EvictedAtMs);
        Assert.IsFalse(back.Outcome.Fold);
        Assert.AreEqual(0, back.State.History.Length);
        state = EvictUnlocked(back.State, 9_000).State;
        Assert.AreEqual(new SectorEvictionRecord(SectorId(2, 2), 9_000), state.History.Single());
    }

    [TestMethod]
    public void The_emptying_pass_evicts_in_ascending_id_and_keeps_the_map_open()
    {
        var state = Opened();
        foreach (var sector in new[] { SectorId(9, 1), SectorId(2, 0), SectorId(5, 3) }) state = Locked(state, sector, 0, SectorLockStatus.Loaded);
        var change = EvictUnlocked(state, 7_000);
        Assert.AreEqual(SectorCacheFailure.None, change.Failure);
        CollectionAssert.AreEqual(new[] { SectorId(2, 0), SectorId(9, 1), SectorId(5, 3) }, change.Evicted.ToArray());
        CollectionAssert.AreEqual(change.Evicted.ToArray(), change.State.History.Select(r => r.Sector).ToArray());
        Assert.IsTrue(change.State.History.All(r => r.EvictedAtMs == 7_000));
        Assert.AreEqual(0, change.State.Slots.Length);
        Assert.IsTrue(change.State.MapOpen);
        // An empty cache gives the same state back.
        Assert.AreSame(change.State, EvictUnlocked(change.State, 8_000).State);
    }

    [TestMethod]
    public void A_map_close_clears_the_history_unless_a_game_is_being_loaded()
    {
        var state = Locked(Opened(), SectorId(1, 0), 0, SectorLockStatus.Loaded);
        state = EvictUnlocked(state, 500).State;
        state = Locked(state, SectorId(2, 0), 600, SectorLockStatus.Loaded);

        var other = Close(state, 1_000, SectorMapClose.OtherMap);
        Assert.AreEqual(SectorCacheFailure.None, other.Failure);
        CollectionAssert.AreEqual(new[] { SectorId(2, 0) }, other.Evicted.ToArray());
        Assert.IsFalse(other.State.MapOpen);
        Assert.AreEqual(0, other.State.Slots.Length);
        Assert.AreEqual(0, other.State.History.Length);

        var load = Close(state, 1_000, SectorMapClose.GameLoad);
        CollectionAssert.AreEqual(new[] { new SectorEvictionRecord(SectorId(1, 0), 500), new SectorEvictionRecord(SectorId(2, 0), 1_000) },
            load.State.History.ToArray());
        Assert.IsFalse(load.State.MapOpen);

        // Closing with no map open does nothing; locking needs a map.
        Assert.AreSame(other.State, Close(other.State, 2_000, SectorMapClose.OtherMap).State);
        Assert.AreEqual(SectorCacheFailure.NoMapOpen, Lock(other.State, SectorId(1, 0), 2_000).Outcome.Failure);
        // Reopening keeps the history and the counter.
        var reopened = Open(load.State, Extent).State;
        Assert.AreEqual(2, reopened.History.Length);
        Assert.AreEqual(load.State.AgeCounter, reopened.AgeCounter);
    }

    [TestMethod]
    public void Refusals_are_typed_and_leave_the_state_as_given()
    {
        var initial = SectorCacheState.Initial(SectorCacheLimits.Default);
        var noMap = Lock(initial, 0, 0);
        Assert.AreEqual(new SectorLockOutcome(0, SectorLockStatus.Refused, SectorCacheFailure.NoMapOpen, null, false, null), noMap.Outcome);
        Assert.AreSame(initial, noMap.State);

        foreach (var bad in new[] { new SectorMapExtent(0, 4), new SectorMapExtent(4, -1), new SectorMapExtent(4, (1L << 26) + 1) })
        {
            var refused = Open(initial, bad);
            Assert.AreEqual(SectorCacheFailure.InvalidExtent, refused.Failure, $"{bad}");
            Assert.AreSame(initial, refused.State);
        }
        Assert.AreEqual(SectorCacheFailure.None, Open(initial, new SectorMapExtent(1L << 26, 1L << 26)).Failure);

        var state = Opened();
        var twice = Open(state, Extent);
        Assert.AreEqual(SectorCacheFailure.MapAlreadyOpen, twice.Failure);
        Assert.AreSame(state, twice.State);
        foreach (var sector in new[] { -1L, SectorId(64, 0), SectorId(0, 64), SectorId(63, 63) + (1L << 52) })
        {
            var result = Lock(state, sector, 0);
            Assert.AreEqual(SectorCacheFailure.OutOfBounds, result.Outcome.Failure, $"{sector}");
            Assert.AreSame(state, result.State);
        }
        Assert.AreEqual(SectorLockStatus.Loaded, Lock(state, SectorId(63, 63), 0).Outcome.Status);

        var badTime = Lock(state, SectorId(1, 1), -1);
        Assert.AreEqual(SectorCacheFailure.InvalidTime, badTime.Outcome.Failure);
        Assert.AreSame(state, badTime.State);
        var loaded = Lock(state, SectorId(2, 2), 0).State;
        foreach (var change in new[] { EvictUnlocked(loaded, -1), Close(loaded, -1, SectorMapClose.OtherMap) })
        {
            Assert.AreEqual(SectorCacheFailure.InvalidTime, change.Failure);
            Assert.AreSame(loaded, change.State);
            Assert.AreEqual(0, change.Evicted.Length);
        }
    }

    [TestMethod]
    public void A_batch_holds_its_sectors_until_it_returns_and_refuses_only_past_the_slot_count()
    {
        var state = Filled(Limited(8), 8, 0);
        // Nine distinct sectors in one call: the eight fresh ones evict the eight old ones, the ninth finds every slot held.
        var sectors = Enumerable.Range(0, 9).Select(i => SectorId(i, 5)).ToArray();
        var batch = LockAll(state, sectors, 1_000);
        Assert.AreEqual(9, batch.Outcomes.Length);
        Assert.IsTrue(batch.Outcomes.Take(8).All(o => o.Status == SectorLockStatus.Loaded && o.Evicted is not null));
        Assert.AreEqual(new SectorLockOutcome(SectorId(8, 5), SectorLockStatus.Refused, SectorCacheFailure.NoFreeSlot, null, false, null),
            batch.Outcomes[8]);
        CollectionAssert.AreEquivalent(sectors.Take(8).ToArray(), batch.State.Slots.Select(s => s.Sector).ToArray());
        Assert.AreEqual(8, batch.State.History.Length);
        // The counter stamps only the ages it gives: 8 loads before, 8 in the batch, none for the refused ninth.
        Assert.AreEqual(16, batch.State.AgeCounter);

        // A repeated sector is a hit, and a later sector never evicts one the batch already locked.
        var repeat = LockAll(Filled(Limited(8), 7, 0), [SectorId(1, 0), SectorId(1, 0), SectorId(20, 0), SectorId(21, 0)], 1_000);
        CollectionAssert.AreEqual(new[] { SectorLockStatus.Hit, SectorLockStatus.Hit, SectorLockStatus.Loaded, SectorLockStatus.Loaded },
            repeat.Outcomes.Select(o => o.Status).ToArray());
        Assert.IsNull(repeat.Outcomes[2].Evicted);
        Assert.AreEqual(SectorId(0, 0), repeat.Outcomes[3].Evicted);
        Assert.IsTrue(repeat.State.IsLoaded(SectorId(1, 0)));

        // A single lock holds nothing afterwards, so it never finds the cache without a free slot (each Locked call asserts Loaded).
        var single = Filled(Limited(8), 8, 0);
        for (var i = 0; i < 20; i++) single = Locked(single, SectorId(i, 7), 2_000 + i, SectorLockStatus.Loaded);

        // Refusals inside a batch stand alone; a batch with no change gives the state back.
        var refusals = LockAll(state, [-1, SectorId(99, 0)], 1_000);
        Assert.IsTrue(refusals.Outcomes.All(o => o.Failure == SectorCacheFailure.OutOfBounds));
        Assert.AreSame(state, refusals.State);
    }

    [TestMethod]
    public void The_rules_are_deterministic_and_never_change_the_state_they_are_given()
    {
        static (long[] Slots, SectorEvictionRecord[] History, long Counter, SectorLockOutcome[] Outcomes) Run(SectorCacheState start)
        {
            var state = start;
            var outcomes = new List<SectorLockOutcome>();
            var time = 0L;
            for (var step = 0; step < 400; step++)
            {
                time += 100 + step % 5 * 900;
                var sector = SectorId((step * step + step / 7) % 29, 0);
                var result = Lock(state, sector, time);
                outcomes.Add(result.Outcome);
                state = result.State;
                if (step % 97 == 96) state = EvictUnlocked(state, time).State;
            }
            return (state.Slots.Select(s => s.Sector).ToArray(), state.History.ToArray(), state.AgeCounter, outcomes.ToArray());
        }

        var start = Opened();
        var first = Run(start);
        var second = Run(start);
        CollectionAssert.AreEqual(first.Slots, second.Slots);
        CollectionAssert.AreEqual(first.History, second.History);
        CollectionAssert.AreEqual(first.Outcomes, second.Outcomes);
        Assert.AreEqual(first.Counter, second.Counter);
        Assert.AreEqual(0, start.Slots.Length);
        Assert.AreEqual(0, start.History.Length);
        Assert.IsTrue(first.Slots.Length <= 16 && first.History.Length <= 32);
        // The run meets every case: hits, folding loads and loads back within 3 s.
        Assert.IsTrue(first.Outcomes.Any(o => o.Status == SectorLockStatus.Hit));
        Assert.IsTrue(first.Outcomes.Any(o => o.Status == SectorLockStatus.Loaded && o.Fold));
        Assert.IsTrue(first.Outcomes.Any(o => o.Status == SectorLockStatus.Loaded && !o.Fold));
    }
}
