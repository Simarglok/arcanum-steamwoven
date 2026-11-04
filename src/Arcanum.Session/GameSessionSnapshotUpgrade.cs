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
