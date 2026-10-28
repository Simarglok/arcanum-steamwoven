namespace Arcanum.Simulation.Magic;

/// <summary>Where an item spell's run is aimed (0x4CB830, by the record's <c>ItemTriggers:</c> bits).</summary>
public enum ItemSpellAim
{
    /// <summary>The item's parent (the holder): no aim bit and no <c>Parent_Atks_Location</c>.</summary>
    Parent,
    /// <summary>The event's other object (<c>Target_Attacker</c>, so <c>Item_Used</c> too).</summary>
    Other,
    /// <summary>The item the other object wears in its weapon slot (1004; <c>Target_Attacker_Weapon</c>).</summary>
    OtherWeapon,
    /// <summary>The item the other object wears in its armour slot (1006; <c>Target_Attacker_Armor</c>).</summary>
    OtherArmor,
    /// <summary>The event's location (<c>Parent_Atks_Location</c> without an aim bit).</summary>
    Location,
    /// <summary>No target: <c>Target_Attacker_Weapon_Melee</c> is the only aim bit, so the executor sets none and starts no run.</summary>
    None,
}

/// <summary>One item spell an event fires: the slot, the run's spell (the slot's low byte), its aim and whether the run plays the
/// casting goal (<see cref="ItemSpellTriggerRules.Withdraw"/>'s recast) or none (the executor's flag 0x1: goal 73).</summary>
public readonly record struct ItemSpellFiring(int Slot, int Spell, ItemSpellAim Aim, bool CastGoal = false);

/// <summary>
/// ARC-MAGIC-020 (FR-MAGIC-011): the item trigger executors as the 1.0.7.4 executable has them (ledger "Item spell triggers",
/// IT rows; contract docs/world/item-spell-triggers.md): which of an item's five spells an event fires (0x4CB830), with its chance
/// flag and aim, and which an unwear or a drop withdraws and casts again (0x4CBFF0). The events' masks are the wrappers'. Pure
/// functions; the host builds the runs (the item as source, its holder as parent) and stops the withdrawn ones.
/// </summary>
public static partial class ItemSpellTriggerRules
{
    /// <summary>The five item spell fields (0x64..0x68), 10000 for none; a run's spell is the slot's low byte (0x4CB790, LM7).</summary>
    public const int Slots = 5, NoSpell = 10000, SpellMask = 0xFF;

    /// <summary>The worn slots an attacker's weapon and armour aims read (0x4649C0).</summary>
    public const int WeaponSlot = 1004, ArmorSlot = 1006;

    /// <summary>The event masks the wrappers pass (<c>ItemTriggers:</c> values): wear (0x4CB7D0), unwear (0x4CBFC0), pickup
    /// (0x4CBAA0), drop (0x4CC130), parent hit (0x4CBAD0, with <see cref="WeaponMelee"/> for a melee hit), parent stunned (0x4CBB80),
    /// parent going unconscious (0x4CBBF0), parent dying (0x4CBC60), parent hit by an attack spell (0x4CBCD0), parent attacks an
    /// opponent (0x4CBD40) or a location (0x4CBDB0), parent damages an opponent (0x4CBE70, with or without the item), target hit,
    /// target going unconscious (0x4CBE00) and item used (0x4CBF70: one bit of <c>Item_Used</c>'s two).</summary>
    public const uint Wear = 0x20000000, Unwear = 0x40000000, Pickup = 0x80000000, Drop = 0x1000000, ParentHit = 0x2000000,
        ParentStunned = 0x4000000, ParentGoingUnconscious = 0x8000000, ParentDying = 0x100000, ParentHitByAttackSpell = 0x200000,
        ParentAttacksOpponent = 0x400000, ParentAttacksLocation = 0x800000, ParentDamagesOpponent = 0x10000, ParentDamagesOpponentWithItem = 0x20000,
        TargetHit = 0x40000, TargetGoingUnconscious = 0x80000, ItemUsed = 0x1000;

    /// <summary>The aim bits (<c>Target_Attacker</c>, <c>_Weapon</c>, <c>_Armor</c>, <c>_Weapon_Melee</c>) and the chance bits
    /// (<c>Random_Chance_Rare</c>, <c>_Uncommon</c>, <c>_Common</c>, <c>_Frequent</c>).</summary>
    public const uint Other = 0x2000, OtherWeapon = 0x4000, OtherArmor = 0x8000, WeaponMelee = 0x100, AimBits = Other | OtherWeapon | OtherArmor | WeaponMelee,
        Rare = 0x200, Uncommon = 0x400, Common = 0x800, Frequent = 0x10, ChanceBits = Rare | Uncommon | Common | Frequent;

    /// <summary>
    /// The executor (0x4CB830, through the wrapper 0x4CB800) for one item and event: the slots in order, leaving at the first that
    /// holds 10000 (so an empty slot 0 fires nothing); a slot whose record's trigger mask (<paramref name="recordTriggers"/> of the
    /// slot's whole value, record +0x38) shares no bit with <paramref name="eventMask"/> is skipped; a mask with a chance bit rolls
    /// d100 (<see cref="SpellRollPurpose.ItemTriggerChance"/>) and fires at or below <see cref="Chance"/>; then its aim
    /// (<see cref="Aim"/>). A firing aimed at <see cref="ItemSpellAim.None"/> is listed, but starts no run (no target is set).
    /// The network client's gate (0x62AC6C) is not modelled: a single-player game always passes it.
    /// </summary>
    [OriginalFunction(0x4CB830, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemSpellFiring> Fire(IReadOnlyList<int> slots, Func<int, uint> recordTriggers, uint eventMask, ISpellRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(recordTriggers);
        ArgumentNullException.ThrowIfNull(rolls);
        var firings = new List<ItemSpellFiring>();
        foreach (var (slot, value, triggers) in Matching(slots, recordTriggers, eventMask))
        {
            if (Chance(triggers) is { } chance && rolls.Roll(SpellRollPurpose.ItemTriggerChance, 1, 100) > chance) continue;
            firings.Add(new(slot, value & SpellMask, Aim(triggers)));
        }
        return firings;
    }

    /// <summary>
    /// The unwear and drop executor (0x4CBFF0; wrappers 0x4CBFC0 and 0x4CC130) for one item and event: the same slot walk and mask
    /// test as <see cref="Fire"/>, with no chance roll; for each match the host stops the first run of the item's parent, aimed at
    /// the parent, of the slot's spell (0x4573D0), then casts the spell again at the parent with the event's mask and the casting goal
    /// (the invocation's flag 0x1 is not set here).
    /// </summary>
    [OriginalFunction(0x4CBFF0, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemSpellFiring> Withdraw(IReadOnlyList<int> slots, Func<int, uint> recordTriggers, uint eventMask)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(recordTriggers);
        return Matching(slots, recordTriggers, eventMask).Select(m => new ItemSpellFiring(m.Slot, m.Value & SpellMask, ItemSpellAim.Parent, true)).ToArray();
    }

    /// <summary>The chance flag's percentage (0x4CB830): the first of Rare 10, Uncommon 20, Common 40 and Frequent 66 the mask
    /// holds, or null for a mask without one (it always fires).</summary>
    public static int? Chance(uint triggers) =>
        (triggers & Rare) != 0 ? 10 : (triggers & Uncommon) != 0 ? 20 : (triggers & Common) != 0 ? 40 : (triggers & Frequent) != 0 ? 66 : null;

    /// <summary>The run's aim (0x4CB830): with an aim bit, the other object, its weapon or its armour in that order, and none for
    /// the melee bit alone; without one, the location for <c>Parent_Atks_Location</c>, else the parent.</summary>
    public static ItemSpellAim Aim(uint triggers) =>
        (triggers & AimBits) != 0
            ? (triggers & Other) != 0 ? ItemSpellAim.Other : (triggers & OtherWeapon) != 0 ? ItemSpellAim.OtherWeapon
              : (triggers & OtherArmor) != 0 ? ItemSpellAim.OtherArmor : ItemSpellAim.None
            : (triggers & ParentAttacksLocation) != 0 ? ItemSpellAim.Location : ItemSpellAim.Parent;

    private static IEnumerable<(int Slot, int Value, uint Triggers)> Matching(IReadOnlyList<int> slots, Func<int, uint> recordTriggers, uint eventMask)
    {
        for (var slot = 0; slot < Math.Min(Slots, slots.Count); slot++)
        {
            var value = slots[slot];
            if (value == NoSpell) yield break;
            var triggers = recordTriggers(value);
            if ((triggers & eventMask) != 0) yield return (slot, value, triggers);
        }
    }
}
