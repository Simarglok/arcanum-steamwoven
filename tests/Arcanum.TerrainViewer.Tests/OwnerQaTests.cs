using System.Text.Json;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-2190: the files and the model behind the in-game owner-checks overlay (synthetic folders only).</summary>
[TestClass]
public sealed class OwnerQaTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private string _folder = null!;

    [TestInitialize]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("arc-owner-qa-").FullName;

    [TestCleanup]
    public void DeleteFolder()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void WriteChecks(string commit = "abc1234")
    {
        var steps = new[]
        {
            new { id = "ARC-DEV-100#1", section = "ARC-DEV-100 - First", step = "Cast the spell.", expected = "A flash.", status = "open" },
            new { id = "ARC-DEV-100#2", section = "ARC-DEV-100 - First", step = "Walk.", expected = "No block.", status = "same" },
            new { id = "ARC-UI-200#1", section = "ARC-UI-200 - Second", step = "Open the window.", expected = "It opens.", status = "open" },
            new { id = "not-an-id", section = "x", step = "x", expected = "x", status = "open" },
        };
        File.WriteAllText(Path.Combine(_folder, OwnerQaFiles.ChecksFile), JsonSerializer.Serialize(new { version = 1, commit, steps }));
    }

    private string[] ResultLines() => File.ReadAllLines(Path.Combine(_folder, OwnerQaFiles.ResultsFile));

    [TestMethod]
    public void The_checks_snapshot_is_read_and_a_bad_id_is_skipped()
    {
        WriteChecks("def5678");
        var (steps, commit, problem) = OwnerQaFiles.ReadChecks(_folder);
        Assert.IsNull(problem);
        Assert.AreEqual("def5678", commit);
        CollectionAssert.AreEqual(new[] { "ARC-DEV-100#1", "ARC-DEV-100#2", "ARC-UI-200#1" }, steps.Select(step => step.Id).ToArray());
        Assert.AreEqual("It opens.", steps[2].Expected);
    }

    [TestMethod]
    public void A_missing_or_broken_snapshot_is_a_message_not_an_exception()
    {
        Assert.IsNotNull(OwnerQaFiles.ReadChecks(_folder).Problem);
        File.WriteAllText(Path.Combine(_folder, OwnerQaFiles.ChecksFile), "{ not json");
        var (steps, _, problem) = OwnerQaFiles.ReadChecks(_folder);
        Assert.AreEqual(0, steps.Count);
        StringAssert.Contains(problem, "cannot be read");
    }

    [TestMethod]
    public void A_result_is_one_line_in_the_page_format_with_the_build_and_the_latest_event_wins()
    {
        Assert.IsNull(OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "same", "fine", null, "abc1234567", Now));
        Assert.IsNull(OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "differs", "no flash \"here\"\nline two", "2026-11-15_10-00-00", "abc1234567", Now));
        Assert.AreEqual(2, ResultLines().Length);
        using var line = JsonDocument.Parse(ResultLines()[1]);
        var root = line.RootElement;
        Assert.AreEqual("result", root.GetProperty("kind").GetString());
        Assert.AreEqual("2026-11-15T12:00:00Z", root.GetProperty("ts").GetString());
        Assert.AreEqual("abc1234567", root.GetProperty("build").GetString());
        Assert.AreEqual("game", root.GetProperty("src").GetString());
        var results = OwnerQaFiles.ReadResults(_folder);
        Assert.AreEqual(new OwnerQaResult("differs", "no flash \"here\"\nline two", "2026-11-15_10-00-00"), results["ARC-DEV-100#1"]);
    }

    [TestMethod]
    public void A_cut_last_line_is_closed_before_the_next_and_bad_lines_are_skipped()
    {
        File.WriteAllText(Path.Combine(_folder, OwnerQaFiles.ResultsFile), "{\"kind\":\"result\",\"step\":\"ARC-DEV-100#2\",\"status\":\"same\"}\n{\"kind\":\"res");
        Assert.IsNull(OwnerQaFiles.AppendResult(_folder, "ARC-UI-200#1", "cannot-test", "no room", null, "b", Now));
        var results = OwnerQaFiles.ReadResults(_folder);
        Assert.AreEqual("same", results["ARC-DEV-100#2"].Status);
        Assert.AreEqual("cannot-test", results["ARC-UI-200#1"].Status);
        Assert.AreEqual(2, results.Count);
    }

    [TestMethod]
    public void Invalid_input_writes_nothing()
    {
        Assert.IsNotNull(OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100", "same", "", null, "b", Now));
        Assert.IsNotNull(OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "broken", "", null, "b", Now));
        Assert.IsNotNull(OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "same", new string('x', OwnerQaFiles.MaxNote + 1), null, "b", Now));
        Assert.IsFalse(File.Exists(Path.Combine(_folder, OwnerQaFiles.ResultsFile)));
    }

    [TestMethod]
    public void The_active_step_is_one_whole_line_and_null_clears_it()
    {
        Assert.IsNull(OwnerQaFiles.WriteActive(_folder, "ARC-UI-200#1"));
        var path = Path.Combine(_folder, BugReportCheck.FileName);
        Assert.AreEqual("ARC-UI-200#1", BugReportCheck.ReadActive(path, DateTime.UtcNow));
        Assert.IsNotNull(OwnerQaFiles.WriteActive(_folder, "nonsense"));
        Assert.AreEqual("ARC-UI-200#1", BugReportCheck.ReadActive(path, DateTime.UtcNow));
        Assert.IsNull(OwnerQaFiles.WriteActive(_folder, null));
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(0, Directory.GetFiles(_folder, ".active-*").Length);
    }

    [TestMethod]
    public void The_overlay_is_on_only_with_the_flag_or_the_marker_and_never_beside_an_automated_run()
    {
        var root = Directory.CreateTempSubdirectory("arc-owner-qa-root-").FullName;
        try
        {
            var qa = Path.Combine(root, "local", "qa");
            Directory.CreateDirectory(qa);
            var start = Path.Combine(root, "artifacts", "packages", "macos");
            Directory.CreateDirectory(start);
            string?[] starts = [start];
            Assert.IsNull(OwnerQaFiles.FindFolder(starts, false, null, null), "no flag, no marker: a normal build");
            Assert.IsNull(OwnerQaFiles.FindFolder(starts, false, null, "0"));
            Assert.AreEqual(qa, OwnerQaFiles.FindFolder(starts, false, null, "1"));
            File.WriteAllText(Path.Combine(qa, OwnerQaFiles.MarkerFile), "on");
            Assert.AreEqual(qa, OwnerQaFiles.FindFolder(starts, false, null, null));
            Assert.IsNull(OwnerQaFiles.FindFolder(starts, true, null, "1"), "an automated run never looks beside the game");
            Assert.AreEqual(qa, OwnerQaFiles.FindFolder(starts, true, qa, null), "an automated run takes only the folder it is given");
            Assert.IsNull(OwnerQaFiles.FindFolder(starts, true, Path.Combine(root, "missing"), null));
            Assert.IsNull(OwnerQaFiles.FindFolder([null, ""], false, null, "1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void The_current_step_is_first_and_the_filters_choose_the_rest()
    {
        WriteChecks();
        OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "same", "", null, "b", Now);
        var session = new OwnerQaSession(_folder, "b");
        Assert.AreEqual("ARC-UI-200#1", session.CurrentId, "the first open step: the first is marked, the second is `same` in the list");
        Assert.AreEqual("ARC-UI-200#1", session.Rows().Single().Id);
        session.Filter = OwnerQaFilter.All;
        CollectionAssert.AreEqual(new[] { "ARC-UI-200#1", "ARC-DEV-100#1", "ARC-DEV-100#2" }, session.Rows().Select(step => step.Id).ToArray());
        Assert.IsNull(session.SetCurrent("ARC-DEV-100#2"));
        session.Filter = OwnerQaFilter.Section;
        CollectionAssert.AreEqual(new[] { "ARC-DEV-100#2", "ARC-DEV-100#1" }, session.Rows().Select(step => step.Id).ToArray());
        Assert.AreEqual("ARC-DEV-100#2", BugReportCheck.ReadActive(Path.Combine(_folder, BugReportCheck.FileName), DateTime.UtcNow));
        Assert.IsNotNull(session.SetCurrent("ARC-NOPE-1#1"));
    }

    [TestMethod]
    public void A_mark_keeps_the_report_of_the_step_and_a_report_keeps_its_status()
    {
        WriteChecks();
        var session = new OwnerQaSession(_folder, "abc1234567");
        Assert.IsNull(session.RecordReport("2026-11-15_10-00-00", "note", Now));
        Assert.AreEqual("open", session.ResultOf("ARC-DEV-100#1")!.Status);
        Assert.IsNull(session.Mark("differs", "no flash", Now));
        var result = session.ResultOf("ARC-DEV-100#1")!;
        Assert.AreEqual(new OwnerQaResult("differs", "no flash", "2026-11-15_10-00-00"), result);
        Assert.IsNull(session.RecordReport("2026-11-15_10-05-00", "", Now));
        Assert.AreEqual(new OwnerQaResult("differs", "no flash", "2026-11-15_10-05-00"), session.ResultOf("ARC-DEV-100#1"));
        Assert.IsNotNull(session.Mark("open", "", Now));
        Assert.IsTrue(ResultLines().All(line => line.Contains("\"build\":\"abc1234567\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Next_open_wraps_and_the_session_follows_the_page()
    {
        WriteChecks();
        var session = new OwnerQaSession(_folder, "b");
        Assert.AreEqual("ARC-DEV-100#1", session.CurrentId);
        Assert.IsTrue(session.AdvanceToNextOpen(out var problem));
        Assert.IsNull(problem);
        Assert.AreEqual("ARC-UI-200#1", session.CurrentId, "ARC-DEV-100#2 is `same` in the list");
        Assert.IsTrue(session.AdvanceToNextOpen(out _));
        Assert.AreEqual("ARC-DEV-100#1", session.CurrentId, "wraps around");
        Assert.IsNull(session.Mark("same", "", Now));
        OwnerQaFiles.AppendResult(_folder, "ARC-UI-200#1", "same", "", null, "b", Now);
        session.Reload();
        Assert.IsFalse(session.AdvanceToNextOpen(out _), "nothing open is left");
        // the page opens another step: the file changes the current one
        OwnerQaFiles.WriteActive(_folder, "ARC-DEV-100#2");
        Assert.IsTrue(session.FollowActive());
        Assert.AreEqual("ARC-DEV-100#2", session.CurrentId);
        Assert.IsFalse(session.FollowActive());
    }

    [TestMethod]
    public void A_report_keeps_the_status_the_step_shows_and_a_mark_sees_the_pages_change()
    {
        WriteChecks();
        var session = new OwnerQaSession(_folder, "b");
        Assert.IsNull(session.SetCurrent("ARC-DEV-100#2"));              // `same` in the list, never marked
        Assert.IsNull(session.RecordReport("2026-11-15_10-00-00", "", Now));
        Assert.AreEqual("same", session.ResultOf("ARC-DEV-100#2")!.Status);
        // the page changes the note and the report of the step while the window is open: the game's mark keeps them
        OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#2", "same", "page note", "2026-11-15_11-00-00", "page", Now);
        Assert.IsNull(session.Mark("differs", "game note", Now));
        Assert.AreEqual(new OwnerQaResult("differs", "game note", "2026-11-15_11-00-00"), session.ResultOf("ARC-DEV-100#2"));
    }

    [TestMethod]
    public void A_read_problem_keeps_the_list_and_the_current_step_and_the_default_step_is_written_for_Ctrl_B()
    {
        WriteChecks();
        var session = new OwnerQaSession(_folder, "b");
        Assert.AreEqual("ARC-DEV-100#1", BugReportCheck.ReadActive(Path.Combine(_folder, BugReportCheck.FileName), DateTime.UtcNow));
        File.WriteAllText(Path.Combine(_folder, OwnerQaFiles.ChecksFile), "");
        session.Reload();
        Assert.IsNotNull(session.Problem);
        Assert.AreEqual(3, session.Steps.Count);
        Assert.AreEqual("ARC-DEV-100#1", session.CurrentId);
        File.Delete(Path.Combine(_folder, BugReportCheck.FileName));
        session.TouchActive();
        Assert.AreEqual("ARC-DEV-100#1", BugReportCheck.ReadActive(Path.Combine(_folder, BugReportCheck.FileName), DateTime.UtcNow));
    }

    [TestMethod]
    public void An_append_waits_for_the_pages_lock_and_gives_up_with_a_message()
    {
        using (new FileStream(Path.Combine(_folder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var problem = OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "same", "", null, "b", Now);
            StringAssert.Contains(problem, "busy");
            Assert.IsFalse(File.Exists(Path.Combine(_folder, OwnerQaFiles.ResultsFile)));
        }
        Assert.IsNull(OwnerQaFiles.AppendResult(_folder, "ARC-DEV-100#1", "same", "", null, "b", Now));
    }

    [TestMethod]
    public void A_nearer_qa_folder_that_is_not_the_owners_is_passed_over_not_taken()
    {
        // ARC-DEV-2236: the first `local/qa` going up used to win even when it had neither marker nor list.
        var root = Directory.CreateTempSubdirectory("arc-owner-qa-root-").FullName;
        try
        {
            var outer = Path.Combine(root, "local", "qa");
            var start = Path.Combine(root, "worktree", "artifacts");
            var inner = Path.Combine(root, "worktree", "local", "qa");
            Directory.CreateDirectory(outer); Directory.CreateDirectory(inner); Directory.CreateDirectory(start);
            string?[] starts = [start];
            Assert.IsNull(OwnerQaFiles.FindFolder(starts, false, null, null), "no marker anywhere: off");
            Assert.AreEqual(inner, OwnerQaFiles.FindFolder(starts, false, null, "1"), "flag on, no list anywhere: the nearest");
            File.WriteAllText(Path.Combine(outer, OwnerQaFiles.ChecksFile), "{}");
            Assert.AreEqual(outer, OwnerQaFiles.FindFolder(starts, false, null, "1"), "flag on: the one with the list beats a bare nearer one");
            File.WriteAllText(Path.Combine(outer, OwnerQaFiles.MarkerFile), "on");
            Assert.AreEqual(outer, OwnerQaFiles.FindFolder(starts, false, null, null), "the marker of the outer folder turns the overlay on, the bare inner folder is skipped");
            File.Delete(Path.Combine(outer, OwnerQaFiles.MarkerFile));
            Assert.IsNull(OwnerQaFiles.FindFolder(starts, false, null, null), "the marker removed: off again at the next read");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OwnerQaKey Key(char name, bool pressed = true, bool ctrl = false, bool shift = false, bool echo = false, bool alt = false, bool meta = false) =>
        new(name, name == 'Q', name == 'E', pressed, echo, ctrl, shift, alt, meta);   // 'E' stands for Escape

    private static readonly Func<bool> On = () => true;
    private static readonly Func<bool> Off = () => false;

    [TestMethod]
    public void Ctrl_Q_opens_and_Ctrl_Shift_Q_toggles_only_for_the_exact_chord_and_only_when_available()
    {
        var router = new OwnerQaInputRouter();
        Assert.AreEqual(OwnerQaRoute.Open, router.Route(Key('Q', ctrl: true), false, On));
        Assert.AreEqual(OwnerQaRoute.TogglePlate, router.Route(Key('Q', ctrl: true, shift: true), false, On));
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q', ctrl: true), false, Off), "the overlay is off: the key stays the game's");
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q'), false, On), "Q alone");
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q', ctrl: true, echo: true), false, On), "a held key repeats nothing");
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q', ctrl: true, alt: true), false, On));
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q', ctrl: true, meta: true), false, On));
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q', pressed: false, ctrl: true), false, On), "a release opens nothing");
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(null, false, On), "a mouse or other event");
        var asked = 0;
        router.Route(Key('W', ctrl: true), false, () => { asked++; return true; });
        Assert.AreEqual(0, asked, "availability (a folder search) is asked only for the chord");
    }

    [TestMethod]
    public void An_open_overlay_owns_every_event_and_Escape_or_Ctrl_Q_closes_it()
    {
        var router = new OwnerQaInputRouter();
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(Key('B', ctrl: true), true, On), "Ctrl+B over the overlay is not the game's");
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(Key('W'), true, On));
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(null, true, On), "a mouse event");
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(Key('Q', ctrl: true, shift: true), true, On), "Ctrl+Shift+Q does not close it");
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(Key('E', echo: true), true, On), "a held Escape closes nothing");
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(Key('E', pressed: false), true, On), "a release closes nothing");
        Assert.AreEqual(OwnerQaRoute.Close, router.Route(Key('E'), true, On));
        router.Reset();
        Assert.AreEqual(OwnerQaRoute.Close, router.Route(Key('Q', ctrl: true), true, Off), "Ctrl+Q closes it even when the flag went off meanwhile");
    }

    [TestMethod]
    public void The_release_of_the_closing_key_is_swallowed_once_and_any_other_press_forgets_it()
    {
        var router = new OwnerQaInputRouter();
        Assert.AreEqual(OwnerQaRoute.Close, router.Route(Key('E'), true, On));
        Assert.AreEqual((int)'E', router.ClosingCode);
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(null, false, On), "a mouse event does not forget it");
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('W', pressed: false), false, On), "the release of another key passes");
        Assert.AreEqual(OwnerQaRoute.Consumed, router.Route(Key('E', pressed: false), false, On), "the closing key's release is the overlay's");
        Assert.IsNull(router.ClosingCode);
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('E', pressed: false), false, On), "only once");
        router.Route(Key('Q', ctrl: true), true, On);                       // closes with Ctrl+Q: its code is Q
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('W'), false, Off), "a new press forgets the pending release ...");
        Assert.IsNull(router.ClosingCode);
        Assert.AreEqual(OwnerQaRoute.PassThrough, router.Route(Key('Q', pressed: false), false, On), "... so the old key's release is no longer swallowed");
    }
}
