using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Combat;

/// <summary>The five basic skills an attack reads, by the source's basic-skill index (bow 0 .. backstab 4).</summary>
public enum BasicCombatSkill { Bow = 0, Dodge = 1, Melee = 2, Throwing = 3, Backstab = 4 }

/// <summary>
/// The level and training of a basic combat skill (ARC-COMBAT-005): the attack's view of <see cref="SkillRules"/>, which owns
/// the rules of all sixteen skills since ARC-SKILLS-001 (checked in the original 1.0.7.4 executable, ledger rows FA7–FA9).
/// Every function validates its inputs (<see cref="ArgumentException"/>).
/// </summary>
public static class CombatSkillRules
{
    /// <summary>The highest skill level.</summary>
    public const int MaxLevel = SkillRules.MaxLevel;
    /// <summary>The level one skill point buys.</summary>
    public const int LevelPerPoint = SkillRules.LevelPerPoint;
    /// <summary>The melee level a monstrous critter starts from instead of its points.</summary>
    public const int MonstrousMeleeLevel = SkillRules.MonstrousMeleeLevel;
    /// <summary>Bound of the magnitude of a stored skill value or a stat these rules read.</summary>
    public const int MaxValue = SkillRules.MaxValue;

    /// <summary>The key stat of a basic combat skill: dexterity for all five.</summary>
    public static CharacterStat KeyStat(BasicCombatSkill skill) => SkillRules.KeyStat(Skill(skill));

    /// <summary>The points of a stored skill value (bits 0..5).</summary>
    public static int Points(int stored) => SkillRules.Points(stored);

    /// <summary>The training of a stored skill value (bits 6..7).</summary>
    public static SkillTraining StoredTraining(int stored) => SkillRules.StoredTraining(stored);

    /// <summary>The level a skill's points give before effects: four per point.</summary>
    public static int BaseLevel(int stored) => SkillRules.BaseLevel(stored);

    /// <summary>The highest level a key stat allows: the original's table for the stat clamped to 1..20.</summary>
    public static int KeyStatCap(int keyStat) => SkillRules.KeyStatCap(keyStat);

    /// <summary>A combat skill's effective level (<see cref="SkillRules.Level"/>).</summary>
    public static int Level(BasicCombatSkill skill, int stored, int keyStat, bool monstrous, IReadOnlyList<EffectModifier>? effects = null) =>
        SkillRules.Level(Skill(skill), stored, keyStat, monstrous, effects);

    /// <summary>A combat skill's training (<see cref="SkillRules.Training"/>).</summary>
    public static SkillTraining Training(BasicCombatSkill skill, int stored, bool monstrous, int meleeLevel, int characterLevel) =>
        SkillRules.Training(Skill(skill), stored, monstrous, meleeLevel, characterLevel);

    private static Skill Skill(BasicCombatSkill skill) => Enum.IsDefined(skill) ? (Skill)(int)skill : throw new ArgumentOutOfRangeException(nameof(skill));
}
