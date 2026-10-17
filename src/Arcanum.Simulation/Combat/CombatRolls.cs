namespace Arcanum.Simulation.Combat;

/// <summary>What a combat roll decides; every roll carries one so a trace shows which roll decided what.</summary>
public enum CombatRollPurpose
{
    HitLocation,
    TechnologyFailure,
    MagickCancel,
    AttackCheck,
    AttackCritical,
    DodgeCheck,
    DodgeCritical,
    DodgeCriticalMiss,
    CriticalSelfHit,
    DamageTriple,
    DamageDouble,
    DamageOneAndHalf,
    DamageNormal,
    DamagePoison,
    DamageElectrical,
    DamageFire,
    DamageFatigue,
    /// <summary>A roll made by an <see cref="ICriticalEffects"/> critical-hit table.</summary>
    CriticalHitEffect,
    /// <summary>A roll made by an <see cref="ICriticalEffects"/> critical-miss table.</summary>
    CriticalMissEffect,
    /// <summary>The 10..20 a knockout adds to maximum fatigue (<see cref="LifeRules.ApplyDamage"/>).</summary>
    KnockoutFatigue,
    /// <summary>The d100 (≤ 50) that cripples the second arm of a critter with one crippled arm (<see cref="LifeRules.ApplyDamage"/>).</summary>
    CrippleBothArms,
}

/// <summary>
/// The injected randomness of combat: an inclusive uniform integer in <c>min..max</c> (<c>min ≤ max</c>), the source's
/// 0x4CEF00. A seeded implementation makes a fight reproducible; tests script each roll.
/// </summary>
public interface ICombatRollSource
{
    int Roll(CombatRollPurpose purpose, int min, int max);
}

/// <summary>One roll as it was made.</summary>
public readonly record struct CombatRoll(CombatRollPurpose Purpose, int Min, int Max, int Value);

/// <summary>Records every roll of one resolution and checks that the source stays in range.</summary>
public sealed class CombatRollLog : ICombatRollSource
{
    private readonly ICombatRollSource _source;
    private readonly List<CombatRoll> _rolls = new();

    public CombatRollLog(ICombatRollSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public IReadOnlyList<CombatRoll> Rolls => _rolls;

    public int Roll(CombatRollPurpose purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        var value = _source.Roll(purpose, min, max);
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"Roll source returned {value} for {purpose} outside {min}..{max}.");
        }
        _rolls.Add(new CombatRoll(purpose, min, max, value));
        return value;
    }

    /// <summary>The source's d100: <c>1..100</c>.</summary>
    public int D100(CombatRollPurpose purpose) => Roll(purpose, 1, 100);
}
