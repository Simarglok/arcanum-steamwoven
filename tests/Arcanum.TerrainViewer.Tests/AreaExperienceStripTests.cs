using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1365: the experience progress strip of the bottom strip (ledger GB13, GB14; the draw 0x556C20): the table, the scaling of the
/// progress, the whole pieces, the cut last row and the edges.</summary>
[TestClass]
public sealed class AreaExperienceStripTests
{
    [TestMethod]
    public void The_table_is_the_originals()
    {
        CollectionAssert.AreEqual(
            new[] { (211, 37, 773), (249, 37, 774), (287, 37, 775), (327, 37, 776), (365, 37, 777), (403, 37, 778), (439, 37, 779), (478, 37, 780), (516, 37, 781), (555, 37, 782), (216, 47, 772) },
            AreaExperienceStripLayout.Rows.ToArray(), "0x5C71D0 and 0x5C7270");
        CollectionAssert.AreEqual(Enumerable.Range(772, 11).ToArray(), AreaExperienceStripLayout.ArtNumbers.ToArray());
        Assert.AreEqual((211, 37, 384, 14), (AreaExperienceStripLayout.ClearX, AreaExperienceStripLayout.ClearY, AreaExperienceStripLayout.ClearWidth, AreaExperienceStripLayout.ClearHeight));
        Assert.IsTrue(AreaGameBarLayout.ArtNumbers.Intersect(AreaExperienceStripLayout.ArtNumbers).Count() == 11, "the bars read the strip's arts");
        Assert.IsTrue(AreaExperienceStripLayout.Rows.All(row => row.Y >= AreaHotkeyBandBottom), "the strip lies under the hot-key bar's band");
    }

    // The hot-key bar's band ends at row 36 of the strip (440 + 37 = 477 on the screen, minus the strip's top 441).
    private const int AreaHotkeyBandBottom = 37;

    [TestMethod]
    public void Nothing_draws_without_progress_data_or_at_the_start_of_a_level()
    {
        Assert.AreEqual(0, AreaExperienceStripLayout.Pieces(null).Count);
        Assert.AreEqual(0, AreaExperienceStripLayout.Pieces(0).Count, "no whole hundred and no remainder");
        Assert.AreEqual(0, AreaExperienceStripLayout.Pieces(-5).Count, "a negative scaled value draws nothing (C truncation)");
        Assert.AreEqual(0, AreaExperienceStripLayout.Pieces(-200).Count);
    }

    [TestMethod]
    public void Progress_is_scaled_by_eleven_tenths_and_split_at_hundreds()
    {
        Assert.AreEqual(550, AreaExperienceStripLayout.Scaled(500));
        Assert.AreEqual(1100, AreaExperienceStripLayout.Scaled(1000));
        Assert.AreEqual(1098, AreaExperienceStripLayout.Scaled(999));
        var half = AreaExperienceStripLayout.Pieces(500);
        CollectionAssert.AreEqual(
            new[] { new AreaExperiencePiece(773, 211, 37, 100), new(774, 249, 37, 100), new(775, 287, 37, 100), new(776, 327, 37, 100), new(777, 365, 37, 100), new(772, 216, 47, 50) },
            half.ToArray(), "five whole pieces, then the last row at 50 percent");
        var small = AreaExperienceStripLayout.Pieces(1);
        CollectionAssert.AreEqual(new[] { new AreaExperiencePiece(772, 216, 47, 1) }, small.ToArray(), "1 permille scales to 1: only the cut last row");
        Assert.AreEqual(new AreaExperiencePiece(772, 216, 47, 99), AreaExperienceStripLayout.Pieces(909).Last(), "909 scales to 999: nine whole pieces and 99 percent");
        Assert.AreEqual(9, AreaExperienceStripLayout.Pieces(909).Count(p => p.WidthPercent == 100));
    }

    [TestMethod]
    public void The_cut_row_is_always_the_tables_last_row_never_the_next_piece()
    {
        foreach (var progress in new[] { 1, 99, 100, 250, 505, 700, 905, 999 })
        {
            var cut = AreaExperienceStripLayout.Pieces(progress).Where(p => p.WidthPercent < 100).ToArray();
            foreach (var piece in cut) Assert.AreEqual((772, 216, 47), (piece.Art, piece.X, piece.Y), $"progress {progress}");
        }
    }

    [TestMethod]
    public void A_full_progress_draws_all_eleven_rows_whole_and_no_cut()
    {
        var full = AreaExperienceStripLayout.Pieces(1000);
        Assert.AreEqual(11, full.Count);
        Assert.IsTrue(full.All(p => p.WidthPercent == 100));
        CollectionAssert.AreEqual(AreaExperienceStripLayout.Rows.Select(r => r.Art).ToArray(), full.Select(p => p.Art).ToArray());
        Assert.AreEqual(11, AreaExperienceStripLayout.Pieces(5000).Count, "beyond the table the original reads past it; the viewer stops at the rows");
    }

    [TestMethod]
    public void A_wrapped_progress_scales_as_a_32_bit_product_does()
    {
        // 0x556CA0: LEA gives progress * 11 in 32 bits, the division by 10 truncates toward zero.
        Assert.AreEqual(-99496729, AreaExperienceStripLayout.Scaled(300_000_000), "300,000,000 * 11 wraps to -994,967,296 in 32 bits; the division truncates toward zero");
        Assert.AreEqual(214748363, AreaExperienceStripLayout.Scaled(int.MaxValue));
        Assert.AreEqual(-5, AreaExperienceStripLayout.Scaled(-5));
    }

    [TestMethod]
    public void A_cut_width_truncates()
    {
        Assert.AreEqual(19, AreaExperienceStripLayout.CutWidth(38, 50));
        Assert.AreEqual(0, AreaExperienceStripLayout.CutWidth(38, 1), "a width of 0 draws nothing");
        Assert.AreEqual(37, AreaExperienceStripLayout.CutWidth(38, 99));
    }
}
