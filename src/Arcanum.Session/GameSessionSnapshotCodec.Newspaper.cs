using System.Text.Json;

namespace Arcanum.Session;

public static partial class GameSessionSnapshotCodec
{
    // ARC-DEV-230 (schema 74): the newspaper issue queue, the 25 words of the table and its count.
    private static readonly string[] NewspaperFields = ["entries", "count"];

    private static void WriteNewspaper(Utf8JsonWriter w, SessionNewspaperState newspaper)
    {
        w.WriteStartObject("newspaper");
        w.WriteStartArray("entries");
        foreach (var entry in newspaper.Entries) w.WriteNumberValue(entry);
        w.WriteEndArray();
        w.WriteNumber("count", newspaper.Count);
        w.WriteEndObject();
    }

    // Shapes and ranges: 25 words and a count from the head's place to the table's end. The words a build that cannot pop leaves are Restore's.
    private static SessionNewspaperState Newspaper(JsonElement element)
    {
        const string p = "$.newspaper";
        var f = Fields(element, p, NewspaperFields);
        var rows = Array(f["entries"], p + ".entries", SessionNewspaperState.Capacity);
        if (rows.Length != SessionNewspaperState.Capacity) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".entries");
        var entries = new int[rows.Length];
        for (var i = 0; i < rows.Length; i++) entries[i] = Int(rows[i], $"{p}.entries[{i}]", int.MinValue, int.MaxValue);
        return new(entries, Int(f["count"], p + ".count", SessionNewspaperState.Head + 1, SessionNewspaperState.Capacity));
    }
}
