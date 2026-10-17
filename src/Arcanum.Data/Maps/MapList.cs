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
}

public enum MapListStatus { Read, Missing, Unreadable, Malformed }

/// <summary>
/// The module's map list (<c>rules/maplist.mes</c>, the content stack's winner): rows numbered from 5000
/// without gaps, at most <see cref="MaxEntries"/> (pinned CE <c>map_list_info_load</c> stops at the first
/// missing number). Scripts name maps by catalogue number, jump points and the session by runtime number.
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
            // Read like C strtoll (leading digits, else 0), as the original's value parser does; a
            // negative coordinate is not a tile, so it is treated as absent.
            WorldLocation? start = null;
            if (values.Length >= 3 && WorldCatalog.StrToLong(values[1]) is var x and >= 0 && WorldCatalog.StrToLong(values[2]) is var y and >= 0) start = new(x, y);
            entries.Add(new(number, values[0].Trim(), start));
        }
        return new(MapListStatus.Read, entries.AsReadOnly());
    }
}
