using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1742: the original's scroll bar widget (0x5804E0, 0x581550..0x581660, 0x580B10, 0x5807F0; ledger ALP10..ALP15) on the schemes page's
/// descriptor: a bar at (209, 60), 17 × 255, value 0..44 by 1, arrows 11 × 15 and 11 × 14, page 5, wheel 3.</summary>
[TestClass]
public sealed class AreaSheetScrollBarTests
{
    private static readonly AreaScrollArrows Arrows = new(11, 15, 11, 14);

    private static AreaSheetScrollBar Bar(int value = 0, int maximum = 44, List<int>? heard = null)
    {
        var bar = new AreaSheetScrollBar(209, 60, 17, 255, 0, maximum, 1, value, Arrows, (23, 66, 177, 240));
        if (heard is not null) bar.Changed += heard.Add;
        return bar;
    }

    [TestMethod]
    public void The_thumb_is_one_step_long_and_at_least_twelve_pixels()
    {
        var bar = Bar();
        // (255 − 14 − 15) / (44 + 1) pixels of one step: 5.02, so the thumb takes its 12-pixel minimum.
        Assert.AreEqual(226d / 45, bar.Unit, 1e-9);
        Assert.AreEqual(12, bar.ThumbLength);
        Assert.AreEqual((75, 87), (bar.ThumbTop, bar.ThumbBottom), "below the up arrow (60 + 15)");
        // A short range gives a long thumb: range 0..3 → 226 / 4 = 56 pixels.
        var short3 = Bar(maximum: 3);
        Assert.AreEqual((56, 75 + 56), (short3.ThumbLength, short3.ThumbBottom));
        Assert.AreEqual(75 + 56, Bar(1, 3).ThumbTop + 0, "one step down: 56.5 pixels, truncated to 56");
    }

    [TestMethod]
    public void The_thumb_goes_down_a_step_at_a_time_and_sits_on_the_down_arrow_at_the_end()
    {
        Assert.AreEqual(75 + 5, Bar(1).ThumbTop, "one step 5.02 truncated");
        Assert.AreEqual(75 + (int)(226d / 45 * 22), Bar(22).ThumbTop);
        Assert.AreEqual(60 + 255 - 14 - 12, Bar(44).DrawnThumbTop(), "at the largest value with no offset: against the down arrow (its own top would overflow)");
        Assert.AreEqual(75, Bar(0).DrawnThumbTop(-30), "kept below the up arrow");
        Assert.AreEqual(60 + 255 - 14 - 12, Bar(10).DrawnThumbTop(1000), "kept above the down arrow");
    }

    [TestMethod]
    public void A_value_outside_the_range_is_put_at_the_minimum()
    {
        Assert.AreEqual(0, Bar(99).Value);
        Assert.AreEqual(0, Bar(-3).Value);
        Assert.AreEqual(44, Bar(44).Value);
    }

    [TestMethod]
    public void The_arrows_move_by_a_step_and_call_back_even_at_the_ends()
    {
        var heard = new List<int>();
        var bar = Bar(1, heard: heard);
        bar.PressArrow(up: false); bar.PressArrow(up: true); bar.PressArrow(up: true); bar.PressArrow(up: true);
        CollectionAssert.AreEqual(new[] { 2, 1, 0, 0 }, heard, "a press at the minimum calls back with the unchanged value");
        var end = Bar(44, heard: heard = []);
        end.PressArrow(up: false);
        CollectionAssert.AreEqual(new[] { 44 }, heard);
    }

    [TestMethod]
    public void A_press_on_the_track_moves_a_page_and_the_wheel_three_steps()
    {
        var heard = new List<int>();
        var bar = Bar(10, heard: heard);
        bar.PressTrack(above: false); bar.PressTrack(above: true); bar.PressTrack(above: true); bar.PressTrack(above: true);
        CollectionAssert.AreEqual(new[] { 15, 10, 5, 0 }, heard);
        bar.Turn(-1); bar.Turn(-1); bar.Turn(1); bar.Turn(0);
        CollectionAssert.AreEqual(new[] { 15, 10, 5, 0, 3, 6, 3 }, heard, "a turn down shows later entries, up earlier ones; no delta, no call");
        Assert.AreEqual(44, Bar(43).Also(b => b.Turn(-1)).Value, "clamped to the maximum");
    }

    [TestMethod]
    public void The_parts_answer_in_the_exes_order()
    {
        var bar = Bar(10);
        // The up arrow centred in 17 (209 + 3), at the top; the down arrow at the bottom.
        Assert.AreEqual((212, 60), bar.UpArrow);
        Assert.AreEqual((212, 60 + 255 - 14), bar.DownArrow);
        Assert.AreEqual(AreaSheetScrollBar.Part.Up, bar.PartAt(215, 66));
        Assert.AreEqual(AreaSheetScrollBar.Part.None, bar.PartAt(210, 66), "the arrow's art is 11 wide in a 17-wide bar: the corner is the track above the thumb, which starts below the arrow");
        Assert.AreEqual(AreaSheetScrollBar.Part.Down, bar.PartAt(215, 60 + 255 - 5));
        Assert.AreEqual(AreaSheetScrollBar.Part.Thumb, bar.PartAt(209, bar.ThumbTop + 3));
        Assert.AreEqual(AreaSheetScrollBar.Part.AboveThumb, bar.PartAt(220, bar.ThumbTop - 3));
        Assert.AreEqual(AreaSheetScrollBar.Part.BelowThumb, bar.PartAt(220, bar.ThumbBottom + 3));
        Assert.AreEqual(AreaSheetScrollBar.Part.None, bar.PartAt(208, bar.ThumbTop + 3), "left of the bar");
        Assert.AreEqual(AreaSheetScrollBar.Part.None, bar.PartAt(227, bar.ThumbTop + 3), "right of the bar");
        Assert.IsTrue(bar.InWheelRectangle(23, 66) && bar.InWheelRectangle(200, 306) && !bar.InWheelRectangle(201, 100) && !bar.InWheelRectangle(100, 65));
    }

    [TestMethod]
    public void A_drag_takes_whole_steps_as_the_pointer_travels_and_commits_on_release()
    {
        var heard = new List<int>();
        var bar = Bar(10, heard: heard);
        var top = bar.ThumbTop;
        bar.BeginDrag(top + 3);
        Assert.IsTrue(bar.Dragging);
        // 2.2 steps down: the pixel offset 11.05 (more than a step, 5.02) takes two steps; the callback gets the stored value plus them.
        var offset = bar.Drag(top + 3 + 11);
        Assert.AreEqual(11, offset);
        Assert.AreEqual(12, heard[^1]);
        Assert.AreEqual(10, bar.Value, "stored only at the release");
        bar.Drag(top + 3 + 40);
        Assert.AreEqual(10 + 7, heard[^1], "40 pixels: 7 steps (the loop leaves a remainder of at most one step)");
        bar.Drag(top + 3 + 1);
        // Back near the start: the steps come back, but the exe stops once the remainder is within one step of either side, so it keeps one step (11): the
        // remainder 1 − 7 × 5.02 only has to climb to −5.02.
        Assert.AreEqual(11, heard[^1], "back near the start");
        bar.Drag(top + 3 - 11);
        Assert.AreEqual(8, heard[^1]);
        bar.EndDrag();
        Assert.AreEqual((false, 8), (bar.Dragging, bar.Value));
        Assert.AreEqual(0, bar.Drag(0), "no drag, no offset");
    }

    [TestMethod]
    public void A_drag_stops_at_the_ends()
    {
        var heard = new List<int>();
        var bar = Bar(40, heard: heard);
        bar.BeginDrag(bar.ThumbTop);
        bar.Drag(bar.ThumbTop + 500);
        Assert.AreEqual(44, heard[^1]);
        bar.EndDrag();
        Assert.AreEqual(44, bar.Value);
        bar.BeginDrag(bar.ThumbTop);
        bar.Drag(-500);
        Assert.AreEqual(0, heard[^1]);
        bar.EndDrag();
        Assert.AreEqual(0, bar.Value);
    }
}

internal static class AreaSheetScrollBarTestExtensions
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
