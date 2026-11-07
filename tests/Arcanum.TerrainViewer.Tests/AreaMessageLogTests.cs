using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-023 message log of the bottom strip (ledger ML1–ML8): the ring, repeats, hover texts and their quiet time, the
/// scroll areas, the panes, sounds and the layout.</summary>
[TestClass]
public sealed class AreaMessageLogTests
{
    private static AreaMessageEntry Message(string text) => AreaMessageEntry.Text(AreaMessageKind.Message, text);
    private static AreaMessageEntry Plain(string text) => AreaMessageEntry.Text(AreaMessageKind.Plain, text);
    private static AreaMessageEntry Hover(string text) => AreaMessageEntry.Text(AreaMessageKind.Hover, text);
    private static AreaMessageEntry Level(int level) => new(AreaMessageKind.LevelUp, "Level up", $"Level {level}", "Unspent points: 1");

    [TestMethod]
    public void The_ring_keeps_the_last_ten_and_shows_the_newest()
    {
        var log = new AreaMessageLog();
        Assert.IsNull(log.Shown);
        for (var i = 1; i <= 12; i++)
        {
            var post = log.Post(Level(i), i * 10_000);
            Assert.AreEqual(new AreaMessagePost(true, true, 3005), post, "a stored level-up entry is drawn with 3005");
        }
        Assert.AreEqual(AreaMessageLog.Capacity, log.Count);
        CollectionAssert.AreEqual(Enumerable.Range(3, 10).Select(i => $"Level {i}").ToArray(), log.History.Select(e => e.Line).ToArray(), "oldest first, the first two left");
        Assert.AreEqual("Level 12", log.Shown?.Line);
        log.Clear();
        Assert.AreEqual(0, log.Count);
        Assert.IsNull(log.Shown);
    }

    [TestMethod]
    public void A_repeated_text_is_not_stored_again_but_drawn_with_its_sound()
    {
        var log = new AreaMessageLog();
        Assert.AreEqual(new AreaMessagePost(true, true, 3004), log.Post(Message("Heavy"), 0));
        log.Post(Plain("Other"), 1);
        log.Older();
        Assert.AreEqual("Heavy", log.Shown?.Title);
        // A plain text equal to the newest entry's (another type) is a repeat: the newest is shown, drawn as its own type (no sound).
        Assert.AreEqual(new AreaMessagePost(true, true, null), log.Post(Message("Other"), 2));
        Assert.AreEqual(2, log.Count);
        Assert.AreEqual(AreaMessageKind.Plain, log.Shown?.Kind);
        Assert.AreEqual(new AreaMessagePost(true, true, null), log.Post(Plain("Other"), 3));
        Assert.AreEqual(2, log.Count);
        // Only the newest entry counts, and a level-up entry is never a repeat.
        log.Post(Message("Heavy"), 4);
        Assert.AreEqual(3, log.Count);
        log.Post(Level(2), 5); log.Post(Level(2), 6);
        Assert.AreEqual(5, log.Count);
        // ASCII letters compare without case (0x58ADB0); other characters as they are.
        log.Post(Message("Ärger"), 7); log.Post(Message("äRGER"), 8);
        Assert.AreEqual(7, log.Count, "Ä and ä differ");
        log.Post(Message("ärger"), 9);
        Assert.AreEqual(7, log.Count, "only A–Z fold");
    }

    [TestMethod]
    public void Texts_are_cut_to_199_characters_and_a_longer_repeat_is_stored()
    {
        var log = new AreaMessageLog();
        var text = new string('x', 250);
        log.Post(Message(text), 0);
        Assert.AreEqual(AreaMessageLog.MaxTextLength, log.Shown!.Title.Length);
        // The original compares the new text with the stored (cut) one: a text over 199 characters never repeats.
        log.Post(Message(text), 1);
        Assert.AreEqual(2, log.Count);
        log.Post(Message(text[..199]), 2);
        Assert.AreEqual(2, log.Count);
        // A level-up entry's text field is its scheme text (cut the same way); with points it is empty.
        var scheme = new AreaMessageEntry(AreaMessageKind.LevelUp, "T", "L", text, Scheme: true);
        log.Post(scheme, 3);
        Assert.AreEqual(AreaMessageLog.MaxTextLength, log.Shown!.Detail.Length);
        Assert.AreEqual("T", log.Shown.Title);
        log.Post(Message(text[..199].ToUpperInvariant()), 4);
        Assert.AreEqual(3, log.Count, "a message equal to the scheme text repeats it");
        log.Post(Level(2), 5);
        log.Post(Message("Level up"), 6);
        Assert.AreEqual(5, log.Count, "the level-up title is not the entry's text");
    }

    [TestMethod]
    public void Hover_texts_wait_for_the_quiet_time_and_are_never_stored()
    {
        var log = new AreaMessageLog();
        Assert.AreEqual(new AreaMessagePost(false, true, null), log.Post(Hover("Inventory"), 0), "nothing stored yet: shown");
        Assert.AreEqual(0, log.Count);
        log.Post(Message("Heavy"), 10_000);
        Assert.AreEqual(new AreaMessagePost(false, false, null), log.Post(Hover("Inventory"), 13_000), "3 s after the entry: not yet");
        Assert.AreEqual("Heavy", log.Shown?.Title);
        Assert.AreEqual(new AreaMessagePost(false, true, null), log.Post(Hover("Inventory"), 13_001));
        Assert.AreEqual("Inventory", log.Shown?.Title);
        Assert.AreEqual(1, log.Count);
        // Leaving the button: Steamwoven shows the entry again (the original blanks the window, ML7).
        log.ClearHover();
        Assert.AreEqual("Heavy", log.Shown?.Title);
        log.EnterPane();
        Assert.AreEqual(new AreaMessagePost(false, false, null), log.Post(Hover("Inventory"), 99_000), "never in a pane");
    }

    [TestMethod]
    public void The_scroll_areas_step_through_the_log_and_stop_at_its_ends()
    {
        var log = new AreaMessageLog();
        Assert.IsFalse(log.Older(), "an empty log draws nothing");
        Assert.IsFalse(log.Newer());
        for (var i = 1; i <= 3; i++) log.Post(Level(i), i);
        log.Older(); Assert.AreEqual("Level 2", log.Shown?.Line);
        log.Older(); Assert.AreEqual("Level 1", log.Shown?.Line);
        log.Older(); Assert.AreEqual("Level 1", log.Shown?.Line, "no further back than the log holds");
        log.Newer(); log.Newer(); log.Newer();
        Assert.AreEqual("Level 3", log.Shown?.Line, "up to the newest");
        // A full ring: back nine steps from the newest, never past the oldest.
        for (var i = 4; i <= 14; i++) log.Post(Level(i), i);
        for (var i = 0; i < 12; i++) log.Older();
        Assert.AreEqual("Level 5", log.Shown?.Line);
        // A new entry jumps to the newest.
        log.Post(Level(15), 15);
        Assert.AreEqual("Level 15", log.Viewed?.Line);
    }

    [TestMethod]
    public void A_pane_owns_the_window_until_an_entry_and_takes_it_back_on_a_move()
    {
        var log = new AreaMessageLog();
        log.Post(Message("Heavy"), 0);
        log.EnterPane();
        Assert.IsTrue(log.PaneMode);
        Assert.IsNull(log.Shown);
        Assert.IsFalse(log.Older(), "the scroll areas are the message mode's");
        // A stored entry puts the window back in the message mode (0x552960).
        log.Post(Message("Light"), 1);
        Assert.IsFalse(log.PaneMode);
        Assert.AreEqual("Light", log.Shown?.Title);
        Assert.IsFalse(log.PointerMoved(400, 500, paneButtonDown: false), "no button down: the window stays");
        Assert.IsFalse(log.PointerMoved(400, 440, paneButtonDown: true), "above the strip (the log already)");
        Assert.IsFalse(log.PointerMoved(5, 500, paneButtonDown: true), "left of x 10");
        Assert.IsFalse(log.PointerMoved(400, 591, paneButtonDown: true), "below y 590");
        Assert.IsTrue(log.PointerMoved(790, 590, paneButtonDown: true));
        Assert.IsTrue(log.PaneMode);
        // Above the strip the spells or skills pane gives the window back to the log (0x54B7C0), the weapon's spells do not.
        Assert.IsFalse(log.PointerMoved(400, 300, paneButtonDown: true, weaponPane: true));
        Assert.IsTrue(log.PaneMode);
        Assert.IsTrue(log.PointerMoved(400, 440, paneButtonDown: true));
        Assert.IsFalse(log.PaneMode);
        Assert.AreEqual("Light", log.Shown?.Title);
        Assert.IsFalse(log.PointerMoved(400, 300, paneButtonDown: true), "already the log");
        log.PointerMoved(400, 500, paneButtonDown: true);
        // The button going up: Steamwoven shows the entry again (the original draws only the background, ML7).
        log.EnterMessages();
        Assert.AreEqual("Light", log.Shown?.Title);
    }

    [TestMethod]
    public void Sounds_by_type()
    {
        Assert.AreEqual(3005, AreaMessageLog.Sound(AreaMessageKind.LevelUp));
        Assert.AreEqual(3004, AreaMessageLog.Sound(AreaMessageKind.Message));
        Assert.IsNull(AreaMessageLog.Sound(AreaMessageKind.Plain));
        Assert.IsNull(AreaMessageLog.Sound(AreaMessageKind.Hover));
        Assert.ThrowsExactly<ArgumentException>(() => AreaMessageEntry.Text(AreaMessageKind.LevelUp, "x"));
    }

    [TestMethod]
    public void The_layout_is_the_originals_table()
    {
        Assert.AreEqual((211, 503, 383, 82), AreaMessageLayout.Window, "0x5C6D60 mode 0");
        Assert.AreEqual((210, 504, 382, 41), AreaMessageLayout.OlderArea);
        Assert.AreEqual((210, 545, 382, 41), AreaMessageLayout.NewerArea);
        CollectionAssert.IsSubsetOf(new[] { 354, 438, 442, 443, 444 }, AreaMessageLayout.ArtNumbers.ToArray());
        // ARC-UI-035: the colleges' icons (spell hints) and the items' type icons (item windows) too.
        CollectionAssert.IsSubsetOf(Enumerable.Range(446, 16).ToArray(), AreaMessageLayout.ArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(Arcanum.Presentation.Hud.HotkeyBarRules.TypeIconNumbers.ToArray(), AreaMessageLayout.ArtNumbers.ToArray());
        CollectionAssert.AllItemsAreUnique(AreaMessageLayout.ArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaGameBarLayout.ArtNumbers.ToArray(), AreaGameBarContent.ArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaMessageLayout.ArtNumbers.ToArray(), AreaGameBarContent.ArtNumbers.ToArray());

        static string Of(AreaMessageDraw draw) => $"{draw.Icon?.ToString() ?? "-"} {draw.Widens}: " + string.Join(" | ", draw.Texts.Select(t => $"{t.Rect} {t.Font} {t.Truncate} '{t.Text}'"));
        Assert.AreEqual("438 False: (290, 504, 291, 19) Title False 'Level up' | (290, 528, 291, 18) Text False 'Level 3' | (290, 546, 291, 18) Text False 'Unspent points: 1'",
            Of(AreaMessageLayout.Draw(Level(3))));
        Assert.AreEqual("438 False: (290, 504, 291, 19) Title False 'T' | (290, 528, 291, 18) Text False 'L' | (290, 546, 291, 36) Text False 'S'",
            Of(AreaMessageLayout.Draw(new(AreaMessageKind.LevelUp, "T", "L", "S", Scheme: true))));
        Assert.AreEqual("442 True: (290, 504, 291, 78) Text False 'Heavy'", Of(AreaMessageLayout.Draw(Message("Heavy"))));
        Assert.AreEqual("443 True: (290, 504, 291, 19) Title False 'Magic' | (290, 528, 291, 54) Text False 'a\nb'", Of(AreaMessageLayout.Draw(Plain("Magic\na\nb"))));
        Assert.AreEqual("444 True: (290, 504, 291, 78) Text False 'Inventory'", Of(AreaMessageLayout.Draw(Hover("Inventory"))));
        Assert.AreEqual("- False: (217, 504, 364, 78) Text True 'Heavy'", Of(AreaMessageLayout.Draw(Message("Heavy"), wide: true)));
        Assert.AreEqual("- False: (217, 504, 364, 19) Title True 'Magic' | (217, 528, 364, 54) Text True 'x'", Of(AreaMessageLayout.Draw(Plain("Magic\nx"), wide: true)));
    }

    [TestMethod]
    public void Hints_are_shown_like_hover_texts_and_never_stored()
    {
        var log = new AreaMessageLog();
        log.Post(Message("Heavy"), 1_000);
        var skill = new AreaMessageEntry(AreaMessageKind.SkillHint, "Prowling", Detail: "Sneak.");
        Assert.IsFalse(skill.Stored);
        Assert.AreEqual(new AreaMessagePost(false, false, null), log.Post(skill, 4_000), "within 3 s of the last stored entry");
        Assert.AreEqual(new AreaMessagePost(false, true, null), log.Post(skill, 4_001));
        Assert.AreSame(skill, log.Shown);
        Assert.AreEqual(1, log.Count, "never stored");
        log.ClearHover();
        Assert.AreEqual("Heavy", log.Shown?.Title, "leaving shows the viewed entry again (ML7)");
        Assert.IsNull(AreaMessageLog.Sound(AreaMessageKind.SpellHint));
        Assert.ThrowsExactly<ArgumentException>(() => AreaMessageEntry.Text(AreaMessageKind.SkillHint, "x"));
        Assert.ThrowsExactly<ArgumentException>(() => log.Post(new AreaMessageEntry(AreaMessageKind.Item, "Sword"), 9_000));
    }

    [TestMethod]
    public void The_item_window_draws_at_once_and_a_pane_takes_only_the_name()
    {
        var log = new AreaMessageLog();
        log.Post(Message("Heavy"), 1_000);
        var item = new AreaMessageEntry(AreaMessageKind.Item, "Sword") { Icon = 391 };
        Assert.IsTrue(log.ShowItem(item), "no time test: 0x554F10 does not go through the post");
        Assert.AreSame(item, log.Shown);
        Assert.AreEqual(1, log.Count);
        Assert.IsFalse(log.ShowPaneName(1, "Sword"), "no pane name in the message mode");
        log.EnterPane();
        Assert.IsFalse(log.ShowItem(item));
        Assert.IsFalse(log.ShowPaneName(3, "Sword", item: true), "an item writes nothing in bar mode 3 (0x553BE0)");
        Assert.IsTrue(log.ShowPaneName(3, "Prowling"));
        Assert.IsTrue(log.ShowPaneName(8, "Sword", item: true));
        Assert.AreEqual((8, "Sword"), log.PaneName);
        log.ClearHover();
        Assert.AreEqual((8, "Sword"), log.PaneName, "leaving in a pane mode keeps the name (0x550720 acts in mode 0 only)");
        log.EnterMessages();
        Assert.IsNull(log.PaneName);
        Assert.AreEqual("Heavy", log.Shown?.Title);
        Assert.AreEqual((208, 574, 387, 18), AreaMessageLayout.PaneText(1), "0x5C6D60 + 20");
        Assert.AreEqual((208, 567, 387, 18), AreaMessageLayout.PaneText(2));
        Assert.AreEqual((355, 506, 227, 18), AreaMessageLayout.PaneText(8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaMessageLayout.PaneText(9));
    }

    [TestMethod]
    public void Hints_and_the_item_window_follow_0x5529C0_and_0x554F10()
    {
        static string Of(AreaMessageDraw draw) => $"{draw.Icon?.ToString() ?? "-"} {draw.Widens}: " + string.Join(" | ",
            draw.Texts.Select(t => $"{t.Rect} {t.Font}{(t.Right ? " right" : "")}{(t.Colour != AreaMessageColour.White ? " " + t.Colour : "")} '{t.Text}'"));
        Assert.AreEqual("444 False: (290, 504, 291, 19) Title 'Prowling' | (290, 528, 291, 54) Text 'Sneak.'",
            Of(AreaMessageLayout.Draw(new(AreaMessageKind.SkillHint, "Prowling", Detail: "Sneak."))));
        Assert.AreEqual("444 False: (290, 504, 291, 19) Title 'Repair' | (290, 528, 291, 54) Text 'Fix.' | (290, 504, 291, 19) Text right Red 'Points: 3'",
            Of(AreaMessageLayout.Draw(new(AreaMessageKind.SkillHint, "Repair", Detail: "Fix.") { Value = "Points: 3" })));
        Assert.AreEqual("447 False: (290, 504, 291, 19) Title 'Spark' | (290, 528, 291, 18) Text 'Minimum willpower: 6' | (290, 528, 291, 18) Text right 'Fatigue cost: 5' | (290, 546, 291, 36) Text 'Zap.'",
            Of(AreaMessageLayout.Draw(new(AreaMessageKind.SpellHint, "Spark", "Minimum willpower: 6", "Zap.") { Icon = 447, Value = "Fatigue cost: 5" })));
        Assert.AreEqual("391 False: (290, 504, 291, 19) Title Magick 'Sword'",
            Of(AreaMessageLayout.Draw(new(AreaMessageKind.Item, "Sword") { Icon = 391, TitleColour = AreaMessageColour.Magick })));
        Assert.AreEqual("- False: (290, 504, 291, 19) Title 'Rock'", Of(AreaMessageLayout.Draw(new(AreaMessageKind.Item, "Rock"))));
    }
}
