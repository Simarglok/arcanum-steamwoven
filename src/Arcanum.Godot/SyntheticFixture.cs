using System.Buffers.Binary;
using Arcanum.Data;
using Godot;

namespace Arcanum.Godot;

internal static class SyntheticFixture
{
    public static byte[] CreateArt()
    {
        var header = new byte[132];
        WriteI32(header, 0, 1); // one physical group
        WriteI32(header, 8, 8); // bpp
        WriteI32(header, 12, 1); // palette slot 0 is present
        WriteI32(header, 20, 1); // palette slot 2 is present; slot 1 is a gap
        WriteI32(header, 32, 1); // one frame in the physical group

        var palette0 = Palette((1, 3, 2, 1), (2, 6, 5, 4), (3, 9, 8, 7), (4, 12, 11, 10), (5, 15, 14, 13));
        var palette2 = Palette((1, 33, 32, 31), (2, 36, 35, 34), (3, 39, 38, 37), (4, 42, 41, 40), (5, 45, 44, 43));
        var frameHeader = new byte[28];
        WriteI32(frameHeader, 0, 3);
        WriteI32(frameHeader, 4, 2);
        WriteI32(frameHeader, 8, 6);
        return header.Concat(palette0).Concat(palette2).Concat(frameHeader).Concat(new byte[] { 0, 1, 2, 3, 4, 5 }).ToArray();
    }

    private static byte[] Palette(params (int Index, byte Blue, byte Green, byte Red)[] entries)
    {
        var palette = new byte[1024];
        foreach (var entry in entries)
        {
            var offset = entry.Index * 4;
            palette[offset] = entry.Blue;
            palette[offset + 1] = entry.Green;
            palette[offset + 2] = entry.Red;
        }

        return palette;
    }

    private static void WriteI32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}

internal static class GodotSelfTest
{
    public static string Run()
    {
        var art = SyntheticFixture.CreateArt();
        var frame = ArtDecoder.Decode(art, new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions);
        var expected = new byte[]
        {
            0, 0, 0, 0, 31, 32, 33, 255, 34, 35, 36, 255,
            37, 38, 39, 255, 40, 41, 42, 255, 43, 44, 45, 255,
        };
        Require(frame.Width == 3 && frame.Height == 2, "dimensions");
        Require(frame.RequestedPaletteSlot == 2 && frame.EffectivePaletteSlot == 2, "palette switch");
        Require(frame.Rgba.SequenceEqual(expected), "decoder RGBA byte order or orientation");

        var fallback = ArtDecoder.Decode(art, new ArtFrameSelection(0, 0, 1), ViewerState.DecoderOptions);
        Require(fallback.EffectivePaletteSlot == 0, "slot-zero fallback");
        Require(fallback.Rgba[3] == 0 && fallback.Rgba[7] == 255, "transparent index zero / opaque nonzero index");

        using var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba.ToArray());
        using var texture = ImageTexture.CreateFromImage(image);
        using var textureImage = texture.GetImage();
        Require(image.GetData().SequenceEqual(expected), "Godot Image bytes");
        Require(textureImage.GetData().SequenceEqual(expected), "Godot ImageTexture bytes");
        Require(texture.GetSize() == new Vector2I(3, 2), "texture aspect");
        var topLeft = textureImage.GetPixel(0, 0);
        var topRight = textureImage.GetPixel(2, 0);
        var bottomLeft = textureImage.GetPixel(0, 1);
        Require(Mathf.IsZeroApprox(topLeft.A) && Mathf.IsEqualApprox(topRight.R, 34f / 255f) && Mathf.IsEqualApprox(bottomLeft.R, 37f / 255f), "top-left row-major orientation");
        return "Godot synthetic RGBA8 self-test passed: 3x2, palette 2, fallback 0, alpha, byte order, orientation, nearest 1:1 state.";
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test failed: {name}.");
        }
    }
}
