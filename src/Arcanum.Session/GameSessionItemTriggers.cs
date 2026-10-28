using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>One executor call of a combat or life event through the trigger host (ARC-MAGIC-023): the event, its mask, the worn item,
/// its holder (the event's parent), the event's other object and what the host did (<see cref="ISessionItemTriggers.Fire"/>).</summary>
public sealed record SessionItemTriggerFiring(ItemTriggerEvent Event, uint Mask, string Item, string Holder, string? Other, SessionItemTriggerResult Result);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-023: the item events of the hero's damage step on an NPC (0x4B4390, ledger IC2), fired through
    /// <paramref name="triggers"/> after the attack's steps, in the step's order (<see cref="ItemSpellTriggerRules.DamageStep"/>). Each
    /// wrapper walks its parent's worn items in slot order (<see cref="ItemSpellTriggerRules.WornCalls"/>) as they are at its point of
    /// the step: the NPC's items as the host passed them (<see cref="SessionAttack.TargetWorn"/>; none without them) for the stun and
    /// the knockdown, and, for the fatigue's going unconscious (unless a knockout caused it), the dying and the parent hit, without
    /// those this attack's critical drops or breaks took off; the hero's items as it wore them before the attack's own weapon wear.
    /// Without a host nothing fires.
    /// </summary>
    private IReadOnlyList<SessionItemTriggerFiring> FireDamageStepItems(ISessionItemTriggers? triggers, LifeFacts facts, LifeDamage damage,
        LifeOutcome outcome, string target, IReadOnlyList<string> heroWorn, string? weapon, bool melee,
        IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? targetWorn, SessionAttackWearResult? wear, IReadOnlyList<SessionCriticalStep> criticals)
    {
        if (triggers is null) return [];
        var hero = SessionEventKinds.PlayerEntity;
        var before = targetWorn?.OrderBy(pair => pair.Key).Select(pair => pair.Value.Entity).ToArray() ?? [];
        var gone = criticals.Where(step => step is { Whose: DamageRecipient.Target, Outcome: SessionCriticalStepOutcome.Dropped }).Select(step => step.Entity)
            .Concat(wear?.Steps.Where(step => step.Wearer == DamageRecipient.Target && step.Item is { Break: not ItemBreakStep.None }).Select(step => step.Entity) ?? [])
            .ToHashSet();
        var after = before.Where(item => !gone.Contains(item)).ToArray();
        var firings = new List<SessionItemTriggerFiring>();
        foreach (var step in ItemSpellTriggerRules.DamageStep(facts, damage, outcome, attacker: true, killer: true, melee))
        {
            var (holder, worn) = step.Parent == ItemTriggerParty.Target
                ? (target, step.Event is ItemTriggerEvent.ParentStunned || step.Other != ItemTriggerParty.None && step.Event == ItemTriggerEvent.ParentGoingUnconscious
                    || step.Other == ItemTriggerParty.None && outcome.Events.HasFlag(LifeEvents.KnockedOut) ? before : after)
                : (hero, heroWorn);
            var other = step.Other switch { ItemTriggerParty.Target => target, ItemTriggerParty.None => null, _ => hero };
            var used = weapon is null ? -1 : Array.IndexOf(worn.ToArray(), weapon);
            foreach (var call in ItemSpellTriggerRules.WornCalls(step.Event, worn.Count, step.Melee, used < 0 ? null : used))
            {
                var item = worn[call.Item];
                firings.Add(new(step.Event, call.Mask, item, holder, other, triggers.Fire(call.Mask, item, holder, other)));
            }
        }
        return firings;
    }
}
