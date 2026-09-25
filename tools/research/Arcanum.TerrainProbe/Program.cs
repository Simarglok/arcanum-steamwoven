using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data;

namespace Arcanum.TerrainProbe;

internal static class Program
{
    private const string SectorName = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    private const string TileNamesPath = "art\\tile\\tilename.mes";
    private const string FacadeNamesPath = "art\\facade\\facadename.mes";
    private const long ExpectedModuleBytes = 355_938_305;
    private const string ExpectedModuleSha256 = "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f";
    private const int TileCount = 64 * 64;
    private const int MaxEntryBytes = 1024 * 1024;
    private static readonly char[] EdgeCodes = "06b489237ea5dc10".ToCharArray();
    private static readonly int[] EdgeForward = [0, 1, 8, 3, 4, 5, 6, 7, 8, 3, 10, 11, 6, 7, 14, 15];
    private static readonly int[] EdgeReverse = [0, 1, 2, 9, 4, 5, 12, 13, 2, 9, 10, 11, 12, 13, 14, 15];

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS (type-specific palettes, ordinal MES lookup, path branches, and placement arithmetic)");
                return 0;
            }

            if (args.Length != 4 || args[0] != "--module" || args[2] != "--resource")
            {
                throw new ArgumentException("usage: --self-test | --module PATH --resource PATH");
            }

            Probe(args[1], args[3]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"terrain-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void Probe(string modulePath, string resourcePath)
    {
        var moduleBefore = Fingerprint.Create(modulePath);
        if (moduleBefore.Bytes != ExpectedModuleBytes || moduleBefore.Sha256 != ExpectedModuleSha256)
        {
            throw new InvalidDataException("Module fingerprint does not match the frozen Crash Site input.");
        }

        var resourceBefore = Fingerprint.Create(resourcePath);
        using var module = DatArchive.Open(modulePath, Options());
        using var resources = DatArchive.Open(resourcePath, Options());
        var sector = ReadUnique(module, SectorName, "selected sector");
        if (sector.Flags != 0x00000002 || sector.DeclaredOutputSize != 47_291)
        {
            throw new InvalidDataException("The selected sector metadata no longer matches the frozen input.");
        }

        var sectorBytes = module.ReadEntry(sector);
        var words = ReadTileWords(sectorBytes);
        var tileNames = TileNameTable.Parse(ParseMes(ReadUniquePayload(resources, TileNamesPath)));
        var facadeNames = FacadeNameTable.Parse(ParseMes(ReadUniquePayload(resources, FacadeNamesPath)));
        Console.WriteLine($"lookup tables tile_categories={tileNames.Counts} facade_entries={facadeNames.Count}");
        var resolver = new Resolver(resources, tileNames, facadeNames);
        var results = words.Select((word, ordinal) => resolver.Resolve(word, ordinal)).ToArray();

        var tileResults = results.Where(result => result.Kind == TerrainKind.Tile).ToArray();
        var facadeResults = results.Where(result => result.Kind == TerrainKind.Facade).ToArray();
        if (tileResults.Length == 0 || facadeResults.Length == 0)
        {
            throw new InvalidDataException("The selected sector did not contain both supported terrain identifier classes.");
        }

        var samples = InspectRepresentatives(resources, results).ToArray();
        if (samples.Count(sample => sample.Kind == TerrainKind.Tile) < 3 ||
            samples.Count(sample => sample.Kind == TerrainKind.Facade) < 3)
        {
            throw new InvalidDataException("The bounded run did not obtain three distinct verified representative identifiers per observed class.");
        }

        var moduleAfter = Fingerprint.Create(modulePath);
        var resourceAfter = Fingerprint.Create(resourcePath);
        if (moduleBefore != moduleAfter || resourceBefore != resourceAfter)
        {
            throw new InvalidDataException("An input changed during the read-only probe.");
        }

        Console.WriteLine($"module sector_ordinal={sector.Ordinal} decoded_bytes={sector.DeclaredOutputSize} tiles={words.Length}");
        Console.WriteLine($"resolution tile_cells={tileResults.Length} facade_cells={facadeResults.Length} unique_tile_ids={tileResults.Select(item => item.RawId).Distinct().Count()} unique_facade_ids={facadeResults.Select(item => item.RawId).Distinct().Count()} source_candidates=1 tile_unresolved_cells={tileResults.Count(item => !item.Success)} facade_unresolved_cells={facadeResults.Count(item => !item.Success)}");
        ReportBranchCoverage(results, samples);
        foreach (var sample in samples)
        {
            Console.WriteLine($"sample kind={sample.Kind.ToString().ToLowerInvariant()} branch={sample.Branch} local=({sample.LocalX},{sample.LocalY}) ordinal={sample.Ordinal} raw=0x{sample.RawId:X8} packed={sample.Packed} lookup_rule={sample.LookupRule} path={sample.Path} archive_entry_ordinal={sample.EntryOrdinal} art_selection=group:0,frame:{sample.Frame},requested_palette:{sample.RequestedPalette},effective_palette:{sample.EffectivePalette} dimensions={sample.Width}x{sample.Height} hot=({sample.HotX},{sample.HotY}) offset=({sample.OffsetX},{sample.OffsetY}) anchor_relative=({sample.AnchorX},{sample.AnchorY}) anchor_boundary=cell_top_left_plus_1_x");
        }
        Console.WriteLine("placement relative(0,0)=0,0 relative(1,0)=-40,20 relative(0,1)=40,20 start(30,32)=80,1240");
        Console.WriteLine("integrity before_after_match=true");
    }

    private static IEnumerable<FrameSample> InspectRepresentatives(DatArchive resources, IEnumerable<Resolution> source)
    {
        var candidates = source.Where(result => result.Success)
            .GroupBy(result => new { result.Kind, result.RawId })
            .Select(group => group.First())
            .OrderBy(result => result.Kind)
            .ThenBy(result => result.Branch)
            .ThenBy(result => result.RawId)
            .ToArray();
        var selected = new List<Resolution>();
        foreach (var branch in candidates.GroupBy(result => new { result.Kind, result.Branch }))
        {
            selected.Add(branch.First());
        }
        foreach (var kind in Enum.GetValues<TerrainKind>())
        {
            selected.AddRange(candidates.Where(result => result.Kind == kind && selected.All(chosen => chosen.Kind != kind || chosen.RawId != result.RawId))
                .Take(Math.Max(0, 3 - selected.Count(chosen => chosen.Kind == kind))));
        }

        foreach (var result in selected.OrderBy(result => result.Kind).ThenBy(result => result.Branch).ThenBy(result => result.RawId))
        {
            if (result.Entry is null || result.Entry.DeclaredOutputSize > MaxEntryBytes)
            {
                throw new InvalidDataException("A representative ART entry violates the bounded inspection policy.");
            }

            var art = resources.ReadEntry(result.Entry);
            var metadata = ArtDecoder.Inspect(art, new ArtDecoderOptions { MaxInputBytes = MaxEntryBytes, MaxFrameCount = 4096, MaxFrameTableBytes = 256 * 1024, MaxDimension = 4096, MaxFramePixels = 4 * 1024 * 1024, MaxRgbaBytes = 16 * 1024 * 1024, MaxEncodedFrameBytes = MaxEntryBytes });
            var frame = result.Frame;
            if (frame < 0 || frame >= metadata.Header.FramesPerGroup)
            {
                throw new InvalidDataException("The packed terrain identifier selects a frame outside the bounded ART metadata.");
            }
            var decoded = ArtDecoder.Decode(art, new ArtFrameSelection(0, frame, result.Palette), new ArtDecoderOptions { MaxInputBytes = MaxEntryBytes, MaxFrameCount = 4096, MaxFrameTableBytes = 256 * 1024, MaxDimension = 4096, MaxFramePixels = 4 * 1024 * 1024, MaxRgbaBytes = 16 * 1024 * 1024, MaxEncodedFrameBytes = MaxEntryBytes });
            var metadataFrame = decoded.Frame;
            var localX = result.Ordinal & 63;
            var localY = (result.Ordinal >> 6) & 63;
            yield return new FrameSample(result.Kind, result.Branch, result.Ordinal, localX, localY, result.RawId, result.Packed,
                result.LookupRule, result.Path, result.Entry.Ordinal, frame, result.Palette, decoded.EffectivePaletteSlot,
                decoded.Width, decoded.Height, metadataFrame.HotX, metadataFrame.HotY, metadataFrame.OffsetX, metadataFrame.OffsetY,
                40 * (localY - localX) + 1, 20 * (localY + localX));
        }
    }

    private static void ReportBranchCoverage(IEnumerable<Resolution> results, IEnumerable<FrameSample> samples)
    {
        foreach (var branch in results.GroupBy(result => new { result.Kind, result.Branch }).OrderBy(group => group.Key.Kind).ThenBy(group => group.Key.Branch))
        {
            var representative = samples.Any(sample => sample.Kind == branch.Key.Kind && sample.Branch == branch.Key.Branch);
            Console.WriteLine($"branch kind={branch.Key.Kind.ToString().ToLowerInvariant()} name={branch.Key.Branch} cells={branch.Count()} ids={branch.Select(result => result.RawId).Distinct().Count()} resolved_cells={branch.Count(result => result.Success)} unresolved_cells={branch.Count(result => !result.Success)} representative={representative.ToString().ToLowerInvariant()}");
        }
    }

    private static uint[] ReadTileWords(byte[] bytes)
    {
        if (bytes.Length < 4) throw new InvalidDataException("Sector light count is truncated.");
        var lightCount = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (lightCount is < 0 or > 1024) throw new InvalidDataException("Sector light count is outside the bounded range.");
        var offset = checked(4 + (lightCount * 48));
        var length = checked(TileCount * 4);
        if (offset > bytes.Length || length > bytes.Length - offset) throw new InvalidDataException("Sector tile table is truncated.");
        var result = new uint[TileCount];
        for (var index = 0; index < result.Length; index++) result[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + (index * 4), 4));
        return result;
    }

    private static byte[] ReadUniquePayload(DatArchive archive, string path)
    {
        var entry = ReadUnique(archive, path, "required MES lookup");
        if (entry.DeclaredOutputSize > MaxEntryBytes) throw new InvalidDataException("MES lookup entry exceeds the probe cap.");
        return archive.ReadEntry(entry);
    }

    private static DatEntry ReadUnique(DatArchive archive, string path, string label)
    {
        var expected = System.Text.Encoding.ASCII.GetBytes(path);
        var entries = archive.Entries.Where(entry => RawAsciiEqualsIgnoreCase(entry.RawNameBytes, expected)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException($"The {label} did not resolve exactly once by raw ASCII path.");
        return entries[0];
    }

    private static bool RawAsciiEqualsIgnoreCase(IReadOnlyList<byte> raw, byte[] expected)
    {
        if (raw.Count != expected.Length + 1 || raw[^1] != 0) return false;
        for (var index = 0; index < expected.Length; index++)
        {
            var value = raw[index];
            if (value is >= (byte)'A' and <= (byte)'Z') value = checked((byte)(value + 32));
            var wanted = expected[index];
            if (wanted is >= (byte)'A' and <= (byte)'Z') wanted = checked((byte)(wanted + 32));
            if (value != wanted) return false;
        }
        return true;
    }

    private static SortedDictionary<int, string> ParseMes(byte[] bytes)
    {
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        var result = new SortedDictionary<int, string>();
        for (var cursor = 0; cursor < text.Length;)
        {
            var first = text.IndexOf('{', cursor);
            if (first < 0) break;
            var firstEnd = text.IndexOf('}', first + 1);
            if (firstEnd < 0 || !int.TryParse(text.AsSpan(first + 1, firstEnd - first - 1), out var key)) { cursor = first + 1; continue; }
            var second = firstEnd + 1;
            while (second < text.Length && char.IsWhiteSpace(text[second])) second++;
            if (second >= text.Length || text[second] != '{') { cursor = firstEnd + 1; continue; }
            var secondEnd = text.IndexOf('}', second + 1);
            if (secondEnd < 0) throw new InvalidDataException("MES entry is unterminated.");
            if (!result.TryAdd(key, text[(second + 1)..secondEnd])) throw new InvalidDataException("MES contains a duplicate key.");
            cursor = secondEnd + 1;
        }
        if (result.Count == 0) throw new InvalidDataException("MES produced no brace-keyed entries.");
        return result;
    }

    private static DatReaderOptions Options() => new() { MaxArchiveBytes = 512u * 1024 * 1024, MaxStoredEntryBytes = MaxEntryBytes, MaxDecodedEntryBytes = MaxEntryBytes };

    private static void SelfTest()
    {
        var mes = ParseMes(System.Text.Encoding.ASCII.GetBytes("{0}{oak /b 2}\n{100}{stone 1}\n{200}{slate 1}\n{300}{brick 1}\n"));
        var tiles = TileNameTable.Parse(mes);
        if (tiles.Name(0, true, 1) != "oak" || tiles.Name(0, false, 1) != "sto" || tiles.Name(0, true, 0) != "sla" || tiles.Name(0, false, 0) != "bri") throw new InvalidOperationException("MES tile-category selection regressed.");
        var path = Resolver.TilePath(tiles, (0u << 22) | (0u << 16) | (15u << 12) | (1u << 8) | (1u << 7) | (1u << 6));
        if (path != "art\\tile\\oakbse0a.art") throw new InvalidOperationException("Tile packed-ID lookup grammar regressed.");
        var facades = FacadeNameTable.Parse(new SortedDictionary<int, string> { [9] = "zero", [25] = "one", [100] = "two", [333] = "three" });
        var facade = Resolver.FacadePath(facades, (11u << 28) | (3u << 17) | (7u << 1));
        if (facade != "art\\Facade\\three.art") throw new InvalidOperationException("Facade ordinal MES lookup regressed.");
        var facadeSelection = Resolver.Selection((11u << 28) | (24u << 1), TerrainKind.Facade);
        if (facadeSelection.Frame != 24 || facadeSelection.Palette != 0) throw new InvalidOperationException("Facade frame bits must not select an ART palette.");
        var tileSelection = Resolver.Selection((3u << 4), TerrainKind.Tile);
        if (tileSelection.Frame != 0 || tileSelection.Palette != 3) throw new InvalidOperationException("Tile packed palette selection regressed.");
        AssertPoint(0, 0, 0, 0); AssertPoint(1, 0, -40, 20); AssertPoint(0, 1, 40, 20); AssertPoint(30, 32, 80, 1240);
    }

    private static void AssertPoint(int x, int y, int expectedX, int expectedY)
    {
        if (40 * (y - x) != expectedX || 20 * (x + y) != expectedY) throw new InvalidOperationException("Placement arithmetic regressed.");
    }

    private sealed class Resolver(DatArchive resources, TileNameTable tiles, FacadeNameTable facades)
    {
        public Resolution Resolve(uint rawId, int ordinal)
        {
            var kind = (rawId >> 28) switch { 0 => TerrainKind.Tile, 11 => TerrainKind.Facade, _ => throw new InvalidDataException("Selected sector contains a non-terrain high-nibble class.") };
            var lookup = kind == TerrainKind.Tile ? TileLookup(tiles, rawId) : new Lookup(FacadePath(facades, rawId), "facade-ordinal");
            var path = lookup.Path;
            var matches = resources.Entries.Where(entry => RawAsciiEqualsIgnoreCase(entry.RawNameBytes, System.Text.Encoding.ASCII.GetBytes(path))).ToArray();
            var selection = Selection(rawId, kind);
            var packed = kind == TerrainKind.Tile ? TilePacked(rawId) : FacadePacked(rawId);
            if (matches.Length != 1) return new Resolution(rawId, ordinal, kind, lookup.Branch, packed, $"{lookup.Branch}: exact raw-ASCII archive pathname", path, selection.Frame, selection.Palette, null);
            return new Resolution(rawId, ordinal, kind, lookup.Branch, packed, $"{lookup.Branch}: exact raw-ASCII archive pathname", path, selection.Frame, selection.Palette, matches[0]);
        }

        public static FramePaletteSelection Selection(uint id, TerrainKind kind) => kind switch
        {
            TerrainKind.Tile => new(0, checked((int)((id >> 4) & 3))),
            TerrainKind.Facade => new(checked((int)((id >> 1) & 1023)), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        public static string TilePath(TileNameTable table, uint id) => TileLookup(table, id).Path;

        private static Lookup TileLookup(TileNameTable table, uint id)
        {
            var num1 = checked((int)((id >> 22) & 63));
            var num2 = checked((int)((id >> 16) & 63));
            var outdoor = ((id >> 8) & 1) != 0;
            var flip1 = ((id >> 7) & 1) != 0;
            var flip2 = ((id >> 6) & 1) != 0;
            var edge = checked((int)((id >> 12) & 15));
            if ((id & 1) != 0) edge = EdgeReverse[edge];
            var variation = checked((int)((id >> 9) & 7));
            if ((id & 1) != 0 && EdgeForward[edge] == EdgeReverse[edge]) variation += 8;
            var name1 = table.Name(num1, flip1, outdoor ? 1 : 0);
            var name2 = table.Name(num2, flip2, outdoor ? 1 : 0);
            if (variation >= 8) variation -= 8;
            if (edge == 15 || string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase)) return new($"art\\tile\\{name1}bse{EdgeCodes[edge]}{(char)('a' + variation)}.art", "tile-single-material");
            if (edge == 0) return new($"art\\tile\\{name2}bse0{(char)('a' + variation)}.art", "tile-edge-zero");
            var index1 = table.OutdoorIndex(name1);
            var index2 = table.OutdoorIndex(name2);
            if (index1 < 0) return new($"art\\tile\\{name1}bse{EdgeCodes[edge]}{(char)('a' + variation)}.art", "tile-first-not-outdoor");
            if (index2 < 0) return new($"art\\tile\\{name2}bse{EdgeCodes[15 - edge]}{(char)('a' + variation)}.art", "tile-second-not-outdoor");
            return index1 < index2
                ? new($"art\\tile\\{name1}{name2}{EdgeCodes[edge]}{(char)('a' + variation)}.art", "tile-outdoor-forward")
                : new($"art\\tile\\{name2}{name1}{EdgeCodes[15 - edge]}{(char)('a' + variation)}.art", "tile-outdoor-reversed");
        }

        public static string FacadePath(FacadeNameTable names, uint id)
        {
            var number = checked((int)((id >> 17) & 0xFF));
            if ((id & (1u << 27)) != 0) number += 256;
            return $"art\\Facade\\{names.Name(number)}.art";
        }

        private static string TilePacked(uint id) => $"num1={(id >> 22) & 63},num2={(id >> 16) & 63},edge={(id >> 12) & 15},variation={(id >> 9) & 7},outdoor={(id >> 8) & 1},flip1={(id >> 7) & 1},flip2={(id >> 6) & 1},palette={(id >> 4) & 3},low={(id & 15)}";
        private static string FacadePacked(uint id) => $"number={((id >> 17) & 0xFF) + ((id & (1u << 27)) == 0 ? 0 : 256)},tile={(id >> 11) & 63},type={(id >> 25) & 1},flip={(id >> 26) & 1},frame={(id >> 1) & 1023},walkability={id & 1},palette=0";

        public readonly record struct FramePaletteSelection(int Frame, int Palette);
        private readonly record struct Lookup(string Path, string Branch);
    }

    private sealed class TileNameTable(string[] outdoorFlip, string[] outdoorFixed, string[] indoorFlip, string[] indoorFixed)
    {
        public static TileNameTable Parse(SortedDictionary<int, string> entries) => new(ReadRange(entries, 0, 99), ReadRange(entries, 100, 199), ReadRange(entries, 200, 299), ReadRange(entries, 300, 399));
        public string Counts => $"{outdoorFlip.Length},{outdoorFixed.Length},{indoorFlip.Length},{indoorFixed.Length}";
        public string Name(int number, bool flip, int outdoor) { var values = (outdoor, flip) switch { (1, true) => outdoorFlip, (1, false) => outdoorFixed, (0, true) => indoorFlip, _ => indoorFixed }; return number < values.Length ? values[number] : throw new InvalidDataException($"Packed tile number {number} exceeds MES category outdoor={outdoor} flip={flip} length={values.Length}."); }
        public int OutdoorIndex(string name) { var index = Array.FindIndex(outdoorFlip, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)); if (index >= 0) return index; index = Array.FindIndex(outdoorFixed, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)); return index < 0 ? -1 : outdoorFlip.Length + index; }
        private static string[] ReadRange(SortedDictionary<int, string> entries, int first, int last) => entries.Where(pair => pair.Key >= first && pair.Key <= last).Select(pair => SplitName(pair.Value)).ToArray();
        private static string SplitName(string value)
        {
            var slash = value.IndexOf('/');
            if (slash >= 0) return value[..slash].Trim();
            if (value.Length < 3) throw new InvalidDataException("A tile-name MES value lacks the CE three-character fallback prefix.");
            return value[..3];
        }
    }

    private sealed class FacadeNameTable(string[] names)
    {
        public int Count => names.Length;
        public static FacadeNameTable Parse(SortedDictionary<int, string> entries) => new(entries.Values.ToArray());
        public string Name(int ordinal) => ordinal >= 0 && ordinal < names.Length
            ? names[ordinal]
            : throw new InvalidDataException($"Facade packed-ID ordinal {ordinal} exceeds the sorted facadename.mes entry count {names.Length}.");
    }

    private sealed record Resolution(uint RawId, int Ordinal, TerrainKind Kind, string Branch, string Packed, string LookupRule, string Path, int Frame, int Palette, DatEntry? Entry)
    {
        public bool Success => Entry is not null;
    }
    private sealed record FrameSample(TerrainKind Kind, string Branch, int Ordinal, int LocalX, int LocalY, uint RawId, string Packed,
        string LookupRule, string Path, int EntryOrdinal, int Frame, int RequestedPalette, int EffectivePalette, int Width, int Height,
        int HotX, int HotY, int OffsetX, int OffsetY, int AnchorX, int AnchorY);
    private enum TerrainKind { Tile, Facade }
    private sealed record Fingerprint(long Bytes, string Sha256)
    {
        public static Fingerprint Create(string path) { using var input = File.OpenRead(path); return new(input.Length, Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()); }
    }
}
