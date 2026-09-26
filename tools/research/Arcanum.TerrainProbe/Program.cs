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
    private const long ExpectedResourceBytes = 147_991_122;
    private const string ExpectedResourceSha256 = "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d";
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
                Console.WriteLine("self-test: PASS (fail-closed MES initialization, type-specific palettes, production-path resolver branches, separate historical candidate inventory, and placement arithmetic)");
                return 0;
            }

            if (args.Length < 4 || args[0] != "--module" || args[2] != "--resource")
            {
                throw new ArgumentException("usage: --self-test | --module PATH --resource PATH [--inventory ARCHIVE ...] [--loose-root ART_ROOT ...]");
            }

            var sources = ParseSourceArguments(args.Skip(4));
            Probe(args[1], args[3], sources.InventoryPaths, sources.LooseRoots);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"terrain-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static SourceArguments ParseSourceArguments(IEnumerable<string> arguments)
    {
        var inventoryPaths = new List<string>();
        var looseRoots = new List<string>();
        List<string>? current = null;
        var needsPath = false;
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case "--inventory":
                    if (needsPath) throw new ArgumentException("Each source option requires at least one path.");
                    current = inventoryPaths;
                    needsPath = true;
                    break;
                case "--loose-root":
                    if (needsPath) throw new ArgumentException("Each source option requires at least one path.");
                    current = looseRoots;
                    needsPath = true;
                    break;
                default:
                    if (current is null) throw new ArgumentException($"Unexpected source argument '{argument}'.");
                    current.Add(argument);
                    needsPath = false;
                    break;
            }
        }
        if (needsPath) throw new ArgumentException("Each source option requires at least one path.");
        return new SourceArguments(inventoryPaths, looseRoots);
    }

    private static void Probe(string modulePath, string resourcePath, IReadOnlyList<string> inventoryPaths, IReadOnlyList<string> looseRoots)
    {
        var moduleBefore = Fingerprint.Create(modulePath);
        if (moduleBefore.Bytes != ExpectedModuleBytes || moduleBefore.Sha256 != ExpectedModuleSha256)
        {
            throw new InvalidDataException("Module fingerprint does not match the frozen Crash Site input.");
        }

        var resourceBefore = Fingerprint.Create(resourcePath);
        if (resourceBefore.Bytes != ExpectedResourceBytes || resourceBefore.Sha256 != ExpectedResourceSha256)
        {
            throw new InvalidDataException("Resource fingerprint does not match the frozen one-source Crash Site input.");
        }
        Console.WriteLine($"input module_bytes={moduleBefore.Bytes} module_sha256={moduleBefore.Sha256} resource_bytes={resourceBefore.Bytes} resource_sha256={resourceBefore.Sha256}");
        Console.WriteLine("database_path_profile=ce-non-windows-macos ascii_lowercase=true backslash_to_slash=true");
        using var module = DatArchive.Open(modulePath, Options());
        using var resources = DatArchive.Open(resourcePath, Options());
        var sector = ReadUnique(module, SectorName, "selected sector");
        if (sector.Flags != 0x00000002 || sector.DeclaredOutputSize != 47_291)
        {
            throw new InvalidDataException("The selected sector metadata no longer matches the frozen input.");
        }

        var sectorBytes = module.ReadEntry(sector);
        var words = ReadTileWords(sectorBytes);
        var (tileNames, facadeNames) = InitializeNameTables(resources);
        Console.WriteLine($"lookup tables tile_categories={tileNames.Counts} facade_entries={facadeNames.Count}");
        var resolver = new Resolver(resources, tileNames, facadeNames);
        var results = ValidateSelections(resources, words.Select((word, ordinal) => resolver.Resolve(word, ordinal)).ToArray());

        var tileResults = results.Where(result => result.Kind == TerrainKind.Tile).ToArray();
        var facadeResults = results.Where(result => result.Kind == TerrainKind.Facade).ToArray();
        if (tileResults.Length == 0 || facadeResults.Length == 0)
        {
            throw new InvalidDataException("The selected sector did not contain both supported terrain identifier classes.");
        }

        var samples = InspectRepresentatives(resources, results, tileNames).ToArray();
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
        ReportFailureCoverage(results);
        ReportTileNormalizationEffect(resources, tileNames, tileResults);
        ReportBranchCoverage(results, samples);
        foreach (var sample in samples)
        {
            Console.WriteLine($"sample kind={sample.Kind.ToString().ToLowerInvariant()} branch={sample.Branch} local=({sample.LocalX},{sample.LocalY}) ordinal={sample.Ordinal} raw=0x{sample.RawId:X8} packed={sample.Packed} lookup_rule={sample.LookupRule} path={sample.Path} archive_entry_ordinal={sample.EntryOrdinal} art_selection=group:0,frame:{sample.Frame},requested_palette:{sample.RequestedPalette},effective_palette:{sample.EffectivePalette} dimensions={sample.Width}x{sample.Height} hot=({sample.HotX},{sample.HotY}) offset=({sample.OffsetX},{sample.OffsetY}) anchor_relative=({sample.AnchorX},{sample.AnchorY}) footprint={sample.Placement.Footprint} source={sample.Placement.Source} destination={sample.Placement.Destination} flip_x={sample.Placement.FlipX.ToString().ToLowerInvariant()} metadata_placement=ignored_by_tile_facade_draw_calls");
        }
        InventoryCandidates(results, tileNames, inventoryPaths, looseRoots);
        Console.WriteLine("placement relative(0,0)=0,0 relative(1,0)=-40,20 relative(0,1)=40,20 start(30,32)=80,1240");
        Console.WriteLine("integrity module_before_after_match=true resource_before_after_match=true");
    }

    private static IEnumerable<FrameSample> InspectRepresentatives(DatArchive resources, IEnumerable<Resolution> source, TileNameTable tileNames)
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
        selected.AddRange(candidates.Where(result => result.Kind == TerrainKind.Tile &&
                Resolver.TilePathWithoutNameNormalization(tileNames, result.RawId) != result.Path &&
                selected.All(chosen => chosen.RawId != result.RawId))
            .Take(1));

        foreach (var result in selected.OrderBy(result => result.Kind).ThenBy(result => result.Branch).ThenBy(result => result.RawId))
        {
            if (result.Entry is null || result.Entry.DeclaredOutputSize > MaxEntryBytes)
            {
                throw new InvalidDataException("A representative ART entry violates the bounded inspection policy.");
            }

            var art = resources.ReadEntry(result.Entry);
            var metadata = ArtDecoder.Inspect(art, ArtOptions());
            var frame = result.Frame;
            if (frame < 0 || frame >= metadata.Header.FramesPerGroup)
            {
                throw new InvalidDataException("The packed terrain identifier selects a frame outside the bounded ART metadata.");
            }
            var decoded = ArtDecoder.Decode(art, new ArtFrameSelection(0, frame, result.Palette), ArtOptions());
            var metadataFrame = decoded.Frame;
            var localX = result.Ordinal & 63;
            var localY = (result.Ordinal >> 6) & 63;
            var placement = PlacementSpec.For(result.Kind, localX, localY, result.RawId,
                new FrameGeometry(decoded.Width, decoded.Height, metadataFrame.HotX, metadataFrame.HotY, metadataFrame.OffsetX, metadataFrame.OffsetY),
                PlacementSpec.UnboundedViewport);
            yield return new FrameSample(result.Kind, result.Branch, result.Ordinal, localX, localY, result.RawId, result.Packed,
                result.LookupRule, result.Path, result.Entry.Ordinal, frame, result.Palette, decoded.EffectivePaletteSlot,
                decoded.Width, decoded.Height, metadataFrame.HotX, metadataFrame.HotY, metadataFrame.OffsetX, metadataFrame.OffsetY,
                40 * (localY - localX) + 1, 20 * (localY + localX), placement);
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

    private static Resolution[] ValidateSelections(DatArchive resources, IEnumerable<Resolution> source)
    {
        var metadata = new Dictionary<int, ArtMetadata>();
        var unsupportedEntries = new HashSet<int>();
        var results = new List<Resolution>();
        foreach (var result in source)
        {
            if (!result.Success || result.Entry is null)
            {
                results.Add(result);
                continue;
            }

            if (result.Entry.DeclaredOutputSize > MaxEntryBytes || unsupportedEntries.Contains(result.Entry.Ordinal))
            {
                unsupportedEntries.Add(result.Entry.Ordinal);
                results.Add(result with { Status = ResolutionStatus.UnsupportedArt });
                continue;
            }

            try
            {
                if (!metadata.TryGetValue(result.Entry.Ordinal, out var inspected))
                {
                    inspected = ArtDecoder.Inspect(resources.ReadEntry(result.Entry), ArtOptions());
                    metadata.Add(result.Entry.Ordinal, inspected);
                }
                results.Add(result.Frame >= 0 && result.Frame < inspected.Header.FramesPerGroup
                    ? result
                    : result with { Status = ResolutionStatus.UnsupportedArt });
            }
            catch (Exception) when (result.Entry is not null)
            {
                unsupportedEntries.Add(result.Entry.Ordinal);
                results.Add(result with { Status = ResolutionStatus.UnsupportedArt });
            }
        }
        return results.ToArray();
    }

    private static void ReportFailureCoverage(IEnumerable<Resolution> results)
    {
        Console.WriteLine($"coverage zero_matches={results.Count(result => result.Status == ResolutionStatus.ZeroMatches)} ambiguous_candidates={results.Count(result => result.Status == ResolutionStatus.AmbiguousCandidates)} bad_selectors={results.Count(result => result.Status == ResolutionStatus.BadSelector)} unsupported_behavior={results.Count(result => result.Status is ResolutionStatus.UnsupportedKind or ResolutionStatus.UnsupportedArt)} selected_frame_validated={results.Count(result => result.Success)}");
    }

    private static void ReportTileNormalizationEffect(DatArchive resources, TileNameTable names, IEnumerable<Resolution> tiles)
    {
        var candidates = resources.Entries
            .GroupBy(entry => NormalizeMacOsCeDatabasePath(entry.RawNameBytes, requireTerminalNul: true), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var branch in tiles.GroupBy(result => result.Branch).OrderBy(group => group.Key))
        {
            var changed = branch.Where(result => Resolver.TilePathWithoutNameNormalization(names, result.RawId) != result.Path).ToArray();
            var rawZero = changed.Where(result => candidates.GetValueOrDefault(NormalizeMacOsCeDatabasePath(System.Text.Encoding.ASCII.GetBytes(Resolver.TilePathWithoutNameNormalization(names, result.RawId)), requireTerminalNul: false), 0) == 0).ToArray();
            var restored = rawZero.Where(result => result.Success).ToArray();
            Console.WriteLine($"normalization branch={branch.Key} changed_cells={changed.Length} changed_ids={changed.Select(result => result.RawId).Distinct().Count()} raw_zero_cells={rawZero.Length} raw_zero_ids={rawZero.Select(result => result.RawId).Distinct().Count()} recovered_cells={restored.Length} recovered_ids={restored.Select(result => result.RawId).Distinct().Count()}");
        }
    }

    private static void InventoryCandidates(IEnumerable<Resolution> results, TileNameTable tileNames, IReadOnlyList<string> inventoryPaths, IReadOnlyList<string> looseRoots)
    {
        var correctedPaths = results.Where(result => !string.IsNullOrEmpty(result.Path))
            .Select(result => NormalizeMacOsCeDatabasePath(System.Text.Encoding.ASCII.GetBytes(result.Path), requireTerminalNul: false))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var historicalPaths = results.Where(result => result.Kind == TerrainKind.Tile &&
                Resolver.TilePathWithoutNameNormalization(tileNames, result.RawId) != result.Path)
            .Select(result => NormalizeMacOsCeDatabasePath(System.Text.Encoding.ASCII.GetBytes(Resolver.TilePathWithoutNameNormalization(tileNames, result.RawId)), requireTerminalNul: false))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        InventoryArchiveCandidates("corrected", correctedPaths, inventoryPaths);
        InventoryArchiveCandidates("historical_pre_normalization", historicalPaths, inventoryPaths);
        InventoryLooseCandidates("corrected", correctedPaths, looseRoots);
        InventoryLooseCandidates("historical_pre_normalization", historicalPaths, looseRoots);
    }

    private static void InventoryArchiveCandidates(string set, IReadOnlyList<string> candidatePaths, IReadOnlyList<string> inventoryPaths)
    {
        if (inventoryPaths.Count == 0)
        {
            return;
        }

        var totalCandidateEntries = 0;
        var sourcesWithCandidates = 0;
        foreach (var inventoryPath in inventoryPaths)
        {
            var before = Fingerprint.Create(inventoryPath);
            using var archive = DatArchive.Open(inventoryPath, Options());
            var candidateEntries = CountArchiveCandidateEntries(archive, candidatePaths);
            if (candidateEntries != 0) sourcesWithCandidates++;
            totalCandidateEntries += candidateEntries;
            var after = Fingerprint.Create(inventoryPath);
            if (before != after) throw new InvalidDataException("An inventory archive changed during the read-only probe.");
            Console.WriteLine($"inventory set={set} archive={Path.GetFileName(inventoryPath)} bytes={before.Bytes} sha256={before.Sha256} candidate_entries={candidateEntries}");
        }
        Console.WriteLine($"inventory set={set} candidate_paths={candidatePaths.Count} sources={inventoryPaths.Count} sources_with_candidates={sourcesWithCandidates} candidate_entries={totalCandidateEntries} precedence=not_inferred");
    }

    private static int CountArchiveCandidateEntries(DatArchive archive, IReadOnlyList<string> candidatePaths)
    {
        var normalizedEntries = archive.Entries
            .GroupBy(entry => NormalizeMacOsCeDatabasePath(entry.RawNameBytes, requireTerminalNul: true), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return candidatePaths.Sum(path => normalizedEntries.GetValueOrDefault(path, 0));
    }

    private static void InventoryLooseCandidates(string set, IReadOnlyList<string> candidatePaths, IReadOnlyList<string> looseRoots)
    {
        if (looseRoots.Count == 0)
        {
            return;
        }

        var totalCandidateEntries = 0;
        var rootsWithCandidates = 0;
        for (var rootIndex = 0; rootIndex < looseRoots.Count; rootIndex++)
        {
            var looseRoot = looseRoots[rootIndex];
            var rootExists = Directory.Exists(looseRoot);
            var candidateEntries = rootExists
                ? candidatePaths.Count(path => LooseCandidateExists(looseRoot, path))
                : 0;
            if (candidateEntries != 0) rootsWithCandidates++;
            totalCandidateEntries += candidateEntries;
            Console.WriteLine($"loose_inventory set={set} root_index={rootIndex} root_exists={rootExists.ToString().ToLowerInvariant()} candidate_entries={candidateEntries}");
        }
        Console.WriteLine($"loose_inventory set={set} candidate_paths={candidatePaths.Count} roots={looseRoots.Count} roots_with_candidates={rootsWithCandidates} candidate_entries={totalCandidateEntries} precedence=not_inferred");
    }

    private static bool LooseCandidateExists(string root, string normalizedCandidatePath)
    {
        var rootFullPath = Path.GetFullPath(root);
        var candidateFullPath = Path.GetFullPath(Path.Combine(rootFullPath, normalizedCandidatePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(rootFullPath, candidateFullPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("A generated loose-resource candidate escaped the supplied ART root.");
        }
        return File.Exists(candidateFullPath);
    }

    private static ArtDecoderOptions ArtOptions() => new()
    {
        MaxInputBytes = MaxEntryBytes,
        MaxFrameCount = 4096,
        MaxFrameTableBytes = 256 * 1024,
        MaxDimension = 4096,
        MaxFramePixels = 4 * 1024 * 1024,
        MaxRgbaBytes = 16 * 1024 * 1024,
        MaxEncodedFrameBytes = MaxEntryBytes,
    };

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

    private static (TileNameTable Tiles, FacadeNameTable Facades) InitializeNameTables(DatArchive resources)
    {
        var tiles = TileNameTable.Parse(ParseMes(ReadUniquePayload(resources, TileNamesPath)));
        var facades = FacadeNameTable.Parse(ParseMes(ReadUniquePayload(resources, FacadeNamesPath)));
        return (tiles, facades);
    }

    private static DatEntry ReadUnique(DatArchive archive, string path, string label)
    {
        var expected = NormalizeMacOsCeDatabasePath(System.Text.Encoding.ASCII.GetBytes(path), requireTerminalNul: false);
        var entries = archive.Entries.Where(entry => NormalizeMacOsCeDatabasePath(entry.RawNameBytes, requireTerminalNul: true) == expected).ToArray();
        if (entries.Length != 1) throw new InvalidDataException($"The {label} did not resolve exactly once by raw ASCII path.");
        return entries[0];
    }

    // CE compat.c performs slash conversion only on its non-Windows branch; this macOS probe models that branch.
    private static string NormalizeMacOsCeDatabasePath(IReadOnlyList<byte> raw, bool requireTerminalNul)
    {
        var length = requireTerminalNul ? raw.Count - 1 : raw.Count;
        if (length < 0 || (requireTerminalNul && (raw.Count == 0 || raw[^1] != 0)))
        {
            throw new InvalidDataException("Archive pathname lacks the required terminal NUL.");
        }
        var normalized = new byte[length];
        for (var index = 0; index < length; index++)
        {
            var value = raw[index];
            if (value == (byte)'\\') value = (byte)'/';
            if (value is >= (byte)'A' and <= (byte)'Z') value = checked((byte)(value + 32));
            normalized[index] = value;
        }
        return System.Text.Encoding.ASCII.GetString(normalized);
    }

    private static SortedDictionary<int, string> ParseMes(byte[] bytes)
    {
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        var result = new SortedDictionary<int, string>();
        for (var cursor = 0; cursor < text.Length;)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length) break;
            if (text[cursor] == '\0')
            {
                if (text[cursor..].Any(character => character != '\0' && !char.IsWhiteSpace(character))) throw new InvalidDataException("MES contains data after a terminal NUL.");
                break;
            }
            if (text[cursor] == ';' || (cursor + 1 < text.Length && text[cursor] == '/' && text[cursor + 1] == '/'))
            {
                var lineEnd = text.IndexOf('\n', cursor + 2);
                cursor = lineEnd < 0 ? text.Length : lineEnd + 1;
                continue;
            }
            if (text[cursor] != '{')
            {
                var next = text.IndexOf('{', cursor + 1);
                if (next < 0) break;
                cursor = next;
            }
            var first = cursor;
            var firstEnd = text.IndexOf('}', first + 1);
            if (firstEnd < 0) throw new InvalidDataException("MES key is unterminated.");
            if (!int.TryParse(text.AsSpan(first + 1, firstEnd - first - 1), out var key)) throw new InvalidDataException("MES key is not an integer.");
            var second = firstEnd + 1;
            while (second < text.Length && char.IsWhiteSpace(text[second])) second++;
            if (second >= text.Length || text[second] != '{') throw new InvalidDataException("MES key has no braced value.");
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
        var mes = ParseMes(System.Text.Encoding.ASCII.GetBytes("{0}{oak/b 2}\n{100}{stone 1}\n{200}{slate 1}\n{300}{brick 1}\n{400}{000000}\n"));
        var tiles = TileNameTable.Parse(mes);
        if (tiles.Name(0, true, 1) != "oak" || tiles.Name(0, false, 1) != "sto" || tiles.Name(0, true, 0) != "sla" || tiles.Name(0, false, 0) != "bri") throw new InvalidOperationException("MES tile-category selection regressed.");
        var untrimmed = TileNameTable.Parse(ParseMes(System.Text.Encoding.ASCII.GetBytes("{0}{oak /b 2}\n{100}{stone}\n{200}{slate}\n{300}{brick}\n{400}{000000}")));
        if (untrimmed.Name(0, true, 1) != "oak ") throw new InvalidOperationException("MES tile names must not be trimmed without source evidence.");
        var path = Resolver.TilePath(tiles, (0u << 22) | (0u << 16) | (15u << 12) | (1u << 8) | (1u << 7) | (1u << 6));
        if (path != "art\\tile\\oakbse0a.art") throw new InvalidOperationException("Tile packed-ID lookup grammar regressed.");
        var facades = FacadeNameTable.Parse(new SortedDictionary<int, string> { [0] = "zero", [25] = "one", [100] = "two", [333] = "three" });
        var facade = Resolver.FacadePath(facades, (11u << 28) | (3u << 17) | (7u << 1));
        if (facade != "art\\Facade\\three.art") throw new InvalidOperationException("Facade ordinal MES lookup regressed.");
        var facadeSelection = Resolver.Selection((11u << 28) | (24u << 1), TerrainKind.Facade);
        if (facadeSelection.Frame != 24 || facadeSelection.Palette != 0) throw new InvalidOperationException("Facade frame bits must not select an ART palette.");
        var tileSelection = Resolver.Selection((3u << 4), TerrainKind.Tile);
        if (tileSelection.Frame != 0 || tileSelection.Palette != 3) throw new InvalidOperationException("Tile packed palette selection regressed.");
        AssertMesInitializationFailures();
        AssertResolverProductionPaths();
        AssertHistoricalPathInventory();
        AssertSourceArgumentParsing();
        AssertPoint(0, 0, 0, 0); AssertPoint(1, 0, -40, 20); AssertPoint(0, 1, 40, 20); AssertPoint(30, 32, 80, 1240);
        AssertPlacement();
    }

    private static void AssertHistoricalPathInventory()
    {
        var tiles = TileNameTable.Parse(ParseMes(Ascii("{0}{aaa/x}{1}{bbb/x}{100}{ccc/x}{101}{ddd/x}{200}{aaa/x}{201}{zzz/x}{300}{mmm/x}{301}{nnn/x}{400}{000000}")));
        var rawId = TileId(0, 1, 3, true, true, true) | 0x31u;
        var correctedPath = NormalizeMacOsCeDatabasePath(Ascii(Resolver.TilePath(tiles, rawId)), requireTerminalNul: false);
        var historicalPath = NormalizeMacOsCeDatabasePath(Ascii(Resolver.TilePathWithoutNameNormalization(tiles, rawId)), requireTerminalNul: false);
        if (correctedPath == historicalPath) throw new InvalidOperationException("Synthetic normalization recovery did not create distinct path sets.");
        WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("ART/TILE/AAABBB4A.ART"), [1])), archive =>
        {
            if (CountArchiveCandidateEntries(archive, [correctedPath]) != 1 || CountArchiveCandidateEntries(archive, [historicalPath]) != 0)
            {
                throw new InvalidOperationException("Corrected and historical candidate path inventories were not kept separate.");
            }
        });
    }

    private static void AssertSourceArgumentParsing()
    {
        var parsed = ParseSourceArguments(["--inventory", "first.dat", "second.dat", "--loose-root", "art", "modules/art"]);
        if (!parsed.InventoryPaths.SequenceEqual(["first.dat", "second.dat"]) || !parsed.LooseRoots.SequenceEqual(["art", "modules/art"]))
        {
            throw new InvalidOperationException("Source inventory arguments were not separated by kind.");
        }
        try
        {
            ParseSourceArguments(["--loose-root", "--inventory", "first.dat"]);
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Empty source option was accepted.");
    }

    private static void AssertMesInitializationFailures()
    {
        var tile = Ascii("{0}{oak}\n{100}{stone}\n{200}{slate}\n{300}{brick}\n{400}{000000}");
        var facade = Ascii("{0}{zero}");
        AssertSyntheticInitializationRejected("missing tile lookup", CreateSyntheticDat(new SyntheticDatEntry(NameBytes(FacadeNamesPath), facade)), "required MES lookup");
        AssertSyntheticInitializationRejected("duplicate tile lookup", CreateSyntheticDat(
            new(NameBytes(TileNamesPath), tile),
            new(NameBytes("ART\\TILE\\TILENAME.MES"), tile),
            new(NameBytes(FacadeNamesPath), facade)), "did not resolve exactly once");
        AssertSyntheticInitializationRejected("malformed tile MES", CreateSyntheticDat(
            new(NameBytes(TileNamesPath), Ascii("{0}{oak")),
            new(NameBytes(FacadeNamesPath), facade)), "unterminated");
        foreach (var anchor in new[] { 0, 100, 200, 300, 400 })
        {
            var shifted = string.Concat(new[] { 0, 100, 200, 300, 400 }
                .Select(key => key == anchor ? $"{{{key + 1}}}{{shifted}}" : $"{{{key}}}{{name}}"));
            AssertSyntheticInitializationRejected($"shifted/missing tile MES anchor {anchor}", CreateSyntheticDat(
                new(NameBytes(TileNamesPath), Ascii(shifted)),
                new(NameBytes(FacadeNamesPath), facade)), $"exact key {anchor}");
        }
        AssertSyntheticInitializationRejected("non-zero-first facade MES", CreateSyntheticDat(
            new(NameBytes(TileNamesPath), tile),
            new(NameBytes(FacadeNamesPath), Ascii("{9}{zero}"))), "exact key 0");
        WithSyntheticArchive(CreateSyntheticDat(
            new(NameBytes(TileNamesPath), Ascii("{0}{oak}{100}{stone}{200}{slate}{300}{brick}{400}{000000}{401}{ignored}")),
            new(NameBytes(FacadeNamesPath), facade)), archive =>
        {
            var (tiles, _) = InitializeNameTables(archive);
            if (tiles.Counts != "1,1,1,1") throw new InvalidOperationException("Tile MES range counting did not ignore a key outside the four CE categories.");
        });
        var tooManyFacades = string.Concat(Enumerable.Range(0, 512).Select(index => $"{{{index}}}{{name{index}}}"));
        AssertSyntheticInitializationRejected("over-cap facade MES", CreateSyntheticDat(
            new(NameBytes(TileNamesPath), tile),
            new(NameBytes(FacadeNamesPath), Ascii(tooManyFacades))), "fewer than the CE 512-name capacity");
    }

    private static void AssertResolverProductionPaths()
    {
        var tileMes = Ascii("{0}{aaa/x}\n{1}{bbb/x}\n{100}{ccc/x}\n{101}{ddd/x}\n{200}{aaa/x}\n{201}{zzz/x}\n{300}{mmm/x}\n{301}{nnn/x}\n{400}{000000}");
        var facadeMes = Ascii("{0}{zero}");
        var cases = new (uint Id, string Branch, string Path)[]
        {
            (TileId(0, 0, 0, true, true, true), "tile-single-material", "art\\tile\\aaabse0a.art"),
            (TileId(0, 1, 0, true, true, true), "tile-edge-zero", "art\\tile\\bbbbse0a.art"),
            (TileId(1, 0, 1, false, true, true), "tile-first-not-outdoor", "art\\tile\\zzzbse6a.art"),
            (TileId(0, 1, 1, false, true, true), "tile-second-not-outdoor", "art\\tile\\zzzbse1a.art"),
            (TileId(0, 1, 1, true, true, true), "tile-outdoor-forward", "art\\tile\\aaabbb6a.art"),
            (TileId(1, 0, 1, true, true, true), "tile-outdoor-reversed", "art\\tile\\aaabbb1a.art"),
            (TileId(0, 1, 3, true, true, true) | 0x31u, "tile-outdoor-forward", "art\\tile\\aaabbb4a.art"),
            ((11u << 28) | (7u << 1), "facade-ordinal", "art\\Facade\\zero.art"),
        };
        var entries = new List<SyntheticDatEntry>
        {
            new(NameBytes(TileNamesPath), tileMes),
            new(NameBytes(FacadeNamesPath), facadeMes),
        };
        entries.AddRange(cases.Select(item => new SyntheticDatEntry(NameBytes(item.Path == "art\\tile\\aaabbb6a.art" ? "ART/TILE/AAABBB6A.ART" : item.Path), [1])));
        WithSyntheticArchive(CreateSyntheticDat(entries.ToArray()), archive =>
        {
            var (tiles, facades) = InitializeNameTables(archive);
            var resolver = new Resolver(archive, tiles, facades);
            foreach (var item in cases)
            {
                var result = resolver.Resolve(item.Id, 0);
                if (!result.Success || result.Branch != item.Branch || result.Path != item.Path) throw new InvalidOperationException($"Resolver branch {item.Branch} did not run through the archive lookup path.");
            }
            AssertResolutionStatus(resolver.Resolve(1u << 28, 0), ResolutionStatus.UnsupportedKind, "unsupported identifier class");
            AssertResolutionStatus(resolver.Resolve(TileId(2, 0, 0, true, true, true), 0), ResolutionStatus.BadSelector, "invalid tile selector");
            AssertResolutionStatus(resolver.Resolve(11u << 28 | 1u << 17, 0), ResolutionStatus.BadSelector, "invalid facade selector");
        });
        WithSyntheticArchive(CreateSyntheticDat(
            new(NameBytes(TileNamesPath), tileMes),
            new(NameBytes(FacadeNamesPath), facadeMes),
            new(NameBytes("ART/TILE/AAABSE0A.ART"), [1]),
            new(NameBytes("art\\tile\\aaabse0a.art"), [1])), archive =>
        {
            var (tiles, facades) = InitializeNameTables(archive);
            AssertResolutionStatus(new Resolver(archive, tiles, facades).Resolve(TileId(0, 0, 0, true, true, true), 0), ResolutionStatus.AmbiguousCandidates, "normalized duplicate candidate");
        });
    }

    private static uint TileId(int num1, int num2, int edge, bool outdoor, bool flip1, bool flip2) =>
        ((uint)num1 << 22) | ((uint)num2 << 16) | ((uint)edge << 12) | (outdoor ? 1u << 8 : 0) | (flip1 ? 1u << 7 : 0) | (flip2 ? 1u << 6 : 0);

    private static void AssertSyntheticInitializationRejected(string label, SyntheticDatFixture fixture, string message)
    {
        WithSyntheticArchive(fixture, archive => AssertRejected(() => InitializeNameTables(archive), label, message));
    }

    private static void AssertRejected(Action action, string label, string message)
    {
        try { action(); }
        catch (InvalidDataException exception) when (exception.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"{label} was accepted or rejected at the wrong validation stage.");
    }

    private static void AssertResolutionStatus(Resolution result, ResolutionStatus expected, string label)
    {
        if (result.Status != expected) throw new InvalidOperationException($"{label} did not produce the expected fail-closed status.");
    }

    private static void WithSyntheticArchive(SyntheticDatFixture fixture, Action<DatArchive> action)
    {
        using var stream = new MemoryStream(fixture.Bytes, writable: false);
        using var archive = DatArchive.Open(stream, Options(), leaveOpen: true);
        action(archive);
    }

    private static SyntheticDatFixture CreateSyntheticDat(params SyntheticDatEntry[] entries)
    {
        using var archive = new MemoryStream();
        var offsets = new List<uint>(entries.Length);
        foreach (var entry in entries)
        {
            offsets.Add(checked((uint)archive.Position));
            archive.Write(entry.Payload);
        }
        using var index = new MemoryStream();
        WriteU32(index, checked((uint)entries.Length));
        for (var ordinal = 0; ordinal < entries.Length; ordinal++)
        {
            var entry = entries[ordinal];
            WriteU32(index, checked((uint)entry.RawName.Length));
            index.Write(entry.RawName);
            index.Write(new byte[4]);
            WriteU32(index, 1);
            WriteU32(index, checked((uint)entry.Payload.Length));
            WriteU32(index, checked((uint)entry.Payload.Length));
            WriteU32(index, offsets[ordinal]);
        }
        index.Position = 0;
        index.CopyTo(archive);
        archive.Write(new byte[16]);
        archive.Write("1TAD"u8);
        WriteU32(archive, checked((uint)entries.Sum(entry => entry.RawName.Length)));
        WriteU32(archive, checked((uint)(index.Length + 28)));
        return new SyntheticDatFixture(archive.ToArray());
    }

    private static byte[] Ascii(string value) => System.Text.Encoding.ASCII.GetBytes(value);
    private static byte[] NameBytes(string value) => Ascii(value + "\0");

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void AssertPoint(int x, int y, int expectedX, int expectedY)
    {
        if (40 * (y - x) != expectedX || 20 * (x + y) != expectedY) throw new InvalidOperationException("Placement arithmetic regressed.");
    }

    private static void AssertPlacement()
    {
        var nonzeroMetadata = new FrameGeometry(78, 40, -17, 23, 9, -11);
        var tile = PlacementSpec.For(TerrainKind.Tile, 0, 0, 0x000000C1, nonzeroMetadata, PlacementSpec.UnboundedViewport);
        AssertPlacementResult(tile, new ScreenRect(1, 0, 78, 40), new ScreenRect(0, 0, 78, 40), new ScreenRect(1, 0, 78, 40), true, "tile full footprint and raw-ID flip");

        var clippedTile = PlacementSpec.For(TerrainKind.Tile, 0, 0, 0, nonzeroMetadata, new ScreenRect(20, 10, 30, 20));
        AssertPlacementResult(clippedTile, new ScreenRect(1, 0, 78, 40), new ScreenRect(19, 10, 30, 20), new ScreenRect(20, 10, 30, 20), false, "tile viewport crop");

        var boundedFrame = PlacementSpec.For(TerrainKind.Tile, 0, 0, 0, new FrameGeometry(40, 30, 99, -99, 8, -8), PlacementSpec.UnboundedViewport);
        AssertPlacementResult(boundedFrame, new ScreenRect(1, 0, 78, 40), new ScreenRect(0, 0, 40, 30), new ScreenRect(1, 0, 40, 30), false, "selected frame bounds crop and metadata non-use");

        var facade = PlacementSpec.For(TerrainKind.Facade, 1, 0, 0xB4000000, new FrameGeometry(78, 40, 5, 6, 7, 8), new ScreenRect(-20, 25, 50, 20));
        AssertPlacementResult(facade, new ScreenRect(-39, 20, 80, 40), new ScreenRect(19, 5, 50, 20), new ScreenRect(-20, 25, 50, 20), false, "facade crop and flippable-bit non-use");

        var first = PlacementSpec.For(TerrainKind.Tile, 0, 0, 0, nonzeroMetadata, PlacementSpec.UnboundedViewport).Footprint;
        var nextX = PlacementSpec.For(TerrainKind.Tile, 1, 0, 0, nonzeroMetadata, PlacementSpec.UnboundedViewport).Footprint;
        var nextY = PlacementSpec.For(TerrainKind.Tile, 0, 1, 0, nonzeroMetadata, PlacementSpec.UnboundedViewport).Footprint;
        if (PlacementSpec.Intersect(first, nextX) != new ScreenRect(1, 20, 38, 20) || PlacementSpec.Intersect(first, nextY) != new ScreenRect(41, 20, 38, 20))
        {
            throw new InvalidOperationException("Adjacent terrain footprint overlap regressed.");
        }
    }

    private static void AssertPlacementResult(PlacementResult actual, ScreenRect footprint, ScreenRect source, ScreenRect destination, bool flipX, string label)
    {
        if (actual.Footprint != footprint || actual.Source != source || actual.Destination != destination || actual.FlipX != flipX)
        {
            throw new InvalidOperationException($"{label} regressed.");
        }
    }

    private static class PlacementSpec
    {
        // This is a renderer-independent transcription of the bounded tile/facade draw-call geometry.
        // It intentionally does not reuse the animation-viewer hotspot convention.
        public static readonly ScreenRect UnboundedViewport = new(-1_000_000, -1_000_000, 2_000_000, 2_000_000);

        public static PlacementResult For(TerrainKind kind, int localX, int localY, uint rawId, FrameGeometry geometry, ScreenRect viewport)
        {
            if (geometry.Width <= 0 || geometry.Height <= 0) throw new ArgumentOutOfRangeException(nameof(geometry));
            var anchorX = checked(40 * (localY - localX) + 1);
            var anchorY = checked(20 * (localY + localX));
            var footprint = new ScreenRect(anchorX, anchorY, kind == TerrainKind.Tile ? 78 : 80, 40);
            var visible = Intersect(footprint, viewport) ?? throw new InvalidOperationException("The supplied viewport excludes the terrain footprint.");
            var requestedSource = new ScreenRect(visible.X - footprint.X, visible.Y - footprint.Y, visible.Width, visible.Height);
            var source = Intersect(requestedSource, new ScreenRect(0, 0, geometry.Width, geometry.Height))
                ?? throw new InvalidOperationException("The selected ART frame does not cover the visible terrain footprint.");
            var destination = new ScreenRect(footprint.X + source.X, footprint.Y + source.Y, source.Width, source.Height);
            var flipX = kind == TerrainKind.Tile && (rawId & 0xC1) == 0xC1;
            return new PlacementResult(footprint, source, destination, flipX);
        }

        public static ScreenRect? Intersect(ScreenRect first, ScreenRect second)
        {
            var left = Math.Max(first.X, second.X);
            var top = Math.Max(first.Y, second.Y);
            var right = Math.Min(checked(first.X + first.Width), checked(second.X + second.Width));
            var bottom = Math.Min(checked(first.Y + first.Height), checked(second.Y + second.Height));
            return right > left && bottom > top ? new ScreenRect(left, top, right - left, bottom - top) : null;
        }
    }

    private sealed class Resolver(DatArchive resources, TileNameTable tiles, FacadeNameTable facades)
    {
        public Resolution Resolve(uint rawId, int ordinal)
        {
            var kind = (rawId >> 28) switch { 0 => TerrainKind.Tile, 11 => TerrainKind.Facade, _ => TerrainKind.Unsupported };
            if (kind == TerrainKind.Unsupported)
            {
                return new Resolution(rawId, ordinal, kind, "unsupported-kind", "unsupported", "unsupported high-nibble class", "", rawId, 0, 0, null, ResolutionStatus.UnsupportedKind);
            }

            var pathId = NormalizeForPath(rawId, kind);
            var packed = kind == TerrainKind.Tile ? TilePacked(rawId, pathId) : FacadePacked(rawId);
            var selection = Selection(rawId, kind);
            try
            {
                var lookup = kind == TerrainKind.Tile ? TileLookup(tiles, pathId) : new Lookup(FacadePath(facades, pathId), "facade-ordinal");
                var expected = NormalizeMacOsCeDatabasePath(System.Text.Encoding.ASCII.GetBytes(lookup.Path), requireTerminalNul: false);
                var matches = resources.Entries.Where(entry => NormalizeMacOsCeDatabasePath(entry.RawNameBytes, requireTerminalNul: true) == expected).ToArray();
                var status = matches.Length switch
                {
                    0 => ResolutionStatus.ZeroMatches,
                    1 => ResolutionStatus.Resolved,
                    _ => ResolutionStatus.AmbiguousCandidates,
                };
                return new Resolution(rawId, ordinal, kind, lookup.Branch, packed, $"{lookup.Branch}: CE database normalized ASCII pathname", lookup.Path, pathId, selection.Frame, selection.Palette, matches.Length == 1 ? matches[0] : null, status);
            }
            catch (InvalidDataException)
            {
                return new Resolution(rawId, ordinal, kind, "bad-selector", packed, "packed selector exceeds initialized MES table", "", pathId, selection.Frame, selection.Palette, null, ResolutionStatus.BadSelector);
            }
        }

        public static FramePaletteSelection Selection(uint id, TerrainKind kind) => kind switch
        {
            TerrainKind.Tile => new(0, checked((int)((id >> 4) & 3))),
            TerrainKind.Facade => new(checked((int)((id >> 1) & 1023)), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        public static string TilePath(TileNameTable table, uint id) => TileLookup(table, NormalizeForPath(id, TerrainKind.Tile)).Path;
        public static string TilePathWithoutNameNormalization(TileNameTable table, uint id) => TileLookup(table, id).Path;

        private static uint NormalizeForPath(uint id, TerrainKind kind) => kind == TerrainKind.Tile
            ? id & ~0x3Fu
            : id;

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

        private static string TilePacked(uint id, uint pathId) => $"num1={(id >> 22) & 63},num2={(id >> 16) & 63},edge={(id >> 12) & 15},variation={(id >> 9) & 7},outdoor={(id >> 8) & 1},flip1={(id >> 7) & 1},flip2={(id >> 6) & 1},palette={(id >> 4) & 3},low={(id & 15)},path_id=0x{pathId:X8}";
        private static string FacadePacked(uint id) => $"number={((id >> 17) & 0xFF) + ((id & (1u << 27)) == 0 ? 0 : 256)},tile={(id >> 11) & 63},type={(id >> 25) & 1},flip={(id >> 26) & 1},frame={(id >> 1) & 1023},walkability={id & 1},palette=0";

        public readonly record struct FramePaletteSelection(int Frame, int Palette);
        private readonly record struct Lookup(string Path, string Branch);
    }

    private sealed class TileNameTable(string[] outdoorFlip, string[] outdoorFixed, string[] indoorFlip, string[] indoorFixed)
    {
        public static TileNameTable Parse(SortedDictionary<int, string> entries)
        {
            foreach (var anchor in new[] { 0, 100, 200, 300, 400 })
            {
                if (!entries.ContainsKey(anchor)) throw new InvalidDataException($"Tile MES initialization requires exact key {anchor}.");
            }
            return new(ReadRange(entries, 0, 99), ReadRange(entries, 100, 199), ReadRange(entries, 200, 299), ReadRange(entries, 300, 399));
        }
        public string Counts => $"{outdoorFlip.Length},{outdoorFixed.Length},{indoorFlip.Length},{indoorFixed.Length}";
        public string Name(int number, bool flip, int outdoor) { var values = (outdoor, flip) switch { (1, true) => outdoorFlip, (1, false) => outdoorFixed, (0, true) => indoorFlip, _ => indoorFixed }; return number < values.Length ? values[number] : throw new InvalidDataException($"Packed tile number {number} exceeds MES category outdoor={outdoor} flip={flip} length={values.Length}."); }
        public int OutdoorIndex(string name) { var index = Array.FindIndex(outdoorFlip, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)); if (index >= 0) return index; index = Array.FindIndex(outdoorFixed, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)); return index < 0 ? -1 : outdoorFlip.Length + index; }
        private static string[] ReadRange(SortedDictionary<int, string> entries, int first, int last) => entries.Where(pair => pair.Key >= first && pair.Key <= last).Select(pair => SplitName(pair.Value)).ToArray();
        private static string SplitName(string value)
        {
            var slash = value.IndexOf('/');
            if (slash >= 0) return value[..slash];
            if (value.Length < 3) throw new InvalidDataException("A tile-name MES value lacks the CE three-character fallback prefix.");
            return value[..3];
        }
    }

    private sealed class FacadeNameTable(string[] names)
    {
        public int Count => names.Length;
        public static FacadeNameTable Parse(SortedDictionary<int, string> entries)
        {
            if (entries.Count is < 1 or >= 512) throw new InvalidDataException("Facade MES entry count must be 1 through fewer than the CE 512-name capacity.");
            if (!entries.ContainsKey(0)) throw new InvalidDataException("Facade MES initialization requires exact key 0.");
            return new(entries.Values.ToArray());
        }
        public string Name(int ordinal) => ordinal >= 0 && ordinal < names.Length
            ? names[ordinal]
            : throw new InvalidDataException($"Facade packed-ID ordinal {ordinal} exceeds the sorted facadename.mes entry count {names.Length}.");
    }

    private sealed record Resolution(uint RawId, int Ordinal, TerrainKind Kind, string Branch, string Packed, string LookupRule, string Path, uint PathId, int Frame, int Palette, DatEntry? Entry, ResolutionStatus Status)
    {
        public bool Success => Status == ResolutionStatus.Resolved && Entry is not null;
    }
    private readonly record struct ScreenRect(int X, int Y, int Width, int Height)
    {
        public override string ToString() => $"({X},{Y},{Width},{Height})";
    }
    private readonly record struct FrameGeometry(int Width, int Height, int HotX, int HotY, int OffsetX, int OffsetY);
    private readonly record struct PlacementResult(ScreenRect Footprint, ScreenRect Source, ScreenRect Destination, bool FlipX);
    private sealed record FrameSample(TerrainKind Kind, string Branch, int Ordinal, int LocalX, int LocalY, uint RawId, string Packed,
        string LookupRule, string Path, int EntryOrdinal, int Frame, int RequestedPalette, int EffectivePalette, int Width, int Height,
        int HotX, int HotY, int OffsetX, int OffsetY, int AnchorX, int AnchorY, PlacementResult Placement);
    private sealed record SyntheticDatEntry(byte[] RawName, byte[] Payload);
    private sealed record SyntheticDatFixture(byte[] Bytes);
    private sealed record SourceArguments(IReadOnlyList<string> InventoryPaths, IReadOnlyList<string> LooseRoots);
    private enum TerrainKind { Tile, Facade, Unsupported }
    private enum ResolutionStatus { Resolved, ZeroMatches, AmbiguousCandidates, BadSelector, UnsupportedKind, UnsupportedArt }
    private sealed record Fingerprint(long Bytes, string Sha256)
    {
        public static Fingerprint Create(string path) { using var input = File.OpenRead(path); return new(input.Length, Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()); }
    }
}
