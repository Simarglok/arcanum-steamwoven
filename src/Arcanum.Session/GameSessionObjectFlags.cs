using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>The flag fields a spell's ObjFlag component writes on a critter (ARC-DEV-302): the spell flags (field 20, <c>obj_f_spell_flags</c>, 0x14) and the critter
/// flags (field 218, <c>obj_f_critter_flags</c>, 0xDA). ARC-MAGIC-047 (MA-8, schema 90): the second critter flags (field 219, <c>obj_f_critter_flags2</c>, 0xDB) and the blit flags (field 9,
/// <c>obj_f_blit_flags</c>) the Trait component writes for the elemental bodies and the Dispel reads for the perma-polymorph bit. ARC-DEV-358 (MA-1, schema 88): the object flags (field 19, <c>obj_f_flags</c>, 0x13), which the side effects of the spell flags
/// (invisible, shrunk, water walking, stoned) and the don't-draw flag write.</summary>
public enum SessionObjectFlagField { Spell, Critter, Object, Critter2, Blit }

/// <summary>
/// ARC-DEV-302 (magic plan MT13 with the flag part of P1): what the session wrote on one critter's flag fields, as a change over the value its map data and prototype store. A bit of
/// <see cref="SpellSet"/> is on whatever the data says, a bit of <see cref="SpellCleared"/> is off; the same for the critter flags. A bit is in at most one of each pair.
/// </summary>
public readonly record struct SessionObjectFlags(int SpellSet, int SpellCleared, int CritterSet, int CritterCleared, int ObjectSet = 0, int ObjectCleared = 0,
    int Critter2Set = 0, int Critter2Cleared = 0, int BlitSet = 0, int BlitCleared = 0)
{
    public bool IsEmpty => (SpellSet | SpellCleared | CritterSet | CritterCleared | ObjectSet | ObjectCleared | Critter2Set | Critter2Cleared | BlitSet | BlitCleared) == 0;

    /// <summary>The spell flags now, over the value the data stores.</summary>
    public int Spell(int stored) => (stored & ~SpellCleared) | SpellSet;

    /// <summary>The critter flags now, over the value the data stores.</summary>
    public int Critter(int stored) => (stored & ~CritterCleared) | CritterSet;

    /// <summary>ARC-DEV-358: the object flags (<c>obj_f_flags</c>) now, over the value the data stores.</summary>
    public int Object(int stored) => (stored & ~ObjectCleared) | ObjectSet;

    /// <summary>ARC-MAGIC-047: the second critter flags (<c>obj_f_critter_flags2</c>) now, over the value the data stores.</summary>
    public int Critter2(int stored) => (stored & ~Critter2Cleared) | Critter2Set;

    /// <summary>ARC-MAGIC-047: the blit flags (<c>obj_f_blit_flags</c>) now, over the value the data stores (the data's blit flags are not read: callers pass 0).</summary>
    public int Blit(int stored) => (stored & ~BlitCleared) | BlitSet;

    /// <summary>Whether the change is well formed: no bit both set and cleared in one field.</summary>
    public bool IsValid => (SpellSet & SpellCleared) == 0 && (CritterSet & CritterCleared) == 0 && (ObjectSet & ObjectCleared) == 0
        && (Critter2Set & Critter2Cleared) == 0 && (BlitSet & BlitCleared) == 0;

    /// <summary>ARC-MAGIC-047 (MG10, the plain write 0x406D40 of a whole word): the field now is exactly <paramref name="value"/>, whatever the data and earlier writes held: every bit of it is set or cleared.</summary>
    public SessionObjectFlags Assign(SessionObjectFlagField field, int value) => field switch
    {
        SessionObjectFlagField.Spell => this with { SpellSet = value, SpellCleared = ~value },
        SessionObjectFlagField.Object => this with { ObjectSet = value, ObjectCleared = ~value },
        SessionObjectFlagField.Critter2 => this with { Critter2Set = value, Critter2Cleared = ~value },
        SessionObjectFlagField.Blit => this with { BlitSet = value, BlitCleared = ~value },
        _ => this with { CritterSet = value, CritterCleared = ~value },
    };

    /// <summary>The change with <paramref name="mask"/> turned on or off in <paramref name="field"/>. With <paramref name="dataHas"/> (whether the data's value has the bits) a
    /// value equal to the data's keeps no change of those bits, so a hold that ended leaves nothing behind.</summary>
    public SessionObjectFlags With(SessionObjectFlagField field, int mask, bool on, bool? dataHas = null)
    {
        var neutral = dataHas == on;
        var set = neutral || !on ? 0 : mask;
        var cleared = neutral || on ? 0 : mask;
        return field switch
        {
            SessionObjectFlagField.Spell => this with { SpellSet = (SpellSet & ~mask) | set, SpellCleared = (SpellCleared & ~mask) | cleared },
            SessionObjectFlagField.Object => this with { ObjectSet = (ObjectSet & ~mask) | set, ObjectCleared = (ObjectCleared & ~mask) | cleared },
            SessionObjectFlagField.Critter2 => this with { Critter2Set = (Critter2Set & ~mask) | set, Critter2Cleared = (Critter2Cleared & ~mask) | cleared },
            SessionObjectFlagField.Blit => this with { BlitSet = (BlitSet & ~mask) | set, BlitCleared = (BlitCleared & ~mask) | cleared },
            _ => this with { CritterSet = (CritterSet & ~mask) | set, CritterCleared = (CritterCleared & ~mask) | cleared },
        };
    }
}

/// <summary>One critter's written flags in a save (the <c>spells</c> section's <c>objectFlags</c>, schema 83).</summary>
public sealed record SessionObjectFlagRecord(string Entity, SessionObjectFlags Flags);

public sealed partial class GameSessionOwner
{
    /// <summary>The most critters that carry written flags at once.</summary>
    public const int MaxObjectFlagCritters = 4096;

    private ImmutableSortedDictionary<string, SessionObjectFlags> _objectFlags = ImmutableSortedDictionary.Create<string, SessionObjectFlags>(StringComparer.Ordinal);

    /// <summary>
    /// ARC-DEV-302: the flags the session wrote on a critter (the hero <c>pc</c> or a mobile NPC <c>oid:</c>), empty when none. A reader applies it over the value the critter's
    /// data stores (<see cref="SessionObjectFlags.Spell"/>, <see cref="SessionObjectFlags.Critter"/>).
    /// </summary>
    public SessionObjectFlags QueryObjectFlags(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        return _objectFlags.GetValueOrDefault(entity);
    }

    /// <summary>The critters with written flags, ascending by entity.</summary>
    public IReadOnlyList<SessionObjectFlagRecord> QueryAllObjectFlags()
    {
        ThrowIfDisposed();
        return [.. _objectFlags.Select(pair => new SessionObjectFlagRecord(pair.Key, pair.Value))];
    }

    /// <summary>
    /// ARC-DEV-302 (the field write of the ObjFlag component 0x454A10, 0x406D40): turns <paramref name="mask"/> on or off in one flag field of a critter, over what its data
    /// stores. The decision whether to write (the reference count over the runs, 0x455100) is the caller's. One revision; the same change again is NoChange. An entity that is not
    /// the hero or a mobile object, or a zero mask, is refused (<see cref="GameSessionFailure.InvalidChangeKey"/>), as is a new critter beyond
    /// <see cref="MaxObjectFlagCritters"/>. <paramref name="dataHas"/>, when the caller knows it, says whether the critter's data has the bits: a write that gives back the data's
    /// value drops the change of those bits.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x454A10, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public GameSessionMutationResult WriteObjectFlag(GameSessionVersion expected, string entity, SessionObjectFlagField field, int mask, bool on, bool? dataHas = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!ObjectFlagEntity(entity) || mask == 0 || !Enum.IsDefined(field)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _objectFlags.TryGetValue(entity, out var old);
        var now = old.With(field, mask, on, dataHas);
        if (now == old) return NoChange();
        if (!held && _objectFlags.Count >= MaxObjectFlagCritters) return Reject(GameSessionFailure.InvalidChangeKey);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _objectFlags = now.IsEmpty ? _objectFlags.Remove(entity) : _objectFlags.SetItem(entity, now);
        return Changed();
    }

    /// <summary>
    /// ARC-MAGIC-047 (MG10: the Trait component's plain writes 0x406D40 of the critter flags 2 and the blit flags for an elemental body): the field is exactly <paramref name="value"/> from now on, over whatever
    /// the data and earlier writes held. One revision; the same assignment again is NoChange. An entity that is not the hero or a mobile object is refused (<see cref="GameSessionFailure.InvalidChangeKey"/>),
    /// as is a new critter beyond <see cref="MaxObjectFlagCritters"/>.
    /// </summary>
    public GameSessionMutationResult AssignObjectFlag(GameSessionVersion expected, string entity, SessionObjectFlagField field, int value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!ObjectFlagEntity(entity) || !Enum.IsDefined(field)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _objectFlags.TryGetValue(entity, out var old);
        var now = old.Assign(field, value);
        if (now == old) return NoChange();
        if (!held && _objectFlags.Count >= MaxObjectFlagCritters) return Reject(GameSessionFailure.InvalidChangeKey);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _objectFlags = _objectFlags.SetItem(entity, now);
        return Changed();
    }

    // The hero or a mobile map object: a static object has no critter flags, and its positional id repeats from map to map.
    private static bool ObjectFlagEntity(string entity) => SessionValidation.Entity(entity)
        && (entity == SessionEventKinds.PlayerEntity || entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal));

    private IReadOnlyList<SessionObjectFlagRecord> CaptureObjectFlags() => QueryAllObjectFlags();

    // The run table as the run rules see it now: inside an action or pass the processing's own table (a component counting the runs that provide a flag must see the runs that action
    // already ended), else the session's.
    internal SpellRunTable LiveSpellRuns() => _activeSpellSystem?.Table ?? _spellRuns;

    // ARC-DEV-302: a critter's life facts with the hold flags the session wrote: stoned (spell flag 0x10000) and paralysed (critter flag 0x40) on or off over the traits its data gives, so
    // the life's activity (0x45D790), its damage and its stun read them.
    // ARC-DEV-348: and the maxima of the effect list the session holds for the NPC (WithWrittenEffects), which replace the host's.
    private LifeFacts WithWrittenHolds(string entity, LifeFacts facts)
    {
        facts = WithWrittenEffects(entity, facts);
        var written = _objectFlags.GetValueOrDefault(entity);
        if (written.IsEmpty) return facts;
        var traits = facts.Traits;
        static LifeTraits Trait(LifeTraits traits, LifeTraits trait, int set, int cleared, int bit) =>
            (set & bit) != 0 ? traits | trait : (cleared & bit) != 0 ? traits & ~trait : traits;
        traits = Trait(traits, LifeTraits.Stoned, written.SpellSet, written.SpellCleared, SessionSpellCaster.StonedSpellFlag);
        traits = Trait(traits, LifeTraits.Paralyzed, written.CritterSet, written.CritterCleared, SessionSpellCaster.ParalysedCritterFlag);
        return traits == facts.Traits ? facts : facts with { Traits = traits };
    }

    // A save's written flags: ascending distinct entities of the kinds above, each change well formed and not empty, at most MaxObjectFlagCritters.
    private static GameSessionFailure ImportObjectFlags(IReadOnlyList<SessionObjectFlagRecord> saved, out ImmutableSortedDictionary<string, SessionObjectFlags> flags)
    {
        flags = ImmutableSortedDictionary.Create<string, SessionObjectFlags>(StringComparer.Ordinal);
        if (saved.Count > MaxObjectFlagCritters) return GameSessionFailure.InvalidSnapshot;
        var builder = flags.ToBuilder();
        foreach (var record in saved)
        {
            if (record is null || !ObjectFlagEntity(record.Entity) || !record.Flags.IsValid || record.Flags.IsEmpty || !builder.TryAdd(record.Entity, record.Flags))
                return GameSessionFailure.InvalidSnapshot;
        }
        flags = builder.ToImmutable();
        return GameSessionFailure.None;
    }
}
