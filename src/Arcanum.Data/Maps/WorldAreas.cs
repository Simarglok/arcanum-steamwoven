using System.Text;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Maps;

/// <summary>
/// One area of <c>mes/gamearea.mes</c> (ledger WT1): its index (the entry's position in the file), its location on the start
/// map, the offsets of its name on the world map, its name and description, and its discovery radius in tiles (320 by default,
/// <c>Radius:</c> n × 64; area 0 gets 0 from a <c>Radius:</c> key). An entry the original reads only in part (no <c>/</c> name or description) keeps
/// what it read: <see cref="Complete"/> is false and its radius is 0.
/// </summary>
public sealed record WorldArea(int Index, WorldLocation Location, int NameOffsetX, int NameOffsetY, string Name, string Description,
    long Radius, bool Complete);

public enum WorldAreaTableStatus { Read, Missing, Unreadable, Refused }

public sealed record WorldAreaTableLoad(WorldAreaTableStatus Status, WorldAreaTable? Table, string Detail);

/// <summary>
/// ARC-WORLD-056: the module's area table, read as the original's area module reads it when the module loads (ledger WT1,
/// 0x4CA9B0): the content stack's winner, entries in number order and numbered by position. A table the MES grammar does not
/// admit is refused (project policy); a missing table is no failure of the original (no areas).
/// </summary>
public sealed class WorldAreaTable
{
    public const string Path = "mes/gamearea.mes";
    /// <summary>The default discovery radius: 320 tiles (5 sectors).</summary>
    public const long DefaultRadius = 320;

    private WorldAreaTable(IReadOnlyList<WorldArea> areas) => Areas = areas;

    /// <summary>The areas by index; index 0 is the unknown area.</summary>
    public IReadOnlyList<WorldArea> Areas { get; }

    public static WorldAreaTableLoad Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new(); options.Validate();
        if (ContentPhase.Candidate(stack, Path, ContentLoadPhase.ModuleLoaded) is not { } candidate)
            return new(WorldAreaTableStatus.Missing, Create([]), Path);
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read) return new(WorldAreaTableStatus.Unreadable, null, $"{Path} {read.Status}");
        var parsed = MesFile.Parse(read.Bytes, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(WorldAreaTableStatus.Refused, null, $"{Path} {parsed.Status} line {parsed.Line}");
        var table = Create(parsed.File!.Messages.Select(m => Encoding.Latin1.GetString(m.Bytes.Span)).ToArray());
        return new(WorldAreaTableStatus.Read, table, $"areas {table.Areas.Count}");
    }

    /// <summary>The table of the entries' texts in number order.</summary>
    [OriginalFunction(0x4CA9B0, OriginalFunctionScope.Partial)]
    public static WorldAreaTable Create(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var areas = new WorldArea[entries.Count];
        for (var index = 0; index < entries.Count; index++) areas[index] = Parse(index, entries[index] ?? throw new ArgumentNullException(nameof(entries)));
        return new(Array.AsReadOnly(areas));
    }

    // The C locale's isspace: space, tab, line feed, vertical tab, form feed, carriage return.
    private static readonly char[] CSpace = [' ', '\t', '\n', '\v', '\f', '\r'];

    private static WorldArea Parse(int index, string text)
    {
        // Four comma-separated numbers: x and y (64-bit), then the name offsets.
        var fields = text.Split(',', 4);
        long Long(int i) => i < fields.Length ? WorldCatalog.StrToLong(fields[i]) : 0;
        int Int(int i) => i < fields.Length ? SoundSchemeParser.Atoi(fields[i].TrimStart(CSpace)) : 0;
        var location = new WorldLocation(Long(0), Long(1));
        var first = text.IndexOf('/');
        if (first < 0) return new(index, location, Int(2), Int(3), "", "", 0, false);
        var second = text.IndexOf('/', first + 1);
        if (second < 0) return new(index, location, Int(2), Int(3), text[(first + 1)..], "", 0, false);
        var third = text.IndexOf('/', second + 1);
        var name = text[(first + 1)..second];
        var description = third < 0 ? text[(second + 1)..] : text[(second + 1)..third];
        var radius = DefaultRadius;
        // The key reader skips C white space and needs the key at the start; area 0 takes 0 from any value.
        if (third >= 0 && text[(third + 1)..].TrimStart(CSpace) is var key && key.StartsWith("Radius:", StringComparison.Ordinal))
            radius = index == 0 ? 0 : SoundSchemeParser.Atoi(key.AsSpan("Radius:".Length)) * 64L;
        return new(index, location, Int(2), Int(3), name, description, radius, true);
    }
}

/// <summary>
/// ARC-WORLD-056: which sectors of a map a world-map route may enter (ledger WT7): a sector in the map's script-blocked list
/// never does; a sector with its own sector file always does; any other sector does unless its terrain word is outside the
/// grid or its base or second type carries <c>/b</c> (0x4E8E00). The blocked list (<c>map.sbf</c>) is filled by scripts at
/// runtime; the installed module ships none, so a new game starts with it empty.
/// </summary>
public sealed class WorldTravelSectors
{
    private readonly HashSet<WorldSectorCoordinate> _files;
    private readonly WorldTerrainGrid _grid;
    private readonly TerrainTypeTable _types;
    private readonly IReadOnlySet<WorldSectorCoordinate> _blocked;

    public WorldTravelSectors(WorldMap map, WorldTerrainGrid grid, TerrainTypeTable types, IReadOnlySet<WorldSectorCoordinate>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _files = map.SectorFiles.ToHashSet();
        _blocked = blocked ?? new HashSet<WorldSectorCoordinate>();
    }

    public long WidthSectors => _grid.Header.WidthSectors;
    public long HeightSectors => _grid.Header.HeightSectors;

    /// <summary>ARC-WORLD-1159: the terrain word of a sector of the start map, <see cref="TerrainWord.OutsideGrid"/> outside the grid (the read the encounter roll makes).</summary>
    public ushort TerrainWordAt(long x, long y) =>
        x < 0 || y < 0 || x >= _grid.Header.WidthSectors || y >= _grid.Header.HeightSectors ? TerrainWord.OutsideGrid : _grid[(int)x, (int)y];

    [OriginalFunction(0x4E8E00, OriginalFunctionScope.Full)]
    public bool TerrainBlocked(long x, long y)
    {
        if (x < 0 || y < 0 || x >= _grid.Header.WidthSectors || y >= _grid.Header.HeightSectors) return true;
        var word = new TerrainWord(_grid[(int)x, (int)y]);
        if (word.Value == TerrainWord.OutsideGrid) return true;
        bool Blocked(int type) => type < _types.Types.Count && _types.Types[type].Blocked;
        return Blocked(word.Base) || Blocked(word.Second);
    }

    /// <summary>Whether a route may enter the sector.</summary>
    public bool Passes(long x, long y)
    {
        if (x < 0 || y < 0 || x > int.MaxValue || y > int.MaxValue) return false;
        var sector = new WorldSectorCoordinate((int)x, (int)y);
        if (_blocked.Contains(sector)) return false;
        return _files.Contains(sector) || !TerrainBlocked(x, y);
    }
}
