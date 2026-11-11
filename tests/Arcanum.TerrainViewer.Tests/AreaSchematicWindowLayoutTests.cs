using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-142 the schematic window's original look (ledger SW9–SW14): the layout tables as the executable's, the picture fit, the art list; no
/// original data.</summary>
[TestClass]
public sealed class AreaSchematicWindowLayoutTests
{
    [TestMethod]
    public void The_window_art_list_is_complete_and_has_no_repeat()
    {
        var arts = AreaSchematicWindowLayout.ArtNumbers;
        Assert.AreEqual(arts.Count, arts.Distinct().Count(), "no art twice");
        foreach (var number in new[] { 365, 372, 373, 357, 6, 358, 359, 360, 830, 831, 231, 371, 229, 370, 300, 355, 356, 361, 362, 363, 364, 368, 369 })
            CollectionAssert.Contains(arts.ToArray(), number);
        Assert.AreEqual(23, arts.Count);
    }

    [TestMethod]
    public void The_buttons_stand_where_the_tables_put_them()
    {
        // The table 0x5CA850.. holds screen positions; the creator subtracts the window's origin (0, 41).
        Assert.AreEqual((204, 57 - 41), AreaSchematicWindowLayout.Previous);
        Assert.AreEqual((687, 57 - 41), AreaSchematicWindowLayout.Next);
        Assert.AreEqual((76, 365 - 41), AreaSchematicWindowLayout.Assemble);
        Assert.AreEqual((29, 201 - 41), AreaSchematicWindowLayout.ListTab(found: false));
        Assert.AreEqual((29, 289 - 41), AreaSchematicWindowLayout.ListTab(found: true));
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).Select(d => (707, 58 - 41 + 45 * d)).ToArray(),
            Enumerable.Range(0, 8).Select(AreaSchematicWindowLayout.DisciplineTab).ToArray());
        CollectionAssert.AreEqual(new[] { 355, 356, 361, 362, 363, 364, 368, 369 }, Enumerable.Range(0, 8).Select(AreaSchematicWindowLayout.DisciplineArt).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSchematicWindowLayout.DisciplineTab(8));
    }

    [TestMethod]
    public void The_page_draw_stands_where_0x56DDC0_puts_it()
    {
        Assert.AreEqual((240, 146), AreaSchematicWindowLayout.RecordArtAt);
        Assert.AreEqual((261, 30), AreaSchematicWindowLayout.NameAt);
        Assert.AreEqual((219, 71, 466, 75), AreaSchematicWindowLayout.Description);
        Assert.AreEqual((222, 32), AreaSchematicWindowLayout.GlyphAt);
        Assert.AreEqual((43, 338), AreaSchematicWindowLayout.AssembleFaceAt);
        Assert.AreEqual(360, AreaSchematicWindowLayout.FaceArt(AssemblyButton.CanAssemble));
        Assert.AreEqual(359, AreaSchematicWindowLayout.FaceArt(AssemblyButton.ItemMissing));
        Assert.AreEqual(358, AreaSchematicWindowLayout.FaceArt(AssemblyButton.ExpertiseLacking));
        Assert.AreEqual((579, 181, 99, 67), AreaSchematicWindowLayout.Slot(0));
        Assert.AreEqual((579, 277, 99, 67), AreaSchematicWindowLayout.Slot(1));
        Assert.AreEqual((583, 182), AreaSchematicWindowLayout.SlotGlyphAt(0));
        Assert.AreEqual((583, 278), AreaSchematicWindowLayout.SlotGlyphAt(1));
        Assert.AreEqual((658, 232), AreaSchematicWindowLayout.SlotNumberAt(0));
        Assert.AreEqual((658, 328), AreaSchematicWindowLayout.SlotNumberAt(1));
        Assert.AreEqual((50, 26, 89, 89), AreaSchematicWindowLayout.RoundView);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSchematicWindowLayout.Slot(2));
    }

    [TestMethod]
    public void A_picture_fits_its_slot_as_0x56E190_fits_it()
    {
        var slot = AreaSchematicWindowLayout.Slot(0);
        // Smaller than the slot: its own size, centred.
        Assert.AreEqual((579 + 34, 181 + 17, 31, 32), AreaSchematicWindowLayout.FitPicture(slot, 31, 32));
        // Wider than tall and wider than the slot: the width ratio shrinks it.
        Assert.AreEqual((579, 181 + 8, 99, 50), AreaSchematicWindowLayout.FitPicture(slot, 198, 100));
        // Taller: the height ratio shrinks it.
        Assert.AreEqual((579 + (99 - 33) / 2, 181, 33, 67), AreaSchematicWindowLayout.FitPicture(slot, 66, 134));
        // The slot's own size stays.
        Assert.AreEqual((579, 181, 99, 67), AreaSchematicWindowLayout.FitPicture(slot, 99, 67));
        // An empty picture draws nothing.
        Assert.AreEqual(0, AreaSchematicWindowLayout.FitPicture(slot, 0, 10).Width);
    }

    [TestMethod]
    public void A_point_is_inside_a_rectangle_on_the_left_and_top_edge_only()
    {
        var rect = AreaSchematicWindowLayout.RoundView;
        Assert.IsTrue(AreaSchematicWindowLayout.Inside(rect, 50, 26));
        Assert.IsTrue(AreaSchematicWindowLayout.Inside(rect, 138.9, 114.9));
        Assert.IsFalse(AreaSchematicWindowLayout.Inside(rect, 139, 60));
        Assert.IsFalse(AreaSchematicWindowLayout.Inside(rect, 60, 115));
        Assert.IsFalse(AreaSchematicWindowLayout.Inside(rect, 49.9, 60));
    }

    [TestMethod]
    public void The_art_content_needs_every_window_art_and_not_the_record_arts()
    {
        Assert.IsFalse(AreaSchematicArtContent.Empty("none").Ready);
        var frames = new[] { new AreaInterfaceFrame(1, 1, new byte[4]) };
        var ready = AreaSchematicWindowLayout.ArtNumbers.ToDictionary(number => number,
            number => new AreaInterfaceArtLoad(number, AreaInterfaceArtStatus.Ready, null, frames, ""));
        Assert.IsTrue(new AreaSchematicArtContent(ready, "all").Ready);
        // A record's art that did not read leaves only its picture out.
        var withBadRecord = new Dictionary<int, AreaInterfaceArtLoad>(ready) { [900] = new(900, AreaInterfaceArtStatus.MissingFile, null, [], "missing") };
        Assert.IsTrue(new AreaSchematicArtContent(withBadRecord, "bad record art").Ready);
        // A window art that did not read keeps the diagnostic layout.
        var withoutBackground = new Dictionary<int, AreaInterfaceArtLoad>(ready) { [365] = new(365, AreaInterfaceArtStatus.MissingFile, null, [], "missing") };
        Assert.IsFalse(new AreaSchematicArtContent(withoutBackground, "no background").Ready);
    }
}
