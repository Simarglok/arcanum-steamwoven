using System.Buffers.Binary;
using Arcanum.Data;

namespace Arcanum.ArtProbe;

internal static class Program
{
    private const int HeaderBytes = 132;
    private const int PaletteBytes = 256 * 4;
    private const int FrameHeaderBytes = 28;
    private const int MaxCandidateReads = 48;
    private const int MaxDecodedEntryBytes = 1024 * 1024;
    private const int MaxFrames = 1024;
    private const int MaxDimension = 4096;
    private const int MaxFramePixels = 4 * 1024 * 1024;

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS (valid raw/RLE, truncated header, malformed RLE, and oversized dimensions)");
                return 0;
            }

            var archives = ParseArchives(args);
            if (archives.Count == 0)
            {
                Console.Error.WriteLine("usage: --self-test | --archive LABEL:PATH [--archive LABEL:PATH ...]");
                return 2;
            }

            ProbeArchives(archives);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"art-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static List<ArchiveArgument> ParseArchives(IEnumerable<string> args)
    {
        var values = args.ToArray();
        var archives = new List<ArchiveArgument>();
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] != "--archive" || ++index == values.Length)
            {
                throw new ArgumentException("Each archive must use --archive LABEL:PATH.");
            }

            var separator = values[index].IndexOf(':');
            if (separator <= 0 || separator == values[index].Length - 1)
            {
                throw new ArgumentException("Archive selector must be LABEL:PATH.");
            }

            archives.Add(new ArchiveArgument(values[index][..separator], values[index][(separator + 1)..]));
        }

        return archives;
    }

    private static void ProbeArchives(IEnumerable<ArchiveArgument> archiveArguments)
    {
        var candidates = new List<Candidate>();
        var categoryCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var expectedClassMismatches = 0;
        var probedCandidates = 0;
        foreach (var archiveArgument in archiveArguments)
        {
            using var archive = DatArchive.Open(
                archiveArgument.Path,
                new DatReaderOptions
                {
                    MaxStoredEntryBytes = MaxDecodedEntryBytes,
                    MaxDecodedEntryBytes = MaxDecodedEntryBytes,
                });

            var artEntries = archive.Entries.Where(IsArtEntry).ToArray();
            var readableCandidates = artEntries.Count(IsBoundedReadable);
            foreach (var entry in artEntries)
            {
                var category = GetArtCategory(entry);
                categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + 1;
            }
            Console.WriteLine(
                $"inventory archive={archiveArgument.Label} dat_entries={archive.Entries.Count} art_entries={artEntries.Length} bounded_art_candidates={readableCandidates}");

            foreach (var entry in artEntries.Where(IsBoundedReadable).Where(entry => ClassifyCategory(GetArtCategory(entry)) is not null))
            {
                var expectedStatic = ClassifyCategory(GetArtCategory(entry))!.Value;
                if (expectedStatic && candidates.Count(candidate => candidate.Result.IsStatic) >= 6)
                {
                    continue;
                }

                if (!expectedStatic && candidates.Count(candidate => !candidate.Result.IsStatic) >= 6)
                {
                    continue;
                }

                if (probedCandidates >= MaxCandidateReads)
                {
                    break;
                }

                probedCandidates++;
                var bytes = archive.ReadEntry(entry);
                try
                {
                    var result = ProbeArt(bytes);
                    if (result.IsStatic != expectedStatic)
                    {
                        expectedClassMismatches++;
                        continue;
                    }

                    candidates.Add(new Candidate(archiveArgument.Label, entry.Ordinal, GetArtCategory(entry), result));
                }
                catch (ArtProbeException)
                {
                    // A candidate is deliberately not selected if it cannot prove the bounded layout.
                }
            }
        }

        foreach (var category in categoryCounts)
        {
            Console.WriteLine($"category name={category.Key} art_entries={category.Value}");
        }

        var staticSamples = candidates.Where(candidate => candidate.Result.IsStatic).Take(6).ToArray();
        var directionalSamples = candidates.Where(candidate => !candidate.Result.IsStatic).Take(6).ToArray();
        var samples = staticSamples.Concat(directionalSamples).ToArray();

        Console.WriteLine($"probe candidates_read={probedCandidates} structurally_valid={candidates.Count} class_mismatches={expectedClassMismatches} selected={samples.Length} static={staticSamples.Length} directional={directionalSamples.Length}");
        foreach (var sample in samples)
        {
            Console.WriteLine(
                $"sample selector={sample.ArchiveLabel}#{sample.Ordinal} category={sample.Category} kind={(sample.Result.IsStatic ? "static" : "directional")} palettes={sample.Result.PaletteCount} frames={sample.Result.TotalFrames} raw_frames={sample.Result.RawFrames} rle_frames={sample.Result.RleFrames} decoded_pixels={sample.Result.DecodedPixels} header_group_match={sample.Result.HeaderGroupMatches}");
        }

        if (samples.Length < 6 || staticSamples.Length == 0 || directionalSamples.Length == 0)
        {
            throw new ArtProbeException("The bounded selector did not produce the required 6–12 mixed static/directional sample corpus.");
        }
    }

    private static bool IsArtEntry(DatEntry entry)
    {
        var name = entry.RawNameBytes;
        return name.Count >= 5 &&
               name[^1] == 0 &&
               name[^5] == (byte)'.' &&
               (name[^4] | 0x20) == (byte)'a' &&
               (name[^3] | 0x20) == (byte)'r' &&
               (name[^2] | 0x20) == (byte)'t';
    }

    private static bool IsBoundedReadable(DatEntry entry) =>
        (entry.Flags == 0x001 || entry.Flags == 0x002) &&
        entry.DeclaredOutputSize is > 0 and <= MaxDecodedEntryBytes &&
        entry.StoredSize is > 0 and <= MaxDecodedEntryBytes;

    private static string GetArtCategory(DatEntry entry)
    {
        var name = entry.RawNameBytes;
        var prefix = "art\\"u8;
        if (name.Count <= prefix.Length || !name.Take(prefix.Length).SequenceEqual(prefix.ToArray()))
        {
            return "other";
        }

        var end = prefix.Length;
        while (end < name.Count && name[end] is not (byte)'\\' and not 0)
        {
            end++;
        }

        return System.Text.Encoding.ASCII.GetString(name.Skip(prefix.Length).Take(end - prefix.Length).ToArray()).ToLowerInvariant();
    }

    private static bool? ClassifyCategory(string category) => category switch
    {
        "item" => true,
        "critter" or "monster" or "unique_npc" => false,
        _ => null,
    };

    private static ArtResult ProbeArt(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes)
        {
            throw new ArtProbeException("ART header is truncated.");
        }

        var flags = U32(bytes, 0);
        var palettes = 0;
        for (var slot = 0; slot < 4; slot++)
        {
            if (U32(bytes, 12 + (slot * 4)) != 0)
            {
                palettes++;
            }
        }

        var frameCount = U32(bytes, 32);
        var rotations = (flags & 1) != 0 ? 1U : 8U;
        var totalFrames = CheckedToInt(checked(frameCount * rotations), "frame count");
        if (totalFrames is < 1 or > MaxFrames)
        {
            throw new ArtProbeException("ART frame count is outside the probe cap.");
        }

        var frameHeadersOffset = checked(HeaderBytes + (palettes * PaletteBytes));
        var frameDataOffset = checked(frameHeadersOffset + checked(totalFrames * FrameHeaderBytes));
        if (frameDataOffset > bytes.Length)
        {
            throw new ArtProbeException("ART palette or frame-header table is truncated.");
        }

        var headerDirectionSums = new uint[8];
        for (var direction = 0; direction < 8; direction++)
        {
            headerDirectionSums[direction] = U32(bytes, 68 + (direction * sizeof(uint)));
        }

        var observedDirectionSums = new ulong[8];
        var payloadOffset = frameDataOffset;
        var decodedPixels = 0UL;
        var rawFrames = 0;
        var rleFrames = 0;
        for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            var headerOffset = checked(frameHeadersOffset + (frameIndex * FrameHeaderBytes));
            var width = U32(bytes, headerOffset);
            var height = U32(bytes, headerOffset + 4);
            var dataSize = U32(bytes, headerOffset + 8);
            var pixels = checked((ulong)width * height);
            if (width is 0 or > MaxDimension || height is 0 or > MaxDimension || pixels > MaxFramePixels)
            {
                throw new ArtProbeException("ART dimensions are outside the probe cap.");
            }

            var payloadLength = CheckedToInt(dataSize, "frame data size");
            if (payloadLength == 0 || payloadLength > bytes.Length - payloadOffset)
            {
                throw new ArtProbeException("ART frame payload is truncated or has zero length.");
            }

            var payload = bytes.Slice(payloadOffset, payloadLength);
            if (dataSize == pixels)
            {
                // Raw indexed pixels; no renderer or palette-alpha policy is inferred.
                rawFrames++;
            }
            else
            {
                ValidateRle(payload, CheckedToInt(pixels, "frame pixels"));
                rleFrames++;
            }

            var direction = frameIndex / CheckedToInt(frameCount, "per-direction frame count");
            observedDirectionSums[direction] = checked(observedDirectionSums[direction] + dataSize);
            decodedPixels = checked(decodedPixels + pixels);
            payloadOffset = checked(payloadOffset + payloadLength);
        }

        if (payloadOffset != bytes.Length)
        {
            throw new ArtProbeException("ART frame payloads do not consume the selected DAT entry exactly.");
        }

        var headerGroupMatches = true;
        for (var direction = 0; direction < 8; direction++)
        {
            if (headerDirectionSums[direction] != observedDirectionSums[direction])
            {
                headerGroupMatches = false;
            }
        }

        return new ArtResult((flags & 1) != 0, palettes, totalFrames, rawFrames, rleFrames, decodedPixels, headerGroupMatches);
    }

    private static void ValidateRle(ReadOnlySpan<byte> payload, int expectedPixels)
    {
        var input = 0;
        var output = 0;
        while (input < payload.Length)
        {
            var control = payload[input++];
            var count = control & 0x7f;
            if (count == 0 || count > expectedPixels - output)
            {
                throw new ArtProbeException("ART RLE control count is invalid.");
            }

            if ((control & 0x80) == 0)
            {
                if (input == payload.Length)
                {
                    throw new ArtProbeException("ART RLE repeated run lacks its byte.");
                }

                input++;
            }
            else
            {
                if (count > payload.Length - input)
                {
                    throw new ArtProbeException("ART RLE literal run is truncated.");
                }

                input += count;
            }

            output += count;
        }

        if (output != expectedPixels)
        {
            throw new ArtProbeException("ART RLE output size differs from frame dimensions.");
        }
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static int CheckedToInt(uint value, string label) =>
        value <= int.MaxValue ? (int)value : throw new ArtProbeException($"ART {label} exceeds Int32.");

    private static int CheckedToInt(ulong value, string label) =>
        value <= int.MaxValue ? (int)value : throw new ArtProbeException($"ART {label} exceeds Int32.");

    private static void SelfTest()
    {
        var raw = BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [1, 2, 3, 4], declaredSize: 4);
        var parsedRaw = ProbeArt(raw);
        Assert(parsedRaw.IsStatic && parsedRaw.DecodedPixels == 4 && parsedRaw.HeaderGroupMatches, "raw ART did not validate");

        var rle = BuildSynthetic(staticFlag: false, width: 2, height: 2, payload: [4, 9], declaredSize: 2);
        var parsedRle = ProbeArt(rle);
        Assert(!parsedRle.IsStatic && parsedRle.TotalFrames == 8 && parsedRle.DecodedPixels == 32, "RLE directional ART did not validate");

        AssertThrows(() => ProbeArt(raw.AsSpan(0, HeaderBytes - 1)), "truncated header was accepted");
        var badRle = BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [5, 9], declaredSize: 2);
        AssertThrows(() => ProbeArt(badRle), "RLE overrun was accepted");
        var oversized = BuildSynthetic(staticFlag: true, width: (uint)(MaxDimension + 1), height: 1, payload: [1], declaredSize: 1);
        AssertThrows(() => ProbeArt(oversized), "oversized dimensions were accepted");
    }

    private static byte[] BuildSynthetic(bool staticFlag, uint width, uint height, byte[] payload, uint declaredSize)
    {
        var rotations = staticFlag ? 1 : 8;
        var bytes = new byte[HeaderBytes + (rotations * FrameHeaderBytes) + (rotations * payload.Length)];
        WriteU32(bytes, 0, staticFlag ? 1U : 0U);
        WriteU32(bytes, 32, 1);
        for (var direction = 0; direction < rotations; direction++)
        {
            WriteU32(bytes, 68 + (direction * 4), declaredSize);
            var frame = HeaderBytes + (direction * FrameHeaderBytes);
            WriteU32(bytes, frame, width);
            WriteU32(bytes, frame + 4, height);
            WriteU32(bytes, frame + 8, declaredSize);
            payload.CopyTo(bytes, HeaderBytes + (rotations * FrameHeaderBytes) + (direction * payload.Length));
        }

        return bytes;
    }

    private static void WriteU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (ArtProbeException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private sealed record ArchiveArgument(string Label, string Path);
    private sealed record Candidate(string ArchiveLabel, int Ordinal, string Category, ArtResult Result);
    private sealed record ArtResult(bool IsStatic, int PaletteCount, int TotalFrames, int RawFrames, int RleFrames, ulong DecodedPixels, bool HeaderGroupMatches);
    private sealed class ArtProbeException(string message) : Exception(message);
}
