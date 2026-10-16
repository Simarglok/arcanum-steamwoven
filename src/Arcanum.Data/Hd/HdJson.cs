using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcanum.Data.Hd;

/// <summary>Strict reading of the HD pack JSON files: objects have exactly the known
/// properties (no unknown, no duplicate), integers are plain JSON integers, strings match
/// their pattern. A violation throws <see cref="HdFormatException"/>, which every public
/// entry point turns into a typed result.</summary>
internal static partial class HdJson
{
    public static readonly JsonDocumentOptions Options = new() { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    public static partial Regex IdPattern();
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    public static partial Regex DigestPattern();
    [GeneratedRegex("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();
    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,31}$", RegexOptions.CultureInvariant)]
    public static partial Regex ParameterPattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    public static partial Regex FileNamePattern();

    public static Dictionary<string, JsonElement> Object(JsonElement element, string path, string[] required, params string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid(path, "must be an object");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in Properties(element, path))
        {
            if (!required.Contains(name, StringComparer.Ordinal) && !optional.Contains(name, StringComparer.Ordinal))
                throw Invalid(path + "." + name, "unknown property");
            if (!properties.TryAdd(name, value)) throw Invalid(path + "." + name, "duplicate property");
        }
        foreach (var name in required)
            if (!properties.ContainsKey(name)) throw Invalid(path + "." + name, "missing");
        return properties;
    }

    public static long Long(JsonElement element, string path, long min, long max)
    {
        if (element.ValueKind != JsonValueKind.Number || !IntegerPattern().IsMatch(element.GetRawText()) || !element.TryGetInt64(out var value))
            throw Invalid(path, "must be an integer");
        if (value < min || value > max) throw Invalid(path, $"must be in {min}..{max}");
        return value;
    }

    public static int Int(JsonElement element, string path, int min, int max) => (int)Long(element, path, min, max);

    public static bool Bool(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid(path, "must be true or false"),
    };

    public static string String(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String) throw Invalid(path, "must be a string");
        // Invalid UTF-8 or an escaped lone surrogate make GetString throw.
        try { return element.GetString()!; }
        catch (InvalidOperationException) { throw new HdFormatException(HdFormatError.Malformed, path + " is not valid UTF-16 text"); }
    }

    /// <summary>The properties of an object, in order; a name that is not valid text is
    /// <see cref="HdFormatError.Malformed"/>.</summary>
    public static List<(string Name, JsonElement Value)> Properties(JsonElement element, string path)
    {
        var list = new List<(string, JsonElement)>();
        try { foreach (var property in element.EnumerateObject()) list.Add((property.Name, property.Value)); }
        catch (InvalidOperationException) { throw new HdFormatException(HdFormatError.Malformed, path + " has a property name that is not valid text"); }
        return list;
    }

    public static string Matching(JsonElement element, string path, Regex pattern)
    {
        var value = String(element, path);
        if (!pattern.IsMatch(value)) throw Invalid(path, "has an invalid format");
        return value;
    }

    public static string Digest(JsonElement element, string path) => Matching(element, path, DigestPattern());

    /// <summary>Printable ASCII label of 1..128 characters with no path separator, so that
    /// a build record cannot carry a local path.</summary>
    public static string Label(JsonElement element, string path)
    {
        var value = String(element, path);
        if (value.Length is 0 or > 128 || value.Any(c => c is < ' ' or > '~' or '/' or '\\') || value != value.Trim())
            throw Invalid(path, "must be 1..128 printable ASCII characters without '/' or '\\' or outer spaces");
        return value;
    }

    /// <summary>Free text (a prompt) of 1..<paramref name="max"/> characters: any Unicode
    /// (unpaired surrogates are refused by <see cref="String"/>) except control characters
    /// other than line feed and tab.</summary>
    public static string Text(JsonElement element, string path, int max)
    {
        var value = String(element, path);
        if (value.Length == 0 || value.Length > max || value.Any(c => char.IsControl(c) && c is not ('\n' or '\t')))
            throw Invalid(path, $"must be 1..{max} characters without control characters other than line feed and tab");
        return value;
    }

    public static T Enum<T>(JsonElement element, string path, IReadOnlyDictionary<string, T> values)
    {
        var value = String(element, path);
        return values.TryGetValue(value, out var result) ? result : throw Invalid(path, "has an unknown value");
    }

    public static DateTimeOffset Utc(JsonElement element, string path)
    {
        var value = String(element, path);
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
            throw Invalid(path, "must be yyyy-MM-ddTHH:mm:ssZ");
        return result;
    }

    public static JsonElement[] Array(JsonElement element, string path, int min, int max)
    {
        if (element.ValueKind != JsonValueKind.Array) throw Invalid(path, "must be an array");
        var length = element.GetArrayLength();
        if (length < min || length > max) throw Invalid(path, $"must have {min}..{max} items");
        return element.EnumerateArray().ToArray();
    }

    public static void Schema(Dictionary<string, JsonElement> root, string kind)
    {
        if (root["kind"].ValueKind != JsonValueKind.String || String(root["kind"], "kind") != kind)
            throw new HdFormatException(HdFormatError.UnsupportedSchema, "kind must be " + kind);
        if (root["schema"].ValueKind != JsonValueKind.Number || root["schema"].GetRawText() != "1")
            throw new HdFormatException(HdFormatError.UnsupportedSchema, "schema must be 1");
    }

    public static HdFormatException Invalid(string path, string message) => new(HdFormatError.InvalidField, path + " " + message);
}

internal enum HdFormatError { Malformed, UnsupportedSchema, InvalidField, InvalidPath, DuplicateFile, DuplicateKey, TooManyEntries, Unsupported }

internal sealed class HdFormatException(HdFormatError error, string detail) : Exception(detail)
{
    public HdFormatError Error { get; } = error;
}
