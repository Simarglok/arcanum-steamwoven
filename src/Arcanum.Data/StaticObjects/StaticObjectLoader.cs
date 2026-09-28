using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.StaticObjects;

/// <summary>Fail-closed v119 reader for the selected type-3 static-object window.</summary>
public static class StaticObjectLoader
{
    private const int ObjectVersion = 119;
    private const uint DynamicFlag = 0x00002000;
    private const int CurrentArtSlot = 0;
    private const int LocationSlot = 1;
    private const int OffsetXSlot = 2;
    private const int OffsetYSlot = 3;
    private const int ObjectFlagsSlot = 18;
    private static readonly WireKind[] Type3Fields = [.. K("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64"), .. K("i,poid,i*2,au32,au64")];

    public static StaticObjectInspection Inspect(StaticObjectSource source, StaticObjectPrototypeSource prototypes, StaticObjectLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prototypes);
        var limits = options ?? new StaticObjectLoaderOptions();
        limits.Validate();
        try
        {
            var window = TerrainLoader.ExtractObjectWindow(source.ModuleArchive, source.ModuleEntryPath, new TerrainLoaderOptions
            {
                MaxSectorDecodedEntryBytes = limits.MaxSectorBytes,
                MaxObjectTerminalCount = limits.MaxRecords
            });
            if (window.TerminalObjectCount > limits.MaxRecords)
                throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "The terminal object count exceeds the configured limit.");
            var parsed = ParseWindow(window.Bytes.ToArray(), window.TerminalObjectCount, limits);
            var loadedPrototypes = ReadPrototypes(prototypes, parsed.Where(record => record.DirectCurrentArt is null || record.DirectFlags is null), limits);
            var resolved = parsed.Select(record => ResolveFields(record, loadedPrototypes)).ToArray();
            for (var index = 0; index < Math.Min(5, resolved.Length); index++)
                resolved[index] = ResolveSelectedResource(resolved[index], source.ResourceArchive, limits);
            return new StaticObjectInspection(StaticObjectCensusStatus.Complete, window.TerminalObjectCount, resolved);
        }
        catch (TerrainException exception)
        {
            throw new StaticObjectException(StaticObjectErrorCode.InvalidFraming, "The selected sector framing is invalid.", exception);
        }
    }

    /// <summary>
    /// Performs the opt-in path-owning validation operation and verifies all consumed immutable inputs before and after reading.
    /// The returned evidence contains only sizes and hashes, never caller paths or source payloads.
    /// </summary>
    public static StaticObjectValidation ValidateOriginalData(string modulePath, string moduleEntryPath, string prototypeDirectory,
        string resourcePath, string resourceSourceId, string prototypeSourceId, StaticObjectLoaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(modulePath); ArgumentException.ThrowIfNullOrEmpty(resourcePath);
        var moduleBefore = Fingerprint(modulePath); var resourceBefore = Fingerprint(resourcePath);
        using var module = DatArchive.Open(modulePath);
        using var resource = DatArchive.Open(resourcePath);
        var inspection = Inspect(new StaticObjectSource(module, moduleEntryPath, resource, resourceSourceId),
            new StaticObjectPrototypeSource(prototypeDirectory, prototypeSourceId), options);
        var selected = inspection.Records.Where(record => record.DirectCurrentArt is null || record.DirectFlags is null)
            .Select(record => record.PrototypeAid).Where(aid => aid.HasValue).Select(aid => aid!.Value).Distinct().Order().ToArray();
        var prototypeBefore = selected.Select(aid => (Aid: aid, Path: UniquePrototypePath(prototypeDirectory, aid), Fingerprint: Fingerprint(UniquePrototypePath(prototypeDirectory, aid)))).ToArray();
        var aggregate = Aggregate(prototypeBefore.Select(item => (item.Aid, item.Fingerprint)));
        if (moduleBefore != Fingerprint(modulePath) || resourceBefore != Fingerprint(resourcePath) ||
            prototypeBefore.Any(item => item.Fingerprint != Fingerprint(item.Path)))
            throw new StaticObjectException(StaticObjectErrorCode.InvalidFraming, "A caller-selected immutable input changed during validation.");
        return new StaticObjectValidation(inspection, moduleBefore, resourceBefore, aggregate);
    }

    private static ParsedRecord[] ParseWindow(byte[] bytes, int terminalCount, StaticObjectLoaderOptions limits)
    {
        var cursor = new Cursor(bytes);
        var records = new ParsedRecord[terminalCount];
        for (var index = 0; index < terminalCount; index++)
        {
            try { records[index] = ParseRecord(ref cursor, index, limits); }
            catch (StaticObjectException exception) { throw new StaticObjectException(exception.Code, $"Object record {index} at offset {cursor.Offset} is invalid.", exception); }
            catch (OverflowException exception) { throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, $"Object record {index} at offset {cursor.Offset} overflows a bounded calculation.", exception); }
        }
        if (!cursor.AtEnd)
            throw new StaticObjectException(StaticObjectErrorCode.InvalidFraming, "The object window has trailing bytes after its terminal record count.");
        return records;
    }

    private static ParsedRecord ParseRecord(ref Cursor cursor, int sourceIndex, StaticObjectLoaderOptions limits)
    {
        if (cursor.I32() != ObjectVersion) throw new StaticObjectException(StaticObjectErrorCode.UnsupportedVersion, "The object version is unsupported.");
        var prototype = cursor.ObjectId(allowBlocked: false);
        if (prototype.Kind is < 0 or > 3) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "The prototype identifier form is invalid.");
        _ = cursor.ObjectId(allowBlocked: false);
        var type = cursor.I32();
        if (type != 3) throw new StaticObjectException(StaticObjectErrorCode.UnsupportedType, "Only safely delimited type-3 static-object records are admitted.");
        var fieldCount = cursor.I16();
        if (fieldCount < 0 || fieldCount > limits.MaxFields) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "The object field count exceeds the configured limit.");
        var set = ReadType3Masks(ref cursor, out var count);
        if (fieldCount != count) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "The object field count differs from the override-mask population.");
        int? art = null; long? location = null; int? offsetX = null; int? offsetY = null; uint? flags = null;
        for (var field = 0; field < Type3Fields.Length; field++)
        {
            if (!set[field]) continue;
            if (field == CurrentArtSlot) art = cursor.I32();
            else if (field == LocationSlot) location = cursor.OptionalI64();
            else if (field == OffsetXSlot) offsetX = cursor.I32();
            else if (field == OffsetYSlot) offsetY = cursor.I32();
            else if (field == ObjectFlagsSlot) flags = cursor.U32();
            else Skip(ref cursor, Type3Fields[field], limits);
        }
        return new ParsedRecord(sourceIndex, prototype.Aid ?? -1, type, art, flags, location, offsetX, offsetY);
    }

    private static bool[] ReadType3Masks(ref Cursor cursor, out int count)
    {
        var set = new bool[Type3Fields.Length]; count = 0;
        var common = Type3Fields.Length - 6;
        var lengths = new[] { 32, common - 32, Type3Fields.Length - common };
        var start = 0;
        foreach (var length in lengths)
        {
            var mask = cursor.U32();
            var allowed = length == 32 ? uint.MaxValue : (1u << length) - 1u;
            if ((mask & ~allowed) != 0) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An object override mask has a reserved bit.");
            for (var bit = 0; bit < length; bit++) if ((mask & (1u << bit)) != 0) { set[start + bit] = true; count++; }
            start += length;
        }
        return set;
    }

    private static Dictionary<int, Prototype> ReadPrototypes(StaticObjectPrototypeSource source, IEnumerable<ParsedRecord> records, StaticObjectLoaderOptions limits)
    {
        if (!Directory.Exists(source.DirectoryPath)) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "The explicit prototype source is unavailable.");
        var result = new Dictionary<int, Prototype>();
        foreach (var aid in records.Select(record => record.PrototypeAid).Distinct())
        {
            if (aid is < 0 or > 999999) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "An inherited value has no canonical six-digit type-A prototype identifier.");
            var candidates = Directory.EnumerateFiles(source.DirectoryPath, $"{aid:D6} - *.pro", SearchOption.TopDirectoryOnly).ToArray();
            if (candidates.Length != 1) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "A required prototype is missing or ambiguous.");
            var info = new FileInfo(candidates[0]);
            if (info.Length > limits.MaxPrototypeBytes) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "A required prototype exceeds the configured byte limit.");
            result.Add(aid, ParsePrototype(File.ReadAllBytes(candidates[0]), aid, limits));
        }
        return result;
    }

    private static Prototype ParsePrototype(byte[] bytes, int expectedAid, StaticObjectLoaderOptions limits)
    {
        var cursor = new Cursor(bytes);
        if (cursor.I32() != ObjectVersion) throw new StaticObjectException(StaticObjectErrorCode.UnsupportedVersion, "A prototype version is unsupported.");
        if (cursor.ObjectId(allowBlocked: true).Kind != -1) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "A prototype lacks its required marker.");
        var id = cursor.ObjectId(allowBlocked: false);
        if (id.Aid != expectedAid) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "A prototype identifier does not match its canonical file selector.");
        if (cursor.I32() != 3) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "A prototype type does not match its static-object reference.");
        _ = ReadType3Masks(ref cursor, out _);
        var art = cursor.I32(); uint flags = 0;
        for (var field = 1; field < Type3Fields.Length; field++)
        {
            if (field == ObjectFlagsSlot) flags = cursor.U32();
            else Skip(ref cursor, Type3Fields[field], limits);
        }
        if (!cursor.AtEnd) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "A prototype has trailing bytes.");
        return new Prototype(art, flags);
    }

    private static StaticObjectRecord ResolveFields(ParsedRecord record, IReadOnlyDictionary<int, Prototype> prototypes)
    {
        Prototype? prototype = null;
        if (record.DirectCurrentArt is null || record.DirectFlags is null)
        {
            if (!prototypes.TryGetValue(record.PrototypeAid, out prototype))
                return ToRecord(record, StaticObjectFieldStatus.UnresolvedReference, null, false, null, false, null, StaticObjectResourceStatus.OutsideResourceScope, null);
        }
        var currentArt = record.DirectCurrentArt ?? prototype!.CurrentArt;
        var flags = record.DirectFlags ?? prototype!.Flags;
        var isStatic = record.Type == 3 && (flags & DynamicFlag) == 0;
        return ToRecord(record, StaticObjectFieldStatus.Resolved, currentArt, record.DirectCurrentArt is null, flags, record.DirectFlags is null, isStatic, StaticObjectResourceStatus.NotEvaluated, null);
    }

    private static StaticObjectRecord ResolveSelectedResource(StaticObjectRecord record, DatArchive archive, StaticObjectLoaderOptions limits)
    {
        if (record.FieldStatus != StaticObjectFieldStatus.Resolved || record.CurrentArt is not int aid)
            return Copy(record, StaticObjectResourceStatus.OutsideResourceScope, null);
        try
        {
            var value = unchecked((uint)aid);
            var artType = (int)(value >> 28);
            var number = (int)((value >> 19) & 0x1ff); var frame = (int)((value >> 14) & 0x1f);
            var group = (int)((value >> 11) & 0x7); var category = (int)((value >> 6) & 0x1f); var palette = (int)((value >> 4) & 0x3);
            if (artType != 4) return Copy(record, StaticObjectResourceStatus.InvalidArtSelector, null);
            var mes = Unique(archive, "art/scenery/scenery.mes");
            if (mes.DeclaredOutputSize > limits.MaxResourceBytes) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "The scenery mapping exceeds its configured limit.");
            var map = ParseMapping(archive.ReadEntry(mes));
            if (!map.TryGetValue(checked(category * 1000 + number), out var file) || !SafeFileName(file)) return Copy(record, StaticObjectResourceStatus.UnresolvedResource, null);
            var art = Unique(archive, "art/scenery/" + file);
            if (art.DeclaredOutputSize > limits.MaxResourceBytes) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "A selected ART entry exceeds its configured limit.");
            var metadata = ArtDecoder.Inspect(archive.ReadEntry(art), limits.ArtOptions);
            if (group >= metadata.Header.PhysicalGroupCount || frame >= metadata.Header.FramesPerGroup || metadata.Header.PalettePresenceWords[palette] == 0)
                return Copy(record, StaticObjectResourceStatus.InvalidArtSelector, null);
            var selected = metadata.GetFrame(group, frame);
            return Copy(record, StaticObjectResourceStatus.Resolved, new StaticObjectArtMetadata(artType, category, number, group, frame, palette, palette,
                selected.Width, selected.Height, selected.HotX, selected.HotY, selected.OffsetX, selected.OffsetY));
        }
        catch (StaticObjectException) { throw; }
        catch (DatException exception) { throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "The selected resource archive cannot supply a bounded ART chain.", exception); }
        catch (ArtException exception) { throw new StaticObjectException(StaticObjectErrorCode.InvalidArtSelector, "The selected ART metadata is invalid.", exception); }
    }

    private static string UniquePrototypePath(string root, int aid)
    {
        var matches = Directory.EnumerateFiles(root, $"{aid:D6} - *.pro", SearchOption.TopDirectoryOnly).ToArray();
        if (matches.Length != 1) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedReference, "A required prototype is missing or ambiguous.");
        return matches[0];
    }

    private static StaticObjectInputFingerprint Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return new StaticObjectInputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string Aggregate(IEnumerable<(int Aid, StaticObjectInputFingerprint Fingerprint)> inputs)
    {
        var bytes = Encoding.ASCII.GetBytes(string.Join("|", inputs.OrderBy(input => input.Aid).Select(input => $"{input.Aid:D6}:{input.Fingerprint.Sha256}")));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static DatEntry Unique(DatArchive archive, string path)
    {
        var normalized = Normalize(path);
        var matches = archive.Entries.Where(entry => Normalize(entry.RawNameBytes) == normalized).ToArray();
        if (matches.Length != 1) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "A selected resource is missing or ambiguous.");
        return matches[0];
    }

    private static SortedDictionary<int, string> ParseMapping(byte[] bytes)
    {
        if (bytes.Any(value => value > 0x7f)) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "The scenery mapping is not ASCII.");
        var text = Encoding.ASCII.GetString(bytes); var result = new SortedDictionary<int, string>(); var cursor = 0;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length || text[cursor] == '\0') break;
            if (text[cursor] == ';' || (cursor + 1 < text.Length && text[cursor] == '/' && text[cursor + 1] == '/'))
            {
                var lineEnd = text.IndexOf('\n', cursor);
                cursor = lineEnd < 0 ? text.Length : lineEnd + 1;
                continue;
            }
            if (text[cursor] != '{') throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "The scenery mapping has unsupported syntax.");
            var keyEnd = text.IndexOf('}', cursor + 1);
            if (keyEnd < 0 || !int.TryParse(text.AsSpan(cursor + 1, keyEnd - cursor - 1), out var key)) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "The scenery mapping key is invalid.");
            cursor = keyEnd + 1; while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            var valueEnd = cursor < text.Length && text[cursor] == '{' ? text.IndexOf('}', cursor + 1) : -1;
            if (valueEnd < 0 || !result.TryAdd(key, text[(cursor + 1)..valueEnd])) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "The scenery mapping is malformed or ambiguous.");
            cursor = valueEnd + 1;
        }
        if (result.Count == 0) throw new StaticObjectException(StaticObjectErrorCode.UnresolvedResource, "The scenery mapping has no entries.");
        return result;
    }

    private static string Normalize(string value) => Normalize(Encoding.ASCII.GetBytes(value + "\0"));
    private static string Normalize(IReadOnlyList<byte> raw)
    {
        if (raw.Count == 0 || raw[^1] != 0 || raw.Take(raw.Count - 1).Any(value => value > 0x7f)) return string.Empty;
        var bytes = new byte[raw.Count - 1];
        for (var index = 0; index < bytes.Length; index++) { var value = raw[index]; bytes[index] = value == (byte)'\\' ? (byte)'/' : value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value; }
        return Encoding.ASCII.GetString(bytes);
    }
    private static bool SafeFileName(string value) => value.Length > 4 && value.EndsWith(".art", StringComparison.OrdinalIgnoreCase) && value.All(c => c is >= ' ' and <= '~' && c is not '/' and not '\\');

    private static StaticObjectRecord ToRecord(ParsedRecord value, StaticObjectFieldStatus status, int? art, bool artInherited, uint? flags, bool flagsInherited,
        bool? isStatic, StaticObjectResourceStatus resourceStatus, StaticObjectArtMetadata? metadata) =>
        new(value.SourceIndex, value.Type, value.PrototypeAid < 0 ? null : value.PrototypeAid, value.DirectCurrentArt, value.DirectFlags, value.Location, value.DirectOffsetX, value.DirectOffsetY,
            status, art, artInherited, flags, flagsInherited, isStatic, resourceStatus, metadata);
    private static StaticObjectRecord Copy(StaticObjectRecord value, StaticObjectResourceStatus status, StaticObjectArtMetadata? metadata) =>
        new(value.SourceIndex, value.ObjectType, value.PrototypeAid, value.DirectCurrentArt, value.DirectFlags, value.Location, value.DirectOffsetX, value.DirectOffsetY,
            value.FieldStatus, value.CurrentArt, value.CurrentArtInherited, value.EffectiveFlags, value.FlagsInherited, value.IsStatic, status, metadata);

    private static void Skip(ref Cursor cursor, WireKind kind, StaticObjectLoaderOptions limits)
    {
        switch (kind)
        {
            case WireKind.I32: cursor.Skip(4); return;
            case WireKind.OptI64: _ = cursor.OptionalI64(); return;
            case WireKind.OptObjectId: if (cursor.Present()) _ = cursor.ObjectId(allowBlocked: false); return;
            case WireKind.OptString:
                if (!cursor.Present()) return;
                var length = cursor.I32(); if (length < 0 || length > limits.MaxStringBytes) throw new StaticObjectException(StaticObjectErrorCode.LimitExceeded, "A string exceeds its configured limit.");
                var text = cursor.Bytes(checked(length + 1)); if (text[^1] != 0) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "A string lacks its terminal NUL."); return;
            default:
                if (!cursor.Present()) return;
                var size = cursor.I32(); var count = cursor.I32(); _ = cursor.I32(); var expected = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An object wire kind is unknown.") };
                if (size != expected || count < 0 || count > limits.MaxArrayElements) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An array envelope is invalid.");
                cursor.Skip(checked(size * count)); var words = cursor.I32(); if (words < 2 || words > limits.MaxArrayElements) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An array bitset is invalid.");
                var bitset = cursor.Bytes(checked(words * 4)); var population = 0; for (var index = 0; index < words; index++) population += BitOperations.PopCount(BinaryPrimitives.ReadUInt32LittleEndian(bitset.Slice(index * 4, 4)));
                if (population != count) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An array bitset population differs from its element count."); return;
        }
    }

    private static WireKind[] K(string specification)
    {
        var values = new List<WireKind>();
        foreach (var token in specification.Split(','))
        {
            var parts = token.Split('*'); var count = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
            var kind = parts[0] switch { "i" => WireKind.I32, "p64" => WireKind.OptI64, "ai32" => WireKind.ArrI32, "ai64" => WireKind.ArrI64, "au32" => WireKind.ArrU32, "au64" => WireKind.ArrU64, "ascript" => WireKind.ArrScript, "aquest" => WireKind.ArrQuest, "s" => WireKind.OptString, "poid" => WireKind.OptObjectId, "aoid" => WireKind.ArrObjectId, _ => throw new InvalidOperationException("Invalid static-object wire descriptor.") };
            for (var index = 0; index < count; index++) values.Add(kind);
        }
        return values.ToArray();
    }

    private enum WireKind { I32, OptI64, ArrI32, ArrI64, ArrU32, ArrU64, ArrScript, ArrQuest, OptString, OptObjectId, ArrObjectId }
    private readonly record struct ObjectId(short Kind, int? Aid);
    private sealed record ParsedRecord(int SourceIndex, int PrototypeAid, int Type, int? DirectCurrentArt, uint? DirectFlags, long? Location, int? DirectOffsetX, int? DirectOffsetY);
    private sealed record Prototype(int CurrentArt, uint Flags);

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _bytes;
        public Cursor(ReadOnlySpan<byte> bytes) { _bytes = bytes; Offset = 0; }
        public int Offset { get; private set; }
        public bool AtEnd => Offset == _bytes.Length;
        public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Bytes(2));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public long? OptionalI64() => Present() ? BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)) : null;
        public bool Present() { var value = Bytes(1)[0]; if (value is not 0 and not 1) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An optional envelope has an invalid presence byte."); return value == 1; }
        public ObjectId ObjectId(bool allowBlocked) { var kind = I16(); Skip(6); var payload = Bytes(16); if (kind == -1 && allowBlocked) return new ObjectId(kind, null); if (kind is < 0 or > 3) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "An object identifier form is invalid."); return new ObjectId(kind, kind == 1 ? BinaryPrimitives.ReadInt32LittleEndian(payload) : null); }
        public ReadOnlySpan<byte> Bytes(int count) { if (count < 0 || count > _bytes.Length - Offset) throw new StaticObjectException(StaticObjectErrorCode.InvalidRecord, "The object record is truncated."); var result = _bytes.Slice(Offset, count); Offset = checked(Offset + count); return result; }
        public void Skip(int count) => _ = Bytes(count);
    }
}
