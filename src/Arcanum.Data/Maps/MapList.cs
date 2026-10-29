using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Maps;

/// <summary>One row of <c>rules/maplist.mes</c>: the catalogue number, the map folder name as the row
/// spells it (trimmed) and the start tile where a new arrival lands (null when the row has fewer
/// than three values or a negative coordinate).</summary>
public sealed record MapListEntry(int Catalogue, string Name, WorldLocation? Start)
{
    /// <summary>The runtime map number: the row's position plus one (catalogue − 4999).</summary>
    public int Runtime => Catalogue - MapList.FirstCatalogue + 1;

    /// <summary>ARC-UI-038: the row's map type (its fourth value <c>Type: word</c>; <see cref="MapListType.None"/> without one).</summary>
    public MapListType Type { get; init; }

    /// <summary>ARC-SCRIPT-050: the row's world-map area (its optional <c>Area: n</c> value, 0x4126FD; 0 without one), which get area (84)
    /// answers for an object on this map (0x40FFE0).</summary>
    public int Area { get; init; }
}

/// <summary>ARC-UI-038: the map types of the map list (the original's table at 0x59F058: <c>NONE</c>, <c>START_MAP</c>,
/// <c>SHOPPING_MAP</c>).</summary>
public enum MapListType { None, Start, Shopping }

public enum MapListStatus { Read, Missing, Unreadable, Malformed }

/// <summary>
/// The module's map list (<c>rules/maplist.mes</c>, the content stack's winner): rows numbered from 5000
/// without gaps, at most <see cref="MaxEntries"/> (the source's map list loader 0x4125C0 stops at
/// the first missing number). Scripts name maps by catalogue number, jump points and the session by runtime number.
/// A list that cannot be read is empty with a typed status; nothing is thrown.
/// </summary>
public sealed class MapList
{
    public const int FirstCatalogue = 5000, MaxEntries = 200;

    private MapList(MapListStatus status, IReadOnlyList<MapListEntry> entries) { Status = status; Entries = entries; }

    public MapListStatus Status { get; }
    /// <summary>Rows in catalogue order.</summary>
    public IReadOnlyList<MapListEntry> Entries { get; }

    /// <summary>The first row naming the map folder, compared without case; null when none does.</summary>
    public MapListEntry? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public MapListEntry? ByCatalogue(int catalogue) =>
        catalogue - FirstCatalogue is var index && index >= 0 && index < Entries.Count ? Entries[index] : null;

    public MapListEntry? ByRuntime(int runtime) => ByCatalogue(runtime + FirstCatalogue - 1);

    /// <summary>ARC-UI-038: the map of a type (0x40FF50): the last row the load gave that type, as each typed row overwrites the
    /// type's slot (0x4126AD); null for <see cref="MapListType.None"/> (the getter answers 0 outside types 1 and 2) or a type no row
    /// has.</summary>
    [OriginalFunction(0x40FF50, OriginalFunctionScope.Full)]
    public MapListEntry? OfType(MapListType type) => type is MapListType.Start or MapListType.Shopping ? Entries.LastOrDefault(e => e.Type == type) : null;

    [OriginalFunction(0x4125C0, OriginalFunctionScope.Partial)]
    public static MapList Read(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var winner = stack.Resolve("rules/maplist.mes").Winner;
        if (winner is null) return new(MapListStatus.Missing, []);
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read) return new(MapListStatus.Unreadable, []);
        var parsed = MesFile.Parse(read.Bytes!);
        if (parsed.Status != MesParseStatus.Parsed) return new(MapListStatus.Malformed, []);
        var entries = new List<MapListEntry>();
        for (var number = FirstCatalogue; number < FirstCatalogue + MaxEntries; number++)
        {
            if (parsed.File!.Find(number) is not { } message) break;
            var values = Encoding.Latin1.GetString(message.Bytes.Span).Split(',');
            // Read like C strtoll (leading digits, else 0); the original's 64-bit atoi agrees except that
            // it wraps on overflow where this saturates (ledger IF2). A negative coordinate is not a tile,
            // so it is treated as absent (the original keeps it).
            WorldLocation? start = null;
            if (values.Length >= 3 && WorldCatalog.StrToLong(values[1]) is var x and >= 0 && WorldCatalog.StrToLong(values[2]) is var y and >= 0) start = new(x, y);
            entries.Add(new(number, values[0].Trim(), start) { Type = values.Length >= 4 ? TypeOf(values[3]) : MapListType.None, Area = AreaOf(values) });
        }
        return new(MapListStatus.Read, entries.AsReadOnly());
    }

    /// <summary>ARC-SCRIPT-050: the area of the runtime map (0x40FFE0): the row's <see cref="MapListEntry.Area"/> for a number 1..count, else 0.</summary>
    [OriginalFunction(0x40FFE0, OriginalFunctionScope.Full)]
    public int AreaOfRuntime(int runtime) => runtime >= 1 && runtime <= Entries.Count ? Entries[runtime - 1].Area : 0;

    // ARC-SCRIPT-050: the optional values after the start are read in a fixed order through one cursor (0x412681..0x41271C): "Type:",
    // then "WorldMap:" (0x532320 with 0x5A05D0), then "Area:" (0x5A05C8). Each test skips white space and must find its case-sensitive
    // keyword at the cursor; a value that does not match leaves the cursor on it for the next test, a match consumes the value up to and
    // past its comma. The number after "WorldMap:" and "Area:" is the C runtime's atol of the value (0x531990), which accumulates without an
    // overflow check (no installed row overflows). A row without a matching "Area:" keeps 0.
    private static int AreaOf(string[] values)
    {
        var at = 3;
        if (at < values.Length && Keyword(values[at], "Type:") is not null) at++;
        if (at < values.Length && Keyword(values[at], "WorldMap:") is not null) at++;
        return at < values.Length && Keyword(values[at], "Area:") is { } rest ? Atol(rest) : 0;
    }

    private static string? Keyword(string value, string keyword)
    {
        var rest = value.TrimStart(' ', '\t', '\n', '\v', '\f', '\r');
        return rest.StartsWith(keyword, StringComparison.Ordinal) ? rest[keyword.Length..] : null;
    }

    private static int Atol(string text)
    {
        var i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = i < text.Length && text[i] == '-';
        if (i < text.Length && text[i] is '-' or '+') i++;
        var total = 0;
        for (; i < text.Length && text[i] is >= '0' and <= '9'; i++) total = unchecked(total * 10 + (text[i] - '0'));
        return negative ? unchecked(-total) : total;
    }

    // ARC-UI-038: the optional type after the start (0x412681 → 0x532480): the value must begin, after white space, with the case-sensitive
    // keyword "Type:"; the word after it and its white space, up to the next comma, is compared without case with the table's words
    // (0x531EE0 → _stricmp 0x58ADB0), so a word with trailing white space names no type.
    private static MapListType TypeOf(string value)
    {
        var rest = value.TrimStart(' ', '\t', '\n', '\v', '\f', '\r');
        if (!rest.StartsWith("Type:", StringComparison.Ordinal)) return MapListType.None;
        var word = rest["Type:".Length..].TrimStart(' ', '\t', '\n', '\v', '\f', '\r');
        return word.Equals("START_MAP", StringComparison.OrdinalIgnoreCase) ? MapListType.Start
            : word.Equals("SHOPPING_MAP", StringComparison.OrdinalIgnoreCase) ? MapListType.Shopping : MapListType.None;
    }
}
