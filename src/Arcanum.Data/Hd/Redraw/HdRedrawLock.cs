namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-DEV-242: the shading and colour lock of the U3 redraw method (ARC-RES-094). The redraw's luma, box-reduced to the Legacy
/// grid over the opaque texels, is pulled back to the Legacy luma (shading lock), and its low-frequency chroma to the Legacy
/// chroma (colour lock); the corrections are bicubic-enlarged residuals, so detail inside a texel stays the redraw's.
/// Then the alpha is the parent texel's exactly (<see cref="LegacyAlpha"/>). Float RGB 0..255, interleaved.
/// </summary>
public static class HdRedrawLock
{
    /// <summary>Back-projects the luma error (and, with <paramref name="color"/>, the chroma error) of <paramref name="hd"/>
    /// (<c>scale·w × scale·h</c>, 3 channels) against the Legacy RGB (<c>w × h</c>) over the opaque texels of
    /// <paramref name="mask"/>. <paramref name="coarse"/> &gt; 0 blurs the corrections (σ in Legacy texels): the U3c variant.</summary>
    public static float[] ShadingLock(float[] hd, float[] legacyRgb, bool[] mask, int width, int height, int scale,
        int iterations = 4, bool color = true, double sigma = 1.0, double coarse = 0.0)
    {
        ArgumentNullException.ThrowIfNull(hd); ArgumentNullException.ThrowIfNull(legacyRgb); ArgumentNullException.ThrowIfNull(mask);
        if (hd.Length != width * scale * height * scale * 3 || legacyRgb.Length != width * height * 3 || mask.Length != width * height)
            throw new ArgumentException("Sizes do not match the Legacy frame and scale.");
        var result = (float[])hd.Clone();
        int bigW = width * scale, bigH = height * scale, small = width * height;
        var weights = new float[bigW * bigH];
        for (var y = 0; y < bigH; y++) for (var x = 0; x < bigW; x++) weights[y * bigW + x] = mask[(y / scale) * width + x / scale] ? 1f : 0f;
        var legacyLuma = HdRaster.Luma(legacyRgb, small);

        for (var round = 0; round < iterations; round++)
        {
            var down = HdRaster.BoxDown(result, weights, bigW, bigH, 3, scale);
            var downLuma = HdRaster.Luma(down, small);
            var error = new float[small];
            for (var i = 0; i < small; i++) error[i] = mask[i] ? legacyLuma[i] - downLuma[i] : 0;
            error = HdRaster.Bleed(error, mask, width, height, 1, 12);
            if (coarse > 0) error = HdRaster.Gaussian(error, width, height, 1, coarse);
            var up = HdRaster.UpBicubic(error, width, height, 1, scale);
            for (var i = 0; i < up.Length; i++) { result[i * 3] += up[i]; result[i * 3 + 1] += up[i]; result[i * 3 + 2] += up[i]; }
        }
        if (color)
        {
            for (var round = 0; round < 2; round++)
            {
                var down = HdRaster.BoxDown(result, weights, bigW, bigH, 3, scale);
                var downLuma = HdRaster.Luma(down, small);
                var error = new float[small * 3];
                for (var i = 0; i < small; i++)
                {
                    if (!mask[i]) continue;
                    for (var c = 0; c < 3; c++)
                        error[i * 3 + c] = (legacyRgb[i * 3 + c] - legacyLuma[i]) - (down[i * 3 + c] - downLuma[i]);
                }
                error = HdRaster.Bleed(error, mask, width, height, 3, 12);
                error = HdRaster.Gaussian(error, width, height, 3, Math.Max(sigma, coarse));
                var up = HdRaster.UpBicubic(error, width, height, 3, scale);
                for (var i = 0; i < up.Length; i++) result[i] += up[i];
            }
        }
        for (var i = 0; i < result.Length; i++) result[i] = Math.Clamp(result[i], 0f, 255f);
        return result;
    }

    /// <summary>The RGBA bytes of an HD frame: the rounded colour of <paramref name="rgb"/> and, for every HD texel, exactly the
    /// alpha of its parent Legacy texel (the recipe's <c>legacy</c> alpha mode: hard edge, holes and colour keys as in the original).</summary>
    public static byte[] LegacyAlpha(float[] rgb, ReadOnlySpan<byte> legacyRgba, int width, int height, int scale)
    {
        int bigW = width * scale, bigH = height * scale;
        if (rgb.Length != bigW * bigH * 3 || legacyRgba.Length != width * height * 4) throw new ArgumentException("Sizes do not match the Legacy frame and scale.");
        var rgba = new byte[bigW * bigH * 4];
        for (var y = 0; y < bigH; y++)
        for (var x = 0; x < bigW; x++)
        {
            var at = y * bigW + x; var parent = ((y / scale) * width + x / scale) * 4;
            for (var c = 0; c < 3; c++) rgba[at * 4 + c] = (byte)Math.Clamp((int)(rgb[at * 3 + c] + 0.5f), 0, 255);
            rgba[at * 4 + 3] = legacyRgba[parent + 3];
        }
        return rgba;
    }

    /// <summary>ARC-UI-111: like <see cref="LegacyAlpha"/>, but the edge is anti-aliased (the <c>legacySmooth</c> alpha mode of
    /// <see cref="HdAlphaCheck"/>). The Legacy mask (alpha ≥ 128) is enlarged bilinearly (texel centres at their centres, so a straight
    /// edge stays exactly on the texel boundary) and the ramp is steepened by <paramref name="scale"/> to about one HD pixel: an
    /// axis-aligned edge stays as crisp as the original's, a staircase diagonal turns into a clean one-pixel gradient. Pixels whose
    /// 3×3 Legacy neighbourhood is on one side of the mask keep their parent's alpha exactly; single-texel holes and specks keep their
    /// centre. HD pixels near the edge whose parent is transparent take the nearest opaque-parent colour within one Legacy texel,
    /// so the new partial alpha shows the art's edge colour and not the redraw's background.</summary>
    public static byte[] SmoothAlpha(float[] rgb, ReadOnlySpan<byte> legacyRgba, int width, int height, int scale)
    {
        int bigW = width * scale, bigH = height * scale;
        if (rgb.Length != bigW * bigH * 3 || legacyRgba.Length != width * height * 4) throw new ArgumentException("Sizes do not match the Legacy frame and scale.");
        var rgba = new byte[bigW * bigH * 4];
        var mask = new float[width * height];
        for (var i = 0; i < mask.Length; i++) mask[i] = legacyRgba[i * 4 + 3] >= 128 ? 1f : 0f;
        // Pixels outside the exact interior: the edge band, where alpha is computed (and the colour of a transparent parent is replaced).
        var band = new bool[bigW * bigH];
        for (var y = 0; y < bigH; y++)
        for (var x = 0; x < bigW; x++)
        {
            int px = x / scale, py = y / scale; var at = y * bigW + x;
            for (var c = 0; c < 3; c++) rgba[at * 4 + c] = (byte)Math.Clamp((int)(rgb[at * 3 + c] + 0.5f), 0, 255);
            var parent = legacyRgba[(py * width + px) * 4 + 3];
            if (HdAlphaCheck.UniformSide(legacyRgba, width, height, px, py, parent >= 128)) { rgba[at * 4 + 3] = parent; continue; }
            band[at] = true;
            var u = HdRaster.Bilinear(mask, width, height, (x + 0.5) / scale - 0.5, (y + 0.5) / scale - 0.5);
            rgba[at * 4 + 3] = (byte)Math.Clamp((int)Math.Round(255.0 * Math.Clamp((u - 0.5) * scale + 0.5, 0.0, 1.0)), 0, 255);
        }
        // Colour of the band pixels whose parent is transparent: the nearest pixel (within one texel) whose parent is opaque.
        for (var y = 0; y < bigH; y++)
        for (var x = 0; x < bigW; x++)
        {
            var at = y * bigW + x;
            if (!band[at] || legacyRgba[((y / scale) * width + x / scale) * 4 + 3] >= 128) continue;
            int best = -1, bestDistance = int.MaxValue;
            for (var ny = Math.Max(0, y - scale); ny <= Math.Min(bigH - 1, y + scale); ny++)
            for (var nx = Math.Max(0, x - scale); nx <= Math.Min(bigW - 1, x + scale); nx++)
            {
                if (legacyRgba[((ny / scale) * width + nx / scale) * 4 + 3] < 128) continue;
                int distance = (nx - x) * (nx - x) + (ny - y) * (ny - y);
                if (distance < bestDistance) { bestDistance = distance; best = ny * bigW + nx; }
            }
            if (best >= 0) for (var c = 0; c < 3; c++) rgba[at * 4 + c] = (byte)Math.Clamp((int)(rgb[best * 3 + c] + 0.5f), 0, 255);
        }
        return rgba;
    }

    /// <summary>The U3d derivation for a button's other frames: the redrawn frame 0 plus the bicubic-enlarged Legacy colour
    /// difference <c>frame − frame 0</c>, so a pressed or hovered frame keeps the original's change without a second redraw.</summary>
    public static float[] AddLegacyDifference(float[] baseRgb, float[] legacyFrame, float[] legacyFrame0, int width, int height, int scale)
    {
        if (baseRgb.Length != width * scale * height * scale * 3 || legacyFrame.Length != width * height * 3 || legacyFrame0.Length != legacyFrame.Length)
            throw new ArgumentException("Sizes do not match the Legacy frame and scale.");
        var difference = new float[legacyFrame.Length];
        for (var i = 0; i < difference.Length; i++) difference[i] = legacyFrame[i] - legacyFrame0[i];
        var up = HdRaster.UpBicubic(difference, width, height, 3, scale);
        var result = new float[baseRgb.Length];
        for (var i = 0; i < result.Length; i++) result[i] = Math.Clamp(baseRgb[i] + up[i], 0f, 255f);
        return result;
    }
}
