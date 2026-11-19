using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-156 (B27, B28): the original's text fonts (art 27 and 229) as the message window, the pane names, the tooltip and the
/// floaters place them (ledger ML4, ML6, HK21, CH10, MW26), on synthetic glyph frames.</summary>
[TestClass]
public sealed class AreaTextFontsTests
{
    /// <summary>A font whose every glyph is 5 wide with an advance of 4, <paramref name="height"/> high (frame 0 is character 31).</summary>
    private static (AreaArtFont Font, IReadOnlyList<AreaInterfaceFrame> Frames) Font(int height = 12)
    {
        var frames = Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, height, new byte[5 * height * 4], 4)).ToArray();
        return (new AreaArtFont(frames), frames);
    }

    [TestMethod]
    public void The_fonts_are_arts_27_229_230_25_171_and_486_and_the_line_is_the_tallest_glyph()
    {
        // ARC-UI-124: art 230 is the combat panel's number font.
        // ARC-UI-1884: art 25 is the roller font of the quantity pane's count; it is optional like the number font.
        // ARC-UI-2368: art 171 is the bottom strip's bar font of the number slots; it is optional too.
        // ARC-UI-2795: art 486 is the inventory windows' rating font; optional as well.
        CollectionAssert.AreEqual(new[] { 27, 229, 230, 25, 171, 486 }, AreaTextFontLayout.ArtNumbers.ToArray());
        CollectionAssert.AreEqual(new[] { 27, 229 }, AreaTextFontLayout.RequiredArtNumbers.ToArray());
        Assert.AreEqual(12, AreaTextFontLayout.LineHeight(Font(12).Frames));
        Assert.AreEqual(0, AreaTextFontLayout.LineHeight([]));
        Assert.IsFalse(AreaTextFontContent.Empty("none").ArtReady);
    }

    [TestMethod]
    public void A_text_is_broken_at_the_rectangles_width_and_drawn_while_the_height_takes_a_line()
    {
        var (font, _) = Font();
        // 5 wide with an advance of 4: "aaaa" is 4 × 4 + 5 = 17 wide.
        var one = AreaTextFontLayout.Place(font, 12, (0, 0, 291, 18), "aaaa");
        Assert.IsTrue(one.Fits);
        Assert.AreEqual(1, one.Lines.Count);
        Assert.AreEqual((0, 0), (one.Lines[0].X, one.Lines[0].Y));
        // 20 wide holds "ab cd" (5 characters: 4 × 4 + 5 = 21 does not): the line breaks at the space, the next starts after it.
        var wrapped = AreaTextFontLayout.Place(font, 12, (0, 0, 20, 24), "ab cd");
        Assert.IsTrue(wrapped.Fits);
        CollectionAssert.AreEqual(new[] { 0, 12 }, wrapped.Lines.Select(line => line.Y).ToArray(), "one line height below the other");
        // A height for one line: the second is not drawn and the text does not fit.
        var cut = AreaTextFontLayout.Place(font, 12, (0, 0, 20, 18), "ab cd");
        Assert.IsFalse(cut.Fits);
        Assert.AreEqual(1, cut.Lines.Count);
        // A height below one line still draws one (a window must not blank when its font is taller than the rectangle).
        var low = AreaTextFontLayout.Place(font, 12, (0, 0, 291, 11), "a");
        Assert.IsTrue(low.Fits);
        Assert.AreEqual(1, low.Lines.Count);
        Assert.AreEqual(1, AreaTextFontLayout.Place(font, 12, (0, 0, 20, 11), "ab cd").Lines.Count);
    }

    [TestMethod]
    public void The_number_font_is_optional_and_the_title_and_text_fonts_do_not_need_it()
    {
        var (_, frames) = Font();
        AreaInterfaceArtLoad Ready(int number) => new(number, AreaInterfaceArtStatus.Ready, "f.art", frames, "");
        var two = new AreaTextFontContent(new Dictionary<int, AreaInterfaceArtLoad> { [27] = Ready(27), [229] = Ready(229) }, "");
        Assert.IsTrue(two.ArtReady);
        Assert.IsFalse(two.NumberArtReady, "ARC-UI-124: art 230 missing, the text font draws the number");
        var three = new AreaTextFontContent(new Dictionary<int, AreaInterfaceArtLoad> { [27] = Ready(27), [229] = Ready(229), [230] = Ready(230) }, "");
        Assert.IsTrue(three.ArtReady && three.NumberArtReady);
        Assert.IsFalse(new AreaTextFontContent(new Dictionary<int, AreaInterfaceArtLoad> { [229] = Ready(229), [230] = Ready(230) }, "").ArtReady);
    }

    [TestMethod]
    public void A_centred_line_starts_at_the_free_width_halved_and_a_wider_one_at_the_left()
    {
        // ARC-UI-124 (flag 0x4 of 0x550A10): "aaaa" is 17 wide; in 64: (64 − 17) / 2 = 23; a line as wide as the rectangle stays at its left.
        var (font, _) = Font();
        Assert.AreEqual(23, AreaTextFontLayout.Place(font, 12, (0, 0, 64, 64), "aaaa", centre: true).Lines[0].X);
        Assert.AreEqual(0, AreaTextFontLayout.Place(font, 12, (0, 0, 17, 64), "aaaa", centre: true).Lines[0].X);
        Assert.AreEqual(0, AreaTextFontLayout.Place(font, 12, (0, 0, 64, 64), "aaaa").Lines[0].X, "left-aligned by default");
        Assert.AreEqual(47, AreaTextFontLayout.Place(font, 12, (0, 0, 64, 64), "aaaa", right: true).Lines[0].X);
    }

    [TestMethod]
    public void A_scan_that_passes_the_width_with_no_break_does_not_fit()
    {
        var (font, _) = Font();
        var placed = AreaTextFontLayout.Place(font, 12, (0, 0, 20, 82), "aaaaaaaaaaaa");
        Assert.IsFalse(placed.Fits, "MW26: no space or hyphen to break at");
        Assert.AreEqual(0, placed.Lines.Count);
        AreaPremadeBrowserArtLayout.Wrap(font, "aaaaaaaaaaaa", 20, out var complete);
        Assert.IsFalse(complete);
        AreaPremadeBrowserArtLayout.Wrap(font, "aa aa", 20, out complete);
        Assert.IsTrue(complete);
        Assert.AreEqual(0, AreaTextFontLayout.Place(font, 12, (0, 0, 20, 82), "").Lines.Count);
        Assert.IsTrue(AreaTextFontLayout.Place(font, 12, (0, 0, 20, 82), "").Fits);
    }

    [TestMethod]
    public void The_wide_layouts_cut_characters_until_the_text_fits()
    {
        var (font, _) = Font();
        var text = "aa aa aa aa aa";
        var shown = text;
        var placed = AreaTextFontLayout.Place(font, 12, (0, 0, 20, 12), shown);
        Assert.IsFalse(placed.Fits);
        while (shown.Length > 0 && !placed.Fits) { shown = shown[..^1]; placed = AreaTextFontLayout.Place(font, 12, (0, 0, 20, 12), shown); }
        Assert.IsTrue(placed.Fits);
        Assert.IsTrue(shown.Length > 0 && shown.Length < text.Length);
    }

    [TestMethod]
    public void A_right_aligned_one_line_text_ends_at_the_rectangles_edge()
    {
        var (font, _) = Font();
        // "aaaa" is 17 wide: it starts 291 − 17 = 274 from the left.
        Assert.AreEqual(274, AreaTextFontLayout.Place(font, 12, (0, 0, 291, 19), "aaaa", right: true).Lines[0].X);
        Assert.AreEqual(0, AreaTextFontLayout.Place(font, 12, (0, 0, 291, 19), "aaaa").Lines[0].X, "left by default");
        // A text that wraps is not aligned.
        Assert.AreEqual(0, AreaTextFontLayout.Place(font, 12, (0, 0, 20, 24), "ab cd", right: true).Lines[0].X);
    }

    [TestMethod]
    public void A_pane_name_is_centred_both_ways()
    {
        var (font, _) = Font();
        // "aaaa" is 17 wide in 227 × 18: (227 − 17) / 2 = 105 and (18 − 12) / 2 = 3.
        var line = AreaTextFontLayout.Centred(font, 12, (355, 506, 227, 18), "aaaa").Lines[0];
        Assert.AreEqual((105, 3), (line.X, line.Y));
        // A line wider than the field starts at its left; a field lower than the line gives a free height of at most 0 toward zero.
        Assert.AreEqual(0, AreaTextFontLayout.Centred(font, 12, (0, 0, 10, 18), "aaaa").Lines[0].X);
        Assert.AreEqual(0, AreaTextFontLayout.Centred(font, 19, (0, 0, 227, 18), "aaaa").Lines[0].Y);
    }

    [TestMethod]
    public void A_tooltip_block_stacks_its_lines_and_measures_them()
    {
        var (font, _) = Font();
        var (placed, width, height) = AreaTextFontLayout.Block(font, 12, "aaaa\naa");
        Assert.AreEqual(2, placed.Lines.Count);
        Assert.AreEqual(12, placed.Lines[1].Y);
        Assert.AreEqual(17, width, "the widest line");
        Assert.AreEqual(24, height);
    }
}
