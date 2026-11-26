namespace Arcanum.Simulation.Character;

/// <summary>
/// Fixed rules profile <see cref="RulesId"/>. Numbers are the claims of docs/world/character-stats-runtime.md, not yet
/// checked in the retail executable (evidence level F0, docs/verification/functions.csv); retail parity is NOT_VERIFIED. The limits of
/// <see cref="Range"/> were compared with the executable's tables (0x4B0FC0, 0x4B0FD0; ARC-RES-109, F1).
/// </summary>
internal static class CharacterStatRules
{
    public const string RulesId = "steamwoven-character-stats-v1";
    public const int ExtraordinaryThreshold = 20;
    public const int MaxSectorAptitudeAdjustment = 100;
    /// <summary>The poison level stat's range in the source (0..1000).</summary>
    public const int MaxPoisonLevel = 1000;
    /// <summary>The mage backgrounds' block numbers (stat getter 0x4B0490, ledger BG6) and the magick points they move.</summary>
    public const int DayMageBackground = 62, NightMageBackground = 63, SkyMageBackground = 64, NatureMageBackground = 65, MageBackgroundPoints = 4;
    /// <summary>The first hour of the original's day and the first hour of its night (0x45ACF0: from 6 up to but not including 18).</summary>
    public const int DayStartHour = 6, NightStartHour = 18;

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

    [OriginalFunction(0x4B0FC0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4B0FD0, OriginalFunctionScope.Partial)]
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

    /// <summary>
    /// ARC-MAGIC-1476: the range the stat setter (0x4B0980) clamps a stored stat to, by the <b>source's</b> stat index (0..27, <c>obj_f_critter_stat_base_idx</c>; not <see cref="CharacterStat"/>, whose order differs): the
    /// minimum table at 0x5B5194 (0x4B0FC0) and the maximum table at 0x5B5204 with the race's raised maxima (0x4B0FD0; the registry marks them on <see cref="Range(CharacterRace, CharacterStat)"/>). The primary stats 0..7 are <see cref="Range(CharacterRace, CharacterStat)"/>; 17 level 0 to 51,
    /// 18 experience 0 to 2,000,000,000, 19 alignment -1000 to 1000, 20 fate points 0 to 100, 21 unspent points 0 to 56, 22 and 23 magick and tech points 0 to 210, 24 poison 0 to 1000, 25 20 to 1000, 26 gender 0 to 1,
    /// 27 race 0 to 11. Null for 8 to 16 (derived, the setter never writes them) and an index outside 0..27.
    /// </summary>
    public static StatRange? StoredRange(CharacterRace race, int index) => index switch
    {
        >= 0 and <= 7 => Range(race, (CharacterStat)index),
        17 => new(0, 51),
        18 => new(0, 2_000_000_000),
        19 => new(-1000, 1000),
        20 => new(0, 100),
        21 => new(0, 56),
        22 or 23 => new(0, 210),
        24 => new(0, 1000),
        25 => new(20, 1000),
        26 => new(0, 1),
        27 => new(0, 11),
        _ => null,
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
    /// Part of the original's situational adjustment of a base value before effects: poison lowers strength and dexterity by
    /// <c>min(3, poison / 100)</c>, crippled legs lower speed by 5, and the four mage backgrounds move magick points by 4
    /// (<see cref="MageBackgroundAdjustment"/>) (the stat getter 0x4B0490, ledger IF12, BG6, XR6; Tempus Fugit is the context's
    /// <see cref="CharacterStatContext.SpeedShift"/>; the follower bonus and the other backgrounds' adjustments (66, 67, 68) are not modelled).
    /// </summary>
    [OriginalFunction(0x4B0490, OriginalFunctionScope.Partial)]
    public static int Situational(CharacterStat stat, CharacterStatContext context) => stat switch
    {
        CharacterStat.Strength or CharacterStat.Dexterity => -Math.Min(3, context.PoisonLevel / 100),
        CharacterStat.Speed => context.SpeedShift + (context.CrippledLegs ? -5 : 0),
        CharacterStat.MagickPoints => MageBackgroundAdjustment(context),
        _ => 0,
    };

    /// <summary>The speed change of Tempus Fugit that holders gain and every other critter loses (stat getter 0x4B0490, ledger XR6).</summary>
    public const int TempusFugitSpeed = 10;

    /// <summary>
    /// ARC-MAGIC-056 (MA-9, ledger XR6, owner decision on XR14 (a)): the speed change of Tempus Fugit for one critter. The original (0x4B0490) changes speed only while the
    /// <b>local player</b> holds the spell flag: a holder gains 10, every other critter loses 10; an NPC's cast changes nobody's speed unless the player holds it too. The owner's decision
    /// (2026-11-11, both modes, no setting) fixes that bug: the change applies while <b>any</b> critter holds the spell, not only the player. With the player holding it the result is the
    /// original's; with only an NPC holding it, that NPC and the critters that hold it gain 10 and every other critter loses 10.
    /// </summary>
    public static int TempusFugitShift(bool critterHolds, bool anyCritterHolds) => !anyCritterHolds ? 0 : critterHolds ? TempusFugitSpeed : -TempusFugitSpeed;

    /// <summary>
    /// The magick points of the Day, Night, Sky and Nature Mage backgrounds (62..65) at read time (0x4B0490, the magick-points
    /// branch): each is +4 when its condition holds and -4 when it does not (a fixed 8-point swing, not "+4 or nothing").
    /// The Day Mage's condition is the hour from 6 up to 17 (0x45ACF0), the Night Mage's its opposite, the Sky Mage's an outdoor
    /// tile under the critter (0x5038C0) and the Nature Mage's the <c>n</c> flag of the tile's name (0x4EBBA0). Any other
    /// background, and every NPC, adds nothing.
    /// </summary>
    public static int MageBackgroundAdjustment(CharacterStatContext context)
    {
        var day = context.Hour is >= DayStartHour and < NightStartHour;
        return context.Background switch
        {
            DayMageBackground => day ? MageBackgroundPoints : -MageBackgroundPoints,
            NightMageBackground => day ? -MageBackgroundPoints : MageBackgroundPoints,
            SkyMageBackground => context.OutdoorTile ? MageBackgroundPoints : -MageBackgroundPoints,
            NatureMageBackground => context.NatureTile ? MageBackgroundPoints : -MageBackgroundPoints,
            _ => 0,
        };
    }

    /// <summary>
    /// Applies every modifier for <paramref name="stat"/> with the source's aggregation (<see cref="EffectModifiers.Apply"/>,
    /// ARC-WORLD-029). The caller clamps the result to the range.
    /// </summary>
    public static int ApplyModifiers(CharacterStat stat, int value, IReadOnlyList<AppliedEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        // ARC-RENDER-3418: every stat of every snapshot comes through here, so the changes are folded in place.
        var changes = new EffectModifiers.Fold();
        for (var index = 0; index < effects.Count; index++)
            foreach (var modifier in effects[index].Definition.Modifiers)
                if (modifier.Stat == stat) changes.Add(new(modifier.Operator, modifier.Value));
        return changes.Result(value);
    }
}
