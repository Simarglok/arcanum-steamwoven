using System.Buffers.Binary;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Public, synthetic 8-bpp ART fixture: palette index zero is transparent and remaining entries are asymmetric.</summary>
public static class SyntheticTerrainFixture
{
    public static byte[] CreateArt()
    {
        const int width = 6;
        const int height = 5;
        const int headerBytes = 132;
        const int paletteBytes = 256 * 4;
        const int frameHeaderBytes = 28;
        var pixels = new byte[]
        {
            0, 1, 2, 3, 4, 1,
            3, 2, 1, 4, 0, 2,
            2, 4, 3, 1, 2, 0,
            1, 3, 4, 2, 1, 3,
            4, 0, 2, 3, 1, 4,
        };
        var bytes = new byte[headerBytes + paletteBytes + frameHeaderBytes + pixels.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); // one physical group
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        // Decoder consumes palette entries as B,G,R,A and writes straight R,G,B,A.
        SetPalette(bytes, 1, 255, 0, 0); // blue
        SetPalette(bytes, 2, 0, 255, 0); // green
        SetPalette(bytes, 3, 0, 0, 255); // red
        SetPalette(bytes, 4, 255, 255, 255); // white X marker
        var frame = headerBytes + paletteBytes;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), height);
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