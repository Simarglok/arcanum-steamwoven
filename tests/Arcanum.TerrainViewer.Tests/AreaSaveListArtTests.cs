using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-062 save and load window art (ledger MW9–MW13): the art, the buttons' places, the rows' places, the scroll top, the
/// row a press picks and the "..." fit, on synthetic glyph frames.</summary>
[TestClass]
public sealed class AreaSaveListArtTests
{
    /// <summary>A font whose every glyph is 5 wide with an advance of 4, 19 high (frame 0 is character 31).</summary>
    private static AreaArtFont Font() =>
        new([.. Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4))]);

    [TestMethod]
    public void The_window_reads_the_originals_art()
    {
        CollectionAssert.AreEqual(new[] { 745, 336, 335, 321, 323, 32, 27 }, AreaSaveListArtLayout.ArtNumbers.ToArray());
        Assert.AreEqual((42, 161, 145, 213), AreaSaveListArtLayout.List, "MW11: the window's (42, 120) below the top strip");
        Assert.AreEqual((281, 96, 468, 300), AreaSaveListArtLayout.Preview);
        Assert.AreEqual((213, 152, 12, 232), AreaSaveListArtLayout.ScrollBar);
        CollectionAssert.AreEqual(new[] { (200, 441, 400, 47), (0, 441, 200, 159), (600, 441, 200, 159) }, AreaSaveListArtLayout.BottomStripParts.ToArray());
        Assert.IsFalse(AreaSaveListArtContent.Empty("none").ArtReady);
        Assert.IsNull(AreaSaveListArtContent.Empty("none").Font);
        Assert.AreEqual("New", AreaSaveListArtContent.Empty("none").NewSaveLabel);
    }

    [TestMethod]
    public void The_buttons_stand_at_the_exes_places()
    {
        Assert.AreEqual((321, 675, 496), AreaSaveListArtLayout.Button(AreaSaveListArtButton.Confirm), "MW10: bottom strip (675, 55)");
        Assert.AreEqual((323, 69, 496), AreaSaveListArtLayout.Button(AreaSaveListArtButton.Back));
        Assert.AreEqual((32, 115, 396), AreaSaveListArtLayout.Button(AreaSaveListArtButton.Delete), "a window button: the screen's point");
        Assert.AreEqual(7021, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Confirm, AreaSaveListMode.Save));
        Assert.AreEqual(7020, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Confirm, AreaSaveListMode.Load));
        Assert.AreEqual(7002, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Back, AreaSaveListMode.Load));
        Assert.AreEqual(7022, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Delete, AreaSaveListMode.Save));
    }

    [TestMethod]
    public void Rows_stand_twenty_apart_and_eleven_show()
    {
        Assert.AreEqual((42, 161, 141, 20), AreaSaveListArtLayout.Row(0));
        Assert.AreEqual((42, 361, 141, 20), AreaSaveListArtLayout.Row(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSaveListArtLayout.Row(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSaveListArtLayout.Row(-1));
    }

    [TestMethod]
    public void The_scroll_top_follows_the_selection()
    {
        Assert.AreEqual(0, AreaSaveListArtLayout.ScrollTop(0, 10, 30), "the eleventh row is still shown");
        Assert.AreEqual(1, AreaSaveListArtLayout.ScrollTop(0, 11, 30), "Down past it scrolls it to the bottom (0x5430D0)");
        Assert.AreEqual(5, AreaSaveListArtLayout.ScrollTop(8, 5, 30), "Up above the top scrolls to it (0x543060)");
        Assert.AreEqual(8, AreaSaveListArtLayout.ScrollTop(8, -1, 30), "no selection keeps the top");
        Assert.AreEqual(4, AreaSaveListArtLayout.ScrollTop(20, 14, 15), "the top stays within the rows less eleven");
        Assert.AreEqual(0, AreaSaveListArtLayout.ScrollTop(3, 2, 5));
        Assert.AreEqual(0, AreaSaveListArtLayout.MaxScrollTop(11));
        Assert.AreEqual(1, AreaSaveListArtLayout.MaxScrollTop(12));
    }

    [TestMethod]
    public void A_press_picks_the_row_under_it()
    {
        Assert.AreEqual(0, AreaSaveListArtLayout.RowAt(42, 161, 0, 5, AreaSaveListMode.Load));
        Assert.AreEqual(4, AreaSaveListArtLayout.RowAt(100, 161 + 59, 2, 5, AreaSaveListMode.Load), "the top plus the rows below the list's top");
        Assert.AreEqual(-1, AreaSaveListArtLayout.RowAt(100, 161 + 100, 0, 3, AreaSaveListMode.Load), "past the end: none in the load window");
        Assert.AreEqual(3, AreaSaveListArtLayout.RowAt(100, 161 + 100, 0, 4, AreaSaveListMode.Save), "and the last row in the save window");
        Assert.IsNull(AreaSaveListArtLayout.RowAt(41, 170, 0, 5, AreaSaveListMode.Load), "left of the list");
        Assert.IsNull(AreaSaveListArtLayout.RowAt(187, 170, 0, 5, AreaSaveListMode.Load), "its right edge is outside");
        Assert.IsNull(AreaSaveListArtLayout.RowAt(100, 161 + 213, 0, 30, AreaSaveListMode.Load), "below the list");
    }

    [TestMethod]
    public void A_wide_description_ends_in_dots()
    {
        var font = Font();
        Assert.AreEqual(21, font.Measure("abcde").Width, "advances 4 each, the last its width 5");
        Assert.AreEqual("abcde", AreaSaveListArtLayout.Fit(font, "abcde", 22));
        Assert.AreEqual("a...", AreaSaveListArtLayout.Fit(font, "abcde", 21), "a text as wide as the row is cut (0x542D00 draws whole only when narrower)");
        // "..." measures 13; the room is 22 − 13 = 9: two letters (9).
        Assert.AreEqual("ab...", AreaSaveListArtLayout.Fit(font, "abcdef", 22));
        Assert.AreEqual("...", AreaSaveListArtLayout.Fit(font, "abcde", 10));
        Assert.AreEqual("", AreaSaveListArtLayout.Fit(font, "", 141));
    }
}
