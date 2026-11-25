namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-RENDER-3002, ARC-UI-148: the pixel size of an interface frame at a device scale and the area (box) filter that resamples a pack frame to it. Godot-free.
/// ARC-UI-3143: what stays of the retired generated chrome renderer; a frame is the pack's redraw (or the Legacy art) at the device scale, never painted.
/// </summary>
public static class InterfaceResample
{
    /// <summary>The largest device scale a frame is resampled to (an 8K screen at 800 × 600 units fits 7).</summary>
    public const int MaxScale = 8;

    /// <summary>The pixel size of a frame of <paramref name="width"/> × <paramref name="height"/> Legacy pixels at <paramref name="scale"/>
    /// (each edge is <c>round(size × scale)</c>, so a whole scale gives exactly size × scale).</summary>
    public static (int Width, int Height) PixelSize(int width, int height, double scale) =>
        ((int)Math.Round(width * scale, MidpointRounding.AwayFromZero), (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));

    /// <summary>The area (box) filter for a frame that keeps its transparency: the alpha of a target pixel is the area average of the source alphas
    /// (straight alpha, rounded) and its colour is weighted by alpha, so a transparent texel adds no colour and an edge does not grow a dark or light halo.
    /// <paramref name="source"/> is RGBA of <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/> (e.g. the pack's 4× redraw); a source of the target's
    /// size is copied as it is.</summary>
    public static byte[] ResampleStraight(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth < 1 || sourceHeight < 1 || targetWidth < 1 || targetHeight < 1) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (source.Length != sourceWidth * sourceHeight * 4) throw new ArgumentException("source RGBA size", nameof(source));
        if (sourceWidth == targetWidth && sourceHeight == targetHeight) return source.ToArray();
        var output = new byte[targetWidth * targetHeight * 4];
        double fx = (double)sourceWidth / targetWidth, fy = (double)sourceHeight / targetHeight;
        for (var ty = 0; ty < targetHeight; ty++)
        {
            double y0 = ty * fy, y1 = y0 + fy;
            int sy0 = (int)y0, sy1 = Math.Min(sourceHeight - 1, (int)Math.Ceiling(y1) - 1);
            for (var tx = 0; tx < targetWidth; tx++)
            {
                double x0 = tx * fx, x1 = x0 + fx;
                int sx0 = (int)x0, sx1 = Math.Min(sourceWidth - 1, (int)Math.Ceiling(x1) - 1);
                double r = 0, g = 0, b = 0, weight = 0, rawR = 0, rawG = 0, rawB = 0, area = 0;
                for (var sy = sy0; sy <= sy1; sy++)
                {
                    var wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
                    for (var sx = sx0; sx <= sx1; sx++)
                    {
                        var w = wy * (Math.Min(sx + 1, x1) - Math.Max(sx, x0));
                        var i = (sy * sourceWidth + sx) * 4;
                        var aw = w * source[i + 3];
                        r += aw * source[i]; g += aw * source[i + 1]; b += aw * source[i + 2]; weight += aw;
                        rawR += w * source[i]; rawG += w * source[i + 1]; rawB += w * source[i + 2]; area += w;
                    }
                }
                var o = (ty * targetWidth + tx) * 4;
                // A fully transparent area keeps its own colour (the texel under a hole is never shown, but stays defined).
                if (weight > 0) { output[o] = (byte)Math.Round(r / weight); output[o + 1] = (byte)Math.Round(g / weight); output[o + 2] = (byte)Math.Round(b / weight); }
                else { output[o] = (byte)Math.Round(rawR / area); output[o + 1] = (byte)Math.Round(rawG / area); output[o + 2] = (byte)Math.Round(rawB / area); }
                output[o + 3] = (byte)Math.Round(weight / area);
            }
        }
        return output;
    }
}
