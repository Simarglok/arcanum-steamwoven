using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-140: the generated interface chrome on synthetic frames (no game art).</summary>
[TestClass]
public sealed class InterfaceChromeTests
{
    private const int S = InterfaceChromeRenderer.Scale;

    // A synthetic opaque frame with a 10 × 8 rectangular hole at (4, 4) and a round hole of diameter 12 at (30, 3).
    private static byte[] Frame(int width, int height, bool holes = true, (byte R, byte G, byte B)? colour = null)
    {
        var rgba = new byte[width * height * 4];
        var (r, g, b) = colour ?? ((byte)90, (byte)60, (byte)30);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var rectHole = holes && x >= 4 && x < 14 && y >= 4 && y < 12;
            double dx = x + 0.5 - 36, dy = y + 0.5 - 9;
            var roundHole = holes && dx * dx + dy * dy < 36;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = rectHole || roundHole ? ((byte)0, (byte)0, (byte)0, (byte)0) : (r, g, b, (byte)255);
        }
        return rgba;
    }

    private static ChromeRecipe Panel(params ChromePart[] parts) => new("test.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.Bottom, parts);

    private static (int R, int G, int B, int A) Pixel(byte[] rgba, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    [TestMethod]
    public void Recipes_are_found_by_the_art_name_hash_only()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.AllItemsAreUnique(names);
        CollectionAssert.IsSubsetOf(new[] { "inttop.art", "intbotom.art", "char_but.art", "combat_button.art", "lilgrnbut.art" }, names);
        foreach (var recipe in InterfaceChromeRecipes.All)
        {
            Assert.AreEqual(HdInterfaceKey.NameSha256(recipe.File), recipe.NameSha256);
            var query = HdInterfaceKey.Query(HdInterfaceKey.Identity(recipe.File.ToUpperInvariant(), [1, 2, 3]), 2, 1, 1, [0, 0, 0, 0]);
            Assert.AreSame(recipe, InterfaceChromeRecipes.For(query), "any frame, the name case-folded like the pack's");
        }
        Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("mainmenuback.art", [1]), 0, 1, 1, [0, 0, 0, 0])));
        Assert.IsNull(InterfaceChromeRecipes.For(null));
    }

    [TestMethod]
    public void Holes_are_found_and_told_round_or_rectangular()
    {
        var holes = InterfaceChromeRenderer.Holes(48, 20, Frame(48, 20));
        Assert.AreEqual(2, holes.Count);
        Assert.AreEqual(new InterfaceChromeRenderer.Hole(4, 4, 14, 12, false), holes.Single(h => !h.Round));
        Assert.IsTrue(holes.Single(h => h.Round) is { X0: 30, Y0: 3, X1: 42, Y1: 15 });
        Assert.AreEqual(0, InterfaceChromeRenderer.Holes(48, 20, Frame(48, 20, holes: false)).Count);
    }

    [TestMethod]
    public void Only_near_square_holes_are_round_and_specks_are_skipped()
    {
        // A 20 × 7 slot with cut corners fills ~0.9 of its box but is no circle; a 2-pixel speck is no hole.
        const int W = 40, H = 20;
        var legacy = Frame(W, H, holes: false);
        for (var y = 6; y < 13; y++)
        for (var x = 10; x < 30; x++)
        {
            var corner = (x is 10 or 29) && (y is 6 or 12);
            if (!corner) legacy[(y * W + x) * 4 + 3] = 0;
        }
        legacy[(2 * W + 2) * 4 + 3] = 0; legacy[(2 * W + 3) * 4 + 3] = 0;
        var holes = InterfaceChromeRenderer.Holes(W, H, legacy);
        Assert.AreEqual(1, holes.Count);
        Assert.IsFalse(holes[0].Round);
        var output = InterfaceChromeRenderer.Render(Panel(), W, H, legacy, [], 0);
        Assert.AreEqual(255, Pixel(output, W * S, 20 * S, 2 * S).A, "the wood above the slot stays opaque");
        Assert.AreEqual(255, Pixel(output, W * S, 10 * S + 1, 6 * S + 1).A, "an opaque corner inside the slot's box stays");
        Assert.AreNotEqual((20, 14, 8), (Pixel(output, W * S, 10 * S + 1, 6 * S + 1).R, Pixel(output, W * S, 10 * S + 1, 6 * S + 1).G, Pixel(output, W * S, 10 * S + 1, 6 * S + 1).B),
            "and is not inked over");
        var transparent = Pixel(output, W * S, 20 * S, 9 * S);
        Assert.AreEqual((20, 14, 8, 0), transparent, "a transparent texel carries ink");
    }

    [TestMethod]
    public void A_panel_keeps_the_legacy_silhouette_and_is_deterministic()
    {
        var legacy = Frame(48, 20);
        var a = InterfaceChromeRenderer.Render(Panel(), 48, 20, legacy, [], 0);
        var b = InterfaceChromeRenderer.Render(Panel(), 48, 20, legacy, [], 0);
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(48 * S * 20 * S * 4, a.Length);
        for (var y = 0; y < 20 * S; y++)
        for (var x = 0; x < 48 * S; x++)
        {
            var legacyAlpha = legacy[((y / S) * 48 + x / S) * 4 + 3];
            var alpha = a[(y * 48 * S + x) * 4 + 3];
            double dx = x + 0.5 - 36 * S, dy = y + 0.5 - 9 * S;
            var nearRoundEdge = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 6 * S) < 2 * S;
            if (!nearRoundEdge) Assert.AreEqual(legacyAlpha, alpha, $"({x},{y})");
        }
        Assert.AreEqual(0, Pixel(a, 48 * S, 36 * S, 9 * S).A, "a round socket stays open");
        var ring = Pixel(a, 48 * S, 36 * S, 9 * S - 6 * S - (int)(1.5 * S));
        Assert.IsTrue(ring.A == 255 && ring.R > ring.B + 40, $"a brass ring above it: {ring}");
        var frame = Pixel(a, 48 * S, 9 * S, 4 * S - (int)(1.5 * S));
        Assert.IsTrue(frame.R > frame.B + 40, $"a brass frame above the rectangular hole: {frame}");
        var rim = Pixel(a, 48 * S, 20, 20 * S - 1);
        Assert.AreEqual((20, 14, 8, 255), rim, "the bottom rim's ink line");
    }

    [TestMethod]
    public void Keep_regions_copy_the_picture_and_parts_paint_their_rectangles()
    {
        var legacy = Frame(48, 20, holes: false);
        // The kept rectangle's Legacy pixels are green; the wood around it (the frame's own colour) is not.
        for (var y = 2; y < 6; y++)
        for (var x = 34; x < 38; x++)
            (legacy[(y * 48 + x) * 4], legacy[(y * 48 + x) * 4 + 1], legacy[(y * 48 + x) * 4 + 2]) = (10, 200, 10);
        var picture = new byte[48 * S * 20 * S * 4];
        for (var i = 0; i < picture.Length; i += 4) (picture[i], picture[i + 1], picture[i + 2], picture[i + 3]) = (1, 2, 3, 255);
        var recipe = Panel(new ChromeSunk(20, 2, 10, 10, ChromeMaterial.Dark), new ChromeKeep(2, 2, 4, 4), new ChromeKeep(34, 2, 4, 4));
        var withPicture = InterfaceChromeRenderer.Render(recipe, 48, 20, legacy, picture, 0);
        Assert.AreEqual((1, 2, 3, 255), Pixel(withPicture, 48 * S, 3 * S, 3 * S), "the HD picture where given");
        var withLegacy = InterfaceChromeRenderer.Render(recipe, 48, 20, legacy, [], 0);
        Assert.AreEqual((10, 200, 10, 255), Pixel(withLegacy, 48 * S, 35 * S + 1, 3 * S + 2), "else the Legacy pixel");
        var sunk = Pixel(withLegacy, 48 * S, 20 * S, 6 * S);
        Assert.AreEqual((96, 70, 30, 255), sunk, "a sunk panel's shaded left bevel");
        var inside = Pixel(withLegacy, 48 * S, 25 * S, 7 * S);
        Assert.IsTrue(inside.R < 30 && inside.G < 30, $"its dark fill: {inside}");
        var wood = Pixel(withLegacy, 48 * S, 10 * S, 10 * S);
        Assert.IsTrue(wood.R > wood.B && wood.G != 200, $"the rest is generated wood: {wood}");
        Assert.IsTrue(Math.Abs(wood.R - 90) <= 4 && Math.Abs(wood.G - 60) <= 4 && Math.Abs(wood.B - 30) <= 4, $"in the tone of the frame's own wood: {wood}");
    }

    [TestMethod]
    public void A_button_gets_a_rim_that_sinks_on_its_down_frame_and_keeps_its_glyph()
    {
        const int N = 20;
        var legacy = Frame(N, N, holes: false, colour: (40, 40, 20));
        for (var y = 8; y < 12; y++)
        for (var x = 8; x < 12; x++)
            (legacy[(y * N + x) * 4], legacy[(y * N + x) * 4 + 1], legacy[(y * N + x) * 4 + 2]) = (220, 30, 30);
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "char_but.art");
        var idle = InterfaceChromeRenderer.Render(recipe, N, N, legacy, [], 0);
        var down = InterfaceChromeRenderer.Render(recipe, N, N, legacy, [], recipe.DownFrame);
        var topLeftRim = (S + 2, 10 * S);
        Assert.AreEqual((214, 178, 96, 255), Pixel(idle, N * S, topLeftRim.Item1 + 2, topLeftRim.Item2), "raised: lit on the left");
        Assert.AreEqual((96, 70, 30, 255), Pixel(down, N * S, topLeftRim.Item1 + 2, topLeftRim.Item2), "down: shaded on the left");
        var hover = InterfaceChromeRenderer.Render(recipe, N, N, legacy, [], recipe.HoverFrame);
        var (hr, hg, hb, _) = Pixel(hover, N * S, topLeftRim.Item1 + 2, topLeftRim.Item2);
        Assert.IsTrue(hr > 214 && hg > 178 && hb > 96, $"hover: a lighter rim ({hr},{hg},{hb})");
        Assert.AreNotEqual(Pixel(idle, N * S, 5 * S, 5 * S), Pixel(hover, N * S, 5 * S, 5 * S), "hover: a lighter face");
        Assert.AreEqual((220, 30, 30, 255), Pixel(idle, N * S, 10 * S, 10 * S), "the glyph is the art's");
        Assert.AreEqual((20, 14, 8, 255), Pixel(idle, N * S, 0, 10 * S), "an ink outline");
    }

    [TestMethod]
    public void Bad_inputs_are_refused()
    {
        var recipe = Panel();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(recipe, 0, 4, [], [], 0));
        Assert.ThrowsExactly<ArgumentException>(() => InterfaceChromeRenderer.Render(recipe, 4, 4, new byte[10], [], 0));
        Assert.ThrowsExactly<ArgumentException>(() => InterfaceChromeRenderer.Render(recipe, 4, 4, new byte[64], new byte[5], 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(recipe, 4, 4, new byte[64], [], -1));
    }

    // ARC-UI-141: the exact device scale.
    [TestMethod]
    public void Chrome_is_painted_at_every_whole_device_scale_with_one_unit_lines_of_scale_pixels()
    {
        var legacy = Frame(48, 20);
        foreach (var scale in new[] { 1, 2, 3, 5, 8 })
        {
            var a = InterfaceChromeRenderer.Render(Panel(), 48, 20, legacy, [], 0, scale);
            Assert.AreEqual(48 * scale * 20 * scale * 4, a.Length, $"size at {scale}");
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(Panel(), 48, 20, legacy, [], 0, scale), $"deterministic at {scale}");
            var width = 48 * scale;
            // The bottom rim: an ink line of exactly one unit (scale pixels), then the lit brass line of one unit, away from the holes.
            for (var k = 0; k < scale; k++) Assert.AreEqual((20, 14, 8, 255), Pixel(a, width, 20 * scale, 20 * scale - 1 - k), $"ink line pixel {k} at {scale}");
            Assert.AreNotEqual((20, 14, 8, 255), Pixel(a, width, 20 * scale, 20 * scale - 1 - scale), $"the line is {scale} pixels, no more");
            // The silhouette and the holes are the Legacy frame's, whatever the scale (a square hole's pixels exactly).
            for (var y = 0; y < 20 * scale; y++)
            for (var x = 0; x < 48 * scale; x++)
            {
                if (x >= 20 * scale) break; // the round socket (anti-aliased) is to the right
                Assert.AreEqual(legacy[((y / scale) * 48 + x / scale) * 4 + 3], a[(y * width + x) * 4 + 3], $"alpha ({x},{y}) at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_wood_takes_the_tone_of_the_frames_own_wood_outside_its_parts()
    {
        // A light grey-green wood with a little grain; a sunk part over a different (dark red) area must not count.
        var legacy = Frame(48, 20, holes: false);
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 48; x++)
        {
            var i = (y * 48 + x) * 4;
            var grain = (byte)((x + y) % 2 == 0 ? 10 : 0);
            var inSunk = x >= 20 && x < 30 && y >= 4 && y < 14;
            (legacy[i], legacy[i + 1], legacy[i + 2]) = inSunk ? ((byte)200, (byte)10, (byte)10) : ((byte)(150 + grain), (byte)(160 + grain), (byte)(140 + grain));
        }
        var tuned = InterfaceChromeRenderer.Render(Panel(new ChromeSunk(20, 4, 10, 10, ChromeMaterial.Dark)), 48, 20, legacy, [], 0, 1);
        double sr = 0, sg = 0; var n = 0;
        for (var y = 2; y < 16; y++)
        for (var x = 36; x < 46; x++) { var p = Pixel(tuned, 48, x, y); sr += p.R; sg += p.G; n++; }
        Assert.IsTrue(Math.Abs(sr / n - 155) < 8 && Math.Abs(sg / n - 165) < 8, $"mean wood {sr / n:F0},{sg / n:F0} follows the frame's 155,165");
        var tiny = Frame(4, 4, holes: false, colour: (200, 10, 10));
        var own = InterfaceChromeRenderer.Render(Panel(), 4, 4, tiny, [], 0, 1);
        Assert.IsTrue(Pixel(own, 4, 2, 0).R < 130, "too little wood to read keeps our own palette");
    }

    [TestMethod]
    public void A_button_at_a_whole_scale_keeps_its_outline_and_glyph()
    {
        const int N = 20;
        var legacy = Frame(N, N, holes: false, colour: (40, 40, 20));
        for (var y = 8; y < 12; y++)
        for (var x = 8; x < 12; x++)
            (legacy[(y * N + x) * 4], legacy[(y * N + x) * 4 + 1], legacy[(y * N + x) * 4 + 2]) = (220, 30, 30);
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "char_but.art");
        foreach (var scale in new[] { 1, 2, 3 })
        {
            var idle = InterfaceChromeRenderer.Render(recipe, N, N, legacy, [], 0, scale);
            Assert.AreEqual((20, 14, 8, 255), Pixel(idle, N * scale, 0, 10 * scale), $"an ink outline at {scale}");
            Assert.AreEqual((220, 30, 30, 255), Pixel(idle, N * scale, 10 * scale, 10 * scale), $"the glyph at {scale}");
        }
    }

    [TestMethod]
    public void A_picture_is_averaged_to_the_render_scale()
    {
        // 4 × 4 source pixels: a checkerboard of black and white 2 × 2 blocks (alpha 255).
        var source = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var white = ((x / 2) + (y / 2)) % 2 == 0;
            var i = (y * 4 + x) * 4;
            (source[i], source[i + 1], source[i + 2], source[i + 3]) = white ? ((byte)200, (byte)100, (byte)50, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)255);
        }
        CollectionAssert.AreEqual(source, InterfaceChromeRenderer.ResamplePicture(source, 4, 4, 4, 4), "the same size is returned as it is (it was opaque already)");
        var see = (byte[])source.Clone(); see[3] = 0;
        Assert.AreEqual(255, InterfaceChromeRenderer.ResamplePicture(see, 4, 4, 4, 4)[3], "a transparent texel becomes opaque at the same size too");
        var half = InterfaceChromeRenderer.ResamplePicture(source, 4, 4, 2, 2);
        Assert.AreEqual((200, 100, 50, 255), Pixel(half, 2, 0, 0), "a whole 2:1 box keeps a flat block");
        Assert.AreEqual((0, 0, 0, 255), Pixel(half, 2, 1, 0));
        var one = InterfaceChromeRenderer.ResamplePicture(source, 4, 4, 1, 1);
        Assert.AreEqual((100, 50, 25, 255), Pixel(one, 1, 0, 0), "4:1 is the mean of all sixteen");
        var third = InterfaceChromeRenderer.ResamplePicture(source, 4, 4, 3, 3);
        // Target pixel (1, 1) covers source 1.33..2.67 on both axes: area-weighted mix of all four blocks, so a mid grey.
        var mid = Pixel(third, 3, 1, 1);
        Assert.IsTrue(mid.R is > 60 and < 140 && mid.A == 255, $"4:3 mixes the blocks by area: {mid}");
        var sum = 0;
        for (var i = 0; i < third.Length; i += 4) sum += third[i];
        Assert.IsTrue(Math.Abs(sum / 9.0 - 100) < 6, "the mean is kept at 4:3");
    }

    [TestMethod]
    public void A_transparent_source_texel_adds_no_colour_to_a_picture()
    {
        var source = new byte[2 * 1 * 4];
        (source[0], source[1], source[2], source[3]) = (255, 0, 0, 0);
        (source[4], source[5], source[6], source[7]) = (0, 0, 255, 255);
        var one = InterfaceChromeRenderer.ResamplePicture(source, 2, 1, 1, 1);
        Assert.AreEqual((0, 0, 255, 255), Pixel(one, 1, 0, 0));
    }

    [TestMethod]
    public void Scale_and_picture_bounds_are_refused()
    {
        var recipe = Panel();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(recipe, 4, 4, new byte[64], [], 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(recipe, 4, 4, new byte[64], [], 0, InterfaceChromeRenderer.MaxScale + 1));
        Assert.ThrowsExactly<ArgumentException>(() => InterfaceChromeRenderer.Render(recipe, 4, 4, new byte[64], new byte[4 * 4 * 4 * 4 * 4 + 4], 0, 2));
        Assert.ThrowsExactly<ArgumentException>(() => InterfaceChromeRenderer.ResamplePicture(new byte[10], 2, 2, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.ResamplePicture(new byte[16], 2, 2, 0, 1));
    }

    // ARC-UI-148: a window's own (fractional) device scale and the device-pixel grid rule.
    [TestMethod]
    public void A_fractional_scale_gives_rounded_sizes_the_legacy_silhouette_and_whole_pixel_lines()
    {
        var legacy = Frame(48, 20);
        foreach (var scale in new[] { 1.25, 1.5, 1.75, 2.4, 3.333 })
        {
            var (w, h) = InterfaceChromeRenderer.PixelSize(48, 20, scale);
            Assert.AreEqual((int)Math.Round(48 * scale, MidpointRounding.AwayFromZero), w);
            var a = InterfaceChromeRenderer.Render(Panel(), 48, 20, legacy, [], 0, scale);
            Assert.AreEqual(w * h * 4, a.Length, $"size at {scale}");
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(Panel(), 48, 20, legacy, [], 0, scale), $"deterministic at {scale}");
            // The bottom rim's ink line is max(1, round(scale)) device pixels, the same at every column (equal bands stay equal).
            var line = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
            foreach (var x in new[] { w / 2, w - 3 })
            {
                for (var k = 0; k < line; k++) Assert.AreEqual((20, 14, 8, 255), Pixel(a, w, x, h - 1 - k), $"ink pixel {k} at {scale}");
                Assert.AreNotEqual((20, 14, 8, 255), Pixel(a, w, x, h - 1 - line), $"no more than {line} ink pixels at {scale}");
            }
            // The silhouette is the Legacy frame's: the square hole's device pixels are exactly the rounded edges (the frame is 4..14 × 4..12).
            int E(int v) => (int)Math.Round(v * scale, MidpointRounding.AwayFromZero);
            Assert.AreEqual(0, Pixel(a, w, E(4), E(4)).A, $"hole corner at {scale}");
            Assert.AreEqual(0, Pixel(a, w, E(14) - 1, E(12) - 1).A, $"hole far corner at {scale}");
            Assert.AreEqual(255, Pixel(a, w, E(4) - 1, E(8)).A, $"outside the hole at {scale}");
            Assert.AreEqual(255, Pixel(a, w, E(14), E(8)).A, $"outside the hole at {scale}");
        }
    }

    [TestMethod]
    public void Scales_below_one_and_not_finite_scales_are_refused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(Panel(), 4, 4, new byte[64], [], 0, 0.99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(Panel(), 4, 4, new byte[64], [], 0, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceChromeRenderer.Render(Panel(), 4, 4, new byte[64], [], 0, double.PositiveInfinity));
    }

    [TestMethod]
    public void A_grid_draws_one_unit_lines_on_the_last_pixel_of_each_inner_cell()
    {
        // Four columns of 8 from (2, 2), two rows of 6: vertical lines at x = 9, 17, 25 and one horizontal line at y = 9, over a pit.
        var legacy = Frame(48, 20, holes: false);
        var recipe = Panel(new ChromeSunk(0, 0, 40, 18, ChromeMaterial.Pit), new ChromeGrid(2, 2, 4, 2, 8));
        foreach (var scale in new[] { 1, 2, 3 })
        {
            var a = InterfaceChromeRenderer.Render(recipe, 48, 20, legacy, [], 0, scale);
            var w = 48 * scale;
            var pit = Pixel(a, w, 6 * scale + 1, 4 * scale);
            Assert.AreEqual((3, 3, 5, 255), pit, "the pit is flat near-black");
            foreach (var lineX in new[] { 9, 17, 25 })
                for (var k = 0; k < scale; k++) Assert.AreEqual((48, 54, 64, 255), Pixel(a, w, lineX * scale + k, 4 * scale), $"line {lineX} pixel {k} at {scale}");
            Assert.AreEqual(pit, Pixel(a, w, 10 * scale, 4 * scale), "between the lines the pit shows");
            for (var k = 0; k < scale; k++) Assert.AreEqual((48, 54, 64, 255), Pixel(a, w, 12 * scale, 9 * scale + k), $"the row line at {scale}");
            Assert.AreEqual(pit, Pixel(a, w, 12 * scale, 8 * scale), "no line above the row line");
        }
    }

    [TestMethod]
    public void The_legacy_silhouette_changes_exactly_at_the_rounded_edges_at_every_scale()
    {
        // The left 22 columns of a 44-wide frame are opaque: the output's alpha must change at device column round(22 × scale), including at scales whose products
        // sit on a rounding tie (1.005, 1.13, 2.05 ...).
        const int W = 44, H = 6;
        var legacy = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < 22; x++) { var i = (y * W + x) * 4; legacy[i] = 90; legacy[i + 1] = 60; legacy[i + 2] = 30; legacy[i + 3] = 255; }
        for (var scale = 1.0; scale <= 4.0; scale += 0.005)
        {
            var a = InterfaceChromeRenderer.Render(Panel(), W, H, legacy, [], 0, scale);
            var (w, h) = InterfaceChromeRenderer.PixelSize(W, H, scale);
            var edge = (int)Math.Round(22 * scale, MidpointRounding.AwayFromZero);
            Assert.AreEqual(255, Pixel(a, w, edge - 1, h / 2).A, $"last opaque column at {scale}");
            Assert.AreEqual(0, Pixel(a, w, edge, h / 2).A, $"first transparent column at {scale}");
        }
    }

    [TestMethod]
    public void A_grids_lines_are_the_last_pixels_of_their_cells_at_a_fractional_scale()
    {
        var legacy = Frame(48, 20, holes: false);
        var recipe = Panel(new ChromeSunk(0, 0, 40, 18, ChromeMaterial.Pit), new ChromeGrid(2, 2, 4, 2, 8));
        foreach (var scale in new[] { 1.5, 2.4, 3.333 })
        {
            var a = InterfaceChromeRenderer.Render(recipe, 48, 20, legacy, [], 0, scale);
            var (w, _) = InterfaceChromeRenderer.PixelSize(48, 20, scale);
            var line = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
            var y = (int)Math.Round(4 * scale, MidpointRounding.AwayFromZero);
            foreach (var cell in new[] { 1, 2, 3 })
            {
                var end = (int)Math.Round((2 + 8 * cell) * scale, MidpointRounding.AwayFromZero);
                for (var k = 1; k <= line; k++) Assert.AreEqual((48, 54, 64, 255), Pixel(a, w, end - k, y), $"line pixel {k} of cell {cell} at {scale}");
                Assert.AreNotEqual((48, 54, 64, 255), Pixel(a, w, end, y), $"nothing over the next cell at {scale}");
                Assert.AreNotEqual((48, 54, 64, 255), Pixel(a, w, end - line - 1, y), $"one line only at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_inventory_recipes_paint_in_bounds_at_whole_and_fractional_scales()
    {
        // Synthetic frames of the two windows' sizes (no game art): opaque wood colour everywhere.
        foreach (var (file, w, h) in new[] { ("inventor.art", 442, 400), ("pdoll.art", 358, 400) })
        {
            var legacy = Frame(w, h, holes: false);
            var recipe = InterfaceChromeRecipes.All.Single(r => r.File == file);
            foreach (var scale in new[] { 1.0, 1.345, 2.0, 2.5 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} is opaque like its Legacy frame at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_inventory_window_frames_have_recipes_and_no_other_window_frame_does_yet()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "inventor.art", "pdoll.art" }, names);
        foreach (var file in new[] { "inventor.art", "pdoll.art" })
            Assert.AreEqual(ChromeKind.Panel, InterfaceChromeRecipes.All.Single(r => r.File == file).Kind);
    }

    // ARC-UI-1194: the loot, barter and steal windows' frames.
    private static readonly string[] LootWindowFrames = ["loot.art", "barter.art", "barter_follower.art", "loot_pd.art", "lns_bart.art", "lns_loot.art", "lns_papr.art"];

    [TestMethod]
    public void The_loot_barter_and_steal_frames_and_lenses_have_recipes_and_the_state_boxes_stay_art()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.IsSubsetOf(LootWindowFrames, names);
        foreach (var file in LootWindowFrames) Assert.AreEqual(ChromeKind.Panel, InterfaceChromeRecipes.All.Single(r => r.File == file).Kind);
        // The drop, use and gamble boxes carry their state in their glow and corners: art, as in the inventory (ARC-UI-148).
        foreach (var file in new[] { "gamble_box_on.art", "gamble_box_off.art", "gamble_box_ill.art", "use_box_ill.art", "drop_ill.art" })
            CollectionAssert.DoesNotContain(names, file);
    }

    [TestMethod]
    public void The_loot_window_recipes_paint_in_bounds_deterministically_at_whole_and_fractional_scales()
    {
        // Synthetic frames of the windows' sizes (no game art): wood colour, with the square hole the loot and follower frames have at (16, 17) of 89.
        foreach (var (file, w, h, hole) in new[] { ("loot.art", 358, 400, true), ("barter.art", 358, 400, false), ("barter_follower.art", 358, 400, true), ("loot_pd.art", 358, 266, false), ("pdoll.art", 358, 400, false) })
        {
            var legacy = Frame(w, h, holes: false);
            if (hole)
                for (var y = 17; y < 106; y++)
                for (var x = 16; x < 105; x++) legacy[(y * w + x) * 4 + 3] = 0;
            var recipe = InterfaceChromeRecipes.All.Single(r => r.File == file);
            foreach (var scale in new[] { 1.0, 1.345, 2.0, 2.5 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                // The silhouette is the Legacy one: a frame without a hole is opaque in every pixel; one with the 89 x 89 hole is opaque everywhere more than two pixels
                // outside the hole's box and clear in the hole's middle.
                Assert.AreEqual(hole ? 0 : 255, Pixel(a, pw, (int)(60.5 * scale), (int)(61.5 * scale)).A, $"{file} middle of the round view at {scale}");
                int x0 = (int)(16 * scale) - 2, x1 = (int)Math.Ceiling(105 * scale) + 2, y0 = (int)(17 * scale) - 2, y1 = (int)Math.Ceiling(106 * scale) + 2;
                for (var y = 0; y < ph; y++)
                for (var x = 0; x < pw; x++)
                    if (!hole || x < x0 || x >= x1 || y < y0 || y >= y1)
                        Assert.AreEqual(255, a[(y * pw + x) * 4 + 3], $"{file} is opaque at ({x}, {y}) at {scale}");
            }
        }
    }

    [TestMethod]
    public void A_closed_grid_also_draws_the_line_of_its_last_column_and_row()
    {
        // Three columns and two rows of 8 from (2, 2): closed, lines at x = 9, 17, 25 and y = 9, 17; open, only x = 9, 17 and y = 9.
        var legacy = Frame(48, 28, holes: false);
        foreach (var scale in new[] { 1, 3 })
        {
            var w = 48 * scale;
            var open = InterfaceChromeRenderer.Render(Panel(new ChromeSunk(0, 0, 40, 22, ChromeMaterial.Pit), new ChromeGrid(2, 2, 3, 2, 8)), 48, 28, legacy, [], 0, scale);
            var closed = InterfaceChromeRenderer.Render(Panel(new ChromeSunk(0, 0, 40, 22, ChromeMaterial.Pit), new ChromeGrid(2, 2, 3, 2, 8, true)), 48, 28, legacy, [], 0, scale);
            var (line, pit) = ((48, 54, 64, 255), (3, 3, 5, 255));
            Assert.AreEqual(pit, Pixel(open, w, 25 * scale, 4 * scale), "open: no line on the last column");
            Assert.AreEqual(line, Pixel(closed, w, 25 * scale, 4 * scale), "closed: the last column has its line");
            Assert.AreEqual(pit, Pixel(open, w, 12 * scale, 17 * scale), "open: no line on the last row");
            Assert.AreEqual(line, Pixel(closed, w, 12 * scale, 17 * scale), "closed: the last row has its line");
            foreach (var x in new[] { 9, 17 }) Assert.AreEqual(Pixel(open, w, x * scale, 4 * scale), Pixel(closed, w, x * scale, 4 * scale), "the inner lines are the same");
            Assert.AreEqual(line, Pixel(closed, w, 25 * scale + scale - 1, 4 * scale), "a line is as wide as the scale at its cell's last pixel");
        }
    }

    [TestMethod]
    public void A_round_keep_copies_only_its_inscribed_circle()
    {
        var legacy = Frame(40, 40, holes: false);
        const int scale = 2;
        var picture = new byte[80 * 80 * 4];
        for (var i = 0; i < picture.Length; i += 4) (picture[i], picture[i + 1], picture[i + 2], picture[i + 3]) = (200, 10, 10, 255);
        var square = InterfaceChromeRenderer.Render(Panel(new ChromeKeep(4, 4, 32, 32)), 40, 40, legacy, picture, 0, scale);
        var round = InterfaceChromeRenderer.Render(Panel(new ChromeKeep(4, 4, 32, 32, true)), 40, 40, legacy, picture, 0, scale);
        Assert.AreEqual((200, 10, 10, 255), Pixel(square, 80, 4 * scale + 1, 4 * scale + 1), "a square keep copies its corner");
        Assert.AreEqual((200, 10, 10, 255), Pixel(round, 80, 20 * scale, 20 * scale), "a round keep copies its middle");
        Assert.AreNotEqual((200, 10, 10, 255), Pixel(round, 80, 4 * scale + 1, 4 * scale + 1), "a round keep leaves its corner to the generated wood");
        Assert.AreEqual((200, 10, 10, 255), Pixel(round, 80, 4 * scale + 2, 20 * scale), "the circle reaches the rectangle's side");
        Assert.AreEqual(Pixel(square, 80, 2, 2), Pixel(round, 80, 2, 2), "outside the rectangle the two agree");
    }

    // An opaque frame of 60 × 60 with a square transparent hole of 40 at (10, 10) (the loot window's round view).
    private static byte[] SquareHoleFrame()
    {
        var rgba = Frame(60, 60, holes: false);
        for (var y = 10; y < 50; y++)
        for (var x = 10; x < 50; x++) rgba[(y * 60 + x) * 4 + 3] = 0;
        return rgba;
    }

    [TestMethod]
    public void A_round_view_lays_its_ring_around_the_circle_and_spares_the_square_rim()
    {
        var legacy = SquareHoleFrame();
        const int scale = 4;
        var plain = InterfaceChromeRenderer.Render(Panel(), 60, 60, legacy, [], 0, scale);
        var view = InterfaceChromeRenderer.Render(Panel(new ChromeRoundView(30, 30, 20)), 60, 60, legacy, [], 0, scale);
        var w = 60 * scale;
        // Without the part the square hole gets the square rim: ink in its first unit just outside, near a corner (far from the circle's ring).
        Assert.AreNotEqual(Pixel(view, w, 9 * scale + 2, 12 * scale), Pixel(plain, w, 9 * scale + 2, 12 * scale), "the square rim is not drawn around the round view");
        // The ring: just outside the circle's side, 1.5 units beyond the square's edge it is brass, beyond the ring it is the frame's wood again.
        var ring = Pixel(view, w, 30 * scale, 10 * scale - 2);
        Assert.AreEqual(255, ring.A);
        Assert.AreNotEqual(Pixel(plain, w, 30 * scale, 10 * scale - 2), ring, "the ring paints the band beyond the square's side");
        Assert.AreEqual(Pixel(plain, w, 30 * scale, 10 * scale - 5 * scale), Pixel(view, w, 30 * scale, 10 * scale - 5 * scale), "beyond the ring the wood is the frame's");
        Assert.AreEqual(0, Pixel(view, w, 30 * scale, 30 * scale).A, "the hole stays transparent");
    }

    [TestMethod]
    public void A_round_view_smooths_a_circle_that_touches_the_frames_sides_and_a_frame_may_keep_its_opaque_box()
    {
        // A lens: 40 × 40, a transparent circle of radius 20 (the hole finder takes it for the outside of the frame, so only the part's circle shapes it).
        var lens = new byte[40 * 40 * 4];
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 40; x++)
        {
            double dx = x + 0.5 - 20, dy = y + 0.5 - 20;
            var i = (y * 40 + x) * 4;
            (lens[i], lens[i + 1], lens[i + 2], lens[i + 3]) = dx * dx + dy * dy < 400 ? ((byte)0, (byte)0, (byte)0, (byte)0) : ((byte)90, (byte)60, (byte)30, (byte)255);
        }
        var shaped = InterfaceChromeRenderer.Render(Panel(new ChromeRoundView(20, 20, 20)), 40, 40, lens, [], 0, 3);
        Assert.AreEqual(0, Pixel(shaped, 120, 60, 60).A, "the lens is clear in the middle");
        Assert.AreEqual(255, Pixel(shaped, 120, 3, 3).A, "and opaque in the corner");
        // The ring is brass between the circle and the corner, its outer half lit on the upper left and shaded on the lower right.
        var lit = Pixel(shaped, 120, 60 - 47, 60 - 47);
        var shade = Pixel(shaped, 120, 60 + 47, 60 + 47);
        Assert.IsTrue(lit.R + lit.G + lit.B > shade.R + shade.G + shade.B, $"{lit} lit against {shade}");
        // A frame whose box behind the live view is opaque keeps it: no shaping.
        var opaque = Frame(40, 40, holes: false);
        var kept = InterfaceChromeRenderer.Render(Panel(new ChromeRoundView(20, 20, 15, false)), 40, 40, opaque, [], 0, 3);
        Assert.AreEqual(255, Pixel(kept, 120, 60, 60).A, "Shape false keeps what the Legacy frame has inside");
        var smoothed = InterfaceChromeRenderer.Render(Panel(new ChromeRoundView(20, 20, 15)), 40, 40, opaque, [], 0, 3);
        Assert.AreEqual(255, Pixel(smoothed, 120, 60, 60).A, "Shape true leaves the Legacy alpha deep inside the circle alone");
        // 45 device pixels is the circle's radius at scale 3 (15 units); a pixel 0.2 inside the edge turns mostly transparent, one 4 outside is untouched.
        Assert.IsTrue(Pixel(smoothed, 120, 60 + 44, 60).A < 255, "the circle's edge pixel is smoothed");
        Assert.AreEqual(255, Pixel(smoothed, 120, 60 + 49, 60).A, "outside the circle the frame is opaque");
    }

    // ARC-UI-1195: the character sheet's frames and buttons.
    private static readonly (string File, int Width, int Height)[] SheetPanels =
        [("char_maint.art", 800, 400), ("Skills_Window.ART", 235, 325), ("Disciplines_Window.ART", 235, 325), ("char_spells.art", 235, 325),
         ("Scheme_Rot.ART", 235, 325)];

    private static readonly string[] SheetButtons =
    [
        "char_minus.art", "char_plus.art", "SP_Minus.ART", "SP_Plus.ART", "SkilAddBut.ART", "SkilMinusBut.ART", "SpellTech_Add.ART", "SpellTech_Minus.ART",
        "char_Common_Skills.art", "char_Tech_Skills.art", "char_Spells_Skills.art", "char_Schem_Skills.ART", "Combat_But.ART", "Thieving_But.ART", "Social_But.ART",
        "Technological_But.ART", "Anatomical_But.ART", "Chemistry_But.ART", "Electrical_But.ART", "Explosives_But.ART", "GunSmithy_But.ART", "Mechanical_But.ART",
        "Smithy_But.ART", "Therapeutics_But.ART",
    ];

    [TestMethod]
    public void The_character_sheet_window_pages_the_schemes_page_buttons_and_college_tabs_have_recipes()
    {
        foreach (var (file, _, _) in SheetPanels) Assert.AreEqual(ChromeKind.Panel, InterfaceChromeRecipes.All.Single(r => r.File == file).Kind, file);
        foreach (var file in SheetButtons) Assert.AreEqual(ChromeKind.Button, InterfaceChromeRecipes.All.Single(r => r.File == file).Kind, file);
        // ARC-UI-1784: the schemes page (567, the auto-level tab) and the sixteen college tabs have recipes; the NPC sheet and the editor draw the same frames and so the same recipes.
        Assert.AreEqual(16, InterfaceChromeRecipes.CollegeTabFiles.Distinct().Count());
        foreach (var file in InterfaceChromeRecipes.CollegeTabFiles)
        {
            var tab = InterfaceChromeRecipes.All.Single(r => r.File == file);
            Assert.AreEqual(ChromeKind.Button, tab.Kind, file);
            Assert.IsTrue(tab.KeepFace, file);
        }
        // The spell icons (32 x 32, one per spell) are illustrations, like the portraits, and stay pictures.
        Assert.IsFalse(InterfaceChromeRecipes.All.Any(r => System.Text.RegularExpressions.Regex.IsMatch(r.File, @"^s_[a-z]+[1-5]\.art$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        // The recipes are found by the lowercase art name hash whatever the file's own case.
        Assert.IsNotNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("CHAR_MAINT.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
    }

    [TestMethod]
    public void Every_part_of_a_sheet_recipe_lies_inside_its_frame_and_a_kept_picture_never_covers_a_generated_box()
    {
        foreach (var (file, w, h) in SheetPanels)
        {
            var recipe = InterfaceChromeRecipes.All.Single(r => r.File == file);
            var boxes = recipe.Parts.Select(part => part switch
            {
                ChromeSunk p => (X: p.X, Y: p.Y, Width: p.Width, Height: p.Height, Kind: "sunk"),
                ChromeKeep p => (X: p.X, Y: p.Y, Width: p.Width, Height: p.Height, Kind: "keep"),
                ChromeRivet p => (X: (int)(p.X - p.Radius), Y: (int)(p.Y - p.Radius), Width: (int)Math.Ceiling(2 * p.Radius), Height: (int)Math.Ceiling(2 * p.Radius), Kind: "rivet"),
                _ => (X: 0, Y: 0, Width: 0, Height: 0, Kind: "other"),
            }).Where(box => box.Kind != "other").ToArray();
            Assert.IsTrue(boxes.Length > 0, file);
            foreach (var (x, y, bw, bh, kind) in boxes)
                Assert.IsTrue(x >= 0 && y >= 0 && x + bw <= w + 1 && y + bh <= h + 1, $"{file}: a {kind} at ({x}, {y}, {bw} x {bh}) lies inside {w} x {h}");
            foreach (var keep in boxes.Where(box => box.Kind == "keep"))
            foreach (var other in boxes.Where(box => box.Kind != "keep"))
            {
                // A kept picture may lie wholly inside a generated pit (the info box's divider line); it may not cut into the edge of a box or sit over a rivet.
                var overlaps = keep.X < other.X + other.Width && other.X < keep.X + keep.Width && keep.Y < other.Y + other.Height && other.Y < keep.Y + keep.Height;
                var inside = other.Kind == "sunk" && keep.X >= other.X + 2 && keep.Y >= other.Y + 2 && keep.X + keep.Width <= other.X + other.Width - 2 && keep.Y + keep.Height <= other.Y + other.Height - 2;
                Assert.IsTrue(!overlaps || inside, $"{file}: the kept picture at ({keep.X}, {keep.Y}) covers a generated {other.Kind} at ({other.X}, {other.Y})");
            }
        }
    }

    [TestMethod]
    public void The_sheet_frames_paint_in_bounds_deterministically_at_whole_and_fractional_scales_and_the_pages_are_darker_than_iron()
    {
        foreach (var (file, w, h) in SheetPanels)
        {
            var legacy = Frame(w, h, holes: false);
            var recipe = InterfaceChromeRecipes.All.Single(r => r.File == file);
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 3.25 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} is opaque at {scale}");
            }
        }
        // The pages are slate, darker than the iron of the bars' frames: the mean brightness of a bare page against a bare iron panel of the same size.
        var page = new ChromeRecipe("test.art", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.None, []);
        var iron = new ChromeRecipe("test.art", ChromeKind.Panel, ChromeMaterial.Iron, ChromeSides.None, []);
        long Sum(byte[] rgba) { long total = 0; for (var i = 0; i < rgba.Length; i += 4) total += rgba[i] + rgba[i + 1] + rgba[i + 2]; return total; }
        var legacyPage = Frame(80, 80, holes: false);
        Assert.IsTrue(Sum(InterfaceChromeRenderer.Render(page, 80, 80, legacyPage, [], 0, 2)) < Sum(InterfaceChromeRenderer.Render(iron, 80, 80, legacyPage, [], 0, 2)));
    }

    [TestMethod]
    public void The_sheet_buttons_follow_their_silhouette_on_every_frame_at_a_fractional_scale()
    {
        // A synthetic round 39 x 39 button (the tabs' size) and a square 32 x 32 one (the technology page's): the silhouette stays, hover and down differ from idle.
        foreach (var (w, h, round) in new[] { (39, 39, true), (32, 32, false) })
        {
            var legacy = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                double dx = x + 0.5 - w / 2.0, dy = y + 0.5 - h / 2.0;
                var inside = !round || dx * dx + dy * dy < w * w / 4.0;
                var i = (y * w + x) * 4;
                (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = inside ? ((byte)50, (byte)60, (byte)20, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
            }
            foreach (var file in SheetButtons)
            {
                var recipe = InterfaceChromeRecipes.All.Single(r => r.File == file);
                var frames = Enumerable.Range(0, 3).Select(frame => InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], frame, 1.5)).ToArray();
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, 1.5);
                foreach (var frame in frames) Assert.AreEqual(pw * ph * 4, frame.Length, file);
                Assert.AreEqual(round ? 0 : 255, Pixel(frames[0], pw, 0, 0).A, $"{file} corner");
                Assert.AreEqual(255, Pixel(frames[0], pw, pw / 2, ph / 2).A, $"{file} middle");
                CollectionAssert.AreNotEqual(frames[0], frames[1], $"{file} down differs from idle");
                CollectionAssert.AreNotEqual(frames[0], frames[2], $"{file} hover differs from idle");
            }
        }
    }

    [TestMethod]
    public void A_college_tab_keeps_the_balls_picture_inside_a_generated_rim_at_whole_and_fractional_scales()
    {
        // A synthetic 24 x 24 round ball: a blue disc of radius 9.5 inside a red outline, transparent corners.
        const int size = 24;
        var legacy = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            double dx = x + 0.5 - 12, dy = y + 0.5 - 12, d = Math.Sqrt(dx * dx + dy * dy);
            var i = (y * size + x) * 4;
            if (d >= 12) continue;
            (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = d < 9.5 ? ((byte)20, (byte)40, (byte)200, (byte)255) : ((byte)200, (byte)30, (byte)30, (byte)255);
        }
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == InterfaceChromeRecipes.CollegeTabFiles[0]);
        foreach (var scale in new[] { 1.0, 1.5, 2.0, 4.0 })
        {
            var rgba = InterfaceChromeRenderer.Render(recipe, size, size, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(size, size, scale);
            Assert.AreEqual(pw * ph * 4, rgba.Length);
            CollectionAssert.AreEqual(rgba, InterfaceChromeRenderer.Render(recipe, size, size, legacy, [], 0, scale), $"deterministic at {scale}");
            var centre = Pixel(rgba, pw, pw / 2, ph / 2);
            Assert.IsTrue(centre.B > 150 && centre.R < 60, $"the ball's own colour stays inside the rim at {scale}: {centre}");
            Assert.AreEqual(0, Pixel(rgba, pw, 0, 0).A, "the corner is outside the round silhouette");
            // The Legacy outline's red is replaced by the generated brass ring: no strongly red pixel (R high, G low) is left between the disc and the edge.
            var ringY = (int)Math.Round(ph * 0.5);
            for (var x = 0; x < pw; x++)
            {
                var p = Pixel(rgba, pw, x, ringY);
                if (p.A > 0) Assert.IsFalse(p.R > 180 && p.G < 60, $"no Legacy red outline at {scale}, x {x}");
            }
        }
        // Idle, hover and down differ (the ring is lit differently per frame).
        var frames = Enumerable.Range(0, 3).Select(f => InterfaceChromeRenderer.Render(recipe, size, size, legacy, [], f, 2)).ToArray();
        CollectionAssert.AreNotEqual(frames[0], frames[1]);
        CollectionAssert.AreNotEqual(frames[0], frames[2]);
    }

    // ARC-UI-1197: the world map and town map window's frame (138 `mapmain`, 800 x 400) with the map view's hole (150, 11, 501 x 365) and the portrait's square hole (25, 24, 89 x 89).
    private static byte[] MapFrame()
    {
        var rgba = Frame(800, 400, holes: false);
        void Clear(int x0, int y0, int w, int h)
        {
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++) { var i = (y * 800 + x) * 4; (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (0, 0, 0, 0); }
        }
        Clear(150, 11, 501, 365); Clear(25, 24, 89, 89);
        return rgba;
    }

    [TestMethod]
    public void The_map_window_frame_has_a_recipe_and_the_arts_no_window_draws_have_none()
    {
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "mapmain.art");
        Assert.AreEqual(ChromeKind.Panel, recipe.Kind);
        // 140 and 199 (the waypoint panel's pieces) and 219 and 220 (the two landscape pictures) are drawn by no window of the viewer; the picture stays art.
        foreach (var file in new[] { "wp_main.art", "WP_Cover.ART", "Tmp_Day.ART", "Tmp_Nght.ART" })
            Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity(file, new byte[] { 1 }), 0, 1, 1, new byte[4])), file);
        Assert.IsNotNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("MAPMAIN.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
    }

    [TestMethod]
    public void Every_part_of_the_map_recipe_lies_inside_its_frame_and_a_kept_picture_never_covers_a_generated_box()
    {
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "mapmain.art");
        var boxes = recipe.Parts.Select(part => part switch
        {
            ChromeSunk p => (X: p.X, Y: p.Y, Width: p.Width, Height: p.Height, Kind: "sunk"),
            ChromeKeep p => (X: p.X, Y: p.Y, Width: p.Width, Height: p.Height, Kind: "keep"),
            ChromeRoundView p => (X: (int)(p.CenterX - p.Radius), Y: (int)(p.CenterY - p.Radius), Width: (int)Math.Ceiling(2 * p.Radius), Height: (int)Math.Ceiling(2 * p.Radius), Kind: "view"),
            _ => (X: 0, Y: 0, Width: 0, Height: 0, Kind: "other"),
        }).Where(box => box.Kind != "other").ToArray();
        Assert.IsTrue(boxes.Count(box => box.Kind == "sunk") == 3 && boxes.Count(box => box.Kind == "view") == 1);
        foreach (var (x, y, w, h, kind) in boxes) Assert.IsTrue(x >= 0 && y >= 0 && x + w <= 800 && y + h <= 400, $"a {kind} at ({x}, {y}, {w} x {h}) lies inside 800 x 400");
        foreach (var keep in boxes.Where(box => box.Kind == "keep"))
        foreach (var other in boxes.Where(box => box.Kind == "sunk"))
        {
            // The route sketch lies inside its sunk plate (2 units from three of its edges and 1 from the bottom, where the Travel ring's circle comes close); no other kept picture touches a plate.
            var overlaps = keep.X < other.X + other.Width && other.X < keep.X + keep.Width && keep.Y < other.Y + other.Height && other.Y < keep.Y + keep.Height;
            var inside = keep.X >= other.X + 1 && keep.Y >= other.Y + 1 && keep.X + keep.Width <= other.X + other.Width - 1 && keep.Y + keep.Height <= other.Y + other.Height - 1;
            Assert.IsTrue(!overlaps || inside, $"the kept picture at ({keep.X}, {keep.Y}) covers a generated plate at ({other.X}, {other.Y})");
        }
    }

    [TestMethod]
    public void The_map_frame_paints_in_bounds_deterministically_keeps_its_view_hole_clear_and_gives_the_round_portrait_no_square_rim()
    {
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "mapmain.art");
        var legacy = MapFrame();
        foreach (var scale in new[] { 1.0, 1.5, 2.0, 2.4 })
        {
            var a = InterfaceChromeRenderer.Render(recipe, 800, 400, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(800, 400, scale);
            Assert.AreEqual(pw * ph * 4, a.Length, $"at {scale}");
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, 800, 400, legacy, [], 0, scale), $"deterministic at {scale}");
            // The map view's hole (and the portrait's) stays transparent in its middle; the wood around the frame is opaque.
            Assert.AreEqual(0, Pixel(a, pw, (int)(400 * scale), (int)(190 * scale)).A, $"the map view is clear at {scale}");
            Assert.AreEqual(0, Pixel(a, pw, (int)(70 * scale), (int)(70 * scale)).A, $"the portrait hole is clear at {scale}");
            Assert.AreEqual(255, Pixel(a, pw, (int)(140 * scale), (int)(200 * scale)).A, $"wood at {scale}");
        }
        var one = InterfaceChromeRenderer.Render(recipe, 800, 400, legacy, [], 0, 1);
        // The square hole of the map view gets the brass rim every hole gets (its ink line is the outer pixel); the round portrait view does not get one around its square's corner.
        Assert.IsTrue(Pixel(one, 800, 149, 200).R < 60, "the map view's rim");
        Assert.IsTrue(Pixel(one, 800, 24, 24).R > 60, "no square rim at the portrait's corner");
        // The readout plates are sunk black pits.
        Assert.IsTrue(Pixel(one, 800, 727, 168).R < 12 && Pixel(one, 800, 727, 200).R < 12, "the two readout plates are black");
    }
}
