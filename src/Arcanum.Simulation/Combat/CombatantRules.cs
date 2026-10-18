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
    /// A critter's effective resistances: its stored resistance plus its worn armour's adjustments (with their magic, already
    /// scaled for the wearer, <see cref="EquipmentRules.ResistanceAdjustments"/>), plus <c>5 × (CN − 4)</c> for poison when
    /// positive, clamped to 0..95 unless the critter is a monstrous NPC (<paramref name="unclamped"/>). Resistance effects are not
    /// applied (no owner models them yet).
    /// </summary>
    [OriginalFunction(0x43D6D0, OriginalFunctionScope.Partial)]
    public static DamageResistances Resistances(DamageResistances stored, EquipmentResistances worn, int constitution, bool unclamped)
    {
        foreach (var value in new[] { stored.Normal, stored.Poison, stored.Electrical, stored.Fire, worn.Normal, worn.Poison, worn.Electrical,
                     worn.Fire, constitution })
            if (Math.Abs((long)value) > MaxValue) throw new ArgumentOutOfRangeException(nameof(stored));
        var poisonBonus = Math.Max(0, 5 * (constitution - 4));
        int Clamp(long value) => unclamped ? (int)Math.Clamp(value, -AttackResolver.MaxResistance, AttackResolver.MaxResistance)
            : (int)Math.Clamp(value, 0, MaxResistance);
        return new(Clamp((long)stored.Normal + worn.Normal), Clamp((long)stored.Poison + worn.Poison + poisonBonus),
            Clamp((long)stored.Electrical + worn.Electrical), Clamp((long)stored.Fire + worn.Fire));
    }

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
}
