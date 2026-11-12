using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class MousePressTraceTests
{
    private static MousePressTrace.Press P(MousePressTrace.Stage stage, string button, long us, long frame = 10, double x = 100, double y = 50, long id = 1)
        => new(stage, button, us, frame, 0, x, y, id, false, 1);

    [TestMethod]
    public void The_first_press_has_no_gap_and_a_later_one_far_away_is_no_burst()
    {
        var trace = new MousePressTrace();
        var first = trace.Note(P(MousePressTrace.Stage.Input, "Left", 1_000_000));
        StringAssert.Contains(first, "gap=first");
        Assert.IsFalse(first.Contains("BURST"));
        var second = trace.Note(P(MousePressTrace.Stage.Input, "Left", 1_400_000, id: 2));
        StringAssert.Contains(second, "gap=400.000ms");
        Assert.IsFalse(second.Contains("BURST"));
    }

    [TestMethod]
    public void Presses_within_a_few_milliseconds_count_as_one_growing_burst_with_distinct_event_ids()
    {
        var trace = new MousePressTrace();
        trace.Note(P(MousePressTrace.Stage.Input, "Left", 5_000_000, id: 11));
        var two = trace.Note(P(MousePressTrace.Stage.Input, "Left", 5_014_000, id: 12));
        var three = trace.Note(P(MousePressTrace.Stage.Input, "Left", 5_017_000, id: 13));
        StringAssert.Contains(two, "BURST=2");
        StringAssert.Contains(two, "same-event=False");
        StringAssert.Contains(three, "BURST=3");
        StringAssert.Contains(three, "gap=3.000ms");
        StringAssert.Contains(three, "same-pos=True");
        var later = trace.Note(P(MousePressTrace.Stage.Input, "Left", 5_600_000, id: 14));
        Assert.IsFalse(later.Contains("BURST"), "a press 600 ms later starts a new burst of one");
    }

    [TestMethod]
    public void One_event_seen_twice_is_marked_as_the_same_event_and_stages_and_buttons_are_kept_apart()
    {
        var trace = new MousePressTrace();
        trace.Note(P(MousePressTrace.Stage.Unhandled, "Left", 100, id: 7));
        var again = trace.Note(P(MousePressTrace.Stage.Unhandled, "Left", 900, id: 7));
        StringAssert.Contains(again, "same-event=True");
        var otherStage = trace.Note(P(MousePressTrace.Stage.Input, "Left", 950, id: 7));
        StringAssert.Contains(otherStage, "gap=first");
        var otherButton = trace.Note(P(MousePressTrace.Stage.Unhandled, "Right", 960, id: 8));
        StringAssert.Contains(otherButton, "gap=first");
    }
}
