using System.Text.Json;

namespace Arcanum.Session;

public static partial class GameSessionSnapshotCodec
{
    // ARC-WORLD-1159 (schema 92): the use counts of the world-map random encounter rows that fired, ascending by row.
    private static readonly string[] EncountersFields = ["uses"];
    private static readonly string[] EncounterUseFields = ["line", "count"];

    private static void WriteEncounters(Utf8JsonWriter w, SessionEncounterState encounters)
    {
        w.WriteStartObject("encounters");
        w.WriteStartArray("uses");
        foreach (var use in encounters.Uses.OrderBy(use => use.Line))
        {
            w.WriteStartObject();
            w.WriteNumber("line", use.Line);
            w.WriteNumber("count", use.Count);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    // Shapes and ranges only; a line twice is Restore's refusal.
    private static SessionEncounterState Encounters(JsonElement element)
    {
        const string p = "$.encounters";
        var f = Fields(element, p, EncountersFields);
        var uses = Array(f["uses"], p + ".uses", SessionEncounterState.MaxUses).Select((row, i) =>
        {
            var path = $"{p}.uses[{i}]";
            var g = Fields(row, path, EncounterUseFields);
            return new SessionEncounterUse(Int(g["line"], path + ".line", 50000, int.MaxValue), Int(g["count"], path + ".count", 1, SessionEncounterState.MaxCount));
        }).ToArray();
        return new(uses);
    }
}
