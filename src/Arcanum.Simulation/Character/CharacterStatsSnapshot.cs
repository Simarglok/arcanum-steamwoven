using System.Collections.Immutable;

namespace Arcanum.Simulation.Character;

/// <summary>
/// Detached, fully computed view of one revision under one context. Later owner changes never alter it.
/// Base of a stored stat is the stored value; base of a derived stat is its formula before effects.
/// Effective = the context's situational adjustment (poison, crippled legs) added to base, then effects, then clamped to
/// the race range.
/// </summary>
public sealed class CharacterStatsSnapshot
{
    private readonly ImmutableArray<int> baseValues;
    private readonly ImmutableArray<int> effectiveValues;

    internal CharacterStatsSnapshot(long revision, CharacterRace race, CharacterGender gender,
        CharacterStatContext context, ImmutableArray<AppliedEffect> appliedEffects,
        ImmutableArray<int> baseValues, ImmutableArray<int> effectiveValues)
    {
        Revision = revision;
        Race = race;
        Gender = gender;
        Context = context;
        AppliedEffects = appliedEffects;
        this.baseValues = baseValues;
        this.effectiveValues = effectiveValues;
    }

    public static string RulesId => CharacterStatRules.RulesId;
    public long Revision { get; }
    public CharacterRace Race { get; }
    public CharacterGender Gender { get; }
    public CharacterStatContext Context { get; }

    /// <summary>All applied effects in application order, innate ones included.</summary>
    public ImmutableArray<AppliedEffect> AppliedEffects { get; }

    public int GetBase(CharacterStat stat) => baseValues[Index(stat)];
    public int GetEffective(CharacterStat stat) => effectiveValues[Index(stat)];
    public StatRange GetRange(CharacterStat stat) => CharacterStatRules.Range(Race, Checked(stat));

    /// <summary>True for a primary stat whose effective value is at least 20.</summary>
    public bool IsExtraordinary(CharacterStat stat) =>
        CharacterStatRules.IsPrimary(Checked(stat)) && GetEffective(stat) >= CharacterStatRules.ExtraordinaryThreshold;

    private static int Index(CharacterStat stat) => (int)Checked(stat);

    private static CharacterStat Checked(CharacterStat stat) => CharacterStatRules.IsDefined(stat)
        ? stat
        : throw new ArgumentOutOfRangeException(nameof(stat));
}
