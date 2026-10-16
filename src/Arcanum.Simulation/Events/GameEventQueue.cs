using System.Collections.Immutable;
using System.Text;

namespace Arcanum.Simulation.Events;

/// <summary>
/// The game event queue core (ARC-WORLD-022, slice E1 of ARC-RES-046): one queue for every map and all three clock
/// domains, ordered per domain by (deadline, sequence) with a persisted, strictly increasing sequence (owner decisions
/// OQ1, OQ3). It holds no clock: callers pass the three clock values. Godot-free and session-free; the session owner
/// and its snapshot section are slice E2. Caller-serialized; a handler may schedule and cancel during a pass, nothing
/// else may run concurrently.
/// </summary>
public sealed class GameEventQueue
{
    private static readonly GameEventDomain[] PassOrder = [GameEventDomain.Real, GameEventDomain.Game, GameEventDomain.Animation];

    private readonly Dictionary<long, GameEvent> _events = new();
    private readonly SortedSet<Order>[] _byDomain = [new(), new(), new()];
    private readonly Dictionary<string, SortedSet<Order>> _byKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _byKey = new(StringComparer.Ordinal);
    private long _nextSequence = 1;
    private bool _passing;
    private long _passWatermark;

    /// <summary>An empty queue. Throws <see cref="ArgumentException"/> when the limits are out of range or a kind has
    /// more slots than the limits allow.</summary>
    public GameEventQueue(GameEventRegistry registry, GameEventQueueLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        limits ??= GameEventQueueLimits.Default;
        if (!limits.IsValid) throw new ArgumentException("Event queue limits are out of range.", nameof(limits));
        foreach (var kind in registry.Kinds)
        {
            if (kind.EntityCount > limits.MaxEntitiesPerEvent || kind.ValueCount > limits.MaxValuesPerEvent)
                throw new ArgumentException($"Event kind '{kind.Name}' has more slots than the limits allow.", nameof(limits));
            _byKind.Add(kind.Name, new SortedSet<Order>());
        }
        Registry = registry;
        Limits = limits;
    }

    public GameEventRegistry Registry { get; }
    public GameEventQueueLimits Limits { get; }

    /// <summary>Pending events.</summary>
    public int Count => _events.Count;

    /// <summary>The sequence the next new event gets; persisted, so equal-deadline order survives a save.</summary>
    public long NextSequence => _nextSequence;

    /// <summary>
    /// Schedules an event <paramref name="delayMs"/> after the current time of the kind's domain in <paramref
    /// name="now"/>. A delay of 0 is stored as 1 ms, as in the source (ARC-RES-046 Q2), so even an immediate event is
    /// due at the next pass at the earliest.
    /// </summary>
    public GameEventScheduleResult ScheduleAfter(string kind, GameEventPayload payload, long delayMs, GameEventClocks now)
    {
        if (!Registry.TryGet(kind, out var definition)) return Refuse(GameEventFailure.UnknownKind);
        if (!now.IsValid) return Refuse(GameEventFailure.InvalidClocks);
        if (delayMs < 0) return Refuse(GameEventFailure.InvalidDelay);
        long deadline;
        try { deadline = checked(now.For(definition.Domain) + Math.Max(delayMs, 1)); }
        catch (OverflowException) { return Refuse(GameEventFailure.TimeOverflow); }
        return Schedule(definition, payload, deadline);
    }

    /// <summary>Schedules an event at an absolute deadline of the kind's domain (nonnegative; a past deadline is due at
    /// the next pass).</summary>
    public GameEventScheduleResult ScheduleAt(string kind, GameEventPayload payload, long deadlineMs)
    {
        if (!Registry.TryGet(kind, out var definition)) return Refuse(GameEventFailure.UnknownKind);
        if (deadlineMs < 0) return Refuse(GameEventFailure.InvalidDeadline);
        return Schedule(definition, payload, deadlineMs);
    }

    /// <summary>Whether an event of <paramref name="kind"/> matching <paramref name="match"/> (any, when null) is
    /// pending.</summary>
    public bool Any(string kind, Func<GameEvent, bool>? match = null)
    {
        foreach (var order in KindSet(kind))
            if (match is null || match(_events[order.Sequence])) return true;
        return false;
    }

    /// <summary>Removes exactly the first pending match in queue order (deadline, sequence), or returns null (the
    /// source can remove two, anomaly TE4).</summary>
    public GameEvent? RemoveFirst(string kind, Func<GameEvent, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        GameEvent? found = null;
        foreach (var order in KindSet(kind))
        {
            var candidate = _events[order.Sequence];
            if (match(candidate)) { found = candidate; break; }
        }
        if (found is not null) Remove(found);
        return found;
    }

    /// <summary>Removes every pending match of <paramref name="kind"/> and returns them in queue order.</summary>
    public ImmutableArray<GameEvent> RemoveAll(string kind, Func<GameEvent, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var removed = KindSet(kind).Select(o => _events[o.Sequence]).Where(match).ToImmutableArray();
        foreach (var e in removed) Remove(e);
        return removed;
    }

    /// <summary>All pending events, saved or not, in queue order (domain, deadline, sequence). Detached.</summary>
    public ImmutableArray<GameEvent> QueryPending() =>
        PassOrder.SelectMany(d => DomainSet(d)).Select(o => _events[o.Sequence]).ToImmutableArray();

    /// <summary>
    /// Takes due events off the queue and runs their handlers. Per domain in the order real, game, animation, an event
    /// is due when its deadline is at or before that domain's time; it is eligible when its map is global or the
    /// active one and it was scheduled before this pass began (events a handler schedules wait for the next pass,
    /// ARC-RES-046 Q4, Q5). Ineligible events stay pending. Each eligible due event, in (deadline, sequence) order,
    /// leaves the queue; if one of its entities no longer exists it is dropped, otherwise its handler runs. The pass
    /// stops at the budget; the queue consumes no randomness. A pass started from a handler is refused
    /// (<see cref="GameEventFailure.PassInProgress"/>); an invalid request is <see cref="GameEventFailure.InvalidPass"/>.
    /// A handler exception propagates; its event has already left the queue.
    /// </summary>
    public GameEventPassResult ProcessDue(GameEventPassRequest request)
    {
        if (_passing) return new(GameEventFailure.PassInProgress, ImmutableArray<GameEventOutcome>.Empty, false);
        if (request is null || request.EntityExists is null || !request.Clocks.IsValid || request.ActiveMap < 0 ||
            request.Budget < 1 || request.Budget > Limits.MaxPerPass)
            return new(GameEventFailure.InvalidPass, ImmutableArray<GameEventOutcome>.Empty, false);

        var outcomes = ImmutableArray.CreateBuilder<GameEventOutcome>();
        var exhausted = false;
        _passing = true;
        _passWatermark = _nextSequence;
        try
        {
            for (var d = 0; d < PassOrder.Length && !exhausted; d++)
            {
                var domain = PassOrder[d];
                var now = request.Clocks.For(domain);
                Order? position = null;
                while (true)
                {
                    var next = NextEligible(domain, now, request.ActiveMap, position);
                    if (next is not { } order) break;
                    if (outcomes.Count == request.Budget)
                    {
                        exhausted = true;
                        break;
                    }
                    position = order;
                    var due = _events[order.Sequence];
                    Remove(due);
                    if (!due.Payload.Entities.All(request.EntityExists))
                    {
                        outcomes.Add(new(due, GameEventOutcomeKind.EntityMissing, now));
                        continue;
                    }
                    var handled = Kind(due.Kind).Handler.Handle(new GameEventFiring(due, now), this);
                    outcomes.Add(new(due, handled == GameEventHandlerOutcome.Completed
                        ? GameEventOutcomeKind.Fired : GameEventOutcomeKind.HandlerRefused, now));
                }
            }
        }
        finally { _passing = false; }
        return new(GameEventFailure.None, outcomes.ToImmutable(), exhausted);
    }

    /// <summary>The persisted state: pending events of saved kinds (<see cref="GameEventPersistence.Saved"/> and
    /// <see cref="GameEventPersistence.SavedUnlessRebuilt"/>) and the next sequence. Throws
    /// <see cref="InvalidOperationException"/> during a pass.</summary>
    public GameEventQueueSnapshot CaptureSnapshot()
    {
        if (_passing) throw new InvalidOperationException("An event queue snapshot cannot be taken during a pass.");
        var events = QueryPending().Where(e => Kind(e.Kind).Persistence != GameEventPersistence.NotSaved).ToImmutableArray();
        return new(Registry.Version, _nextSequence, events);
    }

    /// <summary>
    /// Builds a queue from a snapshot after checking it strictly: same registry version, next sequence at least 1,
    /// at most <see cref="GameEventQueueLimits.MaxPending"/> events in queue order, each of a known saved kind with
    /// its kind's domain, a unique sequence in 1..next − 1, a nonnegative deadline, a valid payload with the scope map
    /// it implies, and no two events of a unique kind with the same key. The profile defaults to
    /// <see cref="GameEventQueueProfile.Steamwoven"/>; with <see cref="GameEventQueueProfile.RebuildOnLoad"/> (the
    /// <see cref="GameEventQueueProfile.Original"/> setting) the events of
    /// <see cref="GameEventPersistence.SavedUnlessRebuilt"/> kinds are checked, left out and returned in
    /// <see cref="GameEventRestoreResult.Rebuild"/>. A failure builds nothing.
    /// </summary>
    public static GameEventRestoreResult Restore(GameEventRegistry registry, GameEventQueueSnapshot snapshot,
        GameEventQueueProfile? profile = null, GameEventQueueLimits? limits = null)
    {
        var queue = new GameEventQueue(registry, limits);
        profile ??= GameEventQueueProfile.Steamwoven;
        if (snapshot is null || snapshot.Events.IsDefault) return Fail(GameEventFailure.InvalidSnapshot, null);
        if (snapshot.RegistryVersion != registry.Version) return Fail(GameEventFailure.RegistryMismatch, null);
        if (snapshot.NextSequence < 1) return Fail(GameEventFailure.InvalidSequence, null);
        if (snapshot.Events.Length > queue.Limits.MaxPending) return Fail(GameEventFailure.QueueFull, null);

        var sequences = new HashSet<long>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var rebuild = ImmutableArray.CreateBuilder<GameEvent>();
        var kept = new List<GameEvent>();
        GameEvent? previous = null;
        foreach (var e in snapshot.Events)
        {
            if (e is null) return Fail(GameEventFailure.InvalidSnapshot, null);
            if (!registry.TryGet(e.Kind, out var kind)) return Fail(GameEventFailure.UnknownKind, e);
            if (kind.Persistence == GameEventPersistence.NotSaved) return Fail(GameEventFailure.NotPersistedKind, e);
            if (e.Domain != kind.Domain) return Fail(GameEventFailure.InvalidSnapshot, e);
            if (e.Sequence < 1 || e.Sequence >= snapshot.NextSequence || !sequences.Add(e.Sequence))
                return Fail(GameEventFailure.InvalidSequence, e);
            if (e.DeadlineMs < 0) return Fail(GameEventFailure.InvalidDeadline, e);
            var failure = queue.CheckPayload(kind, e.Payload, out var scopeMap);
            if (failure != GameEventFailure.None) return Fail(failure, e);
            if (e.ScopeMap != scopeMap) return Fail(GameEventFailure.InvalidScope, e);
            if (previous is not null && CompareQueueOrder(previous, e) >= 0) return Fail(GameEventFailure.InvalidSnapshot, e);
            if (kind.Uniqueness != GameEventUniqueness.Many && !keys.Add(Key(kind, e.Payload)))
                return Fail(GameEventFailure.DuplicateKey, e);
            previous = e;
            if (profile.RebuildOnLoad && kind.Persistence == GameEventPersistence.SavedUnlessRebuilt) rebuild.Add(e);
            else kept.Add(e);
        }
        foreach (var e in kept) queue.Insert(e);
        queue._nextSequence = snapshot.NextSequence;
        return new(queue, GameEventFailure.None, null, rebuild.ToImmutable());

        static GameEventRestoreResult Fail(GameEventFailure failure, GameEvent? offending) =>
            new(null, failure, offending, ImmutableArray<GameEvent>.Empty);
    }

    private GameEventScheduleResult Schedule(GameEventKindDefinition kind, GameEventPayload payload, long deadline)
    {
        var failure = CheckPayload(kind, payload, out var scopeMap);
        if (failure != GameEventFailure.None) return Refuse(failure);

        GameEvent? pending = null;
        if (kind.Uniqueness != GameEventUniqueness.Many && _byKey.TryGetValue(Key(kind, payload), out var pendingSequence))
            pending = _events[pendingSequence];

        if (pending is not null)
        {
            switch (kind.Uniqueness)
            {
                case GameEventUniqueness.KeepExisting:
                    return new(GameEventScheduleDisposition.KeptExisting, GameEventFailure.None, pending, null);
                case GameEventUniqueness.Merge:
                    var merged = kind.Merge!(pending.Payload, payload);
                    if (CheckPayload(kind, merged, out var mergedMap) != GameEventFailure.None || mergedMap != pending.ScopeMap ||
                        !string.Equals(Key(kind, merged), Key(kind, pending.Payload), StringComparison.Ordinal))
                        return Refuse(GameEventFailure.InvalidMerge);
                    var updated = pending with { Payload = merged };
                    _events[pending.Sequence] = updated;
                    return new(GameEventScheduleDisposition.Merged, GameEventFailure.None, updated, pending);
            }
        }

        if (_nextSequence == long.MaxValue) return Refuse(GameEventFailure.SequenceExhausted);
        if (pending is null && _events.Count >= Limits.MaxPending) return Refuse(GameEventFailure.QueueFull);
        if (pending is not null) Remove(pending);
        var added = new GameEvent(_nextSequence++, kind.Name, kind.Domain, deadline, scopeMap, payload);
        Insert(added);
        return new(pending is null ? GameEventScheduleDisposition.Added : GameEventScheduleDisposition.Replaced,
            GameEventFailure.None, added, pending);
    }

    private GameEventFailure CheckPayload(GameEventKindDefinition kind, GameEventPayload? payload, out int scopeMap)
    {
        scopeMap = 0;
        if (payload is null || payload.Entities.IsDefault || payload.Values.IsDefault ||
            payload.Entities.Length != kind.EntityCount || payload.Values.Length != kind.ValueCount)
            return GameEventFailure.InvalidPayload;
        foreach (var entity in payload.Entities)
            if (entity.Map < 1 || !GameEventRegistry.IsCode(entity.Code, 128)) return GameEventFailure.InvalidPayload;
        if (kind.Validate is { } validate && !validate(payload)) return GameEventFailure.InvalidPayload;
        if (kind.Scope == GameEventScopeRule.Global) return GameEventFailure.None;
        var map = payload.Entities[0].Map;
        foreach (var entity in payload.Entities)
            if (entity.Map != map) return GameEventFailure.InvalidScope;
        scopeMap = map;
        return GameEventFailure.None;
    }

    private Order? NextEligible(GameEventDomain domain, long now, int activeMap, Order? after)
    {
        var lower = after is { } a ? new Order(a.DeadlineMs, a.Sequence + 1) : new Order(long.MinValue, long.MinValue);
        var upper = new Order(now, long.MaxValue);
        if (lower.CompareTo(upper) > 0) return null;
        foreach (var order in DomainSet(domain).GetViewBetween(lower, upper))
        {
            if (order.Sequence >= _passWatermark) continue;
            var scope = _events[order.Sequence].ScopeMap;
            if (scope == 0 || scope == activeMap) return order;
        }
        return null;
    }

    private void Insert(GameEvent e)
    {
        var kind = Kind(e.Kind);
        var order = new Order(e.DeadlineMs, e.Sequence);
        _events.Add(e.Sequence, e);
        DomainSet(e.Domain).Add(order);
        _byKind[e.Kind].Add(order);
        if (kind.Uniqueness != GameEventUniqueness.Many) _byKey.Add(Key(kind, e.Payload), e.Sequence);
    }

    private void Remove(GameEvent e)
    {
        var kind = Kind(e.Kind);
        var order = new Order(e.DeadlineMs, e.Sequence);
        _events.Remove(e.Sequence);
        DomainSet(e.Domain).Remove(order);
        _byKind[e.Kind].Remove(order);
        if (kind.Uniqueness != GameEventUniqueness.Many) _byKey.Remove(Key(kind, e.Payload));
    }

    private GameEventKindDefinition Kind(string name) => Registry.TryGet(name, out var kind)
        ? kind : throw new InvalidOperationException($"Event kind '{name}' is not registered.");

    private SortedSet<Order> KindSet(string kind) => kind is not null && _byKind.TryGetValue(kind, out var set)
        ? set : throw new ArgumentException($"Event kind '{kind}' is not registered.", nameof(kind));

    private SortedSet<Order> DomainSet(GameEventDomain domain) => _byDomain[(int)domain - 1];

    // Kind names and entity codes never contain '|', and the slot counts of a kind are fixed, so the text is unambiguous.
    private static string Key(GameEventKindDefinition kind, GameEventPayload payload)
    {
        var key = new StringBuilder(kind.Name);
        for (var i = 0; i < kind.KeyEntities; i++) key.Append('|').Append(payload.Entities[i].Map).Append(':').Append(payload.Entities[i].Code);
        for (var i = 0; i < kind.KeyValues; i++) key.Append('|').Append(payload.Values[i]);
        return key.ToString();
    }

    private static int CompareQueueOrder(GameEvent x, GameEvent y)
    {
        var domain = x.Domain.CompareTo(y.Domain);
        return domain != 0 ? domain : new Order(x.DeadlineMs, x.Sequence).CompareTo(new Order(y.DeadlineMs, y.Sequence));
    }

    private static GameEventScheduleResult Refuse(GameEventFailure failure) =>
        new(GameEventScheduleDisposition.Rejected, failure, null, null);

    private readonly record struct Order(long DeadlineMs, long Sequence) : IComparable<Order>
    {
        public int CompareTo(Order other)
        {
            var deadline = DeadlineMs.CompareTo(other.DeadlineMs);
            return deadline != 0 ? deadline : Sequence.CompareTo(other.Sequence);
        }
    }
}
