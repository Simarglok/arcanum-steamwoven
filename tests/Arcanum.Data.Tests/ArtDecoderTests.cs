using System.Buffers.Binary;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class ArtDecoderTests
{
    [TestMethod]
    public void Decode_preserves_metadata_palette_gaps_and_converts_row_major_rgba()
    {
        var palettes = new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 99), (0, 0, 0, 0)), [2] = Palette((9, 8, 7, 255), (30, 20, 10, 0)) };
        var bytes = Fixture.Create(staticFlag: true, framesPerGroup: 1,
            [new Frame(2, 3, [0, 1, 2, 1, 2, 1], -4, 5, -6, 7)], palettes,
            flags: 0x41, fps: -12, actionFrame: -3, rawWord68: 0xfeedbeef);
        var original = bytes.ToArray();

        var result = ArtDecoder.Decode(bytes, new ArtFrameSelection(0, 0, 2));

        Assert.AreEqual(2, result.Width);
        Assert.AreEqual(3, result.Height);
        Assert.AreEqual(2, result.RequestedPaletteSlot);
        Assert.AreEqual(2, result.EffectivePaletteSlot);
        Assert.AreEqual(4, result.PalettesBySlot.Count);
        Assert.IsNull(result.PalettesBySlot[1]);
        CollectionAssert.AreEqual(new byte[]
        {
            0, 0, 0, 0, 10, 20, 30, 255, 0, 0, 0, 255,
            10, 20, 30, 255, 0, 0, 0, 255, 10, 20, 30, 255,
        }, result.Rgba.ToArray());
        Assert.AreEqual(0x41u, result.Header.Flags);
        Assert.AreEqual(-12, result.Header.FramesPerSecond);
        Assert.AreEqual(-3, result.Header.ActionFrame);
        Assert.AreEqual(0xfeedbeefu, result.Header.RawWords68To99[0]);
        Assert.AreEqual(-4, result.Frame.HotX);
        Assert.AreEqual(5, result.Frame.HotY);
        Assert.AreEqual(-6, result.Frame.OffsetX);
        Assert.AreEqual(7, result.Frame.OffsetY);
        Assert.AreEqual(28, result.Frame.RawHeader.Count);
        CollectionAssert.AreEqual(original, bytes);
        Assert.IsFalse(result.Rgba is byte[]);
        Assert.IsFalse(result.Header.RawBytes is byte[]);
    }

    [TestMethod]
    public void Decode_selects_physical_groups_frames_and_slot_zero_fallback()
    {
        var frames = new List<Frame>();
        for (var index = 0; index < 16; index++)
        {
            frames.Add(new Frame(1, 1, [(byte)(index + 1)]));
        }

        var bytes = Fixture.Create(staticFlag: false, framesPerGroup: 2, frames, new Dictionary<int, byte[]>
        {
            [0] = PaletteEntries(
                (1, 1, 2, 3, 0),
                (16, 90, 80, 70, 0)),
        });
        var result = ArtDecoder.Decode(bytes, new ArtFrameSelection(7, 1, 3));

        Assert.AreEqual(7, result.GroupIndex);
        Assert.AreEqual(1, result.FrameIndex);
        Assert.AreEqual(3, result.RequestedPaletteSlot);
        Assert.AreEqual(0, result.EffectivePaletteSlot);
        CollectionAssert.AreEqual(new byte[] { 70, 80, 90, 255 }, result.Rgba.ToArray());
        Assert.AreEqual(8, result.Header.PhysicalGroupCount);
        Assert.AreEqual(2, result.Header.FramesPerGroup);
    }

    [TestMethod]
    public void Decode_handles_raw_rle_and_selected_only_rle_validation()
    {
        var rawControls = new Frame(2, 2, [0x84, 1, 2, 3]);
        var literalRepeat = new Frame(2, 3, [0x82, 1, 2, 3, 3, 0x81, 4]);
        var bytes = Fixture.Create(staticFlag: true, framesPerGroup: 2, [rawControls, literalRepeat],
            new Dictionary<int, byte[]>
            {
                [0] = PaletteEntries(
                    (1, 1, 2, 3, 0),
                    (2, 4, 5, 6, 0),
                    (3, 7, 8, 9, 0),
                    (4, 10, 11, 12, 0),
                    (132, 13, 14, 15, 0)),
            });

        var raw = ArtDecoder.Decode(bytes, new ArtFrameSelection(0, 0, 0));
        var rle = ArtDecoder.Decode(bytes, new ArtFrameSelection(0, 1, 0));
        CollectionAssert.AreEqual(new byte[]
        {
            15, 14, 13, 255, 3, 2, 1, 255, 6, 5, 4, 255, 9, 8, 7, 255,
        }, raw.Rgba.ToArray());
        Assert.AreEqual(24, rle.Rgba.Count);

        var malformedUnselected = Fixture.Create(staticFlag: true, framesPerGroup: 2,
            [new Frame(1, 1, [1]), new Frame(2, 2, [0, 0])], new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 0)) });
        Assert.AreEqual(4, ArtDecoder.Decode(malformedUnselected, new ArtFrameSelection(0, 0, 0)).Rgba.Count);
        Throws<ArtFormatException>(() => ArtDecoder.Decode(malformedUnselected, new ArtFrameSelection(0, 1, 0)));
    }

    [TestMethod]
    public void Decode_rejects_selectors_palettes_and_malformed_rle()
    {
        var valid = Fixture.Create(staticFlag: true, framesPerGroup: 1, [new Frame(2, 2, [4, 1])], new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 0)) });
        Throws<ArgumentOutOfRangeException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(-1, 0, 0)));
        Throws<ArgumentOutOfRangeException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(0, 1, 0)));
        Throws<ArgumentOutOfRangeException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(0, 0, -1)));
        Throws<ArgumentOutOfRangeException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(0, 0, 4)));
        var noFallback = Fixture.Create(staticFlag: true, framesPerGroup: 1, [new Frame(1, 1, [1])], new Dictionary<int, byte[]> { [2] = Palette((1, 2, 3, 0)) });
        Throws<ArtFormatException>(() => ArtDecoder.Decode(noFallback, new ArtFrameSelection(0, 0, 1)));
        var noPalettes = Fixture.Create(staticFlag: true, framesPerGroup: 1, [new Frame(1, 1, [1])], new Dictionary<int, byte[]>());
        Throws<ArtFormatException>(() => ArtDecoder.Decode(noPalettes, new ArtFrameSelection(0, 0, 0)));

        foreach (var payload in new[] { new byte[] { 0, 0 }, new byte[] { 4 }, new byte[] { 0x84, 1 }, new byte[] { 5, 1 }, new byte[] { 3, 1 }, new byte[] { 4, 1, 1 } })
        {
            var malformed = Fixture.Create(staticFlag: true, framesPerGroup: 1, [new Frame(2, 2, payload)], new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 0)) });
            Throws<ArtFormatException>(() => ArtDecoder.Decode(malformed, new ArtFrameSelection(0, 0, 0)));
        }
    }

    [TestMethod]
    public void Decode_handles_count_127_and_runs_crossing_row_boundaries()
    {
        var count127 = Fixture.Create(staticFlag: true, framesPerGroup: 1,
            [new Frame(127, 1, [127, 1])],
            new Dictionary<int, byte[]> { [0] = PaletteEntries((1, 2, 3, 4, 0)) });
        var count127Result = ArtDecoder.Decode(count127, new ArtFrameSelection(0, 0, 0));
        CollectionAssert.AreEqual(RepeatRgba(127, 4, 3, 2, 255), count127Result.Rgba.ToArray());

        var crossingRows = Fixture.Create(staticFlag: true, framesPerGroup: 1,
            [new Frame(3, 2, [4, 1, 2, 2])],
            new Dictionary<int, byte[]> { [0] = PaletteEntries((1, 10, 20, 30, 0), (2, 40, 50, 60, 0)) });
        var crossingResult = ArtDecoder.Decode(crossingRows, new ArtFrameSelection(0, 0, 0));
        CollectionAssert.AreEqual(new byte[]
        {
            30, 20, 10, 255, 30, 20, 10, 255, 30, 20, 10, 255,
            30, 20, 10, 255, 60, 50, 40, 255, 60, 50, 40, 255,
        }, crossingResult.Rgba.ToArray());
    }

    [TestMethod]
    public void Decode_rejects_structure_unsupported_format_and_policy_limits()
    {
        var valid = Fixture.Create(staticFlag: true, framesPerGroup: 1, [new Frame(1, 1, [1])], new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 0)) });
        Throws<ArtFormatException>(() => ArtDecoder.Decode(valid[..131], new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithTrailingByte(valid), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtUnsupportedFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 8, 7), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 32, 0), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 32, -1), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 132 + 1024 + 8, 0), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 132 + 1024, -1), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 132 + 1024 + 4, -1), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 132 + 1024 + 8, -1), new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(valid[..(132 + 1024 - 1)], new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(valid[..(132 + 1024 + 28 - 1)], new ArtFrameSelection(0, 0, 0)));
        Throws<ArtFormatException>(() => ArtDecoder.Decode(valid[..^1], new ArtFrameSelection(0, 0, 0)));

        var dimensions = Fixture.Create(staticFlag: true, framesPerGroup: 1, [new Frame(2, 2, [4, 1])], new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 0)) });
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(dimensions, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxDimension = 1 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(dimensions, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxFramePixels = 3 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(dimensions, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxRgbaBytes = 15 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(dimensions, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxEncodedFrameBytes = 1 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxInputBytes = 1 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(Fixture.WithI32(valid, 32, 2), new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxFrameCount = 1 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(Fixture.Create(staticFlag: false, framesPerGroup: 1, Enumerable.Repeat(new Frame(1, 1, [1]), 8).ToList(), new Dictionary<int, byte[]> { [0] = Palette((1, 2, 3, 0)) }), new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxFrameCount = 7 }));
        Throws<ArtLimitExceededException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxFrameTableBytes = 27 }));
        var overflow = Fixture.WithI32(Fixture.WithI32(valid, 132 + 1024, int.MaxValue), 132 + 1024 + 4, 2);
        Throws<ArtFormatException>(() => ArtDecoder.Decode(overflow, new ArtFrameSelection(0, 0, 0), new ArtDecoderOptions { MaxDimension = int.MaxValue, MaxFramePixels = int.MaxValue }));

        var optionProperties = typeof(ArtDecoderOptions).GetProperties().Where(property => property.PropertyType == typeof(int));
        foreach (var property in optionProperties)
        {
            var options = new ArtDecoderOptions();
            property.SetValue(options, 0);
            Throws<ArgumentOutOfRangeException>(() => ArtDecoder.Decode(valid, new ArtFrameSelection(0, 0, 0), options));
        }
    }

    private static byte[] Palette(params (byte B, byte G, byte R, byte X)[] colors)
    {
        var table = new byte[1024];
        for (var index = 0; index < colors.Length; index++)
        {
            (table[index * 4], table[(index * 4) + 1], table[(index * 4) + 2], table[(index * 4) + 3]) = colors[index];
        }

        return table;
    }

    private static byte[] PaletteEntries(params (int Index, byte B, byte G, byte R, byte X)[] colors)
    {
        var table = new byte[1024];
        foreach (var color in colors)
        {
            (table[color.Index * 4], table[(color.Index * 4) + 1], table[(color.Index * 4) + 2], table[(color.Index * 4) + 3]) =
                (color.B, color.G, color.R, color.X);
        }

        return table;
    }

    private static byte[] RepeatRgba(int count, byte red, byte green, byte blue, byte alpha)
    {
        var rgba = new byte[count * 4];
        for (var index = 0; index < count; index++)
        {
            var offset = index * 4;
            rgba[offset] = red;
            rgba[offset + 1] = green;
            rgba[offset + 2] = blue;
            rgba[offset + 3] = alpha;
        }

        return rgba;
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception.GetType() == typeof(TException))
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private sealed record Frame(int Width, int Height, byte[] Payload, int HotX = 0, int HotY = 0, int OffsetX = 0, int OffsetY = 0);

    private static class Fixture
    {
        internal static byte[] Create(bool staticFlag, int framesPerGroup, IReadOnlyList<Frame> frames, IReadOnlyDictionary<int, byte[]> palettes,
            uint flags = 0, int fps = 0, int actionFrame = 0, uint rawWord68 = 0)
        {
            var groups = staticFlag ? 1 : 8;
            Assert.AreEqual(groups * framesPerGroup, frames.Count);
            var paletteBytes = palettes.Count * 1024;
            var tableOffset = 132 + paletteBytes;
            var payloadOffset = tableOffset + (frames.Count * 28);
            var bytes = new byte[payloadOffset + frames.Sum(frame => frame.Payload.Length)];
            WriteU32(bytes, 0, flags | (staticFlag ? 1u : 0u));
            WriteI32(bytes, 4, fps);
            WriteI32(bytes, 8, 8);
            WriteI32(bytes, 28, actionFrame);
            WriteI32(bytes, 32, framesPerGroup);
            WriteU32(bytes, 68, rawWord68);

            var paletteCursor = 132;
            for (var slot = 0; slot < 4; slot++)
            {
                if (!palettes.TryGetValue(slot, out var table)) continue;
                Assert.AreEqual(1024, table.Length);
                WriteU32(bytes, 12 + (slot * 4), unchecked((uint)(slot + 10)));
                table.CopyTo(bytes, paletteCursor);
                paletteCursor += 1024;
            }

            var payloadCursor = payloadOffset;
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                var header = tableOffset + (index * 28);
                WriteI32(bytes, header, frame.Width);
                WriteI32(bytes, header + 4, frame.Height);
                WriteI32(bytes, header + 8, frame.Payload.Length);
                WriteI32(bytes, header + 12, frame.HotX);
                WriteI32(bytes, header + 16, frame.HotY);
                WriteI32(bytes, header + 20, frame.OffsetX);
                WriteI32(bytes, header + 24, frame.OffsetY);
                frame.Payload.CopyTo(bytes, payloadCursor);
                payloadCursor += frame.Payload.Length;
            }

            return bytes;
        }

        internal static byte[] WithI32(byte[] source, int offset, int value)
        {
            var copy = source.ToArray();
            WriteI32(copy, offset, value);
            return copy;
        }

        internal static byte[] WithTrailingByte(byte[] source)
        {
            var copy = new byte[source.Length + 1];
            source.CopyTo(copy, 0);
            return copy;
        }

        private static void WriteI32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        private static void WriteU32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    }
}
