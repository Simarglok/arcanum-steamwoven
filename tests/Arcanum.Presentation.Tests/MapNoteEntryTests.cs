using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-2306 (ledger BM4, exe 0x564030, 0x564070, 0x564000, 0x5672A0, 0x5672D0, 0x566F00): the map note entry of the bottom strip (bar mode 6) and the shared text edit.
[TestClass]
public sealed class MapNoteEntryTests
{
    private static MapNoteEntry Opened(string text = "")
    {
        var entry = new MapNoteEntry();
        entry.Open(text);
        return entry;
    }

    [TestMethod]
    public void The_layout_is_the_modes_rows()
    {
        Assert.AreEqual((6, 200, 196, 492), (MapNoteEntry.BarMode, MapNoteEntry.BackgroundArt, MapNoteEntry.BackgroundX, MapNoteEntry.BackgroundY));
        Assert.AreEqual((262, 508, 303, 24, 50), (MapNoteEntry.TextX, MapNoteEntry.TextY, MapNoteEntry.TextWidth, MapNoteEntry.TextHeight, MapNoteEntry.MaxLength));
        CollectionAssert.AreEqual(new[] { (MapNoteAction.Done, 203, 281, 549), (MapNoteAction.Clear, 201, 396, 550), (MapNoteAction.Cancel, 202, 511, 550) },
            MapNoteEntry.Buttons.Select(b => (b.Action, b.Art, b.X, b.Y)).ToArray());
        CollectionAssert.AreEqual(new[] { (0, 146, 213, 509), (1, 148, 213, 536), (2, 145, 213, 563) }, MapNoteEntry.Markers.Select(m => (m.Type, m.Art, m.X, m.Y)).ToArray());
        CollectionAssert.AreEquivalent(new[] { 200, 203, 201, 202, 146, 148, 145 }, MapNoteEntry.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void A_closed_entry_takes_nothing_and_an_opened_one_is_bound_at_the_end_of_the_text()
    {
        var entry = new MapNoteEntry();
        Assert.IsFalse(entry.IsOpen);
        Assert.IsFalse(entry.Type('a'));
        Assert.IsFalse(entry.Press(StripEntryKey.Home));
        Assert.IsFalse(entry.Clear());
        Assert.IsFalse(entry.Cancel());
        entry.Open("Old note");
        Assert.AreEqual(("Old note", "Old note", 8), (entry.Text, entry.Saved, entry.Caret));
        Assert.IsTrue(entry.Close());
        Assert.AreEqual(("", false), (entry.Text, entry.IsOpen));
        Assert.IsFalse(entry.Close());
    }

    [TestMethod]
    public void Typing_stops_at_49_characters_as_the_50_byte_record_says()
    {
        var entry = Opened();
        foreach (var c in new string('a', 60)) entry.Type(c);
        Assert.AreEqual(49, entry.Text.Length);
        Assert.IsFalse(entry.Type('b'));
        Assert.IsTrue(entry.Type('\b'));
        Assert.AreEqual(48, entry.Text.Length);
    }

    [TestMethod]
    public void A_stored_text_longer_than_the_record_is_kept_and_takes_no_more_characters()
    {
        var entry = Opened(new string('z', 80));
        Assert.AreEqual(80, entry.Text.Length);
        Assert.IsFalse(entry.Type('x'));
        Assert.IsTrue(entry.Type('\b'));
        Assert.AreEqual(79, entry.Text.Length);
    }

    [TestMethod]
    public void Clear_empties_the_text_and_keeps_the_entry_open_and_Cancel_puts_the_opened_text_back()
    {
        var entry = Opened("Home");
        Assert.IsTrue(entry.Clear());
        Assert.AreEqual(("", 0, true), (entry.Text, entry.Caret, entry.IsOpen));
        Assert.IsFalse(entry.Clear(), "nothing left to clear");
        foreach (var c in "other") entry.Type(c);
        Assert.IsTrue(entry.Cancel());
        Assert.AreEqual(("Home", true), (entry.Text, entry.IsOpen));
        Assert.IsTrue(entry.Caret <= entry.Text.Length, "the viewer keeps the caret inside the restored text");
        Assert.IsFalse(entry.Cancel(), "already the opened text");
    }

    [TestMethod]
    public void The_marker_choice_persists_across_entries_and_starts_with_the_first()
    {
        var entry = new MapNoteEntry();
        Assert.AreEqual(0, entry.Marker);
        Assert.IsTrue(entry.Select(2));
        Assert.IsFalse(entry.Select(2));
        Assert.IsFalse(entry.Select(3));
        Assert.IsFalse(entry.Select(-1));
        entry.Open("x");
        entry.Close();
        Assert.AreEqual(2, entry.Marker);
    }

    [TestMethod]
    public void The_revision_grows_with_every_change_the_strip_shows()
    {
        var entry = new MapNoteEntry();
        var seen = entry.Revision;
        entry.Open("a"); Assert.IsTrue(entry.Revision > seen); seen = entry.Revision;
        entry.Type('b'); Assert.IsTrue(entry.Revision > seen); seen = entry.Revision;
        entry.Select(1); Assert.IsTrue(entry.Revision > seen); seen = entry.Revision;
        entry.Press(StripEntryKey.Left); Assert.IsTrue(entry.Revision > seen); seen = entry.Revision;
        entry.Close(); Assert.IsTrue(entry.Revision > seen);
    }

    [TestMethod]
    public void The_edit_keys_are_the_order_lines_which_share_the_entry()
    {
        var entry = Opened(new string('a', 45));
        Assert.IsTrue(entry.Press(StripEntryKey.Up), "45 - 40 stays inside the text");
        Assert.AreEqual(5, entry.Caret);
        Assert.IsFalse(entry.Press(StripEntryKey.Up), "5 - 40 would leave the text");
        entry.Press(StripEntryKey.Home);
        Assert.AreEqual(0, entry.Caret);
        Assert.IsTrue(entry.Press(StripEntryKey.Insert));
        Assert.IsTrue(entry.Type('X'));
        Assert.AreEqual(("Xaaaa", 1), (entry.Text[..5], entry.Caret), "overwrite replaces the character at the caret");
        Assert.IsTrue(entry.Press(StripEntryKey.Delete));
        Assert.AreEqual(44, entry.Text.Length);
        Assert.IsFalse(entry.Type('\u0001'), "controls do nothing");
        Assert.IsFalse(entry.Type('Ā'), "a character above one byte is refused");
    }
}
