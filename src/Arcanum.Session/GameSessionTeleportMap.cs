namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-116 (MIG11 S5, FM10, GR15, ledger WS3): an NPC's teleport map, the one tie between an NPC and a map that the original keeps (object
/// field 243), as the object state of its directory entry: the field <see cref="Property"/>, a mobile object field of
/// <see cref="ObjectStateVocabulary"/>. The record names the teleport map itself and holds its number, so the object's directory entry answers
/// "which map is this NPC tied to" without asking where it stands. No record is the original's 0, never set.
/// </summary>
public static class SessionTeleportMap
{
    /// <summary>The object field that holds an NPC's teleport map.</summary>
    public const string Property = "npc.teleport";

    /// <summary>The map change key of the NPC's teleport map.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}

/// <summary>
/// ARC-SCRIPT-1163 (E5-a, ledger NS3): an NPC's day and night standpoints (object fields 0x121 and 0x122), as the object state of its directory entry: the location a script's set day or night standpoint wrote, as the original stores it
/// (x in the low word, y in the high word). No record is the standpoint its map data holds.
/// </summary>
public static class SessionNpcStandpoint
{
    /// <summary>The object fields that hold the day and the night standpoint.</summary>
    public const string DayProperty = "npc.standpoint.day", NightProperty = "npc.standpoint.night";

    /// <summary>The map change key of the NPC's day or night standpoint.</summary>
    public static MapChangeKey Key(string entity, bool day) => MapChangeKey.ForEntity(entity, day ? DayProperty : NightProperty);

    /// <summary>The stored 64-bit word of a location: <paramref name="x"/> in the low word and <paramref name="y"/> in the high word.</summary>
    public static long Pack(uint x, uint y) => unchecked((long)(((ulong)y << 32) | x));
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SCRIPT-1163 (NS3, 0x4EFDD0 on field 243): the extended standpoint actions write the NPC's teleport map outright (the catalogue map number less 4,999), not only while it is unset (<see cref="EnsureTeleportMap"/>). The record of a mobile object is one
    /// whatever map it names, so it moves to <paramref name="map"/>. The same value is <see cref="GameSessionDisposition.NoChange"/>; a map below 1, or an entity that is not a mobile object, is refused as there.
    /// </summary>
    public GameSessionMutationResult SetTeleportMap(GameSessionVersion expected, string entity, SessionMapId map)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        return SetMapChange(expected, map, SessionTeleportMap.Key(entity), map.RuntimeMap);
    }

    /// <summary>
    /// ARC-DEV-116 (FM10): the teleport map recorded for the NPC (field 243 of the original), or null while it is 0 (never set: no change of place
    /// and no route step of the NPC has written it) or the value is unreadable.
    /// </summary>
    public int? QueryTeleportMap(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var (_, value) in QueryEntityChanges(entity, SessionTeleportMap.Property))
            return value is >= 1 and <= int.MaxValue ? (int)value : null;
        return null;
    }

    /// <summary>
    /// ARC-DEV-116 (FM10, WS3, 0x45F6D0): the teleport map getter's write: while the NPC has none, the open map becomes its teleport map (one revision);
    /// a teleport map already set is left alone (<see cref="GameSessionDisposition.NoChange"/>). An entity that is not a mobile object (<c>oid:</c>)
    /// is refused (<see cref="GameSessionFailure.InvalidChangeKey"/>), as is a map below 1.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x45F6D0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public GameSessionMutationResult EnsureTeleportMap(GameSessionVersion expected, string entity, SessionMapId openMap)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Map(openMap)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (QueryTeleportMap(entity) is not null) return NoChange();
        return SetMapChange(expected, openMap, SessionTeleportMap.Key(entity), openMap.RuntimeMap);
    }

    // FM6 (0x4D3D60, both cases): every NPC the change of place prepares gets the open map as its teleport map while it has none. The edits join the
    // step's own, so they are one revision with the move: the travellers of the hero's change (with the hero's map as the map they leave) and a
    // non-player mover. An entity that is not a mobile object, and one that has a teleport map, is left out.
    private IReadOnlyList<MapChangeEdit> WithTeleportMaps(IReadOnlyList<MapChangeEdit> changes, IEnumerable<string>? travelling, string? mover)
    {
        var entities = (travelling ?? []).Concat(mover is null ? [] : [mover]).Distinct(StringComparer.Ordinal)
            .Where(entity => entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal) && QueryTeleportMap(entity) is null).ToArray();
        if (entities.Length == 0) return changes;
        var open = _actor.Map;
        var written = changes.Select(change => change.Key).ToHashSet();
        var extra = entities.Select(entity => new MapChangeEdit(open, SessionTeleportMap.Key(entity), open.RuntimeMap)).Where(edit => !written.Contains(edit.Key)).ToArray();
        return extra.Length == 0 ? changes : [.. changes, .. extra];
    }
}
