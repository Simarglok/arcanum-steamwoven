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
                Console.WriteLine("self-test: PASS (privacy aggregation and full production-path structural/RLE rejection matrix)");
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

                    var decoderCheck = VerifyProductionDecoder(bytes, result);
                    candidates.Add(new Candidate(archiveArgument.Label, entry.Ordinal, GetArtCategory(entry), result, decoderCheck));
                }
                catch (ArtProbeException)
                {
                    // A candidate is deliberately not selected if it cannot prove the bounded layout.
                }
            }
        }

        WriteCategoryInventory(categoryCounts);

        var staticSamples = candidates.Where(candidate => candidate.Result.IsStatic).Take(6).ToArray();
        var directionalSamples = candidates.Where(candidate => !candidate.Result.IsStatic).Take(6).ToArray();
        var samples = staticSamples.Concat(directionalSamples).ToArray();

        Console.WriteLine($"probe candidates_read={probedCandidates} structurally_valid={candidates.Count} class_mismatches={expectedClassMismatches} selected={samples.Length} static={staticSamples.Length} directional={directionalSamples.Length}");
        foreach (var sample in samples)
        {
            Console.WriteLine(
                $"sample selector={sample.ArchiveLabel}#{sample.Ordinal} category={sample.Category} kind={(sample.Result.IsStatic ? "static" : "directional")} palettes={sample.Result.PaletteCount} frames={sample.Result.TotalFrames} raw_frames={sample.Result.RawFrames} rle_frames={sample.Result.RleFrames} decoded_pixels={sample.Result.DecodedPixels} decoder_sample_kinds={sample.DecoderCheck.SampleKinds} decoder_rgba_samples={sample.DecoderCheck.RgbaSamples} decoder_palette_slots={sample.DecoderCheck.PaletteSlots} header_group_match={sample.Result.HeaderGroupMatches}");
        }

        if (samples.Length < 6 || staticSamples.Length == 0 || directionalSamples.Length == 0)
        {
            throw new ArtProbeException("The bounded selector did not produce the required 6–12 mixed static/directional sample corpus.");
        }

        Console.WriteLine(
            $"sample_totals selected={samples.Length} frames={samples.Sum(sample => sample.Result.TotalFrames)} raw_frames={samples.Sum(sample => sample.Result.RawFrames)} rle_frames={samples.Sum(sample => sample.Result.RleFrames)} decoder_rgba_samples={samples.Sum(sample => sample.DecoderCheck.RgbaSamples)} decoder_palette_slots={samples.Sum(sample => sample.DecoderCheck.PaletteSlots)}");
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

    private static void WriteCategoryInventory(IEnumerable<KeyValuePair<string, int>> categoryCounts)
    {
        foreach (var category in categoryCounts)
        {
            Console.WriteLine($"category name={category.Key} art_entries={category.Value}");
        }
    }

    private static string GetArtCategory(DatEntry entry) => GetArtCategory(entry.RawNameBytes);

    private static string GetArtCategory(IReadOnlyList<byte> name)
    {
        var prefix = "art\\"u8;
        if (name.Count <= prefix.Length || !SegmentEquals(name, 0, prefix.Length, prefix))
        {
            return "other";
        }

        var categoryEnd = prefix.Length;
        while (categoryEnd < name.Count && name[categoryEnd] is not (byte)'\\' and not 0)
        {
            categoryEnd++;
        }

        // A top-level ART entry has no category directory. Its basename is not an
        // aggregate and must never be decoded or emitted by this research harness.
        if (categoryEnd == prefix.Length || categoryEnd == name.Count || name[categoryEnd] == 0)
        {
            return "other";
        }

        var categoryLength = categoryEnd - prefix.Length;
        if (SegmentEquals(name, prefix.Length, categoryLength, "item"u8)) return "item";
        if (SegmentEquals(name, prefix.Length, categoryLength, "critter"u8)) return "critter";
        if (SegmentEquals(name, prefix.Length, categoryLength, "monster"u8)) return "monster";
        if (SegmentEquals(name, prefix.Length, categoryLength, "unique_npc"u8)) return "unique_npc";
        if (SegmentEquals(name, prefix.Length, categoryLength, "scenery"u8)) return "scenery";
        if (SegmentEquals(name, prefix.Length, categoryLength, "tile"u8)) return "tile";
        if (SegmentEquals(name, prefix.Length, categoryLength, "wall"u8)) return "wall";
        if (SegmentEquals(name, prefix.Length, categoryLength, "interface"u8)) return "interface";
        if (SegmentEquals(name, prefix.Length, categoryLength, "portal"u8)) return "portal";
        return "other";
    }

    private static bool SegmentEquals(IReadOnlyList<byte> name, int start, int length, ReadOnlySpan<byte> expected)
    {
        if (length != expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (name[start + index] != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool? ClassifyCategory(string category) => category switch
    {
        "item" => true,
        "critter" or "monster" or "unique_npc" => false,
        _ => null,
    };

    private static ArtResult ProbeArt(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return ProbeArtCore(bytes);
        }
        catch (OverflowException exception)
        {
            throw new ArtProbeException($"ART arithmetic overflow: {exception.Message}");
        }
    }

    private static ArtResult ProbeArtCore(ReadOnlySpan<byte> bytes)
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

    private static ProductionDecoderCheck VerifyProductionDecoder(byte[] bytes, ArtResult result)
    {
        var framesPerGroup = CheckedToInt(U32(bytes, 32), "per-group frame count");
        var groups = result.IsStatic ? 1 : 8;
        for (var group = 0; group < groups; group++)
        {
            for (var frameIndex = 0; frameIndex < framesPerGroup; frameIndex++)
            {
                var frame = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frameIndex, 0));
                if (frame.Header.BitsPerPixel != 8 || frame.Rgba.Count != checked(frame.Width * frame.Height * 4))
                {
                    throw new ArtProbeException("Production ART decoder returned inconsistent frame metadata.");
                }
            }
        }

        var samples = new List<DecoderSample> { new(0, 0, "first") };
        if (framesPerGroup > 1)
        {
            samples.Add(new DecoderSample(0, framesPerGroup - 1, "nonfirst"));
        }

        if (groups > 1)
        {
            samples.Add(new DecoderSample(1, 0, "group"));
        }

        var presentSlots = Enumerable.Range(0, 4)
            .Where(slot => U32(bytes, 12 + (slot * sizeof(uint))) != 0)
            .ToArray();
        var sampleCount = 0;
        foreach (var paletteSlot in presentSlots)
        {
            var sawTransparent = false;
            var sawOpaque = false;
            foreach (var sample in samples)
            {
                var frame = ArtDecoder.Decode(bytes, new ArtFrameSelection(sample.Group, sample.Frame, paletteSlot));
                var pixels = VerifySampleRgba(bytes, result.TotalFrames, framesPerGroup, paletteSlot, sample, frame);
                sawTransparent |= pixels.SawTransparent;
                sawOpaque |= pixels.SawOpaque;
                sampleCount++;
            }

            if (!sawTransparent || !sawOpaque)
            {
                throw new ArtProbeException("Bounded production decoder samples did not exercise both transparent index 0 and nonzero BGRX palette output.");
            }
        }

        return new ProductionDecoderCheck(result.TotalFrames, presentSlots.Length, sampleCount, string.Join(",", samples.Select(sample => sample.Kind)));
    }

    // This uses direct ART bytes only to cross-check the production API on a bounded
    // sample. It is not an independent pixel oracle.
    private static PixelEvidence VerifySampleRgba(byte[] bytes, int totalFrames, int framesPerGroup, int paletteSlot, DecoderSample sample, ArtFrame frame)
    {
        var paletteOffset = HeaderBytes;
        ReadOnlySpan<byte> palette = default;
        for (var slot = 0; slot < 4; slot++)
        {
            if (U32(bytes, 12 + (slot * sizeof(uint))) == 0)
            {
                continue;
            }

            if (slot == paletteSlot)
            {
                palette = bytes.AsSpan(paletteOffset, PaletteBytes);
            }

            paletteOffset += PaletteBytes;
        }

        if (palette.IsEmpty)
        {
            throw new ArtProbeException("A declared palette slot was not readable for production sampling.");
        }

        var tableBytes = checked(totalFrames * FrameHeaderBytes);
        var payloadOffset = checked(paletteOffset + tableBytes);
        var linearIndex = checked((sample.Group * framesPerGroup) + sample.Frame);
        for (var index = 0; index < linearIndex; index++)
        {
            payloadOffset = checked(payloadOffset + CheckedToInt(U32(bytes, paletteOffset + (index * FrameHeaderBytes) + 8), "sample frame payload length"));
        }

        var selectedHeader = checked(paletteOffset + (linearIndex * FrameHeaderBytes));
        var width = CheckedToInt(U32(bytes, selectedHeader), "sample frame width");
        var height = CheckedToInt(U32(bytes, selectedHeader + 4), "sample frame height");
        var payloadLength = CheckedToInt(U32(bytes, selectedHeader + 8), "sample frame payload length");
        var pixelCount = checked(width * height);
        var indices = DecodeSampleIndices(bytes.AsSpan(payloadOffset, payloadLength), pixelCount);
        if (frame.Width != width || frame.Height != height || frame.Rgba.Count != checked(pixelCount * 4))
        {
            throw new ArtProbeException("Production ART decoder sample metadata is inconsistent with its bounded input.");
        }

        var sawTransparent = false;
        var sawOpaque = false;
        for (var index = 0; index < indices.Length; index++)
        {
            var value = indices[index];
            var destination = index * 4;
            if (value == 0)
            {
                sawTransparent = true;
                if (frame.Rgba[destination] != 0 || frame.Rgba[destination + 1] != 0 || frame.Rgba[destination + 2] != 0 || frame.Rgba[destination + 3] != 0)
                {
                    throw new ArtProbeException("Production ART decoder does not return transparent black for palette index 0.");
                }

                continue;
            }

            sawOpaque = true;
            var source = value * 4;
            if (frame.Rgba[destination] != palette[source + 2] || frame.Rgba[destination + 1] != palette[source + 1] ||
                frame.Rgba[destination + 2] != palette[source] || frame.Rgba[destination + 3] != byte.MaxValue)
            {
                throw new ArtProbeException("Production ART decoder BGRX-to-RGBA sample does not match the bounded source bytes.");
            }
        }

        return new PixelEvidence(sawTransparent, sawOpaque);
    }

    private static byte[] DecodeSampleIndices(ReadOnlySpan<byte> payload, int pixels)
    {
        if (payload.Length == pixels)
        {
            return payload.ToArray();
        }

        var indices = new byte[pixels];
        var input = 0;
        var output = 0;
        while (input < payload.Length)
        {
            var control = payload[input++];
            var count = control & 0x7f;
            if (count == 0 || count > indices.Length - output)
            {
                throw new ArtProbeException("Bounded production sample RLE count is invalid.");
            }

            if ((control & 0x80) != 0)
            {
                if (count > payload.Length - input)
                {
                    throw new ArtProbeException("Bounded production sample literal RLE is truncated.");
                }

                payload.Slice(input, count).CopyTo(indices.AsSpan(output, count));
                input += count;
            }
            else
            {
                if (input == payload.Length)
                {
                    throw new ArtProbeException("Bounded production sample repeated RLE is truncated.");
                }

                indices.AsSpan(output, count).Fill(payload[input++]);
            }

            output += count;
        }

        if (output != pixels)
        {
            throw new ArtProbeException("Bounded production sample RLE did not produce its declared pixels.");
        }

        return indices;
    }

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

        AssertRejected(() => ProbeArt(raw[..(HeaderBytes - 1)]), "truncated header was accepted");
        var withPalette = BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [1, 2, 3, 4], declaredSize: 4, paletteCount: 1);
        AssertRejected(() => ProbeArt(withPalette[..(HeaderBytes + PaletteBytes - 1)]), "truncated palette was accepted");
        AssertRejected(() => ProbeArt(raw[..(HeaderBytes + FrameHeaderBytes - 1)]), "truncated frame table was accepted");
        AssertRejected(() => ProbeArt(raw[..^1]), "truncated payload was accepted");

        AssertRejected(() => ProbeArt(WithU32(raw, 32, 0)), "zero frame count was accepted");
        AssertRejected(() => ProbeArt(WithU32(raw, 32, (uint)(MaxFrames + 1))), "over-cap frame count was accepted");
        var directionalOverflow = BuildSynthetic(staticFlag: false, width: 2, height: 2, payload: [4, 9], declaredSize: 2);
        AssertRejected(() => ProbeArt(WithU32(directionalOverflow, 32, uint.MaxValue)), "overflow frame count was accepted");

        AssertRejected(() => ProbeArt(WithU32(raw, HeaderBytes, 0)), "zero width was accepted");
        AssertRejected(() => ProbeArt(WithU32(raw, HeaderBytes + 4, 0)), "zero height was accepted");
        var oversizedDimension = BuildSynthetic(staticFlag: true, width: (uint)(MaxDimension + 1), height: 1, payload: [1], declaredSize: 1);
        AssertRejected(() => ProbeArt(oversizedDimension), "oversized dimensions were accepted");
        AssertRejected(() => ProbeArt(WithU32(raw, HeaderBytes + 8, 0)), "zero payload was accepted");
        AssertRejected(() => ProbeArt(WithU32(raw, HeaderBytes + 8, uint.MaxValue)), "oversized payload was accepted");
        AssertRejected(() => ProbeArt(WithTrailingByte(raw, 0)), "trailing bytes were accepted");

        AssertRejected(() => ProbeArt(BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [0x84], declaredSize: 1)), "truncated literal run was accepted");
        AssertRejected(() => ProbeArt(BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [4], declaredSize: 1)), "truncated repeat run was accepted");
        AssertRejected(() => ProbeArt(BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [0, 0], declaredSize: 2)), "zero RLE count was accepted");
        AssertRejected(() => ProbeArt(BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [5, 9], declaredSize: 2)), "RLE overrun was accepted");
        AssertRejected(() => ProbeArt(BuildSynthetic(staticFlag: true, width: 2, height: 2, payload: [3, 9], declaredSize: 2)), "under-produced RLE output was accepted");

        AssertTopLevelNameIsNotEmitted();
    }

    private static void AssertTopLevelNameIsNotEmitted()
    {
        const string sentinel = "SYNTHETIC_TOP_LEVEL_ART_NAME_MUST_NOT_LEAK.art";
        var category = GetArtCategory(System.Text.Encoding.ASCII.GetBytes($"art\\{sentinel}"));
        Assert(category == "other", "top-level ART entry was not mapped to the safe aggregate");

        var originalOut = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            WriteCategoryInventory(new SortedDictionary<string, int>(StringComparer.Ordinal) { [category] = 1 });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = captured.ToString();
        Assert(output.Contains("category name=other art_entries=1", StringComparison.Ordinal), "safe aggregate was not emitted");
        Assert(!output.Contains(sentinel, StringComparison.Ordinal), "top-level ART name reached stdout");
    }

    private static byte[] BuildSynthetic(bool staticFlag, uint width, uint height, byte[] payload, uint declaredSize, int paletteCount = 0)
    {
        var rotations = staticFlag ? 1 : 8;
        var paletteBytes = checked(paletteCount * PaletteBytes);
        var frameHeadersOffset = checked(HeaderBytes + paletteBytes);
        var frameDataOffset = checked(frameHeadersOffset + (rotations * FrameHeaderBytes));
        var bytes = new byte[checked(frameDataOffset + (rotations * payload.Length))];
        WriteU32(bytes, 0, staticFlag ? 1U : 0U);
        WriteU32(bytes, 32, 1);
        for (var palette = 0; palette < paletteCount; palette++)
        {
            WriteU32(bytes, 12 + (palette * sizeof(uint)), 1);
        }
        for (var direction = 0; direction < rotations; direction++)
        {
            WriteU32(bytes, 68 + (direction * 4), declaredSize);
            var frame = frameHeadersOffset + (direction * FrameHeaderBytes);
            WriteU32(bytes, frame, width);
            WriteU32(bytes, frame + 4, height);
            WriteU32(bytes, frame + 8, declaredSize);
            payload.CopyTo(bytes, frameDataOffset + (direction * payload.Length));
        }

        return bytes;
    }

    private static byte[] WithU32(byte[] source, int offset, uint value)
    {
        var copy = source.ToArray();
        WriteU32(copy, offset, value);
        return copy;
    }

    private static byte[] WithTrailingByte(byte[] source, byte value)
    {
        var copy = new byte[source.Length + 1];
        source.CopyTo(copy, 0);
        copy[^1] = value;
        return copy;
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

    private static void AssertRejected(Action action, string message)
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
    private sealed record Candidate(string ArchiveLabel, int Ordinal, string Category, ArtResult Result, ProductionDecoderCheck DecoderCheck);
    private sealed record ArtResult(bool IsStatic, int PaletteCount, int TotalFrames, int RawFrames, int RleFrames, ulong DecodedPixels, bool HeaderGroupMatches);
    private sealed record DecoderSample(int Group, int Frame, string Kind);
    private sealed record PixelEvidence(bool SawTransparent, bool SawOpaque);
    private sealed record ProductionDecoderCheck(int Frames, int PaletteSlots, int RgbaSamples, string SampleKinds);
    private sealed class ArtProbeException(string message) : Exception(message);
}
