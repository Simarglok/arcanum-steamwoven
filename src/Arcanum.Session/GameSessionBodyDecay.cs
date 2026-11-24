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
    /// <summary>ARC-SCRIPT-2879: the remove-item scripts (28) the walk ran, one per item with the point, with the corpse as the triggerer.</summary>
    public SessionPassRemovals Removals { get; init; } = SessionPassRemovals.None;
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
    /// ARC-WORLD-1158 (the event 0x45EB50, the inventory walk 0x463860, the destroy 0x43CCA0; ledger PO10): a dead critter drops its whole inventory, last item first, and is destroyed. ARC-WORLD-1299 (VP24, VPG4): an item with spell flag
    /// 0x4000 (summoned) or item flag 0x10 (no display) is destroyed (<see cref="SessionItemFacts.Summoned"/>, <see cref="SessionItemFacts.NoDisplay"/>, read from the seed; a fact the session does not have reads as clear), every
    /// other one is laid on the body's cell by the drop rule (a junk pile with room, else loose, SettleDrop) and gets its decay event as any drop does, an item with the no-drop flag 0x20 included. A critter that is not dead is left
    /// as it is (the original's error line, no further action). A map NPC's destroy is the script destroy's map change (<see cref="ObjectDestroyedProperty"/>); a created critter's is <see cref="DestroyObject"/>'s.
    /// The host's runs of the critter end (<see cref="ISessionAiWorld.Destroy"/>). A revision step for a created critter, two for a map NPC (its destroy is a map change, which revises itself). Limits: the items of a holder in another map's seed than the body's stay with the destroyed critter,
    /// a drop script is not run (the original's flag-1 walk is not read for it; the spill of a container does the same), the hero's death queues no event, a made item's flags read as clear (its prototype is the host's, not the session's), a key or key ring added to the NPC (the ground holds none)
    /// and, in a session with no hero, a given item, gold pile or ammunition stack (their ground records exist only as a hero's drops) are destroyed with the body (<see cref="AddedBodyItem"/>), and a map NPC's other pending events (a restock, an AI tick) stay queued as after a script's destroy.
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

        // 0x463860 walks the list last first: the seed's items of the critter on this map, then the items the session added to it. An item with spell flag 0x4000 (summoned) or item flag 0x10 (no display) is
        // destroyed, every other one is laid down, an item with the no-drop flag 0x20 included (the remove check's refusal is not asked on this path; ledger VP24, VPG4).
        var held2 = new List<BodyItem>();
        if (_inventory.Seeds.TryGetValue(map, out var seed) && seed.Holders.TryGetValue(at.Code, out var holder) && holder.Kind == SessionHolderKind.Npc)
            foreach (var item in _inventory.Current(seed, holder, entity => DroppedCell(map, seed, entity), added: false))
                held2.Add(new(item.Entity, null, true, seed.Items[item.Entity].Facts is { Summoned: true } or { NoDisplay: true }));
        foreach (var made in _inventory.AddedTo(at.Code))
            held2.Add(AddedBodyItem(made, map, where));
        held2.Reverse();

        // The budgets first, so the event either does all of it or waits: the deleted-item records of the destroyed items, the created items' bound of the dropped made items, the hero's items' bound of the other placed ones.
        var destroyedItems = held2.Where(item => item.Destroy).ToArray();
        var droppedCreated = held2.Count(item => item.Placed is { Source: SessionPlacedSource.Created });
        var droppedHeld = held2.Count(item => item.Placed is { } placed && placed.Source != SessionPlacedSource.Created);
        if (_durability.Count + destroyedItems.Length > _limits.MaxDurabilityRecords || (long)_inventory.Created + droppedCreated > _limits.MaxCreatedItems ||
            (long)_inventory.Held + droppedHeld > _limits.MaxPcItems)
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
        // ARC-SCRIPT-2879 (0x463860 → 0x467860, BD3): every item of the walk, destroyed or dropped, runs its remove-item script (28; ARC-SCRIPT-2885: the wield-off script 25 first when the corpse wore the item) with the corpse as the triggerer after its unlink and before it is destroyed or laid down. Limit: the runs come after the commit of the corpse's destroy and the seed items' drop changes above (the original runs point 28 before its destroy), so a script that reads the corpse or an item's drop state through the host sees them already changed.
        var removals = new PassRemovals(this, _passWorld?.ItemScriptHost, map, npc => PassWorldAs<ISessionRestockWorld>()?.NpcWieldScripts(map, npc));
        foreach (var (entity, placed, isSeed, destroy) in held2)
        {
            removals.Run(at.Code, entity, PassItemPoints(map, entity));
            if (destroy) { DestroyObject(entity); continue; }
            later.Remove(entity);
            if (placed is not null)
            {
                _inventory.RemoveFromNpc(entity);
                // A given item goes back to the hero's side of the books: it is no longer given, it lies on the ground as a placed kit or taken item.
                _inventory.Hero.TakeGiven(entity);
                _inventory.Hero.AddPlaced(placed);
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
        _passBodyDecays?.Add(Report(SessionBodyDecayOutcome.Removed, held2.Count - destroyedItems.Length, destroyedItems.Length) with { Removals = removals.Result });
        return GameEventHandlerOutcome.Completed;
    }

    /// <summary>One item of a body's inventory walk: a seed item (<paramref name="Seed"/>), or an added one with the ground record it becomes (<paramref name="Placed"/>); <paramref name="Destroy"/> when it is destroyed instead.</summary>
    private readonly record struct BodyItem(string Entity, SessionPlacedItem? Placed, bool Seed, bool Destroy);

    /// <summary>
    /// ARC-WORLD-1299 (ledger BD3, the dead body's inventory walk of FireBodyDecay): what an item the session added to the dying NPC becomes. It is laid down like every other item unless its flags say it is destroyed: a made item's flags are not known to the session
    /// (its prototype is the host's), so it reads as clear, a given taken item's come from its origin seed's row when that seed is attached (else clear), a given kit item's read as clear. The ground record is the one the hero's own
    /// drops make, so that a save of it restores: a given kit or taken item goes back to the placed kit or taken items with its origin, a gold pile or an ammunition stack the made items of the hero with their coin or unit record, any other
    /// made item (not a key or key ring, which the ground does not hold) the created items. The limits left: a key or key ring is destroyed with the body, and so is every item whose ground record only a hero's drop makes (given, pile, stack) in a session
    /// with no hero.
    /// </summary>
    private BodyItem AddedBodyItem(SessionMadeItem item, SessionMapId map, CellCoord where)
    {
        var given = _inventory.Hero.GivenOf(item.Entity);
        var facts = AddedItemFacts(item);
        if (facts is { Summoned: true } or { NoDisplay: true }) return new(item.Entity, null, false, true);
        if (given is { } record)
        {
            // Only a hero's drops place a kit, taken or made item (a save of such an item restores only with a hero).
            if (_hero is null) return new(item.Entity, null, false, true);
            var source = record.Origin is null ? SessionPlacedSource.Kit : SessionPlacedSource.Pc;
            return new(item.Entity, new(item.Entity, item.Kind, source, record.Origin, map, where), false, false);
        }
        if (SessionMadeItem.Number(item.Entity) is null) return new(item.Entity, null, false, true);
        var pile = _inventory.Piles.ContainsKey(item.Entity);
        if (pile || item.Kind.Type == SessionItemType.Ammo)
            return _hero is null || pile && item.Kind.Type != SessionItemType.Gold
                ? new(item.Entity, null, false, true)
                : new(item.Entity, new(item.Entity, item.Kind, SessionPlacedSource.Made, null, map, where, item.Complexity), false, false);
        return InventoryDomain.Admitted(item.Kind.Type)
            ? new(item.Entity, new(item.Entity, item.Kind, SessionPlacedSource.Created, null, map, where, item.Complexity), false, false)
            : new(item.Entity, null, false, true);
    }

    // The seed row's facts of an item the session added to an NPC when it is a given taken item (its origin seed attached), else null (a made or kit item's flags read as clear).
    private SessionItemFacts? AddedItemFacts(SessionMadeItem item) =>
        _inventory.Hero.GivenOf(item.Entity) is { Origin: { } origin } && _inventory.Seeds.TryGetValue(origin, out var seed) && seed.Items.TryGetValue(item.Entity, out var row) ? row.Facts : null;

    private const long ItemDecayRulesRetryMs = Arcanum.Simulation.Items.ItemDecayRules.RetryDelayMs;
}
