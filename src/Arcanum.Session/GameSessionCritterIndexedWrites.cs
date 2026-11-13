using System.Collections.Immutable;
using System.Globalization;
using Arcanum.Data.Combat;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1295 (the TraitIdx component, 0x452D80): the indexed critter fields a spell writes into an NPC. The original stores a base stat in the critter's
/// <c>obj_f_critter_stat_base_idx</c> array (field 220, 28 entries) and a resistance in <c>obj_f_resistance_idx</c> (field 31, 5 entries, in the order normal, fire, electrical, poison, magic).
/// The session holds each written entry as the object state of the NPC's directory entry (<see cref="StatProperty"/>, <see cref="ResistanceProperty"/>; a mobile object field, saved by identity,
/// no schema step): the value the entry has from now on, over what the map or prototype data stores. <see cref="Apply"/> lays the written entries over the stored fields, as the other
/// consumers of an NPC's stored values read them.
/// </summary>
public sealed record SessionCritterIndexedWrites(ImmutableSortedDictionary<int, int> Stats, ImmutableSortedDictionary<int, int> Resistances)
{
    /// <summary>The entries of the base stat array (the source's 28) and of the resistance array (5).</summary>
    public const int StatCount = 28, ResistanceCount = 5;

    private const string StatPrefix = "npc.stat.", ResistancePrefix = "npc.resist.";

    // The names are built once: a query reads all of them for every build of an NPC's combat values.
    private static readonly string[] StatProperties = [.. Enumerable.Range(0, StatCount).Select(i => StatPrefix + i.ToString(CultureInfo.InvariantCulture))];
    private static readonly string[] ResistanceProperties = [.. Enumerable.Range(0, ResistanceCount).Select(i => ResistancePrefix + i.ToString(CultureInfo.InvariantCulture))];

    /// <summary>The object field that holds base stat <paramref name="index"/> (0..27) of an NPC.</summary>
    public static string StatProperty(int index) => StatProperties[index];

    /// <summary>The object field that holds resistance <paramref name="index"/> (0..4) of an NPC.</summary>
    public static string ResistanceProperty(int index) => ResistanceProperties[index];

    /// <summary>Whether the property is one of the written entries (canonical spelling: no sign, no leading zero, an index in range).</summary>
    internal static bool Owns(string? property) =>
        property is not null && (Index(property, StatPrefix, StatCount) is not null || Index(property, ResistancePrefix, ResistanceCount) is not null);

    private static int? Index(string property, string prefix, int count) =>
        property.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(property.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < count
            && property == prefix + index.ToString(CultureInfo.InvariantCulture)
            ? index : null;

    /// <summary>Nothing written.</summary>
    public static SessionCritterIndexedWrites None { get; } = new(ImmutableSortedDictionary<int, int>.Empty, ImmutableSortedDictionary<int, int>.Empty);

    /// <summary>No entry is written.</summary>
    public bool IsEmpty => Stats.IsEmpty && Resistances.IsEmpty;

    /// <summary>
    /// The stored fields of an NPC with the written entries laid over them: the base stat array of its critter block and the resistance array of its object block. The fields themselves when nothing
    /// is written or they have no critter block, or an array of another length than the original's (a caller's synthetic record).
    /// </summary>
    public CombatFields Apply(CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (IsEmpty) return fields;
        var result = fields;
        if (!Stats.IsEmpty && fields.Critter is { } critter && critter.BaseStats.Count == StatCount)
            result = result with { Critter = critter with { BaseStats = Lay(critter.BaseStats, Stats) } };
        if (!Resistances.IsEmpty && fields.Object.Resistances.Count == ResistanceCount)
            result = result with { Object = fields.Object with { Resistances = Lay(fields.Object.Resistances, Resistances) } };
        return result;
    }

    /// <summary>A key that names these written entries (for a cache that holds values built from the fields); empty when none is written.</summary>
    public string Signature => IsEmpty ? "" : string.Join(',', Stats.Select(p => $"s{p.Key}={p.Value}").Concat(Resistances.Select(p => $"r{p.Key}={p.Value}")));

    /// <summary>The stored base stats with the written ones laid over them; the stored array itself when none is written or it is not the original's 28.</summary>
    public IReadOnlyList<int>? OverStats(IReadOnlyList<int>? stored) => stored is not null && stored.Count == StatCount ? Lay(stored, Stats) : stored;

    /// <summary>The stored resistances with the written ones laid over them; the stored array itself when none is written or it is not the original's 5.</summary>
    public IReadOnlyList<int>? OverResistances(IReadOnlyList<int>? stored) => stored is not null && stored.Count == ResistanceCount ? Lay(stored, Resistances) : stored;

    private static IReadOnlyList<int> Lay(IReadOnlyList<int> stored, ImmutableSortedDictionary<int, int> written)
    {
        if (written.IsEmpty) return stored;
        var values = stored.ToArray();
        foreach (var (index, value) in written) values[index] = value;
        return Array.AsReadOnly(values);
    }
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-1295 (the TraitIdx component's base stat write, 0x4B0980 after its clamp and guards): base stat <paramref name="index"/> (0..27) of the NPC is <paramref name="value"/> from now on, the record kept on
    /// <paramref name="map"/>. The same value as the one written is <see cref="GameSessionDisposition.NoChange"/>. Refused as a map change is: a map below 1 (<see cref="GameSessionFailure.InvalidMap"/>), an entity that is not a mobile
    /// object or an index out of range (<see cref="GameSessionFailure.InvalidChangeKey"/>), a stale version, the change budget. The bounds and guards of the setter are the caller's.
    /// </summary>
    [OriginalFunction(0x4B0980, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult WriteCritterStat(GameSessionVersion expected, SessionMapId map, string entity, int index, int value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (index is < 0 or >= SessionCritterIndexedWrites.StatCount) return Reject(GameSessionFailure.InvalidChangeKey);
        return WriteIndexed(expected, map, entity, SessionCritterIndexedWrites.StatProperty(index), value);
    }

    /// <summary>ARC-MAGIC-1295 (the indexed write 0x407340 of the TraitIdx component): resistance <paramref name="index"/> (0..4) of the NPC is <paramref name="value"/> from now on; the refusals of <see cref="WriteCritterStat"/>.</summary>
    [OriginalFunction(0x407340, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult WriteCritterResistance(GameSessionVersion expected, SessionMapId map, string entity, int index, int value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (index is < 0 or >= SessionCritterIndexedWrites.ResistanceCount) return Reject(GameSessionFailure.InvalidChangeKey);
        return WriteIndexed(expected, map, entity, SessionCritterIndexedWrites.ResistanceProperty(index), value);
    }

    private GameSessionMutationResult WriteIndexed(GameSessionVersion expected, SessionMapId map, string entity, string property, int value)
    {
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        return ApplyChanges(expected, null, [new(map, MapChangeKey.ForEntity(entity, property), value)]);
    }

    /// <summary>
    /// ARC-MAGIC-1295: the base stats and resistances written into the NPC (empty when none): what a consumer of the stored fields lays over them (<see cref="SessionCritterIndexedWrites.Apply"/>).
    /// A read only; wherever the NPC stands.
    /// </summary>
    public SessionCritterIndexedWrites QueryCritterIndexedWrites(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return SessionCritterIndexedWrites.None;
        // Most NPCs have none written and this is read for every build of an NPC's combat values: the builders are made on the first hit only.
        ImmutableSortedDictionary<int, int>.Builder? stats = null, resistances = null;
        for (var i = 0; i < SessionCritterIndexedWrites.StatCount; i++)
            foreach (var (_, value) in QueryEntityChanges(entity, SessionCritterIndexedWrites.StatProperty(i))) (stats ??= ImmutableSortedDictionary.CreateBuilder<int, int>())[i] = unchecked((int)value);
        for (var i = 0; i < SessionCritterIndexedWrites.ResistanceCount; i++)
            foreach (var (_, value) in QueryEntityChanges(entity, SessionCritterIndexedWrites.ResistanceProperty(i))) (resistances ??= ImmutableSortedDictionary.CreateBuilder<int, int>())[i] = unchecked((int)value);
        return stats is null && resistances is null ? SessionCritterIndexedWrites.None
            : new(stats?.ToImmutable() ?? ImmutableSortedDictionary<int, int>.Empty, resistances?.ToImmutable() ?? ImmutableSortedDictionary<int, int>.Empty);
    }

    /// <summary>
    /// ARC-MAGIC-1295 (the guard of the stat setter reads the stat after the write, 0x4B0980 with the getter 0x4B0490): the effective value of primary stat <paramref name="stat"/> of the NPC if its base were
    /// <paramref name="baseValue"/>, the other stored stats as <paramref name="stored"/> gives them: each clamped to its race's range, the NPC's effect list (the session's, else the one its data stores) laid over
    /// them as an effect list of the stats is everywhere else (<c>WithStatEffects</c>). Null when the stats or the list do not fit the character rules.
    /// </summary>
    public int? QueryNpcEffectiveStat(SessionLifeTarget target, IReadOnlyList<int> stored, CharacterStat stat, int baseValue)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stored);
        if (stored.Count != SessionCritterIndexedWrites.StatCount || !CharacterStatRules.IsPrimary(stat) || stored[27] is < 0 or > 10 || stored[26] is not (0 or 1)) return null;
        var created = CharacterStatsOwner.Create(new((CharacterRace)stored[27], stored[26] == 0 ? CharacterGender.Female : CharacterGender.Male, InnateEffectTable.Empty,
            CharacterStatLimits.Default with { MaxAppliedEffects = SessionNpcEffectLimits.MaxEntries }));
        if (created.Owner is not { } owner) return null;
        for (var index = 0; index < 8; index++)
        {
            var range = owner.RangeOf((CharacterStat)index);
            var value = (CharacterStat)index == stat ? baseValue : stored[index];
            if (owner.SetBase((CharacterStat)index, Math.Clamp(value, range.Minimum, range.Maximum)).Disposition == CharacterStatsDisposition.Rejected) return null;
        }
        if (QueryNpcEffects(target) is { } list)
            foreach (var entry in list.Entries)
                if (list.Definitions.FirstOrDefault(rule => rule.Number == entry.Number) is { } rule && EffectDomain.Stats(rule) is { } modifiers
                    && owner.AddEffect(modifiers, EffectCause.Spell).Disposition == CharacterStatsDisposition.Rejected) return null;
        return owner.Query(new(0, 0)).Snapshot?.GetEffective(stat);
    }
}
