using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-168 (B34, B35): the save and load messages of the original (ledger SL9–SL11): which numbers, which entry types, the
/// project's wording when the game's file does not read, and how the 3 s rule of the message window treats the notes.</summary>
[TestClass]
public sealed class AreaSaveMessagesTests
{
    private static readonly AreaMainMenuTexts Project = AreaMainMenuTexts.Project("test");

    [TestMethod]
    public void The_numbers_are_the_originals()
    {
        CollectionAssert.AreEqual(new[] { 5000, 5001, 5002, 5003, 5060, 5061, 5062, 5063 },
            new[] { AreaSaveMessages.LoadDone, AreaSaveMessages.LoadFailed, AreaSaveMessages.SaveDone, AreaSaveMessages.SaveFailed,
                AreaSaveMessages.Saving, AreaSaveMessages.Loading, AreaSaveMessages.SaveCompleted, AreaSaveMessages.LoadCompleted });
        CollectionAssert.AreEquivalent(new[] { 5000, 5001, 5002, 5003, 5060, 5061, 5062, 5063 }, AreaSaveMessages.ProjectText.Keys.ToArray(), "each has the project's wording");
    }

    [TestMethod]
    public void Results_are_stored_messages_and_notes_are_hover_entries()
    {
        foreach (var message in new[] { AreaSaveMessages.LoadDone, AreaSaveMessages.LoadFailed, AreaSaveMessages.SaveDone, AreaSaveMessages.SaveFailed })
        {
            var entry = AreaSaveMessages.Result(Project, message);
            Assert.IsTrue(entry.Stored && entry.Kind == AreaMessageKind.Message, message.ToString());
        }
        foreach (var message in new[] { AreaSaveMessages.Saving, AreaSaveMessages.Loading, AreaSaveMessages.SaveCompleted, AreaSaveMessages.LoadCompleted })
        {
            var entry = AreaSaveMessages.Note(Project, message);
            Assert.IsTrue(!entry.Stored && entry.Kind == AreaMessageKind.Hover, message.ToString());
        }
    }

    [TestMethod]
    public void The_game_text_wins_over_the_project_wording()
    {
        var game = AreaMainMenuTexts.From(number => number == AreaSaveMessages.LoadDone ? "из игры" : null, "test");
        Assert.AreEqual("из игры", AreaSaveMessages.LoadResult(game, true).Title);
        Assert.AreEqual(AreaSaveMessages.ProjectText[AreaSaveMessages.LoadFailed], AreaSaveMessages.LoadResult(game, false).Title, "a number the file lacks takes the project's");
    }

    [TestMethod]
    public void The_results_follow_the_outcome()
    {
        Assert.AreEqual(AreaSaveMessages.ProjectText[AreaSaveMessages.LoadDone], AreaSaveMessages.LoadResult(Project, true).Title);
        Assert.AreEqual(AreaSaveMessages.ProjectText[AreaSaveMessages.LoadFailed], AreaSaveMessages.LoadResult(Project, false).Title);
        Assert.AreEqual(AreaSaveMessages.ProjectText[AreaSaveMessages.SaveDone], AreaSaveMessages.SaveWindowResult(Project, true).Title);
        Assert.AreEqual(AreaSaveMessages.ProjectText[AreaSaveMessages.SaveFailed], AreaSaveMessages.SaveWindowResult(Project, false).Title);
    }

    [TestMethod]
    public void F7_ends_with_the_completion_note_and_a_failed_save_does_not_say_it_completed()
    {
        var done = AreaSaveMessages.QuickSaveAfter(Project, saved: true);
        Assert.AreEqual((AreaMessageKind.Hover, AreaSaveMessages.ProjectText[AreaSaveMessages.SaveCompleted]), (done.Kind, done.Title));
        // The original posts the completion note whatever the save did; a technical display bug, fixed in both rule modes.
        var failed = AreaSaveMessages.QuickSaveAfter(Project, saved: false);
        Assert.AreEqual((AreaMessageKind.Message, AreaSaveMessages.ProjectText[AreaSaveMessages.SaveFailed]), (failed.Kind, failed.Title));
    }

    [TestMethod]
    public void The_menu_texts_read_the_save_messages_from_the_same_file()
    {
        foreach (var (number, text) in AreaSaveMessages.ProjectText) Assert.AreEqual(text, Project.Text(number));
        Assert.AreEqual("Save Game", Project.Text(30), "the menu's own project texts are still there");
    }

    [TestMethod]
    public void A_note_after_a_result_is_held_back_by_the_three_second_rule_and_a_lone_note_is_shown()
    {
        // F7: nothing stored lately, so the "Saving" note is drawn; it is never stored.
        var log = new AreaMessageLog();
        var saving = log.Post(AreaSaveMessages.Note(Project, AreaSaveMessages.Saving), 10_000);
        Assert.IsTrue(saving.Shown && !saving.Stored, "a lone note is shown and not stored");
        Assert.AreEqual(0, log.Count);
        // F8: the result is stored, with the message sound, and the completion note right after it is not drawn (ML2: 3 s).
        var result = log.Post(AreaSaveMessages.LoadResult(Project, loaded: true), 10_100);
        Assert.AreEqual(new AreaMessagePost(true, true, 3004), result);
        var completed = log.Post(AreaSaveMessages.Note(Project, AreaSaveMessages.LoadCompleted), 10_150);
        Assert.IsFalse(completed.Shown || completed.Stored, "the note after a stored result is held back");
        Assert.AreEqual(AreaSaveMessages.ProjectText[AreaSaveMessages.LoadDone], log.Shown?.Title, "the result stays in the window");
        Assert.AreEqual(1, log.Count);
    }
}
