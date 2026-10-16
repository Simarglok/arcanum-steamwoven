using System.Collections.Immutable;

namespace Arcanum.Simulation.Character;

/// <summary>
/// Caller-serialized authority for one character's stored stats, race, gender and applied effects.
/// Every accepted change increments the revision once; rejected and no-op requests leave state untouched.
/// Snapshots are recomputed from the authoritative state, so derived values never go stale.
/// </summary>
public sealed class CharacterStatsOwner
{
    private readonly InnateEffectTable innate;
    private readonly CharacterStatLimits limits;
    private readonly int[] stored;
    private readonly List<AppliedEffect> effects = [];
    private CharacterRace race;
    private CharacterGender gender;
    private long revision;
    private long nextEffectId = 1;

    private CharacterStatsOwner(CharacterStatsDefinition definition)
    {
        innate = new(definition.Innate.RaceEffects.ToImmutableDictionary(),
            definition.Innate.GenderEffects.ToImmutableDictionary());
        limits = definition.Limits;
        race = definition.Race;
        gender = definition.Gender;
        stored = new int[CharacterStatRules.Count];
        for (var stat = 0; stat < stored.Length; stat++) stored[stat] = CharacterStatRules.Default((CharacterStat)stat);
        ApplyInnate(EffectCause.Race);
        ApplyInnate(EffectCause.Gender);
    }

    public static CharacterStatsCreateResult Create(CharacterStatsDefinition? definition)
    {
        if (definition is null || definition.Innate is null || definition.Innate.RaceEffects is null ||
            definition.Innate.GenderEffects is null || !IsValid(definition.Limits))
            return new(CharacterStatsFailure.InvalidDefinition, null);
        if (!CharacterStatRules.IsDefined(definition.Race)) return new(CharacterStatsFailure.InvalidRace, null);
        if (!CharacterStatRules.IsDefined(definition.Gender)) return new(CharacterStatsFailure.InvalidGender, null);
        foreach (var (key, effect) in definition.Innate.RaceEffects)
            if (!CharacterStatRules.IsDefined(key) || !IsValid(effect, definition.Limits))
                return new(CharacterStatsFailure.InvalidDefinition, null);
        foreach (var (key, effect) in definition.Innate.GenderEffects)
            if (!CharacterStatRules.IsDefined(key) || !IsValid(effect, definition.Limits))
                return new(CharacterStatsFailure.InvalidDefinition, null);
        return new(CharacterStatsFailure.None, new CharacterStatsOwner(definition));
    }

    public long Revision => revision;

    /// <summary>Inclusive set-time range of a stat for the current race.</summary>
    public StatRange RangeOf(CharacterStat stat) => CharacterStatRules.IsDefined(stat)
        ? CharacterStatRules.Range(race, stat)
        : throw new ArgumentOutOfRangeException(nameof(stat));

    public CharacterStatsQueryResult Query(CharacterStatContext context)
    {
        if (Math.Abs((long)context.SectorAptitudeAdjustment) > CharacterStatRules.MaxSectorAptitudeAdjustment)
            return new(CharacterStatsFailure.InvalidContext, null);
        return new(CharacterStatsFailure.None, BuildSnapshot(context));
    }

    /// <summary>
    /// Writes a stored stat. Derived stats are read-only; values outside the current race range are rejected
    /// without clamping. Lowering a stat does not consult skill/spell/tech prerequisites (not modelled here).
    /// </summary>
    public CharacterStatsMutationResult SetBase(CharacterStat stat, int value)
    {
        if (!CharacterStatRules.IsDefined(stat)) return Reject(CharacterStatsFailure.InvalidStat);
        if (CharacterStatRules.IsDerived(stat)) return Reject(CharacterStatsFailure.DerivedStatReadOnly);
        if (!CharacterStatRules.Range(race, stat).Contains(value)) return Reject(CharacterStatsFailure.OutOfRange);
        if (stored[(int)stat] == value) return NoChange();
        if (Exhausted) return Reject(CharacterStatsFailure.RevisionExhausted);
        stored[(int)stat] = value;
        return Changed(null);
    }

    /// <summary>
    /// Replaces the race: the previous race effect is removed before the new one is applied, so switching never
    /// accumulates. Stored values above the new racial maximum are kept; their effective value is clamped.
    /// </summary>
    public CharacterStatsMutationResult SetRace(CharacterRace value)
    {
        if (!CharacterStatRules.IsDefined(value)) return Reject(CharacterStatsFailure.InvalidRace);
        if (value == race) return NoChange();
        if (Exhausted) return Reject(CharacterStatsFailure.RevisionExhausted);
        effects.RemoveAll(effect => effect.Cause == EffectCause.Race);
        race = value;
        ApplyInnate(EffectCause.Race);
        return Changed(null);
    }

    /// <summary>Replaces the gender with the same remove-then-apply rule as <see cref="SetRace"/>.</summary>
    public CharacterStatsMutationResult SetGender(CharacterGender value)
    {
        if (!CharacterStatRules.IsDefined(value)) return Reject(CharacterStatsFailure.InvalidGender);
        if (value == gender) return NoChange();
        if (Exhausted) return Reject(CharacterStatsFailure.RevisionExhausted);
        effects.RemoveAll(effect => effect.Cause == EffectCause.Gender);
        gender = value;
        ApplyInnate(EffectCause.Gender);
        return Changed(null);
    }

    /// <summary>Applies a non-innate effect; Race and Gender causes belong to the setters.</summary>
    public CharacterStatsMutationResult AddEffect(StatEffectDefinition? definition, EffectCause cause)
    {
        if (!CharacterStatRules.IsDefined(cause) || CharacterStatRules.IsInnate(cause))
            return Reject(CharacterStatsFailure.InvalidCause);
        if (!IsValid(definition, limits)) return Reject(CharacterStatsFailure.InvalidEffect);
        if (effects.Count(effect => !CharacterStatRules.IsInnate(effect.Cause)) >= limits.MaxAppliedEffects)
            return Reject(CharacterStatsFailure.EffectCapacityExceeded);
        if (Exhausted) return Reject(CharacterStatsFailure.RevisionExhausted);
        var id = Append(definition!, cause);
        return Changed(id);
    }

    /// <summary>Removes exactly the effect instance <paramref name="id"/>; independent causes remain.</summary>
    public CharacterStatsMutationResult RemoveEffect(AppliedEffectId id)
    {
        var index = effects.FindIndex(effect => effect.Id == id);
        if (index < 0) return Reject(CharacterStatsFailure.UnknownEffect);
        if (CharacterStatRules.IsInnate(effects[index].Cause)) return Reject(CharacterStatsFailure.InvalidCause);
        if (Exhausted) return Reject(CharacterStatsFailure.RevisionExhausted);
        effects.RemoveAt(index);
        return Changed(null);
    }

    private void ApplyInnate(EffectCause cause)
    {
        var found = cause == EffectCause.Race
            ? innate.RaceEffects.TryGetValue(race, out var definition)
            : innate.GenderEffects.TryGetValue(gender, out definition);
        if (found) Append(definition!, cause);
    }

    private AppliedEffectId Append(StatEffectDefinition definition, EffectCause cause)
    {
        var id = new AppliedEffectId(nextEffectId++);
        effects.Add(new(id, cause, definition));
        return id;
    }

    private CharacterStatsSnapshot BuildSnapshot(CharacterStatContext context)
    {
        var applied = effects.ToImmutableArray();
        var baseValues = new int[stored.Length];
        var effective = new int[stored.Length];
        // Stored stats first: derived formulas read only effective primary and point stats.
        for (var index = 0; index < stored.Length; index++)
        {
            var stat = (CharacterStat)index;
            if (CharacterStatRules.IsDerived(stat)) continue;
            baseValues[index] = stored[index];
            effective[index] = Effective(stat, stored[index], applied);
        }
        for (var index = 0; index < stored.Length; index++)
        {
            var stat = (CharacterStat)index;
            if (!CharacterStatRules.IsDerived(stat)) continue;
            baseValues[index] = CharacterStatRules.DerivedBase(stat, input => effective[(int)input], context);
            effective[index] = Effective(stat, baseValues[index], applied);
        }
        return new(revision, race, gender, context, applied, baseValues.ToImmutableArray(), effective.ToImmutableArray());
    }

    private int Effective(CharacterStat stat, int baseValue, IReadOnlyList<AppliedEffect> applied)
    {
        var range = CharacterStatRules.Range(race, stat);
        return Math.Clamp(CharacterStatRules.ApplyModifiers(stat, baseValue, applied), range.Minimum, range.Maximum);
    }

    private CharacterStatsMutationResult Changed(AppliedEffectId? added)
    {
        revision++;
        return new(CharacterStatsDisposition.Changed, CharacterStatsFailure.None, revision, added);
    }

    private CharacterStatsMutationResult NoChange() =>
        new(CharacterStatsDisposition.NoChange, CharacterStatsFailure.None, revision, null);

    private CharacterStatsMutationResult Reject(CharacterStatsFailure failure) =>
        new(CharacterStatsDisposition.Rejected, failure, revision, null);

    private bool Exhausted => revision == long.MaxValue || nextEffectId == long.MaxValue;

    private static bool IsValid(CharacterStatLimits limits) =>
        limits.MaxAppliedEffects is > 0 and <= CharacterStatLimits.AppliedEffectsCeiling &&
        limits.MaxModifiersPerEffect is > 0 and <= CharacterStatLimits.ModifiersPerEffectCeiling &&
        limits.MaxModifierMagnitude is > 0 and <= CharacterStatLimits.ModifierMagnitudeCeiling;

    private static bool IsValid(StatEffectDefinition? definition, CharacterStatLimits limits)
    {
        if (definition is null || definition.EffectId < 0 || definition.Modifiers.IsDefaultOrEmpty ||
            definition.Modifiers.Length > limits.MaxModifiersPerEffect) return false;
        foreach (var modifier in definition.Modifiers)
        {
            if (!CharacterStatRules.IsDefined(modifier.Stat) || !CharacterStatRules.IsDefined(modifier.Operator) ||
                Math.Abs((long)modifier.Value) > limits.MaxModifierMagnitude) return false;
            if (modifier.Operator is StatModifierOperator.Divide && modifier.Value == 0) return false;
        }
        return true;
    }
}
