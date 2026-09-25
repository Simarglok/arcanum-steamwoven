using System.Buffers.Binary;
using System.IO.Compression;
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
        var resolved = ObjectWire.ResolveCurrentAids(instances, prototypes);
        var direct = instances.Count(instance => instance.CurrentAidOverridden);
        var inherited = resolved.Count - direct;

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

    private static void SelfTest()
    {
        var valid = CreateFixture(lightCount: 0, tileId: 0, trailingObjectCount: 2);
        var sector = SectorSlice.Parse(valid, rejectNonTileArtIds: true);
        if (sector.TrailingObjectCount != 2 || sector.ObjectPayloadBytes != 0) throw new InvalidOperationException("The valid synthetic sector was not parsed as expected.");
        AssertRejected(() => SectorSlice.Parse(valid[..3], true), "truncated light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, -1), true), "negative light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, MaxLights + 1), true), "light count over cap");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)) - 1)], true), "truncated tile table");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)))], true), "missing terminal object count after complete tile table");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(0, 0x10000000, 0), true), "non-tile art identifier");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(0, 0, -1), true), "negative object count");
        AssertProjection(0, 0, 0, 0); AssertProjection(1, 0, -40, 20); AssertProjection(0, 1, 40, 20); AssertProjection(30, 32, 80, 1240);

        var prototypeId = ObjectId.A(7001);
        foreach (var id in new[] { ObjectId.Null(), prototypeId, ObjectId.Guid(23), ObjectId.P(7, 8, 9) })
        {
            var form = ObjectWire.CreateSyntheticInstance(3, id, true, true, currentAid: 101);
            if (ObjectWire.ParseWindow(form, 1)[0].DirectCurrentAid != 101) throw new InvalidOperationException("A persisted ObjectID form did not retain the direct AID.");
        }
        foreach (var type in Enumerable.Range(0, 18))
        {
            var instance = ObjectWire.CreateSyntheticInstance(type, prototypeId, true, true, currentAid: 1000 + type);
            var parsed = ObjectWire.ParseWindow(instance, 1);
            if (parsed.Count != 1 || parsed[0].Type != type || parsed[0].DirectCurrentAid != 1000 + type) throw new InvalidOperationException("A supported type or wire kind was not delimited in the synthetic suite.");
        }

        var inherited = ObjectWire.CreateSyntheticInstance(3, prototypeId, false, true);
        var direct = ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, currentAid: 41);
        var chained = ObjectWire.ParseWindow(inherited.Concat(direct).ToArray(), 2);
        if (chained.Count != 2 || chained[0].Location is null || chained[1].DirectCurrentAid != 41) throw new InvalidOperationException("Synthetic record chaining or early-field retention failed.");
        var prototype = ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 73));
        if (!prototype.Id.Matches(prototypeId) || prototype.Type != 3 || prototype.CurrentAid != 73) throw new InvalidOperationException("Synthetic prototype validation failed.");
        if (!ObjectWire.ResolveCurrentAids(chained, [prototype]).SequenceEqual([73, 41])) throw new InvalidOperationException("Direct and inherited AID resolution did not return concrete values.");
        AssertRejected(() => ObjectWire.ResolveCurrentAids(chained, []), "missing prototype");
        AssertRejected(() => ObjectWire.ResolveCurrentAids(chained, [prototype, prototype]), "duplicate prototype");
        AssertRejected(() => ObjectWire.ResolveCurrentAids(chained, [ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(4, prototypeId, 73))]), "type-mismatched prototype");
        AssertRejected(() => ObjectWire.ParsePrototype(ObjectWire.CreateSyntheticPrototype(3, prototypeId, 73)[..^1]), "malformed prototype");

        AssertRejected(() => ObjectWire.ParseWindow(WithI32(direct, 0, 118), 1), "wrong object version");
        AssertRejected(() => ObjectWire.ParseWindow(direct[..^1], 1), "one-byte record truncation");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateUncheckedInstance(18, prototypeId), 1), "type 18");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateUncheckedInstance(19, prototypeId), 1), "type 19");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 4, -2), 1), "runtime ObjectID form");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 4, -1), 1), "prototype ObjectID marker in sector");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 4, 4), 1), "invalid ObjectID form");
        AssertRejected(() => ObjectWire.ParseWindow(WithI16(direct, 56, 0), 1), "field-count mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(WithU32(direct, 62, 0x80000000), 1), "reserved mask bit");
        AssertRejected(() => ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, prototypeId, true, true, invalidPresence: true), 1), "invalid presence byte");

        var stringFixture = ObjectWire.CreateSyntheticSingleField(15, WireKind.OptString, out var stringOffset);
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(stringFixture, stringOffset + 1, -1), 1), "negative string length");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(stringFixture, stringOffset + 1, MaxDecodedSectorBytes + 1), 1), "oversized string length");
        AssertRejected(() => ObjectWire.ParseWindow(WithByte(stringFixture, stringOffset + 5, 1), 1), "missing string NUL");
        AssertRejected(() => ObjectWire.ParseWindow(stringFixture[..^1], 1), "truncated string");

        var arrayFixture = ObjectWire.CreateSyntheticSingleField(3, WireKind.ArrU32, out var arrayOffset);
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 1, 8), 1), "array element size mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 5, -1), 1), "negative array count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 5, (MaxDecodedSectorBytes / 4) + 1), 1), "oversized array count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 17, -1), 1), "negative bitset word count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 17, (MaxDecodedSectorBytes / 4) + 1), 1), "oversized bitset word count");
        AssertRejected(() => ObjectWire.ParseWindow(WithI32(arrayFixture, arrayOffset + 17, 1), 1), "bitset count mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(WithU32(arrayFixture, arrayOffset + 21, 0), 1), "bitset popcount mismatch");
        AssertRejected(() => ObjectWire.ParseWindow(arrayFixture[..^1], 1), "truncated array or bitset");

        AssertPrototypeArchivePath(inherited, prototypeId);
    }

    private static void AssertProjection(int localX, int localY, int expectedX, int expectedY)
    {
        if (checked(40 * (localY - localX)) != expectedX || checked(20 * (localY + localX)) != expectedY) throw new InvalidOperationException("Synthetic numeric projection regression failed.");
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

    private static byte[] WithI16(byte[] source, int offset, short value)
    {
        var copy = source.ToArray();
        BinaryPrimitives.WriteInt16LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] WithU32(byte[] source, int offset, uint value)
    {
        var copy = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] WithByte(byte[] source, int offset, byte value)
    {
        var copy = source.ToArray();
        copy[offset] = value;
        return copy;
    }

    private static void AssertRejected(Action action, string label, string? expectedMessageFragment = null)
    {
        try { action(); }
        catch (InvalidDataException exception)
        {
            if (expectedMessageFragment is not null && !exception.Message.Contains(expectedMessageFragment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} rejected from the wrong validation stage.");
            }
            return;
        }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    private static void AssertPrototypeArchivePath(byte[] inheritedBytes, ObjectId prototypeId)
    {
        var validPrototype = ObjectWire.CreateSyntheticPrototype(3, prototypeId, currentAid: 73);
        var happy = CreateSyntheticDat(
            new SyntheticDatEntry(NameBytes("PrOtO\\007001 - fixture.PrO"), validPrototype, Compressed: true),
            new SyntheticDatEntry(NameBytes("proto\\007001 - fixtureNpro"), [1]),
            new SyntheticDatEntry(NameBytes("proto\\007001 - fixture.txt"), [1]),
            new SyntheticDatEntry(NameBytes("proto\\007002 - unrelated.pro"), [1]));
        WithSyntheticArchive(happy, (archive, stream) =>
        {
            var instance = ObjectWire.ParseWindow(inheritedBytes, 1);
            if (instance[0].CurrentAidOverridden) throw new InvalidOperationException("The inherited archive-path instance unexpectedly overrides its current AID.");
            var prototypes = ObjectWire.ReadReferencedPrototypes(archive, [prototypeId, prototypeId]);
            if (!ObjectWire.ResolveCurrentAids(instance, prototypes).SequenceEqual([73])) throw new InvalidOperationException("The archive-path prototype did not resolve inherited AID 73.");
            AssertPayloadReadPattern(archive, stream, 0);
        });

        AssertArchiveRejected(CreateSyntheticDat(), [prototypeId], "missing canonical prototype", [], "missing or duplicated");
        AssertArchiveRejected(CreateSyntheticDat(
            new SyntheticDatEntry(NameBytes("proto\\007001 - first.pro"), validPrototype),
            new SyntheticDatEntry(NameBytes("PROTO\\007001 - second.PRO"), validPrototype)), [prototypeId], "duplicate canonical prototype", [], "missing or duplicated");
        AssertArchiveRejected(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("proto\\007001 - truncated.pro"), validPrototype[..^1])), [prototypeId], "malformed canonical prototype", [0], "Object record is truncated");
        AssertArchiveRejected(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("proto\\007001 - wrong-id.pro"), ObjectWire.CreateSyntheticPrototype(3, ObjectId.A(7002), 73))), [prototypeId], "own-ID-mismatched canonical prototype", [0], "canonical requested A ID");

        WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("proto\\007001 - type.pro"), ObjectWire.CreateSyntheticPrototype(4, prototypeId, 73))), (archive, stream) =>
        {
            var prototypes = ObjectWire.ReadReferencedPrototypes(archive, [prototypeId]);
            AssertRejected(() => ObjectWire.ResolveCurrentAids(ObjectWire.ParseWindow(inheritedBytes, 1), prototypes), "type-mismatched archive prototype", "inherited current ART ID");
            AssertPayloadReadPattern(archive, stream, 0);
        });

        foreach (var unlocatable in new[] { ObjectId.Null(), ObjectId.Guid(23), ObjectId.P(7, 8, 9), ObjectId.A(-1), ObjectId.A(1_000_000) })
        {
            var parsed = ObjectWire.ParseWindow(ObjectWire.CreateSyntheticInstance(3, unlocatable, false, true), 1);
            AssertArchiveRejected(happy, [parsed[0].PrototypeId], "unlocatable inherited prototype", [], "locatable");
        }

        AssertCanonicalArchiveAcceptance(0);
        AssertCanonicalArchiveAcceptance(999999);
        foreach (var invalid in new (string Label, byte[] Name)[]
        {
            ("five digits", NameBytes("proto\\07001 - fixture.pro")),
            ("seven digits", NameBytes("proto\\0007001 - fixture.pro")),
            ("altered backslash", NameBytes("proto/007001 - fixture.pro")),
            ("altered space", NameBytes("proto\\007001_- fixture.pro")),
            ("altered hyphen", NameBytes("proto\\007001 + fixture.pro")),
            ("altered period", NameBytes("proto\\007001 - fixtureXpro")),
            ("Npro suffix", NameBytes("proto\\007001 - fixtureNpro")),
            ("non-pro extension", NameBytes("proto\\007001 - fixture.txt")),
            ("embedded NUL", System.Text.Encoding.ASCII.GetBytes("proto\\007001 - fi\0xture.pro\0")),
            ("empty label", NameBytes("proto\\007001 - .pro")),
        })
        {
            AssertCanonicalNameRejected(invalid.Name, invalid.Label);
            AssertArchiveRejected(CreateSyntheticDat(new SyntheticDatEntry(invalid.Name, [1])), [prototypeId], invalid.Label, [], "missing or duplicated");
        }
        AssertCanonicalNameRejected(System.Text.Encoding.ASCII.GetBytes("proto\\007001 - fixture.pro"), "missing terminal NUL");
        AssertCanonicalNameRejected(System.Text.Encoding.ASCII.GetBytes("proto\\007001 - fixture.pro\0x"), "extra byte after terminal NUL");
    }

    private static void AssertCanonicalArchiveAcceptance(int aid)
    {
        var id = ObjectId.A(aid);
        WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes($"pRoTo\\{aid:D6} - boundary.pRo"), ObjectWire.CreateSyntheticPrototype(3, id, 73), Compressed: true)), (archive, stream) =>
        {
            var prototypes = ObjectWire.ReadReferencedPrototypes(archive, [id]);
            if (prototypes.Count != 1 || !prototypes[0].Id.Matches(id)) throw new InvalidOperationException("A canonical six-digit boundary candidate was not retained.");
            AssertPayloadReadPattern(archive, stream, 0);
        });
    }

    private static void AssertCanonicalNameRejected(byte[] rawName, string label)
    {
        if (ObjectWire.TryParseCanonicalPrototypeAid(rawName, out _)) throw new InvalidOperationException($"The {label} grammar was accepted.");
    }

    private static void AssertArchiveRejected(SyntheticDatFixture fixture, ObjectId[] referenced, string label, int[] expectedReadOrdinals, string? expectedMessageFragment = null)
    {
        WithSyntheticArchive(fixture, (archive, stream) =>
        {
            AssertRejected(() => ObjectWire.ReadReferencedPrototypes(archive, referenced), label, expectedMessageFragment);
            AssertPayloadReadPattern(archive, stream, expectedReadOrdinals);
        });
    }

    private static void WithSyntheticArchive(SyntheticDatFixture fixture, Action<DatArchive, TrackingReadStream> action)
    {
        using var stream = new TrackingReadStream(fixture.Bytes);
        using var archive = DatArchive.Open(stream, SyntheticOptions(), leaveOpen: true);
        stream.ResetReadRanges();
        action(archive, stream);
    }

    private static DatReaderOptions SyntheticOptions() => new()
    {
        MaxArchiveBytes = 128 * 1024,
        MaxIndexBytes = 16 * 1024,
        MaxEntryCount = 16,
        MaxNameBytes = 256,
        MaxTotalNameBytes = 2 * 1024,
        MaxStoredEntryBytes = MaxDecodedSectorBytes,
        MaxDecodedEntryBytes = MaxDecodedSectorBytes,
    };

    private static void AssertPayloadReadPattern(DatArchive archive, TrackingReadStream stream, params int[] expectedReadOrdinals)
    {
        foreach (var entry in archive.Entries)
        {
            var actual = stream.PayloadBytesRead(entry);
            var expected = expectedReadOrdinals.Contains(entry.Ordinal) ? entry.StoredSize : 0;
            if (actual != expected) throw new InvalidOperationException("The synthetic archive path read an unexpected payload range.");
        }
    }

    private static SyntheticDatFixture CreateSyntheticDat(params SyntheticDatEntry[] entries)
    {
        using var archive = new MemoryStream();
        var stored = new List<byte[]>(entries.Length);
        var offsets = new List<uint>(entries.Length);
        foreach (var entry in entries)
        {
            var payload = entry.Compressed ? Compress(entry.Payload) : entry.Payload;
            offsets.Add(checked((uint)archive.Position));
            stored.Add(payload);
            archive.Write(payload);
        }

        using var index = new MemoryStream();
        WriteU32(index, checked((uint)entries.Length));
        for (var indexEntry = 0; indexEntry < entries.Length; indexEntry++)
        {
            var entry = entries[indexEntry];
            WriteU32(index, checked((uint)entry.RawName.Length));
            index.Write(entry.RawName);
            index.Write(new byte[4]);
            WriteU32(index, entry.Compressed ? 2u : 1u);
            WriteU32(index, checked((uint)entry.Payload.Length));
            WriteU32(index, checked((uint)stored[indexEntry].Length));
            WriteU32(index, offsets[indexEntry]);
        }
        index.Position = 0;
        index.CopyTo(archive);
        archive.Write(new byte[16]);
        archive.Write("1TAD"u8);
        WriteU32(archive, checked((uint)entries.Sum(entry => entry.RawName.Length)));
        WriteU32(archive, checked((uint)(index.Length + 28)));
        return new SyntheticDatFixture(archive.ToArray());
    }

    private static byte[] NameBytes(string value) => System.Text.Encoding.ASCII.GetBytes(value + "\0");

    private static byte[] Compress(byte[] input)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(input);
        return output.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record SyntheticDatEntry(byte[] RawName, byte[] Payload, bool Compressed = false);
    private sealed record SyntheticDatFixture(byte[] Bytes);

    private sealed class TrackingReadStream : MemoryStream
    {
        private readonly List<(long Start, int Count)> _readRanges = [];

        public TrackingReadStream(byte[] bytes) : base(bytes, writable: false) { }

        public override int Read(Span<byte> buffer)
        {
            var start = Position;
            var count = base.Read(buffer);
            if (count != 0) _readRanges.Add((start, count));
            return count;
        }

        public void ResetReadRanges() => _readRanges.Clear();

        public long PayloadBytesRead(DatEntry entry)
        {
            var entryStart = (long)entry.Offset;
            var entryEnd = checked(entryStart + entry.StoredSize);
            return _readRanges.Sum(range => Math.Max(0, Math.Min(checked(range.Start + range.Count), entryEnd) - Math.Max(range.Start, entryStart)));
        }
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
        public static ObjectId Null() => new(0, new byte[16]);
        public static ObjectId Guid(byte seed) { var payload = new byte[16]; payload[0] = seed; return new ObjectId(2, payload); }
        public static ObjectId P(long location, int temporaryId, int mapId) { var payload = new byte[16]; BinaryPrimitives.WriteInt64LittleEndian(payload, location); BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), temporaryId); BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), mapId); return new ObjectId(3, payload); }
        public bool Matches(ObjectId other)
        {
            if (Type != other.Type) return false;
            return Type switch { 0 => true, 1 => Payload.AsSpan(0, 4).SequenceEqual(other.Payload.AsSpan(0, 4)), 2 => Payload.AsSpan().SequenceEqual(other.Payload), 3 => Payload.AsSpan().SequenceEqual(other.Payload), _ => false };
        }
    }

    private sealed record InstanceInfo(ObjectId PrototypeId, int Type, int? DirectCurrentAid, long? Location)
    {
        public bool CurrentAidOverridden => DirectCurrentAid.HasValue;
    }
    private sealed record PrototypeInfo(ObjectId Id, int Type, int CurrentAid);

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
            var wanted = new HashSet<int>();
            foreach (var referencedId in referenced)
            {
                if (referencedId.Type != 1)
                {
                    throw new InvalidDataException("A referenced prototype ObjectID is not a locatable A form.");
                }

                var aid = BinaryPrimitives.ReadInt32LittleEndian(referencedId.Payload);
                if (aid is < 0 or > 999999)
                {
                    throw new InvalidDataException("A referenced prototype A ID is outside the locatable six-digit range.");
                }
                wanted.Add(aid);
            }
            if (wanted.Count == 0) return [];

            var candidates = wanted.ToDictionary(aid => aid, _ => new List<DatEntry>());
            foreach (var entry in archive.Entries)
            {
                if (TryParseCanonicalPrototypeAid(entry.RawNameBytes.ToArray(), out var aid) && candidates.TryGetValue(aid, out var matches))
                {
                    matches.Add(entry);
                }
            }

            foreach (var candidate in candidates)
            {
                if (candidate.Value.Count != 1)
                {
                    throw new InvalidDataException("A referenced prototype is missing or duplicated in the immutable module.");
                }
            }

            var found = new List<PrototypeInfo>(wanted.Count);
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Key))
            {
                var entry = candidate.Value[0];
                if (entry.DeclaredOutputSize > MaxDecodedSectorBytes) throw new InvalidDataException("A candidate prototype exceeds the 1 MiB read cap.");
                var prototype = ParsePrototype(archive.ReadEntry(entry));
                if (prototype.Id.Type != 1 || BinaryPrimitives.ReadInt32LittleEndian(prototype.Id.Payload) != candidate.Key)
                {
                    throw new InvalidDataException("A selected prototype candidate does not match its canonical requested A ID.");
                }
                found.Add(prototype);
            }
            return found;
        }

        public static List<int> ResolveCurrentAids(IEnumerable<InstanceInfo> instances, IReadOnlyList<PrototypeInfo> prototypes)
        {
            var resolved = new List<int>();
            foreach (var instance in instances)
            {
                if (instance.DirectCurrentAid is int direct)
                {
                    resolved.Add(direct);
                    continue;
                }

                var matches = prototypes.Where(prototype => prototype.Id.Matches(instance.PrototypeId)).ToArray();
                if (matches.Length != 1 || matches[0].Type != instance.Type) throw new InvalidDataException("An inherited current ART ID did not resolve to one matching validated prototype.");
                resolved.Add(matches[0].CurrentAid);
            }
            return resolved;
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
            var fields = FieldsFor(type);
            var currentAid = cursor.I32();
            for (var field = 1; field < fields.Count; field++) Skip(ref cursor, fields[field], requireNul: true);
            if (!cursor.AtEnd) throw new InvalidDataException("Prototype entry contains trailing bytes.");
            return new PrototypeInfo(id, type, currentAid);
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
            int? currentAid = null;
            long? location = null;
            for (var field = 0; field < fields.Count; field++)
            {
                if (!set[field]) continue;
                if (field == 0) currentAid = cursor.I32();
                else if (field == 1) location = cursor.Presence() ? cursor.I64() : null;
                else Skip(ref cursor, fields[field], requireNul: true);
            }
            return new InstanceInfo(prototype, type, currentAid, location);
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

        public static bool TryParseCanonicalPrototypeAid(ReadOnlySpan<byte> rawName, out int aid)
        {
            aid = 0;
            const int prefixLength = 5;
            const int digitsStart = 6;
            const int labelStart = 15;
            if (rawName.Length < 21 || rawName[^1] != 0 ||
                !AsciiLetterEquals(rawName[0], (byte)'p') || !AsciiLetterEquals(rawName[1], (byte)'r') || !AsciiLetterEquals(rawName[2], (byte)'o') || !AsciiLetterEquals(rawName[3], (byte)'t') || !AsciiLetterEquals(rawName[4], (byte)'o') ||
                rawName[prefixLength] != (byte)'\\' || rawName[12] != (byte)' ' || rawName[13] != (byte)'-' || rawName[14] != (byte)' ' ||
                rawName[^5] != (byte)'.' || !AsciiLetterEquals(rawName[^4], (byte)'p') || !AsciiLetterEquals(rawName[^3], (byte)'r') || !AsciiLetterEquals(rawName[^2], (byte)'o'))
            {
                return false;
            }

            var labelEnd = rawName.Length - 5;
            if (labelEnd <= labelStart) return false;
            var parsed = 0;
            for (var index = 0; index < 6; index++)
            {
                var digit = rawName[digitsStart + index];
                if (digit is < (byte)'0' or > (byte)'9') return false;
                parsed = checked((parsed * 10) + (digit - (byte)'0'));
            }
            for (var index = labelStart; index < labelEnd; index++)
            {
                if (rawName[index] is 0 or (byte)'/' or (byte)'\\') return false;
            }
            aid = parsed;
            return true;
        }

        private static bool AsciiLetterEquals(byte actual, byte expected)
        {
            if (actual is >= (byte)'A' and <= (byte)'Z') actual = checked((byte)(actual + ((byte)'a' - (byte)'A')));
            return actual == expected;
        }

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

        public static byte[] CreateSyntheticInstance(int type, ObjectId prototype, bool currentAidOverridden, bool includeAllFields, bool invalidPresence = false, int currentAid = 0)
        {
            var fields = FieldsFor(type); var masks = new uint[MaskWords(type)];
            for (var index = 0; index < fields.Count; index++) if (includeAllFields || (index == 0 && currentAidOverridden)) SetBit(type, masks, index);
            if (!currentAidOverridden) ClearBit(type, masks, 0);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, prototype); AddObjectId(list, ObjectId.A(17)); AddI32(list, type); AddI16(list, checked((short)masks.Sum(mask => BitOperations.PopCount(mask)))); foreach (var mask in masks) AddU32(list, mask);
            for (var index = 0; index < fields.Count; index++) if (IsSet(type, masks, index)) AddWire(list, fields[index], invalidPresence && index == 1, index == 0 ? currentAid : 0);
            return list.ToArray();
        }

        public static byte[] CreateSyntheticPrototype(int type, ObjectId id, int currentAid)
        {
            var fields = FieldsFor(type);
            var masks = new uint[MaskWords(type)];
            for (var index = 0; index < fields.Count; index++) SetBit(type, masks, index);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, new ObjectId(-1, new byte[16])); AddObjectId(list, id); AddI32(list, type); foreach (var mask in masks) AddU32(list, mask); for (var index = 0; index < fields.Count; index++) AddWire(list, fields[index], false, index == 0 ? currentAid : 0); return list.ToArray();
        }

        public static byte[] CreateUncheckedInstance(int encodedType, ObjectId prototype)
        {
            var bytes = CreateSyntheticInstance(3, prototype, true, false);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), encodedType);
            return bytes;
        }

        public static byte[] CreateSyntheticSingleField(int type, WireKind kind, out int payloadOffset)
        {
            var fields = FieldsFor(type);
            var field = Array.IndexOf(fields.ToArray(), kind);
            if (field < 0) throw new InvalidOperationException("The requested synthetic wire kind is absent from this type.");
            var masks = new uint[MaskWords(type)]; SetBit(type, masks, field);
            var list = new List<byte>(); AddI32(list, ExpectedObjectVersion); AddObjectId(list, ObjectId.A(7001)); AddObjectId(list, ObjectId.A(17)); AddI32(list, type); AddI16(list, 1); foreach (var mask in masks) AddU32(list, mask);
            payloadOffset = list.Count; AddWire(list, kind, false, 0); return list.ToArray();
        }

        private static void AddWire(List<byte> list, WireKind kind, bool invalidPresence, int i32Value)
        {
            if (kind == WireKind.I32) { AddI32(list, i32Value); return; }
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
        public long I64() { var value = BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)); return value; }
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
