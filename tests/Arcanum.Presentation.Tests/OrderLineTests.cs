using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-2305 (ledger BM5, exe 0x566F00, 0x5718A0, 0x571910, 0x571950): the typed order line's text entry.
[TestClass]
public sealed class OrderLineTests
{
    private static OrderLine Opened(string text = "")
    {
        var line = new OrderLine();
        line.Open();
        foreach (var c in text) Assert.IsTrue(line.Type(c));
        return line;
    }

    [TestMethod]
    public void A_closed_line_takes_nothing_and_an_opened_one_starts_empty_with_the_caret_at_0()
    {
        var line = new OrderLine();
        Assert.IsFalse(line.IsOpen);
        Assert.IsFalse(line.Type('a'));
        Assert.IsFalse(line.Press(OrderLineKey.Home));
        Assert.IsNull(line.Accept());
        line.Open();
        Assert.IsTrue(line.IsOpen);
        Assert.AreEqual(("", 0, " "), (line.Text, line.Caret, line.Shown), "an empty text is drawn as one space (0x571990)");
    }

    [TestMethod]
    public void Typing_inserts_at_the_caret_and_backspace_and_delete_remove()
    {
        var line = Opened("hello");
        Assert.AreEqual(5, line.Caret);
        line.Press(OrderLineKey.Left); line.Press(OrderLineKey.Left);
        Assert.IsTrue(line.Type('X'));
        Assert.AreEqual(("helXlo", 4), (line.Text, line.Caret));
        Assert.IsTrue(line.Type('\b'));
        Assert.AreEqual(("hello", 3), (line.Text, line.Caret));
        Assert.IsTrue(line.Press(OrderLineKey.Delete));
        Assert.AreEqual(("helo", 3), (line.Text, line.Caret));
        line.Press(OrderLineKey.Home);
        Assert.IsFalse(line.Type('\b'), "nothing before the caret");
        Assert.IsTrue(line.Press(OrderLineKey.Delete));
        Assert.AreEqual("elo", line.Text);
        line.Press(OrderLineKey.Right); line.Press(OrderLineKey.Right); line.Press(OrderLineKey.Right);
        Assert.IsFalse(line.Press(OrderLineKey.Delete), "nothing at the end");
    }

    [TestMethod]
    public void The_arrows_stay_in_the_text_up_and_down_step_by_40_and_end_is_no_key()
    {
        var line = Opened(new string('a', 90));
        Assert.IsFalse(line.Press(OrderLineKey.Right));
        Assert.IsTrue(line.Press(OrderLineKey.Up));
        Assert.AreEqual(50, line.Caret);
        Assert.IsTrue(line.Press(OrderLineKey.Up));
        Assert.AreEqual(10, line.Caret);
        Assert.IsFalse(line.Press(OrderLineKey.Up), "10 - 40 would leave the text");
        Assert.IsTrue(line.Press(OrderLineKey.Down));
        Assert.AreEqual(50, line.Caret);
        Assert.IsTrue(line.Press(OrderLineKey.Down));
        Assert.AreEqual(90, line.Caret);
        Assert.IsFalse(line.Press(OrderLineKey.Down));
        Assert.IsTrue(line.Press(OrderLineKey.Home));
        Assert.AreEqual(0, line.Caret);
        Assert.IsFalse(line.Press(OrderLineKey.Left));
    }

    [TestMethod]
    public void Typing_at_the_end_stops_at_124_characters_and_125_is_reached_only_by_inserting_inside()
    {
        var line = Opened(new string('a', OrderLine.TypingLimit));
        Assert.AreEqual(124, line.Text.Length);
        Assert.IsFalse(line.Type('b'), "the caret is at 124");
        line.Press(OrderLineKey.Home);
        Assert.IsTrue(line.Type('b'), "inserted inside: 125 characters");
        Assert.AreEqual(OrderLine.MaxLength, line.Text.Length);
        Assert.IsFalse(line.Type('c'), "an insert is swallowed when the text is longer than 124");
        Assert.AreEqual(OrderLine.MaxLength, line.Text.Length);
    }

    [TestMethod]
    public void Insert_toggles_overwrite_and_overwriting_past_the_end_appends()
    {
        var line = Opened("abc");
        line.Press(OrderLineKey.Home);
        Assert.IsTrue(line.Press(OrderLineKey.Insert));
        Assert.IsTrue(line.Overwrite);
        line.Type('X'); line.Type('Y');
        Assert.AreEqual(("XYc", 2), (line.Text, line.Caret));
        line.Type('Z'); line.Type('W');
        Assert.AreEqual("XYZW", line.Text);
        line.Press(OrderLineKey.Insert);
        Assert.IsFalse(line.Overwrite);
    }

    [TestMethod]
    public void Controls_and_characters_beyond_one_byte_are_refused_and_high_bytes_are_text()
    {
        var line = Opened();
        Assert.IsFalse(line.Type('\t'));
        Assert.IsFalse(line.Type('\r'));
        Assert.IsFalse(line.Type('Ā'));
        Assert.IsTrue(line.Type('é'));
        Assert.IsTrue(line.Type(' '));
        Assert.AreEqual("é ", line.Text);
    }

    [TestMethod]
    public void Accept_hands_back_a_non_empty_text_and_always_closes()
    {
        var line = Opened("Wolf wait");
        Assert.AreEqual("Wolf wait", line.Accept());
        Assert.AreEqual((false, ""), (line.IsOpen, line.Text));
        var empty = Opened();
        Assert.IsNull(empty.Accept(), "an empty text sends nothing");
        Assert.IsFalse(empty.IsOpen, "and the line closes");
    }

    [TestMethod]
    public void Close_drops_the_text_and_reopening_starts_empty()
    {
        var line = Opened("abc");
        Assert.IsTrue(line.Close());
        Assert.IsFalse(line.Close());
        line.Open();
        Assert.AreEqual(("", 0), (line.Text, line.Caret));
    }

    [TestMethod]
    public void The_revision_grows_with_changes_only()
    {
        var line = Opened();
        var before = line.Revision;
        Assert.IsFalse(line.Press(OrderLineKey.Left));
        Assert.AreEqual(before, line.Revision);
        line.Type('a');
        Assert.AreNotEqual(before, line.Revision);
    }
}
