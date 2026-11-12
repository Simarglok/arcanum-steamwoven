using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Equipment;

/// <summary>
/// ARC-UI-1121 (B30, ledger IW25, IW29, IW41–IW43): the two numbers the inventory and loot windows print under a doll, the original's displayed damage
/// rating (0x464630) and displayed armour rating (0x464700), both weighted averages that the window reads for the critter whose doll it
/// shows. Pure arithmetic over values the caller has already evaluated for that critter (the weapon's damage ranges, the skill effectiveness,
/// armour class and resistances), so the rules stay free of any session.
/// </summary>
public static class DollRatings
{
    /// <summary>The weight of the weapon skill's effectiveness in the damage rating (the constant at 0x5B32EC).</summary>
    public const int SkillWeight = 15;

    /// <summary>The weights of the five damage types in the damage rating, in <see cref="DamageType"/> order (normal, poison, electrical, fire,
    /// fatigue; the table at 0x5B32F0).</summary>
    public static IReadOnlyList<int> DamageTypeWeights { get; } = new int[] { 12, 12, 18, 15, 12 };

    /// <summary>The weight of the armour class in the armour rating (the constant at 0x5B3304).</summary>
    public const int ArmorClassWeight = 24;

    /// <summary>The weights of the five resistances in the armour rating, in the resistance order normal, fire, electrical, poison, magic (the
    /// table at 0x5B3308).</summary>
    public static IReadOnlyList<int> ResistanceWeights { get; } = new int[] { 25, 15, 10, 16, 15 };

    /// <summary>The 6 both ratings add to their divisor.</summary>
    public const int DivisorOffset = 6;

    /// <summary>
    /// The displayed damage rating (0x464630): <c>(15 × skill + Σ weight × (min + max) / 2) / (Σ weights + 15 + 6)</c> over the five damage types of
    /// the wielded weapon (bare hands without one) for the wielder, with the weapon skill's effectiveness (<see cref="AttackRules.AttackEffectiveness"/>,
    /// 0x4C62E0 or 0x4C69F0 without a target). The ranges are those the attack uses (<see cref="AttackRules.WeaponDamageRange"/>, 0x465F70 with the
    /// magic adjustments). Integer division truncates toward zero as the original's.
    /// </summary>
    [OriginalFunction(0x464630, OriginalFunctionScope.Full)]
    public static int Damage(CombatWeapon weapon, int damageBonus, CombatSkill skill, int skillEffectiveness)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        long total = (long)SkillWeight * skillEffectiveness, weights = SkillWeight;
        foreach (var type in Enum.GetValues<DamageType>())
        {
            var range = AttackRules.WeaponDamageRange(weapon, damageBonus, skill, type, magicCancelled: false);
            var weight = DamageTypeWeights[(int)type];
            total += (long)((range.Min + range.Max) / 2) * weight;
            weights += weight;
        }
        return (int)(total / (weights + DivisorOffset));
    }

    /// <summary>
    /// The displayed armour rating (0x464700): <c>(24 × armour class + Σ weight × resistance) / (Σ weights + 24 + 6) × 2</c> over the five resistances
    /// (normal, fire, electrical, poison, magic), the division first as in the original. The armour class and resistances are the displayed
    /// forms: unidentified magickal armour gives no magic adjustment (0x465BA0 and 0x43D6D0 with their identified-only argument).
    /// </summary>
    [OriginalFunction(0x464700, OriginalFunctionScope.Full)]
    public static int Armour(int armorClass, int normal, int fire, int electrical, int poison, int magic)
    {
        long total = (long)ArmorClassWeight * armorClass, weights = ArmorClassWeight;
        int[] resistances = [normal, fire, electrical, poison, magic];
        for (var i = 0; i < resistances.Length; i++)
        {
            total += (long)resistances[i] * ResistanceWeights[i];
            weights += ResistanceWeights[i];
        }
        return (int)(total / (weights + DivisorOffset)) * 2;
    }

    /// <summary>Whether the weapon in the weapon slot is too heavy for its wielder, the test that fills its slot on the doll (0x576520, 0x577D0C):
    /// its minimum strength for the wielder (<see cref="EquipmentRules.MinimumStrength"/>, 0x465E90) exceeds the wielder's effective strength.</summary>
    public static bool TooHeavy(int minimumStrength, int strength) => minimumStrength > strength;
}
