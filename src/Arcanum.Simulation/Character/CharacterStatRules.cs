namespace Arcanum.Simulation.Character;

/// <summary>
/// Fixed rules profile <see cref="RulesId"/>. Numbers are source claims observed in CE at
/// <see cref="SourceRevision"/> (see docs/world/character-stats-runtime.md); retail parity is NOT_VERIFIED.
/// </summary>
internal static class CharacterStatRules
{
    public const string RulesId = "steamwoven-character-stats-v1";
    public const string SourceRevision = "a7ff41b300ef712f0e7d088183a3d08957110cdd";
    public const int ExtraordinaryThreshold = 20;
    public const int MaxSectorAptitudeAdjustment = 100;
    private const long Saturation = 1_000_000_000;

    private static readonly int StatCount = Enum.GetValues<CharacterStat>().Length;

    // Index = beauty - 1 for ordinary beauty 1..19; extraordinary beauty uses the linear branch instead.
    private static readonly int[] BeautyReaction =
        [-65, -52, -42, -33, -25, -18, -12, -7, -3, 0, 3, 7, 12, 18, 25, 33, 42, 52, 65];

    public static int Count => StatCount;
    public static bool IsDefined(CharacterStat stat) => (uint)stat < (uint)StatCount;
    public static bool IsPrimary(CharacterStat stat) => stat <= CharacterStat.Charisma;
    public static bool IsDerived(CharacterStat stat) => stat >= CharacterStat.CarryWeight && IsDefined(stat);
    public static bool IsDefined(CharacterRace race) => race is >= CharacterRace.Human and <= CharacterRace.Orc;
    public static bool IsDefined(CharacterGender gender) => gender is CharacterGender.Female or CharacterGender.Male;
    public static bool IsDefined(EffectCause cause) => cause is >= EffectCause.Race and <= EffectCause.Gender;
    // Setter-owned causes only; the source's wider "innate" filter (adds Background, Class) is not modelled.
    public static bool IsInnate(EffectCause cause) => cause is EffectCause.Race or EffectCause.Gender;
    public static bool IsDefined(StatModifierOperator op) => op is >= StatModifierOperator.Add and <= StatModifierOperator.Percent;

    public static int Default(CharacterStat stat) => IsPrimary(stat) ? 8 : 0;

    public static StatRange Range(CharacterRace race, CharacterStat stat) => stat switch
    {
        <= CharacterStat.Charisma => new(1, RacialPrimaryMaximum(race, stat)),
        CharacterStat.MagickPoints or CharacterStat.TechPoints => new(0, 210),
        CharacterStat.CarryWeight => new(300, 10_000),
        CharacterStat.DamageBonus => new(-50, 50),
        CharacterStat.AcAdjustment => new(-9, 95),
        CharacterStat.Speed => new(1, 100),
        CharacterStat.HealRate => new(0, 6),
        CharacterStat.PoisonRecovery => new(1, 20),
        CharacterStat.ReactionModifier => new(-65, 200),
        CharacterStat.MaxFollowers => new(1, 7),
        CharacterStat.MagickTechAptitude => new(-100, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    private static int RacialPrimaryMaximum(CharacterRace race, CharacterStat stat) => (race, stat) switch
    {
        (CharacterRace.Dwarf or CharacterRace.HalfOrc, CharacterStat.Strength or CharacterStat.Constitution) => 21,
        (CharacterRace.Elf or CharacterRace.DarkElf,
            CharacterStat.Dexterity or CharacterStat.Beauty or CharacterStat.Willpower) => 21,
        (CharacterRace.HalfElf, CharacterStat.Dexterity) => 21,
        (CharacterRace.Gnome, CharacterStat.Willpower) => 22,
        (CharacterRace.Halfling, CharacterStat.Dexterity) => 22,
        (CharacterRace.HalfOgre, CharacterStat.Strength) => 24,
        (CharacterRace.Ogre, CharacterStat.Strength) => 26,
        (CharacterRace.Orc, CharacterStat.Strength or CharacterStat.Constitution) => 22,
        _ => 20,
    };

    /// <summary>Pre-effect value of a derived stat from already-effective inputs.</summary>
    public static int DerivedBase(CharacterStat stat, Func<CharacterStat, int> effective, CharacterStatContext context)
    {
        switch (stat)
        {
            case CharacterStat.CarryWeight:
                return 500 * effective(CharacterStat.Strength);
            case CharacterStat.DamageBonus:
            {
                var strength = effective(CharacterStat.Strength);
                var bonus = strength - 10;
                if (bonus < 0) bonus /= 2;
                return strength >= ExtraordinaryThreshold ? bonus * 2 : bonus;
            }
            case CharacterStat.AcAdjustment:
                return effective(CharacterStat.Dexterity) - 10;
            case CharacterStat.Speed:
            {
                var dexterity = effective(CharacterStat.Dexterity);
                return dexterity >= ExtraordinaryThreshold ? dexterity + 5 : dexterity;
            }
            case CharacterStat.HealRate:
                return (effective(CharacterStat.Constitution) + 1) / 3;
            case CharacterStat.PoisonRecovery:
                return effective(CharacterStat.Constitution);
            case CharacterStat.ReactionModifier:
            {
                var beauty = effective(CharacterStat.Beauty);
                return beauty >= ExtraordinaryThreshold ? 10 * beauty - 100 : BeautyReaction[beauty - 1];
            }
            case CharacterStat.MaxFollowers:
                return effective(CharacterStat.Charisma) / 4;
            case CharacterStat.MagickTechAptitude:
                return (50 * effective(CharacterStat.MagickPoints) - 55 * effective(CharacterStat.TechPoints)) / 10 +
                    context.SectorAptitudeAdjustment;
            default:
                throw new ArgumentOutOfRangeException(nameof(stat));
        }
    }

    /// <summary>
    /// Applies every modifier for <paramref name="stat"/>. Integer division truncates toward zero; intermediates
    /// saturate at ±1e9, so beyond that bound accumulation is order-dependent (original overflow behaviour is NOT_VERIFIED). The caller clamps the result to the range.
    /// </summary>
    public static int ApplyModifiers(CharacterStat stat, int value, IReadOnlyList<AppliedEffect> effects)
    {
        long percents = 0, adds = 0, multiply = 1, divide = 1;
        int? minimum = null, maximum = null;
        foreach (var effect in effects)
        foreach (var modifier in effect.Definition.Modifiers)
        {
            if (modifier.Stat != stat) continue;
            switch (modifier.Operator)
            {
                case StatModifierOperator.Add: adds = Saturate(adds + modifier.Value); break;
                case StatModifierOperator.Percent: percents = Saturate(percents + modifier.Value); break;
                case StatModifierOperator.Multiply: multiply = Saturate(multiply * modifier.Value); break;
                case StatModifierOperator.Divide: divide = Saturate(divide * modifier.Value); break;
                case StatModifierOperator.Minimum:
                    minimum = minimum is { } low ? Math.Max(low, modifier.Value) : modifier.Value; break;
                case StatModifierOperator.Maximum:
                    maximum = maximum is { } high ? Math.Min(high, modifier.Value) : modifier.Value; break;
            }
        }

        long result = value;
        if (percents != 0) result = Saturate(result * (percents + 100) / 100);
        result = Saturate(result * multiply);
        result /= divide;
        result = Saturate(result + adds);
        if (minimum is { } min && result < min) result = min;
        if (maximum is { } max && result > max) result = max;
        return (int)result;
    }

    private static long Saturate(long value) => Math.Clamp(value, -Saturation, Saturation);
}
