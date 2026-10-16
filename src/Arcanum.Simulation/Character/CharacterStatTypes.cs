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

/// <summary>World inputs to derived formulas. Sector aptitude adjustment is bounded to [-100, 100].</summary>
public readonly record struct CharacterStatContext(int SectorAptitudeAdjustment);

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
