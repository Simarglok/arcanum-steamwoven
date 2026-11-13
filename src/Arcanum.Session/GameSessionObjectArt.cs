namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1735 (E5-a tail, ledger NS5 and the change art number rows): an object's art id as a script's change art number (59) wrote it (object field 1 of the original), kept as the object state of its directory entry:
/// the new art id, frame 0 included, by the object's identity (a mobile object's <c>oid:</c> entity, or a static object's <c>sector:S:I</c> on its map). No record is the art id the map data holds. The field is an ordinary routed object
/// field, so it is saved in the snapshot's objects section (a save made before the action had none and loads as it was: no schema step, as for <see cref="SessionNpcStandpoint"/>), carried with a mobile object and kept on its map
/// for a static one.
/// </summary>
public static class SessionObjectArt
{
    /// <summary>The object field that holds the art id a script wrote.</summary>
    public const string Property = "obj.art";

    /// <summary>The map change key of an object's written art id.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}

public sealed partial class GameSessionOwner
{
    /// <summary>ARC-SCRIPT-1735: the art id a script wrote for the object on <paramref name="map"/> (a static object's identity is the map and the entity), or null while the object has the art its map data gives it.</summary>
    public uint? QueryObjectArt(SessionMapId map, string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        return QueryObjectChange(map, SessionObjectArt.Key(entity)) is { } value && value is >= 0 and <= uint.MaxValue ? (uint)value : null;
    }

    /// <summary>ARC-SCRIPT-1735: every object of <paramref name="map"/> with a written art id (entity and art id), in entity order. A read only: the viewer draws these objects with their new art.</summary>
    public IReadOnlyList<(string Entity, uint Art)> QueryObjectArts(SessionMapId map)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_maps.ForMap(map).Where(change => change.Key.Scope == MapChangeScope.Entity && change.Key.Property == SessionObjectArt.Property && change.Value is >= 0 and <= uint.MaxValue)
            .Select(change => (change.Key.Entity!, (uint)change.Value)).ToArray());
    }

    /// <summary>
    /// ARC-SCRIPT-1735 (NS5): records the art id the object now has: <paramref name="art"/> equal to the art id its map data gives (<paramref name="baseline"/>) returns the object to the baseline (the record goes), any other id is the record.
    /// The same state is <see cref="GameSessionDisposition.NoChange"/>.
    /// </summary>
    public GameSessionMutationResult SetObjectArt(GameSessionVersion expected, SessionMapId map, string entity, uint art, uint baseline)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        var key = SessionObjectArt.Key(entity);
        if (art == baseline) return ClearMapChange(expected, map, key);
        return SetMapChange(expected, map, key, art);
    }
}
