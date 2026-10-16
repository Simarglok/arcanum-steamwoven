using System.Collections.Immutable;

namespace Arcanum.Simulation.Events;

/// <summary>
/// The clock an event waits on (ARC-RES-046 K1..K3). A pass handles the domains in this order, as the source pings its
/// real, game and animation lists. Turn-based combat freezes game time only; the caller supplies all three clocks.
/// </summary>
public enum GameEventDomain { Real = 1, Game = 2, Animation = 3 }

/// <summary>Whether a kind's events go into <see cref="GameEventQueueSnapshot"/>.</summary>
public enum GameEventPersistence
{
    /// <summary>Captured and restored.</summary>
    Saved = 1,
    /// <summary>Never captured (host-paced or rebuilt kinds such as an animated light).</summary>
    NotSaved = 2,
    /// <summary>Captured; on restore with <see cref="GameEventQueueProfile.RebuildOnLoad"/> dropped and handed back
    /// for a fresh schedule, as the source rebuilds AI ticks on load (owner decision OQ2).</summary>
    SavedUnlessRebuilt = 3,
}

/// <summary>What scheduling does when an event with the same key is already pending (ARC-RES-046 Q10).</summary>
public enum GameEventUniqueness
{
    /// <summary>Any number per key.</summary>
    Many = 1,
    /// <summary>The new event replaces the pending one (new deadline, new sequence).</summary>
    Replace = 2,
    /// <summary>The pending one stays and the new one is not added.</summary>
    KeepExisting = 3,
    /// <summary>The kind's merge function folds the new payload into the pending event, which keeps its deadline and
    /// sequence.</summary>
    Merge = 4,
}

/// <summary>
/// Which map an event belongs to. <see cref="Global"/> events run on any map; the other two run only while their map
/// (the map of their referenced entities) is the active one and stay pending otherwise. The difference between the
/// last two is the session's map-change rule (slice E2): <see cref="ActiveMapOnly"/> kinds are removed when their map
/// is left and rebuilt when it is activated again.
/// </summary>
public enum GameEventScopeRule { Global = 1, EntityMap = 2, ActiveMapOnly = 3 }

/// <summary>A session entity reference: the runtime map number (positive) and the stable entity code (1..128 ASCII
/// letters, digits and <c>. _ : -</c>, as the session's entity ids). Never a live handle.</summary>
public readonly record struct GameEventEntity(int Map, string Code);

/// <summary>
/// The parameters of one event: entity references and signed 64-bit values, in the fixed slot counts of its kind.
/// Equality is by content.
/// </summary>
public sealed record GameEventPayload(ImmutableArray<GameEventEntity> Entities, ImmutableArray<long> Values)
{
    public static GameEventPayload Empty { get; } = new(ImmutableArray<GameEventEntity>.Empty, ImmutableArray<long>.Empty);

    public static GameEventPayload Of(IEnumerable<GameEventEntity> entities, params long[] values) =>
        new(entities.ToImmutableArray(), values.ToImmutableArray());

    public bool Equals(GameEventPayload? other) => other is not null &&
        Entities.AsSpan().SequenceEqual(other.Entities.AsSpan()) && Values.AsSpan().SequenceEqual(other.Values.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entity in Entities.AsSpan()) hash.Add(entity);
        hash.Add(-1);
        foreach (var value in Values.AsSpan()) hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One pending event. Order within a domain is (<see cref="DeadlineMs"/>, <see cref="Sequence"/>): equal deadlines fire
/// first scheduled first (owner decision OQ1, anomaly TE3). <see cref="ScopeMap"/> is 0 for a global event, else the
/// map of its entities.
/// </summary>
public sealed record GameEvent(long Sequence, string Kind, GameEventDomain Domain, long DeadlineMs, int ScopeMap,
    GameEventPayload Payload);

/// <summary>The three clock values in milliseconds of their own domain, each nonnegative.</summary>
public readonly record struct GameEventClocks(long RealMs, long GameMs, long AnimationMs)
{
    public long For(GameEventDomain domain) => domain switch
    {
        GameEventDomain.Real => RealMs,
        GameEventDomain.Game => GameMs,
        GameEventDomain.Animation => AnimationMs,
        _ => throw new ArgumentOutOfRangeException(nameof(domain)),
    };

    internal bool IsValid => RealMs >= 0 && GameMs >= 0 && AnimationMs >= 0;
}

/// <summary>What a handler reports. A refusal is recorded in the pass result; the event is consumed either way.</summary>
public enum GameEventHandlerOutcome { Completed = 1, Refused = 2 }

/// <summary>A due event handed to its kind's handler: <see cref="NowMs"/> is the current time of its domain,
/// <see cref="Clocks"/> the pass's three clocks, for scheduling in any domain with
/// <see cref="GameEventQueue.ScheduleAfter"/>.</summary>
public readonly record struct GameEventFiring(GameEvent Event, long NowMs, GameEventClocks Clocks);

/// <summary>
/// A kind's handler. It runs after the event has left the queue and may schedule or cancel events of the same queue
/// (a periodic kind reschedules itself from <see cref="GameEventFiring.NowMs"/>); it may not start another pass or take
/// a snapshot. Events it schedules never fire in the pass that runs it. Merge functions, value checks and match
/// predicates, unlike handlers, must not call the queue.
/// </summary>
public interface IGameEventHandler
{
    GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue);
}

/// <summary>Folds an incoming payload into the pending one of a <see cref="GameEventUniqueness.Merge"/> kind. The result
/// must keep the key slots and pass the kind's validation, or the schedule is refused.</summary>
public delegate GameEventPayload GameEventMerge(GameEventPayload pending, GameEventPayload incoming);

/// <summary>
/// One kind of the closed registry. <see cref="EntityCount"/> and <see cref="ValueCount"/> are exact slot counts; the
/// uniqueness key is the first <see cref="KeyEntities"/> entities and the first <see cref="KeyValues"/> values (both 0:
/// one event of the kind at most). <see cref="Validate"/> is an optional extra check of the values. A scoped kind
/// (<see cref="GameEventScopeRule.EntityMap"/>, <see cref="GameEventScopeRule.ActiveMapOnly"/>) needs at least one
/// entity, and all its entities on one map.
/// </summary>
public sealed record GameEventKindDefinition(string Name, GameEventDomain Domain, GameEventPersistence Persistence,
    GameEventUniqueness Uniqueness, GameEventScopeRule Scope, int EntityCount, int ValueCount, int KeyEntities,
    int KeyValues, IGameEventHandler Handler, GameEventMerge? Merge = null, Func<GameEventPayload, bool>? Validate = null);

/// <summary>Explicit bounds of one queue. The defaults are the ARC-RES-046 proposal (500 is the source's per-ping
/// guard).</summary>
public sealed record GameEventQueueLimits(int MaxPending = 16_384, int MaxPerPass = 500, int MaxEntitiesPerEvent = 4,
    int MaxValuesPerEvent = 8)
{
    public static GameEventQueueLimits Default { get; } = new();

    internal bool IsValid => MaxPending is >= 1 and <= 1_048_576 && MaxPerPass >= 1 && MaxPerPass <= MaxPending &&
        MaxEntitiesPerEvent is >= 0 and <= 64 && MaxValuesPerEvent is >= 0 and <= 64;
}

public enum GameEventScheduleDisposition { Added = 1, Replaced = 2, KeptExisting = 3, Merged = 4, Rejected = 5 }

public enum GameEventFailure
{
    None,
    UnknownKind,
    InvalidPayload,
    InvalidScope,
    InvalidDelay,
    InvalidDeadline,
    InvalidClocks,
    TimeOverflow,
    InvalidMerge,
    QueueFull,
    SequenceExhausted,
    PassInProgress,
    InvalidPass,
    RegistryMismatch,
    InvalidSnapshot,
    NotPersistedKind,
    InvalidSequence,
    DuplicateKey,
}

/// <summary>
/// The result of one schedule call. <see cref="Event"/> is the event now pending under the request (the added, merged
/// or kept one); <see cref="Previous"/> is the pending event it replaced or merged into, as it was.
/// </summary>
public readonly record struct GameEventScheduleResult(GameEventScheduleDisposition Disposition,
    GameEventFailure Failure, GameEvent? Event, GameEvent? Previous);

/// <summary>
/// One pass. <see cref="ActiveMap"/> is the active runtime map (0: none, only global events are eligible).
/// <see cref="Budget"/> is the most events the pass takes off the queue, fired or dropped, 1..<see
/// cref="GameEventQueueLimits.MaxPerPass"/>. <see cref="EntityExists"/> tells whether a referenced entity still exists;
/// an event that names a missing one is dropped without running (anomalies TE6, TE9).
/// </summary>
public sealed record GameEventPassRequest(GameEventClocks Clocks, int ActiveMap, int Budget,
    Func<GameEventEntity, bool> EntityExists);

public enum GameEventOutcomeKind
{
    /// <summary>The handler ran and completed.</summary>
    Fired = 1,
    /// <summary>The handler ran and refused.</summary>
    HandlerRefused = 2,
    /// <summary>A referenced entity no longer exists; the handler did not run.</summary>
    EntityMissing = 3,
}

/// <summary>What happened to one event taken off the queue, with its domain's time.</summary>
public readonly record struct GameEventOutcome(GameEvent Event, GameEventOutcomeKind Kind, long NowMs);

/// <summary>
/// The result of a pass: the outcomes in processing order, and whether due eligible events were left because the
/// budget ran out (anomalies TE5, TE7). Events of an inactive map, and events scheduled during the pass, are not due
/// for this purpose.
/// </summary>
public sealed record GameEventPassResult(GameEventFailure Failure, ImmutableArray<GameEventOutcome> Outcomes,
    bool BudgetExhausted)
{
    public int Fired => Outcomes.Count(o => o.Kind == GameEventOutcomeKind.Fired);
    public int Dropped => Outcomes.Length - Fired;
}

/// <summary>The result of <see cref="GameEventQueue.Relocate"/>: the moved events as they now are, or a failure with none
/// moved.</summary>
public sealed record GameEventRelocateResult(GameEventFailure Failure, ImmutableArray<GameEvent> Moved);

/// <summary>
/// Detached persisted state: the registry version, the next sequence number and the pending events of saved kinds in
/// queue order (domain, deadline, sequence).
/// </summary>
public sealed record GameEventQueueSnapshot(int RegistryVersion, long NextSequence, ImmutableArray<GameEvent> Events);

/// <summary>
/// The two event queue rule settings of owner decision D8, shaped like the combat profile:
/// <see cref="Steamwoven"/> (the default) and <see cref="Original"/>. The only switch is the AI-tick rule of owner
/// decision OQ2. The profile holds no state and is not saved (D4): any snapshot restores under either setting.
/// Equal-deadline order is FIFO under both (owner decision OQ1 has no switch).
/// </summary>
public sealed record GameEventQueueProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>. A profile changed with <c>with</c> is a custom
    /// mix and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>OQ2: events of <see cref="GameEventPersistence.SavedUnlessRebuilt"/> kinds are not restored but handed
    /// back for a fresh schedule, as the source rebuilds every NPC's AI tick on load.</summary>
    public bool RebuildOnLoad { get; init; }

    /// <summary>Saved AI ticks restore with their timing; the default setting.</summary>
    public static readonly GameEventQueueProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The source behaviour: AI ticks are rebuilt on load.</summary>
    public static readonly GameEventQueueProfile Original = new() { Name = "original", RebuildOnLoad = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<GameEventQueueProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static GameEventQueueProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}

/// <summary>A restored queue, or the failure and the first offending event. <see cref="Rebuild"/> lists the events
/// dropped by <see cref="GameEventQueueProfile.RebuildOnLoad"/>, in snapshot order.</summary>
public sealed record GameEventRestoreResult(GameEventQueue? Queue, GameEventFailure Failure, GameEvent? Offending,
    ImmutableArray<GameEvent> Rebuild);
