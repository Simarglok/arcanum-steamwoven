using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// Resolves one attack (ARC-RES-030 FR-COMBAT-004/005/008) as a pure function of an explicit request, an injected roll
/// source, the critical-effect tables and the anomaly profile: hit location, technology failure, magick cancellation,
/// the attack roll, dodge, fate, the critical miss that turns the attack on the attacker, and the typed damage packet
/// with immunities and resistances. The combat rolls are made at the same points and in the same order as in the
/// source (docs/world/attack-resolver-runtime.md, F0), except where a recorded D2 fix removes one; the source also draws
/// from the same generator outside combat (AI reaction, sounds, the effect tables' stat checks), so a seeded run matches
/// the source's sequence only once those draws are modelled too. Ammunition, approach and
/// line of fire, projectile flight, scripts and applying the damage to vital state stay with the caller. The resolution also
/// names the armour and weapon wear of the damage step (<see cref="AttackResolution.Wear"/>, ARC-COMBAT-007), which the
/// session applies.
/// </summary>
public static class AttackResolver
{
    /// <summary>Bound on caller magnitudes (damage, adjustments, distances), so no intermediate value overflows.</summary>
    public const int MaxMagnitude = 100_000;
    public const int MaxResistance = 1_000;
    public const int MaxAptitude = 100;
    private const CombatWornItems AllWorn = CombatWornItems.Helmet | CombatWornItems.Weapon | CombatWornItems.Gauntlets
        | CombatWornItems.Boots | CombatWornItems.BodyArmor;

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
        profile ??= CombatAnomalyProfile.Steamwoven;
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
                var check = SkillCheckRules.Run(AttackCheckRequest(request, magicCancelled),
                    new CheckRolls(rolls, CombatRollPurpose.AttackCheck, CombatRollPurpose.AttackCritical), profile.InvocationModifierCountedTwice);
                difficulty = AttackDifficultyOf(check.Difficulty, profile);
                attackCheck = check.Check;
                hit = attackCheck.Success;
                critical = attackCheck.Critical;
                if (!hit) missCause = AttackMissCause.SkillCheck;

                if (hit)
                {
                    dodgeCheck = DodgeCheck(target, request.Difficulty, rolls, profile);
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
                CriticalSelfHit: false, damage, rolls.Rolls.ToArray())
            {
                Wear = CombatWearRules.Of(request, damage, location, target.Material, targetIsCritter),
            };
        }

        if (!critical)
        {
            return new AttackResolution(AttackRefusal.None, AttackOutcome.Miss, missCause, location, aimed, magicCancelled,
                difficulty, attackCheck, dodgeCheck, fate, CriticalSelfHit: false, Damage: null, rolls.Rolls.ToArray());
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
        // The self-hit keeps the attack's location (rolled or aimed), so the attacker's own armour there takes the wear.
        return new AttackResolution(AttackRefusal.None, AttackOutcome.CriticalMiss, missCause, location, Aimed: false,
            magicCancelled, difficulty, attackCheck, dodgeCheck, fate, selfCritical, selfDamage, rolls.Rolls.ToArray())
        {
            Wear = CombatWearRules.Of(request, selfDamage, location, attacker.Material, recipientIsCritter: true),
        };
    }

    private static AttackResolution Refused(AttackRefusal refusal, CombatRollLog rolls) =>
        new(refusal, AttackOutcome.Refused, AttackMissCause.None, HitLocation.Torso, false, false, null, null, null,
            FateUse.None, false, null, rolls.Rolls.ToArray());

    // The shared skill check of the attack (ARC-SKILLS-003): the attacker's combat skill against the critter target.
    private static SkillCheckRequest AttackCheckRequest(AttackRequest request, bool magicCancelled)
    {
        var attacker = request.Attacker;
        var target = request.Target;
        var weapon = request.Weapon;
        var armed = weapon.Kind == WeaponKind.Weapon;
        var magic = armed && !magicCancelled;
        var item = weapon.Kind != WeaponKind.Unarmed;
        var trueBackstab = request.Backstab == BackstabKind.TrueBackstab;
        return new SkillCheckRequest
        {
            Skill = SkillRules.Of(request.Skill),
            Level = request.SkillLevel,
            Training = request.Training,
            Effectiveness = new()
            {
                TargetMagneticInversion = target.Conditions.HasFlag(CombatantConditions.MagneticInversion),
                ExtraordinaryIntelligence = attacker.ExtraordinaryIntelligence,
                LocalPc = attacker.IsPc,
                Difficulty = request.Difficulty,
            },
            Difficulty = new()
            {
                Modifier = request.TargetWasInBed ? -30 : 0,
                Weapon = armed,
                WeaponToHit = weapon.ToHitBonus + (magic ? weapon.MagicToHitAdjustment : 0),
                WeaponMinimumStrength = weapon.MinimumStrength,
                SourceStrength = attacker.Strength,
                Target = true,
                TargetCritter = true,
                TargetConditions = TargetConditionsOf(target.Conditions),
                TargetUnaware = request.TargetUnaware,
                TargetArmorClass = target.ArmorClass,
                TrueBackstabByTrained = trueBackstab && attacker.BackstabTraining != SkillTraining.None,
                AimedLocation = request.AimedLocation,
                Distance = request.Distance,
                Range = weapon.Range,
                SourcePerception = attacker.Perception,
                LineOfFireBlocked = request.LineOfFireBlocked,
                Cover = request.CoverRating,
                Light = target.LightLevel,
                SourceLight = attacker.LightEmission,
                SourceDarkSight = attacker.Conditions.HasFlag(CombatantConditions.DarkSight),
                SourceUndead = attacker.Conditions.HasFlag(CombatantConditions.Undead),
                SourceInjuries = InjuriesOf(attacker.Conditions),
            },
            Critical = new()
            {
                AimedLocation = request.AimedLocation,
                WeaponMagicCriticalHit = magic ? weapon.MagicCriticalHitChance : 0,
                WeaponMagicCriticalMiss = magic ? weapon.MagicCriticalMissChance : 0,
                Item = item,
                ItemHitPoints = item ? weapon.Hp : 0,
                ItemMaxHitPoints = item ? weapon.MaxHp : 0,
                BackstabTerm = trueBackstab
                    ? AttackRules.TrueBackstabCriticalBonus(attacker.BackstabLevel, attacker.BackstabTraining, target.Level)
                    : 0,
                CriticalHitEffects = Adjustment(attacker.CriticalHitChanceAdjustment),
                CriticalMissEffects = Adjustment(attacker.CriticalMissChanceAdjustment),
            },
        };
    }

    // The attack's difficulty terms; A2 shows in the invocation term (the source starts from the modifier and adds it again).
    private static AttackDifficulty AttackDifficultyOf(SkillDifficulty d, CombatAnomalyProfile profile) =>
        new(profile.InvocationModifierCountedTwice ? 2 * d.Modifier : d.Modifier, d.Weapon, d.Awareness, d.Aim, d.ArmorClass,
            d.Range, d.Cover, d.Light, d.Injury);

    // The dodge is the target's shared dodge check: only its blindness and crippled legs make it harder.
    private static SkillCheck DodgeCheck(Combatant target, GameDifficulty gameDifficulty, CombatRollLog rolls, CombatAnomalyProfile profile)
    {
        var request = new SkillCheckRequest
        {
            Skill = Skill.Dodge,
            Level = target.DodgeLevel,
            Training = target.DodgeTraining,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = target.ExtraordinaryIntelligence,
                LocalPc = target.IsPc,
                Difficulty = gameDifficulty,
            },
            Difficulty = new() { SourceInjuries = InjuriesOf(target.Conditions) },
            Critical = new()
            {
                CriticalHitEffects = Adjustment(target.CriticalHitChanceAdjustment),
                CriticalMissEffects = Adjustment(target.CriticalMissChanceAdjustment),
            },
        };
        var check = SkillCheckRules.Run(request, new CheckRolls(rolls, CombatRollPurpose.DodgeCheck, CombatRollPurpose.DodgeCritical),
            profile.InvocationModifierCountedTwice).Check;
        // The source rolls first and then fails the dodge of a critter that cannot move.
        return (target.Conditions & DodgeDisabled) != 0 ? check with { Success = false, Critical = false } : check;
    }

    private static SkillTargetConditions TargetConditionsOf(CombatantConditions c) =>
        (c.HasFlag(CombatantConditions.Paralyzed) ? SkillTargetConditions.Paralyzed : 0)
        | (c.HasFlag(CombatantConditions.Unconscious) ? SkillTargetConditions.Unconscious : 0)
        | (c.HasFlag(CombatantConditions.FallingDown) ? SkillTargetConditions.FallingDown : 0)
        | (c.HasFlag(CombatantConditions.Stunned) ? SkillTargetConditions.Stunned : 0)
        | (c.HasFlag(CombatantConditions.Sleeping) ? SkillTargetConditions.Sleeping : 0);

    private static SkillSourceInjuries InjuriesOf(CombatantConditions c) =>
        (c.HasFlag(CombatantConditions.Blinded) ? SkillSourceInjuries.Blinded : 0)
        | (c.HasFlag(CombatantConditions.CrippledArmOne) ? SkillSourceInjuries.CrippledArmOne : 0)
        | (c.HasFlag(CombatantConditions.CrippledArmsBoth) ? SkillSourceInjuries.CrippledArmsBoth : 0)
        | (c.HasFlag(CombatantConditions.CrippledLegsBoth) ? SkillSourceInjuries.CrippledLegsBoth : 0);

    private static IReadOnlyList<EffectModifier> Adjustment(int value) =>
        value == 0 ? [] : [new EffectModifier(StatModifierOperator.Add, value)];

    private sealed class CheckRolls(CombatRollLog log, CombatRollPurpose check, CombatRollPurpose critical) : ISkillCheckRolls
    {
        public int D100(SkillCheckRoll roll) => log.D100(roll == SkillCheckRoll.Check ? check : critical);
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

        return Step(recipientRole, recipient, recipientKind, rolled, weapon, attacker.IsPc, request.Difficulty, crit,
            critical && recipientIsCritter
                ? () => RollCriticalHit(request, recipient, location, aimed, magicCancelled, selfHit, crit, criticalEffects, rolls)
                : null,
            fullAmount: null);
    }

    /// <summary>
    /// ARC-MAGIC-012: one damage step without an attack (the original's damage step 0x4B4390 as the spell Damage component
    /// 0x4518D0 calls it, contract docs/world/spell-damage-heal.md): no roll, no critical procedure (the context has no critical
    /// flag), the request's damage per type and its effects (the damage flags). The same step as an attack's (one code path):
    /// poison immunity of undead and mechanical critters, a stoned critter takes nothing, an invulnerable target, a
    /// nigh-invulnerable critter without an "uber" weapon in the context and a wall take nothing; then the resistances (none
    /// with an ignoring weapon), the hit-point amount (normal + electrical + fire, or <see cref="DamageStepRequest.FullAmount"/>
    /// for the full and death flags), a PC attacker's difficulty share, the fatigue and poison of a critter and the material
    /// reduction of a portal or container. Applying the result is the caller's (<see cref="LifeDamage.FromStep"/>).
    /// </summary>
    public static AttackDamage DamageStep(DamageStepRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var crit = new CriticalAccumulator { HitEffects = request.Effects };
        return Step(DamageRecipient.Target, request.Target, request.TargetKind, request.Damage, request.Weapon, request.Attacker.IsPc,
            request.Difficulty, crit, null, request.FullAmount, request.DifficultyScalesFullAmount);
    }

    // The damage step after the rolls (0x4B4390 from its gates): shared by an attack and DamageStep.
    private static AttackDamage Step(DamageRecipient recipientRole, Combatant recipient, AttackTargetKind recipientKind, DamageByType rolled,
        CombatWeapon weapon, bool attackerIsPc, GameDifficulty difficulty, CriticalAccumulator crit, Action? critical, int? fullAmount,
        bool difficultyScalesFullAmount = true)
    {
        var recipientIsCritter = recipientKind == AttackTargetKind.Critter;
        var conditions = recipientIsCritter ? recipient.Conditions : CombatantConditions.None;
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

        critical?.Invoke();

        if (!(weapon.Kind == WeaponKind.Weapon && weapon.IgnoresResistance))
        {
            foreach (var type in Enum.GetValues<DamageType>())
            {
                var resistance = Resistance(recipient.Resistances, type);
                if (resistance > 0) damage = damage.With(type, damage[type] - (int)((long)resistance * damage[type] / 100));
            }
        }

        // The full and death flags (0x1, 0x4) take the target's current hit points instead of the sum.
        var hitPoints = fullAmount ?? damage.Normal + damage.Electrical + damage.Fire;
        var damagesArmor = damage.Fire > 0 || (weapon.Kind == WeaponKind.Weapon && weapon.DamagesArmor);
        if (attackerIsPc && (fullAmount is null || difficultyScalesFullAmount))
        {
            hitPoints = difficulty switch
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
        // A8: the original uses 0 for this chance when the weapon's magic was cancelled, as here.
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
        Check(weapon.ArtSubtype is >= 0 and <= CombatWearRules.MaxArtSubtype, "Weapon.ArtSubtype");

        ValidateCombatant(request.Attacker, "Attacker");
        ValidateCombatant(request.Target, "Target");
    }

    private static void Validate(DamageStepRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Attacker, nameof(DamageStepRequest.Attacker));
        ArgumentNullException.ThrowIfNull(request.Target, nameof(DamageStepRequest.Target));
        ArgumentNullException.ThrowIfNull(request.Weapon, nameof(DamageStepRequest.Weapon));
        Check(Enum.IsDefined(request.TargetKind), "TargetKind");
        Check(Enum.IsDefined(request.Difficulty), "Difficulty");
        Check(Enum.IsDefined(request.Weapon.Kind), "Weapon.Kind");
        Check((request.Effects & ~AllEffects) == 0, "Effects");
        foreach (var type in Enum.GetValues<DamageType>()) Check(Math.Abs(request.Damage[type]) <= MaxMagnitude, $"Damage.{type}");
        Check(request.FullAmount is null or >= -MaxMagnitude and <= MaxMagnitude, "FullAmount");
        ValidateCombatant(request.Attacker, "Attacker");
        ValidateCombatant(request.Target, "Target");
    }

    private static readonly CriticalHitEffects AllEffects =
        Enum.GetValues<CriticalHitEffects>().Aggregate(CriticalHitEffects.None, (all, flag) => all | flag);

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
            && c.Perception is >= 0 and <= MaxMagnitude && c.Constitution is >= 0 and <= MaxMagnitude, $"{name} level and stats");
        Check(c.ConstitutionMaximum is >= 1 and <= MaxMagnitude, $"{name}.ConstitutionMaximum");
        Check(c.CriticalBody is >= 0 and <= MaxMagnitude, $"{name}.CriticalBody");
        Check((c.Worn & ~AllWorn) == 0, $"{name}.Worn");
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
