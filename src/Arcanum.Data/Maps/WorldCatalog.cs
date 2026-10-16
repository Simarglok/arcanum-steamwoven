using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Objects;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Maps;

/// <summary>Universal, read-only map reader over a module's <see cref="ContentStack"/>
/// (ARC-DATA-015). Every input is a virtual path, so a module, patch or loose override is
/// read the same way as the original. Checks are structural with explicit limits; a file or
/// record that fails is reported as a <see cref="WorldIssue"/> and does not refuse the map.
/// Facts and counts behind the formats: docs/research/data-census.md (ARC-RES-036).</summary>
public sealed class WorldCatalog
{
    private const int TerrainHeaderBytes = 32;
    private const int JumpPointBytes = 32;
    private const int AggregateGuidBytes = 16;
    private readonly ContentStack _stack;
    private readonly WorldLoaderOptions _options;
    private readonly IReadOnlyDictionary<string, WorldMap> _byName;
    private readonly IReadOnlyDictionary<int, string> _prototypePaths;

    private WorldCatalog(ContentStack stack, WorldLoaderOptions options, IReadOnlyList<WorldMap> maps, IReadOnlyDictionary<int, string> prototypes, IReadOnlyList<WorldIssue> issues)
    {
        _stack = stack; _options = options; Maps = maps; _prototypePaths = prototypes; Issues = issues;
        _byName = maps.ToDictionary(m => m.Name, StringComparer.Ordinal);
    }

    /// <summary>Maps in folder-name order.</summary>
    public IReadOnlyList<WorldMap> Maps { get; }
    /// <summary>Catalogue-level refusals: map folders without usable properties, duplicate prototypes.</summary>
    public IReadOnlyList<WorldIssue> Issues { get; }
    public IReadOnlyCollection<int> PrototypeNumbers => _prototypePaths.Keys.ToArray();

    public WorldMap? Find(string name) => _byName.GetValueOrDefault(ContentMountPlan.AsciiLower(name));

    /// <summary>Lists every map folder of the stack and reads its small files (properties,
    /// terrain header, start location, map info, jump points). The stack stays owned by the caller
    /// and must outlive the catalogue.</summary>
    public static WorldCatalog Open(ContentStack stack, WorldLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new(); options.Validate();
        var issues = new List<WorldIssue>();
        var keys = stack.Keys("maps/");
        var folders = keys.Select(k => k.Split('/')).Where(p => p.Length >= 3).GroupBy(p => p[1], p => string.Join('/', p[2..]), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        var loose = keys.Select(k => k.Split('/')).Where(p => p.Length == 2).Select(p => p[1]).ToHashSet(StringComparer.Ordinal);
        var maps = new List<WorldMap>();
        foreach (var folder in folders)
        {
            var files = folder.ToArray();
            if (!files.Contains("map.prp", StringComparer.Ordinal)) { issues.Add(new(WorldIssueCode.Missing, $"maps/{folder.Key}/map.prp")); continue; }
            if (maps.Count >= options.MaxMaps) { issues.Add(new(WorldIssueCode.LimitExceeded, $"maps/{folder.Key}")); continue; }
            if (files.Length > options.MaxFilesPerMap) { issues.Add(new(WorldIssueCode.LimitExceeded, $"maps/{folder.Key}")); continue; }
            var map = new Reader(stack, options).Map(folder.Key, files, loose, out var refusal);
            if (map is null) { issues.Add(refusal!); continue; }
            maps.Add(map);
        }

        var prototypes = new SortedDictionary<int, string>();
        foreach (var key in stack.Keys("proto/"))
        {
            var name = key[6..];
            if (key.Count(c => c == '/') != 1 || !name.EndsWith(".pro", StringComparison.Ordinal)) continue;
            // The original loads every proto/*.pro whatever its name; only the canonical
            // "NNNNNN - label.pro" form is addressable here, so others are reported, not dropped.
            if (!TryPrototypeNumber(name, out var number)) { issues.Add(new(WorldIssueCode.UnrecognisedFile, key)); continue; }
            if (!prototypes.TryAdd(number, key)) issues.Add(new(WorldIssueCode.Malformed, key, number, "duplicate prototype number"));
        }
        return new(stack, options, maps.AsReadOnly(), new ReadOnlyDictionary<int, string>(prototypes), issues.AsReadOnly());
    }

    /// <summary>Reads one sector file. A sector without a file is <see cref="WorldSectorStatus.NoFile"/>;
    /// the original generates such sectors from the terrain grid.</summary>
    public WorldSectorResult ReadSector(WorldMap map, WorldSectorCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(map);
        var path = $"{map.Folder}/{coordinate.Id.ToString(CultureInfo.InvariantCulture)}.sec";
        if (!map.HasSectorFile(coordinate)) return new(WorldSectorStatus.NoFile, null, null);
        var read = new Reader(_stack, _options).Bytes(path, _options.Sector.MaxSectorDecodedEntryBytes, out var issue);
        if (read is null) return new(WorldSectorStatus.Refused, null, issue);
        TerrainLoader.ParsedSector parsed;
        try { parsed = TerrainLoader.ParseSector(read, _options.Sector); }
        catch (TerrainException error)
        {
            return new(WorldSectorStatus.Refused, null, new(error is TerrainLimitExceededException ? WorldIssueCode.LimitExceeded : WorldIssueCode.Malformed, path, null, error.Message));
        }
        var framing = parsed.Framing;
        var lights = Words(read, framing[TerrainSection.Lights], 12);
        var roofs = framing.RoofDiscriminator == 0 ? Unsigned(read, framing[TerrainSection.Roof], 4) : null;
        var scriptRange = framing[TerrainSection.TileScripts];
        var tileScripts = framing.TileScriptCount == 0 ? Array.Empty<IReadOnlyList<int>>()
            : Words(read, new(scriptRange.Offset + 4, scriptRange.Length - 4), 6);
        var sectorScript = framing[TerrainSection.SectorScript].Length == 0 ? null : Words(read, framing[TerrainSection.SectorScript], 3)[0];
        var metadata = framing[TerrainSection.MetadataAndSound].Length == 0 ? null : Words(read, framing[TerrainSection.MetadataAndSound], 6)[0];
        var window = framing.ObjectWindow;
        var objects = new List<WorldObject>();
        WorldIssue? objectIssue = null;
        var bytes = read.AsSpan(window.Range.Offset, window.Range.Length);
        var offset = 0;
        for (var index = 0; index < window.TerminalObjectCount; index++)
        {
            try
            {
                var record = ObjectWireReader.Read(bytes[offset..], _options.Wire, maxRecordBytes: _options.Sector.MaxSectorDecodedEntryBytes);
                objects.Add(Object(record)); offset += record.Length;
            }
            catch (StaticObjectException error) { objectIssue = new(WorldIssueCode.ObjectRefused, path, index, error.Code.ToString()); break; }
        }
        if (objectIssue is null && offset != bytes.Length) objectIssue = new(WorldIssueCode.Malformed, path, window.TerminalObjectCount, "object window has trailing bytes");
        var sector = new WorldSector(coordinate, path, framing.Placeholder - 0xAA0000, lights, Array.AsReadOnly(parsed.Words), roofs, tileScripts,
            sectorScript, metadata, parsed.BlockMask, window.TerminalObjectCount, objects.AsReadOnly(), objectIssue);
        return new(WorldSectorStatus.Read, sector, null);
    }

    /// <summary>The map's mobile objects as a normal game reads them: the game-generated
    /// aggregate when the stack holds one, otherwise the editor's <c>.mob</c> files (which only
    /// the original editor reads; <see cref="WorldMobileResult.Source"/> says which).</summary>
    public WorldMobileResult ReadMobiles(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var reader = new Reader(_stack, _options);
        var objects = new List<WorldObject>(); var issues = new List<WorldIssue>();
        if (map.AggregatePath is { } aggregate)
        {
            var bytes = reader.Bytes(aggregate, _options.MaxAggregateBytes, out var issue);
            if (bytes is null) return new(WorldMobileSource.Aggregate, [], [issue!]);
            if (bytes.Length < AggregateGuidBytes) return new(WorldMobileSource.Aggregate, [], [new(WorldIssueCode.Malformed, aggregate, null, "aggregate GUID is truncated")]);
            var offset = AggregateGuidBytes;
            while (offset < bytes.Length)
            {
                if (objects.Count >= _options.MaxAggregateRecords) { issues.Add(new(WorldIssueCode.LimitExceeded, aggregate, objects.Count)); break; }
                try
                {
                    var record = ObjectWireReader.Read(bytes.AsSpan(offset), _options.Wire, maxRecordBytes: _options.MaxMobileBytes);
                    objects.Add(Object(record)); offset += record.Length;
                }
                catch (StaticObjectException error) { issues.Add(new(WorldIssueCode.ObjectRefused, aggregate, objects.Count, error.Code.ToString())); break; }
            }
            return new(WorldMobileSource.Aggregate, objects.AsReadOnly(), issues.AsReadOnly());
        }
        if (map.EditorMobilePaths.Count == 0) return new(WorldMobileSource.None, [], []);
        foreach (var path in map.EditorMobilePaths)
        {
            var bytes = reader.Bytes(path, _options.MaxMobileBytes, out var issue);
            if (bytes is null) { issues.Add(issue!); continue; }
            try
            {
                var record = ObjectWireReader.Read(bytes, _options.Wire, maxRecordBytes: _options.MaxMobileBytes);
                if (record.Length != bytes.Length) { issues.Add(new(WorldIssueCode.Malformed, path, null, "mobile file has trailing bytes")); continue; }
                objects.Add(Object(record));
            }
            catch (StaticObjectException error) { issues.Add(new(WorldIssueCode.ObjectRefused, path, 0, error.Code.ToString())); }
        }
        return new(WorldMobileSource.Editor, objects.AsReadOnly(), issues.AsReadOnly());
    }

    /// <summary>Decodes the whole terrain grid (raw words, or one zlib row per sector row).</summary>
    public WorldTerrainResult ReadTerrain(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var path = $"{map.Folder}/terrain.tdf";
        if (map.Terrain is not { } header) return new(null, new(WorldIssueCode.Missing, path));
        var bytes = new Reader(_stack, _options).Bytes(path, _options.MaxTerrainBytes, out var issue);
        if (bytes is null) return new(null, issue);
        var width = (int)header.WidthSectors; var height = (int)header.HeightSectors;
        var words = new ushort[checked(width * height)];
        if (!header.RowCompressed)
        {
            if (bytes.Length != TerrainHeaderBytes + words.Length * 2L) return new(null, new(WorldIssueCode.Malformed, path, null, "raw grid length"));
            for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(TerrainHeaderBytes + i * 2));
            return new(new(header, words), null);
        }
        var offset = TerrainHeaderBytes; var row = new byte[width * 2];
        for (var y = 0; y < height; y++)
        {
            if (bytes.Length - offset < 4) return new(null, new(WorldIssueCode.Malformed, path, y, "row length is truncated"));
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)); offset += 4;
            if (size <= 0 || size > bytes.Length - offset) return new(null, new(WorldIssueCode.Malformed, path, y, "row is truncated"));
            try
            {
                using var input = new MemoryStream(bytes, offset, size, writable: false);
                using var inflate = new ZLibStream(input, CompressionMode.Decompress);
                // Like the original, only one row of output is required; bytes after it, or
                // after the zlib stream inside the length-prefixed row, are not examined.
                var filled = 0; int n;
                while (filled < row.Length && (n = inflate.Read(row, filled, row.Length - filled)) > 0) filled += n;
                if (filled != row.Length) return new(null, new(WorldIssueCode.Malformed, path, y, "row inflates to less than one row"));
            }
            catch (InvalidDataException) { return new(null, new(WorldIssueCode.Malformed, path, y, "row is not zlib")); }
            for (var x = 0; x < width; x++) words[x + y * width] = BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(x * 2));
            offset += size;
        }
        if (offset != bytes.Length) return new(null, new(WorldIssueCode.Malformed, path, height, "trailing bytes after the last row"));
        return new(new(header, words), null);
    }

    public WorldPrototypeResult ReadPrototype(int number)
    {
        if (!_prototypePaths.TryGetValue(number, out var path)) return new(null, new(WorldIssueCode.Missing, $"proto/{number:D6}"));
        var bytes = new Reader(_stack, _options).Bytes(path, _options.Wire.MaxPrototypeBytes, out var issue);
        if (bytes is null) return new(null, issue);
        try
        {
            var record = ObjectWireReader.Read(bytes, _options.Wire, prototype: true, maxRecordBytes: _options.Wire.MaxPrototypeBytes);
            if (record.Length != bytes.Length) return new(null, new(WorldIssueCode.Malformed, path, null, "prototype file has trailing bytes"));
            if (record.Own.Number != number) return new(null, new(WorldIssueCode.Malformed, path, null, "prototype number differs from its file name"));
            return new(Object(record), null);
        }
        catch (StaticObjectException error) { return new(null, new(WorldIssueCode.ObjectRefused, path, 0, error.Code.ToString())); }
    }

    /// <summary>The name of a map's game-generated mobile file: letters rotated by 13 within
    /// their case (the original's wrap is one short of the alphabet), then the whole name
    /// reversed (pinned CE <c>map_obfuscate_name</c>).</summary>
    public static string AggregateName(string mapName)
    {
        ArgumentNullException.ThrowIfNull(mapName);
        var chars = mapName.Select(c => c switch
        {
            >= 'A' and <= 'Z' => (char)(c + 13 > 'Z' ? c + 13 - 24 : c + 13),
            >= 'a' and <= 'z' => (char)(c + 13 > 'z' ? c + 13 - 24 : c + 13),
            _ => c,
        }).ToArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>C <c>strtoll(s, NULL, 10)</c>: leading white space, an optional sign, then
    /// as many decimal digits as follow; no digits gives 0, overflow saturates.</summary>
    internal static long StrToLong(string text)
    {
        var i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = false;
        if (i < text.Length && text[i] is '+' or '-') negative = text[i++] == '-';
        long value = 0;
        for (; i < text.Length && char.IsAsciiDigit(text[i]); i++)
        {
            var digit = text[i] - '0';
            if (value > (long.MaxValue - digit) / 10) return negative ? long.MinValue : long.MaxValue;
            value = value * 10 + digit;
        }
        return negative ? -value : value;
    }

    internal static bool TryPrototypeNumber(string fileName, out int number)
    {
        number = 0;
        if (fileName.Length < 14 || !fileName.EndsWith(".pro", StringComparison.Ordinal) || fileName[6..9] != " - ") return false;
        return int.TryParse(fileName.AsSpan(0, 6), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static IReadOnlyList<IReadOnlyList<int>> Words(byte[] bytes, TerrainByteRange range, int width)
    {
        var stride = width * 4; var rows = new IReadOnlyList<int>[range.Length / stride];
        for (var r = 0; r < rows.Length; r++)
        {
            var row = new int[width];
            for (var w = 0; w < width; w++) row[w] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(range.Offset + r * stride + w * 4));
            rows[r] = Array.AsReadOnly(row);
        }
        return Array.AsReadOnly(rows);
    }

    private static IReadOnlyList<uint> Unsigned(byte[] bytes, TerrainByteRange range, int skip)
    {
        var words = new uint[(range.Length - skip) / 4];
        for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(range.Offset + skip + i * 4));
        return Array.AsReadOnly(words);
    }

    internal static WorldObject Object(ObjectWireRecord record)
    {
        var fields = new SortedDictionary<int, WorldFieldValue>();
        foreach (var (slot, value) in record.Fields)
            fields[slot] = new((WorldFieldPresence)(int)value.Presence, value.Number,
                Array.AsReadOnly(value.Elements.Select(e => new WorldFieldElement(e.Key, e.Words)).ToArray()), value.Fingerprint);
        return new(record.Type, record.Parent.Kind == 1 ? record.Parent.Number : null, record.Own.Kind, record.Own.Sha256, record.Sha256, record.SemanticSha256,
            new ReadOnlyDictionary<int, WorldFieldValue>(fields));
    }

    private readonly struct Reader(ContentStack stack, WorldLoaderOptions options)
    {
        internal byte[]? Bytes(string path, long limit, out WorldIssue? issue)
        {
            issue = null;
            var resolved = stack.Resolve(path);
            if (resolved.Winner is not { } winner) { issue = new(WorldIssueCode.Missing, path); return null; }
            if (winner.Length > limit) { issue = new(WorldIssueCode.LimitExceeded, path); return null; }
            var read = stack.Read(winner);
            if (read.Status != ContentReadStatus.Read) { issue = new(read.Status == ContentReadStatus.TooLarge ? WorldIssueCode.LimitExceeded : WorldIssueCode.Unreadable, path); return null; }
            if (read.Bytes!.Length > limit) { issue = new(WorldIssueCode.LimitExceeded, path); return null; }
            return read.Bytes;
        }

        internal WorldMap? Map(string name, string[] files, HashSet<string> loose, out WorldIssue? refusal)
        {
            var folder = $"maps/{name}";
            var issues = new List<WorldIssue>();
            var prp = Bytes($"{folder}/map.prp", options.MaxSmallFileBytes, out refusal);
            if (prp is null) return null;
            refusal = new(WorldIssueCode.Malformed, $"{folder}/map.prp");
            if (prp.Length != 24) return null;
            var properties = new WorldMapProperties(BinaryPrimitives.ReadInt32LittleEndian(prp), BinaryPrimitives.ReadInt32LittleEndian(prp.AsSpan(4)),
                BinaryPrimitives.ReadInt64LittleEndian(prp.AsSpan(8)), BinaryPrimitives.ReadInt64LittleEndian(prp.AsSpan(16)));
            if (properties.WidthTiles <= 0 || properties.HeightTiles <= 0) return null;
            refusal = null;

            WorldTerrainHeader? terrain = null;
            if (files.Contains("terrain.tdf", StringComparer.Ordinal))
            {
                var tdf = Bytes($"{folder}/terrain.tdf", options.MaxTerrainBytes, out var tdfIssue);
                if (tdf is null) issues.Add(tdfIssue!);
                else if (tdf.Length < TerrainHeaderBytes) issues.Add(new(WorldIssueCode.Malformed, $"{folder}/terrain.tdf", null, "header is truncated"));
                else
                {
                    var header = new WorldTerrainHeader(BinaryPrimitives.ReadSingleLittleEndian(tdf), (BinaryPrimitives.ReadUInt32LittleEndian(tdf.AsSpan(4)) & 1) != 0,
                        BinaryPrimitives.ReadInt64LittleEndian(tdf.AsSpan(8)), BinaryPrimitives.ReadInt64LittleEndian(tdf.AsSpan(16)), BinaryPrimitives.ReadInt32LittleEndian(tdf.AsSpan(24)));
                    // Flag bits other than bit 0 (row compression) are ignored, as by the original.
                    if (header.Version != 1.2f) issues.Add(new(WorldIssueCode.Malformed, $"{folder}/terrain.tdf", null, "version is not 1.2"));
                    else if (header.WidthSectors <= 0 || header.HeightSectors <= 0 || header.WidthSectors > WorldSectorCoordinate.MaxCoordinate + 1L ||
                             header.HeightSectors > WorldSectorCoordinate.MaxCoordinate + 1L || header.WidthSectors * header.HeightSectors > options.MaxTerrainSectors)
                        issues.Add(new(WorldIssueCode.LimitExceeded, $"{folder}/terrain.tdf", null, "grid size"));
                    else terrain = header;
                }
            }
            else issues.Add(new(WorldIssueCode.Missing, $"{folder}/terrain.tdf"));

            WorldLocation? start = null;
            if (files.Contains("startloc.txt", StringComparer.Ordinal))
            {
                var text = Bytes($"{folder}/startloc.txt", options.MaxSmallFileBytes, out var startIssue);
                if (text is null) issues.Add(startIssue!);
                // The original reads the first two lines (blank ones too) with strtoll; it fails
                // the map only when a line is missing.
                else if (RawLines(text) is [var x, var y, ..] && StrToLong(x) is var sx and >= 0 && StrToLong(y) is var sy and >= 0)
                    start = new(sx, sy);
                else issues.Add(new(WorldIssueCode.Malformed, $"{folder}/startloc.txt"));
            }

            WorldMapInfo? info = null;
            if (files.Contains("mapinfo.txt", StringComparer.Ordinal))
            {
                var text = Bytes($"{folder}/mapinfo.txt", options.MaxSmallFileBytes, out var infoIssue);
                if (text is null) issues.Add(infoIssue!);
                else info = MapInfo(text, $"{folder}/mapinfo.txt", issues);
            }

            var jumps = new List<WorldJumpPoint>();
            if (files.Contains("map.jmp", StringComparer.Ordinal))
            {
                var jmp = Bytes($"{folder}/map.jmp", 4L + (long)options.MaxJumpPoints * JumpPointBytes, out var jumpIssue);
                if (jmp is null) issues.Add(jumpIssue!);
                else if (jmp.Length < 4 || BinaryPrimitives.ReadInt32LittleEndian(jmp) is var count && (count < 0 || count > options.MaxJumpPoints || jmp.Length != 4L + (long)count * JumpPointBytes))
                    issues.Add(new(WorldIssueCode.Malformed, $"{folder}/map.jmp"));
                else
                    for (var i = 0; i < count; i++)
                    {
                        var at = jmp.AsSpan(4 + i * JumpPointBytes);
                        jumps.Add(new(BinaryPrimitives.ReadUInt32LittleEndian(at), BinaryPrimitives.ReadInt32LittleEndian(at[4..]),
                            WorldLocation.FromPacked(BinaryPrimitives.ReadInt64LittleEndian(at[8..])), BinaryPrimitives.ReadInt32LittleEndian(at[16..]),
                            BinaryPrimitives.ReadInt32LittleEndian(at[20..]), WorldLocation.FromPacked(BinaryPrimitives.ReadInt64LittleEndian(at[24..]))));
                    }
            }

            var sectors = new List<WorldSectorCoordinate>(); var mobiles = new List<string>();
            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                if (file.Contains('/')) issues.Add(new(WorldIssueCode.UnrecognisedFile, $"{folder}/{file}"));
                else if (file.EndsWith(".sec", StringComparison.Ordinal))
                {
                    var stem = file[..^4];
                    if (stem.Length is > 0 and <= 16 && stem.All(char.IsAsciiDigit) && long.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
                        WorldSectorCoordinate.TryFromId(id, out var coordinate) && coordinate.Id.ToString(CultureInfo.InvariantCulture) == stem)
                        sectors.Add(coordinate);
                    else issues.Add(new(WorldIssueCode.UnrecognisedFile, $"{folder}/{file}"));
                }
                else if (file.EndsWith(".mob", StringComparison.Ordinal)) mobiles.Add($"{folder}/{file}");
                else if (file is not ("map.prp" or "terrain.tdf" or "startloc.txt" or "mapinfo.txt" or "map.jmp"))
                    issues.Add(new(WorldIssueCode.UnrecognisedFile, $"{folder}/{file}"));
            }
            sectors.Sort((a, b) => a.Id.CompareTo(b.Id));
            var aggregate = AggregateName(name);
            return new(name, properties, terrain, start, info, jumps.AsReadOnly(), sectors.AsReadOnly(), mobiles.AsReadOnly(),
                loose.Contains(aggregate) ? $"maps/{aggregate}" : null, issues.AsReadOnly());
        }

        private static string[] Lines(byte[] text) => RawLines(text).Where(l => l.Length > 0).ToArray();
        private static string[] RawLines(byte[] text)
        {
            var lines = Encoding.Latin1.GetString(text).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines.ToArray();
        }


        /// <summary>The original reads <c>Key:Value</c> lines and acts on LightScheme and
        /// SoundScheme (music, ambient); other lines are ignored, so they are only counted.</summary>
        private static WorldMapInfo MapInfo(byte[] text, string path, List<WorldIssue> issues)
        {
            int? light = null, music = null, ambient = null; var unknown = 0;
            foreach (var line in Lines(text))
            {
                var parts = line.Split(':', 2);
                var key = parts[0].Trim();
                if (parts.Length == 2 && key.Equals("LightScheme", StringComparison.OrdinalIgnoreCase))
                {
                    // Each line is applied as read, so a later one wins, as in the original.
                    if (!int.TryParse(parts[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) issues.Add(new(WorldIssueCode.Malformed, path, null, "LightScheme"));
                    else light = value;
                }
                else if (parts.Length == 2 && key.Equals("SoundScheme", StringComparison.OrdinalIgnoreCase))
                {
                    var values = parts[1].Split(',');
                    if (values.Length != 2 || !int.TryParse(values[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var m) ||
                        !int.TryParse(values[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var a)) issues.Add(new(WorldIssueCode.Malformed, path, null, "SoundScheme"));
                    else { music = m; ambient = a; }
                }
                else unknown++;
            }
            return new(light, music, ambient, unknown);
        }
    }
}
