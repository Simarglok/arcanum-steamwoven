using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// One skill change of the hero's race or gender effect (ARC-SKILLS-002; the source keeps it in the effect, the pre-made heroes'
/// race effects change tech skills, prowling, dodge, melee and haggle, ledger SH-09): in level units, aggregated with the skill's
/// other effect changes whenever its level is read (<see cref="SkillRules.Level"/>), never written into the stored points.
/// </summary>
public sealed record SessionInnateSkillChange(EffectCause Cause, Skill Skill, StatModifierOperator Operator, int Value)
{
    /// <summary>At most this many changes: seven per effect (the character owner's modifier limit), race and gender.</summary>
    public const int MaxChanges = 14;
    /// <summary>Bound of a change's magnitude (the character owner's default modifier limit).</summary>
    public const int MaxMagnitude = 1000;

    /// <summary>A race or gender cause, a defined skill and operator, no divide by 0, a magnitude within <see cref="MaxMagnitude"/>.</summary>
    public static bool IsValid(SessionInnateSkillChange? change) => change is not null &&
        change.Cause is EffectCause.Race or EffectCause.Gender && SkillRules.IsDefined(change.Skill) && CharacterStatRules.IsDefined(change.Operator) &&
        !(change.Operator == StatModifierOperator.Divide && change.Value == 0) && Math.Abs((long)change.Value) <= MaxMagnitude;
}

/// <summary>One skill as a critter reads it now: the stored value, its points, the effective level and the training.</summary>
public readonly record struct SessionSkill(Skill Skill, int Stored, int Points, int Level, SkillTraining Training);

/// <summary>A critter's sixteen skills in <see cref="Skill"/> order (ARC-SKILLS-002).</summary>
public sealed class SessionSkills
{
    private SessionSkills(ImmutableArray<SessionSkill> all) => All = all;

    public ImmutableArray<SessionSkill> All { get; }

    public SessionSkill this[Skill skill] => All[(int)skill];

    /// <summary>
    /// The skills of a sheet under its modifiers: each level through <see cref="SkillRules.Level"/> with the effective key stat and
    /// the skill's effect changes (<see cref="AutoLevelRules.BasicSkillLevel"/>, <see cref="AutoLevelRules.TechSkillLevel"/>), each
    /// training through <see cref="SkillRules.Training"/> with the effective melee level and the sheet's level (the monstrous
    /// melee tiers). The caller has admitted both.
    /// </summary>
    internal static SessionSkills Of(AdvancementSheet sheet, AdvancementModifiers modifiers)
    {
        var melee = AutoLevelRules.BasicSkillLevel(sheet, modifiers, (int)Skill.Melee);
        var skills = ImmutableArray.CreateBuilder<SessionSkill>(SkillRules.Count);
        for (var skill = Skill.Bow; skill <= Skill.DisarmTraps; skill++)
        {
            var tech = SkillRules.IsTech(skill);
            var index = SkillRules.FamilyIndex(skill);
            var stored = tech ? sheet.TechSkills[index] : sheet.BasicSkills[index];
            var level = tech ? AutoLevelRules.TechSkillLevel(sheet, modifiers, index) : AutoLevelRules.BasicSkillLevel(sheet, modifiers, index);
            skills.Add(new(skill, stored, SkillRules.Points(stored), level, SkillRules.Training(skill, stored, sheet.Monstrous, melee, sheet.Level)));
        }
        return new(skills.MoveToImmutable());
    }
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero's sixteen skills now (ARC-SKILLS-002), or null without a hero: the stored points and training of its advancement
    /// fields, levels read under its effective stats (poison included), its race and gender skill changes and the skill changes of
    /// its durable effects. Every reader of a hero's skill (its attack, dodge and backstab, weapon speed, the best-items choice, the
    /// follow check, the dialog gates) reads this.
    /// </summary>
    public SessionSkills? QueryHeroSkills()
    {
        ThrowIfDisposed();
        return _hero is null ? null : HeroSkills();
    }

    /// <summary>
    /// The skills of an NPC the session levelled (a follower's saved sheet, ARC-WORLD-036), under <paramref name="modifiers"/> (its
    /// effects' changes and poison now, as <see cref="LevelFollower"/> takes them); null while the session holds no record of it. An
    /// invalid entity or modifiers throw.
    /// </summary>
    public SessionSkills? QueryNpcSkills(string entity, AdvancementModifiers modifiers)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        if (!AutoLevelRules.IsValid(modifiers)) throw new ArgumentException("Invalid advancement modifiers.", nameof(modifiers));
        return _advancement.Npcs.TryGetValue(entity, out var record) ? SessionSkills.Of(record.Sheet, modifiers) : null;
    }

    private SessionSkills HeroSkills()
    {
        var hero = _hero!;
        var poison = _life.Hero is { } life ? life.State.PoisonLevel : 0;
        return SessionSkills.Of(AdvancementDomain.HeroSheet(hero.State), AdvancementDomain.HeroModifiers(hero, _effects, poison));
    }

    /// <summary>The hero's training in the skill of the weapon it wields (melee bare-handed), for the weapon speed.</summary>
    private SkillTraining HeroWeaponTraining(SessionSkills skills) =>
        skills[SkillRules.Of(_equipment.Items().TryGetValue(EquipmentSlot.Weapon, out var weapon) ? weapon.Weapon?.Skill ?? CombatSkill.Melee : CombatSkill.Melee)].Training;

    /// <summary>The hero's persuasion training as the follow check reads it.</summary>
    private PersuasionTraining HeroPersuasion() => (PersuasionTraining)(int)HeroSkills()[Skill.Persuasion].Training;
}
