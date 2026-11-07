namespace Arcanum.Data.Hd;

/// <summary>A decoded colour image for the redraw import: <see cref="Rgb"/> is <c>Width × Height × 3</c> floats, 0..255.</summary>
public sealed record HdRgbImage(int Width, int Height, float[] Rgb)
{
    public static HdRgbImage FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("RGBA must be width × height × 4 bytes.", nameof(rgba));
        var rgb = new float[width * height * 3];
        for (var i = 0; i < width * height; i++) for (var c = 0; c < 3; c++) rgb[i * 3 + c] = rgba[i * 4 + c];
        return new(width, height, rgb);
    }
}

/// <summary>One Legacy frame as the importer sees it (RGBA, <c>Width × Height × 4</c>).</summary>
public sealed record HdLegacyRaster(int Width, int Height, byte[] Rgba);

/// <summary>Where a frame lies in a redraw image: <c>image index = Scale·(sheet coordinate) + Offset</c> per axis, with coordinates in pixel
/// edges (a pixel centre is at +0.5). <see cref="Ncc"/> is the normalised cross-correlation of the blurred luma that was reached.</summary>
public sealed record HdFitTransform(double Sx, double Sy, double Tx, double Ty, double Ncc);

/// <summary>A frame's place on a redraw sheet (the Legacy sheet the redraw was made from, <see cref="HdRedrawSheet"/>).</summary>
public sealed record HdSheetFrame(int X, int Y, int Width, int Height);

/// <summary>
/// ARC-DEV-242: the sheet the owner's external redraw starts from (ARC-RES-094 U3): Legacy frames side by side, each in a
/// <see cref="Pad"/>-texel border filled by colour bleeding, plain grey (128) between. The redraw is an image of this sheet at any
/// scale with the same aspect; <see cref="HdRedrawFit"/> finds its scale and offset again.
/// </summary>
public sealed class HdRedrawSheet
{
    public int Pad { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<HdSheetFrame> Frames { get; }
    public IReadOnlyList<HdLegacyRaster> Legacy { get; }
    /// <summary>The sheet's luma at 1×.</summary>
    public float[] Luma { get; }

    private HdRedrawSheet(int pad, int width, int height, IReadOnlyList<HdSheetFrame> frames, IReadOnlyList<HdLegacyRaster> legacy, float[] luma)
    { Pad = pad; Width = width; Height = height; Frames = frames; Legacy = legacy; Luma = luma; }

    /// <summary>Lays <paramref name="frames"/> out row by row in <paramref name="columns"/> columns (0: one row), each in a cell of the largest
    /// frame plus <paramref name="pad"/> texels on every side, the frame at the cell's top left.</summary>
    public static HdRedrawSheet Build(IReadOnlyList<HdLegacyRaster> frames, int pad, int columns = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0 || pad < 0 || columns < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        if (columns == 0 || columns > frames.Count) columns = frames.Count;
        var rows = (frames.Count + columns - 1) / columns;
        int cellW = frames.Max(f => f.Width) + 2 * pad, cellH = frames.Max(f => f.Height) + 2 * pad;
        // One row keeps each cell as wide as its own frame (frames of different widths); a grid uses equal cells.
        var widths = columns == frames.Count && rows == 1 ? frames.Select(f => f.Width + 2 * pad).ToArray() : Enumerable.Repeat(cellW, frames.Count).ToArray();
        var width = rows == 1 ? widths.Sum() : cellW * columns; var height = cellH * rows;
        var rgb = new float[width * height * 3];
        Array.Fill(rgb, 128f);
        var placed = new List<HdSheetFrame>(); var x = 0;
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            int col = index % columns, row = index / columns;
            var originX = rows == 1 ? x : col * cellW; var originY = row * cellH;
            int tw = rows == 1 ? widths[index] : cellW, th = cellH;
            var tile = new float[tw * th * 3]; var mask = new bool[tw * th];
            for (var j = 0; j < frame.Height; j++)
            for (var i = 0; i < frame.Width; i++)
            {
                var from = (j * frame.Width + i) * 4; var to = (j + pad) * tw + i + pad;
                mask[to] = frame.Rgba[from + 3] > 0;
                for (var c = 0; c < 3; c++) tile[to * 3 + c] = frame.Rgba[from + c];
            }
            tile = HdRaster.Bleed(tile, mask, tw, th, 3, pad + 4);
            for (var j = 0; j < th; j++) Array.Copy(tile, j * tw * 3, rgb, ((originY + j) * width + originX) * 3, tw * 3);
            placed.Add(new(originX + pad, originY + pad, frame.Width, frame.Height));
            x += tw;
        }
        return new(pad, width, height, placed, frames, HdRaster.Luma(rgb, width * height));
    }

    /// <summary>A sheet of one frame with no border, for a redraw that already covers exactly that frame.</summary>
    public static HdRedrawSheet Single(HdLegacyRaster frame) => Build([frame], 0);
}

/// <summary>The registration of a redraw to its Legacy frames: a first fit of the whole sheet, then one per frame, with a final
/// translation refinement against the frame's silhouette (ARC-RES-094 U3, ported from the local research tooling).</summary>
public static class HdRedrawFit
{
    /// <summary>The box of the redraw's content: rows and columns with at least 3 % non-white pixels (white = 244 and above in every
    /// channel). Used as the first guess of the fit when the redraw has a white margin.</summary>
    public static (int X0, int Y0, int X1, int Y1) ContentBox(HdRgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w = image.Width, h = image.Height;
        var rows = new int[h]; var cols = new int[w];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var at = (y * w + x) * 3;
            if (Math.Min(image.Rgb[at], Math.Min(image.Rgb[at + 1], image.Rgb[at + 2])) < 244) { rows[y]++; cols[x]++; }
        }
        int FirstLast(int[] counts, int n, bool last)
        {
            var found = -1;
            for (var i = 0; i < counts.Length; i++) if (counts[i] > 0.03 * n) { if (!last) return i; found = i; }
            return found;
        }
        var x0 = FirstLast(cols, h, false); var x1 = FirstLast(cols, h, true); var y0 = FirstLast(rows, w, false); var y1 = FirstLast(rows, w, true);
        return x0 < 0 || y0 < 0 ? (0, 0, w, h) : (x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>Fits every frame of <paramref name="sheet"/> in <paramref name="redraw"/>. <paramref name="exact"/> skips the search for
    /// the whole sheet and takes the image to cover the sheet exactly (a redraw already cut to the frame, such as a stitched strip).</summary>
    public static IReadOnlyList<HdFitTransform> Fit(HdRgbImage redraw, HdRedrawSheet sheet, bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(redraw); ArgumentNullException.ThrowIfNull(sheet);
        var redrawLuma = HdRaster.Luma(redraw.Rgb, redraw.Width * redraw.Height);
        var blurs = new Blurs(redrawLuma, redraw.Width, redraw.Height);
        double sx0, sy0, tx0, ty0;
        HdFitTransform whole;
        if (exact)
        {
            sx0 = (double)redraw.Width / sheet.Width; sy0 = (double)redraw.Height / sheet.Height; tx0 = 0; ty0 = 0;
            whole = new(sx0, sy0, tx0, ty0, 0);
        }
        else
        {
            var (x0, y0, x1, y1) = ContentBox(redraw);
            // One scale for both axes from the tighter fit, the sheet centred in the content box (a redraw may add free space on one side).
            int cw = x1 - x0, ch = y1 - y0;
            var s0 = Math.Min((double)cw / sheet.Width, (double)ch / sheet.Height);
            sx0 = s0; sy0 = s0;
            tx0 = x0 + (cw - s0 * sheet.Width) / 2; ty0 = y0 + (ch - s0 * sheet.Height) / 2;
            var left = Math.Max(sheet.Frames.Min(f => f.X) - sheet.Pad, 0);
            var right = Math.Min(sheet.Frames.Max(f => f.X + f.Width) + sheet.Pad, sheet.Width);
            var rect = new HdSheetFrame(left, 0, right - left, sheet.Height);
            var slackX = 0.12 * cw; var slackY = 0.12 * ch;
            whole = Register(blurs, sheet.Luma, sheet.Width, rect, [sx0, sy0, tx0, ty0],
                [sx0 * 0.6, sy0 * 0.6, tx0 - slackX, ty0 - slackY], [sx0 * 1.1, sy0 * 1.1, tx0 + slackX, ty0 + slackY],
                Math.Max(sheet.Width, sheet.Height) > 900 ? 1 : 2);
        }

        var result = new List<HdFitTransform>();
        for (var index = 0; index < sheet.Frames.Count; index++)
        {
            var frame = sheet.Frames[index]; var legacy = sheet.Legacy[index];
            HdFitTransform fitted;
            if (exact) fitted = whole;
            else
            {
                double[] start = [whole.Sx, whole.Sy, whole.Tx, whole.Ty];
                fitted = Register(blurs, sheet.Luma, sheet.Width, frame, start,
                    [whole.Sx * 0.98, whole.Sy * 0.98, whole.Tx - 3 * whole.Sx, whole.Ty - 3 * whole.Sy],
                    [whole.Sx * 1.02, whole.Sy * 1.02, whole.Tx + 3 * whole.Sx, whole.Ty + 3 * whole.Sy],
                    Math.Max(frame.Width, frame.Height) < 200 ? 2 : 1);
            }
            result.Add(RefineTranslation(blurs, frame, legacy, fitted));
        }
        return result;
    }

    /// <summary>Resamples the redraw onto the frame's <paramref name="scale"/>× grid by <paramref name="transform"/>
    /// (bicubic), returning <c>scale·w × scale·h × 3</c> floats, 0..255.</summary>
    public static float[] Extract(HdRgbImage redraw, HdSheetFrame frame, HdFitTransform transform, int scale)
    {
        ArgumentNullException.ThrowIfNull(redraw); ArgumentNullException.ThrowIfNull(transform);
        int w = frame.Width * scale, h = frame.Height * scale;
        var result = new float[w * h * 3];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var xs = transform.Sx * (frame.X + (x + 0.5) / scale) + transform.Tx - 0.5;
            var ys = transform.Sy * (frame.Y + (y + 0.5) / scale) + transform.Ty - 0.5;
            for (var c = 0; c < 3; c++) result[(y * w + x) * 3 + c] = Math.Clamp(HdRaster.Bicubic(redraw.Rgb, redraw.Width, redraw.Height, 3, c, xs, ys), 0f, 255f);
        }
        return result;
    }

    private static double Ncc(float[] a, float[] b)
    {
        double ma = 0, mb = 0;
        for (var i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (var i = 0; i < a.Length; i++) { var da = a[i] - ma; var db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        return sab / Math.Sqrt(saa * sbb + 1e-9);
    }

    // Finds (sx, sy, tx, ty) maximising the NCC of the blurred luma of the sheet rect and of the redraw sampled through the transform,
    // coarse to fine (blurs of 6, 3 and 1.5 Legacy texels, each stage starting from the last): the redraw's own texture is a different
    // drawing, so only its broad shading may guide the first stages.
    private static HdFitTransform Register(Blurs redraw, float[] sheetLuma, int sheetW, HdSheetFrame rect, double[] init, double[] low, double[] high, int up)
    {
        var gw = rect.Width * up; var gh = rect.Height * up;
        var sampled = new float[gw * gh];
        var x = init; double value = 0;
        foreach (var blur in new[] { 6.0, 3.0, 1.5 })
        {
            var reference = new float[gw * gh];
            for (var j = 0; j < gh; j++) for (var i = 0; i < gw; i++) reference[j * gw + i] = sheetLuma[(rect.Y + j / up) * sheetW + rect.X + i / up];
            reference = HdRaster.Gaussian(reference, gw, gh, 1, blur * up);
            var blurred = redraw.Of(blur * init[0]);
            double Cost(double[] p)
            {
                for (var j = 0; j < gh; j++)
                for (var i = 0; i < gw; i++)
                {
                    var xs = p[0] * (rect.X + (i + 0.5) / up) + p[2] - 0.5; var ys = p[1] * (rect.Y + (j + 0.5) / up) + p[3] - 0.5;
                    sampled[j * gw + i] = blurred.At(xs, ys);
                }
                return -Ncc(reference, sampled);
            }
            var best = HdOptimizer.NelderMead(Cost, x, low, high, 100, 1e-3);
            x = best.X; value = -best.Value;
        }
        return new(x[0], x[1], x[2], x[3], value);
    }

    /// <summary>The redraw's luma and its Gaussian blurs by σ in redraw pixels, kept for the next fits. A large blur is taken on a box-reduced
    /// copy (<see cref="Plane.K"/> redraw pixels per sample) so it stays cheap.</summary>
    private sealed class Blurs(float[] luma, int width, int height)
    {
        public sealed record Plane(float[] Data, int Width, int Height, int K)
        {
            public float At(double x, double y) => HdRaster.Bilinear(Data, Width, Height, (x + 0.5) / K - 0.5, (y + 0.5) / K - 0.5);
        }

        private readonly Dictionary<int, Plane> _cache = [];
        public int Width { get; } = width;
        public int Height { get; } = height;
        public Plane Of(double sigma)
        {
            var key = (int)Math.Round(sigma * 4);
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var k = Math.Max(1, (int)(key / 4.0 / 3));
            var reduced = k == 1 ? luma : HdRaster.BoxMean(luma, Width, Height, k, out _, out _);
            int rw = (Width + k - 1) / k, rh = (Height + k - 1) / k;
            return _cache[key] = new(HdRaster.Gaussian(reduced, rw, rh, 1, key / 4.0 / k), rw, rh, k);
        }
    }

    // The residual rigid shift of the whole frame: shifts the transform so the redraw's luma composited over mid grey through the Legacy
    // silhouette (hard edge, holes count as shapes), blurred over 3 and then 1.5 Legacy texels, lies on the Legacy frame's.
    private static HdFitTransform RefineTranslation(Blurs redraw, HdSheetFrame frame, HdLegacyRaster legacy, HdFitTransform start)
    {
        var legacyLuma = new float[frame.Width * frame.Height]; double sum = 0, sumSq = 0;
        for (var i = 0; i < legacyLuma.Length; i++)
        {
            var a = legacy.Rgba[i * 4 + 3] / 255f;
            legacyLuma[i] = HdRaster.Luma(legacy.Rgba[i * 4], legacy.Rgba[i * 4 + 1], legacy.Rgba[i * 4 + 2]) * a + 128f * (1 - a);
            sum += legacyLuma[i]; sumSq += legacyLuma[i] * legacyLuma[i];
        }
        var mean = sum / legacyLuma.Length;
        if (Math.Sqrt(Math.Max(sumSq / legacyLuma.Length - mean * mean, 0)) < 4) return start; // a flat frame has nothing to register on
        double dx = 0, dy = 0, ncc = start.Ncc;
        foreach (var (up, blur) in new[] { (1, 3.0), (2, 1.5) })
        {
            int w = frame.Width * up, h = frame.Height * up;
            var alpha = new float[w * h]; var reference = new float[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var parent = (y / up) * frame.Width + x / up;
                alpha[y * w + x] = legacy.Rgba[parent * 4 + 3] / 255f; reference[y * w + x] = legacyLuma[parent];
            }
            reference = HdRaster.Gaussian(reference, w, h, 1, blur * up);
            var plane = redraw.Of(0.5 * blur * start.Sx);
            var sampled = new float[w * h];
            double Cost(double[] d)
            {
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var xs = start.Sx * (frame.X + (x + 0.5) / up + d[0]) + start.Tx - 0.5;
                    var ys = start.Sy * (frame.Y + (y + 0.5) / up + d[1]) + start.Ty - 0.5;
                    var a = alpha[y * w + x];
                    sampled[y * w + x] = plane.At(xs, ys) * a + 128f * (1 - a);
                }
                return -Ncc(reference, HdRaster.Gaussian(sampled, w, h, 1, blur * up * 0.87));
            }
            var best = HdOptimizer.NelderMead(Cost, [dx, dy], [-3, -3], [3, 3], 60, 0.01);
            dx = best.X[0]; dy = best.X[1]; ncc = -best.Value;
        }
        return new(start.Sx, start.Sy, start.Tx + dx * start.Sx, start.Ty + dy * start.Sy, Math.Max(start.Ncc, ncc));
    }
}

/// <summary>A small bounded Nelder–Mead simplex search (the registration has four parameters, so no gradient is needed).</summary>
internal static class HdOptimizer
{
    public sealed record Result(double[] X, double Value);

    public static Result NelderMead(Func<double[], double> f, double[] start, double[] low, double[] high, int maxIterations, double tolerance)
    {
        var n = start.Length;
        double[] Clamp(double[] p) => [.. p.Select((v, i) => Math.Clamp(v, low[i], high[i]))];
        var simplex = new List<(double[] X, double V)>();
        var x0 = Clamp(start); simplex.Add((x0, f(x0)));
        for (var i = 0; i < n; i++)
        {
            var p = (double[])x0.Clone();
            var step = 0.1 * (high[i] - low[i]);
            p[i] = p[i] + step > high[i] ? p[i] - step : p[i] + step;
            simplex.Add((p, f(p)));
        }
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            simplex.Sort((a, b) => a.V.CompareTo(b.V));
            var spread = 0.0;
            for (var i = 0; i < n; i++) spread = Math.Max(spread, simplex.Max(s => Math.Abs(s.X[i] - simplex[0].X[i])) / Math.Max(high[i] - low[i], 1e-12));
            if (spread < tolerance * 0.01) break;
            var centroid = new double[n];
            for (var k = 0; k < n; k++) for (var i = 0; i < n; i++) centroid[i] += simplex[k].X[i] / n;
            double[] Along(double t) => Clamp([.. Enumerable.Range(0, n).Select(i => centroid[i] + t * (simplex[n].X[i] - centroid[i]))]);
            var reflected = Along(-1); var rv = f(reflected);
            if (rv < simplex[0].V)
            {
                var expanded = Along(-2); var ev = f(expanded);
                simplex[n] = ev < rv ? (expanded, ev) : (reflected, rv);
            }
            else if (rv < simplex[n - 1].V) simplex[n] = (reflected, rv);
            else
            {
                var contracted = Along(rv < simplex[n].V ? -0.5 : 0.5); var cv = f(contracted);
                if (cv < Math.Min(rv, simplex[n].V)) simplex[n] = (contracted, cv);
                else
                    for (var k = 1; k <= n; k++)
                    {
                        var p = Clamp([.. Enumerable.Range(0, n).Select(i => simplex[0].X[i] + 0.5 * (simplex[k].X[i] - simplex[0].X[i]))]);
                        simplex[k] = (p, f(p));
                    }
            }
        }
        simplex.Sort((a, b) => a.V.CompareTo(b.V));
        return new(simplex[0].X, simplex[0].V);
    }
}
