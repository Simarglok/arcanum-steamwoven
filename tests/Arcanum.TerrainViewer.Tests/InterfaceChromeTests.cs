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
}
