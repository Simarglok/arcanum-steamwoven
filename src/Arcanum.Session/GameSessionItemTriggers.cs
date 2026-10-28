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
public sealed record SessionItemEvent(ItemTriggerEvent Event, string Parent, string? Other, bool Melee = false);

/// <summary>ARC-MAGIC-024: the parties of a damage step for its item events (DH1): the attacker (the context's +0x08, a spell run's
/// parent) and the damage source the kill credits (+0x30: the run's other object, else its parent), each an entity id or none, and
/// whether an item event started the run (MG46).</summary>
public sealed record SessionDamageParties(string? Attacker, string? Killer, bool ItemRun = false)
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
    /// ARC-MAGIC-023: fires damage step item events of the hero's attack on an NPC (0x4B4390, ledger IC2;
    /// <see cref="ItemSpellTriggerRules.DamageStep"/>) through <paramref name="triggers"/>, in order. Each wrapper walks its parent's
    /// worn items in slot order (<see cref="ItemSpellTriggerRules.WornCalls"/>): the NPC's are <paramref name="targetWorn"/> (as the
    /// caller has them at this point of the step; none without the host's item data), the hero's <paramref name="heroWorn"/> (before
    /// the attack's weapon wear). Nothing without a host.
    /// </summary>
    private IReadOnlyList<SessionItemTriggerFiring> FireDamageStepItems(ISessionItemTriggers? triggers, IEnumerable<ItemTriggerStep> steps,
        string target, IReadOnlyList<string> targetWorn, IReadOnlyList<string> heroWorn, string? weapon)
    {
        if (triggers is null) return [];
        var hero = SessionEventKinds.PlayerEntity;
        var events = steps.Select(step => new SessionItemEvent(step.Event, step.Parent == ItemTriggerParty.Target ? target : hero,
            step.Other switch { ItemTriggerParty.Target => target, ItemTriggerParty.None => null, _ => hero }, step.Melee));
        return WalkItemEvents(triggers, events, holder => holder == target ? targetWorn : heroWorn, weapon);
    }

    /// <summary>
    /// ARC-MAGIC-024: fires <paramref name="events"/> (a life step's <see cref="SessionLifeResult.ItemEvents"/>) through
    /// <paramref name="triggers"/>, in order: each wrapper walks its parent's worn items in slot order
    /// (<see cref="ItemSpellTriggerRules.WornCalls"/>; no weapon, so no damages opponent with the item): the hero's as the session
    /// holds them now, an NPC's from <paramref name="npcWorn"/> (null for an entity that is no NPC the host knows: any other parent
    /// fires nothing, IC1). Refused, firing nothing, from inside the run rules (the hazard of a spell's damage step: its events fire
    /// after the run's pass, <see cref="SessionSpellCaster.FireItemEvents"/>) and during an event pass.
    /// </summary>
    public SessionItemEventResult FireItemEvents(ISessionItemTriggers triggers, IEnumerable<SessionItemEvent> events, Func<string, IReadOnlyList<string>?> npcWorn)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(npcWorn);
        if (_spellRunsBusy) return new(GameSessionFailure.InvalidSpellRun, []);
        if (_passWorld is not null) return new(GameSessionFailure.InvalidEventPass, []);
        var list = events.ToArray();
        if (list.Any(e => e is null || !SessionValidation.Entity(e.Parent) || e.Other is { } other && !SessionValidation.Entity(other)))
            throw new ArgumentException("Invalid item event.", nameof(events));
        return new(GameSessionFailure.None, WalkItemEvents(triggers, list, holder => holder == SessionEventKinds.PlayerEntity
            ? [.. _equipment.Worn.Values.OrderBy(item => item.Slot).Select(item => item.Entity)] : npcWorn(holder), null));
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

    // The walk of each event over its parent's worn items (IC1); a parent without worn items the caller knows fires nothing.
    private static List<SessionItemTriggerFiring> WalkItemEvents(ISessionItemTriggers triggers, IEnumerable<SessionItemEvent> events,
        Func<string, IReadOnlyList<string>?> wornOf, string? weapon)
    {
        var firings = new List<SessionItemTriggerFiring>();
        foreach (var e in events)
        {
            if (wornOf(e.Parent) is not { } worn) continue;
            var used = weapon is null ? -1 : worn.ToList().IndexOf(weapon);
            foreach (var call in ItemSpellTriggerRules.WornCalls(e.Event, Math.Min(worn.Count, ItemSpellTriggerRules.WornSlots), e.Melee, used < 0 ? null : used))
            {
                var item = worn[call.Item];
                firings.Add(new(e.Event, call.Mask, item, e.Parent, e.Other, triggers.Fire(call.Mask, item, e.Parent, e.Other)));
            }
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
        return Resolved(ItemSpellTriggerRules.SpellDamageStep(facts, step.Damage, outcome, parties.Attacker is not null, parties.Killer is not null,
            parties.ItemRun, _rules.Magic), LifeEntity(target), parties);
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

    /// <summary>The NPC's worn items without those this attack's critical drops or its wear's breaks took off (IC3).</summary>
    private static string[] StillWorn(IReadOnlyList<string> worn, SessionAttackWearResult? wear, IReadOnlyList<SessionCriticalStep> criticals)
    {
        var gone = criticals.Where(step => step is { Whose: DamageRecipient.Target, Outcome: SessionCriticalStepOutcome.Dropped }).Select(step => step.Entity)
            .Concat(wear?.Steps.Where(step => step.Wearer == DamageRecipient.Target && (step.Item is { Break: not ItemBreakStep.None }
                || step is { Kind: SessionWearStepKind.WeaponDestroy, Skip: SessionWearSkip.None })).Select(step => step.Entity) ?? [])
            .ToHashSet();
        return [.. worn.Where(item => !gone.Contains(item))];
    }
}
