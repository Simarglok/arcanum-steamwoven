namespace Arcanum.Simulation.Character;

/// <summary>
/// One effect change of a single value that is not a <see cref="CharacterStat"/>: the maximum hit points or maximum fatigue
/// a critter's effects adjust (ARC-WORLD-029, the source's <c>maxhps</c> and <c>maxfatigue</c> special changes).
/// </summary>
public readonly record struct EffectModifier(StatModifierOperator Operator, int Value);

/// <summary>
/// The source's effect aggregation for one value (0x4EA6C0, checked in the original executable):
/// the percent changes summed, then the products of the multiply and divide changes, then the sum of the adds, then the highest
/// minimum and the lowest maximum. As in the original, −1 is the "unset" mark of both bounds: a bound of −1 is never applied,
/// and a −1 met after a lower minimum or a higher maximum clears it (ledger EF6). Integer division truncates toward zero;
/// intermediates saturate at ±1e9 (original overflow is NOT_VERIFIED). The character stats and the reaction effects use it too.
/// </summary>
public static class EffectModifiers
{
    private const long Saturation = 1_000_000_000;
    private const int Unset = -1;

    /// <summary>Applies <paramref name="modifiers"/> to <paramref name="value"/>; a divide change of 0 or an undefined operator throws.</summary>
    [OriginalFunction(0x4EA6C0, OriginalFunctionScope.Full)]
    public static int Apply(long value, IReadOnlyList<EffectModifier> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        var changes = new Fold();
        for (var index = 0; index < modifiers.Count; index++) changes.Add(modifiers[index]);
        return changes.Result(value);
    }

    /// <summary>The same aggregation fed one change at a time, in the order of the changes.</summary>
    internal struct Fold
    {
        private long _percents, _adds, _multiply, _divide;
        private int _minimum, _maximum;

        public Fold() => (_percents, _adds, _multiply, _divide, _minimum, _maximum) = (0, 0, 1, 1, Unset, Unset);

        public void Add(EffectModifier modifier)
        {
            switch (modifier.Operator)
            {
                case StatModifierOperator.Add: _adds = Saturate(_adds + modifier.Value); break;
                case StatModifierOperator.Percent: _percents = Saturate(_percents + modifier.Value); break;
                case StatModifierOperator.Multiply: _multiply = Saturate(_multiply * modifier.Value); break;
                case StatModifierOperator.Divide:
                    if (modifier.Value == 0) throw new ArgumentOutOfRangeException(nameof(modifier), "divisor 0");
                    _divide = Saturate(_divide * modifier.Value);
                    break;
                case StatModifierOperator.Minimum: if (_minimum == Unset || _minimum < modifier.Value) _minimum = modifier.Value; break;
                case StatModifierOperator.Maximum: if (_maximum == Unset || _maximum > modifier.Value) _maximum = modifier.Value; break;
                default: throw new ArgumentOutOfRangeException(nameof(modifier));
            }
        }

        public readonly int Result(long value)
        {
            var result = Saturate(value);
            if (_percents != 0) result = Saturate(result * (_percents + 100) / 100);
            result = Saturate(result * _multiply);
            result /= _divide;
            result = Saturate(result + _adds);
            if (_minimum != Unset && result < _minimum) result = _minimum;
            if (_maximum != Unset && result > _maximum) result = _maximum;
            return (int)result;
        }
    }

    /// <summary>True when every operator is defined, no divide change is 0, the count is at most <paramref name="maxCount"/> and
    /// every magnitude at most <paramref name="maxMagnitude"/>.</summary>
    public static bool IsValid(IReadOnlyList<EffectModifier>? modifiers, int maxCount, int maxMagnitude)
    {
        if (modifiers is null || modifiers.Count > maxCount) return false;
        for (var index = 0; index < modifiers.Count; index++)
        {
            var m = modifiers[index];
            if (m.Operator is < StatModifierOperator.Add or > StatModifierOperator.Percent
                || m.Operator == StatModifierOperator.Divide && m.Value == 0 || Math.Abs((long)m.Value) > maxMagnitude) return false;
        }
        return true;
    }

    private static long Saturate(long value) => Math.Clamp(value, -Saturation, Saturation);
}
