using Arcanum.Data.Hd;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    private static readonly string[] LootWindowFrames = ["loot.art", "barter.art", "barter_follower.art", "loot_pd.art", "barter_pd.art", "pd_cover4inven.art", "lns_bart.art", "lns_loot.art", "lns_papr.art"];

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
        foreach (var (file, w, h, hole) in new[] { ("loot.art", 358, 400, true), ("barter.art", 358, 400, false), ("barter_follower.art", 358, 400, true), ("loot_pd.art", 358, 266, false), ("barter_pd.art", 358, 243, false), ("pd_cover4inven.art", 341, 400, false), ("pdoll.art", 358, 400, false) })
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

    // ARC-UI-1750: the doll cover 341 has four clear columns at its right edge that show the pack under it; they stay clear and take no brass rim.
    [TestMethod]
    public void The_doll_cover_keeps_its_clear_right_columns_clear_and_rimless()
    {
        const int w = 341, h = 400;
        var legacy = Frame(w, h, holes: false);
        for (var y = 0; y < h; y++)
        for (var x = 337; x < w; x++) legacy[(y * w + x) * 4 + 3] = 0;
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "pd_cover4inven.art");
        var brass = recipe with { Holes = ChromeHoles.Brass };
        foreach (var scale in new[] { 1.0, 1.345, 2.0, 2.5 })
        {
            var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
            var rim = InterfaceChromeRenderer.Render(brass, w, h, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
            for (var y = 0; y < ph; y++)
            {
                Assert.AreEqual(0, Pixel(a, pw, pw - 1, y).A, $"the last column is clear at row {y}, scale {scale}");
                Assert.AreEqual(255, Pixel(a, pw, (int)(100 * scale), y).A, $"the frame is opaque inside at row {y}, scale {scale}");
            }
            // Without the rule the hole's rim would be painted beside the clear columns: the renderings differ there and only there.
            var x336 = (int)(336 * scale);
            var differs = Enumerable.Range(0, ph).Any(y => Pixel(a, pw, x336, y) != Pixel(rim, pw, x336, y));
            Assert.IsTrue(differs, $"the brass rule would draw a rim beside the clear columns at scale {scale}");
        }
    }

    // ARC-UI-1750: the barter doll 337 is the loot doll 349 cut 23 pixels shorter at the top; the doll cover 341 is the inventory doll's pit, grid and bottom pits 13 pixels to the left.
    [TestMethod]
    public void The_barter_doll_and_the_doll_cover_recipes_follow_the_frames_they_sit_beside()
    {
        static ChromeRecipe Of(string file) => InterfaceChromeRecipes.All.Single(r => r.File == file);
        static IEnumerable<ChromePart> Moved(IEnumerable<ChromePart> parts, int dx, int dy) => parts.Select<ChromePart, ChromePart>(part => part switch
        {
            ChromeSunk p => p with { X = p.X + dx, Y = p.Y + dy },
            ChromeGrid p => p with { X = p.X + dx, Y = p.Y + dy },
            ChromeKeep p => p with { X = p.X + dx, Y = p.Y + dy },
            _ => throw new AssertFailedException("a part this test does not move: " + part),
        });
        // 337: the pit, the grid of 21-pixel cells and the two slot pits (the weapon's and the armour's, which keep the original's picture) are 349's, 23 pixels higher.
        CollectionAssert.AreEqual(Moved(Of("loot_pd.art").Parts, 0, -23).ToArray(), Of("barter_pd.art").Parts.ToArray());
        Assert.AreEqual(2, Of("barter_pd.art").Parts.OfType<ChromeKeep>().Count());
        // 341: the inventory doll's 330 x 310 pit with its 10 x 9 grid of 32-pixel cells and its two bottom pits, 13 pixels to the left (the cover is 17 pixels narrower), and a name box of its own.
        var cover = Of("pd_cover4inven.art").Parts;
        Assert.AreEqual(1, cover.OfType<ChromeGrid>().Count());
        var doll = Of("pdoll.art").Parts;
        Assert.AreEqual(doll.OfType<ChromeGrid>().Single() with { X = 10 }, cover.OfType<ChromeGrid>().Single());
        Assert.AreEqual(doll.OfType<ChromeSunk>().Single(p => p.Y == 100) with { X = 5 }, cover.OfType<ChromeSunk>().Single(p => p.Y == 100));
        Assert.AreEqual(new ChromeSunk(54, 28, 221, 55, ChromeMaterial.Pit), cover.OfType<ChromeSunk>().Single(p => p.Y == 28), "the name box");
        // The inventory doll's two bottom keeps moved 13 pixels left and clipped to the 341-pixel frame.
        CollectionAssert.AreEqual(new[] { new ChromeKeep(0, 303, 109, 97), new ChromeKeep(231, 303, 110, 97) }, cover.OfType<ChromeKeep>().ToArray());
        var dollKeeps = doll.OfType<ChromeKeep>().Where(keep => keep.Y == 303).ToArray();
        Assert.AreEqual(Math.Max(0, dollKeeps[0].X - 13), cover.OfType<ChromeKeep>().First().X);
        Assert.AreEqual(dollKeeps[1].X - 13, cover.OfType<ChromeKeep>().Last().X);
        Assert.IsTrue(Of("barter_pd.art").Parts.OfType<ChromeSunk>().All(p => p.Y + p.Height <= 243), "the barter pit ends inside the 243-pixel frame");
        Assert.AreEqual(ChromeHoles.None, Of("pd_cover4inven.art").Holes, "the clear right edge of the cover is not a hole");
        Assert.IsTrue(cover.OfType<ChromeKeep>().All(keep => keep.X >= 0 && keep.X + keep.Width <= 341 && keep.Y + keep.Height <= 400), "the kept corners lie in the 341 x 400 frame");
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
        // KeepFace is what keeps the ball: the same input through a plain button recipe gets the brass face (the glyph test keeps nothing of a plain disc).
        var plain = new ChromeRecipe("plain.art", ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, []);
        CollectionAssert.AreNotEqual(InterfaceChromeRenderer.Render(recipe, size, size, legacy, [], 0, 2), InterfaceChromeRenderer.Render(plain, size, size, legacy, [], 0, 2));
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
        // ARC-UI-2194: the brass pill is an opaque island inside the view's hole (the cover's silhouette at (294, 341)), with the compass opening clear to its lower edge.
        var cover = SyntheticCover();
        for (var y = 0; y < 35; y++)
        for (var x = 0; x < 207; x++)
            if (cover[(y * 207 + x) * 4 + 3] > 0) { var i = ((341 + y) * 800 + 294 + x) * 4; (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (110, 80, 40, 255); }
        for (var y = 341; y < 341 + 49; y++)
        for (var x = 294; x < 501; x++)
        {
            double dx = x + 0.5 - (294 + PillCentre.X), dy = y + 0.5 - (341 + PillCentre.Y);
            if (dx * dx + dy * dy < 20.5 * 20.5) { var i = (y * 800 + x) * 4; (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (0, 0, 0, 0); }
        }
        return rgba;
    }

    [TestMethod]
    public void The_map_window_frame_has_a_recipe_and_the_arts_no_window_draws_have_none()
    {
        var recipe = InterfaceChromeRecipes.All.Single(r => r.File == "mapmain.art");
        Assert.AreEqual(ChromeKind.Panel, recipe.Kind);
        // ARC-UI-2694: the window frame draws no ring of its own around the lens's circle (the lens art 198 carries the one ring inside its square): its square takes no rim.
        Assert.AreEqual(0, recipe.Parts.OfType<ChromeRoundView>().Count());
        Assert.IsTrue(recipe.Parts.OfType<ChromeBare>().Any(bare => (bare.X, bare.Y, bare.Width, bare.Height) == (25, 24, 89, 89)), "the lens's square takes no rim");
        Assert.IsFalse(recipe.Parts.OfType<ChromeKeep>().Any(keep => keep.X < 119 && keep.Y < 119 && keep.X + keep.Width > 19 && keep.Y + keep.Height > 19), "no kept picture over the portrait's ring");
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
            ChromeBare p when p.Width == 89 => (X: p.X, Y: p.Y, Width: p.Width, Height: p.Height, Kind: "view"), // ARC-UI-2694: the lens's square, no ring of the frame's own
            _ => (X: 0, Y: 0, Width: 0, Height: 0, Kind: "other"),
        }).Where(box => box.Kind != "other").ToArray();
        Assert.IsTrue(boxes.Count(box => box.Kind == "sunk") == 3 && boxes.Count(box => box.Kind == "view") == 1, $"sunk {boxes.Count(box => box.Kind == "sunk")}, view {boxes.Count(box => box.Kind == "view")}");
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

    // ARC-UI-1810: the world map window's buttons (811 Zoom, 812 Whole map, 813 Travel, 139 compass) and the lens 198.
    [TestMethod]
    public void The_map_window_buttons_have_recipes_a_lamp_ball_and_a_kept_map_picture_for_zoom_and_whole_map()
    {
        foreach (var (file, ballY, keepX) in new[] { ("MapZoomBut.ART", 29.0, 45), ("MapWholeBut.ART", 28.5, 53) })
        {
            var recipe = Recipe(file);
            Assert.AreEqual(ChromeKind.Panel, recipe.Kind, file);
            var ball = recipe.Parts.OfType<ChromeBall>().Single();
            Assert.AreEqual((21.5, ballY), (ball.CenterX, ball.CenterY), file);
            var keep = recipe.Parts.OfType<ChromeKeep>().Single();
            Assert.AreEqual(keepX, keep.X, file);
            // The lamp's circle and the kept map picture do not touch.
            Assert.IsTrue(ball.CenterX + ball.Radius + 2 <= keep.X, file);
        }
        Assert.AreEqual(ChromeKind.Button, Recipe("MapTravelBut.ART").Kind);
        Assert.IsFalse(Recipe("MapTravelBut.ART").KeepFace);
        Assert.AreEqual(ChromeKind.Button, Recipe("navbutton.art").Kind);
        Assert.IsTrue(Recipe("navbutton.art").KeepFace, "the compass ball's own picture stays inside the generated rim");
    }

    // A synthetic Zoom or Whole map button of the real size: a lamp circle (radius 21.5, a dim green dot in the middle) and a cream map picture box on the right; the frame index picks a lit dot.
    private static byte[] LampButton(int width, double lampY, int mapX, bool lit)
    {
        const int h = 56;
        var rgba = new byte[width * h * 4];
        void Put(int x, int y, byte r, byte g, byte b) { var i = (y * width + x) * 4; (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, 255); }
        for (var y = 0; y < h; y++)
        for (var x = 0; x < width; x++)
        {
            double dx = x + 0.5 - 21.5, dy = y + 0.5 - lampY;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 21.5) { if (d < 7) Put(x, y, 20, lit ? (byte)230 : (byte)110, 20); else Put(x, y, 30, 28, 14); }
            else if (x >= mapX) Put(x, y, 220, 200, 150);
        }
        return rgba;
    }

    [TestMethod]
    public void The_lamp_buttons_paint_a_generated_lamp_ball_keep_the_lamp_and_the_map_picture_and_light_the_other_side_when_down()
    {
        foreach (var (file, width, lampY, mapX) in new[] { ("MapZoomBut.ART", 111, 29.0, 45), ("MapWholeBut.ART", 106, 28.5, 53) })
        {
            var recipe = Recipe(file);
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 4.0 })
            {
                var idle = InterfaceChromeRenderer.Render(recipe, width, 56, LampButton(width, lampY, mapX, false), [], 0, scale);
                var down = InterfaceChromeRenderer.Render(recipe, width, 56, LampButton(width, lampY, mapX, true), [], 1, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(width, 56, scale);
                Assert.AreEqual(pw * ph * 4, idle.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(idle, InterfaceChromeRenderer.Render(recipe, width, 56, LampButton(width, lampY, mapX, false), [], 0, scale), $"{file} deterministic at {scale}");
                int At(double v) => (int)(v * scale);
                // The silhouette is the Legacy one: the lamp's middle and the map picture are opaque, the gap between them and the corner outside the circle are clear.
                Assert.AreEqual(255, Pixel(idle, pw, At(21.5), At(lampY)).A, $"{file} lamp at {scale}");
                Assert.AreEqual(255, Pixel(idle, pw, At(mapX + 10), At(28)).A, $"{file} map at {scale}");
                // The map picture's first column is the art's own (the lamp's reach must stop short of it), at every scale.
                var first = Pixel(idle, pw, (int)Math.Ceiling(mapX * scale), At(2));
                Assert.AreEqual((255, 220, 200, 150), (first.A, first.R, first.G, first.B), $"{file} the map's first column at {scale}");
                Assert.AreEqual(0, Pixel(idle, pw, At(mapX - 1), At(28)).A, $"{file} gap at {scale}");
                Assert.AreEqual(0, Pixel(idle, pw, At(1), At(lampY - 20)).A, $"{file} outside the lamp's circle at {scale}");
                // The lamp's dot and the map picture are the art's own pixels (the frame's dim or lit green, the cream).
                var dot = Pixel(idle, pw, At(21.5), At(lampY)); var lit = Pixel(down, pw, At(21.5), At(lampY));
                Assert.IsTrue(dot.G == 110 && lit.G == 230, $"{file} the lamp's own light is kept ({dot}, {lit}) at {scale}");
                Assert.AreEqual((220, 200, 150), (Pixel(idle, pw, At(mapX + 10), At(28)).R, Pixel(idle, pw, At(mapX + 10), At(28)).G, Pixel(idle, pw, At(mapX + 10), At(28)).B), $"{file} map picture at {scale}");
                // The face between the rim and the dot is the generated dark glass, not the Legacy pixel (30, 28, 14).
                var face = Pixel(idle, pw, At(21.5 + 13), At(lampY));
                Assert.IsTrue(face.R != 30 || face.G != 28 || face.B != 14, $"{file} generated face at {scale}");
                // The rim's light is on the other side in the down frame: the upper left is lit when idle, the lower right when down.
                int Sum(byte[] a, int x, int y) { var p = Pixel(a, pw, x, y); return p.R + p.G + p.B; }
                var (ux, uy, lx, ly) = (At(21.5 - 13), At(lampY - 13), At(21.5 + 13), At(lampY + 13));
                Assert.IsTrue(Sum(idle, ux, uy) > Sum(idle, lx, ly), $"{file} idle is lit on the upper left at {scale}");
                Assert.IsTrue(Sum(down, ux, uy) < Sum(down, lx, ly), $"{file} down is lit on the lower right at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_travel_and_compass_balls_and_the_lens_follow_their_silhouette_at_fractional_scales()
    {
        // A round 57 x 57 Travel ball and a 45 x 45 compass ball: the silhouette stays, the down frame differs from idle; the compass keeps the art's whole picture inside the rim.
        foreach (var (file, size) in new[] { ("MapTravelBut.ART", 57), ("navbutton.art", 45) })
        {
            var legacy = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                double dx = x + 0.5 - size / 2.0, dy = y + 0.5 - size / 2.0;
                var i = (y * size + x) * 4;
                (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = dx * dx + dy * dy < size * size / 4.0 ? ((byte)120, (byte)30, (byte)20, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
            }
            var recipe = Recipe(file);
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 2.4 })
            {
                var idle = InterfaceChromeRenderer.Render(recipe, size, size, legacy, [], 0, scale);
                var down = InterfaceChromeRenderer.Render(recipe, size, size, legacy, [], 1, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(size, size, scale);
                Assert.AreEqual(255, Pixel(idle, pw, pw / 2, ph / 2).A, $"{file} middle at {scale}");
                Assert.AreEqual(0, Pixel(idle, pw, 0, 0).A, $"{file} corner at {scale}");
                CollectionAssert.AreNotEqual(idle, down, $"{file} down differs at {scale}");
                var middle = Pixel(idle, pw, pw / 2, ph / 2);
                Assert.AreEqual(recipe.KeepFace, middle.R == 120 && middle.G == 30 && middle.B == 20, $"{file} keeps the art's picture only with KeepFace at {scale}");
            }
        }
        // The lens 198: a transparent circle (radius 44.5) in an 89 x 89 frame is clear in the middle and gets the ring in its corners at every scale.
        var lens = new byte[89 * 89 * 4];
        for (var y = 0; y < 89; y++)
        for (var x = 0; x < 89; x++)
        {
            double dx = x + 0.5 - 44.5, dy = y + 0.5 - 44.5;
            var i = (y * 89 + x) * 4;
            (lens[i], lens[i + 1], lens[i + 2], lens[i + 3]) = dx * dx + dy * dy < 44.5 * 44.5 ? ((byte)0, (byte)0, (byte)0, (byte)0) : ((byte)90, (byte)60, (byte)30, (byte)255);
        }
        foreach (var scale in new[] { 1.0, 1.5, 2.0, 2.4 })
        {
            var a = InterfaceChromeRenderer.Render(Recipe("PCWinCvr.ART"), 89, 89, lens, [], 0, scale);
            var (pw, _) = InterfaceChromeRenderer.PixelSize(89, 89, scale);
            Assert.AreEqual(0, Pixel(a, pw, pw / 2, pw / 2).A, $"the lens is clear in the middle at {scale}");
            Assert.AreEqual(255, Pixel(a, pw, 1, 1).A, $"and opaque in the corner at {scale}");
            // ARC-UI-2645: the circle touches the frame's four sides, so the ring lies inside it: at the middle of each side the ring is there (opaque, brass), not cut off by the box.
            foreach (var (x, y) in new[] { (pw / 2, 1), (pw / 2, pw - 2), (1, pw / 2), (pw - 2, pw / 2) })
            {
                var edge = Pixel(a, pw, x, y);
                Assert.AreEqual(255, edge.A, $"the ring is whole at ({x}, {y}) at {scale}");
                Assert.IsTrue(edge.R > edge.B, $"the ring is brass at ({x}, {y}) at {scale}: {edge}");
            }
            Assert.IsTrue(Pixel(a, pw, pw / 2, (int)(pw * 0.2)).A < 255, $"the view is clear inside the ring at {scale}");
        }
    }

    // ARC-UI-2194: the world map window's bottom cover 217 (207 x 35) and the brass pill under it in the frame 138.
    private static (double X, double Y) PillCentre => (103.5, 28.5);

    // A synthetic cover of the real size: the opaque pill (a housing from x 39 to 169 from row 10, a plate band from x 4 to 203 from row 19, a ring of radius 26 round the compass opening) with the opening (radius 20.5) clear.
    private static byte[] SyntheticCover()
    {
        var rgba = new byte[207 * 35 * 4];
        for (var y = 0; y < 35; y++)
        for (var x = 0; x < 207; x++)
        {
            double dx = x + 0.5 - PillCentre.X, dy = y + 0.5 - PillCentre.Y, d = Math.Sqrt(dx * dx + dy * dy);
            var opaque = (x is >= 39 and < 169 && y >= 10 || x is >= 4 and < 203 && y >= 19 || d < 26) && d >= 20.5;
            var i = (y * 207 + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = opaque ? ((byte)200, (byte)10, (byte)10, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
        }
        return rgba;
    }

    private static bool SamePart(ChromePart a, ChromePart b) => a is ChromePolygon p && b is ChromePolygon q ? p.Material == q.Material && p.Points.SequenceEqual(q.Points) : a.Equals(b);

    [TestMethod]
    public void The_cover_and_the_frames_pill_are_built_from_the_same_parts_with_no_kept_picture()
    {
        var cover = Recipe("Nav_Cvr.ART");
        Assert.AreEqual(ChromeKind.Panel, cover.Kind);
        Assert.AreEqual(ChromeMaterial.Brass, cover.Base);
        Assert.AreEqual(ChromeHoles.None, cover.Holes, "the opening's edge is the pill's ring, not a hole rim");
        Assert.AreEqual((294, 341), (cover.OriginX, cover.OriginY), "the brass is sampled at the cover's place in the window");
        Assert.IsFalse(cover.Parts.OfType<ChromeKeep>().Any(), "no kept picture");
        var parts = InterfaceChromeRecipes.NavPillParts(0, 0);
        Assert.AreEqual(parts.Count, cover.Parts.Count);
        Assert.IsTrue(parts.Zip(cover.Parts).All(pair => SamePart(pair.First, pair.Second)));
        // The pill's parts: one housing, two studs and two plates (capsules), the ring round the opening, two screws.
        Assert.AreEqual(1, parts.OfType<ChromePolygon>().Count());
        Assert.AreEqual(4, parts.OfType<ChromeCapsule>().Count());
        Assert.AreEqual(2, parts.OfType<ChromeRivet>().Count());
        var socket = parts.OfType<ChromeSocket>().Single();
        Assert.AreEqual((PillCentre.X, PillCentre.Y), (socket.CenterX, socket.CenterY));
        // The frame carries the same parts shifted to the cover's place and no kept picture over the pill's rectangle (294, 341, 207 x 51); the opening takes no hole rim of its own.
        var frame = Recipe("mapmain.art");
        var shifted = InterfaceChromeRecipes.NavPillParts(294, 341);
        Assert.IsTrue(shifted.All(part => frame.Parts.Any(own => SamePart(own, part))), "the frame's pill is the cover's parts at (294, 341)");
        Assert.IsFalse(frame.Parts.OfType<ChromeKeep>().Any(keep => keep.X < 501 && keep.X + keep.Width > 294 && keep.Y < 392 && keep.Y + keep.Height > 341), "no kept picture over the pill");
        Assert.IsTrue(frame.Parts.OfType<ChromeBare>().Any(bare => bare.X <= 294 + PillCentre.X && 294 + PillCentre.X < bare.X + bare.Width && bare.Y <= 341 + PillCentre.Y && 341 + PillCentre.Y < bare.Y + bare.Height));
    }

    [TestMethod]
    public void The_cover_keeps_its_silhouette_paints_new_brass_and_meets_the_frames_pill_without_a_seam()
    {
        var legacy = SyntheticCover();
        var cover = Recipe("Nav_Cvr.ART");
        var frameLegacy = MapFrame();
        foreach (var scale in new[] { 1.0, 2.0, 4.0 })
        {
            var a = InterfaceChromeRenderer.Render(cover, 207, 35, legacy, [], 0, scale);
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(cover, 207, 35, legacy, [], 0, scale), $"deterministic at {scale}");
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(207, 35, scale);
            Assert.AreEqual(pw * ph * 4, a.Length);
            int At(double v) => (int)(v * scale);
            // The silhouette (the alpha, so the opening's hit area too) is the Legacy one at every device pixel, and none of the Legacy colour is left.
            for (var y = 0; y < ph; y++)
            for (var x = 0; x < pw; x++)
            {
                var p = Pixel(a, pw, x, y);
                var lx = Math.Min(206, (int)(x / scale)); var ly = Math.Min(34, (int)(y / scale));
                Assert.AreEqual(legacy[(ly * 207 + lx) * 4 + 3] > 0, p.A > 0, $"alpha at {x},{y} at {scale}");
                Assert.IsFalse(p.A > 0 && p.R == 200 && p.G == 10 && p.B == 10, $"generated colour at {x},{y} at {scale}");
            }
            Assert.AreEqual(0, Pixel(a, pw, At(PillCentre.X), At(PillCentre.Y)).A, $"the compass opening is clear at {scale}");
            // The plate: a bright rim, a darker recessed face, both brass (red over green over blue); the housing between the plate and the ring is darker than the plate's rim.
            var rim = Pixel(a, pw, At(30), At(21.5)); var face = Pixel(a, pw, At(30), At(30)); var housing = Pixel(a, pw, At(73.5), At(30));
            foreach (var p in new[] { rim, face, housing }) Assert.IsTrue(p.R > p.G && p.G > p.B && p.A == 255, $"brass at {scale}");
            Assert.IsTrue(rim.R + rim.G + rim.B > face.R + face.G + face.B, $"the rim is brighter than the face at {scale}");
            Assert.IsTrue(face.R + face.G + face.B > housing.R + housing.G + housing.B, $"the face is brighter than the housing at {scale}");

            if (scale > 2) continue;
            // The frame draws the same pill at the cover's place: where the cover is opaque the frame's pixels are the cover's (the same brass at the same window position).
            var f = InterfaceChromeRenderer.Render(Recipe("mapmain.art"), 800, 400, frameLegacy, [], 0, scale);
            var (fw, _) = InterfaceChromeRenderer.PixelSize(800, 400, scale);
            foreach (var (sx, sy) in new[] { (30.0, 30.0), (30.0, 34.0), (12.0, 25.0), (60.0, 14.0), (73.5, 30.0), (80.0, 30.0), (150.0, 30.0), (190.0, 34.0), (104.0, 4.0) })
            {
                var c = Pixel(a, pw, At(sx), At(sy)); var w = Pixel(f, fw, At(294 + sx), At(341 + sy));
                Assert.AreEqual(255, c.A, $"cover opaque at {sx},{sy} at {scale}");
                Assert.AreEqual((c.R, c.G, c.B), (w.R, w.G, w.B), $"the frame's pill matches the cover at {sx},{sy} at {scale}");
            }
            // Across the cover's lower edge the pill goes on: the pixel under the cover's last row is close to it.
            foreach (var sx in new[] { 30.0, 60.0, 150.0, 185.0 })
            {
                var above = Pixel(a, pw, At(sx), ph - 1); var below = Pixel(f, fw, At(294 + sx), At(341 + 35));
                Assert.IsTrue(Math.Abs(above.R - below.R) + Math.Abs(above.G - below.G) + Math.Abs(above.B - below.B) < 70, $"no seam at {sx} at {scale}: {above} / {below}");
            }
            // The frame's opening is clear (the compass button's own hit area) and wood is left outside the pill.
            Assert.AreEqual(0, Pixel(f, fw, At(294 + PillCentre.X), At(341 + PillCentre.Y)).A);
        }
    }

    [TestMethod]
    public void The_cover_at_fractional_scales_keeps_its_size_and_the_ring_reaches_its_outer_edge()
    {
        var legacy = SyntheticCover();
        foreach (var scale in new[] { 1.5, 2.4 })
        {
            var a = InterfaceChromeRenderer.Render(Recipe("Nav_Cvr.ART"), 207, 35, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(207, 35, scale);
            Assert.AreEqual(pw * ph * 4, a.Length);
            Assert.AreEqual(0, Pixel(a, pw, (int)(PillCentre.X * scale), (int)(PillCentre.Y * scale)).A, $"the opening is clear at {scale}");
            // Just inside the ring's outer radius (the art is opaque out to 26 units) the ring's own brass is drawn, not the cover's flat base: the ring's lit and shaded sides differ.
            (int R, int G, int B, int A) At(double dx, double dy) => Pixel(a, pw, (int)((PillCentre.X + dx) * scale), (int)((PillCentre.Y + dy) * scale));
            var (left, right) = (At(-24.5, 0), At(24.5, 0));
            Assert.IsTrue(left.R + left.G + left.B > right.R + right.G + right.B, $"the ring's upper-left is lit at {scale}: {left} / {right}");
        }
    }

    [TestMethod]
    public void A_bare_mark_exempts_the_smallest_hole_around_its_centre_and_the_larger_hole_keeps_its_rim()
    {
        // A 60 x 60 art: a clear rectangle (4 to 56 by 10 to 50, not round) with an opaque island (a disk of radius 10) that has a clear round opening of radius 5 at its centre: the rectangle's box holds the opening's centre too.
        var legacy = new byte[60 * 60 * 4];
        for (var y = 0; y < 60; y++)
        for (var x = 0; x < 60; x++)
        {
            double dx = x + 0.5 - 30, dy = y + 0.5 - 30, d = Math.Sqrt(dx * dx + dy * dy);
            var inSquare = x is >= 4 and < 56 && y is >= 10 and < 50;
            var opaque = !inSquare || (d < 10 && d >= 5);
            var i = (y * 60 + x) * 4;
            (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = opaque ? ((byte)120, (byte)80, (byte)40, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
        }
        ChromeRecipe Make(params ChromePart[] parts) => new("Bare_Test.ART", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, parts);
        var plain = InterfaceChromeRenderer.Render(Make(), 60, 60, legacy, [], 0, 1);
        var bare = InterfaceChromeRenderer.Render(Make(new ChromeBare(28, 28, 4, 4)), 60, 60, legacy, [], 0, 1);
        Assert.IsTrue(Pixel(bare, 60, 3, 30).R < 30, "the large hole keeps its rim");
        Assert.AreEqual(Pixel(plain, 60, 3, 30), Pixel(bare, 60, 3, 30));
        CollectionAssert.AreNotEqual(plain, bare, "the opening's own rim is not drawn when it is bare");
    }

    // ARC-UI-1196: the logbook's frames (synthetic stand-ins of the frames' sizes and holes; no game art).
    private static readonly string[] LogbookFrames =
        ["logbooks_side.art", "book_main.art", "book_quests.art", "tab_note.art", "tab_quest.art", "tab_rep.art", "tab_blesscurse.art", "tab_egoinjure.art", "tab_backgrnd.art", "tab_keys.art", "pageturn_l.art", "pageturn_r.art"];

    private static ChromeRecipe Recipe(string file) => InterfaceChromeRecipes.All.Single(r => r.File == file);

    // A frame of w x h whose pixels are one colour, with a transparent rectangle.
    private static byte[] Flat(int w, int h, (byte R, byte G, byte B) colour, (int X, int Y, int W, int H)? hole = null)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            var clear = hole is { } r && x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = clear ? ((byte)0, (byte)0, (byte)0, (byte)0) : (colour.R, colour.G, colour.B, (byte)255);
        }
        return rgba;
    }

    [TestMethod]
    public void The_logbook_frames_have_recipes_laid_out_by_the_logbook_layout_and_the_shared_lens_is_the_round_views_ring()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.IsSubsetOf(LogbookFrames, names);
        CollectionAssert.AllItemsAreUnique(names);
        Assert.AreEqual(ChromeHoles.None, Recipe("logbooks_side.art").Holes, "the side's square hole is the live view");
        Assert.AreEqual(ChromeHoles.Cloth, Recipe("book_quests.art").Holes);
        // ARC-UI-1810: the lens art (pcwincvr, 198) is shared with the character sheet and the world map; its recipe is the round view's ring (the lenses 231 to 233's), found by the lowercase name.
        Assert.IsNotNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("PCWinCvr.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
        CollectionAssert.AreEqual(new ChromePart[] { new ChromeRoundView(44.5, 44.5, 44.5, Inward: true) }, Recipe("PCWinCvr.ART").Parts.ToArray());
        var tabs = new[] { "tab_note.art", "tab_quest.art", "tab_rep.art", "tab_blesscurse.art", "tab_egoinjure.art", "tab_backgrnd.art", "tab_keys.art" };
        for (var i = 0; i < tabs.Length; i++)
        {
            var recipe = Recipe(tabs[i]);
            Assert.AreEqual(ChromeKind.Tab, recipe.Kind);
            Assert.AreEqual(AreaLogbookArtLayout.Tabs[i].X - AreaLogbookArtLayout.BookCorner.X, recipe.OriginX, $"{tabs[i]} stands where the layout puts tab {i}");
            Assert.AreEqual(AreaLogbookArtLayout.Tabs[i].Y - AreaLogbookArtLayout.BookCorner.Y, recipe.OriginY);
        }
        Assert.AreEqual(AreaLogbookArtLayout.PreviousCorner.X - AreaLogbookArtLayout.BookCorner.X, Recipe("pageturn_l.art").OriginX);
        Assert.AreEqual(AreaLogbookArtLayout.NextCorner.X - AreaLogbookArtLayout.BookCorner.X, Recipe("pageturn_r.art").OriginX);
    }

    [TestMethod]
    public void The_logbook_recipes_paint_in_bounds_deterministically_at_whole_and_fractional_scales()
    {
        foreach (var (file, w, h) in new[] { ("logbooks_side.art", 165, 400), ("book_main.art", 635, 400), ("book_quests.art", 561, 355), ("tab_keys.art", 46, 41), ("pageturn_r.art", 18, 22) })
        {
            var legacy = Flat(w, h, (200, 190, 170));
            var recipe = Recipe(file);
            foreach (var scale in new[] { 1.0, 1.345, 2.0 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} is opaque like its Legacy frame at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_side_panels_square_hole_is_plain_and_a_covers_hole_has_a_cloth_edge_not_a_brass_one()
    {
        // The side panel: a hole at (25, 24, 89, 89) gets no rim, so a pixel beside it equals the same pixel of the frame with no hole.
        var withHole = Flat(165, 400, (90, 60, 30), (25, 24, 89, 89));
        var sideA = InterfaceChromeRenderer.Render(Recipe("logbooks_side.art"), 165, 400, withHole, [], 0, 2);
        var sideB = InterfaceChromeRenderer.Render(Recipe("logbooks_side.art"), 165, 400, Flat(165, 400, (90, 60, 30)), [], 0, 2);
        Assert.AreEqual(Pixel(sideB, 330, 48, 100), Pixel(sideA, 330, 48, 100), "left of the hole");
        Assert.AreEqual(Pixel(sideB, 330, 229, 100), Pixel(sideA, 330, 229, 100), "right of the hole");
        Assert.AreEqual(0, Pixel(sideA, 330, 100, 100).A, "the hole stays clear");
        // The page art: a cloth frame with a hole at (14, 12, 537, 331). The cloth next to the hole is darker than a few units further in, and not brass-coloured.
        var frame = Flat(561, 355, (100, 40, 45), (14, 12, 537, 331));
        var cover = InterfaceChromeRenderer.Render(Recipe("book_quests.art"), 561, 355, frame, [], 0, 2);
        Assert.AreEqual(0, Pixel(cover, 1122, 400, 300).A, "the hole stays clear");
        var edge = Pixel(cover, 1122, 13 * 2 + 1, 300);
        var inner = Pixel(cover, 1122, 6 * 2, 300);
        Assert.IsTrue(edge.R + edge.G + edge.B < inner.R + inner.G + inner.B, $"{edge} against {inner}");
        Assert.IsTrue(edge.R > edge.B && edge.G <= edge.R, $"the edge is the cloth's own red-brown, not brass: {edge}");
    }

    [TestMethod]
    public void The_parchment_sinks_toward_the_fold_and_matches_across_separately_painted_arts()
    {
        // The book: page colour at the middle of the left page is brighter than beside the fold (x 286 of the book), and the fold's own line is the darkest.
        var book = InterfaceChromeRenderer.Render(Recipe("book_main.art"), 635, 400, Flat(635, 400, (120, 110, 90)), [], 0, 1);
        int Luma((int R, int G, int B, int A) p) => p.R + p.G + p.B;
        var far = Enumerable.Range(150, 20).Average(x => Luma(Pixel(book, 635, x, 200)));
        var near = Enumerable.Range(268, 10).Average(x => Luma(Pixel(book, 635, x, 200)));
        Assert.IsTrue(near < far - 20, $"near the fold {near} against far {far}");
        Assert.IsTrue(Luma(Pixel(book, 635, 286, 200)) < near, "the fold's line is darker still");
        // A page button painted as its own art at the book's offset continues the page: its parchment equals the book's at the same place (the arrow glyph part leaves a flat frame alone).
        var button = InterfaceChromeRenderer.Render(Recipe("pageturn_l.art"), 18, 22, Flat(18, 22, (120, 110, 90)), [], 0, 1);
        var ox = AreaLogbookArtLayout.PreviousCorner.X - AreaLogbookArtLayout.BookCorner.X;
        var oy = AreaLogbookArtLayout.PreviousCorner.Y - AreaLogbookArtLayout.BookCorner.Y;
        for (var y = 0; y < 22; y += 5)
        for (var x = 0; x < 18; x += 4)
            Assert.AreEqual(Pixel(book, 635, ox + x, oy + y), Pixel(button, 18, x, y), $"({x}, {y}) of the button is the page under it");
    }

    [TestMethod]
    public void A_glyph_part_multiplies_the_marks_onto_the_ground_and_leaves_the_legacy_grain_out()
    {
        // 18 x 22 paper of a flat colour with a grainy patch (a few levels) and a dark mark.
        var legacy = Flat(18, 22, (200, 190, 170));
        for (var y = 0; y < 22; y++)
        for (var x = 0; x < 18; x++) { var i = (y * 18 + x) * 4; if ((x + y) % 2 == 0) legacy[i] += 6; }
        for (var y = 8; y < 13; y++)
        for (var x = 7; x < 12; x++) { var i = (y * 18 + x) * 4; (legacy[i], legacy[i + 1], legacy[i + 2]) = (60, 40, 20); }
        var withGlyph = InterfaceChromeRenderer.Render(Recipe("pageturn_l.art"), 18, 22, legacy, [], 0, 2);
        var plain = InterfaceChromeRenderer.Render(new ChromeRecipe("t.art", ChromeKind.Panel, ChromeMaterial.Parchment, ChromeSides.None, [], OriginX: Recipe("pageturn_l.art").OriginX, OriginY: Recipe("pageturn_l.art").OriginY), 18, 22, legacy, [], 0, 2);
        Assert.AreEqual(Pixel(plain, 36, 2, 2), Pixel(withGlyph, 36, 2, 2), "the grain of the Legacy ground is not copied");
        // A lighter pixel of the Legacy picture lifts the ground by a few percent at most (here the grain is far below the dead zone, so nothing at all).
        var mark = Pixel(withGlyph, 36, 19, 20);
        var ground = Pixel(plain, 36, 19, 20);
        Assert.IsTrue(mark.R < ground.R * 0.6 && mark.G < ground.G * 0.6, $"the mark {mark} is much darker than the ground {ground}");
    }

    [TestMethod]
    public void A_tab_reads_its_paper_from_the_legacy_frame_and_gives_it_an_edge_a_shadow_and_its_glyph()
    {
        // 46 x 41: dark stack and wood, a bright paper tab from x 9 to 33 with its lower right corner cut away, and a dark glyph on it.
        var legacy = Flat(46, 41, (50, 45, 40));
        for (var y = 0; y < 41; y++)
        for (var x = 0; x < 46; x++)
        {
            var paper = x >= 9 && x <= 33 - Math.Max(0, y - 25) && y < 40;
            if (!paper) continue;
            var i = (y * 46 + x) * 4;
            (legacy[i], legacy[i + 1], legacy[i + 2]) = (190, 185, 170);
        }
        for (var y = 10; y < 18; y++)
        for (var x = 15; x < 24; x++) { var i = (y * 46 + x) * 4; (legacy[i], legacy[i + 1], legacy[i + 2]) = (70, 50, 30); }
        int Luma((int R, int G, int B, int A) p) => p.R + p.G + p.B;
        foreach (var frame in new[] { 0, 1 })
        {
            var a = InterfaceChromeRenderer.Render(Recipe("tab_quest.art"), 46, 41, legacy, [], frame, 3);
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(Recipe("tab_quest.art"), 46, 41, legacy, [], frame, 3), "deterministic");
            var paper = Pixel(a, 138, 19 * 3, 6 * 3);
            // Averages over a few units, since the parchment and the page edge both vary in tone.
            var paperLuma = Enumerable.Range(16, 8).Average(x => Luma(Pixel(a, 138, x * 3, 6 * 3)));
            var stackLuma = Enumerable.Range(1, 7).Average(x => Luma(Pixel(a, 138, x * 3, 20 * 3)));
            Assert.IsTrue(paperLuma > 380 && paperLuma > stackLuma * 1.2, $"the paper {paperLuma} is parchment, brighter than the stack {stackLuma}");
            Assert.IsTrue(Luma(Pixel(a, 138, 19 * 3, 14 * 3)) < Luma(paper) * 0.6, "the glyph's marks are darker than the paper");
            // The paper's right edge (the last paper pixel of a row) is shaded, and the pixel just beyond it is in its shadow.
            var edge = Pixel(a, 138, 33 * 3 + 2, 12 * 3);
            var beyond = Pixel(a, 138, 34 * 3 + 1, 12 * 3);
            Assert.IsTrue(Luma(edge) < Luma(paper) * 0.8, $"edge {edge} against paper {paper}");
            // The same pixel of a frame with no paper at all is the bare ground: the paper's shadow darkens it.
            var bare = InterfaceChromeRenderer.Render(Recipe("tab_quest.art"), 46, 41, Flat(46, 41, (50, 45, 40)), [], frame, 3);
            Assert.IsTrue(Luma(beyond) < Luma(Pixel(bare, 138, 34 * 3 + 1, 12 * 3)) * 0.7, $"beyond the edge the ground is in the paper's shadow: {beyond} against {Pixel(bare, 138, 34 * 3 + 1, 12 * 3)}");
            for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i]);
        }
    }

    [TestMethod]
    public void Materials_sample_at_the_recipes_origin_so_separately_painted_pieces_join_without_a_seam()
    {
        ChromeRecipe At(int ox) => new("t.art", ChromeKind.Panel, ChromeMaterial.Parchment, ChromeSides.None, [], OriginX: ox);
        var whole = InterfaceChromeRenderer.Render(At(0), 100, 20, Flat(100, 20, (1, 2, 3)), [], 0, 1);
        var right = InterfaceChromeRenderer.Render(At(60), 40, 20, Flat(40, 20, (1, 2, 3)), [], 0, 1);
        for (var y = 0; y < 20; y += 3)
        for (var x = 0; x < 40; x += 3) Assert.AreEqual(Pixel(whole, 100, 60 + x, y), Pixel(right, 40, x, y), $"({x}, {y})");
    }

    // ARC-UI-1199: the schematic window's frames (synthetic stand-ins of the frames' sizes and holes; no game art).
    private static readonly string[] SchematicTabFiles =
        ["Anatomical_Tab.ART", "Chemistry_Tab.ART", "Electrical_Tab.ART", "Explosives_Tab.ART", "GunSmithy_Tab.ART", "Mechanical_Tab.ART", "Smithy_Tab.ART", "Theraputics_Tab.ART"];

    // The base: 800 x 400 with the lens's square hole, the record's picture hole and the eight tab slots, all transparent, in a flat opaque frame.
    private static byte[] SchematicBase(bool holes = true)
    {
        var rgba = Flat(800, 400, (90, 60, 30));
        if (!holes) return rgba;
        void Clear(int x0, int y0, int w, int h)
        {
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++) rgba[(y * 800 + x) * 4 + 3] = 0;
        }
        var (lx, ly, lw, lh) = AreaSchematicWindowLayout.RoundView;
        Clear(lx, ly, lw, lh);
        Clear(240, 146, 295, 225);
        for (var d = 0; d < AreaSchematicWindowLayout.Disciplines; d++) { var (x, y) = AreaSchematicWindowLayout.DisciplineTab(d); Clear(x, y, 27, 40); }
        return rgba;
    }

    [TestMethod]
    public void The_schematic_window_frames_have_recipes_laid_out_by_the_window_layout_and_the_pictures_stay_art()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.AllItemsAreUnique(names);
        CollectionAssert.IsSubsetOf(new[] { "Schematic_Base.ART", "Schm_LArrow.ART", "Schm_RArrow.ART", "bgrn_but.art", "CombineButt.ART", "lns_bart.art" }.Concat(SchematicTabFiles).ToArray(), names);
        Assert.AreEqual(ChromeKind.Panel, Recipe("Schematic_Base.ART").Kind);
        Assert.AreEqual(ChromeKind.Button, Recipe("bgrn_but.art").Kind);
        Assert.AreEqual(ChromeKind.Button, Recipe("CombineButt.ART").Kind);
        Assert.IsFalse(Recipe("CombineButt.ART").KeepFace, "the assemble button's face is generated; it has no glyph to keep");
        for (var d = 0; d < SchematicTabFiles.Length; d++)
        {
            var recipe = Recipe(SchematicTabFiles[d]);
            var (x, y) = AreaSchematicWindowLayout.DisciplineTab(d);
            Assert.AreEqual(ChromeKind.Tab, recipe.Kind);
            Assert.AreEqual((x, y), (recipe.OriginX, recipe.OriginY), $"{SchematicTabFiles[d]} stands where the layout puts discipline tab {d}");
            Assert.AreEqual(ChromeMaterial.Paper, recipe.TabPaper);
        }
        Assert.AreEqual((AreaSchematicWindowLayout.Previous.X, AreaSchematicWindowLayout.Previous.Y), (Recipe("Schm_LArrow.ART").OriginX, Recipe("Schm_LArrow.ART").OriginY));
        Assert.AreEqual((AreaSchematicWindowLayout.Next.X, AreaSchematicWindowLayout.Next.Y), (Recipe("Schm_RArrow.ART").OriginX, Recipe("Schm_RArrow.ART").OriginY));
        // The assemble face (358..360), the found and missing tints (830, 831) and the record's picture are pictures: no recipe.
        foreach (var file in new[] { "CombineIndicator1.ART", "CombineIndicator2.ART", "CombineIndicator3.ART", "Schem_NO_Item.ART", "Schem_Have_Item.ART" })
            CollectionAssert.DoesNotContain(names.Select(n => n.ToLowerInvariant()).ToArray(), file.ToLowerInvariant());
        // Every part of the base lies inside the 800 x 400 window.
        foreach (var part in Recipe("Schematic_Base.ART").Parts)
        {
            var (x, y, w, h) = part switch
            {
                ChromePlate p => (p.X, p.Y, p.Width, p.Height), ChromeSunk p => (p.X, p.Y, p.Width, p.Height), ChromeKeep p => (p.X, p.Y, p.Width, p.Height),
                ChromeRuling p => (p.X, p.Y, p.Width, p.Height), ChromeBare p => (p.X, p.Y, p.Width, p.Height), _ => (0, 0, 0, 0),
            };
            Assert.IsTrue(x >= 0 && y >= 0 && x + w <= 800 && y + h <= 400, $"{part}");
        }
    }

    [TestMethod]
    public void The_schematic_frames_paint_in_bounds_deterministically_at_whole_and_fractional_scales()
    {
        foreach (var (file, w, h) in new[] { ("Schematic_Base.ART", 800, 400), ("Chemistry_Tab.ART", 27, 40), ("Schm_LArrow.ART", 18, 22), ("Schm_RArrow.ART", 18, 22), ("bgrn_but.art", 37, 36), ("CombineButt.ART", 51, 51) })
        {
            var legacy = file == "Schematic_Base.ART" ? SchematicBase() : Flat(w, h, (200, 190, 170));
            var recipe = Recipe(file);
            foreach (var scale in new[] { 1.0, 1.345, 2.0 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_schematic_base_leaves_the_lens_and_the_tab_slots_plain_rims_the_records_hole_and_rules_the_page_with_one_unit_lines()
    {
        const int Scale = 2;
        var with = InterfaceChromeRenderer.Render(Recipe("Schematic_Base.ART"), 800, 400, SchematicBase(), [], 0, Scale);
        var without = InterfaceChromeRenderer.Render(Recipe("Schematic_Base.ART"), 800, 400, SchematicBase(holes: false), [], 0, Scale);
        // The lens's square and a tab slot are holes with no rim: the wood beside them is the wood of the frame with no hole.
        var (lx, ly, lw, lh) = AreaSchematicWindowLayout.RoundView;
        Assert.AreEqual(Pixel(without, 1600, (lx - 2) * Scale, (ly + 40) * Scale), Pixel(with, 1600, (lx - 2) * Scale, (ly + 40) * Scale), "beside the lens's square");
        Assert.AreEqual(Pixel(without, 1600, (lx + lw + 1) * Scale, (ly + 40) * Scale), Pixel(with, 1600, (lx + lw + 1) * Scale, (ly + 40) * Scale));
        Assert.AreEqual(0, Pixel(with, 1600, (lx + 40) * Scale, (ly + 40) * Scale).A, "the lens's square stays clear");
        var (tx, ty) = AreaSchematicWindowLayout.DisciplineTab(3);
        Assert.AreEqual(Pixel(without, 1600, (tx + 30) * Scale, (ty + 20) * Scale), Pixel(with, 1600, (tx + 30) * Scale, (ty + 20) * Scale), "right of a tab slot");
        Assert.AreEqual(Pixel(without, 1600, (tx + 10) * Scale, (ty - 2) * Scale), Pixel(with, 1600, (tx + 10) * Scale, (ty - 2) * Scale), "above a tab slot");
        // The record's picture hole has the brass rim: the pixel just outside its left edge differs from the frame with no hole.
        Assert.AreNotEqual(Pixel(without, 1600, 239 * Scale + 1, 250 * Scale), Pixel(with, 1600, 239 * Scale + 1, 250 * Scale), "the record's hole has a rim");
        // The page is ruled: a one-unit vertical line at x 227 (two device pixels here) is more orange (less blue) than the paper on both sides, a horizontal one at y 22 likewise.
        int Blue(int x, int y) => Pixel(without, 1600, x, y).B;
        var blueAt = Enumerable.Range(0, Scale).Select(k => Blue(227 * Scale + k, 100 * Scale)).Max();
        var blueBeside = new[] { Blue(224 * Scale, 100 * Scale), Blue(230 * Scale, 100 * Scale), Blue(233 * Scale, 100 * Scale) }.Min();
        Assert.IsTrue(blueAt + 20 < blueBeside, $"the line {blueAt} against the paper {blueBeside}");
        Assert.IsTrue(Blue(300 * Scale, 22 * Scale) + 20 < Blue(300 * Scale, 26 * Scale), "a horizontal line at y 22");
        Assert.IsTrue(Blue(227 * Scale + Scale, 100 * Scale) > blueAt + 15, "the line is one unit wide: the next unit is paper again");
        // The paper is a warm cream, not grey.
        var paper = Pixel(without, 1600, 400 * Scale, 100 * Scale);
        Assert.IsTrue(paper.R > paper.B + 25 && paper.R > 150, $"{paper}");
    }

    [TestMethod]
    public void A_page_button_continues_the_ruled_page_under_it_and_a_tabs_paper_is_the_same_paper()
    {
        foreach (var scale in new[] { 1.0, 2.0 })
        {
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(800, 400, scale);
            var page = InterfaceChromeRenderer.Render(Recipe("Schematic_Base.ART"), 800, 400, SchematicBase(holes: false), [], 0, scale);
            foreach (var (file, at) in new[] { ("Schm_LArrow.ART", AreaSchematicWindowLayout.Previous), ("Schm_RArrow.ART", AreaSchematicWindowLayout.Next) })
            {
                var button = InterfaceChromeRenderer.Render(Recipe(file), 18, 22, Flat(18, 22, (120, 110, 90)), [], 0, scale);
                var (bw, _) = InterfaceChromeRenderer.PixelSize(18, 22, scale);
                for (var y = 0; y < 22; y += 3)
                for (var x = 0; x < 18; x += 3)
                    Assert.AreEqual(Pixel(page, pw, (int)(at.X * scale) + (int)(x * scale), (int)(at.Y * scale) + (int)(y * scale)),
                        Pixel(button, bw, (int)(x * scale), (int)(y * scale)), $"{file} ({x}, {y}) at {scale} is the page under it");
            }
        }
        // A tab: 27 x 40, a paper bookmark (bright, cut away at the lower right) and a dark glyph on a dark ground; the paper is warm and the glyph darker than the paper.
        var legacy = Flat(27, 40, (50, 45, 40));
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 27; x++)
        {
            if (!(x >= 3 && x <= 23 - Math.Max(0, y - 25) && y < 38)) continue;
            var i = (y * 27 + x) * 4;
            (legacy[i], legacy[i + 1], legacy[i + 2]) = (190, 185, 170);
        }
        for (var y = 10; y < 18; y++)
        for (var x = 9; x < 17; x++) { var i = (y * 27 + x) * 4; (legacy[i], legacy[i + 1], legacy[i + 2]) = (70, 50, 30); }
        foreach (var frame in new[] { 0, 1 })
        {
            var tab = InterfaceChromeRenderer.Render(Recipe("Chemistry_Tab.ART"), 27, 40, legacy, [], frame, 3);
            var paper = Pixel(tab, 81, 20 * 3, 5 * 3);
            Assert.IsTrue(paper.R > paper.B + 25 && paper.R > 150, $"the paper is warm cream: {paper}");
            var glyph = Pixel(tab, 81, 13 * 3, 14 * 3);
            Assert.IsTrue(glyph.R < paper.R * 0.6, $"the glyph {glyph} is darker than the paper {paper}");
            for (var i = 3; i < tab.Length; i += 4) Assert.AreEqual(255, tab[i]);
        }
    }

    [TestMethod]
    public void The_schematic_buttons_follow_their_silhouette_and_sink_on_the_down_frame()
    {
        foreach (var (file, size) in new[] { ("bgrn_but.art", 36), ("CombineButt.ART", 51) })
        {
            var legacy = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                double dx = x + 0.5 - size / 2.0, dy = y + 0.5 - size / 2.0;
                var inside = dx * dx + dy * dy <= (size / 2.0) * (size / 2.0);
                var i = (y * size + x) * 4;
                (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = inside ? ((byte)40, (byte)40, (byte)20, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
            }
            var idle = InterfaceChromeRenderer.Render(Recipe(file), size, size, legacy, [], 0, 2);
            var down = InterfaceChromeRenderer.Render(Recipe(file), size, size, legacy, [], 1, 2);
            Assert.AreEqual(0, Pixel(idle, size * 2, 0, 0).A, $"{file}: the corner stays transparent");
            Assert.IsTrue(Pixel(idle, size * 2, size, size).A == 255, $"{file}: the middle is opaque");
            CollectionAssert.AreNotEqual(idle, down, $"{file}: the down frame is drawn differently");
        }
    }

    // ARC-UI-1819: the common dialog box (822 `DialogueBox`, 325 x 136) and its round buttons (823 `Lil_Accept`, 824 `Lil_Cancel`, 28 x 29, three frames); synthetic stand-ins, no game art.
    private const int DialogButtonWidth = 28, DialogButtonHeight = 29;

    // A round 28 x 29 button of a dark face with a coloured mark (a green block for the check, a red one for the cross) in the middle.
    private static byte[] DialogButton((byte R, byte G, byte B) mark)
    {
        var rgba = new byte[DialogButtonWidth * DialogButtonHeight * 4];
        for (var y = 0; y < DialogButtonHeight; y++)
        for (var x = 0; x < DialogButtonWidth; x++)
        {
            double dx = x + 0.5 - DialogButtonWidth / 2.0, dy = y + 0.5 - DialogButtonHeight / 2.0;
            var inside = dx * dx + dy * dy <= 13.5 * 13.5;
            var marked = x >= 9 && x < 19 && y >= 11 && y < 18;
            var i = (y * DialogButtonWidth + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = !inside ? ((byte)0, (byte)0, (byte)0, (byte)0) : marked ? (mark.R, mark.G, mark.B, (byte)255) : ((byte)40, (byte)44, (byte)24, (byte)255);
        }
        return rgba;
    }

    [TestMethod]
    public void The_dialog_box_and_its_two_buttons_have_recipes_and_the_font_has_none()
    {
        Assert.AreEqual(ChromeKind.Panel, Recipe("DialogueBox.ART").Kind);
        Assert.AreEqual(ChromeMaterial.Slate, Recipe("DialogueBox.ART").Base);
        foreach (var file in new[] { "Lil_Accept.ART", "Lil_Cancel.ART" })
        {
            Assert.AreEqual(ChromeKind.Button, Recipe(file).Kind, file);
            Assert.IsFalse(Recipe(file).KeepFace, $"{file}: the face is generated, only the mark is kept");
        }
        // The names resolve through the HD key as the viewer asks for them; the text font (229) is a picture and has no recipe.
        Assert.IsNotNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("DIALOGUEBOX.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
        Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("Flare12Font.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
    }

    [TestMethod]
    public void Every_part_of_the_dialog_recipe_lies_inside_its_frame_holds_the_text_and_covers_no_button()
    {
        var parts = Recipe("DialogueBox.ART").Parts;
        Assert.IsFalse(parts.Any(part => part is ChromeKeep), "the box keeps no picture, so none can cut into a generated plate");
        var pit = parts.OfType<ChromeSunk>().Single();
        Assert.IsTrue(pit.X >= 0 && pit.Y >= 0 && pit.X + pit.Width <= AreaDialogBoxLayout.Width && pit.Y + pit.Height <= AreaDialogBoxLayout.Height, "the pit lies inside 325 x 136");
        var (tx, ty, tw, th) = AreaDialogBoxLayout.Text;
        // The pit's one-unit rim and one-unit ink line lie outside the text, which is drawn over the black.
        Assert.IsTrue(tx >= pit.X + 2 && ty >= pit.Y + 2 && tx + tw <= pit.X + pit.Width - 2 && ty + th <= pit.Y + pit.Height - 2, "the pit holds the text's rectangle");
        foreach (var kind in new[] { AreaDialogBoxKind.Accept, AreaDialogBoxKind.Cancel, AreaDialogBoxKind.YesNo })
        foreach (var (_, _, x, y) in AreaDialogBoxLayout.Buttons(kind))
        {
            Assert.IsTrue(x >= 0 && y >= 0 && x + DialogButtonWidth <= AreaDialogBoxLayout.Width && y + DialogButtonHeight <= AreaDialogBoxLayout.Height, $"a button at ({x}, {y}) lies inside the box");
            Assert.IsFalse(x < pit.X + pit.Width && pit.X < x + DialogButtonWidth && y < pit.Y + pit.Height && pit.Y < y + DialogButtonHeight, $"the button at ({x}, {y}) stays off the text pit");
        }
    }

    [TestMethod]
    public void The_dialog_box_paints_in_bounds_deterministically_opaque_with_a_black_text_pit_at_whole_and_fractional_scales()
    {
        var recipe = Recipe("DialogueBox.ART");
        var legacy = Flat(AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height, (60, 60, 64));
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0, 2.4 })
        {
            var a = InterfaceChromeRenderer.Render(recipe, AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height, scale);
            Assert.AreEqual(pw * ph * 4, a.Length, $"at {scale}");
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, AreaDialogBoxLayout.Width, AreaDialogBoxLayout.Height, legacy, [], 0, scale), $"deterministic at {scale}");
            for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"the box is opaque at {scale}");
            var (tx, ty, tw, th) = AreaDialogBoxLayout.Text;
            foreach (var (x, y) in new[] { (tx, ty), (tx + tw - 1, ty), (tx, ty + th - 1), (tx + tw - 1, ty + th - 1), (tx + tw / 2, ty + th / 2) })
            {
                var pixel = Pixel(a, pw, (int)(x * scale), (int)(y * scale));
                Assert.IsTrue(pixel.R < 12 && pixel.G < 12 && pixel.B < 14, $"the text pit is black at ({x}, {y}) at {scale}");
            }
            // The stone around the pit is the generated slate (not black, not the pit), with a rim on the box's edge.
            var stone = Pixel(a, pw, (int)(160 * scale), (int)(110 * scale));
            Assert.IsTrue(stone.R > 15 && stone.R < 120, $"the slate at {scale}");
            // The rim on the box's edge and the pit's own line are brass (red well above blue) within a few device pixels of the edges.
            bool BrassWithin(int y0) => Enumerable.Range(y0, (int)Math.Ceiling(3 * scale) + 1).Any(y => Pixel(a, pw, (int)(160 * scale), y) is var q && q.R > q.B + 60);
            Assert.IsTrue(BrassWithin(0), $"the box's top rim is brass at {scale}");
            Assert.IsTrue(BrassWithin((int)(6 * scale) - 1), $"the pit's line is brass at {scale}");
        }
    }

    [TestMethod]
    public void The_dialog_buttons_follow_their_round_silhouette_keep_their_mark_and_sink_on_the_down_frame()
    {
        foreach (var (file, mark) in new[] { ("Lil_Accept.ART", ((byte)30, (byte)170, (byte)30)), ("Lil_Cancel.ART", ((byte)170, (byte)30, (byte)30)) })
        {
            var legacy = DialogButton(mark);
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var frames = Enumerable.Range(0, 3).Select(f => InterfaceChromeRenderer.Render(Recipe(file), DialogButtonWidth, DialogButtonHeight, legacy, [], f, scale)).ToArray();
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(DialogButtonWidth, DialogButtonHeight, scale);
                foreach (var frame in frames)
                {
                    Assert.AreEqual(pw * ph * 4, frame.Length, $"{file} at {scale}");
                    Assert.AreEqual(0, Pixel(frame, pw, 0, 0).A, $"{file}: the corner stays outside the round silhouette at {scale}");
                    var middle = Pixel(frame, pw, pw / 2, (int)(14.5 * scale));
                    Assert.AreEqual(255, middle.A, $"{file}: the middle is opaque at {scale}");
                    // The mark keeps its own colour (a green check stays green, a red cross red) on every frame.
                    Assert.IsTrue(mark.Item2 > mark.Item1 ? middle.G > middle.R + 60 : middle.R > middle.G + 60, $"{file}: the mark keeps its colour at {scale}");
                }
                CollectionAssert.AreNotEqual(frames[0], frames[1], $"{file}: the down frame is drawn differently at {scale}");
                CollectionAssert.AreNotEqual(frames[0], frames[2], $"{file}: the lit frame is drawn differently at {scale}");
                CollectionAssert.AreEqual(frames[1], InterfaceChromeRenderer.Render(Recipe(file), DialogButtonWidth, DialogButtonHeight, legacy, [], 1, scale), $"{file}: deterministic at {scale}");
            }
        }
    }

    // ARC-UI-1817: the follower panel's frames (synthetic stand-ins of the frames' sizes and holes; no game art).
    private static readonly (string File, int W, int H)[] FollowerFiles =
    [
        ("FollowerFrame.ART", 40, 49), ("FollowerArrow_DWN.ART", 67, 13), ("FollowerArrow_UP.ART", 67, 13), ("Follow_Scroll_up.ART", 33, 13), ("Follow_Scroll_up_OFF.ART", 33, 13),
        ("Follow_Scroll_dwn.ART", 34, 13), ("Follow_Scroll_dwn_OFF.ART", 34, 13), ("FollwerComandMenu.ART", InterfaceChromeRecipes.MenuWidth, InterfaceChromeRecipes.MenuHeight),
    ];

    // The portrait frame: opaque iron with the 32 x 32 portrait hole at (4, 4) transparent.
    private static byte[] FollowerFrameLegacy(bool hole = true)
    {
        var rgba = Flat(40, 49, (60, 55, 60));
        if (!hole) return rgba;
        for (var y = 4; y < 36; y++)
        for (var x = 4; x < 36; x++) rgba[(y * 40 + x) * 4 + 3] = 0;
        return rgba;
    }

    // An arrow strip: grey plate, a green triangle in the middle and a bronze rivet at each end.
    private static byte[] FollowerStrip(int w, int h)
    {
        var rgba = Flat(w, h, (110, 106, 110));
        void Set(int x, int y, (byte R, byte G, byte B) c) { var i = (y * w + x) * 4; (rgba[i], rgba[i + 1], rgba[i + 2]) = c; }
        for (var y = 4; y < 9; y++)
        for (var x = w / 2 - (y - 3); x <= w / 2 + (y - 3); x++) Set(x, y, ((x + y) % 2 == 0 ? (byte)40 : (byte)20, (byte)200, (byte)40));
        if (w > 40) foreach (var x in new[] { 5, w - 6 }) for (var y = 4; y < 9; y++) for (var dx = -2; dx <= 2; dx++) Set(x + dx, y, (150, 100, 40));
        return rgba;
    }

    [TestMethod]
    public void The_follower_panel_frames_have_recipes_laid_out_by_the_panel_layout_and_the_bars_stay_art()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.AllItemsAreUnique(names);
        CollectionAssert.IsSubsetOf(FollowerFiles.Select(f => f.File).ToArray(), names);
        foreach (var (file, w, h) in FollowerFiles)
        {
            var recipe = Recipe(file);
            Assert.AreEqual(ChromeKind.Panel, recipe.Kind, file);
            // Every recipe part lies inside its frame.
            foreach (var part in recipe.Parts)
            {
                var (x, y, pw, ph) = part switch
                {
                    ChromePlate p => (p.X, p.Y, p.Width, p.Height), ChromeSunk p => (p.X, p.Y, p.Width, p.Height), ChromeKeep p => (p.X, p.Y, p.Width, p.Height),
                    ChromeMarks p => (p.X, p.Y, p.Width, p.Height), _ => (0, 0, 0, 0),
                };
                Assert.IsTrue(x >= 0 && y >= 0 && x + pw <= w && y + ph <= h, $"{file}: {part}");
            }
        }
        // The frame's size is the layout's, the strips' and scroll buttons' are the rectangles of the panel's rules, the menu's is the art 826's.
        Assert.AreEqual((AreaFollowerFrameLayout.Width, AreaFollowerFrameLayout.Height), (40, 49));
        Assert.AreEqual((AreaFollowerPanelRules.ToggleRect.Width, AreaFollowerPanelRules.ToggleRect.Height), (67, 13));
        Assert.AreEqual((AreaFollowerPanelRules.UpRect.Width, AreaFollowerPanelRules.DownRect.Width), (33, 34));
        // The bars (504, 505, 616) are pictures: no recipe.
        foreach (var file in new[] { "FollowerFrame_FT.ART", "FollowerFrame_HP.ART", "FollowerFrame_PO.ART" })
            CollectionAssert.DoesNotContain(names.Select(n => n.ToLowerInvariant()).ToArray(), file.ToLowerInvariant());
        // The two bar channels hold the bars (34 x 3 at (3, 39) and (3, 44)): the black pit covers both, and no iron plate lies over them.
        var frame = Recipe("FollowerFrame.ART");
        Assert.AreEqual(ChromeHoles.None, frame.Holes, "the original's portrait frame has no rim around its hole");
        var pit = frame.Parts.OfType<ChromePlate>().Single(p => p.Material == ChromeMaterial.Pit && p.Y >= 37);
        // The black line around the portrait hole (32 x 32 at (4, 4)) is one unit wide and lies clear of the bars.
        var ring = frame.Parts.OfType<ChromePlate>().Single(p => p.Material == ChromeMaterial.Pit && p.Y < 37);
        Assert.AreEqual((AreaFollowerFrameLayout.ImageX - 1, AreaFollowerFrameLayout.ImageY - 1, AreaFollowerFrameLayout.PortraitSize + 2, AreaFollowerFrameLayout.PortraitSize + 2), (ring.X, ring.Y, ring.Width, ring.Height));
        Assert.IsTrue(ring.Y + ring.Height <= AreaFollowerFrameLayout.HealthBarY, "the ring stays above the bars");
        foreach (var barY in new[] { AreaFollowerFrameLayout.HealthBarY, AreaFollowerFrameLayout.FatigueBarY })
            Assert.IsTrue(pit.X <= AreaFollowerFrameLayout.BarX && pit.X + pit.Width >= AreaFollowerFrameLayout.BarX + AreaFollowerFrameLayout.BarWidth
                && pit.Y <= barY && pit.Y + pit.Height >= barY + AreaFollowerFrameLayout.BarHeight, $"the pit holds the bar at y {barY}");
        foreach (var plate in frame.Parts.OfType<ChromePlate>().Where(p => p.Material == ChromeMaterial.Iron && p.Height < AreaFollowerFrameLayout.Height))
            foreach (var barY in new[] { AreaFollowerFrameLayout.HealthBarY, AreaFollowerFrameLayout.FatigueBarY })
                Assert.IsTrue(plate.Y + plate.Height <= barY || plate.Y >= barY + AreaFollowerFrameLayout.BarHeight, $"a plate cuts into the bar at y {barY}");
        // The kept marks of a strip or scroll button lie inside its plate (they never reach the plate's bevel rows); the menu's dark bands sit under the odd rows (y 3 + 18 row, 118 x 15 at x 10).
        foreach (var file in FollowerFiles.Select(f => f.File).Where(f => f.StartsWith("Follow", StringComparison.Ordinal) && f != "FollowerFrame.ART" && f != "FollwerComandMenu.ART"))
        {
            var plate = Recipe(file).Parts.OfType<ChromePlate>().Single();
            var marks = Recipe(file).Parts.OfType<ChromeMarks>().Single();
            Assert.IsTrue(marks.X > plate.X && marks.Y > plate.Y && marks.X + marks.Width < plate.X + plate.Width && marks.Y + marks.Height < plate.Y + plate.Height, file);
        }
        var bands = Recipe("FollwerComandMenu.ART").Parts.OfType<ChromePlate>().Where(p => p.Material == ChromeMaterial.Dark).ToArray();
        Assert.AreEqual(4, bands.Length);
        for (var k = 0; k < 4; k++)
        {
            var (rx, ry, rw, rh) = AreaFollowerPanelRules.MenuRow((FollowerMenuItem)(2 * k + 1));
            Assert.IsTrue(bands[k].X <= rx && bands[k].X + bands[k].Width >= rx + rw && bands[k].Y <= ry && bands[k].Y + bands[k].Height >= ry + rh, $"band {k} holds row {2 * k + 1}");
        }
    }

    [TestMethod]
    public void The_follower_frames_paint_in_bounds_deterministically_at_whole_and_fractional_scales()
    {
        foreach (var (file, w, h) in FollowerFiles)
        {
            var legacy = file == "FollowerFrame.ART" ? FollowerFrameLegacy() : file.StartsWith("FollowerArrow", StringComparison.Ordinal) || file.StartsWith("Follow_Scroll", StringComparison.Ordinal) ? FollowerStrip(w, h) : Flat(w, h, (50, 50, 50));
            var recipe = Recipe(file);
            foreach (var scale in new[] { 1.0, 1.345, 2.0, 3.0 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                // The silhouette is the Legacy one: opaque where it is opaque, the portrait hole transparent.
                if (file == "FollowerFrame.ART")
                {
                    Assert.AreEqual(0, Pixel(a, pw, (int)(20 * scale), (int)(20 * scale)).A, $"the portrait hole stays clear at {scale}");
                    Assert.AreEqual(255, Pixel(a, pw, (int)(1 * scale), (int)(20 * scale)).A, $"the border is opaque at {scale}");
                }
                else for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_follower_frame_leaves_the_portrait_hole_plain_and_sinks_the_bar_channels()
    {
        const int Scale = 3;
        var with = InterfaceChromeRenderer.Render(Recipe("FollowerFrame.ART"), 40, 49, FollowerFrameLegacy(), [], 0, Scale);
        var without = InterfaceChromeRenderer.Render(Recipe("FollowerFrame.ART"), 40, 49, FollowerFrameLegacy(hole: false), [], 0, Scale);
        // The border pixel next to the hole is the frame's own iron, with no rim of the hole.
        Assert.AreEqual(Pixel(without, 120, 4 * Scale - 2, 20 * Scale), Pixel(with, 120, 4 * Scale - 2, 20 * Scale), "the portrait hole has no rim");
        // A bar channel is a dark pit: darker than the iron border above the hole.
        int Lum((int R, int G, int B, int A) c) => c.R + c.G + c.B;
        Assert.IsTrue(Lum(Pixel(with, 120, 10 * Scale, 40 * Scale)) * 2 < Lum(Pixel(with, 120, 20 * Scale, 1 * Scale)) + 1, "the bar channel is dark");
        Assert.IsTrue(Lum(Pixel(with, 120, 10 * Scale, 45 * Scale)) * 2 < Lum(Pixel(with, 120, 20 * Scale, 1 * Scale)) + 1, "the second channel is dark");
    }

    [TestMethod]
    public void A_follower_button_keeps_the_coloured_marks_of_the_art_and_not_its_grey_grain()
    {
        foreach (var (file, w, h) in FollowerFiles.Where(f => f.File.StartsWith("FollowerArrow", StringComparison.Ordinal) || f.File.StartsWith("Follow_Scroll", StringComparison.Ordinal)))
        {
            var legacy = FollowerStrip(w, h);
            // Grey grain on the plate: a few bright and dark pixels that must not carry over.
            for (var x = 1; x < w - 1; x += 3) { var i = (3 * w + x) * 4; (legacy[i], legacy[i + 1], legacy[i + 2]) = (200, 198, 200); }
            foreach (var scale in new[] { 1.0, 2.0, 3.0 })
            {
                var painted = InterfaceChromeRenderer.Render(Recipe(file), w, h, legacy, [], 0, scale);
                var pw = InterfaceChromeRenderer.PixelSize(w, h, scale).Width;
                // The triangle's centre pixel (a green one in the Legacy frame) is the Legacy green.
                var centre = Pixel(painted, pw, (int)((w / 2 + 0.5) * scale), (int)(7.5 * scale));
                Assert.IsTrue(centre.G > 150 && centre.R < 80, $"{file} at {scale}: the triangle is kept ({centre})");
                // The grain pixel at (4, 3) is the generated iron, not the Legacy bright grey.
                foreach (var gx in new[] { 4, 28 }) // the first lies in the marks' rectangle of a scroll button, the second in that of every button
                {
                    var grain = Pixel(painted, pw, (int)((gx + 0.5) * scale), (int)(3.5 * scale));
                    Assert.AreNotEqual((200, 198, 200), (grain.R, grain.G, grain.B), $"{file} at {scale}: the grey grain at x {gx} stays out");
                }
            }
        }
        // The rivets of a strip are generated brass, not the Legacy bronze (150, 100, 40), and the plate between the rivet and the triangle is the generated iron (grey).
        var strip = InterfaceChromeRenderer.Render(Recipe("FollowerArrow_UP.ART"), 67, 13, FollowerStrip(67, 13), [], 0, 2);
        var rivet = Pixel(strip, 134, 5 * 2 + 1, 6 * 2);
        Assert.IsTrue(rivet.R > rivet.B + 60, $"the rivet is brass: {rivet}");
        Assert.AreNotEqual((150, 100, 40), (rivet.R, rivet.G, rivet.B), "the rivet is generated, not kept");
        var plate = Pixel(strip, 134, 14 * 2, 6 * 2);
        Assert.IsTrue(Math.Abs(plate.R - plate.B) < 40, $"the plate is grey iron: {plate}");
    }

    [TestMethod]
    public void The_follower_menu_is_a_slab_with_dark_bands_under_the_odd_rows()
    {
        const int Scale = 2;
        var menu = InterfaceChromeRenderer.Render(Recipe("FollwerComandMenu.ART"), InterfaceChromeRecipes.MenuWidth, InterfaceChromeRecipes.MenuHeight,
            Flat(InterfaceChromeRecipes.MenuWidth, InterfaceChromeRecipes.MenuHeight, (40, 40, 40)), [], 0, Scale);
        var width = InterfaceChromeRecipes.MenuWidth * Scale;
        int Lum((int R, int G, int B, int A) c) => c.R + c.G + c.B;
        // The middle of a sunk band (the row Attack, y 21 to 35) is darker than the middle of the row above it (Walk, y 3 to 17).
        var (_, attackY, _, attackH) = AreaFollowerPanelRules.MenuRow(FollowerMenuItem.Attack);
        var (_, walkY, _, walkH) = AreaFollowerPanelRules.MenuRow(FollowerMenuItem.Walk);
        Assert.IsTrue(Lum(Pixel(menu, width, 60 * Scale, (attackY + attackH / 2) * Scale)) < Lum(Pixel(menu, width, 60 * Scale, (walkY + walkH / 2) * Scale)), "the odd rows sit in a dark band");
    }

    // ARC-UI-1201: the reading window. The file names are those the original's interface.mes gives the arts 494, 477, 489, 487, 491, 633, 817, 495 and 496 (read from the game at run time).
    private static readonly string[] ReadingBackgrounds = ["BookBackground.ART", "NoteBackground.ART", "NewsBackground.ART", "TeleBackground.ART", "Stone_Plaque.ART", "vendnewsback.ART"];
    private const int ReadingBackgroundWidth = 668, ReadingBackgroundHeight = 400, ReadingSideWidth = 132, ReadingSideHeight = 400, ReadingArrowWidth = 18, ReadingArrowHeight = 22;

    private static int Luma((int R, int G, int B, int A) pixel) => pixel.R + pixel.G + pixel.B;

    [TestMethod]
    public void The_reading_window_frames_have_recipes_laid_out_by_the_window_layout_and_the_lens_is_the_barter_windows()
    {
        var names = InterfaceChromeRecipes.All.Select(r => r.File).ToArray();
        CollectionAssert.AllItemsAreUnique(names);
        CollectionAssert.IsSubsetOf(ReadingBackgrounds.Concat(["WritenSideBar.ART", "WrtnBookLArro.ART", "WrtnBookRArro.ART"]).ToArray(), names);
        Assert.AreEqual(AreaReadingArtLayout.BackgroundArts.Count, ReadingBackgrounds.Length, "one recipe per background art of the window");
        Assert.AreEqual(ChromeHoles.None, Recipe("WritenSideBar.ART").Holes, "the side bar's lens is the live view");
        Assert.AreEqual(0, Recipe("WritenSideBar.ART").OriginX);
        foreach (var file in ReadingBackgrounds)
        {
            var recipe = Recipe(file);
            Assert.AreEqual(ChromeKind.Panel, recipe.Kind, file);
            Assert.AreEqual(ChromeMaterial.Wood, recipe.Base, $"{file}: the table");
            Assert.AreEqual((AreaReadingArtLayout.BackgroundCorner.X, AreaReadingArtLayout.BackgroundCorner.Y), (recipe.OriginX, recipe.OriginY), $"{file} lies where the redraw puts the backgrounds, so the wood continues the side bar's");
        }
        Assert.AreEqual(AreaReadingArtLayout.PreviousCorner.X - AreaReadingArtLayout.BackgroundCorner.X, Recipe("WrtnBookLArro.ART").OriginX);
        Assert.AreEqual(AreaReadingArtLayout.PreviousCorner.Y - AreaReadingArtLayout.BackgroundCorner.Y, Recipe("WrtnBookLArro.ART").OriginY);
        Assert.AreEqual(AreaReadingArtLayout.NextCorner.X - AreaReadingArtLayout.BackgroundCorner.X, Recipe("WrtnBookRArro.ART").OriginX);
        CollectionAssert.Contains(names, "lns_bart.art", "the lens 231 is the barter window's recipe");
        // The names resolve through the HD key as the viewer asks for them (case does not matter); the text fonts are pictures and have no recipe.
        Assert.IsNotNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("BOOKBACKGROUND.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
        Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("Flare12Font.ART", new byte[] { 1 }), 0, 1, 1, new byte[4])));
    }

    [TestMethod]
    public void Every_part_of_a_reading_recipe_lies_inside_its_frame_and_the_window_keeps_no_picture()
    {
        foreach (var file in ReadingBackgrounds)
        {
            Assert.IsFalse(Recipe(file).Parts.Any(part => part is ChromeKeep), $"{file}: no kept picture");
            foreach (var plate in Recipe(file).Parts.OfType<ChromePlate>())
                Assert.IsTrue(plate.X >= 0 && plate.Y >= 0 && plate.X + plate.Width <= ReadingBackgroundWidth && plate.Y + plate.Height <= ReadingBackgroundHeight, $"{file}: {plate} lies inside 668 x 400");
        }
        // The book's two pages meet at the fold and the clasps stand on it; the newspaper's three column rules fall between the layout's four columns.
        var book = Recipe("BookBackground.ART").Parts.OfType<ChromePlate>().ToArray();
        Assert.AreEqual(book.Single(p => p.X == 77).X + book.Single(p => p.X == 77).Width, book.Single(p => p.X == 320).X, "the pages meet at the fold");
        var rules = Recipe("NewsBackground.ART").Parts.OfType<ChromePlate>().Where(p => p.Width == 1 && p.Height > 100).Select(p => p.X + AreaReadingArtLayout.BackgroundCorner.X).ToArray();
        Assert.AreEqual(AreaReadingArtLayout.Columns.Count - 1, rules.Length);
        for (var i = 0; i < rules.Length; i++)
            Assert.IsTrue(rules[i] > AreaReadingArtLayout.Columns[i].X + AreaReadingArtLayout.Columns[i].Width - 12 && rules[i] < AreaReadingArtLayout.Columns[i + 1].X, $"rule {i} lies between column {i} and {i + 1}");
        Assert.AreEqual(rules.Length, Recipe("vendnewsback.ART").Parts.OfType<ChromePlate>().Count(p => p.Width == 1 && p.Height > 100), "the alternate newspaper is ruled the same way");
        // No column rule runs through the headline's rectangle or the plaque's text lies inside its inner panel.
        var headline = AreaReadingArtLayout.Headline;
        foreach (var file in new[] { "NewsBackground.ART", "vendnewsback.ART" })
            foreach (var rule in Recipe(file).Parts.OfType<ChromePlate>().Where(p => p.Width == 1 && p.Height > 100))
            {
                var x = rule.X + AreaReadingArtLayout.BackgroundCorner.X;
                Assert.IsFalse(x >= headline.X && x < headline.X + headline.Width && rule.Y < headline.Y + headline.Height && rule.Y + rule.Height > headline.Y, $"{file}: the rule at {x} runs through the headline");
            }
        var inner = Recipe("Stone_Plaque.ART").Parts.OfType<ChromePlate>().Last();
        var text = AreaReadingArtLayout.PlaqueText;
        Assert.IsTrue(text.X - AreaReadingArtLayout.BackgroundCorner.X >= inner.X && text.X - AreaReadingArtLayout.BackgroundCorner.X + text.Width <= inner.X + inner.Width
            && text.Y >= inner.Y && text.Y + text.Height <= inner.Y + inner.Height, "the plaque's text rectangle lies inside its inner panel");
    }

    [TestMethod]
    public void The_reading_frames_paint_in_bounds_deterministically_opaque_at_whole_and_fractional_scales()
    {
        var frames = ReadingBackgrounds.Select(file => (file, ReadingBackgroundWidth, ReadingBackgroundHeight))
            .Concat([("WritenSideBar.ART", ReadingSideWidth, ReadingSideHeight), ("WrtnBookLArro.ART", ReadingArrowWidth, ReadingArrowHeight), ("WrtnBookRArro.ART", ReadingArrowWidth, ReadingArrowHeight)]);
        foreach (var (file, w, h) in frames)
        {
            var legacy = Flat(w, h, (150, 120, 90));
            var recipe = Recipe(file);
            foreach (var scale in new[] { 1.0, 1.345, 2.0 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} is opaque like its Legacy frame at {scale}");
            }
        }
    }

    [TestMethod]
    public void The_reading_tables_hold_their_objects_the_pages_are_paper_the_rules_are_ink_and_the_plaque_is_stone()
    {
        foreach (var scale in new[] { 1.0, 2.0 })
        {
            (byte[] Pixels, int Width) Paint(string file)
            {
                var a = InterfaceChromeRenderer.Render(Recipe(file), ReadingBackgroundWidth, ReadingBackgroundHeight, Flat(ReadingBackgroundWidth, ReadingBackgroundHeight, (150, 120, 90)), [], 0, scale);
                return (a, InterfaceChromeRenderer.PixelSize(ReadingBackgroundWidth, ReadingBackgroundHeight, scale).Item1);
            }
            int At((byte[] Pixels, int Width) paint, int x, int y) => Luma(Pixel(paint.Pixels, paint.Width, (int)(x * scale), (int)(y * scale)));
            var note = Paint("NoteBackground.ART");
            Assert.IsTrue(At(note, 320, 200) > At(note, 20, 200) + 60, $"the note's sheet is lighter than the table at {scale}");
            var plaque = Paint("Stone_Plaque.ART");
            Assert.IsTrue(At(plaque, 340, 200) < At(note, 320, 200) - 100, $"the plaque's stone is darker than a sheet of paper at {scale}");
            Assert.IsTrue(At(plaque, 340, 200) > 0, "the stone is not black");
            var book = Paint("BookBackground.ART");
            Assert.IsTrue(At(book, 200, 200) > At(book, 45, 200) + 60, $"a page is lighter than the cover at {scale}");
            var paper = Paint("NewsBackground.ART");
            Assert.IsTrue(At(paper, 189, 250) < At(paper, 194, 250) - 80, $"a column rule is ink on the paper at {scale}");
            Assert.IsTrue(At(paper, 300, 83) < At(paper, 300, 70) - 80, $"the masthead's rule is ink on the paper at {scale}");
            var telegram = Paint("TeleBackground.ART");
            Assert.IsTrue(At(telegram, 186, 60) < At(telegram, 195, 60) - 80, $"the form box's edge is ink on the paper at {scale}");
        }
    }

    [TestMethod]
    public void The_reading_books_fold_shade_lies_where_the_two_pages_meet_at_every_scale()
    {
        Assert.AreEqual((int)AreaReadingArtLayout.BookFold, Recipe("BookBackground.ART").Parts.OfType<ChromePlate>().Single(p => p.X == 320).X, "the fold is where the right page starts");
        // ARC-UI-2720: the shade of the fold is sampled in window coordinates, so the book (at (132, 0)) must carry its own fold x; the darkest column of the page rows is the fold.
        foreach (var scale in new[] { 1.0, 2.0, 3.0 })
        {
            var pixels = InterfaceChromeRenderer.Render(Recipe("BookBackground.ART"), ReadingBackgroundWidth, ReadingBackgroundHeight, Flat(ReadingBackgroundWidth, ReadingBackgroundHeight, (150, 120, 90)), [], 0, scale);
            var width = InterfaceChromeRenderer.PixelSize(ReadingBackgroundWidth, ReadingBackgroundHeight, scale).Item1;
            var best = 0; var bestLuma = double.MaxValue;
            for (var x = (int)(100 * scale); x < (int)(540 * scale); x++)
            {
                double sum = 0;
                for (var y = (int)(60 * scale); y < (int)(340 * scale); y += (int)scale) sum += Luma(Pixel(pixels, width, x, y));
                if (sum < bestLuma) { bestLuma = sum; best = x; }
            }
            Assert.AreEqual(AreaReadingArtLayout.BookFold, best / scale, 1.0, $"the fold of the book lies at its page join at {scale}");
        }
    }

    // ARC-UI-1818: the quantity pane (298 `MultiMove_Base`, 416 x 112) and its five buttons (299 `Sm_RightArrow` 23 x 23, 805 `OldChar_Plus` and 804 `OldChar_Minus` 13 x 10, 33 `Done_Big` and 32 `Cncl_Big` 38 x 37; the 13 x 10 pair stay pictures);
    // synthetic stand-ins, no game art. The places are the pane's (`QuantityPaneRules`, screen coordinates less the background's (196, 492)).
    private const int QuantityWidth = 416, QuantityHeight = 112;
    private static readonly (string File, int Art, int Width, int Height, bool Round)[] QuantityButtons =
        [("Sm_RightArrow.ART", 299, 23, 23, true), ("OldChar_Plus.ART", 805, 13, 10, false), ("OldChar_Minus.ART", 804, 13, 10, false), ("Done_Big.ART", 33, 38, 37, true), ("Cncl_Big.ART", 32, 38, 37, true)];
    // The three that are drawn as generated buttons; 805 and 804 are too small for the generic button (a ten-unit button is all rim and would lose its glyph) and stay pictures on the kept counter.
    private static readonly (string File, int Art, int Width, int Height, bool Round)[] QuantityRecipeButtons = [.. QuantityButtons.Where(b => b.Art is not (805 or 804))];

    private static (int X, int Y) QuantityPlace(int art)
    {
        var button = Arcanum.Presentation.Hud.QuantityPaneRules.Buttons.Single(b => b.Art == art);
        return (button.X - Arcanum.Presentation.Hud.QuantityPaneRules.BackgroundX, button.Y - Arcanum.Presentation.Hud.QuantityPaneRules.BackgroundY);
    }

    private static bool Overlaps((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) => a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    [TestMethod]
    public void The_quantity_pane_and_its_five_buttons_have_recipes_found_by_name()
    {
        var pane = Recipe("MultiMove_Base.ART");
        Assert.AreEqual(ChromeKind.Panel, pane.Kind);
        Assert.AreEqual(ChromeMaterial.Slate, pane.Base);
        Assert.AreEqual(ChromeSides.All, pane.Rim);
        Assert.AreEqual(ChromeHoles.None, pane.Holes);
        foreach (var file in new[] { "OldChar_Plus.ART", "OldChar_Minus.ART" })
            Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity(file, new byte[] { 1 }), 0, 1, 1, new byte[4])), $"{file} stays a picture");
        foreach (var (file, _, _, _, _) in QuantityRecipeButtons)
        {
            Assert.AreEqual(ChromeKind.Button, Recipe(file).Kind, file);
            Assert.IsTrue(Recipe(file).KeepFace, $"{file}: the ball's own picture is kept inside the generated rim");
            Assert.IsNotNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity(file.ToUpperInvariant(), new byte[] { 1 }), 0, 1, 1, new byte[4])), file);
        }
        // The pane's arts are the five buttons' and the background; the count's font (25) is drawn text and has no recipe.
        CollectionAssert.AreEquivalent(Arcanum.Presentation.Hud.QuantityPaneRules.ArtNumbers.ToArray(), QuantityButtons.Select(b => b.Art).Append(298).ToArray());
        Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity("rollerfont.art", new byte[] { 1 }), 0, 1, 1, new byte[4])));
    }

    [TestMethod]
    public void Every_part_of_the_quantity_recipe_lies_inside_its_frame_the_icon_recess_holds_the_icon_and_the_kept_counter_cuts_into_no_pit_or_button()
    {
        var parts = Recipe("MultiMove_Base.ART").Parts;
        var pit = parts.OfType<ChromeSunk>().Single();
        var keep = parts.OfType<ChromeKeep>().Single();
        Assert.AreEqual(2, parts.Count);
        Assert.IsTrue(pit.X >= 0 && pit.Y >= 0 && pit.X + pit.Width <= QuantityWidth && pit.Y + pit.Height <= QuantityHeight, "the pit lies inside 416 x 112");
        Assert.IsTrue(keep.X >= 0 && keep.Y >= 0 && keep.X + keep.Width <= QuantityWidth && keep.Y + keep.Height <= QuantityHeight, "the kept counter lies inside 416 x 112");
        // The icon (71 x 41 at (269, 534) of the screen) lies in the pit's fill, its two-unit frame left out.
        var icon = (X: Arcanum.Presentation.Hud.QuantityPaneRules.IconX - Arcanum.Presentation.Hud.QuantityPaneRules.BackgroundX, Y: Arcanum.Presentation.Hud.QuantityPaneRules.IconY - Arcanum.Presentation.Hud.QuantityPaneRules.BackgroundY);
        Assert.IsTrue(icon.X >= pit.X + 2 && icon.Y >= pit.Y + 2 && icon.X + 71 <= pit.X + pit.Width - 2 && icon.Y + 41 <= pit.Y + pit.Height - 2, "the pit holds the icon");
        var pitBox = (pit.X, pit.Y, pit.Width, pit.Height);
        var keepBox = (keep.X, keep.Y, keep.Width, keep.Height);
        Assert.IsFalse(Overlaps(pitBox, keepBox), "the kept counter and the pit do not meet");
        // The count starts inside the kept counter and the up and down buttons lie on it; every other button stays off both.
        var count = (X: Arcanum.Presentation.Hud.QuantityPaneRules.CountX - Arcanum.Presentation.Hud.QuantityPaneRules.BackgroundX, Y: Arcanum.Presentation.Hud.QuantityPaneRules.CountY - Arcanum.Presentation.Hud.QuantityPaneRules.BackgroundY);
        Assert.IsTrue(count.X >= keep.X && count.Y >= keep.Y && count.Y + 20 <= keep.Y + keep.Height, "the count's first cell and the font's 20 rows lie in the kept counter");
        foreach (var (file, art, w, h, _) in QuantityButtons)
        {
            var (x, y) = QuantityPlace(art);
            Assert.IsTrue(x >= 0 && y >= 0 && x + w <= QuantityWidth && y + h <= QuantityHeight, $"{file} lies inside the pane");
            var box = (x, y, w, h);
            Assert.IsFalse(Overlaps(pitBox, box), $"{file} stays off the icon pit");
            if (art is 805 or 804) Assert.IsTrue(x >= keep.X && y >= keep.Y && x + w <= keep.X + keep.Width && y + h <= keep.Y + keep.Height, $"{file} lies on the kept counter");
            else Assert.IsFalse(Overlaps(keepBox, box), $"{file} stays off the kept counter");
        }
    }

    [TestMethod]
    public void The_quantity_pane_paints_in_bounds_deterministically_opaque_with_a_black_icon_pit_and_its_counter_kept_at_whole_and_fractional_scales()
    {
        var recipe = Recipe("MultiMove_Base.ART");
        var keep = recipe.Parts.OfType<ChromeKeep>().Single();
        var legacy = Flat(QuantityWidth, QuantityHeight, (60, 60, 64));
        // The Legacy counter is silver: its pixels must come through.
        for (var y = keep.Y; y < keep.Y + keep.Height; y++)
        for (var x = keep.X; x < keep.X + keep.Width; x++)
        {
            var i = (y * QuantityWidth + x) * 4;
            (legacy[i], legacy[i + 1], legacy[i + 2]) = (200, 200, 210);
        }
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0, 2.4 })
        {
            var a = InterfaceChromeRenderer.Render(recipe, QuantityWidth, QuantityHeight, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(QuantityWidth, QuantityHeight, scale);
            Assert.AreEqual(pw * ph * 4, a.Length, $"at {scale}");
            CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, QuantityWidth, QuantityHeight, legacy, [], 0, scale), $"deterministic at {scale}");
            for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"the pane is opaque at {scale}");
            // The icon recess is black under the icon.
            foreach (var (x, y) in new[] { (73, 44), (143, 44), (73, 82), (143, 82), (108, 63) })
            {
                var pixel = Pixel(a, pw, (int)(x * scale), (int)(y * scale));
                Assert.IsTrue(pixel.R < 12 && pixel.G < 12 && pixel.B < 14, $"the icon pit is black at ({x}, {y}) at {scale}");
            }
            // The kept counter shows the Legacy silver (well above the generated slate), the slate round it is generated.
            var counter = Pixel(a, pw, (int)((keep.X + keep.Width / 2) * scale), (int)((keep.Y + keep.Height / 2) * scale));
            Assert.IsTrue(counter.R > 170 && counter.B > 180, $"the counter keeps its Legacy picture at {scale}");
            var stone = Pixel(a, pw, (int)(180 * scale), (int)(95 * scale));
            Assert.IsTrue(stone.R > 15 && stone.R < 120, $"the slate at {scale}");
            bool BrassWithin(int y0) => Enumerable.Range(y0, (int)Math.Ceiling(3 * scale) + 1).Any(y => Pixel(a, pw, (int)(300 * scale), y) is var q && q.R > q.B + 60);
            Assert.IsTrue(BrassWithin(0), $"the pane's top rim is brass at {scale}");
        }
    }

    // A button of a dark face with a coloured mark in the middle (a round one inside its box's circle).
    private static byte[] QuantityButton(int width, int height, bool round, (byte R, byte G, byte B) mark)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double dx = x + 0.5 - width / 2.0, dy = y + 0.5 - height / 2.0;
            var inside = !round || dx * dx + dy * dy <= Math.Min(width, height) / 2.0 * Math.Min(width, height) / 2.0;
            var marked = Math.Abs(dx) < width / 5.0 && Math.Abs(dy) < height / 5.0;
            var i = (y * width + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = !inside ? ((byte)0, (byte)0, (byte)0, (byte)0) : marked ? (mark.R, mark.G, mark.B, (byte)255) : ((byte)40, (byte)44, (byte)24, (byte)255);
        }
        return rgba;
    }

    [TestMethod]
    public void The_quantity_buttons_follow_their_silhouette_keep_their_mark_and_differ_on_the_down_and_lit_frames()
    {
        foreach (var (file, art, width, height, round) in QuantityRecipeButtons)
        {
            var mark = art == 33 ? ((byte)30, (byte)170, (byte)30) : ((byte)170, (byte)30, (byte)30);
            var legacy = QuantityButton(width, height, round, mark);
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var frames = Enumerable.Range(0, 3).Select(f => InterfaceChromeRenderer.Render(Recipe(file), width, height, legacy, [], f, scale)).ToArray();
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(width, height, scale);
                foreach (var frame in frames)
                {
                    Assert.AreEqual(pw * ph * 4, frame.Length, $"{file} at {scale}");
                    if (round) Assert.AreEqual(0, Pixel(frame, pw, 0, 0).A, $"{file}: the corner stays outside the round silhouette at {scale}");
                    var middle = Pixel(frame, pw, pw / 2, ph / 2);
                    Assert.AreEqual(255, middle.A, $"{file}: the middle is opaque at {scale}");
                    Assert.IsTrue(mark.Item2 > mark.Item1 ? middle.G > middle.R + 60 : middle.R > middle.G + 60, $"{file}: the mark keeps its colour at {scale}");
                }
                CollectionAssert.AreNotEqual(frames[0], frames[1], $"{file}: the down frame is drawn differently at {scale}");
                CollectionAssert.AreNotEqual(frames[0], frames[2], $"{file}: the lit frame is drawn differently at {scale}");
                CollectionAssert.AreEqual(frames[1], InterfaceChromeRenderer.Render(Recipe(file), width, height, legacy, [], 1, scale), $"{file}: deterministic at {scale}");
            }
        }
    }

    // ARC-UI-1751: the loot window's Take all (808, 51 x 50, three frames), the body window's doll and bag toggles (338, 339, 28 x 27, two frames) and the barter window's follower cycle buttons (757, 758, 23 x 23,
    // three frames) are dark glass balls in a brass ring with a mark; the synthetic frames below have the arts' sizes and frame counts (a dark olive disc inside a gold outline, a red mark in its middle, and
    // for the big button a wooden bezel round the ring), no game data.
    private static byte[] BallButton(int w, int h, bool bezel)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            double dx = (x + 0.5 - w / 2.0) / (w / 2.0), dy = (y + 0.5 - h / 2.0) / (h / 2.0), d = Math.Sqrt(dx * dx + dy * dy);
            if (d >= 1) continue;
            var mark = Math.Abs(dx) < .2 && Math.Abs(dy) < .2;
            var colour = mark ? ((byte)220, (byte)20, (byte)20)
                : bezel && d > .82 ? ((byte)110, (byte)70, (byte)40)
                : d > (bezel ? .7 : .78) ? ((byte)200, (byte)150, (byte)40) : ((byte)40, (byte)36, (byte)8);
            var i = (y * w + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (colour.Item1, colour.Item2, colour.Item3, (byte)255);
        }
        return rgba;
    }

    [TestMethod]
    public void The_window_buttons_of_the_loot_barter_and_steal_windows_are_balls_with_kept_pictures_in_a_generated_rim()
    {
        var sizes = new Dictionary<string, (int W, int H, int Frames, bool Bezel)>
        {
            ["TakeAllButt.ART"] = (51, 50, 3, true), ["PD_lil_butt.ART"] = (28, 27, 2, false), ["Inven_lil_butt.ART"] = (28, 27, 2, false),
            ["MPCycleLeftButton.ART"] = (23, 23, 3, false), ["MPCycleRightButton.ART"] = (23, 23, 3, false),
        };
        CollectionAssert.AreEquivalent(sizes.Keys.ToArray(), InterfaceChromeRecipes.WindowBallButtonFiles.ToArray(), "the five arts the windows' own buttons are");
        foreach (var (file, (w, h, frames, bezel)) in sizes)
        {
            var recipe = Recipe(file);
            Assert.AreEqual(ChromeKind.Button, recipe.Kind, file);
            Assert.IsTrue(recipe.KeepFace, $"{file}: the ball's own picture (the mark is the button's meaning) stays inside the rim");
            var legacy = BallButton(w, h, bezel);
            var rendered = new List<byte[]>();
            foreach (var scale in new[] { 1.0, 1.345, 2.0, 2.5 })
            {
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                for (var frame = 0; frame < frames; frame++)
                {
                    var rgba = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], frame, scale);
                    Assert.AreEqual(pw * ph * 4, rgba.Length, $"{file} frame {frame} at {scale}");
                    CollectionAssert.AreEqual(rgba, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], frame, scale), $"{file} frame {frame}: deterministic at {scale}");
                    Assert.AreEqual(0, Pixel(rgba, pw, 0, 0).A, $"{file} frame {frame}: the corner is outside the round silhouette at {scale}");
                    Assert.AreEqual(255, Pixel(rgba, pw, pw / 2, ph / 2).A, $"{file} frame {frame}: the middle is drawn at {scale}");
                    var mark = Pixel(rgba, pw, pw / 2, ph / 2);
                    Assert.IsTrue(mark.R > 150 && mark.G < 70 && mark.B < 70, $"{file} frame {frame}: the red mark stays at {scale}: {mark}");
                    // Glass of the ball: left of the mark, a quarter of the way in from the rim, the dark olive stays (the plain recipe would paint the brass face there).
                    var glass = Pixel(rgba, pw, (int)(pw * 0.32), ph / 2);
                    Assert.IsTrue(glass.A == 255 && glass.R < 90 && glass.G < 90, $"{file} frame {frame}: the dark glass stays at {scale}: {glass}");
                    if (scale is 1.0 or 2.0)
                    {
                        // The silhouette is the art's: no opaque pixel where the art is transparent (the size and the hit area are the art's).
                        var s = (int)scale;
                        for (var y = 0; y < ph; y++)
                        for (var x = 0; x < pw; x++)
                            if (legacy[((y / s) * w + x / s) * 4 + 3] == 0) Assert.AreEqual(0, Pixel(rgba, pw, x, y).A, $"{file} frame {frame}: ({x}, {y}) lies outside the art's silhouette at {scale}");
                    }
                    if (scale == 2.0) rendered.Add(rgba);
                }
            }
            // The frames differ: the down frame is sunk and the hover frame lit.
            for (var a = 0; a < rendered.Count; a++)
            for (var b = a + 1; b < rendered.Count; b++) CollectionAssert.AreNotEqual(rendered[a], rendered[b], $"{file}: frames {a} and {b} differ");
            // KeepFace is what keeps the ball: a plain button recipe paints the brass face over the same art.
            var plain = new ChromeRecipe("plain.art", ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, []);
            CollectionAssert.AreNotEqual(rendered[0], InterfaceChromeRenderer.Render(plain, w, h, legacy, [], 0, 2), file);
        }
    }
}
