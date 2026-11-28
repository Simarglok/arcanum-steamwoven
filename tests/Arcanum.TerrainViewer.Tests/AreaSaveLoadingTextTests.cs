using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-3503 (ledger LS5): the original's loading line: its rectangle on the 800 × 600 screen, its text and the screen's fit.</summary>
[TestClass]
public sealed class AreaSaveLoadingTextTests
{
    // Synthetic glyphs for the characters 31..255: 5 × 19, advance 6.
    private static readonly AreaArtFont Font = new(Enumerable.Repeat(new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 6), 225).ToArray());

    [TestMethod]
    public void The_line_starts_at_the_rectangle_and_is_not_centred()
    {
        var placed = AreaSaveLoadingText.Place(Font, "Loading Save Game...");
        Assert.AreEqual((340, 210, 200, 20), AreaSaveLoadingText.Rect);
        Assert.AreEqual(1, placed.Lines.Count);
        Assert.AreEqual((0, 0), (placed.Lines[0].X, placed.Lines[0].Y));
        Assert.AreEqual(20, placed.Lines[0].Glyphs.Count);
        Assert.IsTrue(placed.Fits);
    }

    [TestMethod]
    public void A_longer_text_breaks_at_the_width_and_only_the_line_the_height_takes_is_drawn()
    {
        var placed = AreaSaveLoadingText.Place(Font, "Loading the saved game of a very long translation here");
        Assert.AreEqual(1, placed.Lines.Count);
        Assert.IsFalse(placed.Fits);
        Assert.IsTrue(placed.Lines[0].Glyphs[^1].X + placed.Lines[0].Glyphs[^1].Glyph.Width <= AreaSaveLoadingText.Rect.Width);
    }

    [TestMethod]
    public void The_screen_fits_the_window_keeping_its_aspect()
    {
        Assert.AreEqual((1f, 0f, 0f), AreaSaveLoadingText.Fit(800, 600));
        Assert.AreEqual((2.4f, 320f, 0f), AreaSaveLoadingText.Fit(2560, 1440));
        Assert.AreEqual((1f, 0f, 100f), AreaSaveLoadingText.Fit(800, 800));
    }

    [TestMethod]
    public void The_message_is_number_5_of_the_main_menu_texts()
    {
        Assert.AreEqual("Loading Save Game...", AreaMainMenuTexts.Project("test").Text(AreaMainMenuTexts.LoadingSaveGame));
        Assert.AreEqual("Spielstand wird geladen", AreaMainMenuTexts.From(n => n == 5 ? "Spielstand wird geladen" : null, "test").Text(AreaMainMenuTexts.LoadingSaveGame));
    }
}
