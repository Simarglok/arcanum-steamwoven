using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

internal static class Program
{
    private const string SectorEntryName = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    private const int SectorTileCount = 64 * 64;
    private const int SerializedLightBytes = 48;
    private const int TerminalObjectCountBytes = sizeof(int);
    private const int MaxLights = 1_024;
    private const int MaxDecodedSectorBytes = 1_024 * 1_024;
    private const int ExpectedObjectVersion = 119;
    private const int ExpectedObjectCount = 317;
    private const int ObjectWindowStart = 16_388;
    private const int ObjectWindowEnd = 47_287;
    private const long ExpectedModuleBytes = 355_938_305;
    private const string ExpectedModuleSha256 = "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f";

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS (bounded sector, complete object delimiter, and prototype inheritance checks)");
                return 0;
            }

            if (args.Length != 2 || args[0] != "--module")
            {
                throw new ArgumentException("usage: --self-test | --module PATH");
            }

            ProbeModule(args[1]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"crash-site-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void ProbeModule(string path)
    {
        var before = InputFingerprint.Create(path);
        if (before.Bytes != ExpectedModuleBytes || before.Sha256 != ExpectedModuleSha256)
        {
            throw new InvalidDataException("Module fingerprint does not match the bounded Crash Site input.");
        }

        using var archive = DatArchive.Open(path, BoundedOptions());
        var sectorEntries = archive.Entries.Where(entry => RawNameEquals(entry, SectorEntryName)).ToArray();
        if (sectorEntries.Length != 1)
        {
            throw new InvalidDataException("The bounded sector selector did not resolve exactly one DAT entry.");
        }

        var entry = sectorEntries[0];
        if (entry.Flags != 0x00000002 || entry.DeclaredOutputSize > MaxDecodedSectorBytes)
        {
            throw new InvalidDataException("The selected sector entry violates its bounded compressed-input contract.");
        }

        var sectorBytes = archive.ReadEntry(entry);
        var sector = SectorSlice.Parse(sectorBytes, rejectNonTileArtIds: false);
        if (sector.TrailingObjectCount != ExpectedObjectCount || sector.ObjectPayloadBytes != ObjectWindowEnd - ObjectWindowStart)
        {
            throw new InvalidDataException("The selected sector no longer matches the frozen object-window boundary.");
        }

        var instances = ObjectWire.ParseWindow(sectorBytes.AsSpan(ObjectWindowStart, ObjectWindowEnd - ObjectWindowStart), ExpectedObjectCount);
        var referenced = instances.Where(instance => !instance.CurrentAidOverridden).Select(instance => instance.PrototypeId).ToArray();
        var prototypes = ObjectWire.ReadReferencedPrototypes(archive, referenced);
        var inherited = 0;
        var direct = 0;
        foreach (var instance in instances)
        {
            if (instance.CurrentAidOverridden)
            {
                direct++;
                continue;
            }

            var matches = prototypes.Where(prototype => prototype.Id.Matches(instance.PrototypeId)).ToArray();
            if (matches.Length != 1 || matches[0].Type != instance.Type)
            {
                throw new InvalidDataException("An inherited current ART ID did not resolve to one matching validated prototype.");
            }
            inherited++;
        }

        var after = InputFingerprint.Create(path);
        if (after != before)
        {
            throw new InvalidDataException("The module changed during this read-only probe.");
        }

        Console.WriteLine($"input bytes={before.Bytes} sha256={before.Sha256}");
        Console.WriteLine($"sector ordinal={entry.Ordinal} flags=0x{entry.Flags:x8} decoded_bytes={entry.DeclaredOutputSize}");
        Console.WriteLine($"sector lights={sector.LightCount} tiles={SectorTileCount} distinct_tile_art_ids={sector.DistinctTileArtIds} invalid_tile_art_ids={sector.InvalidTileArtIds} non_tile_art_ids={sector.NonTileArtIds} object_payload_bytes={sector.ObjectPayloadBytes} trailing_object_count={sector.TrailingObjectCount}");
        Console.WriteLine($"sector art_type_counts={string.Join(',', sector.ArtTypeCounts.Select(count => count.ToString()))}");
        Console.WriteLine($"objects records={instances.Count} consumed_bytes={ObjectWindowEnd - ObjectWindowStart} cursor={ObjectWindowEnd} direct_current_aid={direct} inherited_current_aid={inherited} validated_prototypes={prototypes.Count}");
        Console.WriteLine("integrity before_after_match=true");
    }

    private static DatReaderOptions BoundedOptions() => new()
    {
        MaxArchiveBytes = checked((uint)ExpectedModuleBytes),
        MaxStoredEntryBytes = MaxDecodedSectorBytes,
        MaxDecodedEntryBytes = MaxDecodedSectorBytes,
    };

    private static bool RawNameEquals(DatEntry entry, string expected)
    {
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected);
        return entry.RawNameBytes.Count == expectedBytes.Length + 1 && entry.RawNameBytes[^1] == 0 && entry.RawNameBytes.Take(expectedBytes.Length).SequenceEqual(expectedBytes);
    }

    private static bool IsPrototypeEntry(DatEntry entry)
    {
        var name = entry.RawNameBytes;
        return name.Count > 10 && name[^1] == 0 &&
            IsAsciiIgnoreCase(name[0], (byte)'p') && IsAsciiIgnoreCase(name[1], (byte)'r') && IsAsciiIgnoreCase(name[2], (byte)'o') && IsAsciiIgnoreCase(name[3], (byte)'t') && IsAsciiIgnoreCase(name[4], (byte)'o') && name[5] == (byte)'\\' &&
            IsAsciiIgnoreCase(name[^5], (byte)'.') && IsAsciiIgnoreCase(name[^4], (byte)'p') && IsAsciiIgnoreCase(name[^3], (byte)'r') && IsAsciiIgnoreCase(name[^2], (byte)'o');
    }

    private static bool IsAsciiIgnoreCase(byte actual, byte expected) => actual == expected || actual == expected - 32 || actual == expected + 32;

    private static void SelfTest()
    {
        var valid = CreateFixture(lightCount: 0, tileId: 0, trailingObjectCount: 2);
        var sector = SectorSlice.Parse(valid, rejectNonTileArtIds: true);
        if (sector.TrailingObjectCount != 2 || sector.ObjectPayloadBytes != 0)
        {
            throw new InvalidOperationException("The valid synthetic sector was not parsed as expected.");
        }
        AssertRejected(() => SectorSlice.Parse(valid[..3], true), "truncated light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, -1), true), "negative light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, MaxLights + 1), true), "light count over cap");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)) - 1)], true), "truncated tile table");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)))], true), "missing terminal object count after complete tile table");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(0, 0x10000000, 0), true), "non-tile art identifier");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(0, 0, -1), true), "negative object count");

        var prototypeId = ObjectId.A(7001);
        foreach (var type in Enumerable.Range(0, 18))
        {
            var instance = ObjectWire.CreateSyntheticInstance(type, prototypeId, currentAidOverridden: true, includeAllFields: true);
            var parsed = ObjectWire.ParseWindow(instance, 1);
            if (parsed.Count != 1 || parsed[0].Type != type || !parsed[0].CurrentAidOverridden)
            {
                throw new InvalidOperationException("A supported type or wire kind was not delimited in the synthetic suite.");
            }
        }

        var inherited = ObjectWire.CreateSyntheticInstance(3, prototypeId, currentAidOverridden: false, includeAllFields: true);
        var direct = ObjectWire.CreateSyntheticInstance(3, prototypeId, currentAidOverridden: true, includeAllFields: true);
        var chain = inherited.Concat(direct).ToArray();
        if (ObjectWire.ParseWindow(chain, 2).Count != 2)
        {
            throw new InvalidOperationException("Synthetic record chaining failed.");
        }
        var prototype = ObjectWire.CreateSyntheticPrototype(3, prototypeId);
        var parsedPrototype = ObjectWire.ParsePrototype(prototype);
        if (!parsedPrototype.Id.Matches(prototypeId) || parsedPrototype.Type != 3)
        {
            throw new InvalidOperationException("Synthetic prototype validation failed.");
        }
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(direct, 0, 118), 1), "wrong object version");
        AssertRejected(() => ObjectWire.ParseWindow(direct[..^1], 1), "one-byte record truncation");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(18, prototypeId, true, false), 1), "reserved object type");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, invalidPresence: true), 1), "invalid presence byte");
    }

    private static byte[] CreateFixture(int lightCount, uint tileId, int trailingObjectCount)
    {
        var tileStart = checked(sizeof(int) + (lightCount * SerializedLightBytes));
        var bytes = new byte[checked(tileStart + (SectorTileCount * sizeof(uint)) + sizeof(int))];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, lightCount);
        for (var index = 0; index < SectorTileCount; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tileStart + (index * sizeof(uint))), tileId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - sizeof(int)), trailingObjectCount);
        return bytes;
    }

    private static byte[] WithI32(byte[] source, int offset, int value)
    {
        var copy = source.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static void AssertRejected(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    private sealed record SectorSlice(int LightCount, int DistinctTileArtIds, int InvalidTileArtIds, int NonTileArtIds, int ObjectPayloadBytes, int TrailingObjectCount, int[] ArtTypeCounts)
    {
        public static SectorSlice Parse(ReadOnlySpan<byte> bytes, bool rejectNonTileArtIds)
        {
            if (bytes.Length < sizeof(int)) throw new InvalidDataException("Sector light count is truncated.");
            var lightCount = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            if (lightCount < 0 || lightCount > MaxLights) throw new InvalidDataException("Sector light count is outside the supported bound.");
            var tileOffset = checked(sizeof(int) + (lightCount * SerializedLightBytes));
            var tileBytes = checked(SectorTileCount * sizeof(uint));
            if (tileOffset > bytes.Length || tileBytes > bytes.Length - tileOffset) throw new InvalidDataException("Sector tile table is truncated.");
            var distinct = new HashSet<uint>(); var types = new int[16]; var invalid = 0; var nonTile = 0;
            for (var index = 0; index < SectorTileCount; index++)
            {
                var artId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(tileOffset + (index * sizeof(uint)), sizeof(uint)));
                distinct.Add(artId); types[artId >> 28]++;
                if (artId == uint.MaxValue) invalid++; else if ((artId >> 28) != 0) nonTile++;
            }
            if (rejectNonTileArtIds && nonTile != 0) throw new InvalidDataException("Sector tile table contains a non-tile ART identifier.");
            var terminalOffset = checked(tileOffset + tileBytes);
            if (bytes.Length - terminalOffset < TerminalObjectCountBytes) throw new InvalidDataException("Sector terminal object count is truncated after the tile table.");
            var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[^TerminalObjectCountBytes..]);
            if (count < 0 || count > SectorTileCount) throw new InvalidDataException("Sector trailing object count is outside the supported bound.");
            return new SectorSlice(lightCount, distinct.Count, invalid, nonTile, checked(bytes.Length - terminalOffset - TerminalObjectCountBytes), count, types);
        }
    }

    private enum WireKind { I32, OptI64, ArrI32, ArrI64, ArrU32, ArrU64, ArrScript, ArrQuest, OptString, OptObjectId, ArrObjectId }

    private readonly record struct ObjectId(short Type, byte[] Payload)
    {
        public static ObjectId A(int value) { var payload = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(payload, value); return new ObjectId(1, payload); }
        public bool Matches(ObjectId other)
        {
            if (Type != other.Type) return false;
            return Type switch { 0 => true, 1 => Payload.AsSpan(0, 4).SequenceEqual(other.Payload.AsSpan(0, 4)), 2 => Payload.AsSpan().SequenceEqual(other.Payload), 3 => Payload.AsSpan().SequenceEqual(other.Payload), _ => false };
        }
    }

    private sealed record InstanceInfo(ObjectId PrototypeId, int Type, bool CurrentAidOverridden);
    private sealed record PrototypeInfo(ObjectId Id, int Type);

    private static class ObjectWire
    {
        private static readonly WireKind[] Common = K("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64");
        private static readonly WireKind[] Wall = K("i*3,au32,au64");
        private static readonly WireKind[] Portal = K("i*6,au32,au64");
        private static readonly WireKind[] Container = K("i*4,poid,i*4,au32,au64");
        private static readonly WireKind[] Scenery = K("i,poid,i*2,au32,au64");
        private static readonly WireKind[] Projectile = K("i*3,poid,i*2,au32,au64");
        private static readonly WireKind[] Item = K("i,poid,i*19,au32,au64");
        private static readonly WireKind[] Weapon = K("i*4,ai32*3,i*18,au32,au64");
        private static readonly WireKind[] Ammo = K("i*5,au32,au64");
        private static readonly WireKind[] Armor = K("i*4,ai32*2,i*4,au32,au64");
        private static readonly WireKind[] Gold = K("i*4,au32,au64");
        private static readonly WireKind[] ShortItem = K("i*3,au32,au64");
        private static readonly WireKind[] KeyRing = K("i,au32,i*2,au32,au64");
        private static readonly WireKind[] Written = K("i*6,au32,au64");
        private static readonly WireKind[] Critter = K("i*2,ai32*3,au32,i*4,au32*2,poid,i,poid*5,i,aoid,i*2,poid,p64,i*6,au32,au64");
        private static readonly WireKind[] Pc = K("i*2,au32,au64,i*2,aquest,au32,au64,au32,au64,i,au64,au32*3,i,s,i,au32*2,i*2,au32,au64");
        private static readonly WireKind[] Npc = K("i,poid,i,poid*2,i*2,au64,i,p64*2,i*3,poid,i*2,aoid,au32*2,i*3,au32,aoid");

        public static List<InstanceInfo> ParseWindow(ReadOnlySpan<byte> bytes, int expectedCount)
        {
            var cursor = new Cursor(bytes); var records = new List<InstanceInfo>(expectedCount);
            for (var index = 0; index < expectedCount; index++) records.Add(ParseInstance(ref cursor));
            if (!cursor.AtEnd) throw new InvalidDataException("Object window has trailing bytes after the required record count.");
            return records;
        }

        public static List<PrototypeInfo> ReadReferencedPrototypes(DatArchive archive, ObjectId[] referenced)
        {
            var wanted = referenced.Aggregate(new List<ObjectId>(), static (unique, candidate) =>
            {
                if (!unique.Any(existing => existing.Matches(candidate))) unique.Add(candidate);
                return unique;
            }).ToArray();
            if (wanted.Length == 0) return [];
            var found = new List<PrototypeInfo>();
            foreach (var entry in archive.Entries.Where(IsPrototypeEntry))
            {
                if (entry.DeclaredOutputSize > MaxDecodedSectorBytes) throw new InvalidDataException("A candidate prototype exceeds the 1 MiB read cap.");
                var prototype = ParsePrototype(archive.ReadEntry(entry));
                if (wanted.Any(id => id.Matches(prototype.Id))) found.Add(prototype);
            }
            foreach (var id in wanted)
            {
                if (found.Count(prototype => prototype.Id.Matches(id)) != 1) throw new InvalidDataException("A referenced prototype is missing or duplicated in the immutable module.");
            }
            return found;
        }

        public static PrototypeInfo ParsePrototype(ReadOnlySpan<byte> bytes)
        {
            var cursor = new Cursor(bytes);
            RequireVersion(ref cursor);
            var marker = ReadObjectId(ref cursor, allowBlocked: true);
            if (marker.Type != -1) throw new InvalidDataException("Prototype record is missing its blocked marker.");
            var id = ReadObjectId(ref cursor, allowBlocked: false);
            var type = ReadType(ref cursor);
            ReadMasks(ref cursor, type, prototype: true, out _);
            foreach (var kind in FieldsFor(type)) Skip(ref cursor, kind, requireNul: true);
            if (!cursor.AtEnd) throw new InvalidDataException("Prototype entry contains trailing bytes.");
            return new PrototypeInfo(id, type);
        }

        private static InstanceInfo ParseInstance(ref Cursor cursor)
        {
            RequireVersion(ref cursor);
            var prototype = ReadObjectId(ref cursor, allowBlocked: false);
            if (prototype.Type is -1 or -2) throw new InvalidDataException("Sector instance has a prototype/runtime discriminator.");
            _ = ReadObjectId(ref cursor, allowBlocked: false);
            var type = ReadType(ref cursor);
            var fields = FieldsFor(type);
            var numFields = cursor.I16();
            if (numFields < 0) throw new InvalidDataException("Instance has a negative field count.");
            var set = ReadMasks(ref cursor, type, prototype: false, out var popcount);
            if (numFields != popcount || numFields > 94) throw new InvalidDataException("Instance field count does not match its valid override mask.");
            var currentAid = false;
            for (var field = 0; field < fields.Count; field++)
            {
                if (!set[field]) continue;
                if (field == 0) currentAid = true;
                Skip(ref cursor, fields[field], requireNul: true);
            }
            return new InstanceInfo(prototype, type, currentAid);
        }

        private static bool[] ReadMasks(ref Cursor cursor, int type, bool prototype, out int popcount)
        {
            var fields = FieldsFor(type); var words = MaskWords(type); var set = new bool[fields.Count]; popcount = 0;
            for (var word = 0; word < words; word++)
            {
                var mask = cursor.U32();
                var start = MaskStart(type, word);
                var valid = MaskLength(type, word);
                var allowed = valid == 32 ? uint.MaxValue : (1u << valid) - 1u;
                if ((mask & ~allowed) != 0) throw new InvalidDataException("Object override mask has a reserved bit set.");
                if (prototype) continue;
                for (var bit = 0; bit < valid; bit++) if ((mask & (1u << bit)) != 0) { set[start + bit] = true; popcount++; }
            }
            return set;
        }

        private static int MaskStart(int type, int word)
        {
            if (word == 0) return 0;
            if (word == 1) return 32;
            if (word == 2) return Common.Length;
            if (type is 15 or 16 && word == 3) return Common.Length + 32;
            if (UsesSecondGroup(type) && word == 3) return Common.Length + FirstTypeGroup(type).Length;
            if (type is 15 or 16 && word == 4) return Common.Length + FirstTypeGroup(type).Length;
            throw new InvalidDataException("Unexpected object mask word.");
        }
        private static int MaskLength(int type, int word)
        {
            if (word == 0) return 32;
            if (word == 1) return Common.Length - 32;
            if (word == 2) return Math.Min(32, FirstTypeGroup(type).Length);
            if (type is 15 or 16 && word == 3) return FirstTypeGroup(type).Length - 32;
            if (UsesSecondGroup(type) && word == 3) return SecondTypeGroup(type).Length;
            if (type is 15 or 16 && word == 4) return SecondTypeGroup(type).Length;
            throw new InvalidDataException("Unexpected object mask word.");
        }
        private static int MaskWords(int type) => type is >= 5 and <= 14 ? 4 : type is 15 or 16 ? 5 : 3;
        private static bool UsesSecondGroup(int type) => type is >= 5 and <= 16;
        private static IReadOnlyList<WireKind> FieldsFor(int type) => [.. Common, .. FirstTypeGroup(type), .. SecondTypeGroup(type)];
        private static WireKind[] FirstTypeGroup(int type) => type switch { 0 => Wall, 1 => Portal, 2 => Container, 3 => Scenery, 4 => Projectile, >= 5 and <= 14 => Item, 15 or 16 => Critter, 17 => ShortItem, _ => throw new InvalidDataException("Unsupported object type.") };
        private static WireKind[] SecondTypeGroup(int type) => type switch { 5 => Weapon, 6 => Ammo, 7 => Armor, 8 => Gold, 9 or 10 or 11 or 14 => ShortItem, 12 => KeyRing, 13 => Written, 15 => Pc, 16 => Npc, _ => [] };
        private static int ReadType(ref Cursor cursor) { var type = cursor.I32(); if (type is < 0 or > 17) throw new InvalidDataException("Object type is outside the supported concrete range."); return type; }
        private static void RequireVersion(ref Cursor cursor) { var version = cursor.I32(); if (version != ExpectedObjectVersion) throw new InvalidDataException($"Object version {version} is unsupported."); }

        private static ObjectId ReadObjectId(ref Cursor cursor, bool allowBlocked)
        {
            var type = cursor.I16(); cursor.Skip(6); var payload = cursor.Bytes(16).ToArray();
            if (type == -1 && allowBlocked) return new ObjectId(type, payload);
            if (type is < 0 or > 3) throw new InvalidDataException("Object ID form is unsupported.");
            return new ObjectId(type, payload);
        }

        private static void Skip(ref Cursor cursor, WireKind kind, bool requireNul)
        {
            switch (kind)
            {
                case WireKind.I32: cursor.Skip(4); return;
                case WireKind.OptI64: if (cursor.Presence()) cursor.Skip(8); return;
                case WireKind.OptString:
                    if (!cursor.Presence()) return;
                    var stringCount = cursor.I32(); if (stringCount < 0 || stringCount > cursor.Remaining - 1 || stringCount > MaxDecodedSectorBytes) throw new InvalidDataException("String count is outside the bounded record.");
                    var value = cursor.Bytes(stringCount + 1); if (requireNul && value[^1] != 0) throw new InvalidDataException("String lacks its required terminator."); return;
                case WireKind.OptObjectId: if (cursor.Presence()) _ = ReadObjectId(ref cursor, false); return;
                default:
                    if (!cursor.Presence()) return;
                    var expectedSize = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new InvalidDataException("Unknown wire kind.") };
                    var size = cursor.I32(); var arrayCount = cursor.I32(); _ = cursor.I32();
                    if (size != expectedSize || arrayCount < 0 || arrayCount > MaxDecodedSectorBytes / expectedSize) throw new InvalidDataException("Array header violates the canonical bounded form.");
                    cursor.Skip(checked(size * arrayCount));
                    var words = cursor.I32(); if (words < 2 || words > MaxDecodedSectorBytes / 4) throw new InvalidDataException("Array bitset word count is outside the supported bound.");
                    var bits = cursor.Bytes(checked(words * 4));
                    var actual = 0; for (var index = 0; index < words; index++) actual += BitOperations.PopCount(BinaryPrimitives.ReadUInt32LittleEndian(bits.Slice(index * 4, 4)));
                    if (actual != arrayCount) throw new InvalidDataException("Array bitset population does not match its element count.");
                    return;
            }
        }

        private static WireKind[] K(string specification)
        {
            var values = new List<WireKind>();
            foreach (var token in specification.Split(','))
            {
                var parts = token.Split('*'); var count = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
                var kind = parts[0] switch { "i" => WireKind.I32, "p64" => WireKind.OptI64, "ai32" => WireKind.ArrI32, "ai64" => WireKind.ArrI64, "au32" => WireKind.ArrU32, "au64" => WireKind.ArrU64, "ascript" => WireKind.ArrScript, "aquest" => WireKind.ArrQuest, "s" => WireKind.OptString, "poid" => WireKind.OptObjectId, "aoid" => WireKind.ArrObjectId, _ => throw new InvalidOperationException("Invalid compact wire descriptor.") };
                for (var index = 0; index < count; index++) values.Add(kind);
            }
            return values.ToArray();
        }

        public static byte[] CreateSyntheticInstance(int type, ObjectId prototype, bool currentAidOverridden, bool includeAllFields, bool invalidPresence = false)
        {
            var fields = FieldsFor(type); var masks = new uint[MaskWords(type)];
            for (var index = 0; index < fields.Count; index++) if (includeAllFields || (index == 0 && currentAidOverridden)) SetBit(type, masks, index);
            if (!currentAidOverridden) ClearBit(type, masks, 0);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, prototype); AddObjectId(list, ObjectId.A(17)); AddI32(list, type); AddI16(list, checked((short)masks.Sum(mask => BitOperations.PopCount(mask)))); foreach (var mask in masks) AddU32(list, mask);
            for (var index = 0; index < fields.Count; index++) if (IsSet(type, masks, index)) AddWire(list, fields[index], invalidPresence && index == 1);
            return list.ToArray();
        }

        public static byte[] CreateSyntheticPrototype(int type, ObjectId id)
        {
            var fields = FieldsFor(type);
            var masks = new uint[MaskWords(type)];
            for (var index = 0; index < fields.Count; index++) SetBit(type, masks, index);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, new ObjectId(-1, new byte[16])); AddObjectId(list, id); AddI32(list, type); foreach (var mask in masks) AddU32(list, mask); foreach (var kind in fields) AddWire(list, kind, false); return list.ToArray();
        }

        private static void AddWire(List<byte> list, WireKind kind, bool invalidPresence)
        {
            if (kind == WireKind.I32) { AddI32(list, 0); return; }
            if (kind == WireKind.OptI64) { list.Add(invalidPresence ? (byte)2 : (byte)1); list.AddRange(new byte[8]); return; }
            if (kind == WireKind.OptString) { list.Add(1); AddI32(list, 0); list.Add(0); return; }
            if (kind == WireKind.OptObjectId) { list.Add(1); AddObjectId(list, ObjectId.A(1)); return; }
            list.Add(1); var size = kind switch { WireKind.ArrI32 or WireKind.ArrU32 => 4, WireKind.ArrI64 or WireKind.ArrU64 => 8, WireKind.ArrScript => 12, WireKind.ArrQuest => 16, WireKind.ArrObjectId => 24, _ => throw new InvalidOperationException() }; AddI32(list, size); AddI32(list, 1); AddI32(list, 0); list.AddRange(new byte[size]); AddI32(list, 2); AddU32(list, 1); AddU32(list, 0);
        }

        private static void SetBit(int type, uint[] masks, int field) { var word = WordFor(type, field, out var bit); masks[word] |= 1u << bit; }
        private static void ClearBit(int type, uint[] masks, int field) { var word = WordFor(type, field, out var bit); masks[word] &= ~(1u << bit); }
        private static bool IsSet(int type, uint[] masks, int field) { var word = WordFor(type, field, out var bit); return (masks[word] & (1u << bit)) != 0; }
        private static int WordFor(int type, int field, out int bit)
        {
            for (var word = 0; word < MaskWords(type); word++)
            {
                var start = MaskStart(type, word);
                var length = MaskLength(type, word);
                if (field >= start && field < start + length)
                {
                    bit = field - start;
                    return word;
                }
            }
            throw new InvalidOperationException("Synthetic field has no valid mask position.");
        }
        private static void AddObjectId(List<byte> list, ObjectId id) { AddI16(list, id.Type); list.AddRange(new byte[6]); list.AddRange(id.Payload); }
        private static void AddI16(List<byte> list, short value) { var bytes = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(bytes, value); list.AddRange(bytes); }
        private static void AddI32(List<byte> list, int value) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); list.AddRange(bytes); }
        private static void AddU32(List<byte> list, uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); list.AddRange(bytes); }
    }

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _bytes; private int _offset;
        public Cursor(ReadOnlySpan<byte> bytes) { _bytes = bytes; _offset = 0; }
        public int Remaining => _bytes.Length - _offset; public bool AtEnd => _offset == _bytes.Length;
        public short I16() { var value = BinaryPrimitives.ReadInt16LittleEndian(Bytes(2)); return value; }
        public int I32() { var value = BinaryPrimitives.ReadInt32LittleEndian(Bytes(4)); return value; }
        public uint U32() { var value = BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4)); return value; }
        public bool Presence() { var value = Bytes(1)[0]; if (value is not 0 and not 1) throw new InvalidDataException("Optional wire value has a non-canonical presence byte."); return value == 1; }
        public ReadOnlySpan<byte> Bytes(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("Object record is truncated."); var result = _bytes.Slice(_offset, count); _offset += count; return result; }
        public void Skip(int count) => _ = Bytes(count);
    }

    private sealed record InputFingerprint(long Bytes, string Sha256)
    {
        public static InputFingerprint Create(string path) { using var stream = File.OpenRead(path); return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()); }
    }
}
