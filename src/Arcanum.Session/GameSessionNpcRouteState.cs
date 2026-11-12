namespace Arcanum.Session;

/// <summary>
/// ARC-AI-1314 (ledger VA18, VA19, VAG3, WS4, WS9): an NPC's route state as the object state of its directory entry: the waypoint index the waypoint step 0x4ACBB0 writes (the original's field 0x120) and the
/// standpoint latch the standpoint step 0x4ACDB0 sets and clears (NPC flag 0x200 of field 0x118). Both are fields of a mobile object, saved and loaded with it. A record is a change over what the NPC's map row stores: no record is
/// the row's own index (and the row's own flag bit), so a save that holds none, and a step that writes the row's value back, leave nothing behind.
/// </summary>
public static class SessionNpcRouteState
{
    /// <summary>The object fields that hold the waypoint index and the standpoint latch (1 set, 0 clear).</summary>
    public const string IndexProperty = "npc.route.index", LatchProperty = "npc.route.latch";

    /// <summary>The map change key of the NPC's waypoint index or standpoint latch.</summary>
    public static MapChangeKey Key(string entity, bool latch) => MapChangeKey.ForEntity(entity, latch ? LatchProperty : IndexProperty);
}

public sealed partial class GameSessionOwner
{
    /// <summary>The waypoint index the NPC's route steps stored (field 0x120 of the original), or null while the NPC holds none (its map row's index stands) or the value is unreadable.</summary>
    public int? QueryRouteIndex(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var (_, value) in QueryEntityChanges(entity, SessionNpcRouteState.IndexProperty))
            return value is >= 0 and <= int.MaxValue ? (int)value : null;
        return null;
    }

    /// <summary>The standpoint latch (NPC flag 0x200) the route steps wrote, or null while none is recorded (the NPC's map row stores the bit).</summary>
    public bool? QueryStandpointLatch(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var (_, value) in QueryEntityChanges(entity, SessionNpcRouteState.LatchProperty))
            return value != 0;
        return null;
    }

    /// <summary>
    /// ARC-AI-1314 (ledger WS3, WS4, WS9): what one route step (the waypoint or the standpoint step) writes on the NPC, in one revision: the waypoint index <paramref name="index"/> (over <paramref name="dataIndex"/>, the row's), the standpoint latch
    /// <paramref name="latch"/> (over <paramref name="dataLatch"/>, the row's bit) and, with <paramref name="ensureTeleportMap"/>, the open map <paramref name="map"/> as the teleport map while the NPC has none (the getter's write). A value that gives the
    /// data's own back leaves no record. Nothing to write, or what is recorded already, is <see cref="GameSessionDisposition.NoChange"/>; an entity that is not a mobile object, a map below 1, a stale version and the change budget are refused as the map change does.
    /// </summary>
    public GameSessionMutationResult WriteRouteStep(GameSessionVersion expected, string entity, SessionMapId map, int? index = null, int dataIndex = 0, bool? latch = null, bool dataLatch = false, bool ensureTeleportMap = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal) || index is < 0) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var edits = new List<MapChangeEdit>();
        if (index is { } wanted) AddRouteEdit(edits, map, SessionNpcRouteState.Key(entity, latch: false), wanted == dataIndex ? null : wanted);
        if (latch is { } on) AddRouteEdit(edits, map, SessionNpcRouteState.Key(entity, latch: true), on == dataLatch ? null : on ? 1 : 0);
        if (ensureTeleportMap && QueryTeleportMap(entity) is null) edits.Add(new(map, SessionTeleportMap.Key(entity), map.RuntimeMap));
        return edits.Count == 0 ? NoChange() : ApplyChanges(expected, null, edits);
    }

    // A record goes back to the data's value by a removal, which names the map the record sits on (none: nothing to remove).
    private void AddRouteEdit(List<MapChangeEdit> edits, SessionMapId open, MapChangeKey key, long? value)
    {
        if (value is not null) edits.Add(new(open, key, value));
        else if (QueryObjectRecordMap(key) is { } held) edits.Add(new(held, key, null));
    }
}
