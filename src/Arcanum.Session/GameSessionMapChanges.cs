namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-092 (MIG7a, TM2, GR5), ARC-DEV-093 (MIG7b): one recorded field of a map-data object, found by the object's identity and not by
/// the map's changes it used to sit among: the object's entity, the property, the map the record names (the object's home map, which the map
/// budgets and <see cref="GameSessionOwner.QueryMapChanges"/> use) and the value. A mobile object's entity is <c>oid:</c> plus the hash of its
/// persistent id, stable on every map, and its record is one whatever map it names (a write on another map moves it); a static object's entity
/// is the positional <c>sector:S:I</c>, which two maps can share, so the identity of a static is the map and the entity together and its
/// record is kept on that map only (<see cref="ObjectStateVocabulary.StaticProperties"/>). It is a row of the snapshot section
/// <see cref="GameSessionSnapshot.Objects"/> (schema 62 for the mobile objects, 63 adds the statics).
/// </summary>
public readonly record struct SessionObjectField(string Entity, string Property, SessionMapId Map, long Value);

/// <summary>
/// ARC-DEV-092 (MIG7a), ARC-DEV-093 (MIG7b): which map changes are the state of a map-data object. The original keeps the changed fields on the
/// object itself and they travel with it (OM9, OM10, PS20, PS21); the session keeps these as fields of the object's directory
/// entry. A change is routed there when its key is entity-scoped and its property is one of the object's own fields. Two classes of object:
/// a <b>mobile</b> object (<c>oid:</c> entity) is found by its identity alone and has one record per property wherever it stands
/// (<see cref="Properties"/>); a <b>static</b> object (<c>sector:S:I</c>, the sector and the ordinal of a sector object, which carries no id of its
/// own) never leaves its map, and its identity is the map and the entity together, because the same sector and ordinal exist on other maps
/// (<see cref="StaticProperties"/>). Every other key, the script attachments included (MIG8), stays in the per-map vocabulary.
/// </summary>
internal static class ObjectStateVocabulary
{
    /// <summary>The prefix of a mobile map object's entity.</summary>
    internal const string EntityPrefix = "oid:";

    /// <summary>The prefix of a static map object's positional entity (<c>sector:S:I</c>).</summary>
    internal const string StaticEntityPrefix = "sector:";

    /// <summary>The fields of a mobile object: off and destroyed flags, an NPC's cell, away map, stored home cell and faction, the follow mark, a dropped
    /// item's cell, the damage and the condition, a container's or door's lock fields and a trap's state.</summary>
    internal static readonly IReadOnlySet<string> Properties = new HashSet<string>(StringComparer.Ordinal)
    {
        "obj.off", "obj.destroyed", "npc.cell", "npc.map", "npc.home", "npc.faction", "follow.pc", "item.dropped",
        "object.damage", "object.condition", "lock.flags", "lock.frame", "lock.kind", "trap.state",
    };

    /// <summary>The fields of a static object (MIG7b): the off and destroyed flags, the damage and the condition, a door's or container's lock
    /// fields and a trap's state. A static is not an NPC, an item or a follower, so the other fields of <see cref="Properties"/> are not its own.</summary>
    internal static readonly IReadOnlySet<string> StaticProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "obj.off", "obj.destroyed", "object.damage", "object.condition", "lock.flags", "lock.frame", "lock.kind", "trap.state",
    };

    /// <summary>Whether the key is a field of a mobile object (one record by the object's identity, whatever map it names).</summary>
    internal static bool Mobile(MapChangeKey key) => key.Scope == MapChangeScope.Entity && Mobile(key.Entity, key.Property);

    internal static bool Mobile(string? entity, string property) =>
        entity is not null && entity.StartsWith(EntityPrefix, StringComparison.Ordinal) && Properties.Contains(property);

    /// <summary>Whether the key is a field of a static object (one record per map, entity and property).</summary>
    internal static bool Static(MapChangeKey key) => key.Scope == MapChangeScope.Entity && Static(key.Entity, key.Property);

    internal static bool Static(string? entity, string property) =>
        entity is not null && entity.StartsWith(StaticEntityPrefix, StringComparison.Ordinal) && StaticProperties.Contains(property);

    /// <summary>Whether the key is a field of an object (the directory entry holds it), mobile or static.</summary>
    internal static bool Routed(MapChangeKey key) => Mobile(key) || Static(key);

    internal static bool Routed(string? entity, string property) => Mobile(entity, property) || Static(entity, property);
}

/// <summary>
/// ARC-DEV-092 (MIG7a), ARC-DEV-093 (MIG7b, TM2, GR5): the session's recorded map changes. The fields of mobile map-data objects
/// (<see cref="ObjectStateVocabulary"/>) are kept by the object's identity, one value per entity and property wherever the object stands;
/// the fields of static objects by the object's map-qualified identity, one value per map, entity and property; every other change stays under
/// its map as before. Both answer the one question "what is the recorded value of this key"
/// (<see cref="Get"/>), count against the same per-map and total budgets, and are saved apart (the fields in the snapshot's objects
/// section, the rest in the maps section). A routed key has one record whatever the maps said before: its map names where the record is
/// kept, a write that names another map moves the record there, and a read or a removal that names a map the record is not on finds none
/// (as before, when the same key under another map was another key; now the record is one and the identity queries find it). A static
/// object's record never moves: the same entity on another map is another object, so a read, a write or a removal names the map it means.
/// </summary>
internal sealed class SessionMapChanges
{
    private readonly Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> _maps = [];
    private readonly Dictionary<(string Entity, string Property), (SessionMapId Map, long Value)> _fields = [];
    private readonly Dictionary<(SessionMapId Map, string Entity, string Property), long> _statics = [];
    private readonly Dictionary<SessionMapId, int> _fieldCounts = [];

    /// <summary>Every recorded change, per-map and object fields together (the budget of the whole session).</summary>
    internal int Total { get; private set; }

    /// <summary>The recorded value of the key on this map, or null at the loaded baseline.</summary>
    internal long? Get(SessionMapId map, MapChangeKey key)
    {
        if (ObjectStateVocabulary.Static(key))
            return _statics.TryGetValue((map, key.Entity!, key.Property), out var staticValue) ? staticValue : null;
        if (ObjectStateVocabulary.Mobile(key))
            return _fields.TryGetValue((key.Entity!, key.Property), out var field) && field.Map == map ? field.Value : null;
        return _maps.TryGetValue(map, out var changes) && changes.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Records the key's value. A new key counts one; a routed key whose record sits on another map moves it here; the caller checked the budgets.</summary>
    internal void Set(SessionMapId map, MapChangeKey key, long value)
    {
        if (ObjectStateVocabulary.Static(key))
        {
            if (_statics.TryAdd((map, key.Entity!, key.Property), value)) { Total++; Count(_fieldCounts, map, 1); }
            else _statics[(map, key.Entity!, key.Property)] = value;
            return;
        }
        if (ObjectStateVocabulary.Mobile(key))
        {
            var id = (key.Entity!, key.Property);
            if (_fields.TryGetValue(id, out var old)) Count(_fieldCounts, old.Map, -1); else Total++;
            _fields[id] = (map, value);
            Count(_fieldCounts, map, 1);
            return;
        }
        if (!_maps.TryGetValue(map, out var changes)) _maps.Add(map, changes = []);
        if (changes.TryAdd(key, value)) Total++; else changes[key] = value;
    }

    /// <summary>Returns the key to its baseline on this map; false when it had no record there. A map left without per-map changes is dropped.</summary>
    internal bool Remove(SessionMapId map, MapChangeKey key)
    {
        if (ObjectStateVocabulary.Static(key))
        {
            if (!_statics.Remove((map, key.Entity!, key.Property))) return false;
            Count(_fieldCounts, map, -1);
            Total--;
            return true;
        }
        if (ObjectStateVocabulary.Mobile(key))
        {
            if (!_fields.TryGetValue((key.Entity!, key.Property), out var old) || old.Map != map) return false;
            _fields.Remove((key.Entity!, key.Property));
            Count(_fieldCounts, old.Map, -1);
            Total--;
            return true;
        }
        if (!_maps.TryGetValue(map, out var changes) || !changes.Remove(key)) return false;
        Total--;
        if (changes.Count == 0) _maps.Remove(map);
        return true;
    }

    /// <summary>The map a mobile object field's record names, or null when none is recorded (a key that is not a mobile field has none: a static
    /// field's record is on the map the caller names, and never moves).</summary>
    internal SessionMapId? RecordMap(MapChangeKey key) =>
        ObjectStateVocabulary.Mobile(key) && _fields.TryGetValue((key.Entity!, key.Property), out var field) ? field.Map : null;

    /// <summary>Changes per map, the object fields counted under the map their record names.</summary>
    internal Dictionary<SessionMapId, int> Counts()
    {
        var counts = _maps.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        foreach (var (map, count) in _fieldCounts) counts[map] = counts.GetValueOrDefault(map) + count;
        return counts;
    }

    /// <summary>How many changes one map holds (its per-map changes and the object fields that name it).</summary>
    internal int CountOf(SessionMapId map) => (_maps.TryGetValue(map, out var changes) ? changes.Count : 0) + _fieldCounts.GetValueOrDefault(map);

    /// <summary>How many maps hold at least one change.</summary>
    internal int MapCount => _maps.Keys.Union(_fieldCounts.Keys).Count();

    /// <summary>The maps that hold a change, ascending.</summary>
    internal IEnumerable<SessionMapId> Maps() => _maps.Keys.Union(_fieldCounts.Keys).OrderBy(map => map.RuntimeMap);

    /// <summary>The per-map changes of one map, in canonical key order, with the object fields that name it.</summary>
    internal MapChange[] ForMap(SessionMapId map)
    {
        var rows = new List<MapChange>();
        if (_maps.TryGetValue(map, out var changes)) rows.AddRange(changes.Select(pair => new MapChange(pair.Key, pair.Value)));
        if (_fieldCounts.GetValueOrDefault(map) > 0)
        {
            rows.AddRange(_fields.Where(pair => pair.Value.Map == map)
                .Select(pair => new MapChange(MapChangeKey.ForEntity(pair.Key.Entity, pair.Key.Property), pair.Value.Value)));
            rows.AddRange(_statics.Where(pair => pair.Key.Map == map)
                .Select(pair => new MapChange(MapChangeKey.ForEntity(pair.Key.Entity, pair.Key.Property), pair.Value)));
        }
        return rows.OrderBy(row => row.Key, MapChangeKeyComparer.Instance).ToArray();
    }

    /// <summary>The per-map changes the maps section saves (the object fields are not among them), one state per map, ascending.</summary>
    internal SessionMapState[] CaptureMaps() => _maps.OrderBy(pair => pair.Key.RuntimeMap)
        .Select(pair => new SessionMapState(pair.Key,
            pair.Value.OrderBy(change => change.Key, MapChangeKeyComparer.Instance).Select(change => new MapChange(change.Key, change.Value)).ToArray())).ToArray();

    /// <summary>The record of a mobile object field and its map, or none for a key that is not a mobile field or has no record.</summary>
    internal IReadOnlyList<(SessionMapId Map, long Value)> RecordsOf(string entity, string property) =>
        _fields.TryGetValue((entity, property), out var field) ? [(field.Map, field.Value)] : [];

    /// <summary>The fields of one object, by property (ordinal) then map. A mobile object has one record per property; a static entity answers the
    /// records of every map that holds one (each names its map: the identity of a static is the map and the entity together).</summary>
    internal SessionObjectField[] FieldsOf(string entity) => _fields.Where(pair => pair.Key.Entity == entity)
        .Select(pair => new SessionObjectField(entity, pair.Key.Property, pair.Value.Map, pair.Value.Value))
        .Concat(_statics.Where(pair => pair.Key.Entity == entity)
            .Select(pair => new SessionObjectField(entity, pair.Key.Property, pair.Key.Map, pair.Value)))
        .OrderBy(field => field.Property, StringComparer.Ordinal).ThenBy(field => field.Map.RuntimeMap).ToArray();

    /// <summary>The fields of one object as it stands on a map: a static object's records on that map (its identity is the map and the entity), a
    /// mobile object's fields as <see cref="FieldsOf(string)"/> answers them (a mobile object's identity does not name a map).</summary>
    internal SessionObjectField[] FieldsOf(SessionMapId map, string entity) =>
        entity.StartsWith(ObjectStateVocabulary.StaticEntityPrefix, StringComparison.Ordinal) ? FieldsOf(entity).Where(field => field.Map == map).ToArray() : FieldsOf(entity);

    /// <summary>Every object field, by entity then property then map (ordinal; a mobile object has one record per property): the objects section
    /// of the snapshot.</summary>
    internal SessionObjectField[] CaptureFields() => _fields
        .Select(pair => new SessionObjectField(pair.Key.Entity, pair.Key.Property, pair.Value.Map, pair.Value.Value))
        .Concat(_statics.Select(pair => new SessionObjectField(pair.Key.Entity, pair.Key.Property, pair.Key.Map, pair.Value)))
        .OrderBy(field => field.Entity, StringComparer.Ordinal).ThenBy(field => field.Property, StringComparer.Ordinal).ThenBy(field => field.Map.RuntimeMap).ToArray();

    private static void Count(Dictionary<SessionMapId, int> counts, SessionMapId map, int delta)
    {
        var next = counts.GetValueOrDefault(map) + delta;
        if (next > 0) counts[map] = next; else counts.Remove(map);
    }
}
