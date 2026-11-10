using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-UI-124 (OH12): what the original's combat panel (0x554830) shows for a hovered critter while the local PC is in combat mode:
/// the hit chance <see cref="Percent"/> (not clamped above; 0 when the shot is impossible or the result is negative), the terms that
/// made the check harder (<see cref="Reasons"/>, one icon each) and whether the weapon's magick or technology is cancelled often
/// enough to show its icon (<see cref="MagickTechPenalty"/>, a cancel chance above 20).
/// </summary>
public sealed record AttackChance(int Percent, SkillDifficultyReasons Reasons, bool MagickTechPenalty);

/// <summary>
/// ARC-UI-124 (OH12): the hit chance the combat panel computes without rolling (0x554830, read in the retail 1.0.7.4 executable). It is
/// the shared check of the attack (<see cref="AttackResolver"/>) with an empty invocation: no aimed location, no backstab, no
/// modifier, the weapon's magic not cancelled. Pure.
/// </summary>
public static class AttackChanceRules
{
    /// <summary>The cancel chance above which the panel adds the magick or technology penalty icon (0x554975: more than 20).</summary>
    public const int MagickTechIconAbove = 20;

    /// <summary>
    /// The panel's hit chance for <paramref name="request"/> (its aimed location, backstab and bed facts are ignored, as the panel's
    /// empty invocation ignores them): the effectiveness of the weapon's skill less the difficulty's total; then, for an item that is not
    /// thrown, the weapon's cancel chance (<see cref="AttackRules.MagickCancelChance"/>, 0x461620) takes its share (truncated toward
    /// zero); a negative result, an item range exceeded or a blocked line gives 0.
    /// </summary>
    [OriginalFunction(0x554830, OriginalFunctionScope.Partial)]
    public static AttackChance Preview(AttackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AttackResolver.Validate(request);
        var panel = request with { AimedLocation = null, Backstab = BackstabKind.None, TargetWasInBed = false };
        var check = AttackResolver.AttackCheckRequest(panel, magicCancelled: false);
        var effectiveness = SkillCheckRules.Effectiveness(check.Skill, check.Level, check.Effectiveness);
        var difficulty = SkillCheckRules.Difficulty(check.Skill, check.Training, effectiveness, check.Difficulty);
        var reasons = SkillCheckRules.DifficultyReasons(check.Skill, check.Training, check.Difficulty, difficulty);
        var percent = (int)Math.Clamp((long)effectiveness - difficulty.Total, int.MinValue, int.MaxValue);
        var magickTech = false;
        if (request.Weapon.Kind != WeaponKind.Unarmed && request.Skill != CombatSkill.Throwing)
        {
            var cancel = AttackRules.MagickCancelChance(request.Weapon.MagickTechComplexity, request.Attacker.MagickTechAptitude,
                request.Target.MagickTechAptitude);
            if (cancel > 0)
            {
                magickTech = cancel > MagickTechIconAbove;
                percent -= (int)((long)cancel * percent / 100);
            }
        }
        if (percent < 0 || (reasons & (SkillDifficultyReasons.OutOfRange | SkillDifficultyReasons.BlockedLine)) != 0) percent = 0;
        return new(percent, reasons, magickTech);
    }
}
