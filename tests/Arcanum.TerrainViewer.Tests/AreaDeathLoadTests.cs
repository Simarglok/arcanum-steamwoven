using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-120: the load of a save beyond the shown window (D1), the save of this hero the death offers and the original's
/// dialog box layout (ledger DB1–DB4).</summary>
[TestClass]
public sealed class AreaDeathLoadTests
{
    [TestMethod]
    public void A_cell_counts_as_shown_only_in_a_core_sector_of_the_window()
    {
        AreaSectorCoordinate[] window = [new(1451, 1290), new(1452, 1290), new(1452, 1291)];
        Assert.IsTrue(AreaQuickSave.InShownSectors(window, new CellCoord(1452 * 64, 1290 * 64)));
        Assert.IsTrue(AreaQuickSave.InShownSectors(window, new CellCoord(1452 * 64 + 63, 1291 * 64 + 63)));
        // The bug report's save: (92939, 82553) lies in sector (1452, 1289), outside the window y 1290..1294.
        Assert.IsFalse(AreaQuickSave.InShownSectors(window, new CellCoord(92939, 82553)));
        Assert.AreEqual((1452, 1289), (92939 >> 6, 82553 >> 6));
        Assert.IsFalse(AreaQuickSave.InShownSectors(window, new CellCoord(-1, 5)), "a negative cell is in no sector");
        Assert.IsFalse(AreaQuickSave.InShownSectors([], new CellCoord(0, 0)));
    }

    private static readonly DateTime Noon = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static GameSaveEntry Entry(string name, int minutes, string hero, int portrait = 0, GameSaveDetails details = GameSaveDetails.Present) =>
        new(name, name == GameSaveLibrary.AutoName, 100, Noon.AddMinutes(minutes), details,
            details is GameSaveDetails.Present or GameSaveDetails.Stale ? Summary(hero, portrait) : null, false);

    private static GameSaveSummary Summary(string hero, int portrait) => new("d", hero, 1, "Arcanum", 1, new(1885, 3, 14, 9, 0), portrait);

    [TestMethod]
    public void The_death_offers_the_newest_save_of_this_hero_only()
    {
        var muck = Summary("Muck", 12);
        GameSaveEntry[] saves =
        [
            Entry("slot0001", 1, "Muck", 12),
            Entry("slot0002", 5, "Muck", 12),
            Entry("slot0003", 9, "Virgil", 12),
            Entry(GameSaveLibrary.AutoName, 7, "Muck", 12),
            Entry("slot0004", 20, "Muck", 30),
            Entry("slot0005", 30, "Muck", 12, GameSaveDetails.Stale),
            Entry("slot0006", 40, "Muck", 12, GameSaveDetails.Missing),
            Entry("slot0007", 50, "muck", 12),
        ];
        Assert.AreEqual(GameSaveLibrary.AutoName, AreaSaveList.HeroSave(saves, muck)?.Name,
            "the newest by file date of the same name and portrait: another hero, another portrait, stale or missing details and another case are not his");
        Assert.IsNull(AreaSaveList.HeroSave(saves, Summary("Sogg", 3)), "no save of this hero");
        Assert.IsNull(AreaSaveList.HeroSave([], muck));
        // A details file of schema 1 records no portrait (0): the name decides; so does a hero without a portrait.
        Assert.AreEqual("slot0008", AreaSaveList.HeroSave([Entry("slot0001", 1, "Muck", 12), Entry("slot0008", 2, "Muck")], muck)?.Name);
        Assert.AreEqual("slot0004", AreaSaveList.HeroSave(saves.Take(5), Summary("Muck", 0))?.Name);
        // Equal dates: the higher name, as the newest-save key orders.
        Assert.AreEqual("slot0010", AreaSaveList.HeroSave([Entry("slot0009", 3, "Muck", 12), Entry("slot0010", 3, "Muck", 12)], muck)?.Name);
    }

    private static AreaArtFont Font() =>
        new([.. Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, 14, new byte[5 * 14 * 4], 4))]);

    [TestMethod]
    public void The_dialog_box_has_the_originals_places_and_lines()
    {
        Assert.AreEqual((237, 232), AreaDialogBoxLayout.QuitPoint);
        Assert.AreEqual((800 - AreaDialogBoxLayout.Width) / 2, AreaDialogBoxLayout.QuitPoint.X, "centred on the 800 × 600 screen");
        Assert.AreEqual((600 - AreaDialogBoxLayout.Height) / 2, AreaDialogBoxLayout.QuitPoint.Y);
        CollectionAssert.AreEqual(new[] { (823, true, 73, 102), (824, false, 225, 102) }, AreaDialogBoxLayout.Buttons(AreaDialogBoxKind.YesNo).ToArray());
        CollectionAssert.AreEqual(new[] { (823, true, 149, 102) }, AreaDialogBoxLayout.Buttons(AreaDialogBoxKind.Accept).ToArray());
        CollectionAssert.AreEqual(new[] { (824, false, 149, 102) }, AreaDialogBoxLayout.Buttons(AreaDialogBoxKind.Cancel).ToArray());
        CollectionAssert.AreEqual(new[] { 822, 823, 824, 229 }, AreaDialogBoxLayout.ArtNumbers.ToArray());
        var font = Font();
        // A line feed starts a line; a long text breaks at the 265-wide rectangle; 65 rows hold four 14-row lines.
        var lines = AreaDialogBoxLayout.Lines(font, "You died.\nLoad?", 14);
        Assert.AreEqual(2, lines.Count);
        var words = string.Join(' ', Enumerable.Repeat("word", 80));
        var wrapped = AreaPremadeBrowserArtLayout.Wrap(font, words, AreaDialogBoxLayout.Text.Width);
        Assert.IsTrue(wrapped.Count > 4);
        Assert.AreEqual(4, AreaDialogBoxLayout.Lines(font, words, 14).Count, "a line that does not fit is not drawn, nor any after it");
        Assert.IsTrue(AreaDialogBoxLayout.Lines(font, words, 14).All(line => line[^1].X + line[^1].Glyph.Advance <= AreaDialogBoxLayout.Text.Width + 4));
        Assert.IsFalse(AreaDialogBoxContent.Empty("none").ArtReady);
        Assert.AreEqual(0, AreaDialogBoxContent.Empty("none").LineHeight);
    }
}
