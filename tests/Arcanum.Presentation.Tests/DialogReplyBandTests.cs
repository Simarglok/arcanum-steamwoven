using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-2025: the dialog reply band's pointer rules against ledger DH1–DH7 (docs/research/ledger/ui-audio.md); values worked by hand:
/// row i is drawn at y = 19 i + 3 for 16 px, a press picks y / 20 (Original) or the drawn row (Steamwoven).</summary>
[TestClass]
public sealed class DialogReplyBandTests
{
    [TestMethod]
    public void Geometry_is_five_rows_of_16_px_every_19_px_in_a_100_px_band()
    {
        CollectionAssert.AreEqual(new[] { 100, 5, 16, 19, 3, 20 },
            new[] { DialogReplyBand.Height, DialogReplyBand.Slots, DialogReplyBand.RowHeight, DialogReplyBand.RowPitch, DialogReplyBand.FirstRowTop, DialogReplyBand.ClickBand });
        CollectionAssert.AreEqual(new[] { 3, 22, 41, 60, 79 }, Enumerable.Range(0, 5).Select(DialogReplyBand.RowTop).ToArray());
        Assert.IsTrue(DialogReplyBand.DrawnRowContains(0, 3) && DialogReplyBand.DrawnRowContains(0, 18) && !DialogReplyBand.DrawnRowContains(0, 19) && !DialogReplyBand.DrawnRowContains(0, 2));
    }

    [TestMethod]
    public void Original_press_picks_y_over_20_and_refuses_an_empty_slot()
    {
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 0), DialogReplyBand.Press(10, 0, 400, 3, true), "above the drawn top is still row 0");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 0), DialogReplyBand.Press(10, 19, 400, 3, true), "the gap below row 0");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 1), DialogReplyBand.Press(10, 20, 400, 3, true));
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 3), DialogReplyBand.Press(10, 79, 400, 5, true), "row 4's first drawn pixel picks row 3");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 4), DialogReplyBand.Press(10, 80, 400, 5, true));
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 4), DialogReplyBand.Press(10, 99, 400, 5, true), "past the drawn bottom, still row 4");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Empty, -1), DialogReplyBand.Press(10, 60, 400, 3, true), "slot 3 holds no reply");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Empty, -1), DialogReplyBand.Press(10, 10, 400, 0, true), "no replies at all");
    }

    [TestMethod]
    public void Steamwoven_press_needs_the_drawn_row()
    {
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 0), DialogReplyBand.Press(10, 3, 400, 5, false));
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Empty, -1), DialogReplyBand.Press(10, 2, 400, 5, false), "above row 0");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Empty, -1), DialogReplyBand.Press(10, 20, 400, 5, false), "the gap");
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Row, 4), DialogReplyBand.Press(10, 79, 400, 5, false));
        Assert.AreEqual(new DialogReplyHit(DialogReplyHitKind.Empty, -1), DialogReplyBand.Press(10, 62, 400, 3, false), "an empty slot");
    }

    [TestMethod]
    public void A_press_outside_the_band_is_outside_in_both_settings()
    {
        foreach (var drift in new[] { true, false })
        {
            Assert.AreEqual(DialogReplyHitKind.Outside, DialogReplyBand.Press(-1, 10, 400, 3, drift).Kind);
            Assert.AreEqual(DialogReplyHitKind.Outside, DialogReplyBand.Press(400, 10, 400, 3, drift).Kind, "the right edge is exclusive");
            Assert.AreEqual(DialogReplyHitKind.Outside, DialogReplyBand.Press(10, -1, 400, 3, drift).Kind);
            Assert.AreEqual(DialogReplyHitKind.Outside, DialogReplyBand.Press(10, 100, 400, 3, drift).Kind, "the bottom edge is exclusive");
            Assert.AreEqual(DialogReplyHitKind.Row, DialogReplyBand.Press(399, 5, 400, 3, drift).Kind);
        }
    }

    [TestMethod]
    public void Hover_lights_a_row_only_when_the_pointer_is_on_its_drawn_pixels_and_in_its_click_band()
    {
        var hover = new DialogReplyBand.Hover(400, 5);
        hover.Move(10, 30);
        Assert.IsTrue(hover.IsLit(1));
        Assert.AreEqual(1, hover.Remembered);
        hover.Move(10, 39);
        Assert.IsFalse(hover.IsLit(1), "the gap below row 1 is in its click band but not on its pixels");
        Assert.AreEqual(1, hover.Remembered);
        hover.Move(10, 10);
        Assert.IsTrue(hover.IsLit(0));
        Assert.IsFalse(hover.IsLit(1), "the old row was redrawn plain");
        hover.Move(10, 79);
        Assert.IsFalse(hover.IsLit(4), "y 79 is on row 4's pixels but in row 3's click band: row 4 is not drawn yet");
        Assert.IsFalse(hover.IsLit(3));
        hover.Move(10, 85);
        Assert.IsTrue(hover.IsLit(4));
        hover.Move(10, 79);
        Assert.IsTrue(hover.IsLit(4), "the old row is redrawn on the move and the pointer is on its pixels");
    }

    [TestMethod]
    public void Hover_ignores_empty_slots_and_unlights_on_leaving_the_band()
    {
        var hover = new DialogReplyBand.Hover(400, 2);
        hover.Move(10, 25);
        Assert.IsTrue(hover.IsLit(1));
        hover.Move(10, 70);
        Assert.IsFalse(hover.IsLit(1));
        Assert.AreEqual(-1, hover.Remembered, "an empty slot clears the remembered row");
        hover.Move(10, 25);
        Assert.IsTrue(hover.IsLit(1));
        hover.Move(500, 25);
        Assert.IsFalse(hover.IsLit(1));
        Assert.AreEqual(-1, hover.Remembered);
        hover.Move(10, 25);
        hover.Leave();
        Assert.IsFalse(hover.IsLit(1));
    }

    [TestMethod]
    public void Hover_keeps_the_stale_lit_row_the_original_leaves_behind()
    {
        var hover = new DialogReplyBand.Hover(400, 5);
        hover.Move(10, 85);
        hover.Move(10, 79);
        Assert.IsTrue(hover.IsLit(4), "row 4 is redrawn lit at y 79 while row 3 is the remembered one");
        Assert.AreEqual(3, hover.Remembered);
        hover.Leave();
        Assert.IsTrue(hover.IsLit(4), "only the remembered row is redrawn plain on leaving: row 4 stays lit, as in the original");
        Assert.IsFalse(hover.IsLit(3));
    }
}
