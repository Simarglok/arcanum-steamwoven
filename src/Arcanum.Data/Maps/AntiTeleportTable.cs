using System.Text;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Maps;

/// <summary>One anti-teleport region of <c>Rules\AntiTeleport.mes</c> (entries 100 onward): a tile of the start map and a radius in tiles (the entry's number × 64).</summary>
public sealed record AntiTeleportRegion(long X, long Y, long Radius);

public enum AntiTeleportTableStatus { Read, Missing, Unreadable, Refused }

public sealed record AntiTeleportTableLoad(AntiTeleportTableStatus Status, AntiTeleportTable? Table, string Detail);

/// <summary>
/// ARC-MAGIC-038 (MA-5 second half; the exe read ledger TP1–TP2, 0x4BD9E0 with 0x4BDB30 and 0x4BDC60): the table of places the teleports refuse, read as the original's anti-teleport module reads it.
/// The file <c>rules/antiteleport.mes</c> (module phase) holds consecutive entries from 100: a region <c>x, y, radius</c> (the radius times 64 is the distance in tiles, 64-bit) and, from 1000, one
/// number per entry for a map on which no teleport works (a number of 5001 or more minus 4999, a lower one plus 1, the original's map numbering of its catalogue). Both runs stop at the first
/// missing entry; either may be empty. A **missing file** is no failure of the original (it only warns): the module stays off and every teleport is allowed. A file the MES grammar does not
/// admit is refused (project policy). Numbers only; no game text.
/// </summary>
public sealed class AntiTeleportTable
{
    public const string Path = "rules/antiteleport.mes";
    public const int FirstRegion = 100, FirstMap = 1000, MapCatalogueBase = 0x1389;

    private AntiTeleportTable(IReadOnlyList<AntiTeleportRegion> regions, IReadOnlyList<int> disabledMaps) { Regions = regions; DisabledMaps = disabledMaps; }

    /// <summary>The regions in file order.</summary>
    public IReadOnlyList<AntiTeleportRegion> Regions { get; }

    /// <summary>The runtime map numbers no teleport works on.</summary>
    public IReadOnlyList<int> DisabledMaps { get; }

    public static AntiTeleportTableLoad Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new(); options.Validate();
        if (ContentPhase.Candidate(stack, Path, ContentLoadPhase.ModuleLoaded) is not { } candidate) return new(AntiTeleportTableStatus.Missing, null, Path);
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read) return new(AntiTeleportTableStatus.Unreadable, null, $"{Path} {read.Status}");
        var parsed = MesFile.Parse(read.Bytes, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(AntiTeleportTableStatus.Refused, null, $"{Path} {parsed.Status} line {parsed.Line}");
        var entries = new Dictionary<int, string>();
        foreach (var message in parsed.File!.Messages) entries[message.Number] = Encoding.Latin1.GetString(message.Bytes.Span);
        var table = Create(entries);
        return new(AntiTeleportTableStatus.Read, table, $"regions {table.Regions.Count} maps {table.DisabledMaps.Count}");
    }

    /// <summary>The table of the entries' texts by number.</summary>
    [OriginalFunction(0x4BD9E0, OriginalFunctionScope.Partial)]
    public static AntiTeleportTable Create(IReadOnlyDictionary<int, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var regions = new List<AntiTeleportRegion>();
        for (var number = FirstRegion; entries.TryGetValue(number, out var text); number++)
        {
            var fields = text.Split(',', 3);
            long Long(int i) => i < fields.Length ? WorldCatalog.StrToLong(fields[i]) : 0;
            var radius = fields.Length > 2 ? SoundSchemeParser.Atoi(fields[2].TrimStart(' ', '\t', '\n', '\v', '\f', '\r')) : 0;
            regions.Add(new(Long(0), Long(1), radius * 64L));
        }
        var maps = new List<int>();
        for (var number = FirstMap; entries.TryGetValue(number, out var text); number++)
        {
            var value = SoundSchemeParser.Atoi(text.TrimStart(' ', '\t', '\n', '\v', '\f', '\r'));
            maps.Add(value < MapCatalogueBase ? value + 1 : value - 4999);
        }
        return new(regions, maps);
    }
}
