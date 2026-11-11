using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// The attack formulas of the combat core research (ARC-RES-030, FR-COMBAT-005/008) as pure functions. Numbers are
/// the claims of docs/world/attack-resolver-runtime.md (F0 except the checked anomaly rows); integer division truncates toward zero as in the source.
/// Original executable parity is NOT_VERIFIED.
/// </summary>
public static class AttackRules
{
    public const int MaxSkillLevel = 20;
    public const int MaxArmorClass = 95;
    public const int MaxLightLevel = 255;
    /// <summary>The difficulty added for an out-of-range or blocked shot: no roll can succeed.</summary>
    public const int Impossible = 1_000_000;

    /// <summary>Penalty of an aimed attack: head −50, arm and leg −30, torso (not aimed) 0.</summary>
    public static int HitLocationPenalty(HitLocation location) => location switch
    {
        HitLocation.Torso => 0,
        HitLocation.Head => -50,
        HitLocation.Arm => -30,
        HitLocation.Leg => -30,
        _ => throw new ArgumentOutOfRangeException(nameof(location)),
    };

    /// <summary>Rolled location of an attack that is not aimed: d100 ≤ 70 torso, ≤ 85 leg, ≤ 95 arm, else head.</summary>
    [OriginalFunction(0x4B65A0, OriginalFunctionScope.Partial)]
    public static HitLocation HitLocationFromRoll(int d100)
    {
        if (d100 is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(d100));
        if (d100 <= 70) return HitLocation.Torso;
        if (d100 <= 85) return HitLocation.Leg;
        if (d100 <= 95) return HitLocation.Arm;
        return HitLocation.Head;
    }

    /// <summary>
    /// Attack effectiveness: melee, bow and firearms <c>5L + 25</c>, throwing <c>7L + 25</c>, firearms −20 against a
    /// target in Magnetic Inversion, +10 for extraordinary intelligence, then a PC's ×1.5 (easy) or ×0.75 (hard).
    /// Combat skills are not capped at 95; the result is at least 0. The shared check's effectiveness
    /// (<see cref="SkillCheckRules.Effectiveness"/>, ARC-SKILLS-003).
    /// </summary>
    public static int AttackEffectiveness(
        CombatSkill skill, int level, bool extraordinaryIntelligence, bool isPc, GameDifficulty difficulty,
        bool targetMagneticInversion)
    {
        CheckLevel(level, nameof(level));
        if (skill is not (CombatSkill.Melee or CombatSkill.Bow or CombatSkill.Firearms or CombatSkill.Throwing))
            throw new ArgumentOutOfRangeException(nameof(skill));
        return SkillCheckRules.Effectiveness(SkillRules.Of(skill), level, Facts(extraordinaryIntelligence, isPc, difficulty, targetMagneticInversion));
    }

    /// <summary>
    /// Dodge effectiveness: <c>2L</c>, +10 for extraordinary intelligence, a PC's difficulty scaling, capped at 95. The
    /// retail 1.0.7.4 executable computes <c>2L</c> (ledger, dodge), so both rule settings
    /// follow the original (ARC-RES-048, docs/research/original-verification-ledger.md).
    /// </summary>
    public static int DodgeEffectiveness(int level, bool extraordinaryIntelligence, bool isPc, GameDifficulty difficulty)
    {
        CheckLevel(level, nameof(level));
        return SkillCheckRules.Effectiveness(Skill.Dodge, level, Facts(extraordinaryIntelligence, isPc, difficulty, false));
    }

    /// <summary>
    /// Critical chance of a successful check: <c>effectiveness / 20</c> (+10 aimed at the head, +6 at an arm or leg) +
    /// the weapon's magic critical chance + the backstab term + the effect adjustment.
    /// </summary>
    public static int CriticalHitChance(
        int effectiveness, HitLocation? aimedLocation, int magicCriticalHitChance, int backstabTerm, int effectAdjustment) =>
        // Any combat skill: the shared check's divisor 20 (SkillCheckRules.CriticalSuccessChance).
        SkillCheckRules.CriticalSuccessChance(Skill.Melee, effectiveness, new SkillCriticalFacts
        {
            AimedLocation = aimedLocation,
            WeaponMagicCriticalHit = magicCriticalHitChance,
            BackstabTerm = backstabTerm,
            CriticalHitEffects = Adjustment(effectAdjustment),
        });

    /// <summary>
    /// The critical chance a true backstab adds: <c>2 × backstab level − target level</c>, +20 for a backstab master.
    /// </summary>
    public static int TrueBackstabCriticalBonus(int backstabLevel, SkillTraining backstabTraining, int targetLevel)
    {
        CheckLevel(backstabLevel, nameof(backstabLevel));
        CheckTraining(backstabTraining, nameof(backstabTraining));
        return 2 * backstabLevel - targetLevel + (backstabTraining == SkillTraining.Master ? 20 : 0);
    }

    /// <summary>
    /// Critical chance of a failed combat check: <c>(100 − effectiveness) / 7</c> + item wear
    /// <c>100 × (max − hp) / (max × 7)</c> + the weapon's magic critical miss chance + the effect adjustment, at least 2.
    /// </summary>
    public static int CriticalMissChance(int effectiveness, int itemHp, int itemMaxHp, int magicCriticalMissChance, int effectAdjustment)
    {
        CheckCondition(itemHp, itemMaxHp);
        // Any combat skill: the shared check's divisor 7 (SkillCheckRules.CriticalFailureChance).
        return SkillCheckRules.CriticalFailureChance(Skill.Melee, effectiveness, new SkillCriticalFacts
        {
            Item = itemMaxHp > 0,
            ItemHitPoints = itemHp,
            ItemMaxHitPoints = itemMaxHp,
            WeaponMagicCriticalMiss = magicCriticalMissChance,
            CriticalMissEffects = Adjustment(effectAdjustment),
        });
    }

    /// <summary>Chance that a critical dodge turns the hit into a critical miss: 0, 10, 50 or 100 % by dodge training.</summary>
    public static int DodgeCriticalMissChance(SkillTraining dodgeTraining) => dodgeTraining switch
    {
        SkillTraining.None => 0,
        SkillTraining.Apprentice => 10,
        SkillTraining.Expert => 50,
        SkillTraining.Master => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(dodgeTraining)),
    };

    /// <summary>
    /// Chance that a technological weapon fails critically in magickal hands: <c>−complexity × aptitude / 100</c> when
    /// the weapon is technological (complexity &lt; 0) and the wielder magickal (aptitude &gt; 0), else 0 (0x461700, read to F2 by
    /// ARC-TECHNOLOGY-008, ledger DV4).
    /// </summary>
    [OriginalFunction(0x461700, OriginalFunctionScope.Partial)]
    public static int TechnologyFailureChance(int complexity, int attackerAptitude) =>
        complexity < 0 && attackerAptitude > 0 ? -complexity * attackerAptitude / 100 : 0;

    /// <summary>
    /// Chance that the target's opposing aptitude cancels the weapon. A magickal weapon against a technological target
    /// (aptitude &lt; 0): the target's <c>−aptitude</c>, scaled by <c>(100 − attacker aptitude) / 100</c> unless the
    /// attacker is technological too. A technological weapon against a magickal target: the target's aptitude, scaled
    /// by <c>(100 + attacker aptitude) / 100</c> unless the attacker is magickal too. Otherwise 0 (0x461620, read to F2 by
    /// ARC-TECHNOLOGY-008, ledger DV5).
    /// </summary>
    [OriginalFunction(0x461620, OriginalFunctionScope.Partial)]
    public static int MagickCancelChance(int complexity, int attackerAptitude, int targetAptitude)
    {
        if (complexity > 0 && targetAptitude < 0)
        {
            return attackerAptitude < 0 ? -targetAptitude : -targetAptitude * (100 - attackerAptitude) / 100;
        }
        if (complexity < 0 && targetAptitude > 0)
        {
            return attackerAptitude > 0 ? targetAptitude : targetAptitude * (100 + attackerAptitude) / 100;
        }
        return 0;
    }

    /// <summary>
    /// The darkness penalty of an attack on a target in light <paramref name="targetLight"/>:
    /// <c>30 × (255 − light) / 255</c>, reduced by the attacker's own light (÷4, ÷3, ÷2, ×¾; the original's ×¾ for a small
    /// light, ARC-SKILLS-003 ledger SC9, replaced an earlier ×⅔) and inverted (<c>30 − penalty</c>) by dark sight. Undead
    /// attackers and melee experts and masters take none. A negative result adds nothing. The shared check's term
    /// (<see cref="SkillCheckRules.LightPenalty"/>).
    /// </summary>
    public static int LightPenalty(
        int targetLight, LightEmission attackerLight, bool attackerDarkSight, bool attackerUndead, CombatSkill skill,
        SkillTraining training)
    {
        if (targetLight is < 0 or > MaxLightLevel) throw new ArgumentOutOfRangeException(nameof(targetLight));
        if (!Enum.IsDefined(attackerLight)) throw new ArgumentOutOfRangeException(nameof(attackerLight));
        CheckTraining(training, nameof(training));
        return SkillCheckRules.LightPenalty(SkillRules.Of(skill), training, new SkillDifficultyFacts
        {
            Light = targetLight,
            SourceLight = attackerLight,
            SourceDarkSight = attackerDarkSight,
            SourceUndead = attackerUndead,
        });
    }

    /// <summary>
    /// Armour class: <c>base + worn adjustments + effective AcAdjustment</c>, clamped 0..95, where the effective
    /// AcAdjustment stat (DX − 10) already carries its effects. Under the A6 switch of the Original setting the
    /// AC-adjustment effects are also applied to <c>base + worn</c> through <paramref name="acAdjustmentEffects"/> before
    /// the stat is added.
    /// </summary>
    public static int ArmorClass(
        int baseAc, int wornAdjustment, int effectiveAcAdjustment, Func<int, int> acAdjustmentEffects,
        CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(acAdjustmentEffects);
        ArgumentNullException.ThrowIfNull(profile);
        long ac = (long)baseAc + wornAdjustment;
        if (profile.ArmorClassEffectsCountedTwice) ac = acAdjustmentEffects((int)Math.Clamp(ac, int.MinValue, int.MaxValue));
        ac += effectiveAcAdjustment;
        return (int)Math.Clamp(ac, 0, MaxArmorClass);
    }

    /// <summary>
    /// Whether a melee attack is a backstab (ARC-RES-030, backstab eligibility): the attacker has backstab training,
    /// the target does not face it, and it wields a dagger, or as an expert or master a sword, axe or two-handed sword.
    /// It is a true backstab when the target can neither see nor hear the attacker.
    /// </summary>
    [OriginalFunction(0x45F9D0, OriginalFunctionScope.Partial)]
    public static BackstabKind Backstab(
        SkillTraining backstabTraining, bool targetFacesAttacker, WeaponKind weapon, BackstabWeaponClass weaponClass,
        bool targetSeesOrHearsAttacker)
    {
        CheckTraining(backstabTraining, nameof(backstabTraining));
        if (!Enum.IsDefined(weapon)) throw new ArgumentOutOfRangeException(nameof(weapon));
        if (!Enum.IsDefined(weaponClass)) throw new ArgumentOutOfRangeException(nameof(weaponClass));
        if (backstabTraining == SkillTraining.None || targetFacesAttacker || weapon != WeaponKind.Weapon) return BackstabKind.None;
        var allowed = weaponClass == BackstabWeaponClass.Dagger
            || (backstabTraining >= SkillTraining.Expert
                && weaponClass is BackstabWeaponClass.Sword or BackstabWeaponClass.Axe or BackstabWeaponClass.TwoHandedSword);
        if (!allowed) return BackstabKind.None;
        return targetSeesOrHearsAttacker ? BackstabKind.Backstab : BackstabKind.TrueBackstab;
    }

    /// <summary>
    /// The damage range of one type: the base range, melee <c>DamageBonus</c> on normal and fatigue (a positive bound that
    /// the bonus would bring to 0 or below becomes 1; a non-positive bound just takes the bonus), the weapon's magic
    /// adjustment unless cancelled, then each
    /// bound clamped to <c>0 .. 3 × base max</c> (<c>2 ×</c> for an unarmed melee attack).
    /// </summary>
    public static DamageRange WeaponDamageRange(CombatWeapon weapon, int damageBonus, CombatSkill skill, DamageType type, bool magicCancelled)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        var (min, max) = weapon.Damage[type];
        var melee = skill == CombatSkill.Melee;
        var massive = (melee && weapon.Kind == WeaponKind.Unarmed ? 2 : 3) * max;
        if (melee && type is DamageType.Normal or DamageType.Fatigue)
        {
            min = min <= 0 || min + damageBonus > 0 ? min + damageBonus : 1;
            max = max <= 0 || max + damageBonus > 0 ? max + damageBonus : 1;
        }
        if (weapon.Kind == WeaponKind.Weapon && !magicCancelled)
        {
            var adjustment = weapon.MagicDamageAdjustment[type];
            min += adjustment;
            max += adjustment;
        }
        return new DamageRange(Math.Clamp(min, 0, massive), Math.Clamp(max, 0, massive));
    }

    /// <summary>The hit-point damage a portal or container ignores: stone, brick, metal and force 10, wood 5, else 0.</summary>
    public static int MaterialDamageReduction(ObjectMaterial material) => material switch
    {
        ObjectMaterial.Stone or ObjectMaterial.Brick or ObjectMaterial.Metal or ObjectMaterial.Force => 10,
        ObjectMaterial.Wood => 5,
        ObjectMaterial.Plant or ObjectMaterial.Flesh or ObjectMaterial.Glass or ObjectMaterial.Cloth
            or ObjectMaterial.Liquid or ObjectMaterial.Paper or ObjectMaterial.Gas or ObjectMaterial.Fire
            or ObjectMaterial.Powder => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(material)),
    };

    private static SkillEffectivenessFacts Facts(bool extraordinaryIntelligence, bool isPc, GameDifficulty difficulty, bool magneticInversion)
    {
        if (!Enum.IsDefined(difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
        return new() { ExtraordinaryIntelligence = extraordinaryIntelligence, LocalPc = isPc, Difficulty = difficulty, TargetMagneticInversion = magneticInversion };
    }

    private static IReadOnlyList<EffectModifier> Adjustment(int value) =>
        value == 0 ? [] : [new EffectModifier(StatModifierOperator.Add, value)];

    internal static void CheckLevel(int level, string name)
    {
        if (level is < 0 or > MaxSkillLevel) throw new ArgumentOutOfRangeException(name);
    }

    internal static void CheckTraining(SkillTraining training, string name)
    {
        if (!Enum.IsDefined(training)) throw new ArgumentOutOfRangeException(name);
    }

    internal static void CheckCondition(int hp, int maxHp)
    {
        if (maxHp < 0 || hp < 0 || hp > maxHp) throw new ArgumentOutOfRangeException(nameof(hp), "item condition must be 0 ≤ hp ≤ max");
    }
}
