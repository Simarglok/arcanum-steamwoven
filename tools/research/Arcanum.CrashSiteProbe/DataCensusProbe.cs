using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-036: read-only census of every original data type a universal loader must read.
// It resolves every virtual path through the module content stack (ARC-DATA-011), parses
// each winner with the readers this repository already has (production Data readers where
// public, the probe's own object/sector framing otherwise) and reports aggregate counts and
// typed refusal buckets only. It never prints payload bytes, names or text.
internal static partial class Program
{
    private const int CensusMaxSectorBytes = 1_024 * 1_024;
    private const int CensusMaxAggregateBytes = 8 * 1_024 * 1_024;
    private const int CensusMaxTerrainBytes = 16 * 1_024 * 1_024;

    internal sealed class CensusTally
    {
        private readonly SortedDictionary<string, long> _counts = new(StringComparer.Ordinal);
        public void Add(string key, long by = 1) => _counts[key] = _counts.GetValueOrDefault(key) + by;
        public void Max(string key, long value) => _counts[key] = Math.Max(_counts.GetValueOrDefault(key), value);
        public long this[string key] => _counts.GetValueOrDefault(key);
        public IReadOnlyDictionary<string, long> Snapshot() => _counts;
    }

    private static int ProbeDataCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("CENSUS_USAGE --data-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"CENSUS_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"CENSUS_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        var report = Census(stack);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Every virtual path of the stack with its winning candidate. The stack does not
    /// expose its index, so names are gathered from each source and resolved again; a name the
    /// stack did not index (skipped or cache files) is not resolvable and drops out.</summary>
    private static SortedDictionary<string, ContentCandidate> CensusKeys(ContentStack stack)
    {
        var keys = new SortedDictionary<string, ContentCandidate>(StringComparer.Ordinal);
        foreach (var source in stack.Plan.SearchOrder)
        {
            IEnumerable<string> names;
            if (source.Kind == ContentSourceKind.Archive)
            {
                using var archive = DatArchive.Open(source.Path);
                names = archive.Entries.Where(e => (e.Flags & 0x400) == 0)
                    .Select(e => Encoding.Latin1.GetString(e.RawNameBytes.ToArray()).TrimEnd('\0')).ToArray();
            }
            else
            {
                names = Directory.EnumerateFiles(source.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Select(f => Path.GetRelativePath(source.Path, f).Replace('/', '\\')).ToArray();
            }
            foreach (var name in names)
            {
                var resolved = stack.Resolve(name);
                if (resolved.Status == ContentResolutionStatus.Resolved && !keys.ContainsKey(resolved.Key)) keys[resolved.Key] = resolved.Winner!;
            }
        }
        return keys;
    }

    internal static string CensusCategory(string key)
    {
        var parts = key.Split('/');
        var extension = Path.GetExtension(parts[^1]);
        var top = parts.Length == 1 ? "<root>" : parts[0];
        return $"{top}|{(extension.Length == 0 ? "<none>" : extension)}";
    }

    /// <summary>Folds numbers that vary per record (offsets, prefixes, counts) out of a refusal
    /// message so equal causes share one bucket; field numbers and versions are kept.</summary>
    internal static string CensusBucket(Exception error)
    {
        var text = Regex.Replace(error.Message, @"(offset|prefix|matches)=\d+", "$1=#");
        return $"{error.GetType().Name}: {text}";
    }

    private static object Census(ContentStack stack)
    {
        var keys = CensusKeys(stack);
        var categories = new CensusTally(); var categoryBytes = new CensusTally();
        foreach (var (key, winner) in keys) { var c = CensusCategory(key); categories.Add(c); categoryBytes.Add(c, winner.Length); }

        var refusals = new CensusTally();
        // Every read goes through here, so a TooLarge or unreadable member is one refusal
        // bucket for its role, never an aborted run.
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|CENSUS_READ_{result.Status}");
            return null;
        }

        // Maps: every folder directly under maps/ (each holds a map's files).
        var mapFiles = keys.Where(k => k.Key.StartsWith("maps/", StringComparison.Ordinal) && k.Key.Count(c => c == '/') == 2)
            .GroupBy(k => k.Key.Split('/')[1]).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        var mapRows = new List<object>(); var sectorTotals = new CensusTally(); var objectTypes = new CensusTally();
        var mobileTypes = new CensusTally();
        foreach (var map in mapFiles)
        {
            var row = new CensusTally();
            foreach (var (key, winner) in map)
            {
                var file = key.Split('/')[2];
                var role = file.EndsWith(".sec", StringComparison.Ordinal) ? "map-sector" : file.EndsWith(".mob", StringComparison.Ordinal) ? "mobile"
                    : file switch { "terrain.tdf" => "terrain", "map.jmp" => "jump", "map.prp" => "properties", _ => null };
                if (role is null) { row.Add($"file|{Path.GetExtension(file)}"); continue; }
                if (Read(winner, role) is not { } bytes) { row.Add("read_refused"); continue; }
                switch (role)
                {
                    case "map-sector": CensusSector(bytes, row, sectorTotals, objectTypes, refusals, role); break;
                    case "mobile": CensusMobile(bytes, row, mobileTypes, refusals); break;
                    case "terrain": CensusTerrain(bytes, row, refusals); break;
                    case "jump": CensusJump(bytes, row, refusals); break;
                    default: CensusProperties(bytes, row, refusals); break;
                }
            }
            var snapshot = row.Snapshot();
            mapRows.Add(new { map = map.Key, counts = snapshot });
        }

        // Game-generated mobile aggregates live directly under maps\ with a selector name.
        var mapNames = mapFiles.Select(m => m.Key).ToArray();
        var aggregates = new List<object>(); var aggregateTypes = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("maps/", StringComparison.Ordinal) && k.Key.Count(c => c == '/') == 1))
        {
            var selector = key[5..];
            var owner = mapNames.Where(name => string.Equals(CaveAggregateSelector(name), selector, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (Read(winner, "aggregate") is not { } bytes) { aggregates.Add(new { owner_map_matches = owner.Length, owner_map = owner.FirstOrDefault(), refused = true }); continue; }
            try
            {
                var records = FrameAggregate(bytes, CensusMaxAggregateBytes);
                foreach (var record in records) aggregateTypes.Add($"type={record.Instance.Type}");
                aggregates.Add(new { owner_map_matches = owner.Length, owner_map = owner.FirstOrDefault(), bytes = bytes.Length, records = records.Count });
            }
            catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException)
            {
                refusals.Add($"aggregate|{CensusBucket(error)}");
                aggregates.Add(new { owner_map_matches = owner.Length, owner_map = owner.FirstOrDefault(), refused = true });
            }
        }

        // Terrain template sectors used to generate absent sectors of a map.
        var templateTotals = new CensusTally(); var templateTypes = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("terrain/", StringComparison.Ordinal) && k.Key.EndsWith(".sec", StringComparison.Ordinal)))
            if (Read(winner, "template-sector") is { } bytes) CensusSector(bytes, templateTotals, null, templateTypes, refusals, "template-sector");

        // Prototypes.
        var prototypeTypes = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("proto/", StringComparison.Ordinal) && k.Key.EndsWith(".pro", StringComparison.Ordinal)))
        {
            if (Read(winner, "prototype") is not { } bytes) continue;
            try { prototypeTypes.Add($"type={ObjectWire.ParsePrototype(bytes).Type}"); }
            catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException) { refusals.Add($"prototype|{CensusBucket(error)}"); }
        }

        // ART: header, palettes and frame table of every file.
        var art = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.EndsWith(".art", StringComparison.Ordinal)))
        {
            var group = key.StartsWith("art/", StringComparison.Ordinal) && key.Count(c => c == '/') >= 2 ? key.Split('/')[1] : "<other>";
            if (Read(winner, "art") is not { } bytes) { art.Add($"{group}|refused"); continue; }
            try
            {
                var metadata = ArtDecoder.Inspect(bytes);
                art.Add($"{group}|inspected"); art.Add($"{group}|frames", metadata.Frames.Count);
            }
            catch (Exception error) when (error is ArtException or InvalidDataException) { art.Add($"{group}|refused"); refusals.Add($"art|{CensusBucket(error)}"); }
        }

        var mes = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.EndsWith(".mes", StringComparison.Ordinal)))
        {
            if (Read(winner, "mes") is not { } bytes) continue;
            mes.Add($"status={MesFile.Parse(bytes).Status}");
        }

        var dlg = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            if (Read(winner, "dlg") is not { } bytes) { dlg.Add("refused"); continue; }
            try { _ = DlgReader.Read(bytes); dlg.Add("read"); }
            catch (Exception error) when (error is DlgException or InvalidDataException) { dlg.Add("refused"); refusals.Add($"dlg|{CensusBucket(error)}"); }
        }

        var scr = new CensusTally();
        foreach (var (key, winner) in keys.Where(k => k.Key.EndsWith(".scr", StringComparison.Ordinal)))
        {
            if (Read(winner, "scr") is not { } bytes) { scr.Add("refused"); continue; }
            try { var (_, lines, _) = CaveScriptFraming(bytes); scr.Add("framed"); scr.Add("lines", lines); }
            catch (InvalidDataException error) { scr.Add("refused"); refusals.Add($"scr|{CensusBucket(error)}"); }
        }

        return new
        {
            module = stack.Plan.ModuleName,
            sources = stack.Plan.SearchOrder.Count,
            keys = keys.Count,
            categories = categories.Snapshot().Where(c => c.Value >= 5).ToDictionary(c => c.Key, c => new { files = c.Value, bytes = categoryBytes[c.Key] }),
            small_categories = categories.Snapshot().Where(c => c.Value < 5).Sum(c => c.Value),
            maps = mapRows,
            sector_totals = sectorTotals.Snapshot(),
            sector_object_types = objectTypes.Snapshot(),
            mobile_types = mobileTypes.Snapshot(),
            aggregates,
            aggregate_types = aggregateTypes.Snapshot(),
            template_totals = templateTotals.Snapshot(),
            template_object_types = templateTypes.Snapshot(),
            prototype_types = prototypeTypes.Snapshot(),
            art = art.Snapshot(),
            mes = mes.Snapshot(),
            dlg = dlg.Snapshot(),
            scr = scr.Snapshot(),
            refusals = refusals.Snapshot(),
        };
    }

    private static void CensusSector(byte[] bytes, CensusTally row, CensusTally? totals, CensusTally types, CensusTally refusals, string role)
    {
        void Both(string key, long by = 1) { row.Add(key, by); totals?.Add(key, by); }
        Both("sectors");
        if (bytes.Length > CensusMaxSectorBytes) { Both("sector_refused"); refusals.Add($"{role}|CENSUS_SECTOR_BYTES"); return; }
        SectorSlice slice; SectorFraming framing;
        try { slice = SectorSlice.Parse(bytes, false); framing = SectorFraming.Parse(bytes, slice.LightCount); }
        catch (InvalidDataException error) { Both("sector_refused"); refusals.Add($"{role}|framing|{CensusBucket(error)}"); return; }
        Both("lights", slice.LightCount);
        if (slice.InvalidTileArtIds != 0) Both("sectors_with_invalid_tiles");
        if (slice.NonTileArtIds != 0) Both("sectors_with_non_tile_art");
        if (framing.RoofEmpty == 0) Both("sectors_with_roofs");
        Both($"placeholder={framing.Placeholder - 0xAA0000}");
        Both("tile_scripts", framing.TileScriptCount);
        Both("objects_declared", slice.TrailingInt32);
        row.Max("max_objects_per_sector", slice.TrailingInt32); totals?.Max("max_objects_per_sector", slice.TrailingInt32);
        row.Max("max_lights_per_sector", slice.LightCount); totals?.Max("max_lights_per_sector", slice.LightCount);
        try
        {
            var instances = ObjectWire.ParseWindow(bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), slice.TrailingInt32);
            Both("objects_parsed", instances.Count);
            foreach (var instance in instances) types.Add($"type={instance.Type}");
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException)
        {
            Both("object_windows_refused");
            refusals.Add($"{role}|objects|{CensusBucket(error)}");
        }
    }

    private static void CensusMobile(byte[] bytes, CensusTally row, CensusTally types, CensusTally refusals)
    {
        row.Add("mobiles");
        try
        {
            var instance = ObjectWire.ParseNext(bytes, out var consumed);
            if (consumed != bytes.Length) throw new InvalidDataException("CENSUS_MOBILE_TRAILING_BYTES");
            types.Add($"type={instance.Type}");
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException)
        {
            row.Add("mobiles_refused"); refusals.Add($"mobile|{CensusBucket(error)}");
        }
    }

    internal static (float Version, uint Flags, long Width, long Height, int BaseType) CensusTerrainHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 32) throw new InvalidDataException("CENSUS_TDF_HEADER");
        return (BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]), BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]));
    }

    /// <summary>Checks the TDF framing: header, then either one raw word per sector or, with flag
    /// bit 0, one length-prefixed zlib row per sector row that inflates to exactly one row.</summary>
    internal static int CensusTerrainRows(ReadOnlySpan<byte> bytes)
    {
        var (version, flags, width, height, _) = CensusTerrainHeader(bytes);
        if (Math.Abs(version - 1.2f) > 1e-6f) throw new InvalidDataException("CENSUS_TDF_VERSION");
        if (width is <= 0 or > 0x4000000 || height is <= 0 or > 0x4000000 || width * height * 2 > CensusMaxTerrainBytes) throw new InvalidDataException("CENSUS_TDF_SIZE");
        if ((flags & 1) == 0)
        {
            if (bytes.Length != 32 + width * height * 2) throw new InvalidDataException("CENSUS_TDF_RAW_LENGTH");
            return (int)height;
        }
        var offset = 32; var row = new byte[width * 2];
        for (var y = 0; y < height; y++)
        {
            if (bytes.Length - offset < 4) throw new InvalidDataException("CENSUS_TDF_ROW_TRUNCATED");
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]); offset += 4;
            if (size <= 0 || size > bytes.Length - offset) throw new InvalidDataException("CENSUS_TDF_ROW_TRUNCATED");
            using var input = new MemoryStream(bytes.Slice(offset, size).ToArray());
            using var inflate = new ZLibStream(input, CompressionMode.Decompress);
            var read = 0; int n;
            while (read < row.Length && (n = inflate.Read(row, read, row.Length - read)) > 0) read += n;
            if (read != row.Length || inflate.ReadByte() != -1) throw new InvalidDataException("CENSUS_TDF_ROW_LENGTH");
            offset += size;
        }
        if (offset != bytes.Length) throw new InvalidDataException("CENSUS_TDF_TRAILING");
        return (int)height;
    }

    private static void CensusTerrain(byte[] bytes, CensusTally row, CensusTally refusals)
    {
        try
        {
            var header = CensusTerrainHeader(bytes);
            CensusTerrainRows(bytes);
            row.Add($"tdf_sectors", header.Width * header.Height);
            row.Add($"tdf_compressed={header.Flags & 1}");
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OverflowException)
        {
            row.Add("tdf_refused"); refusals.Add($"terrain|{CensusBucket(error)}");
        }
    }

    private static void CensusJump(byte[] bytes, CensusTally row, CensusTally refusals)
    {
        if (bytes.Length < 4) { row.Add("jump_refused"); refusals.Add("jump|CENSUS_JMP_TRUNCATED"); return; }
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        row.Add("jump_points", count);
        if (count > 0) row.Add($"jump_entry_bytes={(bytes.Length - 4) / count}");
        if (count < 0 || (count > 0 && (bytes.Length - 4) % count != 0) || (count == 0 && bytes.Length != 4)) { row.Add("jump_refused"); refusals.Add("jump|CENSUS_JMP_FRAMING"); }
    }

    private static void CensusProperties(byte[] bytes, CensusTally row, CensusTally refusals)
    {
        if (bytes.Length != 24) { row.Add("properties_refused"); refusals.Add("properties|CENSUS_PRP_LENGTH"); return; }
        row.Add($"prp_base_terrain={BinaryPrimitives.ReadInt32LittleEndian(bytes)}");
        row.Add("prp_width_tiles", BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8)));
        row.Add("prp_height_tiles", BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16)));
    }

    private static void DataCensusSelfTest()
    {
        static void Check(bool condition, string what) { if (!condition) throw new InvalidOperationException($"census self-test failed: {what}"); }
        Check(CensusCategory("maps/x/1.sec") == "maps|.sec", "category of a sector");
        Check(CensusCategory("arcanum.cfg") == "<root>|.cfg", "category of a root file");
        Check(CensusCategory("maps/selector") == "maps|<none>", "category of an aggregate");
        Check(CensusBucket(new InvalidDataException("AGGREGATE_SUFFIX offset=17 prefix=3 reason=field=12 offset=9 x")) ==
              "InvalidDataException: AGGREGATE_SUFFIX offset=# prefix=# reason=field=12 offset=# x", "bucket folds offsets, keeps fields");

        // Synthetic TDF: 2x2 sectors, compressed rows of two words each.
        var header = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(header, 1.2f); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), 2); BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), 2);
        var file = new List<byte>(header);
        for (var y = 0; y < 2; y++)
        {
            using var packed = new MemoryStream();
            using (var deflate = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(new byte[] { 1, 0, 2, 0 });
            var size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, (int)packed.Length);
            file.AddRange(size); file.AddRange(packed.ToArray());
        }
        Check(CensusTerrainRows(file.ToArray()) == 2, "compressed TDF rows");
        var truncated = file.ToArray()[..^1];
        try { CensusTerrainRows(truncated); Check(false, "truncated TDF refused"); } catch (InvalidDataException) { }
        var raw = new byte[32 + 8]; header.CopyTo(raw, 0); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0);
        Check(CensusTerrainRows(raw) == 2, "raw TDF rows");
        var wrongVersion = (byte[])raw.Clone(); BinaryPrimitives.WriteSingleLittleEndian(wrongVersion, 1.1f);
        try { CensusTerrainRows(wrongVersion); Check(false, "TDF version refused"); } catch (InvalidDataException) { }
        Console.WriteLine("data-census self-test: PASS");
    }
}
