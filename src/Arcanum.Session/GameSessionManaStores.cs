using Arcanum.Simulation;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>ARC-MAGIC-2371 (ledger BC10): the mana store (object field 92) the session holds for <paramref name="entity"/> after a payment drained it
    /// or a recharge restored it, or null when it changed none (the item's own field, the prototype's value for a fresh item, holds it).</summary>
    public int? QueryItemManaStore(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) && _durability.ManaStores.TryGetValue(entity, out var store) ? store : null;
    }

    /// <summary>
    /// ARC-MAGIC-2371: the item's mana store (field 92) is written, as the original's payment (0x450420, which writes the drained store back) and its
    /// recharge handler (0x459680, which adds to it with no cap at the prototype's value) write it. <paramref name="baseline"/> is the item's own field,
    /// read only until the session holds a store; a store equal to the current one is <see cref="GameSessionDisposition.NoChange"/>. Refused for an invalid
    /// entity, a store outside 0..<see cref="SessionItemUseLimits.MaxUses"/> (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an item the
    /// session does not track or deleted (<see cref="GameSessionFailure.ItemNotAvailable"/>) and a full record budget. One revision.
    /// </summary>
    public GameSessionMutationResult WriteItemManaStore(GameSessionVersion expected, string entity, int baseline, int store)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity) || store is < 0 or > SessionItemUseLimits.MaxUses) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (!TracksItem(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        var recorded = _durability.ManaStores.TryGetValue(entity, out var current);
        if ((recorded ? current : baseline) == store) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (!recorded && _durability.Count >= _limits.MaxDurabilityRecords) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _durability.ManaStores[entity] = store;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-MAGIC-2371: the recharge event of a drained worn store (0x459590, ledger BC10): with <paramref name="merge"/> an item's pending event takes the amount
    /// (its stamp and deadline stay, a 0 or negative amount too), otherwise (and when none is pending) a new one fires <see cref="SessionEventKinds.ItemRechargeDelayMs"/> on. A full queue schedules none (project bound).
    /// </summary>
    [OriginalFunction(0x459590, OriginalFunctionScope.Partial)]
    internal void ScheduleManaRecharge(string entity, int amount, bool merge)
    {
        if (!SessionValidation.Entity(entity) || entity == SessionEventKinds.AbsentRole) return;
        if (merge && _events.QueryPending(SessionEventKinds.ItemRecharge).FirstOrDefault(e => e.Payload.Entities[0].Code == entity) is { } pending)
        {
            var sum = (int)Math.Clamp(pending.Payload.Values[1] + amount, -SessionItemUseLimits.MaxUses, SessionItemUseLimits.MaxUses);
            _events.RemoveFirst(SessionEventKinds.ItemRecharge, e => e.Sequence == pending.Sequence);
            _events.ScheduleAt(SessionEventKinds.ItemRecharge, GameEventPayload.Of([new(SessionEventKinds.RechargeMap, entity)], pending.Payload.Values[0], sum), pending.DeadlineMs);
            return;
        }
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - SessionEventKinds.ItemRechargeDelayMs) return;
        if (_events.QueryPending(SessionEventKinds.ItemRecharge).Length >= SessionEventKinds.MaxItemRecharges) return;
        _events.ScheduleAfter(SessionEventKinds.ItemRecharge, GameEventPayload.Of([new(SessionEventKinds.RechargeMap, entity)], now, amount), SessionEventKinds.ItemRechargeDelayMs, EventClocks(now));
    }

    /// <summary>The pending recharge events of worn mana stores (ARC-MAGIC-2371): the item, the pending amount, the stamp and the deadline, in queue order.</summary>
    public IReadOnlyList<(string Entity, int Pending, long StampMs, long DeadlineMs)> QueryManaRecharges()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.ItemRecharge)
            .Select(e => (e.Payload.Entities[0].Code, (int)e.Payload.Values[1], e.Payload.Values[0], e.DeadlineMs)).ToArray());
    }

    // The recharge event (kind 0x17; 0x459680): units = elapsed game seconds / 120 (at least 1), restored = min(pending, 2 × units) added to the store with no cap at the prototype's value,
    // the rest scheduled again without merging. An item the session deleted, or one whose store it never changed (a pending 0 only), has nothing to restore and the event ends.
    // The store is kept within 0..MaxUses (a negative one the original would write back, a prototype's negative store, is not held).
    internal GameEventHandlerOutcome FireItemRecharge(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0].Code;
        if (IsItemDestroyed(entity) || !_durability.ManaStores.TryGetValue(entity, out var store)) return GameEventHandlerOutcome.Completed;
        var pending = (int)firing.Event.Payload.Values[1];
        var elapsed = (int)Math.Min((firing.NowMs - firing.Event.Payload.Values[0]) / 1000, int.MaxValue);
        var (restored, remaining) = SpellCostRules.Recharge(elapsed, pending);
        var written = WriteItemManaStore(Version, entity, store, (int)Math.Clamp((long)store + restored, 0, SessionItemUseLimits.MaxUses));
        if (written.Disposition == GameSessionDisposition.Rejected && written.Failure == GameSessionFailure.RevisionExhausted) return GameEventHandlerOutcome.Refused;
        if (remaining > 0) ScheduleManaRecharge(entity, remaining, merge: false);
        return GameEventHandlerOutcome.Completed;
    }
}
