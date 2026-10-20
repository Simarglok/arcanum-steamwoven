using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>
/// The shared skill check (ARC-SKILLS-003), checked in the original 1.0.7.4 executable by two readers (ledger SC1–SC12):
/// effectiveness from a per-skill formula or the opposed table, the difficulty of one invocation from explicit facts, the
/// d100 roll against them, the critical chances of a success and of a failure, a forced success and the PC's fate. The attack
/// resolver uses it for the attack and the dodge. Pure functions over explicit values; the inputs are validated
/// (<see cref="ArgumentException"/>).
/// </summary>
public static class SkillCheckRules
{
    /// <summary>The source's "impossible" difficulty: out of range, a blocked line of fire, a lock on anything but a portal or a
    /// container.</summary>
    public const int Impossible = 1_000_000;
    /// <summary>The cap of a non-combat skill's effectiveness.</summary>
    public const int EffectivenessCap = 95;
    /// <summary>Bound of the magnitude of a fact or an effectiveness these rules read (project policy).</summary>
    public const int MaxValue = 1_000_000;
    /// <summary>Bound of the effect changes of one critical chance (project policy).</summary>
    public const int MaxCriticalEffects = 64;

    // The source's per-skill flags (its tables at 0x5B6F64 and 0x5B6F94), in Skill order.
    [Flags]
    private enum SkillFlags
    {
        None = 0, Location = 0x1, Distance = 0x2, Light = 0x4, Awareness = 0x8, OpposedPerception = 0x10, OpposedIntelligence = 0x20,
        OpposedSkill = 0x40, Eyes = 0x80, Arms = 0x100, Legs = 0x200, Combat = 0x400, Capped = 0x800,
    }

    private static readonly SkillFlags[] Flags =
    [
        (SkillFlags)0x58F, (SkillFlags)0xE80, (SkillFlags)0x58D, (SkillFlags)0x58F, (SkillFlags)0x880, (SkillFlags)0x998,
        (SkillFlags)0x298, (SkillFlags)0x884, (SkillFlags)0x8C0, (SkillFlags)0x800, (SkillFlags)0x980, (SkillFlags)0x820,
        (SkillFlags)0x980, (SkillFlags)0x58F, (SkillFlags)0x984, (SkillFlags)0x984,
    ];

    // The opposed table (0x5B7044..0x5B708C) for a level difference of −9..+9; the value is a quarter of each entry.
    private static readonly int[] OpposedTable = [55, 66, 78, 91, 105, 120, 136, 153, 171, 190, 209, 227, 244, 260, 275, 289, 302, 314, 325];
    // Pick pocket: the penalty of a stolen item in each worn slot 1000..1008 (the source's table at 0x5B70E4).
    private static readonly int[] WornSlotPenalties = [50, 30, 30, 30, 70, 70, 1000, 1000, 1000];
    private const int FirstWornSlot = 1000, LastWornSlot = 1008, MaxWornArmor = LastWornSlot - FirstWornSlot + 1;

    /// <summary>What an opposed skill is measured against: pick pocket and prowling the target's perception, persuasion its
    /// intelligence, gambling its gambling level; none for the others.</summary>
    public static SkillOpponent Opponent(Skill skill)
    {
        var flags = Of(skill);
        return flags.HasFlag(SkillFlags.OpposedPerception) ? SkillOpponent.Perception
            : flags.HasFlag(SkillFlags.OpposedIntelligence) ? SkillOpponent.Intelligence
            : flags.HasFlag(SkillFlags.OpposedSkill) ? SkillOpponent.SameSkill : SkillOpponent.None;
    }

    /// <summary>Bow, dodge, melee, throwing and firearms: the critical divisors 20 and 7 instead of 2.</summary>
    public static bool IsCombat(Skill skill) => Of(skill).HasFlag(SkillFlags.Combat);

    /// <summary>The fate kind a PC's check of the skill can use: gambling, heal, pick pocket, repair, pick locks, disarm traps.</summary>
    public static SkillFate Fate(Skill skill) => Defined(skill) switch
    {
        Skill.Gambling => SkillFate.Gambling,
        Skill.Heal => SkillFate.Heal,
        Skill.PickPocket => SkillFate.PickPocket,
        Skill.Repair => SkillFate.Repair,
        Skill.PickLocks => SkillFate.PickLocks,
        Skill.DisarmTraps => SkillFate.DisarmTraps,
        _ => SkillFate.None,
    };

    /// <summary>
    /// The opposed effectiveness: 100 without an opponent; otherwise from the difference of the own level and the opponent's
    /// value (<see cref="Opponent"/>): 100 from +10, 0 from −10, between them a quarter of the source's table entry.
    /// </summary>
    [OriginalFunction(0x4C6410, OriginalFunctionScope.Partial)]
    public static int Opposed(int level, int? opponentValue)
    {
        Check(level, nameof(level));
        if (opponentValue is not { } opponent) return 100;
        Check(opponent, nameof(opponentValue));
        var difference = level - opponent;
        if (difference >= 10) return 100;
        if (difference <= -10) return 0;
        return OpposedTable[difference + 9] / 4;
    }

    /// <summary>
    /// A skill's effectiveness at <paramref name="level"/>: the opposed table for pick pocket, prowling, gambling and persuasion
    /// (<see cref="Opposed"/>), else the source's formula: bow, melee 5L + 25; dodge 2L; throwing 7L + 25; spot trap 4L; haggle
    /// 4L + 10; heal 5L; repair 4L; firearms 5L + 25, −20 against a target in Magnetic Inversion; pick locks 6L; disarm traps
    /// 4L + 20 but 0 at level 0; backstab 0 (the prowling formula is unreachable, SK6). Then +10 for extraordinary
    /// intelligence, the local PC's game difficulty (easy + half, hard − a quarter, truncated toward zero), the 95 cap of the
    /// non-combat skills, at least 0.
    /// </summary>
    [OriginalFunction(0x4C62E0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C69F0, OriginalFunctionScope.Partial)]
    public static int Effectiveness(Skill skill, int level, SkillEffectivenessFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (level is < 0 or > SkillRules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        if (!Enum.IsDefined(facts.Difficulty)) throw new ArgumentOutOfRangeException(nameof(facts));
        var value = Opponent(skill) != SkillOpponent.None ? Opposed(level, facts.OpponentValue) : skill switch
        {
            Skill.Bow or Skill.Melee or Skill.Firearms => 5 * level + 25,
            Skill.Dodge => 2 * level,
            Skill.Throwing => 7 * level + 25,
            Skill.SpotTrap or Skill.Repair => 4 * level,
            Skill.Haggle => 4 * level + 10,
            Skill.Heal => 5 * level,
            Skill.PickLocks => 6 * level,
            Skill.DisarmTraps => level == 0 ? 0 : 4 * level + 20,
            _ => 0,
        };
        if (skill == Skill.Firearms && facts.TargetMagneticInversion) value -= 20;
        if (facts.ExtraordinaryIntelligence) value += 10;
        if (facts.LocalPc)
        {
            if (facts.Difficulty == GameDifficulty.Easy) value += value / 2;
            else if (facts.Difficulty == GameDifficulty.Hard) value -= value / 4;
        }
        if (Of(skill).HasFlag(SkillFlags.Capped) && value > EffectivenessCap) return EffectivenessCap;
        return Math.Max(0, value);
    }

    /// <summary>
    /// The difficulty of one check in the source's order: the modifier; with a weapon minus its to-hit bonus, plus 5 per point
    /// of missing strength; against a critter, for the skills that read awareness, −50 paralysed, −50 unconscious, −30 falling
    /// or stunned, −50 asleep, else −30 when it cannot notice the source (not for prowling); against any target, for the skills
    /// with a hit location, an aimed location's penalty (two thirds for an expert in firearms) and the armour class share
    /// effectiveness × (AC / 2) / 100 unless a trained backstabber's true backstab; for bow, throwing and firearms, impossible
    /// beyond an item's range or with a blocked line of fire, +5 per cell beyond perception / 2 except for a master, plus the cover; darkness
    /// <see cref="LightPenalty"/>; the injuries (blinded +30 for the skills that use the eyes, both arms +50 or one +20 for the
    /// arms, both legs +30 for the legs); then the skill's own term: pick pocket a worn slot's penalty and 5 × width × height
    /// (halved for an expert), prowling −100 in all for a hidden prowler, else minus its cover (+50 without cover below expert) in a
    /// seeing check and the worn armour's silent-move adjustments (each halved from apprentice) in a hearing check, heal
    /// impossible without a healing item, else its bonus, pick locks impossible on anything but a portal or container, the lockpick's bonus and the lock's
    /// difficulty (halved for a master), disarm traps a trap's difficulty.
    /// </summary>
    [OriginalFunction(0x4C8430, OriginalFunctionScope.Partial)]
    public static SkillDifficulty Difficulty(Skill skill, SkillTraining training, int effectiveness, SkillDifficultyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var flags = Of(skill);
        if (!SkillRules.IsDefined(training)) throw new ArgumentOutOfRangeException(nameof(training));
        Check(effectiveness, nameof(effectiveness));
        Validate(facts);

        var weapon = 0;
        if (facts.Weapon)
        {
            weapon -= facts.WeaponToHit;
            if (facts.SourceStrength < facts.WeaponMinimumStrength) weapon += 5 * (facts.WeaponMinimumStrength - facts.SourceStrength);
        }

        var awareness = 0;
        if (flags.HasFlag(SkillFlags.Awareness) && facts.TargetCritter)
        {
            var conditions = facts.TargetConditions;
            if (conditions.HasFlag(SkillTargetConditions.Paralyzed) || conditions.HasFlag(SkillTargetConditions.Unconscious)) awareness = -50;
            else if ((conditions & (SkillTargetConditions.FallingDown | SkillTargetConditions.Stunned)) != 0) awareness = -30;
            else if (conditions.HasFlag(SkillTargetConditions.Sleeping)) awareness = -50;
            else if (skill != Skill.Prowling && facts.TargetUnaware) awareness = -30;
        }

        int aim = 0, armor = 0;
        if (flags.HasFlag(SkillFlags.Location) && facts.Target)
        {
            if (facts.AimedLocation is { } aimed)
            {
                var penalty = AttackRules.HitLocationPenalty(aimed);
                if (skill == Skill.Firearms && training >= SkillTraining.Expert) penalty = 2 * penalty / 3;
                aim = -penalty;
            }
            if (!facts.TrueBackstabByTrained) armor = (int)((long)effectiveness * (facts.TargetArmorClass / 2) / 100);
        }

        int range = 0, cover = 0;
        if (flags.HasFlag(SkillFlags.Distance))
        {
            if (facts.Range is { } reach && facts.Distance > reach) range += Impossible;
            if (training < SkillTraining.Master)
            {
                var extra = facts.Distance - facts.SourcePerception / 2;
                if (extra > 0) range += 5 * extra;
            }
            if (facts.LineOfFireBlocked) range += Impossible;
            else if (facts.Cover > 0) cover = facts.Cover;
        }

        var light = LightPenalty(skill, training, facts);

        var injury = 0;
        if (flags.HasFlag(SkillFlags.Eyes) && facts.SourceInjuries.HasFlag(SkillSourceInjuries.Blinded)) injury += 30;
        if (flags.HasFlag(SkillFlags.Arms))
        {
            if (facts.SourceInjuries.HasFlag(SkillSourceInjuries.CrippledArmsBoth)) injury += 50;
            else if (facts.SourceInjuries.HasFlag(SkillSourceInjuries.CrippledArmOne)) injury += 20;
        }
        if (flags.HasFlag(SkillFlags.Legs) && facts.SourceInjuries.HasFlag(SkillSourceInjuries.CrippledLegsBoth)) injury += 30;

        var own = 0;
        var hidden = false;
        switch (skill)
        {
            case Skill.PickPocket:
                if (facts.ItemLocation is >= FirstWornSlot and <= LastWornSlot) own += WornSlotPenalties[facts.ItemLocation - FirstWornSlot];
                var size = 5 * facts.ItemWidth * facts.ItemHeight;
                own += training >= SkillTraining.Expert ? size / 2 : size;
                break;
            case Skill.Prowling when facts.SeeingCheck:
                if (facts.ProwlerHidden) hidden = true;
                else own -= training < SkillTraining.Expert && facts.ProwlerCover == 0 ? -50 : facts.ProwlerCover;
                break;
            case Skill.Prowling when facts.HearingCheck:
                foreach (var silent in facts.ArmorSilentMoves) own -= training >= SkillTraining.Apprentice ? silent / 2 : silent;
                break;
            case Skill.Heal:
                if (facts.HealingItem) own -= facts.HealingItemBonus;
                else own += Impossible;
                break;
            case Skill.PickLocks:
                if (!facts.TargetLockable) own += Impossible;
                else
                {
                    own -= facts.LockpickBonus;
                    own += training >= SkillTraining.Master && facts.LockDifficulty > 0 ? facts.LockDifficulty / 2 : facts.LockDifficulty;
                }
                break;
            case Skill.DisarmTraps:
                if (facts.TargetTrap) own += facts.TrapDifficulty;
                break;
        }
        return new(facts.Modifier, weapon, awareness, aim, armor, range, cover, light, injury, own, hidden);
    }

    /// <summary>
    /// The darkness term: none for an undead source, for the skills without the light flag (bow, melee, throwing, firearms,
    /// spot trap, pick locks, disarm traps have it) unless a seeing check, for an expert in melee or pick locks, and for a
    /// trained source in spot trap, disarm traps or a seeing check. Otherwise 30 × (255 − light) / 255, divided by the source's
    /// own light (extra large ÷4, large ÷3, medium ÷2, small × ¾), replaced by 30 − it when the source has dark sight (in a
    /// seeing check: when the observer has none); a result of 0 or less adds nothing.
    /// </summary>
    public static int LightPenalty(Skill skill, SkillTraining training, SkillDifficultyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.SourceUndead) return 0;
        if (!Of(skill).HasFlag(SkillFlags.Light) && !facts.SeeingCheck) return 0;
        if (skill is Skill.Melee or Skill.PickLocks && training >= SkillTraining.Expert) return 0;
        if ((facts.SeeingCheck || skill is Skill.SpotTrap or Skill.DisarmTraps) && training >= SkillTraining.Apprentice) return 0;
        var penalty = (255 - facts.Light) * 30 / 255;
        penalty = facts.SourceLight switch
        {
            LightEmission.None => penalty,
            LightEmission.Small => 3 * penalty / 4,
            LightEmission.Medium => penalty / 2,
            LightEmission.Large => penalty / 3,
            LightEmission.ExtraLarge => penalty / 4,
            _ => throw new ArgumentOutOfRangeException(nameof(facts)),
        };
        if (facts.SeeingCheck ? !facts.ObserverDarkSight : facts.SourceDarkSight) penalty = 30 - penalty;
        return Math.Max(0, penalty);
    }

    /// <summary>
    /// The critical chance of a success: effectiveness / 20 for a combat skill, / 2 otherwise; an aimed location's penalty / −5;
    /// the item's magic critical chance; the backstab term; then the source's effect changes.
    /// </summary>
    [OriginalFunction(0x4C81A0, OriginalFunctionScope.Partial)]
    public static int CriticalSuccessChance(Skill skill, int effectiveness, SkillCriticalFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        Check(effectiveness, nameof(effectiveness));
        Validate(facts);
        var chance = effectiveness / (IsCombat(skill) ? 20 : 2);
        if (facts.AimedLocation is { } aimed) chance += AttackRules.HitLocationPenalty(aimed) / -5;
        chance += facts.WeaponMagicCriticalHit + facts.BackstabTerm;
        return EffectModifiers.Apply(chance, facts.CriticalHitEffects);
    }

    /// <summary>
    /// The critical chance of a failure: (100 − effectiveness) / 7 for a combat skill, / 2 otherwise; an item below its maximum
    /// hit points adds 100 × (max − hp) / (max × the same divisor); the item's magic critical miss chance; then the source's
    /// effect changes; at least 2.
    /// </summary>
    [OriginalFunction(0x4C82E0, OriginalFunctionScope.Partial)]
    public static int CriticalFailureChance(Skill skill, int effectiveness, SkillCriticalFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        Check(effectiveness, nameof(effectiveness));
        Validate(facts);
        if (facts.ItemHitPoints < 0 || facts.ItemMaxHitPoints < 0 || facts.ItemHitPoints > facts.ItemMaxHitPoints)
            throw new ArgumentOutOfRangeException(nameof(facts));
        var divisor = IsCombat(skill) ? 7 : 2;
        var chance = (100 - effectiveness) / divisor;
        if (facts.Item && facts.ItemHitPoints < facts.ItemMaxHitPoints)
            chance += (int)(100L * (facts.ItemMaxHitPoints - facts.ItemHitPoints) / ((long)facts.ItemMaxHitPoints * divisor));
        chance += facts.WeaponMagicCriticalMiss;
        return Math.Max(2, EffectModifiers.Apply(chance, facts.CriticalMissEffects));
    }

    /// <summary>
    /// Runs one check (the generic part of the source's run): effectiveness, difficulty (with the modifier counted again under
    /// <paramref name="modifierCountedTwice"/>, the original's A2), the check roll (gambling uses its stored roll and keeps the
    /// fresh one for the next check), success
    /// when roll + difficulty ≤ effectiveness or forced, the critical roll against <see cref="CriticalSuccessChance"/> or
    /// <see cref="CriticalFailureChance"/> (a master of melee never fails critically), and an armed fate point of the skill's
    /// kind making it a critical success. The per-skill handlers (what a success does) are the uses'.
    /// </summary>
    [OriginalFunction(0x4C7160, OriginalFunctionScope.Partial)]
    public static SkillCheckResult Run(SkillCheckRequest request, ISkillCheckRolls rolls, bool modifierCountedTwice)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rolls);
        var gambling = request.Skill == Skill.Gambling;
        if (gambling ? request.GamblingRoll is not (>= 1 and <= 100) : request.GamblingRoll is not null)
            throw new ArgumentOutOfRangeException(nameof(request));
        var effectiveness = Effectiveness(request.Skill, request.Level, request.Effectiveness);
        var difficulty = Difficulty(request.Skill, request.Training, effectiveness, request.Difficulty);
        var total = (int)Math.Clamp(difficulty.Total + (modifierCountedTwice ? (long)request.Difficulty.Modifier : 0), int.MinValue, int.MaxValue);
        // Gambling uses the stored roll and stores the one it draws for the next check.
        var drawn = Roll(rolls, SkillCheckRoll.Check);
        var roll = gambling ? request.GamblingRoll!.Value : drawn;
        var success = request.Forced || (long)roll + total <= effectiveness;
        var criticalRoll = Roll(rolls, SkillCheckRoll.Critical);
        int chance;
        bool critical;
        if (success)
        {
            chance = CriticalSuccessChance(request.Skill, effectiveness, request.Critical);
            critical = criticalRoll <= chance;
        }
        else if (request.Skill == Skill.Melee && request.Training >= SkillTraining.Master)
        {
            chance = 0;
            critical = false;
        }
        else
        {
            chance = CriticalFailureChance(request.Skill, effectiveness, request.Critical);
            critical = criticalRoll <= chance;
        }
        var fated = request.FateArmed && Fate(request.Skill) != SkillFate.None;
        if (fated) (success, critical) = (true, true);
        return new(new SkillCheck(effectiveness, total, roll, success, chance, criticalRoll, critical), difficulty, fated,
            gambling ? drawn : null);
    }

    private static int Roll(ISkillCheckRolls rolls, SkillCheckRoll which)
    {
        var value = rolls.D100(which);
        return value is >= 1 and <= 100 ? value : throw new InvalidOperationException($"A skill check roll of {value} is outside 1..100.");
    }

    private static SkillFlags Of(Skill skill) => Flags[(int)Defined(skill)];

    private static Skill Defined(Skill skill) => SkillRules.IsDefined(skill) ? skill : throw new ArgumentOutOfRangeException(nameof(skill));

    private static void Validate(SkillDifficultyFacts f)
    {
        ArgumentNullException.ThrowIfNull(f.ArmorSilentMoves);
        if (f.ArmorSilentMoves.Count > MaxWornArmor) throw new ArgumentOutOfRangeException(nameof(f));
        int[] values = [f.Modifier, f.WeaponToHit, f.WeaponMinimumStrength, f.SourceStrength, f.TargetArmorClass, f.Distance,
            f.SourcePerception, f.Cover, f.ItemLocation, f.ProwlerCover, f.HealingItemBonus, f.LockpickBonus, f.LockDifficulty,
            f.TrapDifficulty, .. f.ArmorSilentMoves];
        foreach (var value in values) Check(value, nameof(f));
        if (f.Range is { } range) Check(range, nameof(f));
        if (f.Light is < 0 or > 255 || f.ItemWidth is < 0 or > 64 || f.ItemHeight is < 0 or > 64 || f.Distance < 0 ||
            f.TargetCritter && !f.Target || !Enum.IsDefined(f.SourceLight) || f.AimedLocation is { } location && !Enum.IsDefined(location) ||
            (f.TargetConditions & ~AllConditions) != 0 || (f.SourceInjuries & ~AllInjuries) != 0)
            throw new ArgumentOutOfRangeException(nameof(f));
    }

    private const SkillTargetConditions AllConditions = SkillTargetConditions.Paralyzed | SkillTargetConditions.Unconscious
        | SkillTargetConditions.FallingDown | SkillTargetConditions.Stunned | SkillTargetConditions.Sleeping;
    private const SkillSourceInjuries AllInjuries = SkillSourceInjuries.Blinded | SkillSourceInjuries.CrippledArmOne
        | SkillSourceInjuries.CrippledArmsBoth | SkillSourceInjuries.CrippledLegsBoth;

    private static void Validate(SkillCriticalFacts f)
    {
        if (f.AimedLocation is { } location && !Enum.IsDefined(location) ||
            !EffectModifiers.IsValid(f.CriticalHitEffects, MaxCriticalEffects, MaxValue) ||
            !EffectModifiers.IsValid(f.CriticalMissEffects, MaxCriticalEffects, MaxValue))
            throw new ArgumentOutOfRangeException(nameof(f));
        Check(f.WeaponMagicCriticalHit, nameof(f));
        Check(f.WeaponMagicCriticalMiss, nameof(f));
        Check(f.BackstabTerm, nameof(f));
        Check(f.ItemMaxHitPoints, nameof(f));
    }

    private static void Check(int value, string name)
    {
        if (Math.Abs((long)value) > MaxValue) throw new ArgumentOutOfRangeException(name);
    }
}
