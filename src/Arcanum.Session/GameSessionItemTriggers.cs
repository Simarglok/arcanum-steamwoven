using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>One executor call of a combat or life event through the trigger host (ARC-MAGIC-023): the event, its mask, the worn item,
/// its holder (the event's parent), the event's other object and what the host did (<see cref="ISessionItemTriggers.Fire"/>).</summary>
public sealed record SessionItemTriggerFiring(ItemTriggerEvent Event, uint Mask, string Item, string Holder, string? Other, SessionItemTriggerResult Result);

/// <summary>ARC-MAGIC-024: one wrapper call a life step calls for (<see cref="SessionLifeResult.ItemEvents"/>): the event, its parent
/// (whose worn items walk: the hero's <see cref="SessionEventKinds.PlayerEntity"/> or an NPC's entity), its other object and, for
/// the parent hit, the melee bit.</summary>
public sealed record SessionItemEvent(ItemTriggerEvent Event, string Parent, string? Other, bool Melee = false)
{
    /// <summary>ARC-MAGIC-025: for <see cref="ItemTriggerEvent.ParentAttacksLocation"/> the projectile's item (0x4CBDB0 names one item
    /// and walks nothing; none fires nothing); null for every other event.</summary>
    public string? Item { get; init; }

    /// <summary>ARC-MAGIC-025: for <see cref="ItemTriggerEvent.ParentAttacksLocation"/> the tile its runs aim at (the struck object's,
    /// the destination or the range end, IC12); none for every other event.</summary>
    public SpellLocation Tile { get; init; }

    /// <summary>ARC-MAGIC-025, MG46: the items whose runs led to this event (the damage step of a run an item event started: its source
    /// and the items before it in its chain); null for an event no item run caused. Steamwoven's walk skips them
    /// (<see cref="ItemSpellTriggerRules.ChainSkips"/>).</summary>
    public IReadOnlyCollection<string>? Chain { get; init; }
}

/// <summary>ARC-MAGIC-024: the parties of a damage step for its item events (DH1): the attacker (the context's +0x08, a spell run's
/// parent) and the damage source the kill credits (+0x30: the run's other object, else its parent), each an entity id or none.</summary>
public sealed record SessionDamageParties(string? Attacker, string? Killer)
{
    internal bool Valid => (Attacker is null || SessionValidation.Entity(Attacker)) && (Killer is null || SessionValidation.Entity(Killer));
}

/// <summary>ARC-MAGIC-024: the walks of <see cref="GameSessionOwner.FireItemEvents"/>: the executor calls in order, or the refusal
/// (<see cref="GameSessionFailure.InvalidSpellRun"/> from inside the run rules, <see cref="GameSessionFailure.InvalidEventPass"/>
/// during an event pass), which fires nothing.</summary>
public sealed record SessionItemEventResult(GameSessionFailure Failure, IReadOnlyList<SessionItemTriggerFiring> Firings);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-023: fires damage step item events of an attack (0x4B4390, ledger IC2; <see cref="ItemSpellTriggerRules.DamageStep"/>)
    /// through <paramref name="triggers"/>, in order. Each wrapper walks its parent's worn items in slot order
    /// (<see cref="ItemSpellTriggerRules.WornCalls"/>): the victim's are <paramref name="targetWorn"/>, the attacker's
    /// <paramref name="attackerWorn"/> (each as the caller has them at this point of the step; none without the host's item data).
    /// The hero's attack is the attacker <see cref="SessionEventKinds.PlayerEntity"/>; ARC-MAGIC-1188: an NPC's is its entity, and the
    /// victim may be the hero (the original's wrappers take any PC or NPC as the parent, IC1). Nothing without a host.
    /// </summary>
    private IReadOnlyList<SessionItemTriggerFiring> FireDamageStepItems(ISessionItemTriggers? triggers, IEnumerable<ItemTriggerStep> steps,
        string target, IReadOnlyList<string> targetWorn, string attacker, IReadOnlyList<string> attackerWorn, string? weapon)
    {
        if (triggers is null) return [];
        var events = steps.Select(step => new SessionItemEvent(step.Event, step.Parent == ItemTriggerParty.Target ? target : attacker,
            step.Other switch { ItemTriggerParty.Target => target, ItemTriggerParty.None => null, _ => attacker }, step.Melee));
        return WalkItemEvents(triggers, events, holder => holder == target ? targetWorn : attackerWorn, weapon, null);
    }

    /// <summary>
    /// ARC-MAGIC-024: fires <paramref name="events"/> (a life step's <see cref="SessionLifeResult.ItemEvents"/>) through
    /// <paramref name="triggers"/>, in order: each wrapper walks its parent's worn items in slot order
    /// (<see cref="ItemSpellTriggerRules.WornCalls"/>; no weapon, so no damages opponent with the item): the hero's as the session
    /// holds them now, an NPC's from <paramref name="npcWorn"/> (null for an entity that is no NPC the host knows: any other parent
    /// fires nothing, IC1). Refused, firing nothing, from inside the run rules (the hazard of a spell's damage step: its events fire
    /// after the run's pass, <see cref="SessionSpellCaster.FireItemEvents"/>) and during an event pass.
    /// ARC-MAGIC-025: attacks a location fires its one item with the event's tile, its parent the run's other object (0x4CBDB0); an
    /// attacks an opponent's first worn item carries its parent as the run's other object (0x4CBD40); a parent that
    /// <paramref name="isItem"/> names an item fires only on damages an opponent, its own target hit with the event's other object
    /// (the victim) as the holder (0x4CBE70's item branch). MG46: in Steamwoven a walk skips the items of the event's chain
    /// (<see cref="ItemSpellTriggerRules.ChainSkips"/>). The chain goes on only through the cast host
    /// (<see cref="SessionSpellCaster.FireItemEvents"/>), which records it for the runs a firing starts; a host that fires chained
    /// events here directly starts runs without one, so the chain restarts at their sources. An event with an item or tile that is
    /// not an attacks a location, or a chain entry that is not an entity id, is refused (<see cref="ArgumentException"/>).
    /// </summary>
    public SessionItemEventResult FireItemEvents(ISessionItemTriggers triggers, IEnumerable<SessionItemEvent> events, Func<string, IReadOnlyList<string>?> npcWorn,
        Func<string, bool>? isItem = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(npcWorn);
        if (_spellRunsBusy) return new(GameSessionFailure.InvalidSpellRun, []);
        if (_passWorld is not null) return new(GameSessionFailure.InvalidEventPass, []);
        var list = events.ToArray();
        if (list.Any(e => e is null || !SessionValidation.Entity(e.Parent) || e.Other is { } other && !SessionValidation.Entity(other)
            || (e.Event == ItemTriggerEvent.ParentAttacksLocation ? e.Item is { } item && !SessionValidation.Entity(item) : e.Item is not null || !e.Tile.IsNone)
            || e.Chain is { } chain && chain.Any(id => !SessionValidation.Entity(id))))
            throw new ArgumentException("Invalid item event.", nameof(events));
        return new(GameSessionFailure.None, WalkItemEvents(triggers, list, holder => holder == SessionEventKinds.PlayerEntity
            ? [.. _equipment.Worn.Values.OrderBy(item => item.Slot).Select(item => item.Entity)] : npcWorn(holder), null, isItem));
    }

    /// <summary>
    /// ARC-MAGIC-025: the item event of an attack animation's begin (0x432700 → 0x4CBD40, IC6,
    /// <see cref="ItemSpellTriggerRules.AttackStart"/>): <paramref name="attacker"/>'s attacks an opponent with
    /// <paramref name="target"/> (an entity id: the hero's <see cref="SessionEventKinds.PlayerEntity"/>, an NPC's or an object's) as
    /// the other object. The host asks at every swing's begin, after its checks and its action point charge (a refusal starts no
    /// animation), and fires it (<see cref="SessionSpellCaster.FireItemEvents"/>); a throw starts no attack animation and asks nothing.
    /// </summary>
    public IReadOnlyList<SessionItemEvent> QueryAttackStartItemEvents(string attacker, string target)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(attacker) || !SessionValidation.Entity(target)) throw new ArgumentException("Invalid attacker or target.");
        return Resolved(ItemSpellTriggerRules.AttackStart(), target, new(attacker, null));
    }

    /// <summary>
    /// ARC-MAGIC-025: the item event of the end of <paramref name="shot"/>'s flight at <paramref name="tile"/> (0x4B2870 → 0x4CBDB0,
    /// IC12): the hero's attacks a location with the projectile's item (the weapon wielded at the shot, or the thrown item) at the
    /// struck object's tile (a strike, <see cref="LandShot"/>, <see cref="LandShotOnObject"/>), at the destination of a shot without a
    /// target or of a boomerang flying back, or at the projectile's tile at its range end (<see cref="EndShot"/>,
    /// <see cref="LandThrownItem"/>, a boomerang's <see cref="ReturnShot"/>); the original fires it after the projectile's end and the
    /// hit. The host asks while the shot is still in flight, before that call, and fires the event after it; not for a flight it
    /// gave up (the map changed). Empty for a shot not in flight, a bare-handed one, or a thrown shot that struck already (its strike
    /// fired it; the landing the session splits off is the same end). Throws for a tile outside 0 .. 2^24 − 1.
    /// </summary>
    public IReadOnlyList<SessionItemEvent> QueryShotEndItemEvents(SessionShot shot, Arcanum.Simulation.Movement.CellCoord tile)
    {
        ThrowIfDisposed();
        if (!DropCellFits(tile)) throw new ArgumentOutOfRangeException(nameof(tile));
        // ARC-DEV-258: an NPC's thrown item attacks the location with the thrower as the event's parent.
        if (_npcShots.TryGetValue(shot, out var npc))
            return [new(ItemTriggerEvent.ParentAttacksLocation, npc.Thrower, null) { Item = npc.Thrown.Item, Tile = new((uint)tile.X, (uint)tile.Y) }];
        if (!_shots.TryGetValue(shot, out var pending) || pending.Struck || pending.WeaponItem is not { } item) return [];
        return [new(ItemTriggerEvent.ParentAttacksLocation, SessionEventKinds.PlayerEntity, null) { Item = item, Tile = new((uint)tile.X, (uint)tile.Y) }];
    }

    /// <summary>
    /// ARC-MAGIC-024: the item event of <paramref name="target"/>'s dying animation's end (0x42FF40, IC5,
    /// <see cref="ItemSpellTriggerRules.DyingEnd"/>): its dying with no other object when it is dead, else none (also for a target
    /// the session cannot resolve). The host calls it when the animation ends (not for a goal with flag 0x200) and fires the event
    /// (<see cref="SessionSpellCaster.DyingAnimationEnded"/>); the object flags and the end of the game are the host's.
    /// </summary>
    public IReadOnlyList<SessionItemEvent> QueryDyingEndItemEvents(SessionLifeTarget target)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (Resolve(target, out var record, out var facts, out _) != GameSessionFailure.None) return [];
        return Resolved(ItemSpellTriggerRules.DyingEnd(facts!, record.State), LifeEntity(target), null);
    }

    // The walk of each event over its parent's worn items (IC1); a parent without worn items the caller knows fires nothing. An attacks
    // a location fires its item alone (0x4CBDB0), an item parent its own target hit (0x4CBE70's item branch, IC13); MG46: Steamwoven
    // skips the items of the event's chain.
    private List<SessionItemTriggerFiring> WalkItemEvents(ISessionItemTriggers triggers, IEnumerable<SessionItemEvent> events,
        Func<string, IReadOnlyList<string>?> wornOf, string? weapon, Func<string, bool>? isItem)
    {
        var firings = new List<SessionItemTriggerFiring>();
        void Call(SessionItemEvent e, ItemTriggerCall call, string item, string holder, string? other)
        {
            if (ItemSpellTriggerRules.ChainSkips(_rules.Magic, item, e.Chain)) return;
            firings.Add(new(e.Event, call.Mask, item, holder, other,
                triggers.Fire(call.Mask, item, holder, other, e.Tile, call.RunOtherIsParent ? e.Parent : null)));
        }
        foreach (var e in events)
        {
            if (e.Event == ItemTriggerEvent.ParentAttacksLocation)
            {
                if (ItemSpellTriggerRules.LocationCall(e.Item is not null) is { } location) Call(e, location, e.Item!, e.Parent, null);
                continue;
            }
            if (isItem?.Invoke(e.Parent) == true)
            {
                // The executor's parent is the victim; without one its run has no parent to aim at, which the host cannot name.
                if (e.Other is { } victim)
                    foreach (var call in ItemSpellTriggerRules.ItemParentCalls(e.Event)) Call(e, call, e.Parent, victim, e.Parent);
                continue;
            }
            if (wornOf(e.Parent) is not { } worn) continue;
            var used = weapon is null ? -1 : worn.ToList().IndexOf(weapon);
            foreach (var call in ItemSpellTriggerRules.WornCalls(e.Event, Math.Min(worn.Count, ItemSpellTriggerRules.WornSlots), e.Melee, used < 0 ? null : used))
                Call(e, call, worn[call.Item], e.Parent, e.Other);
        }
        return firings;
    }

    // A life step's item events (ARC-MAGIC-024): a damage step with its parties walks as the spell's (DH1, IC2), any other step
    // only the fatigue setter's going unconscious (IC4).
    private IReadOnlyList<SessionItemEvent> LifeItemEvents(SessionLifeTarget target, LifeFacts facts, LifeOutcome outcome,
        (LifeDamage Damage, SessionDamageParties Parties)? damageStep)
    {
        if (damageStep is not { } step) return Resolved(ItemSpellTriggerRules.LifeStep(facts, outcome), LifeEntity(target), null);
        var parties = step.Parties;
        return Resolved(ItemSpellTriggerRules.SpellDamageStep(facts, step.Damage, outcome, parties.Attacker is not null, parties.Killer is not null),
            LifeEntity(target), parties);
    }

    private static SessionItemEvent[] Resolved(IEnumerable<ItemTriggerStep> steps, string target, SessionDamageParties? parties)
    {
        string? Of(ItemTriggerParty party) => party switch
        {
            ItemTriggerParty.Target => target,
            ItemTriggerParty.Attacker => parties?.Attacker,
            ItemTriggerParty.Killer => parties?.Killer,
            _ => null,
        };
        return [.. steps.Select(step => Of(step.Parent) is { } parent ? new SessionItemEvent(step.Event, parent, Of(step.Other), step.Melee) : null)
            .OfType<SessionItemEvent>()];
    }

    private static string LifeEntity(SessionLifeTarget target) => target.IsHero ? SessionEventKinds.PlayerEntity : target.Entity!;

    /// <summary>Whether a damage step's item event comes before the step's critical drops and its wear (IC3): the stun, the knockdown's
    /// two walks and a going unconscious the knockout's fatigue caused; the fatigue write's, the dying and the hooks come after.</summary>
    private static bool BeforeItemSteps(ItemTriggerStep step, LifeOutcome outcome) =>
        step.Event is ItemTriggerEvent.ParentStunned or ItemTriggerEvent.TargetGoingUnconscious
        || step.Event == ItemTriggerEvent.ParentGoingUnconscious && (step.Other != ItemTriggerParty.None || outcome.Events.HasFlag(LifeEvents.KnockedOut));

    /// <summary>The worn items of <paramref name="side"/> (the victim by default) without those this attack's critical drops or its wear's breaks took off (IC3).</summary>
    private static string[] StillWorn(IReadOnlyList<string> worn, SessionAttackWearResult? wear, IReadOnlyList<SessionCriticalStep> criticals,
        DamageRecipient side = DamageRecipient.Target)
    {
        var gone = criticals.Where(step => step.Whose == side && step.Outcome == SessionCriticalStepOutcome.Dropped).Select(step => step.Entity)
            .Concat(wear?.Steps.Where(step => step.Wearer == side && (step.Item is { Break: not ItemBreakStep.None }
                || step is { Kind: SessionWearStepKind.WeaponDestroy, Skip: SessionWearSkip.None })).Select(step => step.Entity) ?? [])
            .ToHashSet();
        return [.. worn.Where(item => !gone.Contains(item))];
    }
}
