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

    public static ObjectWireRecord Read(byte[] bytes) => ObjectWireReader.Read(bytes, Limits, prototype: true, maxRecordBytes: 64 * 1024);

    /// <summary>The record as named values; the key says where a value sits ("s31.e9.w0" is word 0 of element 9 of slot 31).</summary>
    public static SortedDictionary<string, string> Flatten(byte[] bytes)
    {
        var record = Read(bytes);
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
