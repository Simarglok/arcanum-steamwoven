using System.Collections.Immutable;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-040 (magic audit MA-8, the executable read TR1–TR6, ledger spell-runs): the monster body a Trait component of art type put on a critter — the original writes a new current art id
/// (object field 1) built from the monster class (12), the species, the old art's rotation and animation, a weapon-part bit and the palette; the stored art (field 24) stays and a restore
/// builds the body back from it. The session keeps only what the spell chose (<see cref="Species"/>, 0..31, and <see cref="Palette"/>, 0..3); the rotation, animation and frame of the body are
/// the viewer's own and a restore is the removal of this record.
/// </summary>
public readonly record struct SessionTraitArt(int Species, int Palette)
{
    /// <summary>The species the original's art builder accepts (<c>art_monster_id_build</c>, 0x503CD0, tests each against its field width).</summary>
    public const int SpeciesLimit = 32, PaletteLimit = 4;

    /// <summary>Whether the pair fits the monster art id (species 0..31, palette 0..3).</summary>
    public bool IsValid => Species is >= 0 and < SpeciesLimit && Palette is >= 0 and < PaletteLimit;

    /// <summary>The first and last species of the three elemental bodies (fire, water and air, 26 to 28): the original sets the elemental critter and blit flags for them (MG10).</summary>
    public const int FirstElemental = 26, LastElemental = 28;

    /// <summary>Whether the species is one of the three elemental bodies the original marks with the critter flags 2 bit 0x2 and the blit flag 0x10.</summary>
    public bool IsElemental => Species is >= FirstElemental and <= LastElemental;
}

/// <summary>One critter's monster body in a save (the <c>spells</c> section's <c>traitArt</c>, schema 89).</summary>
public sealed record SessionTraitArtRecord(string Entity, SessionTraitArt Art);

public sealed partial class GameSessionOwner
{
    /// <summary>The most critters that carry a monster body at once.</summary>
    public const int MaxTraitArtCritters = 4096;

    private ImmutableSortedDictionary<string, SessionTraitArt> _traitArt = ImmutableSortedDictionary.Create<string, SessionTraitArt>(StringComparer.Ordinal);

    /// <summary>ARC-MAGIC-040: the monster body a Trait component put on the hero (<c>pc</c>) or a mobile NPC (<c>oid:</c>), null when it has its own body.</summary>
    public SessionTraitArt? QueryTraitArt(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        return _traitArt.TryGetValue(entity, out var art) ? art : null;
    }

    /// <summary>The critters with a monster body, ascending by entity.</summary>
    public IReadOnlyList<SessionTraitArtRecord> QueryAllTraitArt()
    {
        ThrowIfDisposed();
        return [.. _traitArt.Select(pair => new SessionTraitArtRecord(pair.Key, pair.Value))];
    }

    /// <summary>
    /// ARC-MAGIC-040 (the Trait component, 0x452AD0 → 0x452B00 and the restore 0x452CD0): puts <paramref name="art"/> on a critter, or takes its monster body off (<paramref name="art"/> null: the
    /// restore). One revision; the same body again, or a restore of a critter that has none, is NoChange. An entity that is not the hero or a mobile object, or a species or palette the art id cannot
    /// hold, is refused (<see cref="GameSessionFailure.InvalidChangeKey"/>), as is a new critter beyond <see cref="MaxTraitArtCritters"/>.
    /// </summary>
    public GameSessionMutationResult WriteTraitArt(GameSessionVersion expected, string entity, SessionTraitArt? art)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!ObjectFlagEntity(entity) || art is { IsValid: false }) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _traitArt.TryGetValue(entity, out var old);
        if (art is null)
        {
            if (!held) return NoChange();
        }
        else if (held && old == art) return NoChange();
        else if (!held && _traitArt.Count >= MaxTraitArtCritters) return Reject(GameSessionFailure.InvalidChangeKey);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _traitArt = art is { } now ? _traitArt.SetItem(entity, now) : _traitArt.Remove(entity);
        return Changed();
    }

    private IReadOnlyList<SessionTraitArtRecord> CaptureTraitArt() => QueryAllTraitArt();

    private static GameSessionFailure ImportTraitArt(IReadOnlyList<SessionTraitArtRecord> saved, out ImmutableSortedDictionary<string, SessionTraitArt> art)
    {
        art = ImmutableSortedDictionary.Create<string, SessionTraitArt>(StringComparer.Ordinal);
        if (saved.Count > MaxTraitArtCritters) return GameSessionFailure.InvalidSnapshot;
        var builder = art.ToBuilder();
        foreach (var record in saved)
        {
            if (record is null || !ObjectFlagEntity(record.Entity) || !record.Art.IsValid || !builder.TryAdd(record.Entity, record.Art)) return GameSessionFailure.InvalidSnapshot;
        }
        art = builder.ToImmutable();
        return GameSessionFailure.None;
    }
}
