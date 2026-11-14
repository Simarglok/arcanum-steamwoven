using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-1997: the text bubble rules against the behaviour recorded in docs/research/ledger/ui-audio.md TB1–TB22 (the table
/// of eight, life, colours, wrap, placement slots); values invented, expected results worked by hand. The measure used is 10 px a character.</summary>
[TestClass]
public sealed class TextBubblesTests
{
    private static int Measure(string text) => text.Length * 10;

    private static TextBubbleLayout Layout(string text = "hi") => TextBubbleRules.Layout(text, 20, Measure);

    [TestMethod]
    public void Colours_and_life_are_the_originals()
    {
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), TextBubbleRules.Rgb(BubbleColor.White));
        Assert.AreEqual(((byte)0, (byte)0, (byte)255), TextBubbleRules.Rgb(BubbleColor.Blue));
        Assert.AreEqual(((byte)0, (byte)255, (byte)0), TextBubbleRules.Rgb(BubbleColor.Green));
        Assert.AreEqual(((byte)255, (byte)0, (byte)0), TextBubbleRules.Rgb(BubbleColor.Red));
        Assert.AreEqual(6000L, TextBubbleRules.LifeMs(6), "the default Text Duration 6 is 6 s");
        Assert.AreEqual(0L, TextBubbleRules.LifeMs(0));
        Assert.AreEqual(((byte)10, (byte)10, (byte)10), TextBubbleRules.ShadowRgb);
    }

    [TestMethod]
    public void Text_wraps_at_200_pixels_and_each_line_is_centred()
    {
        var layout = Layout("aaaaaaaaaa bbbbbbbbbb cc");
        CollectionAssert.AreEqual(new[] { "aaaaaaaaaa", "bbbbbbbbbb cc" }, layout.Lines.Select(l => l.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 15, 0 }, layout.Lines.Select(l => l.Left).ToArray(), "(130 - 100) / 2 and 0");
        Assert.AreEqual((131, 41), (layout.Width, layout.Height), "the widest line and 2 lines of 20 px, each plus the shadow's pixel");
    }

    [TestMethod]
    public void Lines_beyond_the_box_height_are_cut_and_a_wide_word_stands_alone()
    {
        var word = new string('a', 25);
        var layout = Layout(string.Join(" ", Enumerable.Repeat(word, 11)));
        Assert.AreEqual(10, layout.Lines.Count, "200 px of 20 px lines is ten lines; the eleventh is cut");
        Assert.AreEqual(251, layout.Width, "a word wider than the box is not broken");
        Assert.AreEqual(0, Layout("").Lines.Count);
    }

    [TestMethod]
    public void The_eight_slots_stand_around_the_anchor()
    {
        (int, int)[] expected = [(350, 160), (440, 225), (480, 280), (440, 340), (350, 310), (260, 340), (220, 280), (260, 225)];
        for (var slot = 0; slot < 8; slot++) Assert.AreEqual(expected[slot], TextBubbleRules.SlotPosition(slot, 400, 300, 100, 40), $"slot {slot}");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextBubbleRules.SlotPosition(8, 0, 0, 1, 1));
    }

    [TestMethod]
    public void The_slot_is_the_first_of_the_regions_order_that_fits_the_view()
    {
        // The view's centre (400, 300): region 7 (x 380-419, y from 290) tries 4 first, below the anchor.
        Assert.AreEqual(4, TextBubbleRules.ChooseSlot(400, 300, 100, 40, 0, 0, 800, 600));
        // Top left corner: region 0 tries 0 (above: off the top), 7, 6, 5 (off the left), then 4.
        Assert.AreEqual(4, TextBubbleRules.ChooseSlot(100, 100, 100, 40, 0, 0, 800, 600));
        // Region 1 (x 380-419, y under 190) starts with 0 above the anchor: it fits.
        Assert.AreEqual(0, TextBubbleRules.ChooseSlot(400, 180, 100, 40, 0, 0, 800, 600));
        // A bubble wider than the view fits nowhere: slot 0, as the original's fallback.
        Assert.AreEqual(0, TextBubbleRules.ChooseSlot(400, 300, 900, 40, 0, 0, 800, 600));
        // The same point in a view twice as wide and high is in a different region (the constants stand for the centre).
        Assert.AreEqual(0, TextBubbleRules.ChooseSlot(400, 300, 100, 40, 0, 0, 1600, 1200));
    }

    [TestMethod]
    public void A_bubble_takes_its_objects_entry_else_the_highest_free_one()
    {
        var field = new TextBubbleField<string>();
        Assert.AreEqual(7, field.Create("a", "x", Layout(), BubbleColor.White, 0));
        Assert.AreEqual(6, field.Create("b", "x", Layout(), BubbleColor.White, 1));
        Assert.AreEqual(7, field.Create("a", "y", Layout(), BubbleColor.Green, 2), "the same object reuses its entry");
        Assert.AreEqual(2, field.Count);
        Assert.AreEqual(("y", BubbleColor.Green, 2L), (field.Of("a")!.Text, field.Of("a")!.Color, field.Of("a")!.CreatedMs));
        CollectionAssert.AreEqual(new[] { "b", "a" }, field.Bubbles.Select(b => b.Owner).ToArray(), "table order: slot 6 before 7");
    }

    [TestMethod]
    public void A_full_table_evicts_the_oldest_non_persistent_bubble_else_the_first_entry()
    {
        var field = new TextBubbleField<string>();
        foreach (var (owner, time) in "abcdefgh".Select((c, i) => (c.ToString(), (long)i))) field.Create(owner, "x", Layout(), BubbleColor.White, time);
        Assert.AreEqual(8, field.Count);
        Assert.AreEqual(7, field.Create("i", "x", Layout(), BubbleColor.White, 100), "a (made first, entry 7) goes");
        Assert.IsNull(field.Of("a"));
        field.SetLife("b", TextBubbleRules.Persistent, 100);
        field.SetLife("c", TextBubbleRules.Persistent, 100);
        Assert.AreEqual(4, field.Create("j", "x", Layout(), BubbleColor.White, 101), "persistent bubbles are skipped: d (entry 4) is the oldest left");
        foreach (var owner in "bcefghij".Select(c => c.ToString())) field.SetLife(owner, TextBubbleRules.Persistent, 102);
        Assert.AreEqual(8, field.Count);
        Assert.AreEqual(0, field.Create("k", "x", Layout(), BubbleColor.White, 103), "all persistent: entry 0 is replaced");
        Assert.IsNull(field.Of("h"));
    }

    [TestMethod]
    public void A_bubble_lives_the_default_life_and_goes_after_it()
    {
        var field = new TextBubbleField<string>(6);
        field.Create("a", "x", Layout(), BubbleColor.White, 1000);
        Assert.AreEqual(6000L, field.Of("a")!.LifeMs);
        Assert.IsFalse(field.Tick(7000), "an age of 6000 ms is not above the life");
        Assert.IsTrue(field.Tick(7001));
        Assert.AreEqual(0, field.Count);
    }

    [TestMethod]
    public void Life_zero_lasts_until_the_next_later_ping()
    {
        var field = new TextBubbleField<string>(0);
        field.Create("a", "x", Layout(), BubbleColor.White, 1000);
        Assert.IsFalse(field.Tick(1000));
        Assert.IsTrue(field.Tick(1001));
    }

    [TestMethod]
    public void Set_life_makes_persistent_restarts_or_sets_seconds()
    {
        var field = new TextBubbleField<string>(6);
        field.Create("a", "x", Layout(), BubbleColor.White, 1000);
        field.SetLife("a", TextBubbleRules.Persistent, 5000);
        Assert.IsTrue(field.Of("a")!.Persistent);
        Assert.AreEqual((1000L, 6000L), (field.Of("a")!.CreatedMs, field.Of("a")!.LifeMs), "-2 changes nothing but the flag");
        Assert.IsFalse(field.Tick(1_000_000_000), "a persistent bubble does not age");
        field.SetLife("a", TextBubbleRules.DefaultLife, 9000);
        Assert.AreEqual((false, 9000L, 6000L), (field.Of("a")!.Persistent, field.Of("a")!.CreatedMs, field.Of("a")!.LifeMs), "-1: the default life from now");
        field.SetLife("a", 3, 10_000);
        Assert.AreEqual((10_000L, 3000L), (field.Of("a")!.CreatedMs, field.Of("a")!.LifeMs));
        field.SetLife("nobody", 3, 0);
        Assert.AreEqual(1, field.Count, "an object without a bubble is ignored");
    }

    [TestMethod]
    public void Changing_Text_Duration_sets_the_live_bubbles_life_at_once_without_restarting_them()
    {
        var field = new TextBubbleField<string>(6);
        field.Create("a", "x", Layout(), BubbleColor.White, 1000);
        field.Create("p", "x", Layout(), BubbleColor.White, 1000);
        field.SetLife("p", TextBubbleRules.Persistent, 1000);
        field.SetTextDuration(2);
        Assert.AreEqual(2, field.TextDuration);
        Assert.AreEqual((2000L, 1000L), (field.Of("a")!.LifeMs, field.Of("a")!.CreatedMs), "the bubble's own start stays; the exe's 1,000,000 factor is a unit slip, fixed");
        Assert.AreEqual(6000L, field.Of("p")!.LifeMs, "a persistent bubble keeps its life");
        field.Create("b", "x", Layout(), BubbleColor.White, 1500);
        Assert.AreEqual(2000L, field.Of("b")!.LifeMs, "a new bubble lives the new setting");
        Assert.IsTrue(field.Tick(3001));
        Assert.IsNull(field.Of("a"));
        Assert.IsNotNull(field.Of("b"));
    }

    [TestMethod]
    public void Remove_and_clear_empty_the_table()
    {
        var field = new TextBubbleField<string>();
        field.Create("a", "x", Layout(), BubbleColor.White, 0);
        field.Create("b", "x", Layout(), BubbleColor.White, 0);
        Assert.IsTrue(field.Remove("a"));
        Assert.IsFalse(field.Remove("a"));
        field.Clear();
        Assert.AreEqual(0, field.Count);
    }
}
