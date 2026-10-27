using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-062: an item dropped on the ground gets its Item Decay event two game days on (0x468090, ledger ID4): every drop the
    /// session makes calls it: the hero's critical drops, an NPC's items a critical dropped and a broken container's spilled items
    /// (an assembly's refused product is created on the tile, not dropped, and gets none: ID9). An earlier event of the item is cancelled first; an item with the no-decay
    /// item flag gets none (and loses an earlier one, as 0x468110 does). Decay is never switched off in the game (the editor's
    /// switch, ID5). Beyond <see cref="SessionEventKinds.MaxItemDecays"/> pending events the drop gets none (project bound), and an
    /// event the queue refuses is not set either (the drop itself stands).
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x468090, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    internal void ScheduleDecay(string entity, SessionMapId map, long delayMs = ItemDecayRules.DelayMs)
    {
        CancelDecay(entity);
        if (entity == SessionEventKinds.AbsentRole) return;
        if (!ItemDecayRules.Decays(false, NoDecay(entity) ? ItemDecayRules.NoDecayFlag : 0, ItemDecayPlace.Loose)) return;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - delayMs) return;
        // The decay share is counted only when the whole queue could hold that many (a spill schedules one per item).
        if (_events.Count >= SessionEventKinds.MaxItemDecays &&
            _events.QueryPending(SessionEventKinds.ItemDecay).Length >= SessionEventKinds.MaxItemDecays) return;
        _events.ScheduleAfter(SessionEventKinds.ItemDecay, GameEventPayload.Of([new(map.RuntimeMap, entity)], now, delayMs), delayMs, new(0, now, 0));
    }

    /// <summary>The item's pending Item Decay events are removed (a pickup, a deletion; ledger ID4).</summary>
    internal void CancelDecay(string entity) => _events.RemoveAll(SessionEventKinds.ItemDecay, e => e.Payload.Entities[0].Code == entity);

    /// <summary>The pending Item Decay events (ARC-WORLD-062): the item, its map and the deadline, in queue order.</summary>
    public IReadOnlyList<(string Entity, SessionMapId Map, long DeadlineMs)> QueryItemDecays()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.ItemDecay)
            .Select(e => (e.Payload.Entities[0].Code, new SessionMapId(e.Payload.Entities[0].Map), e.DeadlineMs)).ToArray());
    }

    // A pickup that took the item off the ground ends its decay (0x461810 → 0x468110: in the PC's list it no longer decays).
    private InventoryMutationResult Picked(string entity, InventoryMutationResult result)
    {
        if (result.Disposition == GameSessionDisposition.Changed) CancelDecay(entity);
        return result;
    }

    // The no-decay item flag (field 0x57, 0x20000) of an item an attached seed lists; the hero's kit and made items carry none (no
    // installed item prototype has it, opt-in census ItemDecayFlagCensusTests). Map instances can carry it (two Crash Site items), and
    // the session keeps it only on their seed rows: a taken item whose seed is not attached reads as decaying (Not ready).
    private bool NoDecay(string entity) => _inventory.Seeds.Values.Any(seed => seed.Items.TryGetValue(entity, out var row) && row.Facts.NoDecay);

    // Whether the item still lies loose on the map: placed there, or a seed item of that map on the ground (its own cell, its spilled
    // container's, or where a critical dropped it) that nobody took. Null when the map's seed is not attached, so a seed item's place is
    // unknown.
    private bool? LiesLoose(SessionMapId map, string entity)
    {
        if (_durability.Destroyed.Contains(entity)) return false;
        if (_inventory.PlacedItem(entity) is { } placed) return placed.Map == map;
        if (!_inventory.Seeds.TryGetValue(map, out var seed)) return _inventory.Taken.Contains(entity) ? false : null;
        return !_inventory.Taken.Contains(entity) && LooseCell(map, seed, entity) is not null;
    }

    // The Item Decay event (kind 25; 0x467FC0): while the host's loot window shows a container it waits a minute; otherwise an item
    // still lying loose is deleted (0x43CCA0), in a revision of its own. Two session limits postpone it instead (a minute, reported as
    // Postponed): a seed item whose map's seed is not attached yet (its place is unknown), and a deletion the saved deleted-item budget
    // (MaxDurabilityRecords) cannot hold.
    internal GameEventHandlerOutcome FireItemDecay(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0];
        var map = new SessionMapId(entity.Map);
        var shows = _passWorld is ISessionLootWindowWorld loot && loot.LootWindowShowsContainer;
        var loose = LiesLoose(map, entity.Code);
        var decays = loose != false && ItemDecayRules.Decays(false, NoDecay(entity.Code) ? ItemDecayRules.NoDecayFlag : 0, ItemDecayPlace.Loose);
        var step = ItemDecayRules.Fire(shows, decays);
        var postponed = step == ItemDecayStep.Destroy && (loose is null ||
            !_durability.Records.ContainsKey(entity.Code) && _durability.Count >= _limits.MaxDurabilityRecords);
        if (step == ItemDecayStep.Retry || postponed) ScheduleDecay(entity.Code, map, ItemDecayRules.RetryDelayMs);
        else if (step == ItemDecayStep.Destroy)
        {
            if (_revision == long.MaxValue) return GameEventHandlerOutcome.Refused;
            Delete(entity.Code);
            _revision++;
        }
        _passDecays?.Add(new(firing.Event, map, entity.Code, step) { Postponed = postponed });
        return GameEventHandlerOutcome.Completed;
    }
}
