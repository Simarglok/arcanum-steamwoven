using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-103: the notes of a town map (ledger WM79, WM91, WM127, WM128). Synthetic data only.
[TestClass]
public sealed class AreaTownNotesTests
{
    private static AreaTownMapInfo Info()
    {
        var bytes = new byte[48];
        BitConverter.GetBytes(2).CopyTo(bytes, 0);
        BitConverter.GetBytes(5120).CopyTo(bytes, 12);
        BitConverter.GetBytes(2560).CopyTo(bytes, 16);
        BitConverter.GetBytes(20).CopyTo(bytes, 20);
        BitConverter.GetBytes(20).CopyTo(bytes, 24);
        BitConverter.GetBytes(((long)96 << 32) | 96u).CopyTo(bytes, 32);
        BitConverter.GetBytes(.25f).CopyTo(bytes, 40);
        return AreaTownMapInfo.Parse(bytes)!;
    }

    [TestMethod]
    public void A_marked_scenery_adds_a_question_note_with_its_name_at_its_pixel_and_ids_count_up_from_200()
    {
        var notes = new AreaTownNotes();
        Assert.IsTrue(notes.AddForObject(Info(), 96, 96, "Inn sign"));
        Assert.IsTrue(notes.AddForObject(Info(), 100, 96, "Well"));
        Assert.AreEqual(new AreaTownNote(200, 2, 640, 320, 3, "Inn sign"), notes.Items[0]);
        Assert.AreEqual(201, notes.Items[1].Id);
        Assert.AreEqual(202, notes.NextId);
        Assert.AreEqual(3, notes.Items[1].Type);
    }

    [TestMethod]
    public void A_note_within_the_hit_box_stops_the_add_and_the_box_is_nine_pixels_each_way()
    {
        var notes = new AreaTownNotes();
        Assert.IsTrue(notes.AddForObject(Info(), 96, 96, "A"));
        // One tile east is 10 px left and 5 down: outside nine pixels on x, so a second note.
        Assert.IsTrue(notes.AddForObject(Info(), 97, 96, "B"));
        Assert.IsFalse(notes.AddForObject(Info(), 96, 96, "C"), "the same spot");
        Assert.AreEqual(2, notes.Count);
        Assert.AreEqual(0, notes.Hit(640 + 9, 320 - 9));
        Assert.AreEqual(-1, notes.Hit(640 + 10, 320));
        Assert.AreEqual(1, notes.Hit(630, 325), "the newest wins an overlap: B lies there");
    }

    [TestMethod]
    public void The_list_holds_199_notes_and_a_location_outside_the_picture_adds_none()
    {
        var notes = new AreaTownNotes();
        var info = Info();
        var added = 0;
        for (var x = 40; x < 200 && added < 220; x++)
            for (var y = 40; y < 200 && added < 220; y++)
                if (notes.AddForObject(info, x, y, "n")) added++;
        Assert.AreEqual(AreaTownNotes.Capacity, notes.Count, "the cap of 0x564160 is 199");
        Assert.AreEqual(399, notes.NextId, "ids 200..398 were used");
        Assert.IsFalse(new AreaTownNotes().AddForObject(info, 5000, 5000, "far"), "the corner glitch of (0, 0) is not reproduced");
    }

    [TestMethod]
    public void A_long_name_is_cut_to_the_250_byte_buffer_and_a_restored_list_keeps_the_next_id()
    {
        var notes = new AreaTownNotes();
        Assert.IsTrue(notes.AddForObject(Info(), 96, 96, new string('x', 400)));
        Assert.AreEqual(AreaTownNotes.MaxTextLength, notes.Items[0].Text.Length);
        Assert.IsTrue(notes.AddForObject(Info(), 97, 96, "tab\there\u0001"));
        Assert.AreEqual("tab here ", notes.Items[1].Text, "control characters would make the session refuse the list");
        var restored = AreaTownNotes.Restore([new(205, 2, 10, 10, 3, "a")], 100);
        Assert.AreEqual(206, restored.NextId, "never lower than the highest id plus one");
        Assert.AreEqual(300, AreaTownNotes.Restore([], 300).NextId);
        Assert.AreEqual(200, AreaTownNotes.Restore([], 0).NextId);
    }

    // ARC-UI-106: the note quill of the Steamwoven rules (WM92, WM126).
    [TestMethod]
    public void The_quill_adds_a_trimmed_question_note_at_a_pixel_and_an_empty_text_adds_none()
    {
        var notes = new AreaTownNotes();
        Assert.IsTrue(notes.AddByQuill(40, 50, "  Home  "));
        Assert.AreEqual(new AreaTownNote(200, 2, 40, 50, 3, "Home"), notes.Items[0]);
        Assert.IsFalse(notes.AddByQuill(60, 60, " \t "), "white space alone is no text");
        Assert.IsFalse(notes.AddByQuill(-1, 5, "x"));
        Assert.AreEqual(1, notes.Count);
        Assert.AreEqual(201, notes.NextId);
    }

    [TestMethod]
    public void The_quill_edits_a_note_in_place_and_an_empty_text_deletes_it_without_reusing_its_id()
    {
        var notes = new AreaTownNotes();
        notes.AddByQuill(10, 10, "A"); notes.AddByQuill(100, 100, "B");
        Assert.IsTrue(notes.EditByQuill(0, "A2"));
        Assert.AreEqual(new AreaTownNote(200, 2, 10, 10, 3, "A2"), notes.Items[0]);
        Assert.IsFalse(notes.EditByQuill(0, "A2"), "the same text changes nothing");
        Assert.IsFalse(notes.EditByQuill(5, "x"), "no such note");
        Assert.IsTrue(notes.EditByQuill(0, ""));
        Assert.AreEqual(1, notes.Count);
        Assert.AreEqual("B", notes.Items[0].Text);
        Assert.AreEqual(202, notes.NextId, "ids are not reused while the list lives (0x564210 keeps the counter)");
        Assert.IsTrue(notes.AddByQuill(200, 200, "C"));
        Assert.AreEqual(202, notes.Items[^1].Id);
    }

    [TestMethod]
    public void The_quill_stops_at_the_cap_and_cuts_long_and_control_text()
    {
        var notes = new AreaTownNotes();
        for (var i = 0; i < AreaTownNotes.Capacity; i++) Assert.IsTrue(notes.AddByQuill(i * 20, 0, "n"));
        Assert.IsTrue(notes.IsFull);
        Assert.IsFalse(notes.AddByQuill(5000, 5000, "one more"));
        Assert.IsTrue(notes.EditByQuill(0, "a\u0001b" + new string('y', 400)), "an edit of a full list still works");
        Assert.AreEqual(AreaTownNotes.MaxTextLength, notes.Items[0].Text.Length);
        StringAssert.StartsWith(notes.Items[0].Text, "a b", "a control character becomes a blank");
        Assert.IsTrue(notes.EditByQuill(0, ""), "a deletion makes room");
        Assert.IsFalse(notes.IsFull);
    }

    [TestMethod]
    public void A_text_of_characters_the_session_refuses_changes_nothing_and_deletes_nothing()
    {
        var notes = new AreaTownNotes();
        Assert.IsTrue(notes.AddByQuill(10, 10, "Keep"));
        Assert.IsFalse(notes.EditByQuill(0, "\U0001F600"), "an emoji alone is no edit and no deletion");
        Assert.AreEqual("Keep", notes.Items[0].Text);
        Assert.IsFalse(notes.AddByQuill(90, 90, "\u0001"));
        Assert.AreEqual(1, notes.Count);
    }
}
