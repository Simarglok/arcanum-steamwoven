using System.Buffers.Binary;
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
    private const long ExpectedModuleBytes = 355_938_305;
    private const string ExpectedModuleSha256 = "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f";

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS (bounded selected-sector parser rejects truncation, invalid counts, overflow-adjacent layout, and unsupported tile IDs)");
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

        using var archive = DatArchive.Open(path, new DatReaderOptions
        {
            MaxArchiveBytes = checked((uint)ExpectedModuleBytes),
            MaxStoredEntryBytes = MaxDecodedSectorBytes,
            MaxDecodedEntryBytes = MaxDecodedSectorBytes,
        });
        var matches = archive.Entries.Where(entry => RawNameEquals(entry, SectorEntryName)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException("The bounded sector selector did not resolve exactly one DAT entry.");
        }

        var entry = matches[0];
        if (entry.Flags != 0x00000002 || entry.DeclaredOutputSize > MaxDecodedSectorBytes)
        {
            throw new InvalidDataException("The selected sector entry violates its bounded compressed-input contract.");
        }

        var sector = SectorSlice.Parse(archive.ReadEntry(entry), rejectNonTileArtIds: false);
        var after = InputFingerprint.Create(path);
        if (after != before)
        {
            throw new InvalidDataException("The module changed during this read-only probe.");
        }

        Console.WriteLine($"input bytes={before.Bytes} sha256={before.Sha256}");
        Console.WriteLine($"sector ordinal={entry.Ordinal} flags=0x{entry.Flags:x8} decoded_bytes={entry.DeclaredOutputSize}");
        Console.WriteLine($"sector lights={sector.LightCount} tiles={SectorTileCount} distinct_tile_art_ids={sector.DistinctTileArtIds} invalid_tile_art_ids={sector.InvalidTileArtIds} non_tile_art_ids={sector.NonTileArtIds} object_payload_bytes={sector.ObjectPayloadBytes} trailing_object_count={sector.TrailingObjectCount}");
        Console.WriteLine($"sector art_type_counts={string.Join(',', sector.ArtTypeCounts.Select(count => count.ToString()))}");
        Console.WriteLine("integrity before_after_match=true");
    }

    private static bool RawNameEquals(DatEntry entry, string expected)
    {
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected);
        return entry.RawNameBytes.Count == expectedBytes.Length + 1 &&
               entry.RawNameBytes[^1] == 0 &&
               entry.RawNameBytes.Take(expectedBytes.Length).SequenceEqual(expectedBytes);
    }

    private static void SelfTest()
    {
        var valid = CreateFixture(lightCount: 0, tileId: 0, trailingObjectCount: 2);
        var result = SectorSlice.Parse(valid, rejectNonTileArtIds: true);
        if (result.LightCount != 0 || result.DistinctTileArtIds != 1 || result.InvalidTileArtIds != 0 || result.NonTileArtIds != 0 || result.TrailingObjectCount != 2)
        {
            throw new InvalidOperationException("The valid synthetic sector was not parsed as expected.");
        }

        if (ProjectRelative(0, 0) != (0, 0) ||
            ProjectRelative(1, 0) != (-40, 20) ||
            ProjectRelative(0, 1) != (40, 20) ||
            ProjectRelative(30, 32) != (80, 1240))
        {
            throw new InvalidOperationException("The CE-derived relative projection examples changed.");
        }

        AssertRejected(() => SectorSlice.Parse(valid[..3], rejectNonTileArtIds: true), "truncated light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, -1), rejectNonTileArtIds: true), "negative light count");
        AssertRejected(() => SectorSlice.Parse(WithI32(valid, 0, MaxLights + 1), rejectNonTileArtIds: true), "light count over cap");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)) - 1)], rejectNonTileArtIds: true), "truncated tile table");
        AssertRejected(() => SectorSlice.Parse(valid[..(sizeof(int) + (SectorTileCount * sizeof(uint)))], rejectNonTileArtIds: true), "missing terminal object count after complete tile table");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(lightCount: 0, tileId: 0x10000000, trailingObjectCount: 0), rejectNonTileArtIds: true), "non-tile art identifier");
        AssertRejected(() => SectorSlice.Parse(CreateFixture(lightCount: 0, tileId: 0, trailingObjectCount: -1), rejectNonTileArtIds: true), "negative object count");
    }

    private static byte[] CreateFixture(int lightCount, uint tileId, int trailingObjectCount)
    {
        var tileStart = checked(sizeof(int) + (lightCount * SerializedLightBytes));
        var bytes = new byte[checked(tileStart + (SectorTileCount * sizeof(uint)) + sizeof(int))];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, lightCount);
        for (var index = 0; index < SectorTileCount; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tileStart + (index * sizeof(uint))), tileId);
        }
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
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    // This is only the relative form of the pinned CE location transform. A scene
    // implementation still supplies its own screen anchor and asset baselines.
    private static (int X, int Y) ProjectRelative(int localX, int localY) =>
        (checked(40 * (localY - localX)), checked(20 * (localY + localX)));

    private sealed record SectorSlice(int LightCount, int DistinctTileArtIds, int InvalidTileArtIds, int NonTileArtIds, int ObjectPayloadBytes, int TrailingObjectCount, int[] ArtTypeCounts)
    {
        public static SectorSlice Parse(ReadOnlySpan<byte> bytes, bool rejectNonTileArtIds)
        {
            if (bytes.Length < sizeof(int))
            {
                throw new InvalidDataException("Sector light count is truncated.");
            }

            var lightCount = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            if (lightCount < 0 || lightCount > MaxLights)
            {
                throw new InvalidDataException("Sector light count is outside the supported bound.");
            }

            var lightBytes = checked(lightCount * SerializedLightBytes);
            var tileOffset = checked(sizeof(int) + lightBytes);
            var tileBytes = checked(SectorTileCount * sizeof(uint));
            if (tileOffset > bytes.Length || tileBytes > bytes.Length - tileOffset)
            {
                throw new InvalidDataException("Sector tile table is truncated.");
            }

            var distinctArtIds = new HashSet<uint>();
            var artTypeCounts = new int[16];
            var invalidTileArtIds = 0;
            var nonTileArtIds = 0;
            for (var index = 0; index < SectorTileCount; index++)
            {
                var artId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(tileOffset + (index * sizeof(uint)), sizeof(uint)));
                distinctArtIds.Add(artId);
                artTypeCounts[artId >> 28]++;
                if (artId == uint.MaxValue)
                {
                    invalidTileArtIds++;
                }
                else if ((artId >> 28) != 0)
                {
                    nonTileArtIds++;
                }
            }

            if (rejectNonTileArtIds && nonTileArtIds != 0)
            {
                throw new InvalidDataException("Sector tile table contains a non-tile ART identifier.");
            }

            var terminalCountOffset = checked(tileOffset + tileBytes);
            if (bytes.Length - terminalCountOffset < TerminalObjectCountBytes)
            {
                throw new InvalidDataException("Sector terminal object count is truncated after the tile table.");
            }
            var trailingObjectCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[^TerminalObjectCountBytes..]);
            if (trailingObjectCount < 0 || trailingObjectCount > SectorTileCount)
            {
                throw new InvalidDataException("Sector trailing object count is outside the supported bound.");
            }

            var objectPayloadBytes = checked(bytes.Length - terminalCountOffset - TerminalObjectCountBytes);
            return new SectorSlice(lightCount, distinctArtIds.Count, invalidTileArtIds, nonTileArtIds, objectPayloadBytes, trailingObjectCount, artTypeCounts);
        }
    }

    private sealed record InputFingerprint(long Bytes, string Sha256)
    {
        public static InputFingerprint Create(string path)
        {
            using var stream = File.OpenRead(path);
            return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
    }
}
