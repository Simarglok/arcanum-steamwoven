using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>
/// The state rules of the sixteen skills (ARC-SKILLS-001), checked in the original 1.0.7.4 executable (ledger FA7–FA9, AL5
/// and SR1–SR9). A critter stores one value per skill: points in bits 0..5, training in bits 6..7, any higher bits kept as they
/// are. The level is four per point (20 for the melee of a monstrous critter: mechanical, monster, animal or undead), then the
/// skill's effect changes, clamped to 0..20, then capped by the key stat. Points are bought one at a time while four times the
/// points stays within that cap; training rises one tier at a time and needs a level of 0, 1, 9 or 18. A tech skill's points
/// are also tech points. Pure functions over explicit values: the reads validate their inputs
/// (<see cref="ArgumentException"/>), the changes and the stat guard answer typed results.
/// </summary>
public static class SkillRules
{
    public const int Count = 16, BasicCount = 12, TechCount = 4;
    /// <summary>The highest skill level.</summary>
    public const int MaxLevel = 20;
    /// <summary>The level one skill point buys.</summary>
    public const int LevelPerPoint = 4;
    /// <summary>The most points a key stat can allow (4 × 5 = 20).</summary>
    public const int MaxPoints = 5;
    /// <summary>The melee level a monstrous critter starts from instead of its points.</summary>
    public const int MonstrousMeleeLevel = 20;
    /// <summary>The point bits of a stored value.</summary>
    public const int PointMask = 0x3F;
    /// <summary>Bound of the magnitude of a stored value, points, a stat or a level these rules read (project policy).</summary>
    public const int MaxValue = 1_000_000;

    private const int TrainingMask = 0xC0, TrainingShift = 6;

    // The original's key stats (its tables at 0x5B6F04 and 0x5B6F34): dexterity for the six physical basic skills, perception
    // for prowling and spot trap, then intelligence, willpower, intelligence, charisma; intelligence, perception, dexterity,
    // perception for the tech skills.
    private static readonly CharacterStat[] KeyStats =
    [
        CharacterStat.Dexterity, CharacterStat.Dexterity, CharacterStat.Dexterity, CharacterStat.Dexterity, CharacterStat.Dexterity,
        CharacterStat.Dexterity, CharacterStat.Perception, CharacterStat.Perception, CharacterStat.Intelligence, CharacterStat.Willpower,
        CharacterStat.Intelligence, CharacterStat.Charisma,
        CharacterStat.Intelligence, CharacterStat.Perception, CharacterStat.Dexterity, CharacterStat.Perception,
    ];
    // The original's cap per key stat 1..20 (its table at 0x5B6FA4).
    private static readonly int[] KeyStatCaps = [3, 3, 3, 3, 3, 7, 7, 7, 11, 11, 11, 15, 15, 15, 19, 19, 19, 20, 20, 20];
    // The level each training tier needs (its table at 0x5B6F44): none 0, apprentice 1, expert 9, master 18.
    private static readonly int[] TierMinimumLevels = [0, 1, 9, 18];
    // Monstrous melee training: the character level each tier needs besides the tier's minimum melee level (0x5B6F58).
    private static readonly int[] MonstrousTierCharacterLevels = [10, 20, 30];

    public static bool IsDefined(Skill skill) => skill is >= Skill.Bow and <= Skill.DisarmTraps;

    public static bool IsDefined(SkillTraining training) => training is >= SkillTraining.None and <= SkillTraining.Master;

    /// <summary>Repair, firearms, pick locks and disarm traps.</summary>
    public static bool IsTech(Skill skill) => Defined(skill) >= Skill.Repair;

    /// <summary>The basic skill with the source's basic-skill index 0..11.</summary>
    public static Skill Basic(int index) => index is >= 0 and < BasicCount ? (Skill)index : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>The tech skill with the source's tech-skill index 0..3.</summary>
    public static Skill Tech(int index) => index is >= 0 and < TechCount ? (Skill)(BasicCount + index) : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>The skill an attack with a weapon skill reads: melee, bow, throwing, or the tech skill firearms.</summary>
    public static Skill Of(CombatSkill skill) => skill switch
    {
        CombatSkill.Melee => Skill.Melee,
        CombatSkill.Bow => Skill.Bow,
        CombatSkill.Throwing => Skill.Throwing,
        CombatSkill.Firearms => Skill.Firearms,
        _ => throw new ArgumentOutOfRangeException(nameof(skill)),
    };

    /// <summary>The source's index of the skill in its family: 0..11 for a basic skill, 0..3 for a tech skill.</summary>
    public static int FamilyIndex(Skill skill) => IsTech(skill) ? (int)skill - BasicCount : (int)skill;

    /// <summary>The skill's key stat (the source's two key-stat tables).</summary>
    [OriginalFunction(0x4C64D0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C6B10, OriginalFunctionScope.Full)]
    public static CharacterStat KeyStat(Skill skill) => KeyStats[(int)Defined(skill)];

    /// <summary>The character points one point of the skill costs: 1.</summary>
    [OriginalFunction(0x4C64B0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C6AF0, OriginalFunctionScope.Full)]
    public static int PointCost(Skill skill)
    {
        Defined(skill);
        return 1;
    }

    /// <summary>The character points removing one point of the skill refunds: 1.</summary>
    [OriginalFunction(0x4C64C0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C6B00, OriginalFunctionScope.Full)]
    public static int PointRefund(Skill skill)
    {
        Defined(skill);
        return 1;
    }

    /// <summary>The points of a stored value (bits 0..5).</summary>
    [OriginalFunction(0x4C5FA0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C6680, OriginalFunctionScope.Full)]
    public static int Points(int stored) => Check(stored, nameof(stored)) & PointMask;

    /// <summary>The training of a stored value (bits 6..7).</summary>
    public static SkillTraining StoredTraining(int stored) => (SkillTraining)((Check(stored, nameof(stored)) >> TrainingShift) & 3);

    /// <summary>The level a skill's points give before effects: four per point.</summary>
    [OriginalFunction(0x4C5E50, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C6580, OriginalFunctionScope.Full)]
    public static int BaseLevel(int stored) => LevelPerPoint * Points(stored);

    /// <summary>The highest level a key stat allows: the original's table for the stat clamped to 1..20.</summary>
    [OriginalFunction(0x4C5F70, OriginalFunctionScope.Full)]
    public static int KeyStatCap(int keyStat) => KeyStatCaps[Math.Clamp(Check(keyStat, nameof(keyStat)), 1, 20) - 1];

    /// <summary>The lowest key stat (1..20) whose cap reaches <paramref name="level"/>, or 0 when none does (a level above 20):
    /// what the character sheet's hint names for the next point.</summary>
    [OriginalFunction(0x4C64E0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C6B20, OriginalFunctionScope.Full)]
    public static int MinimumKeyStat(int level)
    {
        Check(level, nameof(level));
        for (var stat = 1; stat <= 20; stat++)
            if (KeyStatCaps[stat - 1] >= level) return stat;
        return 0;
    }

    /// <summary>The level a training tier needs: 0, 1, 9, 18.</summary>
    [OriginalFunction(0x4C69C0, OriginalFunctionScope.Full)]
    public static int MinimumLevel(SkillTraining training) => IsDefined(training) ? TierMinimumLevels[(int)training]
        : throw new ArgumentOutOfRangeException(nameof(training));

    /// <summary>
    /// A skill's effective level: four per stored point (20 for a monstrous critter's melee), then <paramref name="effects"/>
    /// (the critter's effect changes of this skill, aggregated as every effect is, <see cref="EffectModifiers.Apply"/>), clamped
    /// to 0..20, then capped by <see cref="KeyStatCap"/> of the effective key stat <paramref name="keyStat"/>. A tech skill has
    /// no monstrous case.
    /// </summary>
    [OriginalFunction(0x4C5EB0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C65E0, OriginalFunctionScope.Partial)]
    public static int Level(Skill skill, int stored, int keyStat, bool monstrous, IReadOnlyList<EffectModifier>? effects = null)
    {
        var level = Defined(skill) == Skill.Melee && monstrous ? MonstrousMeleeLevel : BaseLevel(stored);
        if (effects is { Count: > 0 }) level = EffectModifiers.Apply(level, effects);
        return Math.Min(Math.Clamp(level, 0, MaxLevel), KeyStatCap(keyStat));
    }

    /// <summary>
    /// A skill's training: the stored bits, except a monstrous critter's melee, which takes the highest tier whose thresholds
    /// both its effective melee level (<see cref="Level"/>) and its character level reach in order (none below melee 1 or level
    /// 10, apprentice from 1 and 10, expert from 9 and 20, master from 18 and 30).
    /// </summary>
    [OriginalFunction(0x4C60C0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C67F0, OriginalFunctionScope.Full)]
    public static SkillTraining Training(Skill skill, int stored, bool monstrous, int meleeLevel, int characterLevel)
    {
        Check(meleeLevel, nameof(meleeLevel));
        Check(characterLevel, nameof(characterLevel));
        if (Defined(skill) != Skill.Melee || !monstrous) return StoredTraining(stored);
        var tier = 0;
        while (tier < MonstrousTierCharacterLevels.Length && meleeLevel >= TierMinimumLevels[tier + 1]
            && characterLevel >= MonstrousTierCharacterLevels[tier]) tier++;
        return (SkillTraining)tier;
    }

    /// <summary>
    /// Sets a skill's points as the source's point setters do: negative points are refused (<see cref="SkillChangeStatus.NegativePoints"/>,
    /// the source returns 0), points whose four levels each are above the cap of the effective key stat <paramref name="keyStat"/>
    /// are refused (<see cref="SkillChangeStatus.KeyStatTooLow"/>, the source returns the stored points); otherwise the point bits
    /// are replaced and the training and higher bits kept. For a tech skill the change also reports the tech points to add
    /// (new − old points). The level read for the cap ignores the skill's effects and the monstrous melee.
    /// </summary>
    [OriginalFunction(0x4C6000, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C66E0, OriginalFunctionScope.Partial)]
    public static SkillPointsChange SetPoints(Skill skill, int stored, int points, int keyStat)
    {
        if (!IsDefined(skill) || !Bounded(stored) || !Bounded(points) || !Bounded(keyStat)) return new(SkillChangeStatus.Invalid, stored, 0, 0);
        if (points < 0) return new(SkillChangeStatus.NegativePoints, stored, 0, 0);
        var old = stored & PointMask;
        if (LevelPerPoint * points > KeyStatCap(keyStat)) return new(SkillChangeStatus.KeyStatTooLow, stored, old, 0);
        return new(SkillChangeStatus.Changed, (stored & ~PointMask) | points, points, IsTech(skill) ? points - old : 0);
    }

    /// <summary>
    /// Sets a skill's training as the source's training setters do: a tier above the stored one must be the next one
    /// (<see cref="SkillChangeStatus.SkipsTier"/>); a lower or the same tier skips that rule; then the skill's effective
    /// <paramref name="level"/> (<see cref="Level"/>, effects and the monstrous melee included) must reach the tier's
    /// <see cref="MinimumLevel"/> (<see cref="SkillChangeStatus.LevelTooLow"/>), so none always succeeds. A change replaces the
    /// training bits and keeps the others; the caller runs the Educator background update after it.
    /// </summary>
    [OriginalFunction(0x4C6170, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4C6850, OriginalFunctionScope.Partial)]
    public static SkillTrainingChange SetTraining(Skill skill, int stored, SkillTraining training, int level)
    {
        if (!IsDefined(skill) || !IsDefined(training) || !Bounded(stored) || level is < 0 or > MaxLevel)
            return new(SkillChangeStatus.Invalid, stored, SkillTraining.None);
        var current = (SkillTraining)((stored >> TrainingShift) & 3);
        if (training > current && training - 1 != current) return new(SkillChangeStatus.SkipsTier, stored, current);
        if (level < TierMinimumLevels[(int)training]) return new(SkillChangeStatus.LevelTooLow, stored, current);
        return new(SkillChangeStatus.Changed, (stored & ~TrainingMask) | ((int)training << TrainingShift), training);
    }

    /// <summary>
    /// The source's guard against lowering a key stat: <paramref name="value"/> of <paramref name="stat"/> is refused when any skill
    /// keyed on that stat has a base level (<see cref="BaseLevel"/>: four per point, without effects or the monstrous melee) above
    /// <see cref="KeyStatCap"/> of the value. <paramref name="basic"/> holds the twelve basic and <paramref name="tech"/> the four
    /// tech stored values.
    /// </summary>
    [OriginalFunction(0x4C6B50, OriginalFunctionScope.Full)]
    public static SkillKeyStatCheck CheckKeyStat(CharacterStat stat, int value, IReadOnlyList<int>? basic, IReadOnlyList<int>? tech)
    {
        if (!CharacterStatRules.IsDefined(stat) || !Bounded(value) || basic is not { Count: BasicCount } || tech is not { Count: TechCount }
            || !basic.All(Bounded) || !tech.All(Bounded)) return new(SkillKeyStatStatus.Invalid, null);
        var cap = KeyStatCap(value);
        for (var skill = Skill.Bow; skill <= Skill.DisarmTraps; skill++)
        {
            var stored = IsTech(skill) ? tech[FamilyIndex(skill)] : basic[(int)skill];
            if (KeyStats[(int)skill] == stat && BaseLevel(stored) > cap) return new(SkillKeyStatStatus.SkillNeedsStat, skill);
        }
        return new(SkillKeyStatStatus.Allowed, null);
    }

    /// <summary>
    /// What the source's stat setter (0x4B0980) asks of the guard when a stored stat changes from <paramref name="currentBase"/>
    /// to <paramref name="newBase"/> (already clamped to the race's range): nothing unless the stat is lowered; then
    /// <see cref="CheckKeyStat"/> with <paramref name="effectiveAfter"/>, the effective value the new base gives. A refused
    /// change keeps the stored stat. Inputs outside the domain answer <see cref="SkillKeyStatStatus.Invalid"/> whichever way the
    /// stat moves.
    /// </summary>
    public static SkillKeyStatCheck CheckBaseChange(CharacterStat stat, int currentBase, int newBase, int effectiveAfter,
        IReadOnlyList<int>? basic, IReadOnlyList<int>? tech)
    {
        if (!Bounded(currentBase) || !Bounded(newBase)) return new(SkillKeyStatStatus.Invalid, null);
        var check = CheckKeyStat(stat, effectiveAfter, basic, tech);
        return check.Status == SkillKeyStatStatus.Invalid || newBase < currentBase ? check : new(SkillKeyStatStatus.Allowed, null);
    }

    private static Skill Defined(Skill skill) => IsDefined(skill) ? skill : throw new ArgumentOutOfRangeException(nameof(skill));

    private static bool Bounded(int value) => Math.Abs((long)value) <= MaxValue;

    private static int Check(int value, string name) => Bounded(value) ? value : throw new ArgumentOutOfRangeException(name);
}
