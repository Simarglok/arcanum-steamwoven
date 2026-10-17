namespace Arcanum.Simulation.Character;

/// <summary>
/// One effect change of a single value that is not a <see cref="CharacterStat"/>: the maximum hit points or maximum fatigue
/// a critter's effects adjust (ARC-WORLD-029, the source's <c>maxhps</c> and <c>maxfatigue</c> special changes).
/// </summary>
public readonly record struct EffectModifier(StatModifierOperator Operator, int Value);

/// <summary>
/// The source's effect aggregation for one value (<c>effect_adjust_func</c>, checked in the original executable at 0x4EA6C0):
/// the percent changes summed, then the products of the multiply and divide changes, then the sum of the adds, then the highest
/// minimum and the lowest maximum. Integer division truncates toward zero; intermediates saturate at ±1e9 (original overflow
/// is NOT_VERIFIED). The same order as the character stats and the reaction effects.
/// </summary>
public static class EffectModifiers
{
    private const long Saturation = 1_000_000_000;

    /// <summary>Applies <paramref name="modifiers"/> to <paramref name="value"/>; a divide change of 0 or an undefined operator throws.</summary>
    public static int Apply(long value, IReadOnlyList<EffectModifier> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        long percents = 0, adds = 0, multiply = 1, divide = 1;
        int? minimum = null, maximum = null;
        foreach (var modifier in modifiers)
        {
            switch (modifier.Operator)
            {
                case StatModifierOperator.Add: adds = Saturate(adds + modifier.Value); break;
                case StatModifierOperator.Percent: percents = Saturate(percents + modifier.Value); break;
                case StatModifierOperator.Multiply: multiply = Saturate(multiply * modifier.Value); break;
                case StatModifierOperator.Divide:
                    if (modifier.Value == 0) throw new ArgumentOutOfRangeException(nameof(modifiers), "divisor 0");
                    divide = Saturate(divide * modifier.Value);
                    break;
                case StatModifierOperator.Minimum: minimum = minimum is { } low ? Math.Max(low, modifier.Value) : modifier.Value; break;
                case StatModifierOperator.Maximum: maximum = maximum is { } high ? Math.Min(high, modifier.Value) : modifier.Value; break;
                default: throw new ArgumentOutOfRangeException(nameof(modifiers));
            }
        }
        var result = Saturate(value);
        if (percents != 0) result = Saturate(result * (percents + 100) / 100);
        result = Saturate(result * multiply);
        result /= divide;
        result = Saturate(result + adds);
        if (minimum is { } min && result < min) result = min;
        if (maximum is { } max && result > max) result = max;
        return (int)result;
    }

    /// <summary>True when every operator is defined, no divide change is 0, the count is at most <paramref name="maxCount"/> and
    /// every magnitude at most <paramref name="maxMagnitude"/>.</summary>
    public static bool IsValid(IReadOnlyList<EffectModifier>? modifiers, int maxCount, int maxMagnitude) =>
        modifiers is not null && modifiers.Count <= maxCount && modifiers.All(m =>
            m.Operator is >= StatModifierOperator.Add and <= StatModifierOperator.Percent &&
            !(m.Operator == StatModifierOperator.Divide && m.Value == 0) && Math.Abs((long)m.Value) <= maxMagnitude);

    private static long Saturate(long value) => Math.Clamp(value, -Saturation, Saturation);
}
