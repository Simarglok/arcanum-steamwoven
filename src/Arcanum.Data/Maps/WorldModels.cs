using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Maps;

/// <summary>A sector address on a map: the original packs it as <c>X | Y &lt;&lt; 26</c>
/// with both coordinates in 26 bits.</summary>
public readonly record struct WorldSectorCoordinate(int X, int Y)
{
    public const int MaxCoordinate = (1 << 26) - 1;
    public long Id => (long)(uint)X | ((long)Y << 26);

    public static bool TryFromId(long id, out WorldSectorCoordinate coordinate)
    {
        coordinate = default;
        if (id < 0 || (id >> 52) != 0) return false;
        coordinate = new((int)(id & MaxCoordinate), (int)(id >> 26));
        return true;
    }

    /// <summary>The sector holding a tile: 64 tiles per side.</summary>
    public static WorldSectorCoordinate Containing(WorldLocation location) => new((int)(location.X >> 6), (int)(location.Y >> 6));
}

/// <summary>A tile location; the original packs it as <c>X | Y &lt;&lt; 32</c>.</summary>
public readonly record struct WorldLocation(long X, long Y)
{
    public static WorldLocation FromPacked(long packed) => new(packed & 0xFFFFFFFF, (long)((ulong)packed >> 32));
}

public enum WorldIssueCode
{
    /// <summary>A path was resolved but its bytes could not be read (too large or unreadable).</summary>
    Unreadable,
    /// <summary>A file exceeds a configured limit.</summary>
    LimitExceeded,
    /// <summary>A file does not have the expected structure.</summary>
    Malformed,
    /// <summary>An object record could not be read; the rest of its window is unreadable too.</summary>
    ObjectRefused,
    /// <summary>A file in a map folder whose name the loader does not recognise.</summary>
    UnrecognisedFile,
    /// <summary>A required file is missing.</summary>
    Missing,
    /// <summary>ARC-DATA-026: a prototype file in the module archive, a module patch or the module folder. The original
    /// reads prototypes before it mounts the module, so this copy is never read (a base copy of the same name is).</summary>
    IgnoredModuleCopy,
}

/// <summary>One record-level refusal. <see cref="Path"/> is the virtual path (never a local
/// path); <see cref="Index"/> is the record ordinal inside that file when there is one.</summary>
public sealed record WorldIssue(WorldIssueCode Code, string Path, int? Index = null, string? Detail = null);

/// <summary><c>map.prp</c>: the map's base terrain type and its size in tiles.</summary>
public sealed record WorldMapProperties(int BaseTerrain, int Reserved, long WidthTiles, long HeightTiles);

/// <summary>The <c>terrain.tdf</c> header: a grid of one 16-bit terrain word per sector.</summary>
public sealed record WorldTerrainHeader(float Version, bool RowCompressed, long WidthSectors, long HeightSectors, int BaseTerrain);

/// <summary>One <c>map.jmp</c> record (32 bytes): a tile that moves the walker to another
/// map and tile. Padding words are kept so a reader can check they are zero.</summary>
public sealed record WorldJumpPoint(uint Flags, int Padding4, WorldLocation Location, int DestinationMap, int Padding20, WorldLocation Destination);

/// <summary>The keys of <c>mapinfo.txt</c> the original reads; null when absent.</summary>
public sealed record WorldMapInfo(int? LightScheme, int? MusicScheme, int? AmbientScheme, int UnknownLines);

public enum WorldMobileSource { Aggregate, Editor, None }

/// <summary>One map folder of the content stack. Sectors, mobiles and the terrain grid are
/// read on demand through <see cref="WorldCatalog"/>.</summary>
public sealed class WorldMap
{
    internal WorldMap(string name, WorldMapProperties properties, WorldTerrainHeader? terrain, WorldLocation? startLocation, WorldMapInfo? info,
        IReadOnlyList<WorldJumpPoint> jumpPoints, IReadOnlyList<WorldSectorCoordinate> sectors, IReadOnlyList<string> editorMobiles,
        string? aggregatePath, IReadOnlyList<WorldIssue> issues)
    {
        Name = name; Properties = properties; Terrain = terrain; StartLocation = startLocation; Info = info; JumpPoints = jumpPoints;
        SectorFiles = sectors; EditorMobilePaths = editorMobiles; AggregatePath = aggregatePath; Issues = issues;
        _sectorSet = sectors.ToHashSet();
    }

    private readonly HashSet<WorldSectorCoordinate> _sectorSet;

    /// <summary>The folder name, lower case, as the content stack spells it.</summary>
    public string Name { get; }
    public string Folder => $"maps/{Name}";
    public WorldMapProperties Properties { get; }
    public WorldTerrainHeader? Terrain { get; }
    /// <summary><c>startloc.txt</c>: the map's own start tile. A new game starts where the map
    /// catalogue says, which can differ.</summary>
    public WorldLocation? StartLocation { get; }
    public WorldMapInfo? Info { get; }
    public IReadOnlyList<WorldJumpPoint> JumpPoints { get; }
    /// <summary>Sectors that have a file, in id order. Other sectors of the map are generated
    /// from the terrain grid (<see cref="WorldCatalog.ResolveSector"/>, ARC-DATA-031).</summary>
    public IReadOnlyList<WorldSectorCoordinate> SectorFiles { get; }
    public IReadOnlyList<string> EditorMobilePaths { get; }
    /// <summary>The game-generated mobile file the original reads in a normal game.</summary>
    public string? AggregatePath { get; }
    /// <summary>Refusals found while reading the map's small files.</summary>
    public IReadOnlyList<WorldIssue> Issues { get; }
    public bool HasSectorFile(WorldSectorCoordinate coordinate) => _sectorSet.Contains(coordinate);
}

/// <summary>One object read from a sector window, a mobile file or an aggregate. Field slots
/// follow the v119 serialized order (slot 0 is the current ART, slot 1 the location).</summary>
public sealed class WorldObject
{
    internal WorldObject(int type, int? prototypeNumber, short ownIdKind, string ownIdSha256, string sha256, string semanticSha256,
        IReadOnlyDictionary<int, WorldFieldValue> fields)
    {
        Type = type; PrototypeNumber = prototypeNumber; OwnIdKind = ownIdKind; OwnIdSha256 = ownIdSha256; Sha256 = sha256; SemanticSha256 = semanticSha256; Fields = fields;
    }

    public int Type { get; }
    /// <summary>The prototype's number for an instance; null for a prototype record itself.</summary>
    public int? PrototypeNumber { get; }
    public short OwnIdKind { get; }
    public string OwnIdSha256 { get; }
    public string Sha256 { get; }
    public string SemanticSha256 { get; }
    /// <summary>The serialized fields only: an instance holds its overrides, a prototype all slots.</summary>
    public IReadOnlyDictionary<int, WorldFieldValue> Fields { get; }

    public WorldLocation? Location => Fields.TryGetValue(1, out var value) && value.Presence == WorldFieldPresence.Value && value.Number is { } packed
        ? WorldLocation.FromPacked(packed) : null;
}

public enum WorldFieldPresence { Null, Value, Empty, Array }
public sealed record WorldFieldElement(int Key, IReadOnlyList<long> Words);
/// <summary>A serialized field. Strings are not exposed; <see cref="Fingerprint"/> is the
/// SHA-256 of their bytes, and <see cref="Number"/> their length.</summary>
public sealed record WorldFieldValue(WorldFieldPresence Presence, long? Number, IReadOnlyList<WorldFieldElement> Elements, string? Fingerprint);

/// <summary>A sector file read to the end of its object window. Raw sections keep the
/// original words; their meaning is owned by the components that consume them.</summary>
public sealed class WorldSector
{
    internal WorldSector(WorldSectorCoordinate coordinate, string path, int version, IReadOnlyList<IReadOnlyList<int>> lights, IReadOnlyList<uint> tiles,
        IReadOnlyList<uint>? roofs, IReadOnlyList<IReadOnlyList<int>> tileScripts, IReadOnlyList<int>? sectorScript, IReadOnlyList<int>? metadataAndSound,
        TerrainSerializedBlockMask? blockMask, int declaredObjects, IReadOnlyList<WorldObject> objects, WorldIssue? objectIssue,
        WorldSectorSourceKind sourceKind = WorldSectorSourceKind.Own)
    {
        Coordinate = coordinate; Path = path; Version = version; Lights = lights; Tiles = tiles; Roofs = roofs; TileScripts = tileScripts;
        SectorScript = sectorScript; MetadataAndSound = metadataAndSound; BlockMask = blockMask; DeclaredObjects = declaredObjects; Objects = objects; ObjectIssue = objectIssue;
        SourceKind = sourceKind;
    }

    public WorldSectorCoordinate Coordinate { get; }
    /// <summary>The file read: the own sector file, or the template file for <see cref="WorldSectorSourceKind.Template"/>.</summary>
    public string Path { get; }
    /// <summary>ARC-DATA-031: <see cref="WorldSectorSourceKind.Own"/> or <see cref="WorldSectorSourceKind.Template"/>. A template
    /// sector's objects and lights are moved to <see cref="Coordinate"/> (ledger SS6, Wilderness A1); their hashes stay
    /// those of the template's records.</summary>
    public WorldSectorSourceKind SourceKind { get; }
    /// <summary>Sector format 0..4; later versions add tile scripts, a sector script, metadata
    /// and sound, and a block mask.</summary>
    public int Version { get; }
    /// <summary>Each serialized light as twelve 32-bit words.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Lights { get; }
    /// <summary>4,096 tile words, row-major from local (0,0).</summary>
    public IReadOnlyList<uint> Tiles { get; }
    /// <summary>256 roof words, or null when the sector has no roof list.</summary>
    public IReadOnlyList<uint>? Roofs { get; }
    /// <summary>Each tile script as six 32-bit words.</summary>
    public IReadOnlyList<IReadOnlyList<int>> TileScripts { get; }
    public IReadOnlyList<int>? SectorScript { get; }
    public IReadOnlyList<int>? MetadataAndSound { get; }

    /// <summary>
    /// ARC-MAGIC-028 (FR-CHAR-020): the sector's aptitude adjustment, the second word (+0x4644) of the metadata that sector
    /// versions 0xAA0003 and later store after the tile scripts and the sector script; 0 for an older version. The original's
    /// sector reset zeroes the field and its only reader is the aptitude stat (0x45A060, called from 0x4B0740 with the sector
    /// of the critter's location). Its writer (0x45A0A0) is never called or referenced in the 1.0.7.4 executable, so the
    /// number is whatever the file holds. The caller adds it to the aptitude of a critter standing in this sector; a location
    /// outside the map, whose sector cannot be locked, has 0.
    /// </summary>
    [OriginalFunction(0x45A060, OriginalFunctionScope.Partial)]
    public int AptitudeAdjustment() => MetadataAndSound is { Count: >= 2 } words ? words[1] : 0;

    /// <summary>
    /// The art id of the tile slot at a location inside this sector, from the tile index <c>(y &amp; 63) &lt;&lt; 6 | x &amp; 63</c>
    /// (0x4D7090, 0x4D70B0). The sector-from-location step is the caller's.
    /// </summary>
    [OriginalFunction(0x4D70B0, OriginalFunctionScope.Partial)]
    public uint TileArt(int x, int y) => Tiles[(y & 63) << 6 | (x & 63)];

    public TerrainSerializedBlockMask? BlockMask { get; }
    public int DeclaredObjects { get; }
    /// <summary>Objects read before any refusal; complete when <see cref="ObjectIssue"/> is null.</summary>
    public IReadOnlyList<WorldObject> Objects { get; }
    public WorldIssue? ObjectIssue { get; }
}

public enum WorldSectorStatus { Read, NoFile, Refused }

/// <summary>Where the original takes a sector from (ARC-DATA-031, ledger SS2, SS5).</summary>
public enum WorldSectorSourceKind
{
    /// <summary>The map's own <c>&lt;sector id&gt;.sec</c>.</summary>
    Own,
    /// <summary>The terrain template the sector's terrain word names, moved to the sector.</summary>
    Template,
    /// <summary>No own file and no template file (also a transition code without a slot): the original fills the
    /// sector with one tile family in random variations and nothing else; no plan models that.</summary>
    Fill,
    /// <summary>Outside the map's <c>map.prp</c> size: the original's sector lock refuses it.</summary>
    OutsideMap,
    /// <summary>The terrain grid is missing (the original does not open the map) or the word names a type without a
    /// name (the original faults).</summary>
    Unresolved,
}

/// <summary>A sector's source: the own file, or the template with the word that named it.</summary>
public sealed record WorldSectorSource(WorldSectorCoordinate Coordinate, WorldSectorSourceKind Kind, string? Path, TerrainWord? Word,
    TerrainTemplatePath? Template);
public sealed record WorldSectorResult(WorldSectorStatus Status, WorldSector? Sector, WorldIssue? Issue);

/// <summary>A map's mobile objects. A refused aggregate record ends the readable part of
/// the aggregate; a refused editor file affects only that file.</summary>
public sealed record WorldMobileResult(WorldMobileSource Source, IReadOnlyList<WorldObject> Objects, IReadOnlyList<WorldIssue> Issues);

/// <summary>The decoded terrain grid: one 16-bit word per sector, row-major.</summary>
public sealed class WorldTerrainGrid
{
    private readonly ushort[] _words;
    internal WorldTerrainGrid(WorldTerrainHeader header, ushort[] words) { Header = header; _words = words; }
    public WorldTerrainHeader Header { get; }
    public ushort this[int x, int y] => x < 0 || y < 0 || x >= Header.WidthSectors || y >= Header.HeightSectors
        ? throw new ArgumentOutOfRangeException(nameof(x)) : _words[x + y * Header.WidthSectors];
}

public sealed record WorldTerrainResult(WorldTerrainGrid? Grid, WorldIssue? Issue);
public sealed record WorldPrototypeResult(WorldObject? Prototype, WorldIssue? Issue);

public sealed class WorldLoaderOptions
{
    public int MaxMaps { get; init; } = 4096;
    public int MaxFilesPerMap { get; init; } = 262_144;
    public int MaxSmallFileBytes { get; init; } = 64 * 1024;
    public int MaxJumpPoints { get; init; } = 65_536;
    public int MaxMobileBytes { get; init; } = 1024 * 1024;
    public int MaxAggregateBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxAggregateRecords { get; init; } = 1_000_000;
    public int MaxTerrainBytes { get; init; } = 64 * 1024 * 1024;
    public long MaxTerrainSectors { get; init; } = 16L * 1024 * 1024;
    public TerrainLoaderOptions Sector { get; init; } = new();
    public StaticObjectLoaderOptions Wire { get; init; } = new();

    internal void Validate()
    {
        if (MaxMaps <= 0 || MaxFilesPerMap <= 0 || MaxSmallFileBytes <= 0 || MaxJumpPoints < 0 || MaxMobileBytes <= 0 || MaxAggregateBytes <= 0 ||
            MaxAggregateRecords <= 0 || MaxTerrainBytes <= 0 || MaxTerrainSectors <= 0 || Sector is null || Wire is null)
            throw new ArgumentOutOfRangeException(nameof(WorldLoaderOptions), "World limits must be positive.");
        // Upper bounds keep every derived size inside an array and an Int32 offset.
        if (MaxTerrainSectors > Array.MaxLength || MaxJumpPoints > (int.MaxValue - 4) / 32)
            throw new ArgumentOutOfRangeException(nameof(WorldLoaderOptions), "World limits exceed the addressable array size.");
        Sector.Validate(); Wire.Validate();
    }
}
