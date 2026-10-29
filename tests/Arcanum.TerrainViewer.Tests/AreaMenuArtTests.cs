using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-058 menu art (ledger MW1–MW6): the button windows' backgrounds, the labels' places and the art font's measure, on
/// synthetic glyph frames.</summary>
[TestClass]
public sealed class AreaMenuArtTests
{
    /// <summary>A font whose frame f is (f + 2) wide with an advance one less than its width, 33 high (frame 0 is character 31).</summary>
    private static AreaArtFont Font(int frames = 225) =>
        new([.. Enumerable.Range(0, frames).Select(frame => new AreaInterfaceFrame(frame + 2, 33, new byte[(frame + 2) * 33 * 4], frame + 1))]);

    [TestMethod]
    public void The_button_windows_have_the_originals_backgrounds()
    {
        Assert.AreEqual(329, AreaMenuArtLayout.Background(AreaMenuWindow.Main), "MW1: window 2 MainMenuBack");
        Assert.AreEqual(331, AreaMenuArtLayout.Background(AreaMenuWindow.SinglePlayer), "window 5 SinglePlayerBack");
        Assert.AreEqual(329, AreaMenuArtLayout.Background(AreaMenuWindow.NewGame));
        Assert.AreEqual(329, AreaMenuArtLayout.Background(AreaMenuWindow.InPlay));
        Assert.AreEqual(329, AreaMenuArtLayout.Background(AreaMenuWindow.InPlayLimited));
        Assert.IsNull(AreaMenuArtLayout.Background(AreaMenuWindow.Options), "the options window is not a button window of this look");
        Assert.IsNull(AreaMenuArtLayout.Background(AreaMenuWindow.PickCharacter));
        CollectionAssert.AreEqual(new[] { 329, 331, 327 }, AreaMenuArtLayout.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void A_label_stands_centred_on_its_slot()
    {
        Assert.AreEqual((410 - 40, 143, 81, 33), AreaMenuArtLayout.Button(0, (81, 33)), "MW2: x − width / 2, rounded toward zero");
        Assert.AreEqual((410 - 50, 143 + 100, 100, 33), AreaMenuArtLayout.Button(2, (100, 33)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaMenuArtLayout.Button(-1, (1, 1)));
        var buttons = new[] { AreaMenuArtLayout.Button(0, (100, 33)), AreaMenuArtLayout.Button(1, (60, 33)) };
        Assert.AreEqual(0, AreaMenuArtLayout.HitTest(buttons, 360, 143));
        Assert.AreEqual(-1, AreaMenuArtLayout.HitTest(buttons, 460, 150), "the right edge is outside");
        Assert.AreEqual(1, AreaMenuArtLayout.HitTest(buttons, 410, 200));
        Assert.AreEqual(-1, AreaMenuArtLayout.HitTest(buttons, 410, 180), "between the labels");
    }

    [TestMethod]
    public void The_three_passes_are_shadow_highlight_and_face()
    {
        var passes = AreaMenuArtLayout.Passes;
        Assert.AreEqual((-1, -1, ((byte)0, (byte)0, (byte)0)), passes[0], "MW5: black first at (−1, −1)");
        Assert.AreEqual((1, 1, ((byte)97, (byte)61, (byte)42)), passes[1], "brown at (+1, +1)");
        Assert.AreEqual((0, 0, ((byte)100, (byte)0, (byte)0)), passes[2], "the dark red face last");
        Assert.AreEqual(((byte)240, (byte)15, (byte)15), AreaMenuArtLayout.HoverFace, "MW6");
    }

    [TestMethod]
    public void A_text_measures_its_advances_and_the_last_glyphs_width()
    {
        var font = Font();
        // 'A' is 65: frame 34, width 36, advance 35; 'B' frame 35, width 37, advance 36.
        Assert.AreEqual(new AreaArtGlyph(34, 36, 33, 35), font.Glyph('A'), "MW4: frame = character − 31");
        Assert.AreEqual((35 + 37, 33), font.Measure("AB"));
        Assert.AreEqual((36, 33), font.Measure("A"));
        Assert.AreEqual((0, 0), font.Measure(""));
        CollectionAssert.AreEqual(new[] { 0, 35 }, font.Layout("AB").Select(glyph => glyph.X).ToArray());
        Assert.IsNull(font.Glyph('Ā'), "a character above 255 has no glyph");
        Assert.IsNull(font.Glyph('\u0010'), "nor one below 31");
        Assert.AreEqual(font.Measure("AB"), font.Measure("AĀB"), "a character without a glyph is skipped");
        Assert.IsNull(Font(10).Glyph('A'), "a frame past the art's count");
    }

    [TestMethod]
    public void Menu_art_without_a_game_folder_is_not_ready()
    {
        var content = AreaMenuArtContent.Empty("self-test");
        Assert.IsFalse(content.ArtReady);
        Assert.IsNull(content.Font);
    }
}
