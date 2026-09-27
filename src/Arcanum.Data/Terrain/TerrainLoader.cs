using System.Buffers.Binary;
using System.Text;

namespace Arcanum.Data.Terrain;

public static class TerrainLoader
{
    private static readonly char[] EdgeCodes = "06b489237ea5dc10".ToCharArray();
    private static readonly int[] EdgeForward = [0, 1, 8, 3, 4, 5, 6, 7, 8, 3, 10, 11, 6, 7, 14, 15];
    private static readonly int[] EdgeReverse = [0, 1, 2, 9, 4, 5, 12, 13, 2, 9, 10, 11, 12, 13, 14, 15];

    public static TerrainLoadReport Inspect(DatArchive moduleArchive, TerrainSectorSelector selector, TerrainResourceSource resourceSource, TerrainLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(moduleArchive); ArgumentNullException.ThrowIfNull(selector); ArgumentNullException.ThrowIfNull(resourceSource);
        var limits = options ?? new TerrainLoaderOptions(); limits.Validate();
        var module = SelectModule(moduleArchive, selector);
        if (module.DeclaredOutputSize > limits.MaxSectorDecodedEntryBytes) throw new TerrainLimitExceededException("The selected sector exceeds the decoded byte limit.");
        var sectorBytes = moduleArchive.ReadEntry(module);
        var parsed = ParseSector(sectorBytes, limits);
        var tileNames = TileNames.Parse(ParseMes(ReadSelected(resourceSource.Archive, resourceSource.TileMesEntryPath, "tile MES", limits.MaxMesDecodedEntryBytes), limits));
        var facadeNames = FacadeNames.Parse(ParseMes(ReadSelected(resourceSource.Archive, resourceSource.FacadeMesEntryPath, "facade MES", limits.MaxMesDecodedEntryBytes), limits));
        var resourceIndex = resourceSource.Archive.Entries
            .Select(entry => (Entry: entry, Name: NormalizeEntry(entry)))
            .Where(item => item.Name is not null)
            .GroupBy(item => item.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Entry).ToArray(), StringComparer.Ordinal);
        var cache = new Dictionary<int, MetadataResult>();
        var outcomes = new TerrainCellOutcome[limits.ExactCellCount];
        for (var ordinal = 0; ordinal < parsed.Words.Length; ordinal++)
        {
            var rawId = parsed.Words[ordinal];
            var localX = ordinal & 63; var localY = (ordinal >> 6) & 63;
            outcomes[ordinal] = ResolveCell(ordinal, localX, localY, rawId, selector, resourceSource, limits, tileNames, facadeNames, resourceIndex, cache);
        }
        return new TerrainLoadReport(selector, parsed.Framing, outcomes);
    }

    public static TerrainSector Load(DatArchive moduleArchive, TerrainSectorSelector selector, TerrainResourceSource resourceSource, TerrainLoaderOptions? options = null)
    {
        var report = Inspect(moduleArchive, selector, resourceSource, options);
        if (!report.IsComplete) throw new TerrainIncompleteException(report);
        return new TerrainSector(report);
    }

    public static ArtFrame DecodeSelectedFrame(DatArchive resourceArchive, TerrainRecord record, ArtDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resourceArchive); ArgumentNullException.ThrowIfNull(record);
        var identity = record.ResourceIdentity;
        if (identity.DatOrdinal < 0 || identity.DatOrdinal >= resourceArchive.Entries.Count) throw new TerrainSelectionException(TerrainErrorCode.EntryIdentityMismatch, "The selected resource ordinal is absent.");
        var entry = resourceArchive.Entries[identity.DatOrdinal];
        if (entry.Flags != identity.Flags || entry.DeclaredOutputSize != identity.DeclaredDecodedSize || NormalizeEntry(entry) != identity.LogicalEntryName)
            throw new TerrainSelectionException(TerrainErrorCode.EntryIdentityMismatch, "The selected resource identity does not match this archive.");
        return ArtDecoder.Decode(resourceArchive.ReadEntry(entry), record.Selection, options);
    }

    private static TerrainCellOutcome ResolveCell(int ordinal, int localX, int localY, uint rawId, TerrainSectorSelector selector, TerrainResourceSource source,
        TerrainLoaderOptions limits, TileNames tiles, FacadeNames facades, IReadOnlyDictionary<string, DatEntry[]> index, Dictionary<int, MetadataResult> cache)
    {
        var type = rawId >> 28;
        if (type is not 0 and not 11) return Issue(ordinal, TerrainIssueCode.UnsupportedClass, "The packed identifier class is unsupported.");
        var kind = type == 0 ? TerrainKind.Tile : TerrainKind.Facade;
        var lookupId = kind == TerrainKind.Tile ? rawId & ~0x3FU : rawId;
        string logicalPath; string branch; ArtFrameSelection selection;
        try
        {
            if (kind == TerrainKind.Tile)
            {
                (logicalPath, branch) = TilePath(tiles, lookupId);
                selection = new ArtFrameSelection(0, 0, checked((int)((rawId >> 4) & 3)));
            }
            else
            {
                logicalPath = $"art\\Facade\\{facades.Name(FacadeNumber(rawId))}.art"; branch = "facade-ordinal";
                selection = new ArtFrameSelection(0, checked((int)((rawId >> 1) & 1023)), 0);
            }
        }
        catch (TerrainSelectionException exception)
        {
            return Issue(ordinal, kind == TerrainKind.Tile ? TerrainIssueCode.InvalidTileSelector : TerrainIssueCode.InvalidFacadeSelector, exception.Message);
        }
        var normalizedPath = NormalizePath(Encoding.ASCII.GetBytes(logicalPath));
        if (!index.TryGetValue(normalizedPath, out var candidates) || candidates.Length == 0) return Issue(ordinal, TerrainIssueCode.MissingResource, "No selected resource entry matches the logical terrain path.", normalizedPath);
        if (candidates.Length != 1) return Issue(ordinal, TerrainIssueCode.AmbiguousResource, "Multiple selected resource entries match the logical terrain path.", normalizedPath);
        var wasNormalizationRecovered = false;
        if (kind == TerrainKind.Tile && rawId != lookupId)
        {
            try
            {
                var rawPath = NormalizePath(Encoding.ASCII.GetBytes(TilePath(tiles, rawId).Path));
                wasNormalizationRecovered = rawPath != normalizedPath && !index.ContainsKey(rawPath);
            }
            catch (TerrainSelectionException)
            {
                // The normalized selector was already accepted; a raw-only selector cannot create a recovery claim.
            }
        }
        var entry = candidates[0];
        var metadataResult = GetMetadata(source.Archive, entry, limits, cache);
        if (metadataResult.Error is not null) return Issue(ordinal, TerrainIssueCode.UnsupportedArt, "The selected ART entry cannot be inspected within terrain limits.", normalizedPath);
        var metadata = metadataResult.Metadata!;
        ArtFrameMetadata frame;
        try { frame = metadata.GetFrame(selection.GroupIndex, selection.FrameIndex); }
        catch (ArgumentOutOfRangeException) { return Issue(ordinal, TerrainIssueCode.InvalidFrame, "The packed terrain selection is outside the ART frame table.", normalizedPath); }
        var effectivePalette = metadata.Header.PalettePresenceWords[selection.PaletteSlot] != 0 ? selection.PaletteSlot : 0;
        if (metadata.Header.PalettePresenceWords[effectivePalette] == 0) return Issue(ordinal, TerrainIssueCode.UnavailablePalette, "The requested ART palette and fallback palette are unavailable.", normalizedPath);
        try
        {
            var projected = TerrainGeometry.Project(localX, localY);
            var anchor = new TerrainPoint(checked(projected.X + 1), projected.Y);
            var footprint = new TerrainRect(anchor.X, anchor.Y, kind == TerrainKind.Tile ? 78 : 80, 40);
            var identity = new TerrainResourceEntryIdentity(source.SourceId, entry.Ordinal, entry.Flags, entry.DeclaredOutputSize, normalizedPath);
            var order = new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, kind == TerrainKind.Tile ? 0 : 1, localY, localX);
            var record = new TerrainRecord(ordinal, localX, localY, checked(selector.OriginX + localX), checked(selector.OriginY + localY), rawId, lookupId,
                kind, branch, normalizedPath, identity, selection, effectivePalette, frame, projected, anchor, footprint,
                kind == TerrainKind.Tile && (rawId & 0xC1) == 0xC1, wasNormalizationRecovered, order);
            return new TerrainCellOutcome(ordinal, record, null);
        }
        catch (OverflowException exception) { throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "Terrain coordinate arithmetic overflowed.", exception); }
    }

    private static MetadataResult GetMetadata(DatArchive archive, DatEntry entry, TerrainLoaderOptions limits, Dictionary<int, MetadataResult> cache)
    {
        if (cache.TryGetValue(entry.Ordinal, out var cached)) return cached;
        if (cache.Count >= limits.MaxDistinctArtMetadataEntries) throw new TerrainLimitExceededException("The distinct ART metadata entry limit is exceeded.");
        MetadataResult result;
        try
        {
            if (entry.DeclaredOutputSize > limits.MaxArtDecodedEntryBytes) throw new TerrainLimitExceededException("The selected ART entry exceeds the decoded byte limit.");
            result = new MetadataResult(ArtDecoder.Inspect(archive.ReadEntry(entry), limits.ArtOptions), null);
        }
        catch (Exception exception) when (exception is ArtException or TerrainException or DatException) { result = new MetadataResult(null, exception); }
        cache.Add(entry.Ordinal, result); return result;
    }

    private static TerrainCellOutcome Issue(int ordinal, TerrainIssueCode code, string message, string? logicalPath = null) =>
        new(ordinal, null, new TerrainIssue(code, ordinal, message, logicalPath));

    private static DatEntry SelectModule(DatArchive archive, TerrainSectorSelector selector)
    {
        var expected = Encoding.ASCII.GetBytes(selector.ModuleEntryPath + "\0");
        var matches = archive.Entries.Where(e => e.RawNameBytes.SequenceEqual(expected)).ToArray();
        if (matches.Length == 0) throw new TerrainSelectionException(TerrainErrorCode.MissingModuleEntry, "The selected module entry is missing.");
        if (matches.Length != 1) throw new TerrainSelectionException(TerrainErrorCode.AmbiguousModuleEntry, "The selected module entry is ambiguous.");
        var entry = matches[0];
        if (selector.Expectation is { } expectation && (entry.Ordinal != expectation.Ordinal || entry.Flags != expectation.Flags || entry.DeclaredOutputSize != expectation.DeclaredDecodedSize))
            throw new TerrainSelectionException(TerrainErrorCode.EntryIdentityMismatch, "The selected module entry metadata does not match its expectation.");
        return entry;
    }

    private static byte[] ReadSelected(DatArchive archive, string path, string label, int cap)
    {
        var normalized = NormalizePath(Encoding.ASCII.GetBytes(path));
        var matches = archive.Entries.Where(e => NormalizeEntry(e) == normalized).ToArray();
        if (matches.Length == 0) throw new TerrainSelectionException(TerrainErrorCode.MissingMesEntry, $"The required {label} entry is missing.");
        if (matches.Length != 1) throw new TerrainSelectionException(TerrainErrorCode.AmbiguousMesEntry, $"The required {label} entry is ambiguous.");
        if (matches[0].DeclaredOutputSize > cap) throw new TerrainLimitExceededException($"The required {label} entry exceeds the decoded byte limit.");
        return archive.ReadEntry(matches[0]);
    }

    private static ParsedSector ParseSector(byte[] bytes, TerrainLoaderOptions limits)
    {
        if (bytes.Length < 4) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "Sector light count is truncated.");
        var terminalStart = checked(bytes.Length - 4); if (terminalStart < 4) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "Sector terminal count is truncated.");
        var terminal = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(terminalStart));
        var ranges = new Dictionary<TerrainSection, TerrainByteRange>();
        var cursor = new Cursor(bytes, terminalStart);
        var lightStart = cursor.Offset; var lights = cursor.I32("light count");
        if (lights < 0 || lights > limits.MaxLights) throw new TerrainLimitExceededException("Sector light count is outside the configured limit.");
        ranges[TerrainSection.LightCount] = new TerrainByteRange(lightStart, 4); var lightsStart = cursor.Offset; var lightBytes = SectionBytes(lights, 48, "serialized lights"); cursor.Skip(lightBytes, "serialized lights"); ranges[TerrainSection.Lights] = new TerrainByteRange(lightsStart, lightBytes);
        var wordStart = cursor.Offset; var words = new uint[limits.ExactCellCount];
        for (var index = 0; index < words.Length; index++) words[index] = cursor.U32("tile words");
        ranges[TerrainSection.TileWords] = new TerrainByteRange(wordStart, checked(words.Length * 4));
        var roofStart = cursor.Offset; var roof = cursor.I32("roof discriminator"); if (roof is not 0 and not 1) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "The roof discriminator is unsupported."); if (roof == 0) cursor.Skip(SectionBytes(256, 4, "roof words"), "roof words"); ranges[TerrainSection.Roof] = new TerrainByteRange(roofStart, checked(cursor.Offset - roofStart));
        var placeholderStart = cursor.Offset; var placeholder = cursor.I32("placeholder"); if (placeholder is < 0xAA0000 or > 0xAA0004) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "The sector placeholder is unsupported."); ranges[TerrainSection.Placeholder] = new TerrainByteRange(placeholderStart, 4);
        var scriptsStart = cursor.Offset; var scripts = 0; if (placeholder != 0xAA0000) { scripts = cursor.I32("tile script count"); if (scripts < 0 || scripts > limits.MaxTileScripts) throw new TerrainLimitExceededException("Tile script count is outside the configured limit."); cursor.Skip(SectionBytes(scripts, 24, "tile scripts"), "tile scripts"); }
        ranges[TerrainSection.TileScripts] = new TerrainByteRange(scriptsStart, checked(cursor.Offset - scriptsStart));
        var sectorScriptStart = cursor.Offset; if (placeholder >= 0xAA0002) cursor.Skip(12, "sector script"); ranges[TerrainSection.SectorScript] = new TerrainByteRange(sectorScriptStart, checked(cursor.Offset - sectorScriptStart));
        var metadataStart = cursor.Offset; if (placeholder >= 0xAA0003) cursor.Skip(24, "metadata and sound"); ranges[TerrainSection.MetadataAndSound] = new TerrainByteRange(metadataStart, checked(cursor.Offset - metadataStart));
        var blockStart = cursor.Offset; if (placeholder >= 0xAA0004) cursor.Skip(512, "block mask"); ranges[TerrainSection.BlockMask] = new TerrainByteRange(blockStart, checked(cursor.Offset - blockStart));
        var objectStart = cursor.Offset; var objectLength = checked(terminalStart - objectStart); if (objectLength < 0) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "Sector sections overlap the terminal count.");
        if (terminal is < 0 or > 4096 || terminal > limits.MaxObjectTerminalCount) throw new TerrainLimitExceededException("Sector terminal object count is outside the configured limit.");
        ranges[TerrainSection.ObjectWindow] = new TerrainByteRange(objectStart, objectLength); ranges[TerrainSection.TerminalObjectCount] = new TerrainByteRange(terminalStart, 4);
        var opaque = new TerrainOpaqueWindow(ranges[TerrainSection.ObjectWindow], terminal, bytes.AsSpan(objectStart, objectLength).ToArray());
        return new ParsedSector(words, new TerrainSectorFraming(ranges, lights, roof, placeholder, scripts, opaque));
    }

    private static int SectionBytes(int count, int width, string label)
    {
        try
        {
            return checked(count * width);
        }
        catch (OverflowException exception)
        {
            throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, $"Sector {label} byte count overflowed.", exception);
        }
    }

    private static SortedDictionary<int, string> ParseMes(byte[] bytes, TerrainLoaderOptions limits)
    {
        if (bytes.Length > limits.MaxMesDecodedEntryBytes) throw new TerrainLimitExceededException("MES input exceeds the decoded byte limit.");
        if (bytes.Any(value => value > 0x7f)) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES contains non-ASCII bytes.");
        var text = Encoding.ASCII.GetString(bytes); var result = new SortedDictionary<int, string>(); var cursor = 0;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length) break;
            if (text[cursor] == '\0') { if (text[cursor..].Any(c => c != '\0' && !char.IsWhiteSpace(c))) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES has content after terminal NUL."); break; }
            if (text[cursor] == ';' || (cursor + 1 < text.Length && text[cursor] == '/' && text[cursor + 1] == '/')) { var end = text.IndexOf('\n', cursor); cursor = end < 0 ? text.Length : end + 1; continue; }
            if (text[cursor] != '{')
            {
                var nextRecord = text.IndexOf('{', cursor + 1);
                if (nextRecord < 0) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES has unsupported syntax.");
                cursor = nextRecord;
            }
            var keyEnd = text.IndexOf('}', cursor + 1); if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), out var key)) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES key is malformed.");
            cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor] != '{') throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES value is missing.");
            var valueEnd = text.IndexOf('}', cursor + 1); if (valueEnd < 0) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES value is unterminated.");
            var value = text[(cursor + 1)..valueEnd]; if (value.Length > limits.MaxMesValueBytes) throw new TerrainLimitExceededException("MES value exceeds the configured limit.");
            if (!result.TryAdd(key, value)) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES contains a duplicate key.");
            if (result.Count > limits.MaxMesEntries) throw new TerrainLimitExceededException("MES entry count exceeds the configured limit."); cursor = valueEnd + 1;
        }
        if (result.Count == 0) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES contains no supported entries."); return result;
    }

    private static (string Path, string Branch) TilePath(TileNames table, uint id)
    {
        var num1 = checked((int)((id >> 22) & 63)); var num2 = checked((int)((id >> 16) & 63)); var outdoor = ((id >> 8) & 1) != 0; var flip1 = ((id >> 7) & 1) != 0; var flip2 = ((id >> 6) & 1) != 0;
        var edge = checked((int)((id >> 12) & 15)); if ((id & 1) != 0) edge = EdgeReverse[edge]; var variation = checked((int)((id >> 9) & 7)); if ((id & 1) != 0 && EdgeForward[edge] == EdgeReverse[edge]) variation += 8;
        var name1 = table.Name(num1, flip1, outdoor); var name2 = table.Name(num2, flip2, outdoor); if (variation >= 8) variation -= 8;
        if (edge == 15 || string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase)) return ($"art\\tile\\{name1}bse{EdgeCodes[edge]}{(char)('a' + variation)}.art", "tile-single-material");
        if (edge == 0) return ($"art\\tile\\{name2}bse0{(char)('a' + variation)}.art", "tile-edge-zero");
        var first = table.OutdoorIndex(name1); var second = table.OutdoorIndex(name2);
        if (first < 0) return ($"art\\tile\\{name1}bse{EdgeCodes[edge]}{(char)('a' + variation)}.art", "tile-first-not-outdoor");
        if (second < 0) return ($"art\\tile\\{name2}bse{EdgeCodes[15 - edge]}{(char)('a' + variation)}.art", "tile-second-not-outdoor");
        return first < second ? ($"art\\tile\\{name1}{name2}{EdgeCodes[edge]}{(char)('a' + variation)}.art", "tile-outdoor-forward") : ($"art\\tile\\{name2}{name1}{EdgeCodes[15 - edge]}{(char)('a' + variation)}.art", "tile-outdoor-reversed");
    }
    private static int FacadeNumber(uint id) => checked((int)((id >> 17) & 0xFF) + (((id & (1u << 27)) != 0) ? 256 : 0));
    private static string? NormalizeEntry(DatEntry entry)
    {
        var raw = entry.RawNameBytes;
        if (raw.Count == 0 || raw[^1] != 0 || raw.Take(raw.Count - 1).Any(value => value > 0x7f)) return null;
        return NormalizePath(raw.Take(raw.Count - 1).ToArray());
    }
    private static string NormalizePath(IReadOnlyList<byte> bytes)
    {
        var result = new byte[bytes.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var value = bytes[i];
            if (value > 0x7f) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "A terrain pathname contains non-ASCII bytes.");
            if (value == (byte)'\\') value = (byte)'/';
            if (value is >= (byte)'A' and <= (byte)'Z') value += 32;
            result[i] = value;
        }
        return Encoding.ASCII.GetString(result);
    }

    private static string MesPathToken(string value)
    {
        var slash = value.IndexOf('/');
        var token = slash < 0 ? value : value[..slash];
        if (token.Length == 0 || token.Any(char.IsControl))
            throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "MES pathname token is malformed.");
        return token;
    }

    private sealed record ParsedSector(uint[] Words, TerrainSectorFraming Framing);
    private sealed record MetadataResult(ArtMetadata? Metadata, Exception? Error);
    private sealed class TileNames(string[] outdoorFlip, string[] outdoorFixed, string[] indoorFlip, string[] indoorFixed)
    {
        public static TileNames Parse(SortedDictionary<int, string> entries)
        { foreach (var key in new[] { 0, 100, 200, 300, 400 }) if (!entries.ContainsKey(key)) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, $"Tile MES requires exact key {key}."); return new(Read(entries, 0, 99), Read(entries, 100, 199), Read(entries, 200, 299), Read(entries, 300, 399)); }
        public string Name(int number, bool flip, bool outdoor) { var values = (outdoor, flip) switch { (true, true) => outdoorFlip, (true, false) => outdoorFixed, (false, true) => indoorFlip, _ => indoorFixed }; return number >= 0 && number < values.Length ? values[number] : throw new TerrainSelectionException(TerrainErrorCode.InvalidFraming, "Tile selector exceeds initialized MES range."); }
        public int OutdoorIndex(string value) { var index = Array.FindIndex(outdoorFlip, x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)); if (index >= 0) return index; index = Array.FindIndex(outdoorFixed, x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)); return index < 0 ? -1 : outdoorFlip.Length + index; }
        private static string[] Read(SortedDictionary<int, string> entries, int first, int last) => entries.Where(x => x.Key >= first && x.Key <= last).Select(x => MesPathToken(x.Value)).ToArray();
    }
    private sealed class FacadeNames(string[] values)
    {
        public static FacadeNames Parse(SortedDictionary<int, string> entries) { if (entries.Count is < 1 or > 511 || !entries.ContainsKey(0)) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, "Facade MES must have exact key 0 and 1 through 511 entries."); return new(entries.Values.Select(MesPathToken).ToArray()); }
        public string Name(int ordinal) => ordinal >= 0 && ordinal < values.Length ? values[ordinal] : throw new TerrainSelectionException(TerrainErrorCode.InvalidFraming, "Facade selector exceeds initialized MES range.");
    }
    private ref struct Cursor
    { private readonly ReadOnlySpan<byte> bytes; private readonly int end; public Cursor(ReadOnlySpan<byte> bytes, int end) { this.bytes = bytes; this.end = end; Offset = 0; } public int Offset { get; private set; } public int I32(string label) => BinaryPrimitives.ReadInt32LittleEndian(Read(4, label)); public uint U32(string label) => BinaryPrimitives.ReadUInt32LittleEndian(Read(4, label)); public void Skip(int count, string label) => _ = Read(count, label); private ReadOnlySpan<byte> Read(int count, string label) { if (count < 0 || Offset > end - count) throw new TerrainFormatException(TerrainErrorCode.InvalidFraming, $"Sector {label} is truncated."); var result = bytes.Slice(Offset, count); Offset = checked(Offset + count); return result; } }
}
