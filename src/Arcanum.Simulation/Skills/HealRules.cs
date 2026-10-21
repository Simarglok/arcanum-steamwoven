using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>What a heal check does (ARC-SKILLS-005): the hit points healed, whether it cures (one scar and the first injury), the
/// item's uses it costs and the check's critical after an expert's demotion of a critical failure.</summary>
public readonly record struct HealOutcome(int HitPoints, bool Cure, int UsesSpent, bool Critical);

/// <summary>The roll of a heal's amount: <c>random_between(1, maximum)</c> (equal bounds draw nothing).</summary>
public interface IHealRolls
{
    int Amount(int maximum);
}

/// <summary>
/// The heal skill's own handler in the retail 1.0.7.4 executable (the heal case of 0x4C7160, ledger HL1–HL3), read by two readers:
/// the amount from the healer's heal level, the master's and the critical success's full amount and cure, the expert's
/// demotion of a critical failure, and the healing item's uses. The check itself is <see cref="SkillCheckRules.Run"/>; applying
/// the outcome to the target is <c>LifeRules.SkillHeal</c>. Pure functions over explicit values.
/// </summary>
public static class HealRules
{
    /// <summary>The uses a critical failure costs; any other check costs one.</summary>
    public const int CriticalFailureUses = 5;

    /// <summary>The most a check heals: the heal level, × 3 / 2 (toward zero) with any training, at least 1.</summary>
    public static int MaximumAmount(int level, SkillTraining training)
    {
        if (level is < 0 or > SkillRules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        if (!SkillRules.IsDefined(training)) throw new ArgumentOutOfRangeException(nameof(training));
        return Math.Max(1, training == SkillTraining.None ? level : 3 * level / 2);
    }

    /// <summary>
    /// The outcome of one check: a success heals the maximum and cures for a master or a critical success, else
    /// <c>random_between(1, maximum)</c> (the only extra draw) without a cure; a failure heals nothing, and from expert a critical
    /// failure is a plain one. The item loses five uses on a critical failure that stays critical, one otherwise.
    /// </summary>
    public static HealOutcome Resolve(bool success, bool critical, int level, SkillTraining training, IHealRolls rolls)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        var maximum = MaximumAmount(level, training);
        if (success)
        {
            if (training == SkillTraining.Master || critical) return new(maximum, true, 1, critical);
            var amount = rolls.Amount(maximum);
            if (amount < 1 || amount > maximum) throw new InvalidOperationException($"A heal roll of {amount} is outside 1..{maximum}.");
            return new(amount, false, 1, critical);
        }
        if (critical && training >= SkillTraining.Expert) critical = false;
        return new(0, false, critical ? CriticalFailureUses : 1, critical);
    }
}
