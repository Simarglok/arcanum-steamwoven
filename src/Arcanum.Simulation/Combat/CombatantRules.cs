using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// The critter flags an attack reads that neither the life state nor its traits own (the source's critter flags 1 and 2):
/// asleep, fatigue-limiting, fatigue-draining, dark sight and the magnetic-inversion spell. Values are Steamwoven identifiers.
/// </summary>
[Flags]
public enum CombatantFlags
{
    None = 0,
    Sleeping = 1 << 0,
    FatigueLimiting = 1 << 1,
    FatigueDraining = 1 << 2,
    DarkSight = 1 << 3,
    MagneticInversion = 1 << 4,
}

/// <summary>
/// The parts of an attack's <see cref="Combatant"/> that come from other owners (ARC-COMBAT-005): the effective resistances
/// (the source's 0x43D6D0) and the combat conditions of a critter's life state, traits and flags.
/// Pure functions; every one validates its inputs (<see cref="ArgumentException"/>).
/// </summary>
public static class CombatantRules
{
    /// <summary>The highest resistance of a critter that is not a monstrous NPC (and of any other object).</summary>
    public const int MaxResistance = 95;
    /// <summary>Bound of the magnitude of a value these rules read.</summary>
    public const int MaxValue = 1_000_000;

    /// <summary>
    /// A critter's effective resistances (0x43D6D0, ledger EQ8, ARC-MAGIC-027 reading RE1): its stored resistance plus its worn armour's adjustments (with their magic, already
    /// scaled for the wearer, <see cref="EquipmentRules.ResistanceAdjustments"/>), plus <c>5 × (CN − 4)</c> for poison when
    /// positive, then the critter's resistance <paramref name="effects"/> (the aggregation 0x4EA6C0 over the sum), then clamped to 0..95 unless the critter is a monstrous NPC
    /// (<paramref name="unclamped"/>). The effects come after the armour and the poison bonus and before the clamp, so an effect that lifts a resistance past 95 is cut for a
    /// PC and an ordinary NPC and a negative one can take it below the stored value down to 0.
    /// </summary>
    [OriginalFunction(0x43D6D0, OriginalFunctionScope.Partial)]
    public static DamageResistances Resistances(DamageResistances stored, EquipmentResistances worn, int constitution, bool unclamped, EffectResistances effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        foreach (var value in new[] { stored.Normal, stored.Poison, stored.Electrical, stored.Fire, worn.Normal, worn.Poison, worn.Electrical,
                     worn.Fire, constitution })
            if (Math.Abs((long)value) > MaxValue) throw new ArgumentOutOfRangeException(nameof(stored));
        var poisonBonus = Math.Max(0, 5 * (constitution - 4));
        int Clamp(long value) => unclamped ? (int)Math.Clamp(value, -AttackResolver.MaxResistance, AttackResolver.MaxResistance)
            : (int)Math.Clamp(value, 0, MaxResistance);
        return new(Clamp(EffectModifiers.Apply((long)stored.Normal + worn.Normal, effects.Normal)),
            Clamp(EffectModifiers.Apply((long)stored.Poison + worn.Poison + poisonBonus, effects.Poison)),
            Clamp(EffectModifiers.Apply((long)stored.Electrical + worn.Electrical, effects.Electrical)),
            Clamp(EffectModifiers.Apply((long)stored.Fire + worn.Fire, effects.Fire)));
    }

    /// <summary>
    /// ARC-UI-1121 (ledger IW42): a critter's displayed magic resistance, the armour rating's fifth term (the combat resistance getter with the magic type, which
    /// <see cref="Resistances(DamageResistances, EquipmentResistances, int, bool, EffectResistances)"/> leaves out): its stored value plus its worn armour's magic resistance
    /// adjustments (<see cref="EquipmentResistances.Magic"/>), then the critter's magic resistance <paramref name="effects"/> over the sum, then clamped as the other types are.
    /// The save reads the stored value alone (<see cref="Arcanum.Simulation.Magic.SpellResistanceRules.ResistanceUnderEffects"/>).
    /// </summary>
    public static int MagicResistance(int stored, int worn, bool unclamped, IReadOnlyList<EffectModifier> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (Math.Abs((long)stored) > MaxValue) throw new ArgumentOutOfRangeException(nameof(stored));
        if (Math.Abs((long)worn) > MaxValue) throw new ArgumentOutOfRangeException(nameof(worn));
        var value = EffectModifiers.Apply((long)stored + worn, effects);
        return unclamped ? Math.Clamp(value, -AttackResolver.MaxResistance, AttackResolver.MaxResistance) : Math.Clamp(value, 0, MaxResistance);
    }

    /// <summary>The effective resistances with no resistance effects (<see cref="EffectResistances.None"/>).</summary>
    public static DamageResistances Resistances(DamageResistances stored, EquipmentResistances worn, int constitution, bool unclamped) =>
        Resistances(stored, worn, constitution, unclamped, EffectResistances.None);

    /// <summary>
    /// A critter's combat conditions: dead and unconscious from its current values (<paramref name="status"/>), stunned,
    /// blinded and crippled from its life state, undead, mechanical, fatigue-immune, invulnerable, nigh-invulnerable, stoned and
    /// paralysed from its traits, and the other flags from <paramref name="flags"/>. Falling down is the host's animation and
    /// is not set here.
    /// </summary>
    public static CombatantConditions Conditions(LifeStatus status, LifeState state, LifeTraits traits, CombatantFlags flags)
    {
        ArgumentNullException.ThrowIfNull(status);
        var result = CombatantConditions.None;
        void Set(bool on, CombatantConditions condition) { if (on) result |= condition; }
        Set(status.IsDead, CombatantConditions.Dead);
        Set(status.IsUnconscious, CombatantConditions.Unconscious);
        var life = state.Conditions;
        Set(life.HasFlag(LifeConditions.Stunned), CombatantConditions.Stunned);
        Set(life.HasFlag(LifeConditions.Blinded), CombatantConditions.Blinded);
        Set(life.HasFlag(LifeConditions.CrippledArmOne), CombatantConditions.CrippledArmOne);
        Set(life.HasFlag(LifeConditions.CrippledArmsBoth), CombatantConditions.CrippledArmsBoth);
        Set(life.HasFlag(LifeConditions.CrippledLegsBoth), CombatantConditions.CrippledLegsBoth);
        Set(traits.HasFlag(LifeTraits.Undead), CombatantConditions.Undead);
        Set(traits.HasFlag(LifeTraits.Mechanical), CombatantConditions.Mechanical);
        Set(traits.HasFlag(LifeTraits.FatigueImmune), CombatantConditions.FatigueImmune);
        Set(traits.HasFlag(LifeTraits.Invulnerable), CombatantConditions.Invulnerable);
        Set(traits.HasFlag(LifeTraits.NighInvulnerable), CombatantConditions.NighInvulnerable);
        Set(traits.HasFlag(LifeTraits.Stoned), CombatantConditions.Stoned);
        Set(traits.HasFlag(LifeTraits.Paralyzed), CombatantConditions.Paralyzed);
        Set(flags.HasFlag(CombatantFlags.Sleeping), CombatantConditions.Sleeping);
        Set(flags.HasFlag(CombatantFlags.FatigueLimiting), CombatantConditions.FatigueLimiting);
        Set(flags.HasFlag(CombatantFlags.FatigueDraining), CombatantConditions.FatigueDraining);
        Set(flags.HasFlag(CombatantFlags.DarkSight), CombatantConditions.DarkSight);
        Set(flags.HasFlag(CombatantFlags.MagneticInversion), CombatantConditions.MagneticInversion);
        return result;
    }

    /// <summary>The worn items the critical-hit procedure looks at, from the slots a critter wears something in (ARC-COMBAT-009).</summary>
    public static CombatWornItems WornItems(IEnumerable<EquipmentSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var worn = CombatWornItems.None;
        foreach (var slot in slots)
            worn |= slot switch
            {
                EquipmentSlot.Helmet => CombatWornItems.Helmet,
                EquipmentSlot.Weapon => CombatWornItems.Weapon,
                EquipmentSlot.Gauntlets => CombatWornItems.Gauntlets,
                EquipmentSlot.Boots => CombatWornItems.Boots,
                EquipmentSlot.Armor => CombatWornItems.BodyArmor,
                _ => CombatWornItems.None,
            };
        return worn;
    }
}
