using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Magic;

/// <summary>The combat and life events whose wrappers walk a critter's worn items (ARC-MAGIC-023, ledger IC rows): the parent is hit
/// (0x4CBAD0), stunned (0x4CBB80), going unconscious (0x4CBBF0), dying (0x4CBC60), attacks an opponent (0x4CBD40), damages an
/// opponent (0x4CBE70: also the target hit and the weapon's own mask), and its target is going unconscious (0x4CBE00); since
/// ARC-MAGIC-025 also attacks a location (0x4CBDB0), whose one call names an item instead of walking (IC10).</summary>
public enum ItemTriggerEvent
{
    ParentHit,
    ParentStunned,
    ParentGoingUnconscious,
    ParentDying,
    ParentAttacksOpponent,
    ParentDamagesOpponent,
    TargetGoingUnconscious,
    ParentAttacksLocation,
}

/// <summary>Who an event's wrapper takes as the parent (whose worn items fire) or the other object, in the damage step's terms: its
/// attacker (context +0x08), its target, the damage source the kill credits (context +0x30: the attacker, or a spell run's other
/// object), or nobody.</summary>
public enum ItemTriggerParty { None, Attacker, Target, Killer }

/// <summary>One wrapper call of a damage step: the event, its parent and other object, and for <see cref="ItemTriggerEvent.ParentHit"/>
/// whether it is a melee hit (the mask's <c>Target_Attacker_Weapon_Melee</c> bit).</summary>
public readonly record struct ItemTriggerStep(ItemTriggerEvent Event, ItemTriggerParty Parent, ItemTriggerParty Other, bool Melee = false);

/// <summary>One executor call of a wrapper's walk over the parent's worn items: the item (its index in the slot-ordered list the caller
/// passed) and the event mask; ARC-MAGIC-025: whether the call runs while the wrapper's parent sits in the attacker global 0x5FF618,
/// which the executor copies into each run's other object (+0xA0) and clears at its exit (IC6, IC10).</summary>
public readonly record struct ItemTriggerCall(int Item, uint Mask, bool RunOtherIsParent = false);

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
    /// ARC-MAGIC-024: the wrapper call of a life step outside a damage step (ledger IC4): the fatigue setter (0x45D5A0) fires the
    /// critter's going unconscious (0x4CBBF0) with no other object when the step takes a conscious critter to 0 fatigue. The check
    /// reads the fatigue only, so a step that also kills fires it too. Its callers are the spell payment (0x450420), the step move
    /// (0x430FC0), the recovery timers, healing and scripts; a damage step's own call is <see cref="DamageStep"/>'s.
    /// </summary>
    [OriginalFunction(0x45D5A0, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemTriggerStep> LifeStep(LifeFacts facts, LifeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(outcome);
        return !LifeRules.IsUnconscious(outcome.Before, facts) && LifeRules.IsUnconscious(outcome.After, facts)
            ? [new(ItemTriggerEvent.ParentGoingUnconscious, ItemTriggerParty.Target, ItemTriggerParty.None)]
            : [];
    }

    /// <summary>
    /// ARC-MAGIC-024: the wrapper calls of a spell's damage step (0x4518D0 → 0x4B4390, ledger DH1, IC8): <see cref="DamageStep"/> with
    /// the run's parent as the attacker (<paramref name="parent"/>: whether the run has one) and the damage source the kill credits,
    /// the run's other object or else its parent (<paramref name="killer"/>); a spell's context has no attack entry flag, so the parent
    /// hit carries no melee bit and the damages opponent names no weapon. MG46 (IC9) is the walk's (<see cref="ChainSkips"/>): an item
    /// run's damage keeps every call here.
    /// </summary>
    public static IReadOnlyList<ItemTriggerStep> SpellDamageStep(LifeFacts facts, LifeDamage damage, LifeOutcome outcome, bool parent, bool killer) =>
        DamageStep(facts, damage, outcome, parent, killer, melee: false);

    /// <summary>
    /// ARC-MAGIC-025, MG46 (IC9; owner decision 2026-10-28, the narrow fix): whether a walk skips <paramref name="item"/> because it
    /// fired already in the event's chain (<paramref name="chain"/>: the items whose runs led to this event's damage step, the run's
    /// own source among them; none for an event no item run caused). The original skips nothing
    /// (<see cref="MagicAnomalyProfile.ItemRunDamageFiresHooks"/>), so an item fires on its own run's damage again and two answering
    /// items ping-pong; Steamwoven lets each item fire at most once in a chain, keeping every other item's answer.
    /// </summary>
    public static bool ChainSkips(MagicAnomalyProfile profile, string item, IReadOnlyCollection<string>? chain)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(item);
        return !profile.ItemRunDamageFiresHooks && chain is not null && chain.Contains(item);
    }

    /// <summary>
    /// ARC-MAGIC-025: the attack animation's begin (0x432700, ledger IC6): after its checks (attacker and target set, an attacker not
    /// stoned, a critter attacker neither stunned nor paralysed) and its action point charge (CT2: a refusal ends the goal first), the
    /// host's, it fires the attacker's attacks an opponent (0x4CBD40) with the target as the other object, on every swing. A throw
    /// (goal 37, 0x42AB90 → 0x4B3C00) never reaches it (IC14), so it fires none.
    /// </summary>
    [OriginalFunction(0x432700, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemTriggerStep> AttackStart() =>
        [new(ItemTriggerEvent.ParentAttacksOpponent, ItemTriggerParty.Attacker, ItemTriggerParty.Target)];

    /// <summary>
    /// ARC-MAGIC-025: attacks a location (0x4CBDB0, ledger IC10, IC11): no type test of the parent and no walk; for a non-null item one
    /// executor call (0x4CB830) with the item, the parent, no other object, the tile and <see cref="ParentAttacksLocation"/>, the
    /// parent in the attacker global (the run's other object). The executor's run takes its parent from the item (0x450A50: its holder
    /// in an inventory, else the item itself), so a thrown item's run has the item as its parent. Null without an item.
    /// </summary>
    [OriginalFunction(0x4CBDB0, OriginalFunctionScope.Partial)]
    public static ItemTriggerCall? LocationCall(bool item) => item ? new(0, ParentAttacksLocation, RunOtherIsParent: true) : null;

    /// <summary>
    /// ARC-MAGIC-025: the item branch of a wrapper whose parent is an item (types 5..14), which only damages an opponent has (0x4CBE70,
    /// ledger IC1, IC13): one executor call with the item itself as the item and the other object and the event's other object (the
    /// victim) as the parent, with <see cref="TargetHit"/>. A spell run whose parent is an item (a thrown item's, 0x450A50) damages with
    /// it as the attacker. Every other wrapper fires nothing for an item. Such a run's source is that item, so in Steamwoven its own
    /// target hit is in the run's chain and never fires (MG46, <see cref="ChainSkips"/>): the item fired already for the event that
    /// started the run. Latent: no installed record has both <c>Parent_Atks_Location</c> and <c>Target_Hit</c>.
    /// </summary>
    public static IReadOnlyList<ItemTriggerCall> ItemParentCalls(ItemTriggerEvent kind) =>
        kind == ItemTriggerEvent.ParentDamagesOpponent ? [new(0, TargetHit)] : [];

    /// <summary>
    /// ARC-MAGIC-024: the dying animation's end (0x42FF40, ledger IC5): for a dead critter (unless the goal's flag 0x200, the host's)
    /// it fires the critter's dying (0x4CBC60) with no other object, after the object flags and, for the local PC, the end of the
    /// game (the host's). A kill with a killer has already fired dying with the killer (<see cref="DamageStep"/>), so such a
    /// critter fires it twice (Original, latent: no installed record uses <c>Parent_Dying</c>).
    /// </summary>
    [OriginalFunction(0x42FF40, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemTriggerStep> DyingEnd(LifeFacts facts, LifeState state)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(state);
        return LifeRules.IsDead(state, facts) ? [new(ItemTriggerEvent.ParentDying, ItemTriggerParty.Target, ItemTriggerParty.None)] : [];
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
    /// time with damages opponent with the item. The executor (<see cref="Fire"/>) then reads each item's slots. ARC-MAGIC-025: attacks an
    /// opponent (0x4CBD40) puts its parent in the attacker global, which the first executor call copies into its runs and clears, so
    /// only the first worn item's call carries it (<see cref="ItemTriggerCall.RunOtherIsParent"/>). Attacks a location walks nothing
    /// (<see cref="LocationCall"/>).
    /// </summary>
    [OriginalFunction(0x4CBE70, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemTriggerCall> WornCalls(ItemTriggerEvent kind, int wornItems, bool melee = false, int? weapon = null)
    {
        if (wornItems is < 0 or > WornSlots) throw new ArgumentOutOfRangeException(nameof(wornItems));
        var calls = new List<ItemTriggerCall>();
        for (var item = 0; item < wornItems; item++)
        {
            if (kind == ItemTriggerEvent.ParentAttacksLocation) break;
            if (kind != ItemTriggerEvent.ParentDamagesOpponent)
            {
                calls.Add(new(item, Mask(kind, melee), kind == ItemTriggerEvent.ParentAttacksOpponent && item == 0));
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
    /// an opponent 0x10000; target going unconscious 0x80000; attacks a location 0x800000.</summary>
    public static uint Mask(ItemTriggerEvent kind, bool melee = false) => kind switch
    {
        ItemTriggerEvent.ParentHit => melee ? ParentHit | WeaponMelee : ParentHit,
        ItemTriggerEvent.ParentStunned => ParentStunned,
        ItemTriggerEvent.ParentGoingUnconscious => ParentGoingUnconscious,
        ItemTriggerEvent.ParentDying => ParentDying,
        ItemTriggerEvent.ParentAttacksOpponent => ParentAttacksOpponent,
        ItemTriggerEvent.ParentDamagesOpponent => ParentDamagesOpponent,
        ItemTriggerEvent.TargetGoingUnconscious => TargetGoingUnconscious,
        ItemTriggerEvent.ParentAttacksLocation => ParentAttacksLocation,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
