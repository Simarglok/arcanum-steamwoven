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
    /// read only until the session holds a store; a store equal to the current one is <see cref="GameSessionDisposition.NoChange"/>. The store is a plain
    /// 32-bit value as in the original (ARC-MAGIC-2401): any int, a negative one included. Refused for an invalid entity (<see cref="GameSessionFailure.InvalidItem"/>),
    /// a stale version, an item the session deleted (<see cref="GameSessionFailure.ItemNotAvailable"/>), an item it does not track (unless <paramref name="worn"/>, or the
    /// session already holds its store) and a full store budget (<see cref="GameSessionLimits.MaxManaStores"/>, which no other record shares). One revision.
    /// ARC-MAGIC-2403: <paramref name="worn"/> is the payment's mark that a critter wears the item (the host's map names it in one of the payer's slots), which the original needs no
    /// other fact for: its store is written whatever seed the session has attached for the item's map. A store the session holds is written on too (the recharge event), tracked or not.
    /// </summary>
    public GameSessionMutationResult WriteItemManaStore(GameSessionVersion expected, string entity, int baseline, int store, bool worn = false)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        var recorded = _durability.ManaStores.TryGetValue(entity, out var current);
        if (IsItemDestroyed(entity) || !(recorded || worn || TracksItem(entity))) return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        if ((recorded ? current : baseline) == store) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (!recorded && _durability.ManaStores.Count >= _limits.MaxManaStores) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _durability.ManaStores[entity] = store;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-MAGIC-2371: the recharge event of a drained worn store (0x459590, ledger BC10): with <paramref name="merge"/> an item's pending event takes the amount
    /// (its stamp and deadline stay, a 0 or negative amount too), otherwise (and when none is pending) a new one fires <see cref="SessionEventKinds.ItemRechargeDelayMs"/> on. A full queue schedules none (project bound).
    /// ARC-MAGIC-2400: the event is on the map the PC is on (the original's events live in memory until the PC leaves; TL33), and a merge finds only a pending event of that map: one parked on another map is not in memory.
    /// </summary>
    [OriginalFunction(0x459590, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x459640, OriginalFunctionScope.Partial)]
    internal void ScheduleManaRecharge(string entity, int amount, bool merge)
    {
        if (!SessionValidation.Entity(entity) || entity == SessionEventKinds.AbsentRole) return;
        var map = _actor.Map.RuntimeMap;
        if (merge && _events.QueryPending(SessionEventKinds.ItemRecharge).FirstOrDefault(e => e.Payload.Entities[0].Code == entity && e.Payload.Entities[0].Map == map) is { } pending)
        {
            if (_events.NextSequence == long.MaxValue) return;
            // The callback 0x459640 adds the amount to the pending event's 32-bit field (ARC-MAGIC-2401: it wraps, no bound).
            var sum = unchecked((int)pending.Payload.Values[1] + amount);
            _events.RemoveFirst(SessionEventKinds.ItemRecharge, e => e.Sequence == pending.Sequence);
            _events.ScheduleAt(SessionEventKinds.ItemRecharge, GameEventPayload.Of([new(map, entity)], pending.Payload.Values[0], sum), pending.DeadlineMs);
            return;
        }
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - SessionEventKinds.ItemRechargeDelayMs) return;
        if (_events.QueryPending(SessionEventKinds.ItemRecharge).Length >= SessionEventKinds.MaxItemRecharges) return;
        _events.ScheduleAfter(SessionEventKinds.ItemRecharge, GameEventPayload.Of([new(map, entity)], now, amount), SessionEventKinds.ItemRechargeDelayMs, EventClocks(now));
    }

    /// <summary>The pending recharge events of worn mana stores (ARC-MAGIC-2371): the item, the pending amount, the stamp and the deadline, in queue order (the parked ones of other maps too).</summary>
    public IReadOnlyList<(string Entity, int Pending, long StampMs, long DeadlineMs)> QueryManaRecharges()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.ItemRecharge)
            .Select(e => (e.Payload.Entities[0].Code, (int)e.Payload.Values[1], e.Payload.Values[0], e.DeadlineMs)).ToArray());
    }

    /// <summary>ARC-MAGIC-2400: the maps the pending recharge events of <paramref name="entity"/> are on (parked ones included), ascending.</summary>
    public IReadOnlyList<int> QueryManaRechargeMaps(string entity)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.ItemRecharge).Where(e => e.Payload.Entities[0].Code == entity)
            .Select(e => e.Payload.Entities[0].Map).Order().ToArray());
    }

    // ARC-MAGIC-2400 (ledger BC10, TL33): the PC changes map, so the pending recharge events of the items that travel go with them: the PC's own (pack and worn) and those of the travelling followers
    // (the filter 0x45C500 counts the PC, its followers and their inventories as travellers). The other events stay on the map they are on, as the original parks them in that map's file; they fire when
    // the PC is back, late. A destroyed item travels nowhere (its events are removed with it).
    private HashSet<string> TravellingRechargeItems(IReadOnlySet<string> followers)
    {
        var items = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pending in _events.QueryPending(SessionEventKinds.ItemRecharge))
        {
            var item = pending.Payload.Entities[0].Code;
            if (Locate(item) is not { } entry) continue;
            if (entry.Place is SessionObjectPlace.HeroPack or SessionObjectPlace.HeroWorn ||
                (entry.Place is SessionObjectPlace.NpcList or SessionObjectPlace.SeedHolder && entry.Holder is { } holder && followers.Contains(holder)))
                items.Add(item);
        }
        return items;
    }

    // The recharge event (kind 0x17; 0x459680): units = elapsed game seconds / 120 (at least 1), restored = min(pending, 2 × units) added to the store with no cap at the prototype's value,
    // the rest scheduled again without merging. An item the session deleted, or one whose store it never changed (a pending 0 only), has nothing to restore and the event ends.
    // The store is a plain 32-bit value (ARC-MAGIC-2401): a negative pending amount is restored as it is (min(pending, 2 × units) is a signed compare), so the store goes back to its old negative value and nothing is
    // scheduled again; the sum wraps as the original's 32-bit add does.
    internal GameEventHandlerOutcome FireItemRecharge(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0].Code;
        if (IsItemDestroyed(entity) || !_durability.ManaStores.TryGetValue(entity, out var store)) return GameEventHandlerOutcome.Completed;
        var pending = (int)firing.Event.Payload.Values[1];
        var elapsed = (int)Math.Min((firing.NowMs - firing.Event.Payload.Values[0]) / 1000, int.MaxValue);
        var (restored, remaining) = SpellCostRules.Recharge(elapsed, pending);
        var written = WriteItemManaStore(Version, entity, store, unchecked(store + restored));
        // A store the session cannot write (the item was deleted, a stale revision counter) ends the event: the points are not held anywhere else.
        if (written.Disposition == GameSessionDisposition.Rejected) return written.Failure == GameSessionFailure.RevisionExhausted ? GameEventHandlerOutcome.Refused : GameEventHandlerOutcome.Completed;
        if (remaining > 0) ScheduleManaRecharge(entity, remaining, merge: false);
        return GameEventHandlerOutcome.Completed;
    }
}
