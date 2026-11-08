namespace Arcanum.Data.Hd;

/// <summary>ARC-UI-113: where a frame's HD alpha comes from instead of the Legacy alpha: a mask image or an ellipse, both in the
/// frame's own Legacy pixels. Exactly one of <paramref name="Image"/> and <paramref name="Ellipse"/> is set.</summary>
/// <param name="Image">Path (relative to the plan file or absolute) of a grey PNG, white = keep, black = clear (a colour file takes
/// the mean of its channels, an alpha channel is ignored). Its size is the Legacy frame's <c>w × h</c> (smoothed like
/// <see cref="HdAlphaMode.LegacySmooth"/>) or the HD frame's <c>scale·w × scale·h</c> (taken as the alpha itself).</param>
/// <param name="Ellipse"><c>[cx, cy, rx, ry]</c> in Legacy pixels from the frame's top-left corner (texel <c>x</c> covers
/// <c>x..x+1</c>, so the whole of a 19×19 frame is <c>[9.5, 9.5, 9.5, 9.5]</c>); the edge is anti-aliased at the HD resolution.</param>
public sealed record HdAlphaMaskSpec(string? Image, IReadOnlyList<double>? Ellipse);

/// <summary>A frame whose alpha the owner's mask decides. <see cref="Legacy"/> is the frame with its alpha replaced by the mask at the
/// Legacy resolution (255 or 0: what the colour lock and the edge smoothing see); <see cref="HdAlpha"/> is the exact HD alpha when the mask
/// is finer than the Legacy grid, null when the smooth alpha of <see cref="Legacy"/> is to be used.</summary>
internal sealed record HdMaskedFrame(HdLegacyRaster Legacy, byte[]? HdAlpha);

/// <summary>ARC-UI-113: applies an <see cref="HdAlphaMaskSpec"/> to one frame. The mask only ever removes: a texel the Legacy frame
/// leaves transparent stays transparent (an HD-resolution mask or an ellipse is cut to the opaque texels; a Legacy-resolution mask is
/// intersected with them, and its smoothed edge stays inside the masked texels), and nothing is ever drawn beyond one Legacy texel of the
/// Legacy silhouette, which is what the pack's <see cref="HdAlphaMode.Masked"/> check demands.</summary>
internal static class HdAlphaMasks
{
    private const int Samples = 8;

    /// <summary>Resolves the mask of <paramref name="legacy"/>; null with <paramref name="problem"/> when the mask is unusable.</summary>
    internal static HdMaskedFrame? Resolve(HdAlphaMaskSpec spec, string label, HdLegacyRaster legacy, int scale, Func<string, HdRgbImage?> image, out string? problem)
    {
        problem = null;
        int w = legacy.Width, h = legacy.Height;
        byte[]? hdAlpha = null; var texel = new bool[w * h];
        if (spec.Ellipse is { } e)
        {
            hdAlpha = new byte[w * scale * h * scale];
            EllipseAlpha(hdAlpha, w * scale, h * scale, scale, e[0], e[1], e[2], e[3]);
        }
        else
        {
            var picture = image(spec.Image!);
            if (picture is null) { problem = $"{label}: alpha mask {spec.Image} could not be read"; return null; }
            if (picture.Width == w && picture.Height == h)
                for (var i = 0; i < texel.Length; i++) texel[i] = Grey(picture.Rgb, i) >= 128f;
            else if (picture.Width == w * scale && picture.Height == h * scale)
            {
                hdAlpha = new byte[picture.Width * picture.Height];
                for (var i = 0; i < hdAlpha.Length; i++) hdAlpha[i] = (byte)Math.Clamp((int)(Grey(picture.Rgb, i) + 0.5f), 0, 255);
            }
            else { problem = $"{label}: alpha mask {spec.Image} is {picture.Width}x{picture.Height}, it needs {w}x{h} or {w * scale}x{h * scale}"; return null; }
        }
        if (hdAlpha is not null)
        {
            // A mask finer than the Legacy grid is the alpha as drawn, but only inside the texels the Legacy frame leaves opaque.
            for (var y = 0; y < h * scale; y++)
            for (var x = 0; x < w * scale; x++)
                if (legacy.Rgba[((y / scale) * w + x / scale) * 4 + 3] < 128) hdAlpha[y * w * scale + x] = 0;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                long sum = 0;
                for (var dy = 0; dy < scale; dy++) for (var dx = 0; dx < scale; dx++) sum += hdAlpha[(y * scale + dy) * w * scale + x * scale + dx];
                texel[y * w + x] = sum >= 128L * scale * scale;
            }
        }
        else for (var i = 0; i < texel.Length; i++) texel[i] &= legacy.Rgba[i * 4 + 3] >= 128;
        if (!texel.Contains(true)) { problem = $"{label}: the alpha mask leaves nothing opaque"; return null; }
        var masked = (byte[])legacy.Rgba.Clone();
        for (var i = 0; i < texel.Length; i++) masked[i * 4 + 3] = texel[i] ? (byte)255 : (byte)0;
        return new(new HdLegacyRaster(w, h, masked), hdAlpha);
    }

    /// <summary>The RGBA of a masked frame: the locked colour <paramref name="rgb"/> with the mask's alpha. A pixel the mask covers only in
    /// part, and a cleared one next to the circle, takes the nearest fully covered pixel's colour within one Legacy texel, so the edge (also
    /// once filtered when minified) shows the art and not what the redraw has beyond the mask (the light square round a button).</summary>
    internal static byte[] Compose(float[] rgb, HdMaskedFrame frame, HdLegacyRaster original, int scale)
    {
        byte[] rgba;
        if (frame.HdAlpha is null) rgba = HdInterfaceImport.EdgeAlpha(rgb, frame.Legacy, scale);
        else
        {
            int bigW = frame.Legacy.Width * scale, bigH = frame.Legacy.Height * scale;
            rgba = new byte[bigW * bigH * 4];
            for (var i = 0; i < bigW * bigH; i++)
            {
                for (var c = 0; c < 3; c++) rgba[i * 4 + c] = (byte)Math.Clamp((int)(rgb[i * 3 + c] + 0.5f), 0, 255);
                rgba[i * 4 + 3] = frame.HdAlpha[i];
            }
            var source = (byte[])rgba.Clone();
            for (var y = 0; y < bigH; y++)
            for (var x = 0; x < bigW; x++)
            {
                var at = y * bigW + x;
                if (source[at * 4 + 3] == 255) continue;
                int best = -1, bestDistance = int.MaxValue;
                for (var ny = Math.Max(0, y - scale); ny <= Math.Min(bigH - 1, y + scale); ny++)
                for (var nx = Math.Max(0, x - scale); nx <= Math.Min(bigW - 1, x + scale); nx++)
                {
                    if (source[(ny * bigW + nx) * 4 + 3] != 255) continue;
                    int distance = (nx - x) * (nx - x) + (ny - y) * (ny - y);
                    if (distance < bestDistance) { bestDistance = distance; best = ny * bigW + nx; }
                }
                if (best >= 0) for (var c = 0; c < 3; c++) rgba[at * 4 + c] = source[best * 4 + c];
            }
        }
        var alpha = new byte[rgba.Length / 4];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = rgba[i * 4 + 3];
        Limit(alpha, original, scale);
        for (var i = 0; i < alpha.Length; i++) rgba[i * 4 + 3] = alpha[i];
        return rgba;
    }

    // Clears every HD pixel farther than one Legacy texel from the original silhouette (the Masked check's only rule).
    private static void Limit(byte[] alpha, HdLegacyRaster original, int scale)
    {
        int w = original.Width, h = original.Height, bigW = w * scale;
        for (var y = 0; y < h * scale; y++)
        for (var x = 0; x < bigW; x++)
        {
            if (alpha[y * bigW + x] == 0 || NearOpaque(original, x / scale, y / scale)) continue;
            alpha[y * bigW + x] = 0;
        }
    }

    private static bool NearOpaque(HdLegacyRaster legacy, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < legacy.Width && ny < legacy.Height && legacy.Rgba[(ny * legacy.Width + nx) * 4 + 3] >= 128) return true;
        }
        return false;
    }

    private static float Grey(float[] rgb, int pixel) => (rgb[pixel * 3] + rgb[pixel * 3 + 1] + rgb[pixel * 3 + 2]) / 3f;

    // Coverage of each HD pixel by the ellipse, from Samples × Samples points inside the pixel.
    private static void EllipseAlpha(byte[] alpha, int bigW, int bigH, int scale, double cx, double cy, double rx, double ry)
    {
        for (var y = 0; y < bigH; y++)
        for (var x = 0; x < bigW; x++)
        {
            var inside = 0;
            for (var sy = 0; sy < Samples; sy++)
            for (var sx = 0; sx < Samples; sx++)
            {
                double px = (x + (sx + 0.5) / Samples) / scale - cx, py = (y + (sy + 0.5) / Samples) / scale - cy;
                if (px * px / (rx * rx) + py * py / (ry * ry) <= 1.0) inside++;
            }
            alpha[y * bigW + x] = (byte)(int)Math.Round(255.0 * inside / (Samples * Samples));
        }
    }
}
