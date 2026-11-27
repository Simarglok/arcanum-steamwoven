namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-3427: the textures the carried lights' lattice is written into. The lattice covers the lights where they stand, so its
/// columns and rows change as a light moves by single pixels; the shader reads only the nodes inside the lattice's bounds, so the textures may be
/// larger and stay the same while the lattice fits them.</summary>
public static class AreaCarriedTexels
{
    /// <summary>Each side of the textures is a multiple of this many nodes.</summary>
    public const int Step = 16;
    /// <summary>How many times larger than needed a side may stay.</summary>
    public const int Slack = 4;
    private const int BytesPerNode = 4;

    /// <summary>The texture size for a lattice of <paramref name="columns"/> by <paramref name="rows"/> nodes when the shown textures are
    /// <paramref name="shownWidth"/> by <paramref name="shownHeight"/> (zero: none yet). A side stays while it holds the lattice and is at most
    /// <see cref="Slack"/> times the next multiple of <see cref="Step"/> that would; otherwise it becomes that multiple. So a lattice that changes
    /// by a few nodes keeps the textures, and textures grown for a wide view are given back.</summary>
    public static (int Width, int Height) Size(int shownWidth, int shownHeight, int columns, int rows) =>
        (Side(shownWidth, columns), Side(shownHeight, rows));

    private static int Side(int shown, int nodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodes);
        var needed = checked((Math.Max(nodes, 1) + Step - 1) / Step * Step);
        return shown >= needed && shown <= (long)Slack * needed ? shown : needed;
    }

    /// <summary>The RGBA8 rows of a lattice laid into the top left of a texture of <paramref name="width"/> by <paramref name="height"/> nodes;
    /// every node outside the lattice is zero.</summary>
    public static byte[] Lay(ReadOnlySpan<byte> lattice, int columns, int rows, int width, int height)
    {
        if (columns < 0 || rows < 0 || columns > width || rows > height || lattice.Length != checked(columns * rows * BytesPerNode))
            throw new ArgumentException("AREA_CARRIED_LATTICE_DOMAIN", nameof(lattice));
        var texels = new byte[checked(width * height * BytesPerNode)];
        for (var row = 0; row < rows; row++)
            lattice.Slice(row * columns * BytesPerNode, columns * BytesPerNode).CopyTo(texels.AsSpan(row * width * BytesPerNode));
        return texels;
    }
}
