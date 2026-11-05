using System.Collections.Immutable;

namespace Arcanum.Simulation.Residency;

/// <summary>Why a <see cref="SectorCacheLimits"/> was not made.</summary>
public enum SectorCacheLimitsFailure
{
    None,
    /// <summary>The slot count is outside <see cref="SectorCacheLimits.MinimumSlots"/>..<see cref="SectorCacheLimits.MaximumSlots"/>;
    /// it is refused, never clamped.</summary>
    SlotsOutOfRange,
}

/// <summary>
/// ARC-DEV-102 (MIG9 S2, ledger RL2, RL12, RL20): the bounds of a <see cref="SectorCacheState"/>. The original's cache has 16 slots
/// (its init accepts 8..128 and its only caller passes 16) and an eviction history twice the slot count.
/// </summary>
public sealed class SectorCacheLimits
{
    public const int MinimumSlots = 8, MaximumSlots = 128, DefaultSlots = 16;

    private SectorCacheLimits(int slots) => Slots = slots;

    /// <summary>The original's limits: 16 slots, 32 history records.</summary>
    public static SectorCacheLimits Default { get; } = new(DefaultSlots);

    /// <summary>Sectors held at once.</summary>
    public int Slots { get; }

    /// <summary>Eviction records kept: twice <see cref="Slots"/>; the oldest goes first when it is full.</summary>
    public int HistoryCapacity => 2 * Slots;

    /// <summary>Limits with <paramref name="slots"/> slots, or why not.</summary>
    public static SectorCacheLimitsFailure TryCreate(int slots, out SectorCacheLimits? limits)
    {
        limits = null;
        if (slots is < MinimumSlots or > MaximumSlots) return SectorCacheLimitsFailure.SlotsOutOfRange;
        limits = slots == DefaultSlots ? Default : new SectorCacheLimits(slots);
        return SectorCacheLimitsFailure.None;
    }
}

/// <summary>The open map's size in sectors; a sector id is <c>X | Y &lt;&lt; 26</c> with <c>X &lt; SectorsX</c> and
/// <c>Y &lt; SectorsY</c> (the original's bounds test in its lock).</summary>
public readonly record struct SectorMapExtent(long SectorsX, long SectorsY)
{
    /// <summary>Largest count per axis: coordinates are 26-bit.</summary>
    public const long MaximumPerAxis = 1L << 26;

    public bool Valid => SectorsX is >= 1 and <= MaximumPerAxis && SectorsY is >= 1 and <= MaximumPerAxis;
}

/// <summary>A loaded sector and its age stamp (the lock counter's value at its last lock; the least age is evicted first).</summary>
public readonly record struct SectorCacheSlot(long Sector, long Age);

/// <summary>An eviction history record: the sector and the game time (ms) at which it left the cache.</summary>
public readonly record struct SectorEvictionRecord(long Sector, long EvictedAtMs);

/// <summary>A typed refusal of a sector cache transition.</summary>
public enum SectorCacheFailure
{
    None,
    /// <summary>No map is open (the original's map-open flag is clear).</summary>
    NoMapOpen,
    /// <summary>The sector id is negative or outside the open map's extent.</summary>
    OutOfBounds,
    /// <summary>The cache is full and every slot is held by the same call (a batch with more distinct sectors than slots).</summary>
    NoFreeSlot,
    /// <summary>The game time is negative.</summary>
    InvalidTime,
    /// <summary><see cref="SectorCacheRules.Open"/> on a state that already has a map open: close it first.</summary>
    MapAlreadyOpen,
    /// <summary>The extent given to <see cref="SectorCacheRules.Open"/> is not <see cref="SectorMapExtent.Valid"/>.</summary>
    InvalidExtent,
    /// <summary>A saved history (<see cref="SectorCacheRules.RestoreHistory"/>) holds more records than twice the slot count (the original's load
    /// refuses a larger count, 0x4D12B0).</summary>
    HistoryTooLong,
    /// <summary>A saved history record names a sector that is not a sector id (negative, or a coordinate of 26 bits or more).</summary>
    InvalidSector,
    /// <summary>A saved history is not oldest first (an eviction time earlier than the record before it): the history is appended in time order.</summary>
    HistoryOutOfOrder,
}

/// <summary>What one lock did.</summary>
public enum SectorLockStatus
{
    /// <summary>The sector was loaded; its age was refreshed.</summary>
    Hit,
    /// <summary>The sector was not loaded and now is (a miss); see <see cref="SectorLockOutcome.Evicted"/> and
    /// <see cref="SectorLockOutcome.Fold"/>.</summary>
    Loaded,
    /// <summary>Nothing changed; <see cref="SectorLockOutcome.Failure"/> says why.</summary>
    Refused,
}

/// <summary>
/// The outcome of one lock. For <see cref="SectorLockStatus.Loaded"/>: <see cref="Evicted"/> is the sector the miss pushed out of a
/// full cache (null when a slot was free), <see cref="Fold"/> is the fold plan (true: the sector had no history record, or was away
/// long enough, <see cref="SectorCacheRules.FoldDue"/>) and <see cref="EvictedAtMs"/> is the game time of the history record the
/// load consumed (null when there was none).
/// </summary>
public readonly record struct SectorLockOutcome(long Sector, SectorLockStatus Status, SectorCacheFailure Failure, long? Evicted,
    bool Fold, long? EvictedAtMs);

/// <summary>The state after a lock and its outcome; on a refusal the state is the one given.</summary>
public readonly record struct SectorLockResult(SectorCacheState State, SectorLockOutcome Outcome);

/// <summary>The state after a batch of locks and one outcome per requested sector, in request order.</summary>
public readonly record struct SectorLockBatchResult(SectorCacheState State, ImmutableArray<SectorLockOutcome> Outcomes);

/// <summary>The state after an eviction pass, the sectors it evicted (ascending id, the order they entered the history) and a
/// refusal (the state is then the one given).</summary>
public readonly record struct SectorCacheChange(SectorCacheState State, SectorCacheFailure Failure, ImmutableArray<long> Evicted);

/// <summary>The state after <see cref="SectorCacheRules.StampLoaded"/>, the sectors it listed (ascending id) and a refusal (the state is then the one given).</summary>
public readonly record struct SectorHistoryStamp(SectorCacheState State, SectorCacheFailure Failure, ImmutableArray<long> Stamped);

/// <summary>Why a map closes: a game load keeps the eviction history (it restores its own), any other close clears it (RL20, RL26).</summary>
public enum SectorMapClose
{
    /// <summary>A map change, a new game or a return to the menu: the history is cleared after the evictions.</summary>
    OtherMap,
    /// <summary>A game is being loaded: the history keeps the records, including those of this close's evictions.</summary>
    GameLoad,
}

/// <summary>
/// ARC-DEV-102 (MIG9 S2, ledger RL2, RL7, RL8, RL20): which sectors of the open map are loaded, as an immutable value. The slots are
/// kept in ascending sector id (the original's index order, which decides the order of a pass's evictions), the history oldest first.
/// Made by <see cref="Initial"/> and changed only by <see cref="SectorCacheRules"/>.
/// </summary>
public sealed class SectorCacheState
{
    internal SectorCacheState(SectorCacheLimits limits, SectorMapExtent? map, ImmutableArray<SectorCacheSlot> slots, long ageCounter,
        ImmutableArray<SectorEvictionRecord> history)
    {
        Limits = limits; Map = map; Slots = slots; AgeCounter = ageCounter; History = history;
    }

    /// <summary>A cache with no map open, no slot and an empty history (a new game).</summary>
    public static SectorCacheState Initial(SectorCacheLimits limits) =>
        new(limits ?? throw new ArgumentNullException(nameof(limits)), null, [], 0, []);

    public SectorCacheLimits Limits { get; }

    /// <summary>The open map's extent; null when no map is open.</summary>
    public SectorMapExtent? Map { get; }

    public bool MapOpen => Map is not null;

    /// <summary>The loaded sectors, ascending id, at most <see cref="SectorCacheLimits.Slots"/>.</summary>
    public ImmutableArray<SectorCacheSlot> Slots { get; }

    /// <summary>The lock counter: the age the next lock stamps is one more.</summary>
    public long AgeCounter { get; }

    /// <summary>The eviction history, oldest first, at most <see cref="SectorCacheLimits.HistoryCapacity"/>.</summary>
    public ImmutableArray<SectorEvictionRecord> History { get; }

    /// <summary>Whether <paramref name="sector"/> is loaded.</summary>
    public bool IsLoaded(long sector) => SectorCacheRules.IndexOf(Slots, sector) >= 0;
}
