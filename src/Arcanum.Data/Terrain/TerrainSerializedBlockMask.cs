using System.Buffers.Binary;

namespace Arcanum.Data.Terrain;

/// <summary>
/// An owned, exact-size 64 by 64 serialized terrain block-mask snapshot. This component alone
/// does not establish actor occupancy or traversal. A set bit is Blocked and a clear bit is NoBlock.
/// </summary>
public sealed class TerrainSerializedBlockMask
{
    public const int Width = 64;
    public const int Height = 64;
    public const int CellCount = Width * Height;
    public const int ByteLength = 512;

    private readonly uint[] _words;

    private TerrainSerializedBlockMask(uint[] words) => _words = words;

    internal uint[] CopyWords() => _words.ToArray();

    /// <summary>Parses exactly 128 little-endian UInt32 words and owns the resulting snapshot.</summary>
    public static TerrainSerializedBlockMask Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
            throw new ArgumentException("A serialized terrain block mask must contain exactly 512 bytes.", nameof(bytes));

        var words = new uint[ByteLength / sizeof(uint)];
        for (var index = 0; index < words.Length; index++)
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(checked(index * sizeof(uint)), sizeof(uint)));
        return new TerrainSerializedBlockMask(words);
    }

    /// <summary>Returns this component's disposition for a checked local 64 by 64 coordinate.</summary>
    public TerrainObstructionDisposition GetDisposition(int localX, int localY)
    {
        if ((uint)localX >= Width)
            throw new ArgumentOutOfRangeException(nameof(localX), "Local terrain X must be in the inclusive range 0 through 63.");
        if ((uint)localY >= Height)
            throw new ArgumentOutOfRangeException(nameof(localY), "Local terrain Y must be in the inclusive range 0 through 63.");
        return GetDispositionByOrdinal(checked(localX + (Width * localY)));
    }

    /// <summary>Returns this component's disposition for a checked row-major ordinal x + 64*y.</summary>
    public TerrainObstructionDisposition GetDispositionByOrdinal(int ordinal)
    {
        if ((uint)ordinal >= CellCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Terrain block-mask ordinal must be in the inclusive range 0 through 4095.");
        return (_words[ordinal / 32] & (1u << (ordinal % 32))) != 0
            ? TerrainObstructionDisposition.Blocked
            : TerrainObstructionDisposition.NoBlock;
    }
}
