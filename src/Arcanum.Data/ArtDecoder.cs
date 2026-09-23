using System.Buffers.Binary;

namespace Arcanum.Data;

public static class ArtDecoder
{
    private const int HeaderBytes = 132;
    private const int PaletteBytes = 256 * 4;
    private const int FrameHeaderBytes = 28;

    public static ArtFrame Decode(ReadOnlySpan<byte> artBytes, ArtFrameSelection selection, ArtDecoderOptions? options = null)
    {
        options ??= new ArtDecoderOptions();
        options.Validate();
        if (selection.PaletteSlot is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(selection), "Palette slot must be from 0 through 3.");
        }

        if (selection.GroupIndex < 0 || selection.FrameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selection), "Group and frame indexes must be non-negative.");
        }

        if (artBytes.Length > options.MaxInputBytes)
        {
            throw new ArtLimitExceededException("ART input exceeds the configured byte limit.");
        }

        if (artBytes.Length < HeaderBytes)
        {
            throw new ArtFormatException("ART header is truncated.");
        }

        var header = ParseHeader(artBytes, options, out var totalFrames);
        if (selection.GroupIndex >= header.PhysicalGroupCount || selection.FrameIndex >= header.FramesPerGroup)
        {
            throw new ArgumentOutOfRangeException(nameof(selection), "Group or frame index is outside the ART physical frame table.");
        }

        var paletteOffset = HeaderBytes;
        var palettes = ReadPalettes(artBytes, header, ref paletteOffset);
        var effectivePaletteSlot = palettes[selection.PaletteSlot] is not null ? selection.PaletteSlot : 0;
        var palette = palettes[effectivePaletteSlot] ?? throw new ArtFormatException("The requested palette and fallback slot 0 are absent.");

        var tableBytes = CheckedMultiply(totalFrames, FrameHeaderBytes, "frame-table byte count");
        if (tableBytes > options.MaxFrameTableBytes)
        {
            throw new ArtLimitExceededException("ART frame table exceeds the configured byte limit.");
        }

        var tableOffset = paletteOffset;
        var payloadStart = CheckedAdd(tableOffset, tableBytes, "frame-table end");
        EnsureRange(artBytes, tableOffset, tableBytes, "ART palette or frame table is truncated.");

        var frames = new ParsedFrame[totalFrames];
        var payloadOffset = payloadStart;
        for (var index = 0; index < totalFrames; index++)
        {
            var frameOffset = CheckedAdd(tableOffset, CheckedMultiply(index, FrameHeaderBytes, "frame header offset"), "frame header offset");
            var frame = ParseFrame(artBytes, frameOffset, options);
            frames[index] = frame;
            payloadOffset = CheckedAdd(payloadOffset, frame.EncodedPayloadLength, "frame payload end");
            EnsureRange(artBytes, payloadStart, payloadOffset - payloadStart, "ART frame payload is truncated.");
        }

        if (payloadOffset != artBytes.Length)
        {
            throw new ArtFormatException("ART frame payloads do not consume the input exactly.");
        }

        var linearIndex = CheckedAdd(CheckedMultiply(selection.GroupIndex, header.FramesPerGroup, "selected frame index"), selection.FrameIndex, "selected frame index");
        var selected = frames[linearIndex];
        var selectedPayloadOffset = payloadStart;
        for (var index = 0; index < linearIndex; index++)
        {
            selectedPayloadOffset = CheckedAdd(selectedPayloadOffset, frames[index].EncodedPayloadLength, "selected payload offset");
        }

        var rgba = new byte[checked((int)selected.RgbaBytes)];
        var payload = artBytes.Slice(selectedPayloadOffset, selected.EncodedPayloadLength);
        if (selected.EncodedPayloadLength == selected.PixelCount)
        {
            for (var index = 0; index < selected.PixelCount; index++)
            {
                WriteRgba(payload[index], palette.TableSpan, rgba, index);
            }
        }
        else
        {
            DecodeRle(payload, selected.PixelCount, palette.TableSpan, rgba);
        }

        var rawFrameHeader = artBytes.Slice(selected.HeaderOffset, FrameHeaderBytes).ToArray();
        var metadata = new ArtFrameMetadata(selected.Width, selected.Height, selected.EncodedPayloadLength,
            selected.HotX, selected.HotY, selected.OffsetX, selected.OffsetY, rawFrameHeader);
        return new ArtFrame(header, palettes, metadata, selection.GroupIndex, selection.FrameIndex,
            selection.PaletteSlot, effectivePaletteSlot, rgba);
    }

    private static ArtHeader ParseHeader(ReadOnlySpan<byte> bytes, ArtDecoderOptions options, out int totalFrames)
    {
        var raw = bytes[..HeaderBytes].ToArray();
        var flags = U32(raw, 0);
        var bpp = I32(raw, 8);
        if (bpp != 8)
        {
            throw new ArtUnsupportedFormatException("Only 8-bpp ART entries are supported.");
        }

        var framesPerGroup = I32(raw, 32);
        if (framesPerGroup <= 0)
        {
            throw new ArtFormatException("ART frames-per-group must be positive.");
        }

        var groupCount = (flags & 1) != 0 ? 1 : 8;
        var frameTotal = CheckedMultiply(groupCount, framesPerGroup, "total frame count");
        if (frameTotal > options.MaxFrameCount)
        {
            throw new ArtLimitExceededException("ART total frames exceed the configured limit.");
        }

        totalFrames = frameTotal;
        return new ArtHeader(raw, flags, I32(raw, 4), bpp,
            ReadWords(raw, 12, 4), I32(raw, 28), framesPerGroup,
            ReadWords(raw, 36, 8), ReadWords(raw, 68, 8), ReadWords(raw, 100, 8), groupCount);
    }

    private static ArtPalette?[] ReadPalettes(ReadOnlySpan<byte> bytes, ArtHeader header, ref int offset)
    {
        var palettes = new ArtPalette?[4];
        for (var slot = 0; slot < palettes.Length; slot++)
        {
            var presenceWord = header.PalettePresenceWords[slot];
            if (presenceWord == 0)
            {
                continue;
            }

            EnsureRange(bytes, offset, PaletteBytes, "ART palette table is truncated.");
            palettes[slot] = new ArtPalette(slot, presenceWord, bytes.Slice(offset, PaletteBytes).ToArray());
            offset = CheckedAdd(offset, PaletteBytes, "palette table end");
        }

        return palettes;
    }

    private static ParsedFrame ParseFrame(ReadOnlySpan<byte> bytes, int offset, ArtDecoderOptions options)
    {
        var width = I32(bytes, offset);
        var height = I32(bytes, offset + 4);
        var encodedPayloadLength = I32(bytes, offset + 8);
        if (width <= 0 || height <= 0 || encodedPayloadLength <= 0)
        {
            throw new ArtFormatException("ART frame dimensions and encoded payload length must be positive.");
        }

        if (width > options.MaxDimension || height > options.MaxDimension)
        {
            throw new ArtLimitExceededException("ART frame dimension exceeds the configured limit.");
        }

        var pixels = CheckedMultiply(width, height, "frame pixel count");
        if (pixels > options.MaxFramePixels)
        {
            throw new ArtLimitExceededException("ART frame pixels exceed the configured limit.");
        }

        var rgbaBytes = CheckedMultiply(pixels, 4, "RGBA byte count");
        if (rgbaBytes > options.MaxRgbaBytes)
        {
            throw new ArtLimitExceededException("ART RGBA bytes exceed the configured limit.");
        }

        if (encodedPayloadLength > options.MaxEncodedFrameBytes)
        {
            throw new ArtLimitExceededException("ART encoded frame bytes exceed the configured limit.");
        }

        return new ParsedFrame(offset, width, height, encodedPayloadLength, pixels, rgbaBytes,
            I32(bytes, offset + 12), I32(bytes, offset + 16), I32(bytes, offset + 20), I32(bytes, offset + 24));
    }

    private static void DecodeRle(ReadOnlySpan<byte> payload, int pixels, ReadOnlySpan<byte> palette, byte[] rgba)
    {
        var input = 0;
        var output = 0;
        while (input < payload.Length)
        {
            var control = payload[input++];
            var count = control & 0x7f;
            if (count == 0 || count > pixels - output)
            {
                throw new ArtFormatException("ART RLE control count is invalid or overruns the frame.");
            }

            if ((control & 0x80) != 0)
            {
                if (count > payload.Length - input)
                {
                    throw new ArtFormatException("ART RLE literal run is truncated.");
                }

                for (var index = 0; index < count; index++)
                {
                    WriteRgba(payload[input++], palette, rgba, output++);
                }
            }
            else
            {
                if (input == payload.Length)
                {
                    throw new ArtFormatException("ART RLE repeated run is truncated.");
                }

                var value = payload[input++];
                for (var index = 0; index < count; index++)
                {
                    WriteRgba(value, palette, rgba, output++);
                }
            }
        }

        if (output != pixels)
        {
            throw new ArtFormatException("ART RLE output does not match the frame dimensions.");
        }
    }

    private static void WriteRgba(byte paletteIndex, ReadOnlySpan<byte> palette, byte[] rgba, int pixelOffset)
    {
        var destination = pixelOffset * 4;
        if (paletteIndex == 0)
        {
            return;
        }

        var source = paletteIndex * 4;
        rgba[destination] = palette[source + 2];
        rgba[destination + 1] = palette[source + 1];
        rgba[destination + 2] = palette[source];
        rgba[destination + 3] = byte.MaxValue;
    }

    private static uint[] ReadWords(ReadOnlySpan<byte> bytes, int offset, int count)
    {
        var words = new uint[count];
        for (var index = 0; index < count; index++)
        {
            words[index] = U32(bytes, offset + (index * sizeof(uint)));
        }

        return words;
    }

    private static int I32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static int CheckedMultiply(int left, int right, string label)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException exception)
        {
            throw new ArtFormatException($"ART {label} overflows.", exception);
        }
    }

    private static int CheckedAdd(int left, int right, string label)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException exception)
        {
            throw new ArtFormatException($"ART {label} overflows.", exception);
        }
    }

    private static void EnsureRange(ReadOnlySpan<byte> bytes, int offset, int length, string message)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new ArtFormatException(message);
        }
    }

    private readonly record struct ParsedFrame(int HeaderOffset, int Width, int Height, int EncodedPayloadLength,
        int PixelCount, int RgbaBytes, int HotX, int HotY, int OffsetX, int OffsetY);
}
