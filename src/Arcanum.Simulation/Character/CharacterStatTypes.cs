using System.Collections.Immutable;

namespace Arcanum.Simulation.Character;

/// <summary>
/// Stats owned by this component. Primary and point stats are stored; derived stats are read-only formulas.
/// Values are Steamwoven identifiers, not source stat indices or save-file field positions.
/// </summary>
public enum CharacterStat
{
    Strength, Dexterity, Constitution, Beauty, Intelligence, Perception, Willpower, Charisma,
    MagickPoints, TechPoints,
    CarryWeight, DamageBonus, AcAdjustment, Speed, HealRate, PoisonRecovery, ReactionModifier,
    MaxFollowers, MagickTechAptitude,
}

public enum CharacterRace { Human, Dwarf, Elf, HalfElf, Gnome, Halfling, HalfOrc, HalfOgre, DarkElf, Ogre, Orc }
public enum CharacterGender { Female, Male }

/// <summary>
/// Why an effect is applied. Race and Gender are reserved for the race/gender setters (Background and Class are
/// ordinary causes here). Values are Steamwoven identifiers, not wire codes: map data codes explicitly.
/// </summary>
public enum EffectCause { Race, Background, Class, Bless, Curse, Item, Spell, Injury, Tech, Gender }

/// <summary>
/// Aggregation kinds. Evaluation order is Percent, Multiply, Divide, Add, Minimum, Maximum.
/// Values are Steamwoven identifiers, not wire codes: map data codes explicitly.
/// </summary>
public enum StatModifierOperator { Add, Multiply, Divide, Minimum, Maximum, Percent }

public readonly record struct StatModifier(CharacterStat Stat, StatModifierOperator Operator, int Value);

/// <summary>One effect record. <paramref name="EffectId"/> is a caller provenance key, not a catalogue lookup.</summary>
public sealed record StatEffectDefinition(int EffectId, ImmutableArray<StatModifier> Modifiers);

/// <summary>Inclusive bounds of a stat for one race.</summary>
public readonly record struct StatRange(int Minimum, int Maximum)
{
    public bool Contains(int value) => value >= Minimum && value <= Maximum;
}

/// <summary>Explicit capacity and magnitude limits; each must be positive and at most its ceiling.</summary>
public readonly record struct CharacterStatLimits(int MaxAppliedEffects, int MaxModifiersPerEffect, int MaxModifierMagnitude)
{
    public const int AppliedEffectsCeiling = 4096;
    public const int ModifiersPerEffectCeiling = 64;
    public const int ModifierMagnitudeCeiling = 1_000_000;
    public static CharacterStatLimits Default => new(64, 7, 1000);
}

/// <summary>
/// Data-dependent innate effects. A missing key means "no effect" for that race or gender.
/// Contents are caller-supplied (the original keeps them in effect rules data), never built in.
/// </summary>
public sealed record InnateEffectTable(IReadOnlyDictionary<CharacterRace, StatEffectDefinition> RaceEffects,
    IReadOnlyDictionary<CharacterGender, StatEffectDefinition> GenderEffects)
{
    public static InnateEffectTable Empty { get; } = new(
        ImmutableDictionary<CharacterRace, StatEffectDefinition>.Empty,
        ImmutableDictionary<CharacterGender, StatEffectDefinition>.Empty);
}

/// <summary>Creation input. Stored stats start at the rules defaults; adjust them with SetBase afterwards.</summary>
public sealed record CharacterStatsDefinition(CharacterRace Race, CharacterGender Gender, InnateEffectTable Innate,
    CharacterStatLimits Limits);

/// <summary>
/// World and condition inputs of a query. Sector aptitude adjustment is bounded to [-100, 100]. <paramref name="PoisonLevel"/>
/// (0..1000) lowers strength and dexterity by <c>min(3, poison / 100)</c> and <paramref name="CrippledLegs"/> lowers speed
/// by 5, both before effects, as the source's effective-stat read does (ARC-COMBAT-002); the life state owns both values.
/// ARC-MAGIC-028 (FR-CHAR-020): the mage backgrounds move magick points by 4 before effects, from four facts the caller reads
/// where the critter stands (stat getter 0x4B0490). <paramref name="Background"/> is the PC's background block (0 for an NPC,
/// which has none: 0x4C2690); 62 is the Day Mage (+4 from 6:00 to 17:59, else -4), 63 the Night Mage (the reverse), 64 the Sky
/// Mage (+4 when the tile under it is outdoor, else -4) and 65 the Nature Mage (+4 on a tile whose name carries the <c>n</c>
/// flag, else -4); any other value changes nothing. <paramref name="Hour"/> is the game hour 0..23, <paramref name="OutdoorTile"/>
/// and <paramref name="NatureTile"/> are false where no sector is resident (the original reads no tile there).
/// </summary>
public readonly record struct CharacterStatContext(int SectorAptitudeAdjustment, int PoisonLevel = 0, bool CrippledLegs = false,
    int Background = 0, int Hour = 0, bool OutdoorTile = false, bool NatureTile = false);

public readonly record struct AppliedEffectId(long Value);
public readonly record struct AppliedEffect(AppliedEffectId Id, EffectCause Cause, StatEffectDefinition Definition);

public enum CharacterStatsDisposition { Changed, NoChange, Rejected }

public enum CharacterStatsFailure
{
    None, InvalidDefinition, InvalidStat, DerivedStatReadOnly, OutOfRange, InvalidRace, InvalidGender,
    InvalidCause, InvalidEffect, EffectCapacityExceeded, UnknownEffect, InvalidContext, RevisionExhausted,
}

public readonly record struct CharacterStatsCreateResult(CharacterStatsFailure Failure, CharacterStatsOwner? Owner);

/// <summary><paramref name="AddedEffect"/> is set only when AddEffect changed state.</summary>
public readonly record struct CharacterStatsMutationResult(CharacterStatsDisposition Disposition,
    CharacterStatsFailure Failure, long Revision, AppliedEffectId? AddedEffect);

public readonly record struct CharacterStatsQueryResult(CharacterStatsFailure Failure, CharacterStatsSnapshot? Snapshot);
