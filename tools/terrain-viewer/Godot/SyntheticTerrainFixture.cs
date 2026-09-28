using System.Buffers.Binary;

namespace Arcanum.TerrainViewer;

/// <summary>Public asymmetric 8-bpp ART fixture. Palette zero is transparent.</summary>
public static class SyntheticTerrainFixture
{
    public const int Width = 82;
    public const int Height = 42;

    public static byte[] CreateArt()
    {
        const int headerBytes = 132;
        const int paletteBytes = 256 * 4;
        const int frameHeaderBytes = 28;
        var pixels = new byte[Width * Height];
        Array.Fill(pixels, (byte)4); // white interior makes footprint/crop edges observable.
        pixels[0] = 0;
        pixels[1] = 1; // blue at (1,0)
        pixels[79] = 2; // green right edge of the 80x40 facade footprint
        pixels[(Height - 1) * Width] = 3; // red lower-left edge
        pixels[(Height - 1) * Width + Width - 1] = 4;
        pixels[(5 * Width) + 5] = 1;
        pixels[(2 * Width) + 2] = 3;
        pixels[(2 * Width) + 3] = 2;

        var bytes = new byte[headerBytes + paletteBytes + frameHeaderBytes + pixels.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        SetPalette(bytes, 1, 255, 0, 0); // B,G,R => blue
        SetPalette(bytes, 2, 0, 255, 0); // green
        SetPalette(bytes, 3, 0, 0, 255); // red
        SetPalette(bytes, 4, 255, 255, 255); // white
        var frame = headerBytes + paletteBytes;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), Height);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), pixels.Length);
        pixels.CopyTo(bytes, frame + frameHeaderBytes);
        return bytes;
    }

    private static void SetPalette(byte[] bytes, int index, byte blue, byte green, byte red)
    {
        var offset = 132 + (index * 4);
        bytes[offset] = blue;
        bytes[offset + 1] = green;
        bytes[offset + 2] = red;
        bytes[offset + 3] = 255;
    }
}
