using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Arcanum.Simulation.Residency;

/// <summary>
/// ARC-DEV-102 (MIG9 S2, ledger RL2, RL7, RL8, RL20, RL26): the sector cache of the retail 1.0.7.4 executable as pure rules over a
/// <see cref="SectorCacheState"/>. A lock refreshes a loaded sector's age or loads it, evicting the slot of least age from a full
/// cache into the eviction history, and says whether the loaded sector folds. Locks do not outlive a call (residency §3.3): a single
/// <see cref="Lock"/> holds nothing afterwards, and <see cref="LockAll"/> holds its sectors only until it returns, so only a batch
/// with more distinct sectors than slots can find no free slot. No caller yet: the session takes the state in slice S4.
/// </summary>
public static class SectorCacheRules
{
    /// <summary>The fold's away time (RL8): a sector folds when it was away at least this long, or on another game day.</summary>
    public const long FoldAwayMs = 3_000;

    private const long DayMs = 86_400_000;
    private const long CoordinateMask = SectorMapExtent.MaximumPerAxis - 1;

    /// <summary>The original's sector id of sector (<paramref name="x"/>, <paramref name="y"/>): <c>X | Y &lt;&lt; 26</c>.</summary>
    public static long SectorId(long x, long y)
    {
        if (x is < 0 or > CoordinateMask) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < 0 or > CoordinateMask) throw new ArgumentOutOfRangeException(nameof(y));
        return x | (y << 26);
    }

    /// <summary>
    /// Whether a sector evicted at <paramref name="evictedAtMs"/> folds when it loads at <paramref name="nowMs"/> (RL8); both are game
    /// times of 0 or more. The original keeps game time as two unsigned words {day, millisecond of the day}: the lock subtracts the
    /// eviction's pair from the clock word by word when it takes the record (0x45A7D0) and the load step compares the difference with
    /// {0, 3000} (0x45A9B0 at both of its sites): the sector folds when the day words differ, or, on the same day, when the
    /// millisecond words differ by at least 3000 as an unsigned number. So a return across midnight folds however short the absence,
    /// and a time before the eviction (never in play) folds too. One instant differs: the original's clock add (0x45AA10) can hold
    /// {d, 86,400,000}, which the session's single timestamp stores as {d + 1, 0}.
    /// </summary>
    [OriginalFunction(0x45A7D0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45A9B0, OriginalFunctionScope.Partial)]
    public static bool FoldDue(long evictedAtMs, long nowMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(evictedAtMs);
        ArgumentOutOfRangeException.ThrowIfNegative(nowMs);
        return unchecked((uint)(nowMs / DayMs) != (uint)(evictedAtMs / DayMs) ||
            (uint)(nowMs % DayMs - evictedAtMs % DayMs) >= FoldAwayMs);
    }

    /// <summary>
    /// Opens a map of <paramref name="extent"/> on a state with no map open: the slots stay empty, the history and the counter are
    /// kept (a game load restores its history before the open; any other close has already cleared it).
    /// </summary>
    public static SectorCacheChange Open(SectorCacheState state, SectorMapExtent extent)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.MapOpen) return Refuse(state, SectorCacheFailure.MapAlreadyOpen);
        if (!extent.Valid) return Refuse(state, SectorCacheFailure.InvalidExtent);
        return new(new SectorCacheState(state.Limits, extent, [], state.AgeCounter, state.History), SectorCacheFailure.None, []);
    }

    /// <summary>
    /// Locks one sector at game time <paramref name="nowMs"/> (0x4D0540, refusal order: time, map open, bounds): a hit refreshes the age; a
    /// miss on a full cache evicts the slot of least age (its record enters the history first, dropping the oldest record when the
    /// history is full), then takes the sector's oldest history record out of the history and decides the fold from it.
    /// </summary>
    [OriginalFunction(0x4D0540, OriginalFunctionScope.Partial)]
    public static SectorLockResult Lock(SectorCacheState state, long sector, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(state);
        var work = new Work(state);
        var outcome = work.Lock(sector, nowMs, held: null);
        return new(outcome.Status == SectorLockStatus.Refused ? state : work.ToState(), outcome);
    }

    /// <summary>
    /// Locks <paramref name="sectors"/> in order, as one call that holds every sector it locked until it returns (the original's draw
    /// locks all view sectors at once): a sector locked earlier in the batch is never evicted by a later one, a repeated sector is a
    /// hit. Each refusal stands alone; the batch goes on with the next sector.
    /// </summary>
    public static SectorLockBatchResult LockAll(SectorCacheState state, IReadOnlyList<long> sectors, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sectors);
        var work = new Work(state);
        var held = new HashSet<long>();
        var outcomes = ImmutableArray.CreateBuilder<SectorLockOutcome>(sectors.Count);
        foreach (var sector in sectors) outcomes.Add(work.Lock(sector, nowMs, held));
        return new(work.Changed ? work.ToState() : state, outcomes.MoveToImmutable());
    }

    /// <summary>
    /// Evicts every slot at game time <paramref name="nowMs"/>, ascending id, each into the history (0x4D0BC0 with argument 0, the
    /// pass that empties the cache for a map flush, a teleport within the map and a world-map landing; its save-only mode is not
    /// modelled). The map stays open. The original evicts only slots no caller holds and saves a dirty sector first; here no lock
    /// outlives a call and an eviction saves nothing (residency §4).
    /// </summary>
    [OriginalFunction(0x4D0BC0, OriginalFunctionScope.Partial)]
    public static SectorCacheChange EvictUnlocked(SectorCacheState state, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (nowMs < 0) return Refuse(state, SectorCacheFailure.InvalidTime);
        var work = new Work(state);
        var evicted = work.EvictAll(nowMs);
        return new(evicted.Length == 0 ? state : work.ToState(), SectorCacheFailure.None, evicted);
    }

    /// <summary>
    /// Closes the open map at game time <paramref name="nowMs"/> (the sector module's close hook 0x4CF320): every slot is evicted into
    /// the history, ascending id (0x4D0B40), then the history is cleared unless a game is being loaded. Closing with no map open does
    /// nothing, as the original's close does when its closed flag is already set.
    /// </summary>
    [OriginalFunction(0x4CF320, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4D0B40, OriginalFunctionScope.Partial)]
    public static SectorCacheChange Close(SectorCacheState state, long nowMs, SectorMapClose reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (nowMs < 0) return Refuse(state, SectorCacheFailure.InvalidTime);
        if (!state.MapOpen) return new(state, SectorCacheFailure.None, []);
        var work = new Work(state);
        var evicted = work.EvictAll(nowMs);
        var history = reason == SectorMapClose.GameLoad ? work.History.ToImmutableArray() : [];
        return new(new SectorCacheState(state.Limits, null, [], state.AgeCounter, history), SectorCacheFailure.None, evicted);
    }

    internal static int IndexOf(ImmutableArray<SectorCacheSlot> slots, long sector) => IndexOf(slots.AsSpan(), sector);

    private static int IndexOf(List<SectorCacheSlot> slots, long sector) => IndexOf(CollectionsMarshal.AsSpan(slots), sector);

    private static int IndexOf(ReadOnlySpan<SectorCacheSlot> slots, long sector)
    {
        int low = 0, high = slots.Length - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var at = slots[mid].Sector;
            if (at == sector) return mid;
            if (at < sector) low = mid + 1; else high = mid - 1;
        }
        return ~low;
    }

    private static SectorCacheChange Refuse(SectorCacheState state, SectorCacheFailure failure) => new(state, failure, []);

    private static SectorLockOutcome Refused(long sector, SectorCacheFailure failure) =>
        new(sector, SectorLockStatus.Refused, failure, null, false, null);

    /// <summary>Frees a slot (0x4D1400): its id and the game time go to the end of the history, the oldest record dropping out of a
    /// full one first. The parts' teardown (statics freed, mobiles detached) belongs to later slices.</summary>
    [OriginalFunction(0x4D1400, OriginalFunctionScope.Partial)]
    private static void Free(List<SectorCacheSlot> slots, List<SectorEvictionRecord> history, int capacity, int index, long nowMs)
    {
        if (history.Count >= capacity) history.RemoveAt(0);
        history.Add(new(slots[index].Sector, nowMs));
        slots.RemoveAt(index);
    }

    /// <summary>One call's working copy of a state.</summary>
    private sealed class Work(SectorCacheState state)
    {
        private readonly List<SectorCacheSlot> _slots = [.. state.Slots];
        private long _counter = state.AgeCounter;

        public List<SectorEvictionRecord> History { get; } = [.. state.History];

        public bool Changed { get; private set; }

        public SectorLockOutcome Lock(long sector, long nowMs, HashSet<long>? held)
        {
            if (nowMs < 0) return Refused(sector, SectorCacheFailure.InvalidTime);
            if (state.Map is not { } map) return Refused(sector, SectorCacheFailure.NoMapOpen);
            if (sector < 0 || (sector & CoordinateMask) >= map.SectorsX || (sector >> 26) >= map.SectorsY)
                return Refused(sector, SectorCacheFailure.OutOfBounds);
            var index = IndexOf(_slots, sector);
            if (index >= 0)
            {
                _slots[index] = new(sector, ++_counter);
                held?.Add(sector);
                Changed = true;
                return new(sector, SectorLockStatus.Hit, SectorCacheFailure.None, null, false, null);
            }
            long? evicted = null;
            if (_slots.Count >= state.Limits.Slots)
            {
                // The unheld slot of least age, the first in id order on a tie (the original's scan keeps the first minimum).
                var victim = -1;
                for (var i = 0; i < _slots.Count; i++)
                    if ((held is null || !held.Contains(_slots[i].Sector)) && (victim < 0 || _slots[i].Age < _slots[victim].Age)) victim = i;
                if (victim < 0) return Refused(sector, SectorCacheFailure.NoFreeSlot);
                evicted = _slots[victim].Sector;
                Evict(victim, nowMs);
            }
            // The load takes the sector's first (oldest) record out of the history, if any.
            long? evictedAt = null;
            var record = History.FindIndex(r => r.Sector == sector);
            if (record >= 0)
            {
                evictedAt = History[record].EvictedAtMs;
                History.RemoveAt(record);
            }
            _slots.Insert(~IndexOf(_slots, sector), new(sector, ++_counter));
            held?.Add(sector);
            Changed = true;
            return new(sector, SectorLockStatus.Loaded, SectorCacheFailure.None, evicted,
                evictedAt is not { } at || FoldDue(at, nowMs), evictedAt);
        }

        public ImmutableArray<long> EvictAll(long nowMs)
        {
            var evicted = _slots.Select(s => s.Sector).ToImmutableArray();
            while (_slots.Count > 0) Evict(0, nowMs);
            Changed |= evicted.Length > 0;
            return evicted;
        }

        public SectorCacheState ToState() =>
            new(state.Limits, state.Map, [.. _slots], _counter, [.. History]);

        private void Evict(int index, long nowMs) => Free(_slots, History, state.Limits.HistoryCapacity, index, nowMs);
    }
}
