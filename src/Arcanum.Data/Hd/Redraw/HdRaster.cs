namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-DEV-242: float raster helpers of the interface redraw import (registration, lock, difference): luma, bicubic up- and
/// box down-sampling, Gaussian blur, colour bleeding and sampling. Interleaved channels, row-major, values 0..255 for colour.
/// Pure arithmetic on arrays the caller owns; nothing here reads a file.
/// </summary>
internal static class HdRaster
{
    public static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;

    public static float[] Luma(float[] rgb, int pixels)
    {
        var luma = new float[pixels];
        for (var i = 0; i < pixels; i++) luma[i] = Luma(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
        return luma;
    }

    /// <summary>Catmull-Rom (a = -0.5, PIL's bicubic) weights for the four taps around a fractional position.</summary>
    private static void Cubic(float t, Span<float> w)
    {
        const float a = -0.5f;
        w[0] = ((a * (t + 1) - 5 * a) * (t + 1) + 8 * a) * (t + 1) - 4 * a;
        w[1] = ((a + 2) * t - (a + 3)) * t * t + 1;
        w[2] = ((a + 2) * (1 - t) - (a + 3)) * (1 - t) * (1 - t) + 1;
        w[3] = 1 - w[0] - w[1] - w[2];
    }

    /// <summary>Bicubic enlargement by an integer factor with PIL's pixel-centre mapping and clamped edges.</summary>
    public static float[] UpBicubic(float[] data, int width, int height, int channels, int scale)
    {
        var outW = width * scale; var outH = height * scale;
        var horizontal = new float[outW * height * channels];
        Span<float> w = stackalloc float[4];
        for (var x = 0; x < outW; x++)
        {
            var source = (x + 0.5f) / scale - 0.5f; var x0 = (int)MathF.Floor(source); Cubic(source - x0, w);
            for (var y = 0; y < height; y++)
            for (var c = 0; c < channels; c++)
            {
                float sum = 0;
                for (var k = 0; k < 4; k++) sum += w[k] * data[(y * width + Math.Clamp(x0 - 1 + k, 0, width - 1)) * channels + c];
                horizontal[(y * outW + x) * channels + c] = sum;
            }
        }
        var result = new float[outW * outH * channels];
        for (var y = 0; y < outH; y++)
        {
            var source = (y + 0.5f) / scale - 0.5f; var y0 = (int)MathF.Floor(source); Cubic(source - y0, w);
            for (var x = 0; x < outW; x++)
            for (var c = 0; c < channels; c++)
            {
                float sum = 0;
                for (var k = 0; k < 4; k++) sum += w[k] * horizontal[(Math.Clamp(y0 - 1 + k, 0, height - 1) * outW + x) * channels + c];
                result[(y * outW + x) * channels + c] = sum;
            }
        }
        return result;
    }

    /// <summary>Weighted box reduction: every <paramref name="scale"/>×<paramref name="scale"/> block becomes the mean of its
    /// samples weighted by <paramref name="weights"/> (null: plain mean); a block with no weight is 0. <paramref name="width"/> and
    /// <paramref name="height"/> are the large raster's.</summary>
    public static float[] BoxDown(float[] data, float[]? weights, int width, int height, int channels, int scale)
    {
        int w = width / scale, h = height / scale;
        var result = new float[w * h * channels];
        var sum = new double[channels];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            double weightSum = 0; Array.Clear(sum);
            for (var dy = 0; dy < scale; dy++)
            for (var dx = 0; dx < scale; dx++)
            {
                var at = (y * scale + dy) * width + x * scale + dx;
                var weight = weights is null ? 1f : weights[at];
                weightSum += weight;
                for (var c = 0; c < channels; c++) sum[c] += data[at * channels + c] * weight;
            }
            for (var c = 0; c < channels; c++) result[(y * w + x) * channels + c] = (float)(sum[c] / Math.Max(weightSum, 1e-6));
        }
        return result;
    }

    /// <summary>Mean of every <paramref name="k"/>×<paramref name="k"/> block of one channel (edge blocks average what they hold).</summary>
    public static float[] BoxMean(float[] plane, int width, int height, int k, out int outWidth, out int outHeight)
    {
        outWidth = (width + k - 1) / k; outHeight = (height + k - 1) / k;
        var result = new float[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        for (var x = 0; x < outWidth; x++)
        {
            double sum = 0; var count = 0;
            for (var j = y * k; j < Math.Min(height, y * k + k); j++)
            for (var i = x * k; i < Math.Min(width, x * k + k); i++) { sum += plane[j * width + i]; count++; }
            result[y * outWidth + x] = (float)(sum / count);
        }
        return result;
    }

    /// <summary>Replicates every pixel <paramref name="scale"/>×<paramref name="scale"/> times.</summary>
    public static float[] Nearest(float[] data, int width, int height, int channels, int scale)
    {
        var outW = width * scale;
        var result = new float[outW * height * scale * channels];
        for (var y = 0; y < height * scale; y++)
        for (var x = 0; x < outW; x++)
            Array.Copy(data, ((y / scale) * width + x / scale) * channels, result, (y * outW + x) * channels, channels);
        return result;
    }

    /// <summary>Separable Gaussian blur of every channel (reflect at the edges, kernel radius 4σ as SciPy's default).</summary>
    public static float[] Gaussian(float[] data, int width, int height, int channels, double sigma)
    {
        if (sigma <= 0) return (float[])data.Clone();
        var radius = (int)(4 * sigma + 0.5);
        var kernel = new double[2 * radius + 1]; double total = 0;
        for (var i = -radius; i <= radius; i++) total += kernel[i + radius] = Math.Exp(-0.5 * i * i / (sigma * sigma));
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= total;
        static int Reflect(int i, int n)
        {
            if (n == 1) return 0;
            var period = 2 * n; i = ((i % period) + period) % period;
            return i < n ? i : period - 1 - i;
        }
        var pass = new float[data.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var c = 0; c < channels; c++)
        {
            double sum = 0;
            for (var k = -radius; k <= radius; k++) sum += kernel[k + radius] * data[(y * width + Reflect(x + k, width)) * channels + c];
            pass[(y * width + x) * channels + c] = (float)sum;
        }
        var result = new float[data.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var c = 0; c < channels; c++)
        {
            double sum = 0;
            for (var k = -radius; k <= radius; k++) sum += kernel[k + radius] * pass[(Reflect(y + k, height) * width + x) * channels + c];
            result[(y * width + x) * channels + c] = (float)sum;
        }
        return result;
    }

    /// <summary>Grows the values of the masked pixels outwards, <paramref name="iterations"/> rings of 3×3 averages; what stays
    /// unreached takes the mean of the masked values (so a flat area outside the shape is not black).</summary>
    public static float[] Bleed(float[] data, bool[] mask, int width, int height, int channels, int iterations)
    {
        var result = (float[])data.Clone();
        var known = (bool[])mask.Clone();
        var mean = new double[channels]; var count = 0;
        for (var i = 0; i < known.Length; i++) if (known[i]) { count++; for (var c = 0; c < channels; c++) mean[c] += result[i * channels + c]; }
        if (count == 0) return result;
        for (var c = 0; c < channels; c++) mean[c] /= count;
        for (var round = 0; round < iterations; round++)
        {
            var added = new List<(int At, float[] Value)>();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (known[y * width + x]) continue;
                int near = 0; var sum = new float[channels];
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height || !known[ny * width + nx]) continue;
                    near++;
                    for (var c = 0; c < channels; c++) sum[c] += result[(ny * width + nx) * channels + c];
                }
                if (near == 0) continue;
                for (var c = 0; c < channels; c++) sum[c] /= near;
                added.Add((y * width + x, sum));
            }
            if (added.Count == 0) break;
            foreach (var (at, value) in added) { known[at] = true; Array.Copy(value, 0, result, at * channels, channels); }
        }
        for (var i = 0; i < known.Length; i++)
            if (!known[i]) for (var c = 0; c < channels; c++) result[i * channels + c] = (float)mean[c];
        return result;
    }

    /// <summary>Bilinear sample of one channel at index coordinates (pixel centres at integers), edges clamped.</summary>
    public static float Bilinear(float[] plane, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        float fx = (float)(x - x0), fy = (float)(y - y0);
        var top = plane[y0 * width + x0] * (1 - fx) + plane[y0 * width + x1] * fx;
        var bottom = plane[y1 * width + x0] * (1 - fx) + plane[y1 * width + x1] * fx;
        return top * (1 - fy) + bottom * fy;
    }

    /// <summary>Catmull-Rom sample of an interleaved channel at index coordinates, edges clamped.</summary>
    public static float Bicubic(float[] data, int width, int height, int channels, int channel, double x, double y)
    {
        Span<float> wx = stackalloc float[4]; Span<float> wy = stackalloc float[4];
        var x0 = (int)Math.Floor(x); var y0 = (int)Math.Floor(y);
        Cubic((float)(x - x0), wx); Cubic((float)(y - y0), wy);
        float sum = 0;
        for (var j = 0; j < 4; j++)
        {
            var row = Math.Clamp(y0 - 1 + j, 0, height - 1);
            float line = 0;
            for (var i = 0; i < 4; i++) line += wx[i] * data[(row * width + Math.Clamp(x0 - 1 + i, 0, width - 1)) * channels + channel];
            sum += wy[j] * line;
        }
        return sum;
    }
}
