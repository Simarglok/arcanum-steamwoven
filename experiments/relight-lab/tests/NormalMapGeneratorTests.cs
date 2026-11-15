using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Experiments.RelightLab.Tests;

[TestClass]
public sealed class NormalMapGeneratorTests
{
    private static byte[] Solid(int w, int h, Func<int, int, byte> value, byte alpha = 255)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) { var i = (y * w + x) * 4; rgba[i] = rgba[i + 1] = rgba[i + 2] = value(x, y); rgba[i + 3] = alpha; }
        return rgba;
    }

    [TestMethod]
    public void A_flat_sprite_faces_the_viewer()
    {
        var rgba = Solid(8, 8, (_, _) => 100);
        var map = NormalMapGenerator.Generate(rgba, 8, 8, NormalMapOptions.For(SpriteKind.Object), NormalConvention.ImageSpace);
        for (var y = 1; y < 7; y++)
            for (var x = 1; x < 7; x++) { var (nx, ny, nz) = map.Normal(x, y); Assert.AreEqual(0, nx, 0.02); Assert.AreEqual(0, ny, 0.02); Assert.AreEqual(1, nz, 0.02); }
    }

    [TestMethod]
    public void A_brightening_ramp_tilts_the_normal_against_the_slope_in_both_conventions()
    {
        var rgba = Solid(8, 8, (x, _) => (byte)(40 + x * 20));
        var image = NormalMapGenerator.Generate(rgba, 8, 8, new NormalMapOptions(0.5, 0, 0), NormalConvention.ImageSpace);
        Assert.IsTrue(image.Normal(4, 4).X < -0.05, "height rises to the right, so the surface faces left");
        var gl = NormalMapGenerator.Generate(rgba, 8, 8, new NormalMapOptions(0.5, 0, 0), NormalConvention.OpenGl);
        Assert.AreEqual(image.Normal(4, 4).X, gl.Normal(4, 4).X, 0.02);
        // A ramp that rises downwards: image space tilts up (negative y), OpenGL stores the up component in green as positive.
        var down = Solid(8, 8, (_, y) => (byte)(40 + y * 20));
        var downImage = NormalMapGenerator.Generate(down, 8, 8, new NormalMapOptions(0.5, 0, 0), NormalConvention.ImageSpace);
        var downGl = NormalMapGenerator.Generate(down, 8, 8, new NormalMapOptions(0.5, 0, 0), NormalConvention.OpenGl);
        Assert.IsTrue(downImage.Normal(4, 4).Y < -0.05);
        Assert.IsTrue(downGl.Rgba[(4 * 8 + 4) * 4 + 1] > 128, "OpenGL green is the up component");
        Assert.AreEqual(downImage.Normal(4, 4).Y, downGl.Normal(4, 4).Y, 0.02, "Normal() reads both conventions back to image space");
    }

    [TestMethod]
    public void Empty_texels_are_flat_and_keep_their_alpha_and_a_silhouette_edge_bevels_outwards()
    {
        var rgba = Solid(8, 8, (_, _) => 120);
        for (var y = 0; y < 8; y++) for (var x = 5; x < 8; x++) rgba[(y * 8 + x) * 4 + 3] = 0;
        var map = NormalMapGenerator.Generate(rgba, 8, 8, NormalMapOptions.For(SpriteKind.Object), NormalConvention.ImageSpace);
        Assert.AreEqual(0, map.Rgba[(4 * 8 + 6) * 4 + 3]);
        Assert.AreEqual(255, map.Rgba[(4 * 8 + 6) * 4 + 2], "an empty texel is facing the viewer");
        Assert.IsTrue(map.Normal(4, 4).X > 0.05, "the last solid column leans out to the right");
        Assert.AreEqual(0, map.Normal(1, 4).X, 0.05, "away from the edge it stays flat");
        var noBevel = NormalMapGenerator.Generate(rgba, 8, 8, new NormalMapOptions(0.5, 0, 0), NormalConvention.ImageSpace);
        Assert.AreEqual(0, noBevel.Normal(4, 4).X, 0.02, "bevel 0 leaves a silhouette flat");
    }

    [TestMethod]
    public void The_same_pixels_and_options_give_the_same_bytes_and_blur_calms_noise()
    {
        var random = new Random(7);
        var noise = Solid(16, 16, (_, _) => (byte)(100 + random.Next(-12, 13)));
        var a = NormalMapGenerator.Generate(noise, 16, 16, NormalMapOptions.For(SpriteKind.Critter));
        var b = NormalMapGenerator.Generate(noise, 16, 16, NormalMapOptions.For(SpriteKind.Critter));
        CollectionAssert.AreEqual(a.Rgba, b.Rgba);
        var sharp = NormalMapGenerator.Generate(noise, 16, 16, new NormalMapOptions(0.5, 0, 0));
        var flat = NormalMapGenerator.Generate(Solid(16, 16, (_, _) => 100), 16, 16, new NormalMapOptions(0.5, 0, 0));
        var blurred = NormalMapGenerator.Generate(noise, 16, 16, new NormalMapOptions(0.5, 2, 0));
        Assert.IsTrue(NormalMapGenerator.MeanAngleDegrees(blurred, flat) < NormalMapGenerator.MeanAngleDegrees(sharp, flat), "a blur keeps the normals nearer flat on noise");
    }

    [TestMethod]
    public void Ground_is_flatter_than_an_object_on_the_same_pixels()
    {
        var rgba = Solid(16, 16, (x, y) => (byte)(60 + (x * 7 + y * 3) % 90));
        var flat = NormalMapGenerator.Generate(Solid(16, 16, (_, _) => 100), 16, 16, NormalMapOptions.For(SpriteKind.Ground));
        var ground = NormalMapGenerator.Generate(rgba, 16, 16, NormalMapOptions.For(SpriteKind.Ground));
        var wall = NormalMapGenerator.Generate(rgba, 16, 16, NormalMapOptions.For(SpriteKind.Object));
        Assert.IsTrue(NormalMapGenerator.MeanAngleDegrees(ground, flat) < NormalMapGenerator.MeanAngleDegrees(wall, flat));
    }

    [TestMethod]
    public void An_identical_frame_has_no_shimmer_and_the_ratio_is_finite_for_a_changed_one()
    {
        var a = Solid(16, 16, (x, y) => (byte)(50 + x * 6 + y * 2));
        var b = Solid(16, 16, (x, y) => (byte)(52 + x * 6 + y * 2));
        var options = NormalMapOptions.For(SpriteKind.Critter);
        var na = NormalMapGenerator.Generate(a, 16, 16, options); var nb = NormalMapGenerator.Generate(b, 16, 16, options);
        Assert.AreEqual(0, NormalMapGenerator.ShimmerRatio(a, na, a, na, (-1, -1, 1)));
        var ratio = NormalMapGenerator.ShimmerRatio(a, na, b, nb, (-1, -1, 1));
        Assert.IsTrue(ratio >= 0 && ratio < 1, "a uniform brightness shift does not bend the normals: " + ratio);
        Assert.AreEqual(0, NormalMapGenerator.MeanAngleDegrees(na, na), 1e-3);
    }

    [TestMethod]
    public void A_bad_buffer_or_blur_is_refused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => NormalMapGenerator.Generate(new byte[10], 2, 2, NormalMapOptions.For(SpriteKind.Object)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NormalMapGenerator.Generate(new byte[16], 2, 2, new NormalMapOptions(0.5, 9, 0)));
    }

    [TestMethod]
    public void An_n_times_smooth_redraw_with_the_gain_times_n_bends_the_normal_like_the_original()
    {
        // ARC-RENDER-2060: the shader scales the Sobel gain with the HD frame's scale N (relight_scale): an N-times copy has 1/N of the luminance step per texel.
        const int size = 24, scale = 4;
        double Height(double x, double y) => 128 + 60 * Math.Sin(x * 0.35) * Math.Cos(y * 0.28);
        var legacy = Solid(size, size, (x, y) => (byte)Math.Round(Height(x, y)));
        var hd = Solid(size * scale, size * scale, (x, y) => (byte)Math.Round(Height((x + 0.5) / scale - 0.5, (y + 0.5) / scale - 0.5)));
        var options = NormalMapOptions.For(SpriteKind.Object) with { Bevel = 0 };
        var legacyMap = NormalMapGenerator.Generate(legacy, size, size, options, NormalConvention.ImageSpace);
        var scaled = NormalMapGenerator.Generate(hd, size * scale, size * scale, options with { Gain = options.Gain * scale }, NormalConvention.ImageSpace);
        var unscaled = NormalMapGenerator.Generate(hd, size * scale, size * scale, options, NormalConvention.ImageSpace);
        double Tilt(NormalMap map, int x, int y) { var (nx, ny, _) = map.Normal(x, y); return Math.Sqrt(nx * nx + ny * ny); }
        double scaledError = 0, unscaledError = 0; var n = 0;
        for (var y = 4; y < size - 4; y++)
            for (var x = 4; x < size - 4; x++)
            {
                var reference = Tilt(legacyMap, x, y);
                scaledError += Math.Abs(Tilt(scaled, x * scale + scale / 2, y * scale + scale / 2) - reference);
                unscaledError += Math.Abs(Tilt(unscaled, x * scale + scale / 2, y * scale + scale / 2) - reference);
                n++;
            }
        scaledError /= n; unscaledError /= n;
        Assert.IsTrue(scaledError < unscaledError / 3, $"gain x N keeps the HD tilt near the 1x tilt ({scaledError:0.000} against {unscaledError:0.000} unscaled)");
    }

    [TestMethod]
    public void An_n_times_copy_with_the_bevel_stride_n_bends_the_silhouette_like_the_original_and_stride_one_does_not()
    {
        // ARC-RENDER-2063: the shader reads the alpha taps of the bevel N texels away on an N-times frame (rl_taps stride); the twin's BevelStride is that option.
        const int size = 16, scale = 4;
        bool Inside(int x, int y) => x >= 4 && x < 12 && y >= 5 && y < 11;
        var legacy = Solid(size, size, (_, _) => 120);
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) legacy[(y * size + x) * 4 + 3] = Inside(x, y) ? (byte)255 : (byte)0;
        var hd = Solid(size * scale, size * scale, (_, _) => 120);
        for (var y = 0; y < size * scale; y++) for (var x = 0; x < size * scale; x++) hd[(y * size * scale + x) * 4 + 3] = Inside(x / scale, y / scale) ? (byte)255 : (byte)0;
        var options = NormalMapOptions.For(SpriteKind.Object);
        var legacyMap = NormalMapGenerator.Generate(legacy, size, size, options, NormalConvention.ImageSpace);
        var strided = NormalMapGenerator.Generate(hd, size * scale, size * scale, options with { BevelStride = scale }, NormalConvention.ImageSpace);
        var plain = NormalMapGenerator.Generate(hd, size * scale, size * scale, options, NormalConvention.ImageSpace);
        double stridedError = 0, plainError = 0, edgeTilt = 0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (!Inside(x, y)) continue;
                var (lx, ly, _) = legacyMap.Normal(x, y);
                var (sx, sy, _) = strided.Normal(x * scale + scale / 2, y * scale + scale / 2);
                var (px, py, _) = plain.Normal(x * scale + scale / 2, y * scale + scale / 2);
                stridedError += Math.Abs(sx - lx) + Math.Abs(sy - ly);
                plainError += Math.Abs(px - lx) + Math.Abs(py - ly);
                edgeTilt += Math.Abs(lx) + Math.Abs(ly);
            }
        Assert.IsTrue(edgeTilt > 1, $"the 1x silhouette bends its edge texels ({edgeTilt:0.00})");
        Assert.IsTrue(stridedError < 0.05, $"stride N gives the 1x bevel at every Legacy-texel centre of the HD copy ({stridedError:0.000})");
        Assert.IsTrue(plainError > edgeTilt * 0.5, $"stride 1 leaves the edge of an HD copy flat at the texel centres ({plainError:0.000} against the 1x tilt {edgeTilt:0.000})");
    }

    [TestMethod]
    public void The_bevel_stride_is_one_by_default_and_is_bounded()
    {
        Assert.AreEqual(1, NormalMapOptions.For(SpriteKind.Object).BevelStride);
        var rgba = Solid(4, 4, (_, _) => 100);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NormalMapGenerator.Generate(rgba, 4, 4, NormalMapOptions.For(SpriteKind.Object) with { BevelStride = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NormalMapGenerator.Generate(rgba, 4, 4, NormalMapOptions.For(SpriteKind.Object) with { BevelStride = 17 }));
    }
}
