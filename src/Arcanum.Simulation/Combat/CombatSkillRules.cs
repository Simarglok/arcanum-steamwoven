using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Combat;

/// <summary>The five basic skills an attack reads, by the source's basic-skill index (bow 0 .. backstab 4).</summary>
public enum BasicCombatSkill { Bow = 0, Dodge = 1, Melee = 2, Throwing = 3, Backstab = 4 }

/// <summary>
/// The level and training of a basic combat skill (ARC-COMBAT-005; the source's <c>basic_skill_base</c>,
/// <c>basic_skill_level</c>, <c>max_skill_for_stat_level</c> and <c>basic_skill_training_get</c>, checked in the original
/// 1.0.7.4 executable, ledger rows FA7–FA9).
/// A stored skill keeps its points in bits 0..5 and its training in bits 6..7. The level is four per point, or 20 for the
/// melee skill of a monstrous critter (mechanical, monster, animal or undead), then the skill's effect changes, clamped to
/// 0..20 and capped by the key stat (dexterity for all five). Every function validates its inputs
/// (<see cref="ArgumentException"/>).
/// </summary>
public static class CombatSkillRules
{
    /// <summary>The highest skill level.</summary>
    public const int MaxLevel = 20;
    /// <summary>The level one skill point buys.</summary>
    public const int LevelPerPoint = 4;
    /// <summary>The melee level a monstrous critter starts from instead of its points.</summary>
    public const int MonstrousMeleeLevel = 20;
    /// <summary>Bound of the magnitude of a stored skill value or a stat these rules read.</summary>
    public const int MaxValue = 1_000_000;

    // The original's cap per key stat 1..20 (its table at 0x5B6FA4).
    private static readonly int[] KeyStatCaps = [3, 3, 3, 3, 3, 7, 7, 7, 11, 11, 11, 15, 15, 15, 19, 19, 19, 20, 20, 20];
    // Monstrous melee training: the effective melee level and the character level each tier needs (apprentice, expert,
    // master).
    private static readonly int[] MonstrousTierLevels = [1, 9, 18];
    private static readonly int[] MonstrousTierCharacterLevels = [10, 20, 30];

    /// <summary>The key stat of a basic combat skill: dexterity for all five.</summary>
    public static CharacterStat KeyStat(BasicCombatSkill skill) => Enum.IsDefined(skill)
        ? CharacterStat.Dexterity : throw new ArgumentOutOfRangeException(nameof(skill));

    /// <summary>The points of a stored skill value (bits 0..5).</summary>
    [OriginalFunction(0x4C5FA0, OriginalFunctionScope.Full)]
    public static int Points(int stored)
    {
        Check(stored, nameof(stored));
        return stored & 0x3F;
    }

    /// <summary>The training of a stored skill value (bits 6..7).</summary>
    public static SkillTraining StoredTraining(int stored)
    {
        Check(stored, nameof(stored));
        return (SkillTraining)((stored >> 6) & 3);
    }

    /// <summary>The level a skill's points give before effects: four per point.</summary>
    [OriginalFunction(0x4C5E50, OriginalFunctionScope.Full)]
    public static int BaseLevel(int stored) => LevelPerPoint * Points(stored);

    /// <summary>The highest level a key stat allows: the original's table for the stat clamped to 1..20.</summary>
    [OriginalFunction(0x4C5F70, OriginalFunctionScope.Full)]
    public static int KeyStatCap(int keyStat)
    {
        Check(keyStat, nameof(keyStat));
        return KeyStatCaps[Math.Clamp(keyStat, 1, 20) - 1];
    }

    /// <summary>
    /// A skill's effective level: four per stored point (20 for a monstrous critter's melee), then
    /// <paramref name="effects"/> (the critter's effect changes of this skill, aggregated as every effect is,
    /// <see cref="EffectModifiers.Apply"/>), clamped to 0..20, then capped by <see cref="KeyStatCap"/> of the effective key
    /// stat.
    /// </summary>
    [OriginalFunction(0x4C5EB0, OriginalFunctionScope.Partial)]
    public static int Level(BasicCombatSkill skill, int stored, int keyStat, bool monstrous, IReadOnlyList<EffectModifier>? effects = null)
    {
        if (!Enum.IsDefined(skill)) throw new ArgumentOutOfRangeException(nameof(skill));
        var level = skill == BasicCombatSkill.Melee && monstrous ? MonstrousMeleeLevel : BaseLevel(stored);
        if (effects is { Count: > 0 }) level = EffectModifiers.Apply(level, effects);
        return Math.Min(Math.Clamp(level, 0, MaxLevel), KeyStatCap(keyStat));
    }

    /// <summary>
    /// A skill's training: the stored bits, except a monstrous critter's melee, which takes the highest tier whose
    /// thresholds both its effective melee level (<see cref="Level"/>) and its character level reach in order (none below
    /// melee 1 or level 10, apprentice from 1 and 10, expert from 9 and 20, master from 18 and 30).
    /// </summary>
    [OriginalFunction(0x4C60C0, OriginalFunctionScope.Partial)]
    public static SkillTraining Training(BasicCombatSkill skill, int stored, bool monstrous, int meleeLevel, int characterLevel)
    {
        if (!Enum.IsDefined(skill)) throw new ArgumentOutOfRangeException(nameof(skill));
        Check(meleeLevel, nameof(meleeLevel));
        Check(characterLevel, nameof(characterLevel));
        if (skill != BasicCombatSkill.Melee || !monstrous) return StoredTraining(stored);
        var tier = 0;
        while (tier < MonstrousTierLevels.Length && meleeLevel >= MonstrousTierLevels[tier] && characterLevel >= MonstrousTierCharacterLevels[tier]) tier++;
        return (SkillTraining)tier;
    }

    private static void Check(int value, string name)
    {
        if (Math.Abs((long)value) > MaxValue) throw new ArgumentOutOfRangeException(name);
    }
}
