using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>
/// The sixteen skills: the twelve basic skills by the source's basic-skill index (bow 0 .. persuasion 11), then the four tech
/// skills as 12 + the source's tech-skill index (repair 12 .. disarm traps 15).
/// </summary>
public enum Skill
{
    Bow, Dodge, Melee, Throwing, Backstab, PickPocket, Prowling, SpotTrap, Gambling, Haggle, Heal, Persuasion,
    Repair, Firearms, PickLocks, DisarmTraps,
}

/// <summary>Why a skill change was or was not written.</summary>
public enum SkillChangeStatus
{
    /// <summary>The new value was written.</summary>
    Changed,
    /// <summary>Points below 0: the source returns 0 and writes nothing.</summary>
    NegativePoints,
    /// <summary>Four times the points is above the key stat's cap: the source returns the stored points and writes nothing.</summary>
    KeyStatTooLow,
    /// <summary>A training tier more than one step above the stored one: the source returns the stored tier and writes nothing.</summary>
    SkipsTier,
    /// <summary>The skill's effective level is below the tier's minimum: the source returns the stored tier and writes nothing.</summary>
    LevelTooLow,
    /// <summary>An input outside the rules' domain (an undefined skill or tier, a value outside the bounds); the source answers
    /// 0 for an undefined skill or tier and writes nothing.</summary>
    Invalid,
}

/// <summary>
/// A points change: the stored value after it (unchanged unless <see cref="Status"/> is <see cref="SkillChangeStatus.Changed"/>),
/// the value the source returns (the new points, the stored points on a refused cap, 0 otherwise), and for a tech skill the
/// change of the stored tech points (new points − old points on a change, else 0), which the caller adds to the tech points.
/// </summary>
public readonly record struct SkillPointsChange(SkillChangeStatus Status, int Stored, int Points, int TechPointsChange);

/// <summary>
/// A training change: the stored value after it (unchanged unless <see cref="Status"/> is <see cref="SkillChangeStatus.Changed"/>)
/// and the tier the source returns (the new tier on a change, the stored tier on a refusal, none for an invalid input). The
/// source runs the Educator background update after every change, the same tier written again included; that is the caller's.
/// </summary>
public readonly record struct SkillTrainingChange(SkillChangeStatus Status, int Stored, SkillTraining Training);

public enum SkillKeyStatStatus
{
    /// <summary>The value keeps every skill's bought points.</summary>
    Allowed,
    /// <summary>A skill keyed on the stat has a base level (four per point) above the cap the value gives.</summary>
    SkillNeedsStat,
    /// <summary>An input outside the rules' domain (an undefined stat, a value outside the bounds, a skill list of the wrong
    /// length or with a value outside the bounds).</summary>
    Invalid,
}

/// <summary>The stat-lowering guard's answer, with the first skill (basic skills before tech skills, by index) whose bought
/// points the value would no longer allow.</summary>
public readonly record struct SkillKeyStatCheck(SkillKeyStatStatus Status, Skill? Blocking)
{
    public bool Allowed => Status == SkillKeyStatStatus.Allowed;
}
