using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-UI-1115: a line of the item window that is longer than its rectangle (0x554F10 draws every line through the text draw 0x550A10,
/// which breaks it at the rectangle's width and draws the lines the rectangle's height holds, ledger OH20), on synthetic glyph frames.
/// </summary>
[TestClass]
public sealed class AreaItemWindowWrapTests
{
    private static readonly (int X, int Y, int Width, int Height) Second = (290, 528, 291, 18);

    /// <summary>A font whose every glyph is 5 wide with an advance of 4 and <paramref name="height"/> high (frame 0 is character 31).</summary>
    private static AreaArtFont Font(int height) =>
        new(Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, height, new byte[5 * height * 4], 4)).ToArray());

    private static AreaMessageDraw Draw(AreaItemLines lines) =>
        AreaMessageLayout.Draw(AreaHoverDescriptions.ItemWindow(new(441, "Sword", AreaMessageColour.White) { Lines = lines }));

    private static int Width(AreaArtTextLine line) => line.Glyphs.Count == 0 ? 0 : line.Glyphs[^1].X + line.Glyphs[^1].Glyph.Width;

    [TestMethod]
    public void ALongLineIsDrawnInItsOwnRectangleWithoutAWideLayoutOrACutOfCharacters()
    {
        // 25 words of "abcdef" (each 7 characters with its space, 4 pixels a character) take about 700 pixels, over twice the rectangle's width.
        var longLine = string.Join(" ", Enumerable.Repeat("abcdef", 25));
        var draw = Draw(new(Second: longLine, Third: longLine, ThirdRight: longLine, Weight: longLine, HitPoints: longLine, Speed: longLine));
        // The item window draws no second layout (the original ignores the result of 0x550A10 here) and cuts no characters (flag 0x20 is not
        // passed): every text is the whole line, in the rectangle of the original's table.
        Assert.IsFalse(draw.Widens);
        Assert.AreEqual(7, draw.Texts.Count);
        Assert.IsTrue(draw.Texts.Skip(1).All(text => !text.Truncate && text.Text == longLine));
        Assert.AreEqual(Second, draw.Texts[2].Rect);
    }

    [TestMethod]
    public void ALongLineIsBrokenAtItsRectanglesWidthAndOnlyTheLinesItsHeightHoldsAreDrawn()
    {
        var longLine = string.Join(" ", Enumerable.Repeat("abcdef", 25));
        // A font 12 high: the 18 rows of the rectangle hold one line, the first broken at a space within the width; the rest is not drawn.
        var one = AreaTextFontLayout.Place(Font(12), 12, Second, longLine);
        Assert.AreEqual(1, one.Lines.Count);
        Assert.IsFalse(one.Fits, "the text does not fit; the item window does nothing about it");
        Assert.IsTrue(Width(one.Lines[0]) <= Second.Width);
        // 0x535C40: the scan passes 291 at its 73rd character, so the line ends at the last space before it, the 70th: ten words and their spaces.
        Assert.AreEqual(10 * 7, one.Lines[0].Glyphs.Count, "the line holds its break character");
        // A font 9 high: the rectangle holds two lines (0x5357C2: a line is drawn while the remaining height holds one).
        var two = AreaTextFontLayout.Place(Font(9), 9, Second, longLine);
        Assert.AreEqual(2, two.Lines.Count);
        CollectionAssert.AreEqual(new[] { 0, 9 }, two.Lines.Select(line => line.Y).ToArray());
        // A text that fits is not cut.
        Assert.IsTrue(AreaTextFontLayout.Place(Font(12), 12, Second, "D:1-6  FT:1-2  TH:+3  ").Fits);
    }

    [TestMethod]
    public void ARightAlignedLongTextStartsAtTheRectanglesLeftAndAShortOneEndsAtItsRightEdge()
    {
        var font = Font(12);
        var longLine = string.Join(" ", Enumerable.Repeat("abcdef", 25));
        // 0x550A10 measures the text as one line: wider than the rectangle it is broken and drawn from the left, no matter the flag 2.
        Assert.AreEqual(0, AreaTextFontLayout.Place(font, 12, Second, longLine, right: true).Lines[0].X);
        // Narrower, it ends at the right edge: "aaaa" is 4 × 4 + 5 = 17 wide.
        var narrow = AreaTextFontLayout.Place(font, 12, Second, "aaaa", right: true);
        Assert.AreEqual(Second.Width - 17, narrow.Lines[0].X);
        Assert.IsTrue(narrow.Fits);
    }

    [TestMethod]
    public void ALineWithNoPlaceToBreakThatIsWiderThanTheRectangleDrawsNothing()
    {
        // 0x535C40 returns -1 for a scan that passes the width with no space or hyphen: the draw stops before the first line.
        var none = AreaTextFontLayout.Place(Font(12), 12, Second, new string('a', 100));
        Assert.AreEqual(0, none.Lines.Count);
        Assert.IsFalse(none.Fits);
        // With a hyphen to break at, the line is drawn up to it.
        var hyphen = AreaTextFontLayout.Place(Font(12), 12, Second, string.Concat(Enumerable.Repeat("aaaaaaa-", 20)));
        Assert.AreEqual(1, hyphen.Lines.Count);
        Assert.IsTrue(hyphen.Lines[0].Glyphs.Count > 0 && Width(hyphen.Lines[0]) <= Second.Width);
    }

    [TestMethod]
    public void EveryRectangleOfAnItemWindowLiesInsideTheMessageWindowSoTheDrawsBoundsCheckNeverRefusesIt()
    {
        // 0x51E190 refuses (0xC, nothing drawn) a rectangle that is not inside the window's; the item window's seven all are.
        var draw = Draw(new("S", "2", "3", "3r", "4", "4r"));
        var (wx, wy, ww, wh) = AreaMessageLayout.Window;
        Assert.AreEqual(7, draw.Texts.Count);
        foreach (var text in draw.Texts)
        {
            var (x, y, width, height) = text.Rect;
            Assert.IsTrue(x >= wx && y >= wy && x + width <= wx + ww && y + height <= wy + wh, $"{text.Text} {text.Rect}");
        }
    }
}
