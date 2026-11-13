namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-1525 (ledger VB3-1, VBG14, 0x4AD0B0, 0x45F2D0): the bed an NPC lies in, as the object state of its directory entry. The original saves the occupant in the bed's own field 0x46 and the re-seat finds the exact bed by it;
/// here the same fact is kept on the sleeper's side, the field <see cref="Property"/> (a mobile object field of <see cref="ObjectStateVocabulary"/>, saved and loaded with the object, no schema step: an older save holds none and the
/// host's re-seat falls back to the bed of the nearer standpoint). The value names the bed by its position in the map's data, see <see cref="Code"/>. No record is no bed.
/// </summary>
public static class SessionNpcBed
{
    /// <summary>The object field that holds the bed an NPC lies in.</summary>
    public const string Property = "npc.bed";

    // A bed is a map-data object; a static one is named by its sector and its ordinal in the sector (the entity <c>sector:S:I</c>). The sector takes the high 43 bits, the ordinal the low 20 and the code adds 1, so 0 is no bed.
    private const int OrdinalBits = 20;
    private const long MaxSector = (1L << 42) - 1;

    /// <summary>The map change key of the NPC's bed.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);

    /// <summary>The code of the static bed at <paramref name="sector"/> and <paramref name="ordinal"/>, or null when either is out of the range the code holds (such a bed is not saved).</summary>
    public static long? Code(long sector, int ordinal) =>
        sector is < 0 or > MaxSector || ordinal is < 0 or >= 1 << OrdinalBits ? null : ((sector << OrdinalBits) | (uint)ordinal) + 1;
}

public sealed partial class GameSessionOwner
{
    /// <summary>The code of the bed the NPC lies in (<see cref="SessionNpcBed.Code"/>), or null while it lies in none or the value is unreadable.</summary>
    public long? QueryNpcBed(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var (_, value) in QueryEntityChanges(entity, SessionNpcBed.Property))
            return value >= 1 ? value : null;
        return null;
    }

    /// <summary>
    /// ARC-WORLD-1525: records the bed the NPC lies in on the open map <paramref name="map"/> (<paramref name="code"/> from <see cref="SessionNpcBed.Code"/>), or with a null <paramref name="code"/> removes the record. What is recorded already is
    /// <see cref="GameSessionDisposition.NoChange"/>; an entity that is not a mobile object, a map below 1, a code below 1 and a stale version are refused as the route step is.
    /// </summary>
    public GameSessionMutationResult WriteNpcBed(GameSessionVersion expected, string entity, SessionMapId map, long? code)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal) || code is < 1) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (QueryNpcBed(entity) == code) return NoChange();
        var key = SessionNpcBed.Key(entity);
        var edits = new List<MapChangeEdit>();
        AddRouteEdit(edits, map, key, code);
        return edits.Count == 0 ? NoChange() : ApplyChanges(expected, null, edits);
    }
}
