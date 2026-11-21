using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-070 art-font text blend (ledger MW31): the blend of a font's flags, the original's arithmetic for one pixel, the
/// glyph pixels a premultiplied-alpha canvas draws and the reference render, on synthetic values.</summary>
[TestClass]
public sealed class AreaArtTextTests
{
    [TestMethod]
    public void A_fonts_flags_choose_its_blend()
    {
        Assert.AreEqual(AreaArtTextBlend.Coverage, AreaArtTextRules.Blend(0), "MW14/DB2: no flags");
        Assert.AreEqual(AreaArtTextBlend.Coverage, AreaArtTextRules.Blend(0x10 | 0x8 | 0x1), "MW10: centring, shadow and scale do not change it");
        Assert.AreEqual(AreaArtTextBlend.Additive, AreaArtTextRules.Blend(0x100), "MW3: the brown pass");
        Assert.AreEqual(AreaArtTextBlend.Additive, AreaArtTextRules.Blend(0x180), "the additive bit wins over 0x80");
        Assert.AreEqual(AreaArtTextBlend.Multiply, AreaArtTextRules.Blend(0x80));
    }

    [TestMethod]
    public void The_menu_passes_are_cover_add_cover()
    {
        CollectionAssert.AreEqual(new[] { AreaArtTextBlend.Coverage, AreaArtTextBlend.Additive, AreaArtTextBlend.Coverage },
            AreaMenuArtLayout.Passes.Select(pass => pass.Blend).ToArray(), "MW3/MW31: font flags 0, 0x100, 0");
    }

    [TestMethod]
    public void The_cover_mixes_the_colour_by_the_glyphs_red_level()
    {
        (byte, byte, byte) screen = (200, 100, 50), colour = (100, 0, 0);
        Assert.AreEqual(screen, AreaArtTextRules.Apply(screen, (255, 255, 255, 0), colour, AreaArtTextBlend.Coverage), "index 0 leaves the screen");
        // 200 + (100 − 200) × 128 >> 8 = 150; 100 + (−100 × 128 >> 8) = 50; 50 + (−50 × 128 >> 8) = 25.
        Assert.AreEqual(((byte)150, (byte)50, (byte)25), AreaArtTextRules.Apply(screen, (128, 0, 0, 255), colour, AreaArtTextBlend.Coverage));
        // Only red counts: green and blue of the glyph do not.
        Assert.AreEqual(AreaArtTextRules.Apply(screen, (128, 0, 0, 255), colour, AreaArtTextBlend.Coverage),
            AreaArtTextRules.Apply(screen, (128, 255, 7, 255), colour, AreaArtTextBlend.Coverage));
        // A full cover leaves the screen one level short of the colour at most (the shift by 8), rounding down.
        Assert.AreEqual(((byte)100, (byte)0, (byte)0), AreaArtTextRules.Apply(screen, (255, 255, 255, 255), colour, AreaArtTextBlend.Coverage));
        Assert.AreEqual(((byte)254, (byte)254, (byte)254), AreaArtTextRules.Apply((0, 0, 0), (255, 255, 255, 255), (255, 255, 255), AreaArtTextBlend.Coverage));
        Assert.AreEqual(screen, AreaArtTextRules.Apply(screen, (0, 0, 0, 255), colour, AreaArtTextBlend.Coverage), "a black glyph pixel covers nothing");
    }

    [TestMethod]
    public void The_additive_and_multiply_blends_take_the_product_table()
    {
        (byte, byte, byte) screen = (200, 100, 50), colour = (97, 61, 42);
        // glyph × colour / 255 rounded down: 128 × 97 / 255 = 48, 128 × 61 / 255 = 30, 128 × 42 / 255 = 21.
        Assert.AreEqual(((byte)248, (byte)130, (byte)71), AreaArtTextRules.Apply(screen, (128, 128, 128, 255), colour, AreaArtTextBlend.Additive));
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), AreaArtTextRules.Apply((250, 250, 250), (255, 255, 255, 255), (255, 255, 255), AreaArtTextBlend.Additive),
            "held at the channel's top");
        Assert.AreEqual(((byte)48, (byte)30, (byte)21), AreaArtTextRules.Apply(screen, (128, 128, 128, 255), colour, AreaArtTextBlend.Multiply));
        Assert.AreEqual(screen, AreaArtTextRules.Apply(screen, (128, 128, 128, 0), colour, AreaArtTextBlend.Multiply));
    }

    [TestMethod]
    public void A_premultiplied_canvas_gives_the_originals_result_within_one_level()
    {
        var values = Enumerable.Range(0, 52).Select(value => (byte)(value * 5)).Concat(new byte[] { 1, 7, 31, 128, 254 }).ToArray();
        foreach (var blend in Enum.GetValues<AreaArtTextBlend>())
            foreach (var glyph in values)
                foreach (var colour in values)
                    foreach (var screen in values)
                    {
                        var pixel = AreaArtTextRules.LayerPixels(new byte[] { glyph, (byte)(255 - glyph), glyph, 255 }, blend);
                        // The canvas: the texture times the drawn colour, plus the screen times one minus the texture's alpha.
                        var drawn = Math.Clamp(Math.Round(pixel[0] * colour / 255.0 + screen * (1 - pixel[3] / 255.0)), 0, 255);
                        var original = blend == AreaArtTextBlend.Coverage
                            ? AreaArtTextRules.Apply((screen, screen, screen), (glyph, 0, 0, 255), (colour, colour, colour), blend).Item1
                            : AreaArtTextRules.Apply((screen, screen, screen), (glyph, glyph, glyph, 255), (colour, colour, colour), blend).Item1;
                        Assert.IsTrue(Math.Abs(drawn - original) <= 1, $"{blend} glyph {glyph} colour {colour} screen {screen}: {drawn} vs {original}");
                    }
        CollectionAssert.AreEqual(new byte[4], AreaArtTextRules.LayerPixels(new byte[] { 9, 9, 9, 0 }, AreaArtTextBlend.Multiply), "index 0 stays empty");
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9, 9 }, AreaArtTextRules.LayerPixels(new byte[] { 9, 50, 70, 255 }, AreaArtTextBlend.Coverage));
        CollectionAssert.AreEqual(new byte[] { 254, 254, 254, 254 }, AreaArtTextRules.LayerPixels(new byte[] { 255, 0, 0, 255 }, AreaArtTextBlend.Coverage),
            "the cover's weight a / 256 as w / 255");
        CollectionAssert.AreEqual(new byte[] { 9, 50, 70, 0 }, AreaArtTextRules.LayerPixels(new byte[] { 9, 50, 70, 255 }, AreaArtTextBlend.Additive));
        Assert.Throws<ArgumentException>(() => AreaArtTextRules.LayerPixels(new byte[3], AreaArtTextBlend.Coverage));
    }

    [TestMethod]
    public void The_reference_render_places_and_clips_the_glyphs()
    {
        // Two 2 × 1 glyphs (characters 31 and 32): frame 0 red 255 then index 0, frame 1 red 128 twice; advance 2.
        var frames = new[]
        {
            new AreaInterfaceFrame(2, 1, [255, 255, 255, 255, 0, 0, 0, 0], 2),
            new AreaInterfaceFrame(2, 1, [128, 128, 128, 255, 128, 128, 128, 255], 2),
        };
        var screen = new byte[5 * 1 * 4];
        AreaArtTextRules.Render(screen, 5, new AreaArtFont(frames), frames, "\u001f ", 1, 0, (200, 0, 0), AreaArtTextBlend.Coverage);
        var reds = Enumerable.Range(0, 5).Select(x => screen[x * 4]).ToArray();
        CollectionAssert.AreEqual(new byte[] { 0, 199, 0, 100, 100 }, reds, "x 1 full cover, x 2 index 0, x 3–4 half cover");
        var clipped = new byte[2 * 1 * 4];
        AreaArtTextRules.Render(clipped, 2, new AreaArtFont(frames), frames, " ", 1, 0, (200, 0, 0), AreaArtTextBlend.Coverage);
        Assert.AreEqual(100, clipped[4], "the part past the screen's edge is dropped");
    }

    [TestMethod]
    public void The_area_copy_takes_only_the_Steamwoven_step_below_point_four_of_the_4x_frame()
    {
        // ARC-RENDER-2939: a 4x pack frame drawn at 3.6, 2.4, 1.8 and 1.2 device px per original px is 0.9, 0.6, 0.45 and 0.3 of its pixels.
        Assert.IsFalse(AreaArtTextRules.NeedsAreaCopy(3.6f, 4), "x1 keeps the owner's bilinear sample (ARC-UI-145)");
        Assert.IsFalse(AreaArtTextRules.NeedsAreaCopy(2.4f, 4), "x2/3 (0.6) keeps it");
        Assert.IsFalse(AreaArtTextRules.NeedsAreaCopy(1.8f, 4), "x1/2 (0.45) keeps it (owner decision 2026-11-21)");
        Assert.IsFalse(AreaArtTextRules.NeedsAreaCopy(1.6f, 4), "exactly 0.4 stays bilinear, not broken by float rounding");
        Assert.IsTrue(AreaArtTextRules.NeedsAreaCopy(1.2f, 4), "x1/3 (0.3) takes the area copy");
        Assert.IsTrue(AreaArtTextRules.NeedsAreaCopy(1.5f, 4), "a scale between the steps (0.375) takes it too");
        Assert.IsFalse(AreaArtTextRules.NeedsAreaCopy(0f, 4), "an unknown scale (0: no Steamwoven layout) never takes it");
        Assert.IsFalse(AreaArtTextRules.NeedsAreaCopy(1.2f, 1), "a pack that is not a redraw at 2x or more is left alone");
    }

    [TestMethod]
    public void Area_average_is_the_exact_box_filter_on_a_premultiplied_layer()
    {
        // A 4 x 4 layer of one white 2 x 2 block (255 on every channel) in the top-left corner: to 2 x 2 the block is one full pixel, to 1 x 1 a quarter.
        var layer = new byte[4 * 4 * 4];
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) }) for (var c = 0; c < 4; c++) layer[(y * 4 + x) * 4 + c] = 255;
        var half = AreaArtTextRules.AreaAverage(layer, 4, 4, 2, 2);
        CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 255 }, half[..4], "the block is one full pixel");
        Assert.AreEqual(0, half[4], "the next pixel has no ink");
        Assert.AreEqual(64, AreaArtTextRules.AreaAverage(layer, 4, 4, 1, 1)[0], "a quarter of the area is ink (255 / 4 rounded)");
        // 4 to 3 weighs the pixels a target edge splits: a one-pixel-wide full column becomes a third of a pixel per target pixel it touches.
        var column = new byte[4 * 1 * 4];
        for (var c = 0; c < 4; c++) column[c] = 255;
        var third = AreaArtTextRules.AreaAverage(column, 4, 1, 3, 1);
        Assert.AreEqual(191, third[0], "target pixel 0 covers 4/3 source px: 255 of its 4/3 -> 255 * 3/4 = 191");
        Assert.AreEqual(0, third[8], "the last target pixel has no ink");
        CollectionAssert.AreEqual(layer, AreaArtTextRules.AreaAverage(layer, 4, 4, 4, 4), "the same size is the layer itself");
        Assert.Throws<ArgumentException>(() => AreaArtTextRules.AreaAverage(new byte[3], 1, 1, 1, 1));
    }
}
