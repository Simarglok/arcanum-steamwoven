using System.Numerics;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-RENDER-013 (docs/rendering/hd-presentation-contract.md): an HD draw keeps the Legacy
// origin, zoom, destination and reflection and scales only the source crop by N. These are
// synthetic oracles; no original or upscaled art is involved.
[TestClass]
public sealed class HdScaledCropTests
{
    [TestMethod]
    public void ScaledCropKeepsCoverageAndSelectsASubtexelOfTheLegacyTexel()
    {
        var random = new Random(13);
        var zooms = new[] { .125f, .25f, .5f, .8f, 1f, 1.25f, 1.2651956f, 2f, 3f, 8f };
        var owned = 0;
        for (var trial = 0; trial < 3000; trial++)
        {
            var scale = 2 + trial % 3; var zoom = zooms[random.Next(zooms.Length)];
            var origin = random.Next(-2000, 2000) + (trial % 4 == 0 ? 0f : random.Next(0, 64) / 64f);
            var destination = random.Next(-3000, 3000); var length = random.Next(1, 120);
            var sourceStart = random.Next(0, 40); var sourceSize = random.Next(1, 120); var reflected = random.Next(2) == 1;
            var legacy = new AreaNearestAxis(origin, zoom, destination, length, sourceStart, sourceSize, reflected);
            var hd = new AreaNearestAxis(origin, zoom, destination, length, scale * sourceStart, scale * sourceSize, reflected);
            var start = (int)Math.Floor((double)origin + (double)zoom * destination) - 2;
            var end = (int)Math.Ceiling((double)origin + (double)zoom * ((long)destination + length)) + 2;
            for (var s = start; s <= end; s++)
            {
                var l = legacy.SourceIndex(s); var h = hd.SourceIndex(s);
                Assert.AreEqual(Oracle(origin, zoom, destination, length, sourceStart, sourceSize, reflected, s), l, $"trial {trial} legacy s={s}");
                Assert.AreEqual(Oracle(origin, zoom, destination, length, scale * sourceStart, scale * sourceSize, reflected, s), h, $"trial {trial} hd s={s}");
                Assert.AreEqual(l < 0, h < 0, $"trial {trial} coverage s={s}");
                if (l < 0) continue;
                owned++;
                Assert.AreEqual(l, h / scale, $"trial {trial} parent s={s}");
            }
        }
        Assert.IsTrue(owned > 100_000, "the trials must cover many owned pixels");
    }

    [TestMethod]
    public void LiteralScaleTwoIndicesMatchTheContractTable()
    {
        // Columns: origin, zoom, destination, length, crop start, crop size, reflected, first screen pixel, Legacy, HD (-1 = excluded).
        var cases = new (float O, float Z, int D, int N, int C, int M, bool R, int S, int[] Legacy, int[] Hd)[]
        {
            (100, .5f, 0, 8, 2, 8, false, 99, [-1, 3, 5, 7, 9, -1], [-1, 6, 10, 14, 18, -1]),
            (100, .5f, 0, 8, 2, 8, true, 99, [-1, 8, 6, 4, 2, -1], [-1, 17, 13, 9, 5, -1]),
            (0, 1, 0, 4, 0, 4, false, -1, [-1, 0, 1, 2, 3, -1], [-1, 1, 3, 5, 7, -1]),
            (0, 1, 0, 4, 0, 4, true, -1, [-1, 3, 2, 1, 0, -1], [-1, 6, 4, 2, 0, -1]),
            (0, 2, 0, 4, 0, 4, false, -1, [-1, 0, 0, 1, 1, 2, 2, 3, 3, -1], [-1, 0, 1, 2, 3, 4, 5, 6, 7, -1]),
            (0, 1, 0, 3, 0, 6, false, -1, [-1, 1, 3, 5, -1], [-1, 2, 6, 10, -1]),
        };
        foreach (var (o, z, d, n, c, m, r, s, expectedLegacy, expectedHd) in cases)
        {
            var legacy = new AreaNearestAxis(o, z, d, n, c, m, r); var hd = new AreaNearestAxis(o, z, d, n, 2 * c, 2 * m, r);
            var pixels = Enumerable.Range(s, expectedLegacy.Length).ToArray();
            CollectionAssert.AreEqual(expectedLegacy, pixels.Select(legacy.SourceIndex).ToArray(), $"legacy o={o} z={z} r={r}");
            CollectionAssert.AreEqual(expectedHd, pixels.Select(hd.SourceIndex).ToArray(), $"hd o={o} z={z} r={r}");
        }
    }

    [TestMethod]
    public void ScaledCropBeyondTheIndexRowEncodingIsRefused()
    {
        // Index rows encode index + 1 in 16 bits: an HD crop ending past 65534 cannot be drawn
        // and falls back to Legacy (HdAxisLimit); at N = 2 every Legacy axis up to 32,767 fits.
        _ = new AreaNearestAxis(0, 1, 0, 8, 0, 2 * 32767, false);
        Assert.ThrowsExactly<InvalidOperationException>(() => new AreaNearestAxis(0, 1, 0, 8, 2 * 1, 2 * 32767, false));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AreaNearestAxis(0, 1, 0, 8, 0, 2 * 32768, false));
    }

    [TestMethod]
    public void HdRowKeysReproduceTheRowAndStayInvariantUnderIntegerPans()
    {
        var random = new Random(17);
        var zooms = new[] { .25f, .5f, 1f, 1.2651956f, 2f, 3f };
        for (var trial = 0; trial < 1000; trial++)
        {
            var zoom = zooms[random.Next(zooms.Length)];
            var origin = random.Next(-2000, 2000) + (trial % 3 == 0 ? 0f : random.Next(0, 64) / 64f);
            var destination = random.Next(-3000, 3000); var length = random.Next(1, 120);
            var sourceStart = random.Next(0, 40); var sourceSize = random.Next(1, 120); var reflected = random.Next(2) == 1;
            var hd = new AreaNearestAxis(origin, zoom, destination, length, 2 * sourceStart, 2 * sourceSize, reflected);
            var start = (int)Math.Floor((double)origin + (double)zoom * destination) - 1;
            var width = (int)Math.Ceiling((double)origin + (double)zoom * ((long)destination + length)) + 1 - start;
            var key = hd.RowKey(start);
            CollectionAssert.AreEqual(Enumerable.Range(0, width).Select(i => hd.SourceIndex(start + i)).ToArray(),
                Enumerable.Range(0, width).Select(key.SourceIndex).ToArray(), $"trial {trial}");
            var shift = random.Next(-300, 300);
            var moved = new AreaNearestAxis(origin + shift, zoom, destination, length, 2 * sourceStart, 2 * sourceSize, reflected);
            Assert.AreEqual(key, moved.RowKey(start + shift), $"trial {trial}");
        }
    }

    [TestMethod]
    public void NearestReplicatedHdRastersIdenticallyToLegacy()
    {
        var random = new Random(21);
        var frames = Enumerable.Range(0, 10).Select(_ => Frame(random, random.Next(5, 48), random.Next(5, 64))).ToArray();
        var hdFrames = frames.Select(frame => Replicate(frame, 2)).ToArray();
        var items = new List<Item>();
        for (var i = 0; i < 90; i++)
        {
            var f = random.Next(frames.Length); var (w, h, _) = frames[f];
            var cw = random.Next(2) == 0 ? w : random.Next(1, w + 1); var ch = random.Next(2) == 0 ? h : random.Next(1, h + 1);
            var crop = new Rect(random.Next(0, w - cw + 1), random.Next(0, h - ch + 1), cw, ch);
            // Plan-like destinations: unscaled, shrunk (half) and scaled bodies, all inside one view.
            var (dw, dh) = (i % 3) switch { 0 => (cw, ch), 1 => (Math.Max(1, cw / 2), Math.Max(1, ch / 2)), _ => (cw * 3 / 2, ch * 3 / 2) };
            items.Add(new(f, crop, new Rect(random.Next(-400, 400), random.Next(-300, 300), dw, dh), random.Next(2) == 0, random.Next(5) == 0));
        }
        foreach (var (zoom, ox, oy) in new[] { (.5f, 320f, 200f), (1f, 320f, 200f), (1f, 320.5f, 199.25f), (1.2651956f, 333.75f, 201.5f),
                     (2f, 640f, 400f), (2f, 641.5f, 399.75f), (3f, 960f, 600f) })
        {
            const int width = 640, height = 400;
            var legacy = Raster(width, height, zoom, ox, oy, items.Select(item => (item, frames[item.Frame])), 1);
            var hd = Raster(width, height, zoom, ox, oy, items.Select(item => (item, hdFrames[item.Frame])), 2);
            Assert.IsTrue(legacy.Where((value, index) => index % 4 == 3 && value == 255).Count() > 2000, $"zoom {zoom}: the view must show sprites");
            CollectionAssert.AreEqual(legacy, hd, $"zoom {zoom} origin ({ox},{oy})");
        }
    }

    [TestMethod]
    public void HdSubtexelsStayInsideTheirLegacyTexelAndShowDetailOnlyAboveUnitMagnification()
    {
        const int w = 23, h = 17;
        var all = new[] { (0, 0), (0, 1), (1, 0), (1, 1) };
        // Magnification is zoom x destination / frame: zoom 1 with a twice-size destination behaves like zoom 2.
        foreach (var (zoom, size, reflected, phases, distinct) in new[] { (1f, 1, false, new[] { (1, 1) }, w * h), (1f, 1, true, new[] { (0, 1) }, w * h),
                     (2f, 1, false, all, 4 * w * h), (2f, 1, true, all, 4 * w * h), (1f, 2, false, all, 4 * w * h), (1f, 2, true, all, 4 * w * h) })
        {
            var lx = new AreaNearestAxis(100, zoom, 7, size * w, 0, w, reflected); var ly = new AreaNearestAxis(50, zoom, -3, size * h, 0, h, false);
            var hx = new AreaNearestAxis(100, zoom, 7, size * w, 0, 2 * w, reflected); var hy = new AreaNearestAxis(50, zoom, -3, size * h, 0, 2 * h, false);
            var seenPhases = new HashSet<(int, int)>(); var seenTexels = new HashSet<(int, int)>();
            for (var sy = 0; sy < 200; sy++)
            for (var sx = 0; sx < 300; sx++)
            {
                var (x, y, X, Y) = (lx.SourceIndex(sx), ly.SourceIndex(sy), hx.SourceIndex(sx), hy.SourceIndex(sy));
                Assert.AreEqual(x < 0 || y < 0, X < 0 || Y < 0, $"zoom {zoom} ({sx},{sy})");
                if (x < 0 || y < 0) continue;
                Assert.AreEqual((x, y), (X / 2, Y / 2), $"zoom {zoom} ({sx},{sy})");
                seenPhases.Add((X % 2, Y % 2)); seenTexels.Add((X, Y));
            }
            CollectionAssert.AreEquivalent(phases, seenPhases.ToArray(), $"zoom {zoom} size {size} reflected {reflected}");
            Assert.AreEqual(distinct, seenTexels.Count, $"zoom {zoom} size {size} reflected {reflected}");
        }
    }

    [TestMethod]
    public void WallFadeFromTheHdLegacyColumnEqualsLegacyFade()
    {
        // The ARC-RENDER-011 shader: alpha = left + column * (right - left) / width, column mirrored when flipped.
        // The contract's column is floor((sx - N*c) / N) with the Legacy crop width (Legacy: N = 1).
        static double Alpha(int sx, int scale, int cropStart, int width, bool flip, double left, double right)
        {
            var column = (sx - scale * cropStart) / scale;
            return left + (flip ? width - 1 - column : column) * (right - left) / width;
        }
        var random = new Random(29); var finerDiffers = 0; var compared = 0;
        foreach (var zoom in new[] { .5f, 1f, 1.2651956f, 2f, 3f })
        for (var trial = 0; trial < 200; trial++)
        {
            var width = random.Next(2, 160); var crop = trial % 2 == 0 ? 0 : random.Next(1, 30); var flip = random.Next(2) == 0;
            var origin = random.Next(-50, 50) + random.Next(0, 4) / 4f;
            var (left, right) = random.Next(2) == 0 ? (128 / 255d, 1d) : (1d, 128 / 255d);
            var legacy = new AreaNearestAxis(origin, zoom, 0, width, crop, width, flip); var hd = new AreaNearestAxis(origin, zoom, 0, width, 2 * crop, 2 * width, flip);
            for (var s = (int)Math.Floor(origin) - 1; s <= (int)Math.Ceiling(origin + zoom * width) + 1; s++)
            {
                var l = legacy.SourceIndex(s); var hIndex = hd.SourceIndex(s);
                if (l < 0) { Assert.AreEqual(-1, hIndex); continue; }
                var expected = Alpha(l, 1, crop, width, flip, left, right); compared++;
                Assert.AreEqual(expected, Alpha(hIndex, 2, crop, width, flip, left, right), $"zoom {zoom} crop {crop} s={s}");
                // Today's shader on an unchanged HD bind: column = sx - N*c over N*width (the rejected finer gradient).
                if (expected != Alpha(hIndex - crop, 1, crop, 2 * width, flip, left, right)) finerDiffers++;
            }
        }
        Assert.IsTrue(compared > 10_000 && finerDiffers > compared / 4, $"{finerDiffers} of {compared}: the N*width gradient is a different rule");
    }

    private readonly record struct Rect(int X, int Y, int Width, int Height);
    private readonly record struct Item(int Frame, Rect Crop, Rect Destination, bool FlipX, bool FlipY);

    private static (int Width, int Height, byte[] Rgba) Frame(Random random, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = (byte)random.Next(256); rgba[i * 4 + 1] = (byte)random.Next(256); rgba[i * 4 + 2] = (byte)random.Next(256);
            rgba[i * 4 + 3] = random.Next(3) == 0 ? (byte)0 : (byte)255;
        }
        return (width, height, rgba);
    }

    // Synthetic "HD": every Legacy texel becomes an N x N block of itself (nearest upscale).
    private static (int Width, int Height, byte[] Rgba) Replicate((int Width, int Height, byte[] Rgba) frame, int scale)
    {
        var (w, h) = (frame.Width * scale, frame.Height * scale); var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            Array.Copy(frame.Rgba, ((y / scale) * frame.Width + x / scale) * 4, rgba, (y * w + x) * 4, 4);
        return (w, h, rgba);
    }

    // Painter-order raster with binary alpha; the crop is scaled by the frame's N and the
    // destination, origin, zoom and reflection are the Legacy ones.
    private static byte[] Raster(int width, int height, float zoom, float originX, float originY,
        IEnumerable<(Item Item, (int Width, int Height, byte[] Rgba) Frame)> items, int scale)
    {
        var image = new byte[width * height * 4];
        foreach (var (item, frame) in items)
        {
            var (c, d) = (item.Crop, item.Destination);
            var x = new AreaNearestAxis(originX, zoom, d.X, d.Width, scale * c.X, scale * c.Width, item.FlipX);
            var y = new AreaNearestAxis(originY, zoom, d.Y, d.Height, scale * c.Y, scale * c.Height, item.FlipY);
            var columns = Enumerable.Range(0, width).Select(x.SourceIndex).ToArray();
            for (var py = 0; py < height; py++)
            {
                var sy = y.SourceIndex(py); if (sy < 0) continue;
                for (var px = 0; px < width; px++)
                {
                    var sx = columns[px]; if (sx < 0) continue;
                    var at = (sy * frame.Width + sx) * 4;
                    if (frame.Rgba[at + 3] != 255) continue;
                    var to = (py * width + px) * 4;
                    image[to] = frame.Rgba[at]; image[to + 1] = frame.Rgba[at + 1]; image[to + 2] = frame.Rgba[at + 2]; image[to + 3] = 255;
                }
            }
        }
        return image;
    }

    // Independent exact oracle of u = ((s + 1/2 - o) / z - d) * m / n over the binary values of o and z.
    private static int Oracle(float origin, float zoom, int destination, int length, int start, int size, bool reflected, int screen)
    {
        var (on, od) = Exact(origin); var (zn, zd) = Exact(zoom);
        var numerator = (((2 * (BigInteger)screen + 1) * od - 2 * on) * zd - 2 * od * zn * destination) * size;
        var denominator = 2 * od * zn * length;
        if (numerator < 0 || numerator >= denominator * size) return -1;
        var index = (int)(numerator / denominator);
        return start + (reflected ? size - 1 - index : index);
    }

    private static (BigInteger Numerator, BigInteger Denominator) Exact(float value)
    {
        double scaled = value; var denominator = BigInteger.One;
        while (scaled != Math.Floor(scaled)) { scaled *= 2; denominator *= 2; }
        return (new BigInteger(scaled), denominator);
    }
}
