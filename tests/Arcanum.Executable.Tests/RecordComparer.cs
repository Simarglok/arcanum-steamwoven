using Arcanum.Data.Objects;
using Arcanum.Data.StaticObjects;

namespace Arcanum.Executable.Tests;

/// <summary>
/// Field-by-field comparison of two prototype files through the repository's own v119 record reader. A record is flattened
/// into named values (header, then every present field, string and array element word); the storage handle of an array and
/// the zero capacity after its last element are not part of it (the reader leaves them out), and an ID is compared by its
/// kind and the identity that kind uses. Bytes the original leaves uninitialized are found, not assumed: a second build with
/// other uninitialized memory differs from the first in exactly the values that depend on it.
/// </summary>
internal static class RecordComparer
{
    private static readonly StaticObjectLoaderOptions Limits = new() { MaxPrototypeBytes = 64 * 1024, MaxArrayElements = 4096, MaxStringBytes = 4096 };

    /// <summary>The only values found to depend on uninitialized memory: word 0 of an element of the script array (slot 31).</summary>
    private static readonly System.Text.RegularExpressions.Regex UninitializedKey = new(@"^s31\.e\d+\.w0$");

    /// <summary>The records of a map's mobile aggregate (ARC-DEV-082): the 16-byte module GUID, then whole v119 records to the end of the file.</summary>
    public static (byte[] Guid, List<SortedDictionary<string, string>> Records, int Length) FlattenAggregate(byte[] bytes)
    {
        var records = new List<SortedDictionary<string, string>>();
        var offset = 16;
        while (offset < bytes.Length)
        {
            var record = ObjectWireReader.Read(bytes.AsSpan(offset), Limits, maxRecordBytes: 1 << 20);
            records.Add(Flatten(record));
            offset += record.Length;
        }
        return (bytes[..16], records, offset);
    }

    /// <summary>
    /// The values in which two aggregates differ, field by field through the repository's own v119 reader (an ID's bytes its kind does not use are
    /// copied from the stack and are not part of what the reader returns), after the GUID, the record count and the total length.
    /// </summary>
    public static List<string> AggregateDifferences(byte[] built, byte[] original)
    {
        var differences = new List<string>();
        (byte[] Guid, List<SortedDictionary<string, string>> Records, int Length) a, b;
        try { a = FlattenAggregate(built); b = FlattenAggregate(original); }
        catch (StaticObjectException exception) { return [$"unreadable: {exception.Code}"]; }
        if (!a.Guid.AsSpan().SequenceEqual(b.Guid)) differences.Add("the module GUID differs");
        if (a.Length != built.Length || b.Length != original.Length) differences.Add("trailing bytes");
        if (a.Records.Count != b.Records.Count) differences.Add($"{a.Records.Count} records against {b.Records.Count}");
        for (var i = 0; i < Math.Min(a.Records.Count, b.Records.Count); i++)
            foreach (var key in a.Records[i].Keys.Union(b.Records[i].Keys))
            {
                a.Records[i].TryGetValue(key, out var va);
                b.Records[i].TryGetValue(key, out var vb);
                if (va != vb) differences.Add($"record {i} {key}: {va ?? "-"} / {vb ?? "-"}");
            }
        return differences;
    }

    public static ObjectWireRecord Read(byte[] bytes) => ObjectWireReader.Read(bytes, Limits, prototype: true, maxRecordBytes: 64 * 1024);

    /// <summary>The record as named values; the key says where a value sits ("s31.e9.w0" is word 0 of element 9 of slot 31).</summary>
    public static SortedDictionary<string, string> Flatten(byte[] bytes) => Flatten(Read(bytes));

    private static SortedDictionary<string, string> Flatten(ObjectWireRecord record)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = record.Type.ToString(),
            ["parent"] = $"{record.Parent.Kind}/{record.Parent.Entity}",
            ["own"] = $"{record.Own.Entity}",
        };
        foreach (var (slot, value) in record.Fields)
        {
            var key = $"s{slot}";
            map[key] = $"{value.Presence}/{value.Number}";
            if (value.Text is not null) map[$"{key}.text"] = Convert.ToHexString(value.Text.ToArray());
            map[$"{key}.count"] = value.Elements.Count.ToString();
            foreach (var element in value.Elements)
                for (var w = 0; w < element.Words.Count; w++)
                    map[$"{key}.e{element.Key}.w{w}"] = element.Words[w].ToString();
        }
        return map;
    }

    /// <summary>
    /// The values in which <paramref name="built"/> and <paramref name="original"/> differ, leaving out every value in which
    /// <paramref name="built"/> and <paramref name="perturbed"/> (the same build over other uninitialized memory) differ.
    /// </summary>
    public static List<string> Differences(byte[] built, byte[] original, byte[]? perturbed = null)
    {
        SortedDictionary<string, string> a, b;
        SortedDictionary<string, string>? p = null;
        try { a = Flatten(built); b = Flatten(original); if (perturbed is not null) p = Flatten(perturbed); }
        catch (StaticObjectException exception) { return [$"unreadable: {exception.Code}"]; }
        var differences = new List<string>();
        foreach (var key in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(key, out var va);
            b.TryGetValue(key, out var vb);
            if (va == vb) continue;
            if (p is not null)
            {
                p.TryGetValue(key, out var vp);
                if (vp != va && UninitializedKey.IsMatch(key)) continue;   // depends on uninitialized memory: only the one kind found is left out
            }
            differences.Add($"{key}: {va ?? "-"} / {vb ?? "-"}");
        }
        return differences;
    }

    /// <summary>The values in which two builds over different uninitialized memory differ (diagnostics).</summary>
    public static List<string> UninitializedValues(byte[] first, byte[] second)
    {
        var a = Flatten(first); var b = Flatten(second);
        return a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var va) | !b.TryGetValue(k, out var vb) || va != vb).ToList();
    }
}
