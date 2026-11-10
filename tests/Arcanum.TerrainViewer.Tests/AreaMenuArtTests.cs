using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-058 menu art (ledger MW1–MW6): the button windows' backgrounds, the labels' places and the art font's measure, on
/// synthetic glyph frames. ARC-UI-059: the options window's background, page buttons and control slots (MW7, MW8). ARC-UI-061: the
/// options controls' parts (MW9, MW10).</summary>
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
        Assert.AreEqual(556, AreaMenuArtLayout.Background(AreaMenuWindow.Options), "MW1/MW7: window 6 OptionsMenuBack");
        foreach (var window in new[] { AreaMenuWindow.Main, AreaMenuWindow.SinglePlayer, AreaMenuWindow.NewGame, AreaMenuWindow.InPlay, AreaMenuWindow.InPlayLimited })
            Assert.IsTrue(AreaMenuArtLayout.UsesWidescreenPlate(window), "ARC-DEV-344: " + window + " takes the 16:9 plate");
        foreach (var window in new[] { AreaMenuWindow.Options, AreaMenuWindow.PickCharacter, AreaMenuWindow.NewCharacter, AreaMenuWindow.CharacterEditor, AreaMenuWindow.Shop })
            Assert.IsFalse(AreaMenuArtLayout.UsesWidescreenPlate(window), "ARC-DEV-344: " + window + " keeps its own art");
        Assert.IsNull(AreaMenuArtLayout.Background(AreaMenuWindow.PickCharacter));
        Assert.IsNull(AreaMenuArtLayout.Background(AreaMenuWindow.NewCharacter));
        CollectionAssert.AreEqual(new[] { 329, 331, 327, 556, 688, 671, 623, 667, 668, 27, 229 }, AreaMenuArtLayout.ArtNumbers.ToArray());
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
    public void The_options_page_buttons_stand_at_their_own_points()
    {
        Assert.AreEqual((130 - 31, 221, 63, 33), AreaMenuArtLayout.Button(AreaMenuWindow.Options, 0, (63, 33)), "MW7: x 130, y 221, centred");
        Assert.AreEqual((130 - 30, 221 + 120, 60, 33), AreaMenuArtLayout.Button(AreaMenuWindow.Options, 3, (60, 33)), "Done at y 341");
        Assert.AreEqual(AreaMenuArtLayout.Button(2, (60, 33)), AreaMenuArtLayout.Button(AreaMenuWindow.SinglePlayer, 2, (60, 33)), "the other windows keep MW2");
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaMenuArtLayout.Button(AreaMenuWindow.Options, -1, (1, 1)));
    }

    [TestMethod]
    public void The_options_controls_have_two_columns_of_four_slots()
    {
        Assert.AreEqual((261, 122), AreaMenuArtLayout.OptionsSlot(0), "MW8: the first column");
        Assert.AreEqual((261, 347), AreaMenuArtLayout.OptionsSlot(3));
        Assert.AreEqual((531, 122), AreaMenuArtLayout.OptionsSlot(4), "the second column");
        Assert.AreEqual((531, 272), AreaMenuArtLayout.OptionsSlot(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaMenuArtLayout.OptionsSlot(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaMenuArtLayout.OptionsSlot(-1));
    }

    [TestMethod]
    public void An_options_control_has_its_parts_at_the_originals_offsets()
    {
        Assert.AreEqual(688, AreaOptionsControlLayout.Background(AreaOptionKind.Slider), "MW9: 0x57FC20 OptionSlider");
        Assert.AreEqual(671, AreaOptionsControlLayout.Background(AreaOptionKind.List), "OptionMultiChoice");
        Assert.AreEqual(671, AreaOptionsControlLayout.Background(AreaOptionKind.Modules), "the module list is a list too");
        var point = AreaOptionsControlLayout.Place(5);
        Assert.AreEqual((531, 197), point, "PROJECT: the sixth shown control at slot 5");
        Assert.AreEqual((531 + (213 - 100) / 2, 199), AreaOptionsControlLayout.Label(point, 213, 100), "the label centred on the background, 2 rows down");
        Assert.AreEqual((531 + 6, 197 + 21), AreaOptionsControlLayout.Arrow(point, AreaOptionsArrow.Less));
        Assert.AreEqual((531 + 180, 197 + 21), AreaOptionsControlLayout.Arrow(point, AreaOptionsArrow.More));
        Assert.AreEqual((531 + 47, 197 + 24), AreaOptionsControlLayout.Field(point), "the slider's bar");
        Assert.AreEqual((531 + 47 + (120 - 41) / 2, 197 + 30), AreaOptionsControlLayout.Value(point, 120, 41), "a list's value, odd room halved toward zero");
        Assert.AreEqual(10 + (5 - 30) / 2, AreaOptionsControlLayout.Centred(10, 5, 30), "a text wider than its field: the signed half toward zero");
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), AreaOptionsControlLayout.LabelFace, "MW10");
        Assert.AreEqual(((byte)255, (byte)0, (byte)0), AreaOptionsControlLayout.ValueFace);
    }

    [TestMethod]
    public void A_sliders_bar_is_the_values_share_of_its_width()
    {
        Assert.AreEqual(0, AreaOptionsControlLayout.Filled(0, 10, 120), "MW9: value 0 draws no bar");
        Assert.AreEqual(60, AreaOptionsControlLayout.Filled(5, 10, 120));
        Assert.AreEqual(120, AreaOptionsControlLayout.Filled(10, 10, 120));
        Assert.AreEqual(13, AreaOptionsControlLayout.Filled(1, 9, 120), "Brightness's top 9: 120 / 9 rounded toward zero");
        Assert.AreEqual(93, AreaOptionsControlLayout.Filled(7, 9, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaOptionsControlLayout.Filled(1, 0, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaOptionsControlLayout.Filled(11, 10, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaOptionsControlLayout.Filled(-1, 10, 120));
    }

    [TestMethod]
    public void The_three_passes_are_shadow_highlight_and_face()
    {
        var passes = AreaMenuArtLayout.Passes;
        Assert.AreEqual((-1, -1, ((byte)0, (byte)0, (byte)0), AreaArtTextBlend.Coverage), passes[0], "MW5: black first at (−1, −1)");
        Assert.AreEqual((1, 1, ((byte)97, (byte)61, (byte)42), AreaArtTextBlend.Additive), passes[1], "brown at (+1, +1), added (MW31)");
        Assert.AreEqual((0, 0, ((byte)100, (byte)0, (byte)0), AreaArtTextBlend.Coverage), passes[2], "the dark red face last");
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

    /// <summary>The installed game's menu art reads (names, counts and sizes only).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_menu_art_reads()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var content = AreaMenuArtContent.Load(Path.Combine(root, "modules", "Arcanum.dat"));
        foreach (var (number, load) in content.Art.OrderBy(pair => pair.Key))
            Console.WriteLine($"{number} {load.Name} {load.Status} frames={load.Frames.Count} first={(load.Frames.Count > 0 ? $"{load.Frames[0].Width}x{load.Frames[0].Height} hot {load.Frames[0].HotX},{load.Frames[0].HotY}" : "-")}");
        Assert.IsTrue(content.ArtReady, content.Detail);
        (int, int) Size(int number) => (content.Art[number].Frames[0].Width, content.Art[number].Frames[0].Height);
        Assert.AreEqual((213, 56), Size(688), "OptionSlider");
        Assert.AreEqual((213, 56), Size(671), "OptionMultiChoice");
        Assert.AreEqual((120, 27), Size(623), "SkilGauge");
        Assert.AreEqual((29, 30), Size(667));
        Assert.AreEqual(3, content.Art[667].Frames.Count, "an arrow has idle, down and lit frames");
        Assert.AreEqual(3, content.Art[668].Frames.Count);
        Assert.AreEqual(225, content.Art[27].Frames.Count, "morph15font, characters 31..255");
        Assert.AreEqual(225, content.Art[229].Frames.Count, "Flare12Font");
        var point = AreaOptionsControlLayout.Place(7);
        Assert.IsTrue(AreaOptionsControlLayout.Arrow(point, AreaOptionsArrow.More).X + 29 <= point.X + 213, "the right arrow stands inside the background");
        Assert.IsTrue(point.X + 213 <= AreaMenuArtLayout.ScreenWidth && point.Y + 56 <= AreaMenuArtLayout.ScreenHeight, "the last slot fits the screen");
    }

    [TestMethod]
    public void Menu_art_without_a_game_folder_is_not_ready()
    {
        var content = AreaMenuArtContent.Empty("self-test");
        Assert.IsFalse(content.ArtReady);
        Assert.IsNull(content.Font);
    }
}
