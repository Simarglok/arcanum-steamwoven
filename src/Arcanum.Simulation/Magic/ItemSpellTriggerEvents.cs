using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Magic;

/// <summary>The combat and life events whose wrappers walk a critter's worn items (ARC-MAGIC-023, ledger IC rows): the parent is hit
/// (0x4CBAD0), stunned (0x4CBB80), going unconscious (0x4CBBF0), dying (0x4CBC60), attacks an opponent (0x4CBD40), damages an
/// opponent (0x4CBE70: also the target hit and the weapon's own mask), and its target is going unconscious (0x4CBE00).</summary>
public enum ItemTriggerEvent
{
    ParentHit,
    ParentStunned,
    ParentGoingUnconscious,
    ParentDying,
    ParentAttacksOpponent,
    ParentDamagesOpponent,
    TargetGoingUnconscious,
}

/// <summary>Who an event's wrapper takes as the parent (whose worn items fire) or the other object, in the damage step's terms: its
/// attacker (context +0x08), its target, the damage source the kill credits (context +0x30: the attacker, or a spell run's other
/// object), or nobody.</summary>
public enum ItemTriggerParty { None, Attacker, Target, Killer }

/// <summary>One wrapper call of a damage step: the event, its parent and other object, and for <see cref="ItemTriggerEvent.ParentHit"/>
/// whether it is a melee hit (the mask's <c>Target_Attacker_Weapon_Melee</c> bit).</summary>
public readonly record struct ItemTriggerStep(ItemTriggerEvent Event, ItemTriggerParty Parent, ItemTriggerParty Other, bool Melee = false);

/// <summary>One executor call of a wrapper's walk over the parent's worn items: the item (its index in the slot-ordered list the caller
/// passed) and the event mask.</summary>
public readonly record struct ItemTriggerCall(int Item, uint Mask);

public static partial class ItemSpellTriggerRules
{
    /// <summary>
    /// The wrapper calls of one damage step (0x4B4390) on a critter, in its order (ledger IC2): nothing when the step exits before
    /// its writes (an invulnerable or already dead critter). A stun effect on a critter not yet stunned and without a knockdown or
    /// knockout fires the target's stunned (with the attacker), before the stun's own start, so also when that start fails; a
    /// knockdown or knockout fires the target's going unconscious and the attacker's target going unconscious (both with the
    /// other party); the fatigue setter (0x45D5A0) fires the target's going unconscious without an other object when the step takes
    /// a conscious critter to 0 fatigue (also when it dies in the same step: the setter reads the fatigue only); a death fires the
    /// target's dying with the kill's damage source (0x45DA20; none without one). Then, with an attacker, the target's parent hit
    /// and the attacker's damages opponent.
    /// </summary>
    public static IReadOnlyList<ItemTriggerStep> DamageStep(LifeFacts facts, LifeDamage damage, LifeOutcome outcome, bool attacker, bool killer, bool melee)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(outcome);
        if (facts.Traits.HasFlag(LifeTraits.Invulnerable) || outcome.Events.HasFlag(LifeEvents.AlreadyDead)) return [];
        var steps = new List<ItemTriggerStep>();
        var other = attacker ? ItemTriggerParty.Attacker : ItemTriggerParty.None;
        const CriticalHitEffects Down = CriticalHitEffects.Knockdown | CriticalHitEffects.Knockout;
        if (damage.Effects.HasFlag(CriticalHitEffects.Stun) && !outcome.Before.Conditions.HasFlag(LifeConditions.Stunned) && (damage.Effects & Down) == 0)
            steps.Add(new(ItemTriggerEvent.ParentStunned, ItemTriggerParty.Target, other));
        if ((damage.Effects & Down) != 0)
        {
            steps.Add(new(ItemTriggerEvent.ParentGoingUnconscious, ItemTriggerParty.Target, other));
            // 0x4CBE00 walks the attacker's items with the target as the other object; a null attacker fires nothing.
            if (attacker) steps.Add(new(ItemTriggerEvent.TargetGoingUnconscious, ItemTriggerParty.Attacker, ItemTriggerParty.Target));
        }
        if (!LifeRules.IsUnconscious(outcome.Before, facts) && LifeRules.IsUnconscious(outcome.After, facts))
            steps.Add(new(ItemTriggerEvent.ParentGoingUnconscious, ItemTriggerParty.Target, ItemTriggerParty.None));
        if (outcome.Events.HasFlag(LifeEvents.Died) && killer)
            steps.Add(new(ItemTriggerEvent.ParentDying, ItemTriggerParty.Target, ItemTriggerParty.Killer));
        if (attacker)
        {
            steps.Add(new(ItemTriggerEvent.ParentHit, ItemTriggerParty.Target, ItemTriggerParty.Attacker, melee));
            steps.Add(new(ItemTriggerEvent.ParentDamagesOpponent, ItemTriggerParty.Attacker, ItemTriggerParty.Target));
        }
        return steps;
    }

    /// <summary>
    /// Whether a damage step's parent hit carries the melee bit (0x4CBAD0): only a context of an attack entry (flag 0x40000: the
    /// ordinary attack, the throw and the projectile's strike; never a spell's damage), and then for an attacker without a weapon or
    /// with one whose range (0x465E30) is at most 1.
    /// </summary>
    [OriginalFunction(0x4CBAD0, OriginalFunctionScope.Partial)]
    public static bool MeleeHit(bool attackEntry, int? weaponRange) => attackEntry && (weaponRange is not { } range || range <= 1);

    /// <summary>
    /// A wrapper's walk (ledger IC1) over its parent's worn items, given in slot order (1000..1008), for a parent that is a PC or an
    /// NPC (any other parent fires nothing): each item once with the event's mask; for <see cref="ItemTriggerEvent.ParentDamagesOpponent"/>
    /// each item twice, damages opponent then target hit, and the item that is the attack's weapon (<paramref name="weapon"/>) a third
    /// time with damages opponent with the item. The executor (<see cref="Fire"/>) then reads each item's slots.
    /// </summary>
    [OriginalFunction(0x4CBE70, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemTriggerCall> WornCalls(ItemTriggerEvent kind, int wornItems, bool melee = false, int? weapon = null)
    {
        if (wornItems is < 0 or > WornSlots) throw new ArgumentOutOfRangeException(nameof(wornItems));
        var calls = new List<ItemTriggerCall>();
        for (var item = 0; item < wornItems; item++)
        {
            if (kind != ItemTriggerEvent.ParentDamagesOpponent)
            {
                calls.Add(new(item, Mask(kind, melee)));
                continue;
            }
            calls.Add(new(item, ParentDamagesOpponent));
            calls.Add(new(item, TargetHit));
            if (item == weapon) calls.Add(new(item, ParentDamagesOpponentWithItem));
        }
        return calls;
    }

    /// <summary>The worn slots a wrapper walks (1000..1008).</summary>
    public const int WornSlots = 9;

    /// <summary>The mask a wrapper passes for an event (for damages opponent its first one): parent hit 0x2000000, with the melee bit
    /// 0x100 for a melee hit; stunned 0x4000000; going unconscious 0x8000000; dying 0x100000; attacks an opponent 0x400000; damages
    /// an opponent 0x10000; target going unconscious 0x80000.</summary>
    public static uint Mask(ItemTriggerEvent kind, bool melee = false) => kind switch
    {
        ItemTriggerEvent.ParentHit => melee ? ParentHit | WeaponMelee : ParentHit,
        ItemTriggerEvent.ParentStunned => ParentStunned,
        ItemTriggerEvent.ParentGoingUnconscious => ParentGoingUnconscious,
        ItemTriggerEvent.ParentDying => ParentDying,
        ItemTriggerEvent.ParentAttacksOpponent => ParentAttacksOpponent,
        ItemTriggerEvent.ParentDamagesOpponent => ParentDamagesOpponent,
        ItemTriggerEvent.TargetGoingUnconscious => TargetGoingUnconscious,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
