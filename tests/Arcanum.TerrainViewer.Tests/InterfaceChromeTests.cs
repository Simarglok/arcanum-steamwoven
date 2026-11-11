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
        var legacy = Frame(48, 20, holes: false, colour: (10, 200, 10));
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
}
