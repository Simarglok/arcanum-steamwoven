using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-010 save and load window state and the save-list save path (ledger SL1–SL8).</summary>
[TestClass]
public sealed class AreaSaveListTests
{
    private static readonly Guid Module = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly ActorPlacement Outside = new(new(1), new(92910, 82598), NeighborDirection.XPlus);
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("arc-save-list-").FullName;

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static ProfileBinding Clock() => new(1, AreaAmbientDriver.ClockPolicy, AreaAmbientDriver.ClockSourceRevision,
        string.Concat(Enumerable.Repeat("aa", 32)), new string('c', 64), 1885, 15);

    private static GameSessionOwner Session() =>
        AreaTransitionCoordinator.CreateSession(Clock(), Guid.NewGuid(), Module, new AreaStepCell(92928, 82560), 1).Owner
        ?? throw new AssertFailedException("session");

    private static GameSaveEntry Entry(string name, string description = "d") =>
        new(name, name == GameSaveLibrary.AutoName, 10, new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), GameSaveDetails.Present,
            new(description, "Hero", 2, "Crash Site", 1, new(1885, 3, 14, 9, 5)), false);

    [TestMethod]
    public void SaveModeListsANewSaveFirstAndAsksBeforeOverwriting()
    {
        var list = new AreaSaveList(AreaSaveListMode.Save, [Entry("quicksave"), Entry("slot0001", "Gate"), Entry("slot0000", "Start")]);
        CollectionAssert.AreEqual(new[] { "<new save>", "[auto] d", "Gate", "Start" }, list.Rows().Select(r => r.Title).ToArray());
        Assert.AreEqual((1, "quicksave"), (list.Selected, list.SelectedEntry!.Name), "The first save is selected on opening, as in the original.");

        list.Select(0);
        list.Description = "   ";
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.SaveNew, null, ""), list.Confirm(), "Only spaces: the slot name.");
        list.Description = "  By the plane";
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.SaveNew, null, "  By the plane"), list.Confirm(), "Typed text is kept as typed.");

        list.Select(2);
        Assert.AreEqual(AreaSaveCommand.Nothing, list.Confirm());
        Assert.AreEqual(AreaSavePrompt.ConfirmOverwrite, list.Prompt);
        list.Move(1);
        Assert.AreEqual(2, list.Selected, "The selection does not move while a question is open.");
        Assert.IsTrue(list.Cancel(), "Escape answers the question no and keeps the window.");
        Assert.AreEqual(AreaSavePrompt.None, list.Prompt);
        Assert.IsFalse(list.Cancel(), "Escape without a question closes the window.");

        Assert.AreEqual(AreaSaveCommand.Nothing, list.Confirm());
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.Overwrite, "slot0001", "Gate"), list.Confirm(),
            "Return answers yes; the overwrite keeps the save's own description, as the original.");
        list.Select(1); list.Confirm();
        Assert.AreEqual("d", list.Answer(true).Description, "Overwriting the auto-save keeps its own description.");
        var bare = new AreaSaveList(AreaSaveListMode.Save, [Entry("quicksave") with { Details = GameSaveDetails.Missing, Summary = null }]);
        bare.Confirm();
        Assert.AreEqual("Auto-Save", bare.Answer(true).Description, "An auto-save without details is overwritten as \"Auto-Save\".");
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.SaveNew, null, ""), new AreaSaveList(AreaSaveListMode.Save, []).Confirm(),
            "With no saves the new-save row is selected.");
    }

    [TestMethod]
    public void ActivatingARowConfirmsItButNeverAnswersAnOpenQuestion()
    {
        var list = new AreaSaveList(AreaSaveListMode.Load, [Entry("quicksave"), Entry("slot0001"), Entry("slot0000")]);
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.Load, "slot0000", ""), list.Activate(2));
        list.Select(1);
        list.RequestDelete();
        Assert.AreEqual(AreaSaveCommand.Nothing, list.Activate(2), "A double click on another row does not delete the asked one.");
        Assert.AreEqual((AreaSavePrompt.ConfirmDelete, 1), (list.Prompt, list.Selected));
    }

    [TestMethod]
    public void DeleteAsksFirstAndTheAutoSaveIsRefused()
    {
        var list = new AreaSaveList(AreaSaveListMode.Load, [Entry("quicksave"), Entry("slot0000")]);
        Assert.AreEqual(0, list.Selected);
        Assert.AreEqual(AreaSaveCommand.Nothing, list.RequestDelete());
        Assert.AreEqual((AreaSavePrompt.None, "the auto-save cannot be deleted"), (list.Prompt, list.Notice));
        list.Move(1);
        Assert.AreEqual(AreaSaveCommand.Nothing, list.RequestDelete());
        Assert.AreEqual(AreaSavePrompt.ConfirmDelete, list.Prompt);
        Assert.AreEqual(AreaSaveCommand.Nothing, list.Answer(false));
        list.RequestDelete();
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.Delete, "slot0000", ""), list.Answer(true));

        list.Refresh([Entry("quicksave")], null, "deleted", clearSelection: true);
        Assert.AreEqual((-1, "deleted", AreaSavePrompt.None), (list.Selected, list.Notice, list.Prompt), "A delete clears the selection, as the original.");
        Assert.AreEqual(AreaSaveCommand.Nothing, list.Confirm());
        list.Refresh([Entry("quicksave"), Entry("slot0004")], null, null);
        Assert.AreEqual(0, list.Selected, "Without clearing, the selection is kept in range.");
        Assert.AreEqual(new AreaSaveCommand(AreaSaveCommandKind.Load, "quicksave", ""), list.Confirm());
        list.Refresh([Entry("slot0003"), Entry("slot0002")], "slot0002", null);
        Assert.AreEqual(1, list.Selected, "A refresh selects the named save.");

        var empty = new AreaSaveList(AreaSaveListMode.Load, []);
        Assert.AreEqual((-1, 0), (empty.Selected, empty.RowCount));
        Assert.AreEqual(AreaSaveCommand.Nothing, empty.Confirm());
        Assert.AreEqual("select a save first", empty.Notice);
        empty.Move(1);
        Assert.AreEqual(-1, empty.Selected);
    }

    [TestMethod]
    public void RowsDescribeTheSaveAndMarkMissingOrStaleDetails()
    {
        var detail = AreaSaveList.Detail(Entry("slot0004"));
        StringAssert.StartsWith(detail, "Hero, level 2 | Crash Site | 14.03.1885 09:05 | saved ");
        StringAssert.EndsWith(detail, "| slot0004");
        var missing = Entry("quicksave") with { Details = GameSaveDetails.Missing, Summary = null };
        Assert.AreEqual("[auto] Auto-Save", AreaSaveList.Title(missing));
        StringAssert.Contains(AreaSaveList.Detail(missing), "no details");
        StringAssert.Contains(AreaSaveList.Detail(Entry("slot0001") with { Details = GameSaveDetails.Stale }), "details may be out of date");
    }

    [TestMethod]
    public void ASummaryNamesTheDiagnosticPcThePlaceAndTheGameTime()
    {
        using var session = Session();
        var clock = session.QueryClockSnapshot();
        var summary = AreaSaveList.Summary(session, "two\nlines" + new string('x', 100), "Crash Site cave");
        Assert.AreEqual(("diagnostic PC", 0, "Crash Site cave", 1), (summary.HeroName, summary.Level, summary.Location, summary.RuntimeMap));
        Assert.AreEqual(new GameSaveTime(clock.Year, clock.Month, clock.DayOfMonth, clock.Hour, clock.Minute), summary.GameTime);
        Assert.AreEqual(80, summary.Description.Length);
        StringAssert.StartsWith(summary.Description, "twolines");
    }

    [TestMethod]
    public void TheAutoSaveAndANewSaveLoadThroughTheLibrary()
    {
        var library = new GameSaveLibrary(_directory);
        using var first = Session();
        var auto = AreaQuickSave.Save(first, Outside, library, GameSaveLibrary.AutoName, s => AreaSaveList.Summary(s, GameSaveLibrary.AutoDescription, "Crash Site"));
        Assert.IsTrue(auto.Saved, auto.Status);
        StringAssert.StartsWith(auto.Status, "quick saved: map 1 cell (92910,82598)");
        var moved = Outside with { Cell = new(92911, 82598) };
        var created = AreaQuickSave.Save(first, moved, library, null, s => AreaSaveList.Summary(s, "", "Crash Site"));
        Assert.AreEqual((true, "slot0000"), (created.Saved, created.Library!.Name));
        StringAssert.StartsWith(created.Status, "saved slot0000: map 1 cell (92911,82598)");
        Assert.AreEqual("slot0000", library.List().Entries[1].DisplayDescription);

        using var second = Session();
        using var loaded = AreaQuickSave.Load(library.Slot("quicksave"), second, _ => null).Owner ?? throw new AssertFailedException("load");
        Assert.AreEqual(Outside, loaded.QueryActor());
        using var newest = AreaQuickSave.Load(library.Slot(library.Latest()!.Name), second, _ => null).Owner ?? throw new AssertFailedException("load");
        Assert.AreEqual(moved, newest.QueryActor());

        var refused = AreaQuickSave.Save(first, Outside with { Map = new(0) }, library, null, s => AreaSaveList.Summary(s, "", "x"));
        Assert.AreEqual((false, GameSessionFailure.InvalidActor), (refused.Saved, refused.PlaceFailure));
        Assert.AreEqual(2, library.List().Entries.Count, "A refused placement writes nothing.");
        Assert.IsTrue(library.Write("slot9998", first.Capture(), AreaSaveList.Summary(first, "", "x")).Saved);
        var full = AreaQuickSave.Save(first, Outside, library, null, s => AreaSaveList.Summary(s, "", "x"));
        Assert.AreEqual("save FAILED (no free save number below 9999; delete or overwrite a save); the previous save is kept", full.Status);
    }
}
