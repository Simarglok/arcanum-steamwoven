using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;
using Godot;

namespace Arcanum.Godot;

internal static class SyntheticFixture
{
    private const int ArtHeaderBytes = 132;
    private const int PaletteBytes = 1024;
    private const int FrameHeaderBytes = 28;

    internal static byte[] CreateDat() => CreateDat(
        ("duplicate.art", CreateStaticArt()),
        ("duplicate.art", CreateDirectionalArt()),
        ("fallback.art", CreateStaticArt()),
        ("missing-zero.art", CreateMissingZeroArt()),
        ("malformed.art", CreateStaticArt()[..^1]));

    internal static byte[] CreateOversizeDat() => CreateDat(
        ("oversize.art", new byte[checked((int)ViewerState.EntryCapBytes + 1)]));

    internal static byte[] CreateStaticArt() => CreateArt(
        staticFlag: true,
        palettes: new Dictionary<int, byte[]>
        {
            [0] = Palette((1, 3, 2, 1), (2, 6, 5, 4), (3, 9, 8, 7), (4, 12, 11, 10), (5, 15, 14, 13)),
            [2] = Palette((1, 33, 32, 31), (2, 36, 35, 34), (3, 39, 38, 37), (4, 42, 41, 40), (5, 45, 44, 43)),
        },
        new ArtFixtureFrame(3, 2, [0, 1, 2, 3, 4, 5]),
        new ArtFixtureFrame(2, 2, [0x84, 1, 2, 3, 4]));

    internal static byte[] CreateMissingZeroArt() => CreateArt(
        staticFlag: true,
        palettes: new Dictionary<int, byte[]> { [2] = Palette((1, 33, 32, 31), (2, 36, 35, 34)) },
        new ArtFixtureFrame(2, 1, [1, 2]));

    private static byte[] CreateDirectionalArt()
    {
        var frames = new List<ArtFixtureFrame>();
        for (var group = 0; group < 8; group++)
        {
            frames.Add(new ArtFixtureFrame(1, 1, [(byte)((group % 4) + 1)]));
            frames.Add(new ArtFixtureFrame(2, 1, [2, (byte)(((group + 1) % 4) + 1)]));
        }

        return CreateArt(staticFlag: false, new Dictionary<int, byte[]>
        {
            [0] = Palette((1, 3, 2, 1), (2, 6, 5, 4), (3, 9, 8, 7), (4, 12, 11, 10)),
            [1] = Palette((1, 63, 62, 61), (2, 66, 65, 64), (3, 69, 68, 67), (4, 72, 71, 70)),
        }, frames.ToArray());
    }

    private static byte[] CreateArt(bool staticFlag, IReadOnlyDictionary<int, byte[]> palettes, params ArtFixtureFrame[] frames)
    {
        var groups = staticFlag ? 1 : 8;
        if (frames.Length == 0 || frames.Length % groups != 0)
        {
            throw new ArgumentException("ART fixture frames must fill every physical group.", nameof(frames));
        }

        var framesPerGroup = frames.Length / groups;
        var paletteBytes = palettes.Count * PaletteBytes;
        var payloadBytes = frames.Sum(frame => frame.Payload.Length);
        var tableOffset = ArtHeaderBytes + paletteBytes;
        var payloadOffset = tableOffset + (frames.Length * FrameHeaderBytes);
        var art = new byte[payloadOffset + payloadBytes];
        WriteI32(art, 0, staticFlag ? 1 : 0);
        WriteI32(art, 8, 8);
        WriteI32(art, 32, framesPerGroup);

        var paletteCursor = ArtHeaderBytes;
        for (var slot = 0; slot <= ViewerState.MaxRequestedPaletteSlot; slot++)
        {
            if (!palettes.TryGetValue(slot, out var palette))
            {
                continue;
            }

            if (palette.Length != PaletteBytes)
            {
                throw new ArgumentException("ART fixture palettes must contain 1,024 bytes.", nameof(palettes));
            }

            WriteI32(art, 12 + (slot * 4), slot + 1);
            palette.CopyTo(art, paletteCursor);
            paletteCursor += PaletteBytes;
        }

        var payloadCursor = payloadOffset;
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = frames[index];
            var frameOffset = tableOffset + (index * FrameHeaderBytes);
            WriteI32(art, frameOffset, frame.Width);
            WriteI32(art, frameOffset + 4, frame.Height);
            WriteI32(art, frameOffset + 8, frame.Payload.Length);
            frame.Payload.CopyTo(art, payloadCursor);
            payloadCursor += frame.Payload.Length;
        }

        return art;
    }

    private static byte[] CreateDat(params (string Name, byte[] Payload)[] entries)
    {
        using var output = new MemoryStream();
        var offsets = new uint[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            offsets[index] = checked((uint)output.Position);
            output.Write(entries[index].Payload);
        }

        using var indexStream = new MemoryStream();
        WriteU32(indexStream, checked((uint)entries.Length));
        foreach (var (entry, ordinal) in entries.Select((entry, ordinal) => (entry, ordinal)))
        {
            var name = Encoding.ASCII.GetBytes(entry.Name + "\0");
            WriteU32(indexStream, checked((uint)name.Length));
            indexStream.Write(name);
            indexStream.Write(new byte[4]);
            WriteU32(indexStream, 1);
            WriteU32(indexStream, checked((uint)entry.Payload.Length));
            WriteU32(indexStream, checked((uint)entry.Payload.Length));
            WriteU32(indexStream, offsets[ordinal]);
        }

        indexStream.Position = 0;
        indexStream.CopyTo(output);
        output.Write(new byte[16]);
        output.Write("1TAD"u8);
        WriteU32(output, checked((uint)entries.Sum(entry => entry.Name.Length + 1)));
        WriteU32(output, checked((uint)(indexStream.Length + 28)));
        return output.ToArray();
    }

    private static byte[] Palette(params (int Index, byte Blue, byte Green, byte Red)[] entries)
    {
        var palette = new byte[PaletteBytes];
        foreach (var entry in entries)
        {
            var offset = entry.Index * 4;
            palette[offset] = entry.Blue;
            palette[offset + 1] = entry.Green;
            palette[offset + 2] = entry.Red;
        }

        return palette;
    }

    private static void WriteI32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record ArtFixtureFrame(int Width, int Height, byte[] Payload);
}

internal static class GodotSelfTest
{
    internal static string Run()
    {
        var dat = SyntheticFixture.CreateDat();
        using var archive = DatArchive.Open(new MemoryStream(dat), ViewerState.ReaderOptions, leaveOpen: true);
        var entries = ViewerState.ArtEntries(archive);
        Require(entries.Count == 5, "synthetic ART entry count");
        Require(entries[0].DisplayName == "duplicate.art [ordinal 0]" && entries[1].DisplayName == "duplicate.art [ordinal 1]", "duplicate ordinal identity");

        var raw = ArtDecoder.Decode(archive.ReadEntry(entries[0].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions);
        var expected = new byte[]
        {
            0, 0, 0, 0, 31, 32, 33, 255, 34, 35, 36, 255,
            37, 38, 39, 255, 40, 41, 42, 255, 43, 44, 45, 255,
        };
        Require(raw.Width == 3 && raw.Height == 2 && raw.Frame.EncodedPayloadLength == 6, "raw frame metadata");
        Require(raw.Rgba.SequenceEqual(expected), "decoder raw RGBA byte order or orientation");

        var rle = ArtDecoder.Decode(archive.ReadEntry(entries[0].Entry), new ArtFrameSelection(0, 1, 2), ViewerState.DecoderOptions);
        var directional = ArtDecoder.Decode(archive.ReadEntry(entries[1].Entry), new ArtFrameSelection(7, 1, 1), ViewerState.DecoderOptions);
        Require(rle.Width == 2 && rle.Height == 2 && rle.Frame.EncodedPayloadLength == 5, "RLE frame selection");
        Require(directional.GroupIndex == 7 && directional.FrameIndex == 1 && directional.Header.PhysicalGroupCount == 8, "physical group selection");

        RequireThrows<ArtFormatException>(() => ArtDecoder.Decode(archive.ReadEntry(entries[3].Entry), new ArtFrameSelection(0, 0, 0), ViewerState.DecoderOptions), "missing slot zero");
        var groupSelector = new SpinBox();
        var frameSelector = new SpinBox();
        var paletteSelector = new SpinBox();
        ViewerState.ResetEntrySelectors(groupSelector, frameSelector, paletteSelector);
        Require(groupSelector.MaxValue == 0 && frameSelector.MaxValue == 0 && paletteSelector.MaxValue == ViewerState.MaxRequestedPaletteSlot, "viewer palette zero absent selector recovery");
        groupSelector.Free();
        frameSelector.Free();
        paletteSelector.Free();
        var recovered = ArtDecoder.Decode(archive.ReadEntry(entries[3].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions);
        Require(recovered.EffectivePaletteSlot == 2 && ViewerState.MaxRequestedPaletteSlot == 3, "palette zero absent recovery slots 0-3");
        RequireThrows<ArtFormatException>(() => ArtDecoder.Decode(archive.ReadEntry(entries[4].Entry), new ArtFrameSelection(0, 0, 0), ViewerState.DecoderOptions), "malformed synthetic ART");
        Require(ArtDecoder.Decode(archive.ReadEntry(entries[0].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions).Rgba.SequenceEqual(expected), "valid-invalid-valid recovery");

        using var oversize = DatArchive.Open(new MemoryStream(SyntheticFixture.CreateOversizeDat()), ViewerState.ReaderOptions, leaveOpen: true);
        RequireThrows<DatLimitExceededException>(() => oversize.ReadEntry(oversize.Entries[0]), "oversize viewer entry limit");

        using var image = Image.CreateFromData(raw.Width, raw.Height, false, Image.Format.Rgba8, raw.Rgba.ToArray());
        using var texture = ImageTexture.CreateFromImage(image);
        using var textureImage = texture.GetImage();
        Require(image.GetData().SequenceEqual(expected) && textureImage.GetData().SequenceEqual(expected), "Godot Image/ImageTexture bytes");
        Require(texture.GetSize() == new Vector2I(3, 2), "texture aspect");
        var topLeft = textureImage.GetPixel(0, 0);
        var topRight = textureImage.GetPixel(2, 0);
        var bottomLeft = textureImage.GetPixel(0, 1);
        Require(Mathf.IsZeroApprox(topLeft.A) && Mathf.IsEqualApprox(topRight.R, 34f / 255f) && Mathf.IsEqualApprox(bottomLeft.R, 37f / 255f), "top-left row-major orientation");

        var canvas = new FrameCanvas { Size = new Vector2(30, 20) };
        Require(canvas.TextureFilter == Control.TextureFilterEnum.Nearest && !canvas.HasTexture && Mathf.IsEqualApprox(canvas.Zoom, 1f), "FrameCanvas nearest initial state");
        canvas.SetTexture(texture);
        canvas.AdjustZoom(2f, new Vector2(15, 10));
        Require(canvas.HasTexture && Mathf.IsEqualApprox(canvas.Zoom, 2f) && !canvas.FitMode, "FrameCanvas zoom state");
        Require(Mathf.IsEqualApprox(FrameCanvas.CalculateFitZoom(new Vector2(30, 20), texture.GetSize()) ?? 0f, 10f), "FrameCanvas fit calculation");
        canvas.ResetOneToOne();
        Require(!canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 1f), "FrameCanvas one-to-one reset state");
        canvas.ClearFrame();
        Require(!canvas.HasTexture && !canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 1f), "FrameCanvas clear state");
        canvas.Free();

        return "Godot synthetic DAT/ART self-test passed: duplicate ordinals, raw/RLE, groups/frames, palettes/fallback, valid-invalid-valid recovery, limits, RGBA8 texture, and FrameCanvas state.";
    }

    private static void RequireThrows<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Self-test failed: {name}.");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test failed: {name}.");
        }
    }
}