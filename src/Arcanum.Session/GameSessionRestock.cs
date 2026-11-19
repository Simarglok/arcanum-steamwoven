using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>How the restock host gives an inventory source set (ARC-WORLD-094).</summary>
public enum SessionRestockSetStatus
{
    /// <summary>The set generates: <see cref="SessionRestockSet.Set"/> holds its coin pair and lines (a set the load emptied, RK6, as the
    /// coin pair its loot line read and no lines).</summary>
    Read = 1,
    /// <summary>The set's coins would be undefined (an unreachable set, RK7, or one the load emptied before its coin pair): the
    /// inventory is cleared and nothing is generated, in both modes (a technical bug of the original).</summary>
    Unreachable = 2,
    /// <summary>A set the project does not admit (<see cref="Arcanum.Data.Characters.InventorySourceStatus.Malformed"/> or
    /// <see cref="Arcanum.Data.Characters.InventorySourceStatus.InvalidText"/>, which the original reads partially): the holder keeps
    /// its inventory (Not ready).</summary>
    Unsupported = 3,
}

/// <summary>An inventory source set as the restock host reads it (<c>rules\InvenSource.mes</c> through
/// <see cref="Arcanum.Data.Characters.InventorySourceRules"/>).</summary>
public sealed record SessionRestockSet(SessionRestockSetStatus Status, RestockSet? Set);

/// <summary>ARC-WORLD-100: the host's part of a generated-once container's seed (0x463E20 with flag 0x200, ledger RK10): the container's
/// tile x (the low 32 bits of its location, field 2) and the PC's player name bytes (field 0x10E as 0x441B60 copies it for the PC itself:
/// the host's PC, else its prototype's; empty when unset). The PC's race, gender and background are the session hero's.</summary>
public sealed record SessionGeneratedOnceFacts(int LocationX, IReadOnlyList<byte> PlayerName);

/// <summary>What a basic prototype number of a set line creates (the prototype is the number + 20): its kind and, for ammunition, the
/// type and the units its prototype carries.</summary>
public readonly record struct SessionRestockItem(SessionItemKind Kind, int? AmmoType = null, int? Units = null);

/// <summary>
/// ARC-WORLD-094: a host that runs restock events during an event pass (the original's live objects). A pass whose host does not
/// implement it postpones every due restock event by <see cref="SessionEventKinds.RestockRetryDelayMs"/>.
/// </summary>
public interface ISessionRestockWorld : ISessionEventWorld
{
    /// <summary>The object's restock facts on its map (kind, inventory source, death, PC leader, container flags, screen test), or null
    /// when it no longer exists (the event then does nothing, RK3).</summary>
    RestockHolder? RestockFacts(SessionMapId map, string entity);
    /// <summary>The first NPC of the game-window-sized area around the container (0x440FC0, NPCs only, in the host's list order) whose
    /// substitute container (0x45F650: no leader, within 20 tiles) the container is; null when none is.</summary>
    string? RestockMerchant(SessionMapId map, string container);
    /// <summary>Whether the item carries the persistent item flag 0x2000 (field 0x57), which keeps it through a regeneration.</summary>
    bool Persistent(SessionMapId map, string item);
    /// <summary>The inventory source set of a source number.</summary>
    SessionRestockSet RestockSet(int source);
    /// <summary>What a set line's basic prototype number creates; null when the host has no such prototype.</summary>
    SessionRestockItem? RestockItem(int basicPrototype);
    /// <summary>The kind of a new gold pile (type gold).</summary>
    SessionItemKind RestockPileKind { get; }
    /// <summary>The insert check of the holder (0x466510) for the items its regeneration creates.</summary>
    SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder);
    /// <summary>ARC-WORLD-096: whether the host can answer now (its object data and inventory sources loaded); while it cannot, a due
    /// restock event is postponed by <see cref="SessionEventKinds.RestockRetryDelayMs"/> as without a host.</summary>
    bool RestockReady => true;
    /// <summary>ARC-WORLD-100: the host's part of the seed of a container with flag 0x200 (<see cref="SessionGeneratedOnceFacts"/>); null
    /// when the host cannot give it, and the container then keeps its list (Not ready).</summary>
    SessionGeneratedOnceFacts? GeneratedOnceFacts(SessionMapId map, string container) => null;
    /// <summary>ARC-WORLD-103: the facts of an NPC's best-items wield after its regeneration (<see cref="SessionNpcWielder"/>); null when the
    /// host cannot give them, and the NPC then wears only what the clear left it (Not ready).</summary>
    SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => null;
    /// <summary>ARC-MAGIC-1684: what the item event scripts of an NPC's best-items wield need (<see cref="SessionNpcWieldScripts"/>); null when the host cannot give it, and a choice or a worn item with a move script is then not wielded (as
    /// before the scripts were run).</summary>
    SessionNpcWieldScripts? NpcWieldScripts(SessionMapId map, string npc) => null;
    /// <summary>ARC-SCRIPT-2887 (ledger TQ19, NT2): the host that runs the get (4) and insert-item (21) scripts of an item the regeneration creates (the item move's fresh path, with the holder as the triggerer); null when the host has none, and a created
    /// item whose prototype carries either point is then not created (counted as skipped).</summary>
    ISessionItemScriptHost? ItemScripts(SessionMapId map) => null;
}

/// <summary>What one regeneration did (ARC-WORLD-094).</summary>
public enum SessionRestockOutcome
{
    /// <summary>The inventory was cleared and generated from the set.</summary>
    Regenerated = 1,
    /// <summary>The inventory was cleared and nothing generated (<see cref="SessionRestockSetStatus.Unreachable"/>).</summary>
    Cleared = 2,
    /// <summary>Nothing changed: a set the project does not admit, a holder the map's seed does not list as that kind, a spilled
    /// container, or a container with flag 0x200 whose seed the host cannot give (Not ready).</summary>
    Unsupported = 3,
}

/// <summary>One regeneration of a restock event: the holder, the outcome, how many items its clearing removed, the coins and entity of
/// the gold pile it filled (0 and null when none), the items it created in order, how many created items the holder's insert check
/// refused (destroyed, as in the original) and how many lines the session could not create (a type the lists do not admit, a prototype
/// the host does not know, a full budget: Not ready).</summary>
public sealed record SessionRestockRegeneration(string Holder, SessionRestockOutcome Outcome, int Removed, int Coins, string? Pile,
    IReadOnlyList<string> Created, int Refused, int Skipped)
{
    /// <summary>ARC-WORLD-103: an NPC's best-items wield after the regeneration (RK15); null for a container and for a regeneration that
    /// changed nothing.</summary>
    public SessionNpcWield? Wield { get; init; }

    /// <summary>ARC-SCRIPT-2887: the lines the get and insert-item scripts of the created items printed, in order, for the host to show; empty when none (<see cref="ISessionRestockWorld.ItemScripts"/>).</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get; init; } = [];
}

/// <summary>ARC-UI-038: what a regeneration asked for directly did (<see cref="GameSessionOwner.RegenerateInventory"/>).</summary>
public sealed record SessionRegenerateResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionRestockRegeneration? Regeneration,
    GameSessionVersion Version);

/// <summary>A restock event that came due (ARC-WORLD-094, ledger RK3): the object, its map, the rules' decision and the regenerations it
/// made (the merchant's first, then the container's). <see cref="Postponed"/>: the session could not run it yet (no restock host, the
/// map's seed not attached, a full deleted-item budget) and scheduled it again a minute on.</summary>
public sealed record SessionRestockFiring(GameEvent Event, SessionMapId Map, string Holder, RestockDecision Decision,
    IReadOnlyList<SessionRestockRegeneration> Regenerations)
{
    public bool Postponed { get; init; }
    /// <summary>The refusal of the new event a postponement or a <see cref="RestockStep.Reschedule"/> set (a full event budget, an
    /// exhausted generator): <see cref="GameSessionFailure.None"/> when it was set or none was due; the holder then has no pending event.</summary>
    public GameSessionFailure ScheduleFailure { get; init; }
}

/// <summary>
/// ARC-WORLD-094: the original's restock event in the session (type 0x16; 0x4640C0, 0x464180 → 0x463C60, 0x463E20, ledger RK1–RK8). The
/// host schedules it at every inventory window open on an object with an inventory source; when it fires, the rules of
/// <see cref="RestockRules"/> decide, and a regeneration deletes every item of the holder's current list but the persistent ones and
/// creates a gold pile and the set's items with labelled session rolls. Contract: docs/world/barter-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The restock schedule at an inventory window open on <paramref name="holder"/> of the actor's map (0x4640C0, called by 0x5727B0 for
    /// the shown object in every mode, RK2): with <paramref name="hasSource"/> (the host's: an NPC's or container's inventory source is
    /// not 0, <see cref="RestockRules.InventorySource"/>) the holder's pending restock event is replaced by one after a labelled
    /// <c>restock.delay</c> roll of <see cref="RestockRules.MinDelayMs"/>..<see cref="RestockRules.MaxDelayMs"/> game milliseconds
    /// (Steamwoven; the Original setting rolls <see cref="RestockRules.MinDelayMs"/>..<see cref="RestockRules.NarrowMaxDelayMs"/>, the
    /// reach of the original's 15-bit draw, VPG5) (single player); without one its pending event stays (<see cref="GameSessionDisposition.NoChange"/>), as for a container the
    /// session generated once (ARC-WORLD-100: its source is 0, RK12, whatever the host's data says). Refused before any effect:
    /// an entity that is not an object of a map (<see cref="GameSessionFailure.InvalidEvents"/>: the absent role, the player, a made
    /// item), the version, the revision, a full restock or event budget (<see cref="GameSessionFailure.EventBudgetExceeded"/>) and the
    /// roll's refusals. One revision.
    /// </summary>
    public GameSessionMutationResult ScheduleRestock(GameSessionVersion expected, string holder, bool hasSource)
    {
        ThrowIfDisposed();
        if (!SessionEventKinds.RestockHolder(holder)) return Reject(GameSessionFailure.InvalidEvents);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (!hasSource || _inventory.Generated.Contains(new(_actor.Map, holder))) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var failure = ScheduleRestockEvent(_actor.Map, holder);
        return failure == GameSessionFailure.None ? Changed() : Reject(failure);
    }

    /// <summary>The pending restock events (ARC-WORLD-094): the object, its map and the deadline, in queue order.</summary>
    public IReadOnlyList<(string Holder, SessionMapId Map, long DeadlineMs)> QueryRestocks()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.Restock)
            .Select(e => (e.Payload.Entities[0].Code, new SessionMapId(e.Payload.Entities[0].Map), e.DeadlineMs)).ToArray());
    }

    /// <summary>
    /// ARC-UI-038: a regeneration asked for directly (0x463E20, RK4–RK6, RK15), as the opening shop asks for its merchant's and then its
    /// merchant's substitute container's (0x545F1D, 0x545F34, ledger OS7): none of the restock event's tests (death, a PC leader, the
    /// screen) run. The <paramref name="holder"/> of the actor's map is cleared but for its persistent items (RK5), also when its source
    /// (the host's <see cref="ISessionRestockWorld.RestockFacts"/>) is 0, which then ends it; otherwise it is generated from its set with
    /// the session's labelled rolls (a container with flag 0x200 with the seeded draws of <see cref="GenerateContainerOnce"/>, and then
    /// recorded as generated), and an NPC with a source then wields its best items (<see cref="SessionRestockRegeneration.Wield"/>). A
    /// holder the session cannot regenerate (a set it does not admit, a spilled container, no seed facts from the host)
    /// keeps its list (<see cref="SessionRestockOutcome.Unsupported"/>, no change). Refused before any effect: an invalid entity
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, a host not ready (<see cref="ISessionRestockWorld.RestockReady"/>) or no
    /// seed for the actor's map (<see cref="GameSessionFailure.InventoryUnavailable"/>), an object the seed or the host does not list as
    /// an NPC or container of that kind (<see cref="GameSessionFailure.UnknownHolder"/>), a full record, list or generated budget
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>) and the revision. One revision. The host must not call back into the session
    /// from its callbacks during the regeneration.
    /// </summary>
    public SessionRegenerateResult RegenerateInventory(GameSessionVersion expected, string holder, ISessionRestockWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        SessionRegenerateResult Refuse(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, Version);
        if (!SessionEventKinds.RestockHolder(holder)) return Refuse(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Refuse(GameSessionFailure.StaleVersion);
        var map = _actor.Map;
        if (!world.RestockReady || !_inventory.Seeds.TryGetValue(map, out var seed)) return Refuse(GameSessionFailure.InventoryUnavailable);
        if (!seed.Holders.TryGetValue(holder, out var row) || world.RestockFacts(map, holder) is not { } found ||
            (found.Kind == RestockHolderKind.Npc ? SessionHolderKind.Npc : found.Kind == RestockHolderKind.Container ? SessionHolderKind.Container : (SessionHolderKind?)null) != row.Kind)
            return Refuse(GameSessionFailure.UnknownHolder);
        var facts = GeneratedFacts(map, holder, found);
        var plan = PlanRestock(world, map, seed, holder, facts);
        // A call back into the session from the host may have moved it on.
        if (!IsCurrent(expected)) return Refuse(GameSessionFailure.StaleVersion);
        if (plan.Outcome == SessionRestockOutcome.Unsupported)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, new(holder, plan.Outcome, 0, 0, null, [], 0, 0), Version);
        if (!RestockFits([plan])) return Refuse(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Refuse(GameSessionFailure.RevisionExhausted);
        var done = Regenerate(world, map, plan);
        // RK15: only a regeneration that read a set dresses the NPC; a source of 0 ends after the clear.
        if (plan.Kind == SessionHolderKind.Npc && RestockRules.InventorySource(facts) != 0) done = done with { Wield = WieldNpcBest(world, map, seed, holder) };
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, done, Version);
    }

    // The holder's restock event replaced by a new one: after a restock.delay roll, or after the given delay (the retry). The rolled draw
    // comes after the budget checks, so a refusal draws nothing.
    private GameSessionFailure ScheduleRestockEvent(SessionMapId map, string holder, long? delayMs = null)
    {
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var entity = new GameEventEntity(map.RuntimeMap, holder);
        if (!_events.Any(SessionEventKinds.Restock, e => e.Payload.Entities[0] == entity) &&
            (_events.Count >= _events.Limits.MaxPending || _events.QueryPending(SessionEventKinds.Restock).Length >= SessionEventKinds.MaxRestocks))
            return GameSessionFailure.EventBudgetExceeded;
        if (_events.NextSequence == long.MaxValue || now > long.MaxValue - RestockRules.MaxDelayMs) return GameSessionFailure.EventBudgetExceeded;
        long delay;
        if (delayMs is { } given) delay = given;
        else
        {
            // VPG5: the original's 15-bit draw reaches only 12 hours plus 32,767 ms; Steamwoven draws the intended 12..24 hours.
            var roll = Roll(RestockDelayLabel, (int)RestockRules.MinDelayMs,
                (int)(_rules.Items.RestockDelayIsNarrow ? RestockRules.NarrowMaxDelayMs : RestockRules.MaxDelayMs));
            if (roll.Roll is null) return roll.Failure;
            // The quarter delay of a multiplayer game is not modelled.
            delay = RestockRules.Delay(roll.Roll.Value, multiplayer: false);
        }
        var scheduled = _events.ScheduleAfter(SessionEventKinds.Restock, GameEventPayload.Of([entity], now, delay), delay, EventClocks(now));
        return scheduled.Disposition is GameEventScheduleDisposition.Added or GameEventScheduleDisposition.Replaced ? GameSessionFailure.None
            : GameSessionFailure.EventConflict;
    }

    internal const string RestockDelayLabel = "restock.delay";

    // The restock event (type 0x16; 0x464180 → 0x463C60, RK3): the host's facts decide through RestockRules.Fire; a container on screen is
    // scheduled again; a regeneration (the merchant's first, when it has a source of its own, then the container's) is one revision. The
    // event is never repeated otherwise: a holder that does nothing gets no new event.
    [Arcanum.Simulation.OriginalFunction(0x464180, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireRestock(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0];
        var map = new SessionMapId(entity.Map);
        GameEventHandlerOutcome Report(RestockDecision decision, IReadOnlyList<SessionRestockRegeneration> regenerations, bool postponed = false,
            GameSessionFailure scheduled = GameSessionFailure.None)
        {
            _passRestocks?.Add(new(firing.Event, map, entity.Code, decision, regenerations) { Postponed = postponed, ScheduleFailure = scheduled });
            return GameEventHandlerOutcome.Completed;
        }
        GameEventHandlerOutcome Postpone(RestockDecision decision) =>
            Report(decision, [], postponed: true, ScheduleRestockEvent(map, entity.Code, SessionEventKinds.RestockRetryDelayMs));
        var none = new RestockDecision(RestockStep.None, false);
        if (PassWorldAs<ISessionRestockWorld>() is not { RestockReady: true } world || !_inventory.Seeds.TryGetValue(map, out var seed)) return Postpone(none);
        if (world.RestockFacts(map, entity.Code) is not { } facts || !Enum.IsDefined(facts.Kind)) return Report(none, []);
        var holder = GeneratedFacts(map, entity.Code, facts);
        string? merchant = null;
        RestockHolder? merchantFacts = null;
        // Only an off-screen container that does not regenerate alone looks for its merchant (RK3).
        if (holder.Kind == RestockHolderKind.Container && RestockRules.InventorySource(holder) != 0 &&
            (holder.ContainerFlags & RestockRules.RegeneratesAloneFlag) == 0 && !holder.OnScreen &&
            world.RestockMerchant(map, entity.Code) is { } found && found != entity.Code && SessionEventKinds.RestockHolder(found) &&
            world.RestockFacts(map, found) is { Kind: RestockHolderKind.Npc } user)
            (merchant, merchantFacts) = (found, user);
        var decision = RestockRules.Fire(holder, merchantFacts);
        if (decision.Step == RestockStep.None) return Report(decision, []);
        if (decision.Step == RestockStep.Reschedule) return Report(decision, [], scheduled: ScheduleRestockEvent(map, entity.Code));
        var plans = new List<RestockPlan>();
        if (decision.Step == RestockStep.RegenerateMerchantAndContainer && decision.MerchantRegenerates)
            plans.Add(PlanRestock(world, map, seed, merchant!, merchantFacts!.Value));
        plans.Add(PlanRestock(world, map, seed, entity.Code, holder));
        if (!RestockFits(plans)) return Postpone(decision);
        if (_revision == long.MaxValue) return GameEventHandlerOutcome.Refused;
        // ARC-WORLD-103: an NPC wields its best items at the end of its own regeneration, before the container's (RK15).
        var done = plans.Select(plan => Regenerate(world, map, plan) is var regeneration && plan.Kind == SessionHolderKind.Npc &&
            regeneration.Outcome != SessionRestockOutcome.Unsupported ? regeneration with { Wield = WieldNpcBest(world, map, seed, plan.Holder) } : regeneration).ToArray();
        if (done.Any(regeneration => regeneration.Outcome != SessionRestockOutcome.Unsupported)) Revise();
        return Report(decision, Array.AsReadOnly(done));
    }

    // What one regeneration will do: the holder's kind, its set (null for one that clears only), the items its clearing deletes and the
    // ones that stay (persistent), or an unsupported holder or set that keeps everything. ARC-WORLD-100: a container with flag 0x200 draws
    // from the original generator at Seed, and with a source it is recorded as generated once (Generates).
    private sealed record RestockPlan(string Holder, SessionHolderKind Kind, SessionRestockOutcome Outcome, RestockSet? Set,
        IReadOnlyList<string> Deleted, IReadOnlyList<SessionInventoryItem> Kept, int? Seed = null, bool Generates = false);

    private RestockPlan PlanRestock(ISessionRestockWorld world, SessionMapId map, InventoryDomain.Seed seed, string holder, RestockHolder facts)
    {
        var kind = facts.Kind == RestockHolderKind.Npc ? SessionHolderKind.Npc : SessionHolderKind.Container;
        RestockPlan Unsupported() => new(holder, kind, SessionRestockOutcome.Unsupported, null, [], []);
        // A holder of the map's seed as that kind; a spilled container's items lie on the ground. ARC-SCRIPT-2857 (NT2): a holder's transfer script (point 32) is no gate here: a created item has no parent, so 0x461810 never
        // reaches the parented transfer 0x461CA0 for it.
        if (facts.Kind == RestockHolderKind.Other || !seed.Holders.TryGetValue(holder, out var row) || row.Kind != kind ||
            kind == SessionHolderKind.Container && SpillCell(map, seed, holder) is not null) return Unsupported();
        // ARC-WORLD-100: a container with flag 0x200 (0x463E20, RK10) seeds its draws from the hero and the host's tile x and name bytes;
        // without them it keeps its list.
        var source = RestockRules.InventorySource(facts);
        int? draws = null;
        // A source of 0 draws nothing, so it needs no seed.
        if (kind == SessionHolderKind.Container && (facts.ContainerFlags & RestockRules.GeneratedOnceFlag) != 0 && source != 0)
        {
            if (world.GeneratedOnceFacts(map, holder) is not { PlayerName: { } name } once) return Unsupported();
            draws = RestockRules.GeneratedOnceSeed(GeneratedOncePc([.. name]), once.LocationX);
        }
        SessionRestockSet? read = null;
        if (source != 0)
        {
            read = world.RestockSet(source);
            if (read is null || !Enum.IsDefined(read.Status) || read.Status == SessionRestockSetStatus.Unsupported) return Unsupported();
            if (read.Status == SessionRestockSetStatus.Read && (read.Set?.Lines is not { } lines || lines.Count > RestockRules.MaxLines)) return Unsupported();
        }
        // The clear (0x463B30 with keep 1, RK5): every entry of the current list, worn items and gold included, but the persistent ones.
        // A source of 0 (only a first open's container with flag 0x200 reaches here with one) ends the regeneration after it.
        var current = _inventory.Current(seed, row, item => DroppedCell(map, seed, item)).ToArray();
        var kept = current.Where(item => world.Persistent(map, item.Entity)).ToArray();
        var deleted = current.Where(item => !kept.Contains(item)).Select(item => item.Entity).ToArray();
        // After the lines, and only with a source, the container's source and flag 0x200 are cleared (0x4EFDD0, RK10); an unreachable
        // set (RK7) generates nothing but reaches that clearing too.
        var generates = draws is not null && source != 0;
        return read?.Status == SessionRestockSetStatus.Read
            ? new(holder, kind, SessionRestockOutcome.Regenerated, read.Set, deleted, kept, draws, generates)
            : new(holder, kind, SessionRestockOutcome.Cleared, null, deleted, kept, draws, generates);
    }

    // Whether the session-wide budgets hold the plans; otherwise nothing is cleared yet. A deletion of an item that is not a made one keeps
    // a deleted-item record; the item, pile and stack budgets must hold every line and a pile after the clear, or the clear would leave an
    // empty list; and (ARC-WORLD-100) every container generated once is recorded.
    private bool RestockFits(IReadOnlyCollection<RestockPlan> plans)
    {
        var records = plans.SelectMany(plan => plan.Deleted).Count(item => SessionMadeItem.Number(item) is null && !_durability.Records.ContainsKey(item));
        if (_durability.Count + records > _limits.MaxDurabilityRecords) return false;
        var lines = plans.Sum(plan => plan.Set?.Lines.Count + 1 ?? 0);
        var freed = plans.SelectMany(plan => plan.Deleted).Count(item => _inventory.NpcOf(item) is not null);
        return _inventory.NpcHeld - freed + lines <= _limits.MaxNpcItems && _inventory.Piles.Count + plans.Count <= _limits.MaxGoldRecords &&
            _inventory.Ammo.Count + lines <= _limits.MaxAmmoRecords && _inventory.Generated.Count + plans.Count(plan => plan.Generates) <= SessionInventoryState.MaxGenerated &&
            // ARC-WORLD-103: and the record of every NPC its wield dresses.
            _inventory.NpcWorn.Count + plans.Count(plan => plan.Kind == SessionHolderKind.Npc && plan.Outcome != SessionRestockOutcome.Unsupported &&
                !_inventory.NpcWorn.ContainsKey(plan.Holder)) <= SessionInventoryState.MaxNpcWorn;
    }

    // ARC-WORLD-100: the host's facts of a container the session generated once have no source and no flag 0x200 any more (RK10).
    private RestockHolder GeneratedFacts(SessionMapId map, string holder, RestockHolder facts) =>
        facts.Kind == RestockHolderKind.Container && _inventory.Generated.Contains(new(map, holder))
            ? facts with { InventorySource = 0, ContainerFlags = facts.ContainerFlags & ~RestockRules.GeneratedOnceFlag }
            : facts;

    // ARC-WORLD-100: the PC of a generated-once seed (0x40DA50): the hero's race (stat 27, the source's index) and gender (stat 26: female
    // 0, male 1), which no session effect changes, and its background (field 0x101), with the host's name bytes; none without a hero.
    private RestockSeedPc? GeneratedOncePc(byte[] name) => _hero is not { } hero ? null
        : new(HeroDomain.SourceRace(hero.State.Race), hero.State.Gender == Arcanum.Simulation.Character.CharacterGender.Female ? 0 : 1, hero.State.Background, name);

    // The regeneration (0x463E20, RK4–RK6): the clear, the coins (a new pile, or the kept pile's coins grow) and each line's item, created
    // and moved in; an item the holder's insert check refuses is destroyed. ARC-WORLD-103: the NPC's wield of its best items follows it
    // (WieldNpcBest).
    // ARC-WORLD-100: a seeded plan draws from the original generator (RK10, RK11), not the session's, and a container generated once is
    // recorded (its source and flag 0x200 cleared).
    private SessionRestockRegeneration Regenerate(ISessionRestockWorld world, SessionMapId map, RestockPlan plan)
    {
        if (plan.Outcome == SessionRestockOutcome.Unsupported) return new(plan.Holder, plan.Outcome, 0, 0, null, [], 0, 0);
        foreach (var item in plan.Deleted) Discard(item);
        if (plan.Set is null)
        {
            if (plan.Generates) _inventory.Generated.Add(new(map, plan.Holder));
            return new(plan.Holder, plan.Outcome, plan.Deleted.Count, 0, null, [], 0, 0);
        }
        var loot = plan.Seed is { } draws ? RestockRules.GenerateOnce(plan.Set, draws) : RestockRules.Generate(plan.Set, new SessionRestockRolls(this));
        var insert = world.RestockInsertCheck(map, plan.Holder);
        var (refused, skipped) = (0, 0);
        var (coins, pile) = (0, (string?)null);
        var scripts = world.ItemScripts(map);
        var report = new CreatedScripts();
        if (loot.Coins > 0)
        {
            var outcome = RestockCoins(world.RestockPileKind, plan, loot.Coins, insert, out pile, scripts, report);
            if (outcome == RestockCreation.Created) coins = loot.Coins;
            else if (outcome == RestockCreation.Refused) refused++;
            else skipped++;
        }
        var created = new List<string>();
        foreach (var prototype in loot.BasicPrototypes)
        {
            var outcome = RestockItem(world.RestockItem(prototype), plan, insert, out var entity, scripts, report);
            if (outcome == RestockCreation.Created) { if (entity is not null) created.Add(entity); }
            else if (outcome == RestockCreation.Refused) refused++;
            else skipped++;
        }
        if (plan.Generates) _inventory.Generated.Add(new(map, plan.Holder));
        return new(plan.Holder, plan.Outcome, plan.Deleted.Count, coins, pile, created.AsReadOnly(), refused, skipped) { Messages = report.Messages.AsReadOnly() };
    }

    private enum RestockCreation { Created, Refused, Skipped }

    // The coins (0x464830 with no pile given): a new pile moved in; a pile that stayed (persistent) takes them as the insertion's merge.
    private RestockCreation RestockCoins(SessionItemKind pileKind, RestockPlan plan, int coins, SessionInsertCheck insert, out string? pile, ISessionItemScriptHost? scripts, CreatedScripts report)
    {
        pile = null;
        if (pileKind.Type != SessionItemType.Gold) return RestockCreation.Skipped;
        if (plan.Kept.FirstOrDefault(item => item.Facts.Kind.Type == SessionItemType.Gold) is { } kept)
        {
            var held = _inventory.Piles.TryGetValue(kept.Entity, out var tracked) ? tracked : kept.Facts.Quantity ?? 0;
            if ((long)held + coins > int.MaxValue || !_inventory.Piles.ContainsKey(kept.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords)
                return RestockCreation.Skipped;
            var (revision, seeds) = (_revision, _inventory.SeedGeneration);
            var verdict = insert(new(kept.Entity, InventoryDomain.MadeFacts(pileKind) with { Quantity = coins }));
            ThrowIfDisposed();
            // A call back into the session from the check may have changed the pile: nothing is merged then.
            if (_revision != revision || _inventory.SeedGeneration != seeds) return RestockCreation.Skipped;
            if (verdict != SessionInsertVerdict.Accepted) return RestockCreation.Refused;
            _inventory.Piles[kept.Entity] = held + coins;
            pile = kept.Entity;
            return RestockCreation.Created;
        }
        if (_inventory.Piles.Count >= _limits.MaxGoldRecords) return RestockCreation.Skipped;
        var created = NewNpcEntry(plan.Holder, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = coins }, insert, out var refusal, plan.Kind, scripts, report);
        if (created is null) return refusal?.Failure == GameSessionFailure.InsertRefused ? RestockCreation.Refused : RestockCreation.Skipped;
        _inventory.Piles[created.Value.Entity] = coins;
        pile = created.Value.Entity;
        return RestockCreation.Created;
    }

    // One line's item (0x4ED8B0 and 0x4617F0): a new made item at the end of the holder's list, or the units of ammunition joining the
    // holder's stack as the insertion merges them (an NPC's stack of the prototype, as AddItemToNpc; a container's by the type lookup,
    // which follows AMA5). Gold, keys and key rings are no item the lists admit (Not ready).
    private RestockCreation RestockItem(SessionRestockItem? made, RestockPlan plan, SessionInsertCheck insert, out string? entity, ISessionItemScriptHost? scripts, CreatedScripts report)
    {
        entity = null;
        if (made is not { } item || !Enum.IsDefined(item.Kind.Type) || !InventoryDomain.Admitted(item.Kind.Type)) return RestockCreation.Skipped;
        var ammo = item.Kind.Type == SessionItemType.Ammo;
        if (ammo ? item.AmmoType is not (>= 0 and < SessionAmmoRecord.AmmoTypes) || item.Units is not >= 0 : item.AmmoType is not null || item.Units is not null)
            return RestockCreation.Skipped;
        if (ammo)
        {
            var (type, units) = (item.AmmoType!.Value, item.Units!.Value);
            SessionAmmoRecord? stack = plan.Kind == SessionHolderKind.Npc
                ? NpcStacks(plan.Holder).Where(pair => pair.Prototype == item.Kind.Prototype).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault()
                : ContainerStackOfType(plan.Holder, type);
            if (stack is { } into)
            {
                if ((long)into.Units + units > int.MaxValue || !_inventory.Ammo.ContainsKey(into.Entity) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords)
                    return RestockCreation.Skipped;
                var (revision, seeds) = (_revision, _inventory.SeedGeneration);
                var verdict = insert(new(into.Entity, InventoryDomain.MadeFacts(item.Kind) with { AmmoType = type, Quantity = units }));
                ThrowIfDisposed();
                if (_revision != revision || _inventory.SeedGeneration != seeds) return RestockCreation.Skipped;
                if (verdict != SessionInsertVerdict.Accepted) return RestockCreation.Refused;
                _inventory.Ammo[into.Entity] = into with { Units = into.Units + units };
                entity = into.Entity;
                return RestockCreation.Created;
            }
            if (_inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RestockCreation.Skipped;
        }
        var facts = ammo ? InventoryDomain.MadeFacts(item.Kind) with { AmmoType = item.AmmoType, Quantity = item.Units } : InventoryDomain.MadeFacts(item.Kind);
        var created = NewNpcEntry(plan.Holder, item.Kind, facts, insert, out var refusal, plan.Kind, scripts, report);
        if (created is null) return refusal?.Failure == GameSessionFailure.InsertRefused ? RestockCreation.Refused : RestockCreation.Skipped;
        if (ammo) _inventory.Ammo[created.Value.Entity] = new(created.Value.Entity, item.AmmoType!.Value, item.Units!.Value);
        entity = created.Value.Entity;
        return RestockCreation.Created;
    }

    // The clear's destruction of one item (0x43CCA0): the durability deletion; a made item leaves no deleted-item record (no seed lists
    // it, so nothing has to hide it) and its number stays used, so restocks do not fill the deleted-item budget.
    private void Discard(string entity)
    {
        DestroyObject(entity);
        if (SessionMadeItem.Number(entity) is not { } number) return;
        _durability.Destroyed.Remove(entity);
        _inventory.Taken.Remove(entity);
        _inventory.ForgetSlot(entity);
        _inventory.LastDiscardedMade = Math.Max(_inventory.LastDiscardedMade, number);
    }
}

/// <summary>The session generator behind <see cref="IRestockRolls"/> (ARC-WORLD-094): the coins are labelled <c>restock.coins</c>, each
/// line's percent <c>restock.line</c>; refusals throw as in <see cref="SessionCombatRolls"/>.</summary>
internal sealed class SessionRestockRolls(GameSessionOwner owner) : IRestockRolls
{
    public int Roll(RestockRoll purpose, int min, int max)
    {
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        var label = purpose switch
        {
            RestockRoll.Coins => "restock.coins",
            RestockRoll.Line => "restock.line",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
        var result = owner.Roll(label, min, max);
        return result.Roll?.Value ?? throw new InvalidOperationException($"Session roll {purpose} refused: {result.Failure}.");
    }
}
