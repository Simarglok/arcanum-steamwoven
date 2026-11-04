using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-037: older saves load in newer builds. A snapshot of a schema from <see cref="OldestReadableSchemaVersion"/> up to the
/// one before <see cref="GameSessionSnapshot.CurrentSchemaVersion"/> is upgraded one schema at a time, each step turning the text of
/// schema N into the text of schema N + 1 (the fields that schema added, with the value an older session means), and only then
/// decoded, with every check of the current schema. A step never guesses past what the older schema could hold: a field the older
/// schema cannot carry is <see cref="SnapshotDecodeFailure.UnknownField"/>, a section it must carry is missing or of the wrong type
/// as the strict decode reports it. Older schemas stay refused (<see cref="SnapshotDecodeFailure.UnsupportedSchema"/>), as do newer
/// ones. The save file itself is not rewritten; the next save of the loaded session writes the current schema.
/// A schema bump adds its step here (the session tests fail without one), or moves <see cref="OldestReadableSchemaVersion"/> up
/// with the owner's agreement.
/// </summary>
public static partial class GameSessionSnapshotCodec
{
    /// <summary>The oldest schema this build loads: 56, the schema of the first packaged build (ARC-DEV-047).</summary>
    public const int OldestReadableSchemaVersion = 56;

    // The canonical order of the object state rows: entity, then property (both ordinal), then map.
    private static int CompareFields(string entityX, string propertyX, long mapX, string entityY, string propertyY, long mapY) =>
        string.CompareOrdinal(entityX, entityY) is var order and not 0 ? order :
        string.CompareOrdinal(propertyX, propertyY) is var byProperty and not 0 ? byProperty : mapX.CompareTo(mapY);

    // The vocabularies of the schemas the steps below were written for, frozen here: a later build that adds a field to the current vocabulary
    // must not change what an older save's step moves.
    private static readonly IReadOnlySet<string> Schema62MobileFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "obj.off", "obj.destroyed", "npc.cell", "npc.map", "npc.home", "npc.faction", "follow.pc", "item.dropped",
        "object.damage", "object.condition", "lock.flags", "lock.frame", "lock.kind", "trap.state",
    };

    private static readonly IReadOnlySet<string> Schema63StaticFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "obj.off", "obj.destroyed", "object.damage", "object.condition", "lock.flags", "lock.frame", "lock.kind", "trap.state",
    };

    // Each step upgrades a root of schema `key` to schema `key + 1`; the caller then sets its schemaVersion. A step reaches its
    // sections through Section and adds through AddField only, so a damaged older save gets its typed failure and path; the
    // node tree's own exceptions are mapped below as damaged text.
    private static readonly IReadOnlyDictionary<int, Action<JsonObject>> UpgradeSteps = new Dictionary<int, Action<JsonObject>>
    {
        // ARC-WORLD-103 (schema 57): no NPC was dressed by a regeneration, so each NPC keeps its map data's inventory locations.
        [56] = root => AddField(Section(root, "inventory"), "$.inventory", "npcWorn", new JsonArray()),
        // ARC-RENDER-041 (schema 58): no death left a pool of blood.
        [57] = root => AddField(Section(root, "inventory"), "$.inventory", "bloodPools", new JsonArray()),
        // GR19 (ARC-DEV-078, schema 59): a saved round gets the subturn starts the older load restored, none.
        [58] = root =>
        {
            if (!root.TryGetPropertyValue("combat", out var combat)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.combat");
            if (combat is null) return;
            AddField(combat as JsonObject ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.combat"), "$.combat", "subturnStarts", JsonValue.Create(0L));
        },
        // MIG6 (ARC-DEV-084, schema 60): the junk and pool high-water marks; 0 means the older load's rule, the highest number held now.
        [59] = root =>
        {
            var inventory = Section(root, "inventory");
            AddField(inventory, "$.inventory", "lastJunkNumber", JsonValue.Create(0L));
            AddField(inventory, "$.inventory", "lastPoolNumber", JsonValue.Create(0L));
        },
        // MIG6d-3 (ARC-DEV-091, schema 61): the junk piles, the pools of blood and the two number marks move out of the inventory section into
        // the new objects section, as they were saved, in the same order. A schema 60 inventory that lacks one of the four is damaged.
        [60] = root =>
        {
            var inventory = Section(root, "inventory");
            if (root.ContainsKey("objects")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.objects");
            var objects = new JsonObject();
            foreach (var name in new[] { "junkPiles", "bloodPools", "lastJunkNumber", "lastPoolNumber" })
            {
                if (!inventory.TryGetPropertyValue(name, out var value)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.inventory." + name);
                inventory.Remove(name);
                objects.Add(name, value);
            }
            root.Add("objects", objects);
        },
        // MIG7a (ARC-DEV-092, schema 62): the entity changes of mobile map-data objects (the object fields of ObjectStateVocabulary on an `oid:`
        // entity) move out of the maps into the objects section's state, by entity then property. An entity that held the same property under
        // two maps keeps the lowest map's value (the older session kept one value per map; no build wrote one key under two maps). A map left
        // with no change is dropped. Anything a step cannot read stays where it was for the strict decode to refuse with its path.
        [61] = root =>
        {
            var objects = Section(root, "objects");
            if (objects.ContainsKey("state")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.objects.state");
            var moved = new SortedDictionary<(string Entity, string Property), (long Map, JsonNode Value)>(Comparer<(string Entity, string Property)>.Create(
                (x, y) => string.CompareOrdinal(x.Entity, y.Entity) is var order and not 0 ? order : string.CompareOrdinal(x.Property, y.Property)));
            if (root.TryGetPropertyValue("maps", out var mapsNode) && mapsNode is JsonArray maps)
            {
                for (var i = 0; i < maps.Count;)
                {
                    if (maps[i] is JsonObject map && map["changes"] is JsonArray changes && map["map"] is JsonValue mapValue && mapValue.TryGetValue<long>(out var mapNumber))
                    {
                        for (var j = 0; j < changes.Count;)
                        {
                            if (changes[j] is JsonObject change && change["scope"] is JsonValue scope && scope.TryGetValue<string>(out var kind) && kind == "entity" &&
                                change["entity"] is JsonValue entityValue && entityValue.TryGetValue<string>(out var entity) &&
                                change["property"] is JsonValue propertyValue && propertyValue.TryGetValue<string>(out var property) &&
                                entity is not null && entity.StartsWith("oid:", StringComparison.Ordinal) && property is not null && Schema62MobileFields.Contains(property) && change["value"] is { } value)
                            {
                                moved.TryAdd((entity, property), (mapNumber, value.DeepClone()));
                                changes.RemoveAt(j);
                            }
                            else j++;
                        }
                        if (changes.Count == 0) { maps.RemoveAt(i); continue; }
                    }
                    i++;
                }
            }
            var state = new JsonArray();
            foreach (var (key, record) in moved)
                state.Add(new JsonObject { ["entity"] = key.Entity, ["property"] = key.Property, ["map"] = record.Map, ["value"] = record.Value });
            objects.Add("state", state);
        },
        // MIG7b (ARC-DEV-093, schema 63): the entity changes of static map-data objects (a `sector:` entity and one of the static fields) move out of
        // the maps into the objects section's state, each kept with the map it was under (a static's identity is the map and the entity together), and
        // the state is put in canonical order again (entity, property, map). A map left with no change is dropped. A second record of the same map,
        // entity and property is not moved: it stays for the strict decode to refuse. Anything a step cannot read stays where it was.
        [62] = root =>
        {
            var objects = Section(root, "objects");
            if (!objects.TryGetPropertyValue("state", out var stateNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.objects.state");
            var state = stateNode as JsonArray ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.objects.state");
            var moved = new List<(string Entity, string Property, long Map, JsonNode Value)>();
            var seen = new HashSet<(string, string, long)>();
            if (root.TryGetPropertyValue("maps", out var mapsNode) && mapsNode is JsonArray maps)
            {
                for (var i = 0; i < maps.Count;)
                {
                    if (maps[i] is JsonObject map && map["changes"] is JsonArray changes && map["map"] is JsonValue mapValue && mapValue.TryGetValue<long>(out var mapNumber))
                    {
                        var before = changes.Count;
                        for (var j = 0; j < changes.Count;)
                        {
                            if (changes[j] is JsonObject change && change["scope"] is JsonValue scope && scope.TryGetValue<string>(out var kind) && kind == "entity" &&
                                change["entity"] is JsonValue entityValue && entityValue.TryGetValue<string>(out var entity) &&
                                change["property"] is JsonValue propertyValue && propertyValue.TryGetValue<string>(out var property) &&
                                entity is not null && entity.StartsWith("sector:", StringComparison.Ordinal) && property is not null && Schema63StaticFields.Contains(property) && change["value"] is { } value &&
                                seen.Add((entity, property, mapNumber)))
                            {
                                moved.Add((entity, property, mapNumber, value.DeepClone()));
                                changes.RemoveAt(j);
                            }
                            else j++;
                        }
                        if (before > 0 && changes.Count == 0) { maps.RemoveAt(i); continue; }
                    }
                    i++;
                }
            }
            // The existing rows are the mobile objects' (schema 62 holds no static object there), already in canonical order: a row the step cannot
            // read, a `sector:` row or a row out of order is damage, found here whether or not anything moves, so a save is not repaired by the move.
            var rows = new List<(string Entity, string Property, long Map, JsonNode Row)>();
            foreach (var node in state)
            {
                var path = $"$.objects.state[{rows.Count}]";
                if (node is JsonObject row && row["entity"] is JsonValue e && e.TryGetValue<string>(out var rowEntity) && rowEntity is not null &&
                    row["property"] is JsonValue p && p.TryGetValue<string>(out var rowProperty) && rowProperty is not null &&
                    row["map"] is JsonValue m && m.TryGetValue<long>(out var rowMap))
                {
                    if (rowEntity.StartsWith("sector:", StringComparison.Ordinal)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
                    if (rows.Count > 0 && CompareFields(rows[^1].Entity, rows[^1].Property, rows[^1].Map, rowEntity, rowProperty, rowMap) >= 0)
                        throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
                    rows.Add((rowEntity, rowProperty, rowMap, row.DeepClone()));
                }
                else throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
            }
            if (moved.Count == 0) return;
            foreach (var (entity, property, map, value) in moved)
                rows.Add((entity, property, map, new JsonObject { ["entity"] = entity, ["property"] = property, ["map"] = map, ["value"] = value }));
            rows.Sort((x, y) => CompareFields(x.Entity, x.Property, x.Map, y.Entity, y.Property, y.Map));
            var merged = new JsonArray();
            foreach (var row in rows) merged.Add(row.Row);
            objects["state"] = merged;
        },
    };

    /// <summary>The schemas with an upgrade step to the next one, ascending: every schema from
    /// <see cref="OldestReadableSchemaVersion"/> up to the one before the current.</summary>
    internal static IReadOnlyList<int> UpgradableSchemaVersions => UpgradeSteps.Keys.Order().ToArray();

    // The schema of a snapshot this build upgrades, or null (the current schema, or a text the strict decode refuses as it is).
    private static int? UpgradableVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.ValueEquals(Format) &&
        root.TryGetProperty("schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number &&
        schema.TryGetInt32(out var version) && version >= OldestReadableSchemaVersion && version < GameSessionSnapshot.CurrentSchemaVersion
            ? version : null;

    // The text of `version` upgraded to the current schema, or the failure that stops it.
    private static string Upgrade(string text, int version)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(text, null, new JsonDocumentOptions
            {
                MaxDepth = 8, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow,
            }) as JsonObject ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$");
            for (var from = version; from < GameSessionSnapshot.CurrentSchemaVersion; from++)
            {
                if (!UpgradeSteps.TryGetValue(from, out var step)) throw new DecodeException(SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
                step(root);
                root["schemaVersion"] = from + 1;
            }
            return root.ToJsonString();
        }
        // A lone UTF-16 surrogate escaped in a string has no string value to write back.
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw new DecodeException(SnapshotDecodeFailure.MalformedJson, "$"); }
        // The document parsed already, so this is a repeated property name in an object a step unpacked (the root or a section it
        // extends), which the node tree refuses as it is built; a repeat elsewhere passes through to the strict decode and its path.
        catch (ArgumentException) { throw new DecodeException(SnapshotDecodeFailure.DuplicateField, "$"); }
    }

    private static JsonObject Section(JsonObject root, string name)
    {
        if (!root.TryGetPropertyValue(name, out var section)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$." + name);
        return section as JsonObject ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$." + name);
    }

    private static void AddField(JsonObject section, string path, string name, JsonNode value)
    {
        if (section.ContainsKey(name)) throw new DecodeException(SnapshotDecodeFailure.UnknownField, $"{path}.{name}");
        section.Add(name, value);
    }
}
