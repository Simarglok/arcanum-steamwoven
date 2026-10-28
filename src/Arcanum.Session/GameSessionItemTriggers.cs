using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>One executor call of a combat or life event through the trigger host (ARC-MAGIC-023): the event, its mask, the worn item,
/// its holder (the event's parent), the event's other object and what the host did (<see cref="ISessionItemTriggers.Fire"/>).</summary>
public sealed record SessionItemTriggerFiring(ItemTriggerEvent Event, uint Mask, string Item, string Holder, string? Other, SessionItemTriggerResult Result);

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
        var firings = new List<SessionItemTriggerFiring>();
        foreach (var step in steps)
        {
            var (holder, worn) = step.Parent == ItemTriggerParty.Target ? (target, targetWorn) : (hero, heroWorn);
            var other = step.Other switch { ItemTriggerParty.Target => target, ItemTriggerParty.None => null, _ => hero };
            var used = weapon is null ? -1 : worn.ToList().IndexOf(weapon);
            foreach (var call in ItemSpellTriggerRules.WornCalls(step.Event, worn.Count, step.Melee, used < 0 ? null : used))
            {
                var item = worn[call.Item];
                firings.Add(new(step.Event, call.Mask, item, holder, other, triggers.Fire(call.Mask, item, holder, other)));
            }
        }
        return firings;
    }

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
