using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>What the Decay Dead Bodies event of a critter came to (ARC-WORLD-1158, ledger PO10).</summary>
public enum SessionBodyDecayOutcome
{
    /// <summary>The body's inventory left it (<see cref="SessionBodyDecayFiring.Dropped"/> items lie on the ground, <see cref="SessionBodyDecayFiring.Destroyed"/> were destroyed) and the critter is destroyed.</summary>
    Removed,
    /// <summary>The critter is not dead (healed back to life): the original answers an error line and leaves it; the event is consumed.</summary>
    Alive,
    /// <summary>The critter was destroyed already (a script's destroy): nothing is left to do.</summary>
    AlreadyGone,
    /// <summary>The session could not remove the body yet (no place for it from the host, or a budget): the event is scheduled again a minute on.</summary>
    Postponed,
    /// <summary>The session could not remove the body and the event queue had no room to wait: the event is gone and the body stays.</summary>
    Lost,
}

/// <summary>A Decay Dead Bodies event that came due (ARC-WORLD-1158): the critter, its map, what it came to and the counts of its inventory's items placed on the ground and destroyed.</summary>
public sealed record SessionBodyDecayFiring(GameEvent Event, SessionMapId Map, string Entity, SessionBodyDecayOutcome Outcome)
{
    public int Dropped { get; init; }
    public int Destroyed { get; init; }
}

/// <summary>
/// ARC-WORLD-1158: a pass world that knows where a dead critter's body lies (the shown map's AI host: the cell the NPC stands on now, a created critter's or a map NPC's). The Decay Dead Bodies event
/// asks it where the body's inventory is dropped; a world without it makes a created critter's own cell stand in, and any other critter's body waits a minute (the event is scheduled again).
/// </summary>
public interface ISessionBodyWorld : ISessionEventWorld
{
    CellCoord? BodyCell(SessionMapId map, string entity);
}

public sealed partial class GameSessionOwner
{
    /// <summary>The map change a script's destroy of a map NPC writes (value 1); the Decay Dead Bodies event writes it too (ARC-WORLD-1158), so the body is gone from the map for every reader.</summary>
    public const string ObjectDestroyedProperty = "obj.destroyed";

    /// <summary>
    /// ARC-WORLD-1158 (the death's body-decay schedule, ledger PO10): the death of an NPC queues its Decay Dead Bodies event one game day on (kind 24, the game clock), one per critter (a new one replaces the
    /// old), unless its critter flags 2 carry "no decay" (<see cref="LifeTraits.NoBodyDecay"/>). The hero's death queues none here (it ends the game). Beyond the event queue's bound the body stays (project bound).
    /// </summary>
    internal bool ScheduleBodyDecay(string entity, int map, long now, LifeFacts facts, long delayMs = BloodPoolRules.CritterDecayDelayMs)
    {
        if (facts.Traits.HasFlag(LifeTraits.NoBodyDecay)) return false;
        if (!SessionEventKinds.AiTickNpc(entity) || _events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - delayMs) return false;
        return _events.ScheduleAfter(SessionEventKinds.BodyDecay, GameEventPayload.Of([new(map, entity)], now, delayMs), delayMs, EventClocks(now)).Disposition != GameEventScheduleDisposition.Rejected;
    }

    /// <summary>The critter's pending Decay Dead Bodies events are removed (0x45EC80: its destroy; a revived critter's event finds it alive and does nothing).</summary>
    [OriginalFunction(0x45EC80, OriginalFunctionScope.Partial)]
    internal void CancelBodyDecay(string entity) => _events.RemoveAll(SessionEventKinds.BodyDecay, e => e.Payload.Entities[0].Code == entity);

    /// <summary>The pending Decay Dead Bodies events (ARC-WORLD-1158): the critter, its map and the deadline, in queue order.</summary>
    public IReadOnlyList<(string Entity, SessionMapId Map, long DeadlineMs)> QueryBodyDecays()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.BodyDecay)
            .Select(e => (e.Payload.Entities[0].Code, new SessionMapId(e.Payload.Entities[0].Map), e.DeadlineMs)).ToArray());
    }

    /// <summary>
    /// ARC-WORLD-1158 (the event 0x45EB50, the inventory walk 0x463860, the destroy 0x43CCA0; ledger PO10): a dead critter drops its whole inventory, last item first, and is destroyed. An item the host's facts mark
    /// "not droppable" is destroyed; every other one is laid on the body's cell by the drop rule (a junk pile with room, else loose, SettleDrop) and gets its decay event as any drop does. A critter that is not dead is left
    /// as it is (the original's error line, no further action). A map NPC's destroy is the script destroy's map change (<see cref="ObjectDestroyedProperty"/>); a created critter's is <see cref="DestroyObject"/>'s.
    /// The host's runs of the critter end (<see cref="ISessionAiWorld.Destroy"/>). A revision step for a created critter, two for a map NPC (its destroy is a map change, which revises itself). Limits: the items of a holder in another map's seed than the body's stay with the destroyed critter, a drop script is not run
    /// (the original's flag-1 walk is not read for it; the spill of a container does the same), and the hero's death queues no event, an added item the ground cannot hold (gold, ammunition, a given kit or taken item) is destroyed with the body, and a map NPC's other pending events (a restock, an AI tick) stay queued as after a script's destroy.
    /// </summary>
    [OriginalFunction(0x463860, OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireBodyDecay(GameEventFiring firing)
    {
        var at = firing.Event.Payload.Entities[0];
        var map = new SessionMapId(at.Map);
        if (_passWorld?.CritterFacts(map, at.Code) is not { } lifeFacts || !_life.Critters.TryGetValue(at.Code, out var saved)) return GameEventHandlerOutcome.Refused;
        bool WaitAMinute() => ScheduleBodyDecay(at.Code, at.Map, firing.NowMs, lifeFacts, ItemDecayRulesRetryMs);
        SessionBodyDecayFiring Report(SessionBodyDecayOutcome outcome, int dropped = 0, int destroyed = 0) =>
            new(firing.Event, map, at.Code, outcome) { Dropped = dropped, Destroyed = destroyed };

        if (_inventory.Objects.CreatedObject(at.Code) is null && _maps.Get(map, MapChangeKey.ForEntity(at.Code, ObjectDestroyedProperty)) == 1)
        {
            _passBodyDecays?.Add(Report(SessionBodyDecayOutcome.AlreadyGone));
            return GameEventHandlerOutcome.Completed;
        }
        var target = SessionLifeTarget.Critter(at.Code, lifeFacts, saved.State);
        if (Resolve(target, out var record, out var facts, out _) != GameSessionFailure.None) return GameEventHandlerOutcome.Refused;
        if (BodyDecayRules.Fire(LifeRules.Evaluate(record.State, facts!).IsDead) == BodyDecayStep.NotDead)
        {
            _passBodyDecays?.Add(Report(SessionBodyDecayOutcome.Alive));
            return GameEventHandlerOutcome.Refused;
        }

        var created = _inventory.Objects.CreatedObject(at.Code);
        var cell = PassWorldAs<ISessionBodyWorld>()?.BodyCell(map, at.Code) ?? (created is { } held && held.Map == map ? held.Cell : null);
        // The body's place is the host's; without it (or when the drop's cell does not fit the packing) the event waits a minute, as an item's decay waits.
        if (cell is not { } where || !DropCellFits(where) || _revision >= long.MaxValue - 2)
        {
            _passBodyDecays?.Add(Report(WaitAMinute() ? SessionBodyDecayOutcome.Postponed : SessionBodyDecayOutcome.Lost));
            return GameEventHandlerOutcome.Completed;
        }

        // 0x463860 walks the list last first: the seed's items of the critter on this map, then the items the session added to it.
        var held2 = new List<(string Entity, bool Seed, bool Destroy)>();
        if (_inventory.Seeds.TryGetValue(map, out var seed) && seed.Holders.TryGetValue(at.Code, out var holder) && holder.Kind == SessionHolderKind.Npc)
            foreach (var item in _inventory.Current(seed, holder, entity => DroppedCell(map, seed, entity), added: false))
                held2.Add((item.Entity, true, seed.Items[item.Entity].Facts.NoDrop));
        // An added item lies on the ground only when a save of it restores: a made item of a kind the ground admits (not gold, ammunition or a pile, and not one the hero gave: kit and taken items keep their identity and given
        // record, which a ground item of source Created has no place for); any other is destroyed with the body (a recorded limit).
        foreach (var made in _inventory.AddedTo(at.Code))
            held2.Add((made.Entity, false, SessionMadeItem.Number(made.Entity) is null || !InventoryDomain.Admitted(made.Kind.Type) || !InventoryDomain.KitAdmitted(made.Kind.Type) ||
                _inventory.Piles.ContainsKey(made.Entity) || _inventory.Ammo.ContainsKey(made.Entity) || _inventory.Hero.IsGiven(made.Entity)));
        held2.Reverse();

        // The budgets first, so the event either does all of it or waits: the deleted-item records of the destroyed items, the created items' bound of the dropped made items.
        var destroyedItems = held2.Where(item => item.Destroy).ToArray();
        var droppedMade = held2.Count(item => !item.Seed && !item.Destroy);
        if (_durability.Count + destroyedItems.Length > _limits.MaxDurabilityRecords || (long)_inventory.Created + droppedMade > _limits.MaxCreatedItems)
        {
            _passBodyDecays?.Add(Report(WaitAMinute() ? SessionBodyDecayOutcome.Postponed : SessionBodyDecayOutcome.Lost));
            return GameEventHandlerOutcome.Completed;
        }

        // The seed's items lie where they dropped (the item.dropped map change, as a critical's drop writes it), and a map NPC's destroy is written with them: one change, all refusals before the commit.
        var edits = new List<MapChangeEdit>();
        var toDrop = held2.Where(item => item.Seed && !item.Destroy).Select(item => item.Entity).ToArray();
        foreach (var entity in toDrop) edits.Add(new(map, MapChangeKey.ForEntity(entity, ItemDroppedProperty), DroppedValue(where)));
        if (created is null) edits.Add(new(map, MapChangeKey.ForEntity(at.Code, ObjectDestroyedProperty), 1));
        var written = ApplyChanges(Version, null, edits);
        if (written.Disposition == GameSessionDisposition.Rejected)
        {
            _passBodyDecays?.Add(Report(WaitAMinute() ? SessionBodyDecayOutcome.Postponed : SessionBodyDecayOutcome.Lost));
            return GameEventHandlerOutcome.Completed;
        }

        // The items leave their holder in the walk's order (last first); each placement sees the items still to come as not yet dropped (as a container's spill does).
        var later = held2.Where(item => !item.Destroy).Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        foreach (var (entity, isSeed, destroy) in held2)
        {
            if (destroy) { DestroyObject(entity); continue; }
            later.Remove(entity);
            if (!isSeed)
            {
                var item = _inventory.AddedTo(at.Code).First(listed => listed.Entity == entity);
                _inventory.RemoveFromNpc(entity);
                _inventory.Hero.AddPlaced(new(entity, item.Kind, SessionPlacedSource.Created, null, map, where, item.Complexity));
            }
            else _inventory.Unwear(entity);
            SettleDrop(entity, map, where, later);
            ScheduleDecay(entity, map);
        }
        // 0x43CCA0: the object's runs end; the created critter's records go with the destroy (what is left of its list is destroyed with it).
        PassWorldAs<ISessionAiWorld>()?.Destroy(at.Code);
        if (created is not null) DestroyObject(at.Code);
        CancelBodyDecay(at.Code);
        Revise();
        _passBodyDecays?.Add(Report(SessionBodyDecayOutcome.Removed, held2.Count - destroyedItems.Length, destroyedItems.Length));
        return GameEventHandlerOutcome.Completed;
    }

    private const long ItemDecayRulesRetryMs = Arcanum.Simulation.Items.ItemDecayRules.RetryDelayMs;
}
