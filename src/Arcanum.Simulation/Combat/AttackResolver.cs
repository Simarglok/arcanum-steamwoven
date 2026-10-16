namespace Arcanum.Simulation.Combat;

/// <summary>
/// Resolves one attack (ARC-RES-030 FR-COMBAT-004/005/008) as a pure function of an explicit request, an injected roll
/// source, the critical-effect tables and the anomaly profile: hit location, technology failure, magick cancellation,
/// the attack roll, dodge, fate, the critical miss that turns the attack on the attacker, and the typed damage packet
/// with immunities and resistances. Rolls are made at the same points and in the same order as in the source (CE at
/// <see cref="AttackRules.SourceRevision"/>), except where a recorded D2 fix removes one. Ammunition, approach and
/// line of fire, projectile flight, scripts and applying the damage to vital state stay with the caller.
/// </summary>
public static class AttackResolver
{
    /// <summary>Bound on caller magnitudes (damage, adjustments, distances), so no intermediate value overflows.</summary>
    public const int MaxMagnitude = 100_000;
    public const int MaxResistance = 1_000;
    public const int MaxAptitude = 100;

    private const CombatantConditions Inactive = CombatantConditions.Dead | CombatantConditions.Unconscious
        | CombatantConditions.Paralyzed | CombatantConditions.Stunned | CombatantConditions.Stoned;

    private const CombatantConditions DodgeDisabled = CombatantConditions.Unconscious | CombatantConditions.Stoned
        | CombatantConditions.Paralyzed | CombatantConditions.Stunned;

    private static readonly CombatantConditions AllConditions =
        Enum.GetValues<CombatantConditions>().Aggregate(CombatantConditions.None, (all, flag) => all | flag);

    public static AttackResolution Resolve(
        AttackRequest request, ICombatRollSource rollSource, ICriticalEffects? criticalEffects = null,
        CombatAnomalyProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rollSource);
        criticalEffects ??= NoCriticalEffects.Instance;
        profile ??= CombatAnomalyProfile.Default;
        Validate(request);

        var rolls = new CombatRollLog(rollSource);
        var attacker = request.Attacker;
        var target = request.Target;
        var weapon = request.Weapon;
        var targetIsCritter = request.TargetKind == AttackTargetKind.Critter;

        if ((attacker.Conditions & Inactive) != 0) return Refused(AttackRefusal.AttackerInactive, rolls);
        if (targetIsCritter && target.Conditions.HasFlag(CombatantConditions.Dead)) return Refused(AttackRefusal.TargetDead, rolls);

        var aimed = request.AimedLocation is not null;
        var location = request.AimedLocation ?? AttackRules.HitLocationFromRoll(rolls.D100(CombatRollPurpose.HitLocation));

        var hit = false;
        var critical = false;
        var magicCancelled = false;
        var missCause = AttackMissCause.None;
        AttackDifficulty? difficulty = null;
        SkillCheck? attackCheck = null;
        SkillCheck? dodgeCheck = null;

        // A thrown weapon and bare hands take no aptitude checks.
        var aptitudeApplies = weapon.Kind != WeaponKind.Unarmed && request.Skill != CombatSkill.Throwing;
        var technologyFailureChance = aptitudeApplies
            ? AttackRules.TechnologyFailureChance(weapon.MagickTechComplexity, attacker.MagickTechAptitude)
            : 0;
        if (rolls.D100(CombatRollPurpose.TechnologyFailure) <= technologyFailureChance)
        {
            critical = true;
            missCause = AttackMissCause.TechnologyFailure;
        }
        else
        {
            var cancelChance = aptitudeApplies && targetIsCritter
                ? AttackRules.MagickCancelChance(weapon.MagickTechComplexity, attacker.MagickTechAptitude, target.MagickTechAptitude)
                : 0;
            var proceed = true;
            if (rolls.D100(CombatRollPurpose.MagickCancel) <= cancelChance)
            {
                // A magickal melee weapon still swings, without its magic; anything else misses.
                if (request.Skill == CombatSkill.Melee && weapon.Kind == WeaponKind.Weapon && weapon.MagickTechComplexity > 0)
                {
                    magicCancelled = true;
                }
                else
                {
                    proceed = false;
                    missCause = AttackMissCause.MagickCancelled;
                }
            }

            if (proceed && !targetIsCritter)
            {
                hit = true;
            }
            else if (proceed)
            {
                var effectiveness = AttackRules.AttackEffectiveness(
                    request.Skill, request.SkillLevel, attacker.ExtraordinaryIntelligence, attacker.IsPc, request.Difficulty,
                    target.Conditions.HasFlag(CombatantConditions.MagneticInversion));
                difficulty = AttackDifficultyOf(request, effectiveness, magicCancelled, profile);
                attackCheck = AttackCheck(request, effectiveness, difficulty.Total, magicCancelled, rolls);
                hit = attackCheck.Success;
                critical = attackCheck.Critical;
                if (!hit) missCause = AttackMissCause.SkillCheck;

                if (hit)
                {
                    dodgeCheck = DodgeCheck(target, request.Difficulty, rolls);
                    if (dodgeCheck.Success)
                    {
                        hit = false;
                        critical = false;
                        missCause = AttackMissCause.Dodged;
                        if (dodgeCheck.Critical
                            && rolls.D100(CombatRollPurpose.DodgeCriticalMiss) <= AttackRules.DodgeCriticalMissChance(target.DodgeTraining))
                        {
                            critical = true;
                        }
                    }
                }
            }
        }

        var fate = FateUse.None;
        if (attacker.IsPc)
        {
            if (attacker.CriticalHitFateArmed)
            {
                fate = FateUse.AttackerCriticalHit;
                hit = true;
                critical = true;
                missCause = AttackMissCause.None;
            }
        }
        else if (targetIsCritter && target.IsPc && target.CriticalMissFateArmed)
        {
            fate = FateUse.TargetCriticalMiss;
            hit = false;
            critical = true;
            missCause = AttackMissCause.Fate;
        }

        if (hit)
        {
            var crit = new CriticalAccumulator();
            var damage = Damage(request, DamageRecipient.Target, target, request.TargetKind, critical, location, aimed,
                magicCancelled, crit, criticalEffects, rolls, profile);
            return new AttackResolution(AttackRefusal.None, critical ? AttackOutcome.CriticalHit : AttackOutcome.Hit,
                AttackMissCause.None, location, aimed, magicCancelled, difficulty, attackCheck, dodgeCheck, fate,
                CriticalSelfHit: false, damage, rolls.Rolls);
        }

        if (!critical)
        {
            return new AttackResolution(AttackRefusal.None, AttackOutcome.Miss, missCause, location, aimed, magicCancelled,
                difficulty, attackCheck, dodgeCheck, fate, CriticalSelfHit: false, Damage: null, rolls.Rolls);
        }

        // Critical miss: the attack turns on the attacker, which always hits itself, not aimed; half of the time critically.
        var selfCritical = rolls.D100(CombatRollPurpose.CriticalSelfHit) <= 50;
        var accumulator = new CriticalAccumulator();
        if (selfCritical)
        {
            var missChance = (weapon.Kind == WeaponKind.Weapon && !magicCancelled ? weapon.MagicCriticalMissEffectChance : 0)
                + attacker.CriticalMissEffectChanceAdjustment;
            accumulator.MissEffects = criticalEffects.RollMiss(
                new CriticalMissContext(attacker, weapon, MissChart(weapon), missChance), rolls);
            if (profile.CriticalSelfHitRolledTwice)
            {
                RollCriticalHit(request, attacker, location, aimed: false, magicCancelled, selfHit: true, accumulator,
                    criticalEffects, rolls);
            }
        }
        var selfDamage = Damage(request, DamageRecipient.Attacker, attacker, AttackTargetKind.Critter, selfCritical, location,
            aimed: false, magicCancelled, accumulator, criticalEffects, rolls, profile);
        return new AttackResolution(AttackRefusal.None, AttackOutcome.CriticalMiss, missCause, location, Aimed: false,
            magicCancelled, difficulty, attackCheck, dodgeCheck, fate, selfCritical, selfDamage, rolls.Rolls);
    }

    private static AttackResolution Refused(AttackRefusal refusal, CombatRollLog rolls) =>
        new(refusal, AttackOutcome.Refused, AttackMissCause.None, HitLocation.Torso, false, false, null, null, null,
            FateUse.None, false, null, rolls.Rolls);

    private static AttackDifficulty AttackDifficultyOf(
        AttackRequest request, int effectiveness, bool magicCancelled, CombatAnomalyProfile profile)
    {
        var attacker = request.Attacker;
        var target = request.Target;
        var weapon = request.Weapon;

        var invocation = request.TargetWasInBed ? -30 : 0;
        // A2: the source starts the difficulty from the invocation modifier and then adds it again.
        if (profile.InvocationModifierCountedTwice) invocation *= 2;

        var weaponPart = 0;
        if (weapon.Kind == WeaponKind.Weapon)
        {
            weaponPart -= weapon.ToHitBonus + (magicCancelled ? 0 : weapon.MagicToHitAdjustment);
            var shortfall = weapon.MinimumStrength - attacker.Strength;
            if (shortfall > 0) weaponPart += 5 * shortfall;
        }

        var conditions = target.Conditions;
        var awareness = 0;
        if (conditions.HasFlag(CombatantConditions.Paralyzed)) awareness = -50;
        else if (conditions.HasFlag(CombatantConditions.Unconscious)) awareness = -50;
        else if ((conditions & (CombatantConditions.FallingDown | CombatantConditions.Stunned)) != 0) awareness = -30;
        else if (conditions.HasFlag(CombatantConditions.Sleeping)) awareness = -50;
        else if (request.TargetUnaware) awareness = -30;

        var aim = 0;
        if (request.AimedLocation is { } aimed)
        {
            var penalty = AttackRules.HitLocationPenalty(aimed);
            if (request.Skill == CombatSkill.Firearms && request.Training >= SkillTraining.Expert) penalty = 2 * penalty / 3;
            aim = -penalty;
        }

        var armor = 0;
        if (!(request.Backstab == BackstabKind.TrueBackstab && attacker.BackstabTraining != SkillTraining.None))
        {
            armor = effectiveness * (target.ArmorClass / 2) / 100;
        }

        var range = 0;
        var cover = 0;
        if (request.Skill != CombatSkill.Melee)
        {
            if (request.Distance > weapon.Range) range += AttackRules.Impossible;
            if (request.Training < SkillTraining.Master)
            {
                var extra = request.Distance - attacker.Perception / 2;
                if (extra > 0) range += 5 * extra;
            }
            if (request.LineOfFireBlocked) range += AttackRules.Impossible;
            else if (request.CoverRating > 0) cover = request.CoverRating;
        }

        var light = AttackRules.LightPenalty(target.LightLevel, attacker.LightEmission,
            attacker.Conditions.HasFlag(CombatantConditions.DarkSight), attacker.Conditions.HasFlag(CombatantConditions.Undead),
            request.Skill, request.Training);

        var injury = 0;
        if (attacker.Conditions.HasFlag(CombatantConditions.Blinded)) injury += 30;
        if (attacker.Conditions.HasFlag(CombatantConditions.CrippledArmsBoth)) injury += 50;
        else if (attacker.Conditions.HasFlag(CombatantConditions.CrippledArmOne)) injury += 20;

        return new AttackDifficulty(invocation, weaponPart, awareness, aim, armor, range, cover, light, injury);
    }

    private static SkillCheck AttackCheck(
        AttackRequest request, int effectiveness, int difficulty, bool magicCancelled, CombatRollLog rolls)
    {
        var attacker = request.Attacker;
        var weapon = request.Weapon;
        var roll = rolls.D100(CombatRollPurpose.AttackCheck);
        var success = difficulty + roll <= effectiveness;
        var criticalRoll = rolls.D100(CombatRollPurpose.AttackCritical);
        int chance;
        if (success)
        {
            var backstab = request.Backstab == BackstabKind.TrueBackstab
                ? AttackRules.TrueBackstabCriticalBonus(attacker.BackstabLevel, attacker.BackstabTraining, request.Target.Level)
                : 0;
            chance = AttackRules.CriticalHitChance(effectiveness, request.AimedLocation,
                weapon.Kind == WeaponKind.Weapon && !magicCancelled ? weapon.MagicCriticalHitChance : 0, backstab,
                attacker.CriticalHitChanceAdjustment);
        }
        else if (request.Skill == CombatSkill.Melee && request.Training == SkillTraining.Master)
        {
            // A melee master never fails critically.
            return new SkillCheck(effectiveness, difficulty, roll, false, 0, criticalRoll, false);
        }
        else
        {
            var hasItem = weapon.Kind != WeaponKind.Unarmed;
            chance = AttackRules.CriticalMissChance(effectiveness, hasItem ? weapon.Hp : 0, hasItem ? weapon.MaxHp : 0,
                weapon.Kind == WeaponKind.Weapon && !magicCancelled ? weapon.MagicCriticalMissChance : 0,
                attacker.CriticalMissChanceAdjustment);
        }
        return new SkillCheck(effectiveness, difficulty, roll, success, chance, criticalRoll, criticalRoll <= chance);
    }

    private static SkillCheck DodgeCheck(Combatant target, GameDifficulty gameDifficulty, CombatRollLog rolls)
    {
        var effectiveness = AttackRules.DodgeEffectiveness(target.DodgeLevel, target.ExtraordinaryIntelligence, target.IsPc, gameDifficulty);
        var difficulty = 0;
        if (target.Conditions.HasFlag(CombatantConditions.Blinded)) difficulty += 30;
        if (target.Conditions.HasFlag(CombatantConditions.CrippledLegsBoth)) difficulty += 30;
        var roll = rolls.D100(CombatRollPurpose.DodgeCheck);
        var success = difficulty + roll <= effectiveness;
        var criticalRoll = rolls.D100(CombatRollPurpose.DodgeCritical);
        var chance = success
            ? AttackRules.CriticalHitChance(effectiveness, null, 0, 0, target.CriticalHitChanceAdjustment)
            : AttackRules.CriticalMissChance(effectiveness, 0, 0, 0, target.CriticalMissChanceAdjustment);
        var critical = criticalRoll <= chance;
        // The source rolls first and then fails the dodge of a critter that cannot move.
        if ((target.Conditions & DodgeDisabled) != 0)
        {
            success = false;
            critical = false;
        }
        return new SkillCheck(effectiveness, difficulty, roll, success, chance, criticalRoll, critical);
    }

    private static AttackDamage Damage(
        AttackRequest request, DamageRecipient recipientRole, Combatant recipient, AttackTargetKind recipientKind,
        bool critical, HitLocation location, bool aimed, bool magicCancelled, CriticalAccumulator crit,
        ICriticalEffects criticalEffects, CombatRollLog rolls, CombatAnomalyProfile profile)
    {
        var attacker = request.Attacker;
        var weapon = request.Weapon;
        var selfHit = recipientRole == DamageRecipient.Attacker;
        var recipientIsCritter = recipientKind == AttackTargetKind.Critter;
        var conditions = recipientIsCritter ? recipient.Conditions : CombatantConditions.None;

        // A10: the source keeps the backstab bonus when the attack turned on the attacker.
        var backstab = selfHit && !profile.BackstabDamageOnSelfHit ? BackstabKind.None : request.Backstab;

        var rolled = DamageByType.Zero;
        foreach (var type in Enum.GetValues<DamageType>())
        {
            if (type == DamageType.Poison
                && (conditions & (CombatantConditions.Undead | CombatantConditions.Mechanical | CombatantConditions.Stoned)) != 0)
            {
                continue;
            }
            var range = AttackRules.WeaponDamageRange(weapon, attacker.DamageBonus, request.Skill, type, magicCancelled);
            // Like the source's random_between, an empty range draws nothing.
            var value = range.Min == range.Max
                ? range.Min
                : rolls.Roll(DamagePurpose(type), Math.Min(range.Min, range.Max), Math.Max(range.Min, range.Max));
            if (type == DamageType.Normal)
            {
                if (backstab == BackstabKind.TrueBackstab) value += 5 * attacker.BackstabLevel;
                else if (backstab == BackstabKind.Backstab) value += attacker.BackstabLevel;
            }
            else if (type == DamageType.Fatigue)
            {
                if (conditions.HasFlag(CombatantConditions.FatigueDraining) && value > 0) value *= 4;
                if (conditions.HasFlag(CombatantConditions.FatigueLimiting) && value > 0) value /= 4;
            }
            rolled = rolled.With(type, value);
        }
        if (weapon.Kind == WeaponKind.Unarmed)
        {
            rolled = attacker.UnarmedBodySpell switch
            {
                UnarmedBodySpell.BodyOfEarth => rolled with { Normal = rolled.Normal + 5 },
                UnarmedBodySpell.BodyOfFire => rolled with { Fire = rolled.Fire + 15 },
                UnarmedBodySpell.BodyOfWater => rolled with { Fatigue = rolled.Fatigue + 15 },
                UnarmedBodySpell.HardenedHands => rolled with { Normal = rolled.Normal + 2 },
                _ => rolled,
            };
        }

        var damage = rolled;
        if ((conditions & (CombatantConditions.Undead | CombatantConditions.Mechanical)) != 0) damage = damage with { Poison = 0 };
        else if (conditions.HasFlag(CombatantConditions.Stoned)) damage = DamageByType.Zero;

        if (recipient.Conditions.HasFlag(CombatantConditions.Invulnerable))
        {
            return Prevented(recipientRole, rolled, DamagePrevention.Invulnerable);
        }
        if (recipientIsCritter && recipient.Conditions.HasFlag(CombatantConditions.NighInvulnerable)
            && (weapon.Kind == WeaponKind.Unarmed || !weapon.Uber))
        {
            return Prevented(recipientRole, rolled, DamagePrevention.NighInvulnerable);
        }
        if (recipientKind == AttackTargetKind.Wall) return Prevented(recipientRole, rolled, DamagePrevention.Wall);

        if (critical && recipientIsCritter)
        {
            RollCriticalHit(request, recipient, location, aimed, magicCancelled, selfHit, crit, criticalEffects, rolls);
        }

        if (!(weapon.Kind == WeaponKind.Weapon && weapon.IgnoresResistance))
        {
            foreach (var type in Enum.GetValues<DamageType>())
            {
                var resistance = Resistance(recipient.Resistances, type);
                if (resistance > 0) damage = damage.With(type, damage[type] - (int)((long)resistance * damage[type] / 100));
            }
        }

        var hitPoints = damage.Normal + damage.Electrical + damage.Fire;
        var damagesArmor = damage.Fire > 0 || (weapon.Kind == WeaponKind.Weapon && weapon.DamagesArmor);
        if (attacker.IsPc)
        {
            hitPoints = request.Difficulty switch
            {
                GameDifficulty.Easy => hitPoints + hitPoints / 2,
                GameDifficulty.Hard => hitPoints - hitPoints / 4,
                _ => hitPoints,
            };
        }
        var multiplier = crit.Multiplier;
        hitPoints = multiplier switch
        {
            DamageMultiplier.Triple => hitPoints * 3,
            DamageMultiplier.Double => hitPoints * 2,
            DamageMultiplier.OneAndHalf => hitPoints + hitPoints / 2,
            _ => hitPoints,
        };

        int fatigue;
        int poison;
        if (recipientIsCritter)
        {
            fatigue = conditions.HasFlag(CombatantConditions.FatigueImmune) || damage.Fatigue <= 0 ? 0 : damage.Fatigue;
            poison = damage.Poison;
        }
        else
        {
            if (recipientKind is AttackTargetKind.Portal or AttackTargetKind.Container)
            {
                hitPoints -= AttackRules.MaterialDamageReduction(recipient.Material);
            }
            hitPoints = Math.Max(0, hitPoints);
            fatigue = 0;
            poison = 0;
        }

        return new AttackDamage(recipientRole, rolled, damage, hitPoints, fatigue, poison, multiplier,
            crit.MultiplierFromFallback, crit.HitEffects, crit.MissEffects, damagesArmor, DamagePrevention.None);
    }

    private static AttackDamage Prevented(DamageRecipient recipient, DamageByType rolled, DamagePrevention prevention) =>
        // The source returns before any critical effect, including the self-hit's weapon and ammunition effects, applies.
        new(recipient, rolled, DamageByType.Zero, 0, 0, 0, DamageMultiplier.None, false, CriticalHitEffects.None,
            CriticalMissEffects.None, false, prevention);

    /// <summary>
    /// The source's critical-hit procedure: the multiplier rolls (×3 at chance + 10, else ×2 at chance + 30, else ×1.5
    /// at chance + 60; none when an NPC hits a PC), then the effect table, then ×1.5 when nothing at all came out.
    /// </summary>
    private static void RollCriticalHit(
        AttackRequest request, Combatant recipient, HitLocation location, bool aimed, bool magicCancelled, bool selfHit,
        CriticalAccumulator crit, ICriticalEffects criticalEffects, CombatRollLog rolls)
    {
        var attacker = request.Attacker;
        var weapon = request.Weapon;
        // A8: CE leaves this chance unassigned when the weapon's magic was cancelled; both profiles use 0.
        var chance = (weapon.Kind == WeaponKind.Weapon && !magicCancelled ? weapon.MagicCriticalEffectChance : 0)
            + attacker.CriticalEffectChanceAdjustment + (aimed ? 10 : 0);
        var npcHitsPc = recipient.IsPc && !attacker.IsPc;

        if (!npcHitsPc)
        {
            var rolled = DamageMultiplier.None;
            if (rolls.D100(CombatRollPurpose.DamageTriple) <= chance + 10) rolled = DamageMultiplier.Triple;
            else if (rolls.D100(CombatRollPurpose.DamageDouble) <= chance + 30) rolled = DamageMultiplier.Double;
            else if (rolls.D100(CombatRollPurpose.DamageOneAndHalf) <= chance + 60) rolled = DamageMultiplier.OneAndHalf;
            if (rolled > crit.RolledMultiplier) crit.RolledMultiplier = rolled;
        }

        var chart = weapon.Kind == WeaponKind.Weapon ? weapon.CriticalHitChart : CriticalHitChart.Crushing;
        crit.HitEffects |= criticalEffects.RollHit(
            new CriticalHitContext(attacker, recipient, weapon, chart, location, aimed, chance, npcHitsPc, selfHit), rolls);

        if (crit.RolledMultiplier == DamageMultiplier.None && !crit.Fallback
            && crit.HitEffects == CriticalHitEffects.None && crit.MissEffects == CriticalMissEffects.None)
        {
            crit.Fallback = true;
        }
    }

    private static int Resistance(DamageResistances resistances, DamageType type) => type switch
    {
        DamageType.Normal => resistances.Normal,
        DamageType.Poison => resistances.Poison,
        DamageType.Electrical => resistances.Electrical,
        DamageType.Fire => resistances.Fire,
        DamageType.Fatigue => 3 * resistances.Normal / 4,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static CombatRollPurpose DamagePurpose(DamageType type) => type switch
    {
        DamageType.Normal => CombatRollPurpose.DamageNormal,
        DamageType.Poison => CombatRollPurpose.DamagePoison,
        DamageType.Electrical => CombatRollPurpose.DamageElectrical,
        DamageType.Fire => CombatRollPurpose.DamageFire,
        DamageType.Fatigue => CombatRollPurpose.DamageFatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static CriticalMissChart MissChart(CombatWeapon weapon) => weapon.Kind switch
    {
        WeaponKind.Unarmed => CriticalMissChart.Hands,
        WeaponKind.ThrownItem => CriticalMissChart.Bludgeon,
        _ => weapon.CriticalMissChart,
    };

    private static void Validate(AttackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Attacker, nameof(request.Attacker));
        ArgumentNullException.ThrowIfNull(request.Target, nameof(request.Target));
        ArgumentNullException.ThrowIfNull(request.Weapon, nameof(request.Weapon));
        Check(Enum.IsDefined(request.TargetKind), "TargetKind");
        Check(Enum.IsDefined(request.Skill), "Skill");
        Check(Enum.IsDefined(request.Training), "Training");
        Check(Enum.IsDefined(request.Backstab), "Backstab");
        Check(Enum.IsDefined(request.Difficulty), "Difficulty");
        Check(request.SkillLevel is >= 0 and <= AttackRules.MaxSkillLevel, "SkillLevel");
        if (request.AimedLocation is { } aimed)
        {
            Check(Enum.IsDefined(aimed) && aimed != HitLocation.Torso, "AimedLocation (the torso cannot be aimed at)");
        }
        Check(request.Distance is >= 0 and <= MaxMagnitude, "Distance");
        Check(request.CoverRating is >= 0 and <= MaxMagnitude, "CoverRating");

        var weapon = request.Weapon;
        Check(Enum.IsDefined(weapon.Kind), "Weapon.Kind");
        Check(Enum.IsDefined(weapon.CriticalHitChart), "Weapon.CriticalHitChart");
        Check(Enum.IsDefined(weapon.CriticalMissChart), "Weapon.CriticalMissChart");
        Check(weapon.Kind != WeaponKind.Unarmed || request.Skill == CombatSkill.Melee, "Skill (bare hands attack in melee)");
        Check(weapon.Kind != WeaponKind.ThrownItem || request.Skill == CombatSkill.Throwing, "Skill (a non-weapon item can only be thrown)");
        Check(request.Backstab == BackstabKind.None
            || (request.Skill == CombatSkill.Melee && weapon.Kind == WeaponKind.Weapon && request.TargetKind == AttackTargetKind.Critter),
            "Backstab (melee with a weapon against a critter only)");
        Check(weapon.Range is >= 0 and <= MaxMagnitude, "Weapon.Range");
        foreach (var type in Enum.GetValues<DamageType>())
        {
            var range = weapon.Damage[type];
            Check(range.Min is >= 0 and <= MaxMagnitude && range.Max is >= 0 and <= MaxMagnitude, $"Weapon.Damage.{type}");
            Check(Math.Abs(weapon.MagicDamageAdjustment[type]) <= MaxMagnitude, $"Weapon.MagicDamageAdjustment.{type}");
        }
        Check(Magnitude(weapon.ToHitBonus, weapon.MinimumStrength, weapon.MagickTechComplexity, weapon.MagicToHitAdjustment,
            weapon.MagicCriticalHitChance, weapon.MagicCriticalMissChance, weapon.MagicCriticalEffectChance,
            weapon.MagicCriticalMissEffectChance), "Weapon values");
        Check(weapon.MaxHp is >= 0 and <= MaxMagnitude && weapon.Hp >= 0 && weapon.Hp <= weapon.MaxHp, "Weapon condition (0 ≤ Hp ≤ MaxHp)");

        ValidateCombatant(request.Attacker, "Attacker");
        ValidateCombatant(request.Target, "Target");
    }

    private static void ValidateCombatant(Combatant c, string name)
    {
        Check((c.Conditions & ~AllConditions) == 0, $"{name}.Conditions");
        Check(Enum.IsDefined(c.LightEmission), $"{name}.LightEmission");
        Check(Enum.IsDefined(c.DodgeTraining), $"{name}.DodgeTraining");
        Check(Enum.IsDefined(c.BackstabTraining), $"{name}.BackstabTraining");
        Check(Enum.IsDefined(c.UnarmedBodySpell), $"{name}.UnarmedBodySpell");
        Check(Enum.IsDefined(c.Material), $"{name}.Material");
        Check(c.DodgeLevel is >= 0 and <= AttackRules.MaxSkillLevel, $"{name}.DodgeLevel");
        Check(c.BackstabLevel is >= 0 and <= AttackRules.MaxSkillLevel, $"{name}.BackstabLevel");
        Check(c.ArmorClass is >= 0 and <= AttackRules.MaxArmorClass, $"{name}.ArmorClass");
        Check(c.LightLevel is >= 0 and <= AttackRules.MaxLightLevel, $"{name}.LightLevel");
        Check(c.MagickTechAptitude is >= -MaxAptitude and <= MaxAptitude, $"{name}.MagickTechAptitude");
        Check(c.Level is >= 0 and <= MaxMagnitude && c.Strength is >= 0 and <= MaxMagnitude
            && c.Perception is >= 0 and <= MaxMagnitude, $"{name} level and stats");
        Check(Magnitude(c.DamageBonus, c.CriticalHitChanceAdjustment, c.CriticalMissChanceAdjustment,
            c.CriticalEffectChanceAdjustment, c.CriticalMissEffectChanceAdjustment), $"{name} adjustments");
        var r = c.Resistances;
        Check(new[] { r.Normal, r.Poison, r.Electrical, r.Fire }.All(v => Math.Abs(v) <= MaxResistance), $"{name}.Resistances");
    }

    private static bool Magnitude(params int[] values) => values.All(v => v is >= -MaxMagnitude and <= MaxMagnitude);

    private static void Check(bool valid, string what)
    {
        if (!valid) throw new ArgumentOutOfRangeException(nameof(AttackRequest), what);
    }

    private sealed class CriticalAccumulator
    {
        public DamageMultiplier RolledMultiplier { get; set; }
        public bool Fallback { get; set; }
        public CriticalHitEffects HitEffects { get; set; }
        public CriticalMissEffects MissEffects { get; set; }

        public DamageMultiplier Multiplier =>
            RolledMultiplier != DamageMultiplier.None ? RolledMultiplier : Fallback ? DamageMultiplier.OneAndHalf : DamageMultiplier.None;

        public bool MultiplierFromFallback => RolledMultiplier == DamageMultiplier.None && Fallback;
    }
}
