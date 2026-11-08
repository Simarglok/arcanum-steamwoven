namespace Arcanum.Data.Hd;

/// <summary>
/// The alpha check of an HD frame against the device's Legacy alpha, by the recipe's alpha mode (docs/formats/hd-pack.md):
/// <c>legacy</c> every HD texel has exactly its parent's alpha; <c>soft</c> no alpha farther than one Legacy texel from the
/// silhouette and a mask IoU; <c>legacySmooth</c> (ARC-UI-111) the Legacy silhouette with an anti-aliased edge.
/// </summary>
public static class HdAlphaCheck
{
    /// <summary>Whether <paramref name="hd"/> (RGBA, <c>scale·width × scale·height</c>) passes <paramref name="policy"/> against
    /// <paramref name="legacy"/> (RGBA, <c>width × height</c>). The Legacy mask is alpha ≥ 128.
    /// <para>Legacy-smooth rules: (1) an HD texel whose parent and all in-frame 8 neighbours are on one side of the mask must
    /// carry its parent's alpha exactly (no square backgrounds, no growth: only the edge may change); (2) a single-texel hole or
    /// speck (every in-frame 4-neighbour on the other side, at least two) keeps its centre on its own side, so holes and colour
    /// keys survive; (3) the HD mask and the nearest Legacy mask meet the policy's IoU.</para></summary>
    public static bool Matches(ReadOnlySpan<byte> hd, ReadOnlySpan<byte> legacy, int width, int height, int scale, HdAlphaPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (scale < 1 || hd.Length != (long)width * scale * height * scale * 4 || legacy.Length != width * height * 4) return false;
        var hdWidth = width * scale;
        var mode = policy.Mode;
        long intersection = 0, union = 0;
        for (var y = 0; y < height * scale; y++)
        for (var x = 0; x < hdWidth; x++)
        {
            int px = x / scale, py = y / scale;
            var parent = legacy[(py * width + px) * 4 + 3];
            var alpha = hd[(y * hdWidth + x) * 4 + 3];
            if (mode == HdAlphaMode.Legacy)
            {
                if (alpha != parent) return false;
                continue;
            }
            if (mode == HdAlphaMode.Soft)
            {
                if (alpha != 0 && parent < 128 && !NearOpaque(legacy, width, height, px, py)) return false;
            }
            else if (!SmoothPixelAllowed(legacy, width, height, scale, x, y, px, py, parent, alpha)) return false;
            bool a = alpha >= 128, b = parent >= 128;
            if (a && b) intersection++;
            if (a || b) union++;
        }
        return mode == HdAlphaMode.Legacy || intersection * 1000 >= union * policy.MinSoftIoUPermille;
    }

    // Rules 1 and 2 of the legacy-smooth mode for one HD pixel.
    private static bool SmoothPixelAllowed(ReadOnlySpan<byte> legacy, int width, int height, int scale, int x, int y, int px, int py, byte parent, byte alpha)
    {
        var side = parent >= 128;
        if (UniformSide(legacy, width, height, px, py, side)) return alpha == parent;
        if (!Isolated(legacy, width, height, px, py, side)) return true;
        // The core of an isolated texel (within a quarter texel of its centre) stays on the texel's own side of the mask.
        bool core = Math.Abs(2 * (x % scale) + 1 - scale) * 2 <= scale && Math.Abs(2 * (y % scale) + 1 - scale) * 2 <= scale;
        return !core || (alpha >= 128) == side;
    }

    /// <summary>Whether the texel and every in-frame neighbour of its 3×3 lie on <paramref name="side"/> of the mask.</summary>
    internal static bool UniformSide(ReadOnlySpan<byte> legacy, int width, int height, int x, int y, bool side)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < width && ny < height && (legacy[(ny * width + nx) * 4 + 3] >= 128) != side) return false;
        }
        return true;
    }

    /// <summary>A single-texel hole or speck: at least two in-frame 4-neighbours, all on the other side of the mask.</summary>
    internal static bool Isolated(ReadOnlySpan<byte> legacy, int width, int height, int x, int y, bool side)
    {
        var near = 0;
        for (var k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if ((legacy[(ny * width + nx) * 4 + 3] >= 128) == side) return false;
            near++;
        }
        return near >= 2;
    }

    private static bool NearOpaque(ReadOnlySpan<byte> legacy, int width, int height, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < width && ny < height && legacy[(ny * width + nx) * 4 + 3] >= 128) return true;
        }
        return false;
    }
}
