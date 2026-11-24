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

    // The script attachment properties of schema 66, frozen for the step below (ObjectStateVocabulary.Attachment as it was written): use, dialog and
    // scr<point> for the points 0..35 but 1 and 9, each with the parts counters, lf and script.
    private static bool Schema66Attachment(string property)
    {
        var dot = property.IndexOf('.');
        if (dot <= 0 || property[(dot + 1)..] is not ("counters" or "lf" or "script")) return false;
        var point = property[..dot];
        if (point is "use" or "dialog") return true;
        return point.StartsWith("scr", StringComparison.Ordinal) && (point.Length is 4 or 5) &&
            int.TryParse(point.AsSpan(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
            n is >= 0 and < 36 and not 1 and not 9 && point == "scr" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

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
        // MIG7c (ARC-DEV-094, schema 64): the object field `npc.pos` (the place a walking map NPC was left on) is new and no build before wrote one, so
        // nothing moves. The objects state the older schema carried must still be there (a damaged save gets its typed failure, not a repair).
        [63] = root =>
        {
            var objects = Section(root, "objects");
            if (!objects.TryGetPropertyValue("state", out var state)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.objects.state");
            if (state is not JsonArray) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.objects.state");
        },
        // MIG7d (ARC-DEV-095, schema 65): the links of the danger records (focus, fleeing-from, who-hit-me-last, enemy list) move into the objects section's
        // links, by holder then field, each on the map the save was on (an older session did not record the map a link was written on, and the hero's
        // map is the one the fight was on). A record left with no danger bit and no baseline is dropped (it held only links); one that was a baseline
        // before stays for the strict decode to refuse. The step does not repair damage: a handle that is not a string or null, a list that is not an
        // array and records out of order are typed failures.
        [64] = root =>
        {
            var objects = Section(root, "objects");
            if (objects.ContainsKey("links")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.objects.links");
            var danger = Section(root, "danger");
            if (!danger.TryGetPropertyValue("records", out var recordsNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.danger.records");
            var records = recordsNode as JsonArray ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.danger.records");
            var actor = Section(root, "actor");
            if (!actor.TryGetPropertyValue("map", out var mapNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.actor.map");
            if (mapNode is not JsonValue mapValue || !mapValue.TryGetValue<long>(out var map)) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.actor.map");
            var links = new JsonArray();
            string? previous = null;
            // `position` is the record's place in the schema 64 text (the path a failure names), `i` its place in the array while records are dropped.
            for (int i = 0, position = 0; i < records.Count; position++)
            {
                var path = $"$.danger.records[{position}]";
                var record = records[i] as JsonObject ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
                if (record["entity"] is not JsonValue entityValue || !entityValue.TryGetValue<string>(out var entity) || entity is null)
                    throw new DecodeException(SnapshotDecodeFailure.WrongType, path + ".entity");
                if (previous is not null && string.CompareOrdinal(previous, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
                previous = entity;
                JsonNode? Take(string name)
                {
                    if (!record.TryGetPropertyValue(name, out var node)) throw new DecodeException(SnapshotDecodeFailure.MissingField, $"{path}.{name}");
                    record.Remove(name);
                    return node;
                }
                JsonArray? One(string name)
                {
                    var node = Take(name);
                    if (node is null) return null;
                    if (node is not JsonValue handle || !handle.TryGetValue<string>(out _)) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"{path}.{name}");
                    return [node.DeepClone()];
                }
                var focus = One("focus"); var fleeing = One("fleeingFrom"); var hitBy = One("hitBy");
                var enemiesNode = Take("enemies");
                var enemies = enemiesNode as JsonArray ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, path + ".enemies");
                foreach (var enemy in enemies)
                    if (enemy is not JsonValue handle || !handle.TryGetValue<string>(out _)) throw new DecodeException(SnapshotDecodeFailure.WrongType, path + ".enemies");
                var any = focus is not null || fleeing is not null || hitBy is not null || enemies.Count > 0;
                // The fields in the canonical order of the property names: link.enemies, link.fleeing, link.focus, link.hitby.
                void Add(string property, JsonArray? targets)
                {
                    if (targets is not null) links.Add(new JsonObject { ["entity"] = entity, ["property"] = property, ["map"] = map, ["targets"] = targets });
                }
                Add("link.enemies", enemies.Count > 0 ? (JsonArray)enemies.DeepClone() : null);
                Add("link.fleeing", fleeing); Add("link.focus", focus); Add("link.hitby", hitBy);
                // A record that held links and no danger bit has nothing left to save as a bit.
                if (any && record["flags"] is JsonArray flagItems && flagItems.Count == 0) records.RemoveAt(i); else i++;
            }
            objects.Add("links", links);
        },
        // MIG8 (ARC-DEV-096, schema 66): the script attachments (the dialog, use and scr<point> parts of a mobile `oid:` or a static `sector:` entity) move out
        // of the maps into the objects section's state, as the fields of the object they belong to, and the state is put in canonical order again. A
        // mobile object that held the same attachment part under two maps keeps the lowest map's value (the older session kept one value per map and
        // map, a carried companion's included); a static keeps one record per map. A map left with no change is dropped. A second record of the same
        // static map, entity and part is not moved: it stays for the strict decode to refuse. The state rows already there must be readable, in
        // canonical order and free of attachments (schema 65 could not carry one): a save is not repaired by the move.
        [65] = root =>
        {
            var objects = Section(root, "objects");
            if (!objects.TryGetPropertyValue("state", out var stateNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.objects.state");
            var state = stateNode as JsonArray ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.objects.state");
            var rows = new List<(string Entity, string Property, long Map, JsonNode Row)>();
            foreach (var node in state)
            {
                var path = $"$.objects.state[{rows.Count}]";
                if (node is JsonObject row && row["entity"] is JsonValue e && e.TryGetValue<string>(out var rowEntity) && rowEntity is not null &&
                    row["property"] is JsonValue p && p.TryGetValue<string>(out var rowProperty) && rowProperty is not null &&
                    row["map"] is JsonValue m && m.TryGetValue<long>(out var rowMap))
                {
                    if (Schema66Attachment(rowProperty)) throw new DecodeException(SnapshotDecodeFailure.UnknownField, path + ".property");
                    if (rows.Count > 0 && CompareFields(rows[^1].Entity, rows[^1].Property, rows[^1].Map, rowEntity, rowProperty, rowMap) >= 0)
                        throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
                    rows.Add((rowEntity, rowProperty, rowMap, row.DeepClone()));
                }
                else throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
            }
            var moved = new SortedDictionary<(string Entity, string Property), (long Map, JsonNode Value)>(Comparer<(string Entity, string Property)>.Create(
                (x, y) => string.CompareOrdinal(x.Entity, y.Entity) is var order and not 0 ? order : string.CompareOrdinal(x.Property, y.Property)));
            var statics = new List<(string Entity, string Property, long Map, JsonNode Value)>();
            var seen = new HashSet<(string, string, long)>();
            var seenMobile = new HashSet<(string, string, long)>();
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
                                entity is not null && property is not null && Schema66Attachment(property) && change["value"] is { } value)
                            {
                                if (entity.StartsWith("oid:", StringComparison.Ordinal))
                                {
                                    // The same part twice under one map is damage (the older load refused it): the second stays for the restore to refuse.
                                    if (!seenMobile.Add((entity, property, mapNumber))) { j++; continue; }
                                    moved.TryAdd((entity, property), (mapNumber, value.DeepClone()));
                                    changes.RemoveAt(j);
                                    continue;
                                }
                                if (entity.StartsWith("sector:", StringComparison.Ordinal) && seen.Add((entity, property, mapNumber)))
                                {
                                    statics.Add((entity, property, mapNumber, value.DeepClone()));
                                    changes.RemoveAt(j);
                                    continue;
                                }
                            }
                            j++;
                        }
                        if (before > 0 && changes.Count == 0) { maps.RemoveAt(i); continue; }
                    }
                    i++;
                }
            }
            if (moved.Count == 0 && statics.Count == 0) return;
            foreach (var (key, record) in moved)
                rows.Add((key.Entity, key.Property, record.Map, new JsonObject { ["entity"] = key.Entity, ["property"] = key.Property, ["map"] = record.Map, ["value"] = record.Value }));
            foreach (var (entity, property, map, value) in statics)
                rows.Add((entity, property, map, new JsonObject { ["entity"] = entity, ["property"] = property, ["map"] = map, ["value"] = value }));
            rows.Sort((x, y) => CompareFields(x.Entity, x.Property, x.Map, y.Entity, y.Property, y.Map));
            var merged = new JsonArray();
            foreach (var row in rows) merged.Add(row.Row);
            objects["state"] = merged;
        },
        // MIG8 (ARC-DEV-098, schema 67): the scripts section, the global variables and the PC's flags and variables. An older session had no store for
        // them (a script that used one was refused), so every one of them was zero; the step adds the empty section and keeps nothing else.
        [66] = root =>
        {
            if (root.ContainsKey("scripts")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.scripts");
            root.Add("scripts", new JsonObject { ["globalVariables"] = new JsonArray(), ["pcFlags"] = new JsonArray(), ["pcVariables"] = new JsonArray() });
        },
        // MIG9 S6 (ARC-DEV-106, schema 68): the residency section, the open map's sector eviction history. A session of an older schema kept none (it
        // saved no cache), so the step adds the empty section: the first load folds every sector once, as the original does with a save that lists none.
        [67] = root =>
        {
            if (root.ContainsKey("residency")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.residency");
            root.Add("residency", new JsonObject { ["map"] = null, ["history"] = new JsonArray() });
        },
        // MIG11 S5 (ARC-DEV-116, schema 69): the object field `npc.teleport` (an NPC's teleport map, FM10) is new and no build before wrote one, so
        // nothing moves. The objects state the older schema carried must still be there (a damaged save gets its typed failure, not a repair).
        [68] = root =>
        {
            var objects = Section(root, "objects");
            if (!objects.TryGetPropertyValue("state", out var state)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.objects.state");
            if (state is not JsonArray) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.objects.state");
        },
        // MIG12 S3 (ARC-DEV-122, schema 70): the clock section gains the real and animation stamps (TL34). An older session kept one clock: real
        // starts at 1 ms, as for a new session, and animation is the saved game stamp, the nearest consistent value (the two moved together in
        // every older save except inside a turn-based round, where the animation clock was never advanced and the difference is not recoverable).
        // The game stamp the older schema carried must be there and a number (a damaged save gets its typed failure, not a repair); its range is
        // the strict decode's.
        [69] = root =>
        {
            var clock = Section(root, "clock");
            if (!clock.TryGetPropertyValue("timestampMs", out var game)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.clock.timestampMs");
            if (game is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
                throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.clock.timestampMs");
            AddField(clock, "$.clock", "realMs", JsonValue.Create(1L));
            AddField(clock, "$.clock", "animationMs", value.DeepClone());
        },
        // ARC-DEV-205 (bug B22, schema 71): the story section gains the town maps revealed whole (UF1). No older build revealed one (unfog town map was
        // refused), so the step adds the empty list. The story section the older schema carried must be there and an object.
        [70] = root => AddField(Section(root, "story"), "$.story", "revealedTownMaps", new JsonArray()),
        // MIG14 S2 (ARC-DEV-208, schema 72): the goals section, the goal runs of the active map. No older build saved a run, so the step adds the empty
        // section (the next run id 1, as a new session's table starts). A session of an older schema has no section yet.
        [71] = root =>
        {
            if (root.ContainsKey("goals")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.goals");
            root.Add("goals", new JsonObject { ["nextRunId"] = 1, ["runs"] = new JsonArray() });
        },
        // MIG14 S4 (ARC-DEV-210, schema 73): the spells section, the settled spell runs with the maintain bar. No older build saved a run, so the step adds the empty
        // section (no run, five empty slots, no charged cost).
        [72] = root =>
        {
            if (root.ContainsKey("spells")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.spells");
            var bar = new JsonArray();
            for (var slot = 0; slot < 5; slot++) bar.Add(new JsonObject { ["run"] = -1, ["state"] = 0 });
            root.Add("spells", new JsonObject { ["runs"] = new JsonArray(), ["bar"] = bar, ["lastCost"] = 0 });
        },
        // ARC-DEV-230 (schema 74): the newspaper section, the issue queue. No older build had one, so the step adds a new game's queue (four words of -1, the head 5000, the
        // rest 0, count 5). The object field `npc.origin` (an NPC's origin) is new too and no build before wrote one, so no object state moves.
        [73] = root =>
        {
            if (root.ContainsKey("newspaper")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.newspaper");
            // Frozen literals: 25 words, words 0..3 = -1, word 4 = 5000, the rest 0, count 5.
            var entries = new JsonArray();
            for (var i = 0; i < 25; i++) entries.Add(i < 4 ? -1 : i == 4 ? 5000 : 0);
            root.Add("newspaper", new JsonObject { ["entries"] = entries, ["count"] = 5 });
        },
        // ARC-WORLD-122 (schema 75): the story section gains the cells of town maps the PC revealed by walking (WM78). No older build kept one, so the step adds the empty
        // list. The story section the older schema carried must be there and an object.
        [74] = root => AddField(Section(root, "story"), "$.story", "townMapCells", new JsonArray()),
        // ARC-UI-090 (schema 76): the hero's gold is a pile in the inventory grid, the record `gold:pc` with a cell (ledger HG1). An older save kept the
        // coins as a counter only, so the step gives a hero with any coin the first free cell of the grid for a 1 x 1 pile (the first free place row by
        // row, as the session finds one; the art's size lays it out again when a host attaches the art sources). A save that already lists the pile
        // (one written by the build of this schema) is kept as it is; a full grid leaves the pile without a cell, as the session does.
        [75] = root =>
        {
            var inventory = Section(root, "inventory");
            if (!inventory.TryGetPropertyValue("gold", out var goldNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.inventory.gold");
            if (goldNode is not JsonValue goldValue || !goldValue.TryGetValue<long>(out var gold)) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.inventory.gold");
            if (!inventory.TryGetPropertyValue("grid", out var gridNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.inventory.grid");
            if (gridNode is not JsonArray grid) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.inventory.grid");
            // Frozen literals: the grid is 10 columns by 12 rows, the pile's entity.
            const int columns = 10, rows = 12;
            const string goldEntity = "gold:pc";
            if (gold <= 0) return;
            var occupied = new bool[columns * rows];
            var at = 0;
            foreach (var node in grid)
            {
                if (node is not JsonObject item || item["entity"] is not JsonValue entity || !entity.TryGetValue<string>(out var name) ||
                    item["slot"] is not JsonValue slotValue || !slotValue.TryGetValue<long>(out var slot) ||
                    item["width"] is not JsonValue widthValue || !widthValue.TryGetValue<long>(out var width) ||
                    item["height"] is not JsonValue heightValue || !heightValue.TryGetValue<long>(out var height)) return; // the strict decode refuses it with its path
                if (name == goldEntity) return;
                if (slot < 0 || width < 1 || height < 1) return;
                for (var row = 0; row < height; row++)
                    for (var column = 0; column < width; column++)
                    {
                        var cell = slot + row * columns + column;
                        if (slot % columns + column < columns && cell < occupied.Length) occupied[cell] = true;
                    }
            }
            var free = System.Array.IndexOf(occupied, false);
            if (free < 0) return;
            while (at < grid.Count && grid[at] is JsonObject listed && listed["slot"] is JsonValue listedSlot && listedSlot.TryGetValue<long>(out var listedAt) && listedAt < free) at++;
            grid.Insert(at, new JsonObject { ["entity"] = goldEntity, ["slot"] = free, ["width"] = 1, ["height"] = 1 });
        },
        // ARC-UI-081 (schema 77): the story section gains the PC's key numbers. No older build kept a key, so the step adds the empty list.
        [76] = root => AddField(Section(root, "story"), "$.story", "heroKeys", new JsonArray()),
        // ARC-UI-082 (schema 78): the story section gains the PC's kill statistics and injuries. No older build kept any, so the step adds a new game's empty
        // log (no kill, six empty slots, no injury). The story section the older schema carried must be there and an object.
        [77] = root =>
        {
            var slots = new JsonArray();
            for (var i = 0; i < 6; i++) slots.Add(new JsonArray(0, 0));
            AddField(Section(root, "story"), "$.story", "killLog", new JsonObject { ["total"] = 0, ["records"] = slots, ["injuries"] = new JsonArray() });
        },
        // ARC-DEV-266 (schema 79): the PC's flag elements and variables move from the scripts section onto the hero's record (fields 272 and 273 of
        // the PC, SA14), unchanged. A schema 78 session without a hero that held PC cells was refused at its load (InvalidSnapshot); the step refuses
        // it the same way. Both lists must be there; their shapes are the strict decode's.
        [78] = root =>
        {
            var scripts = Section(root, "scripts");
            var flags = Take(scripts, "$.scripts", "pcFlags");
            var variables = Take(scripts, "$.scripts", "pcVariables");
            if (!root.TryGetPropertyValue("hero", out var hero)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.hero");
            if (hero is null)
            {
                if (flags is not JsonArray flagRows) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.scripts.pcFlags");
                if (variables is not JsonArray variableRows) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.scripts.pcVariables");
                if (flagRows.Count > 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, "$.scripts.pcFlags");
                if (variableRows.Count > 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, "$.scripts.pcVariables");
                return;
            }
            // Lists that are not arrays move too and are refused by the strict decode at their new path ($.hero.scripts.flags, .variables).
            AddField(hero as JsonObject ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.hero"), "$.hero", "scripts",
                new JsonObject { ["flags"] = flags, ["variables"] = variables });
        },
        // ARC-DEV-288 (schema 80): the flights section, the projectiles in flight with their shots. No older build saved one (a projectile in flight was lost at the save),
        // so the step adds the empty section with both counters at 0, a new game's.
        [79] = root =>
        {
            if (root.ContainsKey("flights")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.flights");
            root.Add("flights", new JsonObject
            {
                ["lastProjectile"] = 0, ["projectiles"] = new JsonArray(), ["nextShot"] = 0, ["heroShots"] = new JsonArray(), ["npcThrows"] = new JsonArray(), ["npcArrows"] = new JsonArray(),
            });
        },
        // ARC-UI-104 (schema 81): the story section gains the notes of the town maps (WM91, WM127). No older build kept any, so the step adds the empty list. The
        // story section the older schema carried must be there and an object.
        [80] = root => AddField(Section(root, "story"), "$.story", "townNotes", new JsonArray()),
        // ARC-UI-110 (schema 82): the story section gains the notes of the world map's views (Steamwoven rules). No older build kept any, so the step adds the empty list.
        // The story section the older schema carried must be there and an object.
        [81] = root => AddField(Section(root, "story"), "$.story", "mapNotes", new JsonArray()),
        // ARC-DEV-302 (schema 83): the spells section gains the flags the spell components wrote on critters (ObjFlag, MT13). No older build wrote any, so the step adds the
        // empty list. The spells section the older schema carried must be there and an object.
        [82] = root => AddField(Section(root, "spells"), "$.spells", "objectFlags", new JsonArray()),
        // ARC-DEV-289 (schema 84): the flights section gains the spell missiles in flight with their counter. No older build saved one (a missile in flight was refused at the
        // save), so the step adds the empty list with the counter at 0, a new game's. The flights section the older schema carried must be there and an object.
        [83] = root =>
        {
            var flights = Section(root, "flights");
            AddField(flights, "$.flights", "lastMissile", 0);
            AddField(flights, "$.flights", "missiles", new JsonArray());
        },
        // ARC-DEV-347 (schema 85): the objects section gains the created scenery and eye-candy objects with their counter. No older build created one, so the step adds the empty list with
        // the counter at 0, a new game's. The objects section the older schema carried must be there and an object.
        [84] = root =>
        {
            var objects = Section(root, "objects");
            AddField(objects, "$.objects", "created", new JsonArray());
            AddField(objects, "$.objects", "lastCreatedNumber", 0);
        },
        // ARC-DEV-351 (schema 86): the object field `obj.teleported` (the mark a change of place to another map leaves on a carried object, OF_TELEPORTED) is new and no build
        // before wrote one, so nothing moves. The objects state the older schema carried must still be there (a damaged save gets its typed failure, not a repair).
        [85] = root =>
        {
            var objects = Section(root, "objects");
            if (!objects.TryGetPropertyValue("state", out var state)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.objects.state");
            if (state is not JsonArray) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.objects.state");
        },
        // ARC-DEV-348 (schema 87): the effects section gains the effect lists of NPCs. No older build kept any, so the step adds the empty list. The effects section the older
        // schema carried must be there and an object.
        [86] = root => AddField(Section(root, "effects"), "$.effects", "critters", new JsonArray()),
        // ARC-DEV-358 (schema 88): each written-flags row of the spells section gains the object-flags change (obj_f_flags: the invisible, shrunk, water-walking, stoned and don't-draw
        // side effects of the spell flags). No older build wrote one, so the step adds both words as 0 to every row. The spells section and its rows the older schema carried must be there.
        [87] = root =>
        {
            var spells = Section(root, "spells");
            if (!spells.TryGetPropertyValue("objectFlags", out var rows)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.spells.objectFlags");
            if (rows is not JsonArray array) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.spells.objectFlags");
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject row) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"$.spells.objectFlags[{i}]");
                AddField(row, $"$.spells.objectFlags[{i}]", "objectSet", 0);
                AddField(row, $"$.spells.objectFlags[{i}]", "objectCleared", 0);
            }
        },
        // ARC-MAGIC-040 (schema 89): the spells section gains the monster bodies of the Trait component. No older build kept any, so the step adds the empty list. The spells section the
        // older schema carried must be there and an object.
        [88] = root => AddField(Section(root, "spells"), "$.spells", "traitArt", new JsonArray()),
        // ARC-MAGIC-047 (schema 90): each written-flags row of the spells section gains the second critter flags and the blit flags change (the Trait component's elemental flags, MG10). No older
        // build wrote one, so the step adds the four words as 0 to every row. The spells section and its rows the older schema carried must be there.
        [89] = root =>
        {
            var spells = Section(root, "spells");
            if (!spells.TryGetPropertyValue("objectFlags", out var rows)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.spells.objectFlags");
            if (rows is not JsonArray array) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.spells.objectFlags");
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject row) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"$.spells.objectFlags[{i}]");
                foreach (var name in new[] { "critter2Set", "critter2Cleared", "blitSet", "blitCleared" }) AddField(row, $"$.spells.objectFlags[{i}]", name, 0);
            }
        },
        // ARC-WORLD-1157 (schema 91): the generators section, the monster generators' counter table, the creatures they made and the slot mark of the critters they made. No older build ran a
        // generator, so the step adds the empty section (a new game's table).
        [90] = root =>
        {
            if (root.ContainsKey("generators")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.generators");
            root.Add("generators", new JsonObject { ["slots"] = new JsonArray(), ["made"] = new JsonArray(), ["copies"] = new JsonArray() });
        },
        // ARC-MAGIC-1234 (schema 92): each run row of the spells section gains its object list (the original's +0x130). No older build stored one (its runs that name a list in their END area never found
        // it), so the step adds the empty list to every row. The spells section and its run rows the older schema carried must be there.
        [91] = root =>
        {
            var spells = Section(root, "spells");
            if (!spells.TryGetPropertyValue("runs", out var rows)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.spells.runs");
            if (rows is not JsonArray array) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.spells.runs");
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject row) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"$.spells.runs[{i}]");
                AddField(row, $"$.spells.runs[{i}]", "objectList", new JsonArray());
            }
        },
        // ARC-MAGIC-1142 (schema 93): each written-flags row of the spells section gains the NPC flags change (the charm's kill-on-sight override and the attack event's clear). No older build wrote
        // one, so the step adds the two words as 0 to every row. The spells section and its rows the older schema carried must be there.
        [92] = root =>
        {
            var spells = Section(root, "spells");
            if (!spells.TryGetPropertyValue("objectFlags", out var rows)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.spells.objectFlags");
            if (rows is not JsonArray array) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.spells.objectFlags");
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject row) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"$.spells.objectFlags[{i}]");
                foreach (var name in new[] { "npcSet", "npcCleared" }) AddField(row, $"$.spells.objectFlags[{i}]", name, 0);
            }
        },
        // ARC-SCRIPT-1279 (schema 94): the inventory section gains the item fields scripts wrote (written start, inventory display bit). No older build kept any, so the step adds the empty list. The inventory section the older schema carried must be there and an object.
        [93] = root => AddField(Section(root, "inventory"), "$.inventory", "itemFields", new JsonArray()),
        // ARC-RES-1441 (schema 95): the inventory section gains the arranged slots of seed holders. No older build saved any (an arrangement was transient), so the step adds the empty list. The inventory section the older schema carried must be there and an object.
        [94] = root => AddField(Section(root, "inventory"), "$.inventory", "holderSlots", new JsonArray()),
        // ARC-SCRIPT-1136 (schema 96): each party record gains the wait flag (the WAIT bit of an NPC the PC does not hold as waiting). No older build could hold it (the dialog wait was refused for an NPC outside the party), so the
        // step adds false to every follower and every other record. The party section and its two lists the older schema carried must be there.
        [95] = root =>
        {
            var party = Section(root, "party");
            foreach (var name in new[] { "followers", "others" })
            {
                if (!party.TryGetPropertyValue(name, out var rows)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.party." + name);
                if (rows is not JsonArray array) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.party." + name);
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JsonObject row) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"$.party.{name}[{i}]");
                    AddField(row, $"$.party.{name}[{i}]", "waitFlag", false);
                }
            }
            },
        // ARC-WORLD-1159 (schema 97): the encounters section, the use counts of the world-map random encounter rows. No older build rolled an encounter, so the step adds the empty section.
        [96] = root =>
        {
            if (root.ContainsKey("encounters")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.encounters");
            root.Add("encounters", new JsonObject { ["uses"] = new JsonArray() });
        },
        // ARC-UI-1542 (schema 98): the lit state of the top strip's buttons. No older build saved it (the lights lived in the viewer for the session only), so the step adds null: all idle, the face from the hero's sector.
        [97] = root =>
        {
            if (root.ContainsKey("barLights")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.barLights");
            root.Add("barLights", null);
        },
        // ARC-MAGIC-1658 (schema 99): the hero's stored resistance array (field 31) and stored age (stat 25). No older build kept either (the hero's resistances were the worn armour's and the effects' alone, so the array starts at zero
        // as a new hero's does; its age was never read, so it is null, which the TraitIdx component takes as not ready).
        [98] = root =>
        {
            if (!root.TryGetPropertyValue("hero", out var heroNode)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.hero");
            if (heroNode is JsonObject hero)
            {
                AddField(hero, "$.hero", "resistances", new JsonArray(0, 0, 0, 0, 0));
                if (hero.ContainsKey("age")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.hero.age");
                hero.Add("age", null);
            }
        },
        // ARC-TECHNOLOGY-1635 (schema 100): the flights section gains the native traps' shots in flight. No older build flew a trap's shot (it struck at once), so the list is empty.
        [99] = root =>
        {
            AddField(Section(root, "flights"), "$.flights", "trapShots", new JsonArray());
        },
        // ARC-UI-1740 (schema 101): the skill module's pre-thrown gambling roll. No older build held one (there was no gambling), so the step adds null: the next barter window throws the first.
        [100] = root =>
        {
            if (root.ContainsKey("gamblingRoll")) throw new DecodeException(SnapshotDecodeFailure.UnknownField, "$.gamblingRoll");
            root.Add("gamblingRoll", null);
        },
        // ARC-WORLD-1840 (schema 102): the party section gains the followers of NPC leaders. No older build made an NPC lead anyone (the hero was the one leader), so the list is empty.
        [101] = root => AddField(Section(root, "party"), "$.party", "npcLed", new JsonArray()),
        // ARC-MAGIC-2371 (schema 103): the durability section gains the changed mana stores of worn items (field 92). No older build drained one (the stores were the prototype's), so the list is empty.
        [102] = root => AddField(Section(root, "durability"), "$.durability", "manaStores", new JsonArray()),
        // ARC-MAGIC-2400 (schema 104): a pending recharge event names the map it lives on. A schema 103 build named a placeholder map (1) and fired it on any map, so the step puts each such event on the map the PC was
        // on when the save was made (the actor's map), where the events of the PC's own items were: the weapons of the party were drained there, and the original would have them in memory on that map.
        [103] = root =>
        {
            var map = Section(root, "actor")["map"] ?? throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.actor.map");
            if (Section(root, "events")["pending"] is not JsonArray pending) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.events.pending");
            foreach (var node in pending)
                if (node is JsonObject row && row["kind"]?.GetValue<string>() == SessionEventKinds.ItemRecharge && row["entities"] is JsonArray { Count: 1 } entities && entities[0] is JsonObject entity)
                    entity["map"] = map.DeepClone();
        },
        // ARC-SCRIPT-2594 (schema 105): the tile an item last lay on after the session dropped it and took it up again. An older save kept none, so the section starts empty (such an item keeps the tile its map data gives it, as before).
        [104] = root => AddField(Section(root, "inventory"), "$.inventory", "lastDrops", new JsonArray()),
        // ARC-AI-1578 (schema 106): the silent-move adjustments of worn armour (`silentMove`, `magicSilentMove` in a worn item's armor block). A schema 105 save did not hold them, so each worn armour gets 0, as the older load
        // read it; putting the armour on again fills them from the item's data.
        [105] = root =>
        {
            if (!root.TryGetPropertyValue("equipment", out var equipment)) throw new DecodeException(SnapshotDecodeFailure.MissingField, "$.equipment");
            if (equipment is not JsonArray worn) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$.equipment");
            for (var i = 0; i < worn.Count; i++)
            {
                if (worn[i] is not JsonObject row || row["item"] is not JsonObject item || item["armor"] is not JsonObject armor) continue;
                var path = $"$.equipment[{i}].item.armor";
                AddField(armor, path, "silentMove", JsonValue.Create(0L));
                AddField(armor, path, "magicSilentMove", JsonValue.Create(0L));
            }
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

    // Removes a field the older schema must carry and hands it back, detached, for a step that moves it.
    private static JsonNode? Take(JsonObject section, string path, string name)
    {
        if (!section.TryGetPropertyValue(name, out var value)) throw new DecodeException(SnapshotDecodeFailure.MissingField, $"{path}.{name}");
        section.Remove(name);
        return value;
    }

    private static void AddField(JsonObject section, string path, string name, JsonNode value)
    {
        if (section.ContainsKey(name)) throw new DecodeException(SnapshotDecodeFailure.UnknownField, $"{path}.{name}");
        section.Add(name, value);
    }
}
