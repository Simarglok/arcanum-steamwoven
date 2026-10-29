using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-010 save list: numbering, order, the protected auto slot, details and previews (ledger SL1–SL8).</summary>
[TestClass]
public sealed class SaveLibraryTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("arc-save-library-").FullName;

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private GameSaveLibrary Library(GameSaveLimits? limits = null) => new(_directory, limits);

    private static GameSaveSummary Summary(string description = "Before the cave") =>
        new(description, "Hero", 3, "Crash Site", 1, new(1885, 3, 14, 9, 30));

    private void Age(string name, int minutesAgo) =>
        File.SetLastWriteTimeUtc(Path.Combine(_directory, name + ".json"), new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo));

    [TestMethod]
    public void NewSavesTakeTheNextNumberFromTheHighestAndStartAtZero()
    {
        using var owner = GoldenSession();
        var library = Library();
        Assert.AreEqual("slot0000", library.Create(owner.Capture(), Summary()).Name, "The first numbered save is slot0000 (the original's Slot0000).");
        Assert.AreEqual("slot0001", library.Create(owner.Capture(), Summary()).Name);
        Assert.IsTrue(library.Write("slot0007", owner.Capture(), Summary()).Saved);
        Assert.IsTrue(library.Write(GameSaveLibrary.AutoName, owner.Capture(), Summary()).Saved);
        Assert.AreEqual("slot0008", library.Create(owner.Capture(), Summary()).Name, "The highest number plus one; gaps are not reused, the auto slot is not counted.");

        Assert.IsTrue(library.Write("slot9998", owner.Capture(), Summary()).Saved);
        var refused = library.Create(owner.Capture(), Summary());
        Assert.AreEqual((GameSaveFailure.SlotNumbersExhausted, false, (string?)null), (refused.Failure, refused.Saved, refused.Name),
            "The original refuses a new save once the next number reaches 9999.");
        Assert.IsFalse(File.Exists(Path.Combine(_directory, "slot9999.json")));
    }

    [TestMethod]
    public void TheListShowsTheAutoSlotFirstThenNewestFirstWhileLatestIsTheNewestOfAll()
    {
        using var owner = GoldenSession();
        var library = Library();
        foreach (var name in new[] { "slot0000", "slot0001", "slot0002", GameSaveLibrary.AutoName })
            Assert.IsTrue(library.Write(name, owner.Capture(), Summary(name)).Saved);
        Age("slot0000", 5); Age("slot0001", 30); Age("slot0002", 10); Age(GameSaveLibrary.AutoName, 20);

        var list = library.List();
        Assert.AreEqual(GameSaveFailure.None, list.Failure);
        CollectionAssert.AreEqual(new[] { "quicksave", "slot0000", "slot0002", "slot0001" }, list.Entries.Select(e => e.Name).ToArray());
        Assert.IsTrue(list.Entries[0].IsAuto);
        Assert.AreEqual("slot0000", library.Latest()!.Name, "The latest save ignores the auto slot's place at the top of the list.");

        Age(GameSaveLibrary.AutoName, 1);
        Assert.AreEqual(GameSaveLibrary.AutoName, library.Latest()!.Name, "A newer auto save is the latest save.");
        Assert.IsNull(new GameSaveLibrary(Path.Combine(_directory, "absent")).Latest());
        Assert.AreEqual(0, new GameSaveLibrary(Path.Combine(_directory, "absent")).List().Entries.Count);
    }

    [TestMethod]
    public void EqualFileDatesOrderTheHigherNameFirst()
    {
        using var owner = GoldenSession();
        var library = Library();
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        Age("slot0000", 0); Age("slot0001", 0);
        CollectionAssert.AreEqual(new[] { "slot0001", "slot0000" }, library.List().Entries.Select(e => e.Name).ToArray());
        Assert.AreEqual("slot0001", library.Latest()!.Name);
    }

    [TestMethod]
    public void DetailsRoundTripAndAPreviewIsKeptWithTheSave()
    {
        using var owner = GoldenSession();
        var library = Library();
        var written = library.Create(owner.Capture(), Summary("Ready to leave"), Png);
        Assert.AreEqual((GameSaveFailure.None, (string?)null), (written.Failure, written.Warning), written.Detail);
        var entry = library.List().Entries.Single();
        Assert.AreEqual((GameSaveDetails.Present, true, "Ready to leave"), (entry.Details, entry.HasThumbnail, entry.DisplayDescription));
        Assert.AreEqual(Summary("Ready to leave"), entry.Summary);
        Assert.AreEqual(written.Snapshot!.Bytes, entry.Bytes);
        CollectionAssert.AreEqual(Png, library.ReadThumbnail("slot0000"));

        // An overwrite without a preview removes the previous one, so the list never shows a picture of another save.
        Assert.IsTrue(library.Write("slot0000", owner.Capture(), Summary("Second")).Saved);
        entry = library.List().Entries.Single();
        Assert.AreEqual((false, "Second"), (entry.HasThumbnail, entry.DisplayDescription));
        Assert.IsNull(library.ReadThumbnail("slot0000"));
        Assert.IsTrue(library.Slot("slot0000").Read().Snapshot is not null, "The snapshot loads through the ordinary slot.");
    }

    [TestMethod]
    public void DetailsThatCannotBeWrittenAreRemovedRatherThanLeftStale()
    {
        using var owner = GoldenSession();
        Assert.IsTrue(Library().Create(owner.Capture(), Summary("old details")).Saved);
        var tight = Library(new(MaxDetailsBytes: 256));
        var written = tight.Write("slot0000", owner.Capture(), Summary(new string('d', 80)) with { HeroName = new string('h', 64), Location = new string('l', 128) });
        Assert.IsTrue(written.Saved);
        StringAssert.StartsWith(written.Warning, "DETAILS_NOT_WRITTEN DETAILS_TOO_LARGE");
        var entry = Library().List().Entries.Single();
        Assert.AreEqual((GameSaveDetails.Missing, "slot0000"), (entry.Details, entry.DisplayDescription), "The old description is not shown for the new snapshot.");
    }

    [TestMethod]
    public void AnEmptyDescriptionShowsTheSlotNameAndTheAutoSlotItsFixedText()
    {
        using var owner = GoldenSession();
        var library = Library();
        Assert.IsTrue(library.Create(owner.Capture(), Summary("")).Saved);
        // A quick save from before ARC-UI-010 has no details file.
        Assert.IsTrue(GameSessionSaveSlot.QuickSave(_directory).Write(owner.Capture()).Saved);
        var entries = library.List().Entries;
        Assert.AreEqual((GameSaveDetails.Missing, (GameSaveSummary?)null, "Auto-Save"), (entries[0].Details, entries[0].Summary, entries[0].DisplayDescription));
        Assert.AreEqual((GameSaveDetails.Present, "slot0000"), (entries[1].Details, entries[1].DisplayDescription));
    }

    [TestMethod]
    public void DamagedAndStaleDetailsAreMarkedButTheSaveStaysListed()
    {
        using var owner = GoldenSession();
        var library = Library();
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        var details = Path.Combine(_directory, "slot0000" + GameSaveLibrary.DetailsSuffix);
        var text = File.ReadAllText(details);
        foreach (var damaged in new[] { "{", "[]", text.Replace("\"schema\":2", "\"schema\":3", StringComparison.Ordinal),
            text.Replace("\"schema\":2", "\"schema\":1", StringComparison.Ordinal), text.Replace("\"portrait\":0", "\"portrait\":-1", StringComparison.Ordinal),
            text.Replace("\"schema\":2", "\"schema\":\"2\"", StringComparison.Ordinal), text.Replace("\"schema\":2", "\"schema\":1.5", StringComparison.Ordinal),
            text.Replace("\"storyState\":null", "\"storyState\":\"7\"", StringComparison.Ordinal),
            text.Replace("\"storyState\":null", "\"storyState\":-1", StringComparison.Ordinal),
            text.Replace(",0],\"portrait\"", "],\"portrait\"", StringComparison.Ordinal), text.Replace(",\"portrait\":0", "", StringComparison.Ordinal),
            text.Replace("}", ",\"extra\":1}", StringComparison.Ordinal), text.Replace("\"level\":3", "\"level\":-1", StringComparison.Ordinal),
            text.Replace("Hero", "He\\nro", StringComparison.Ordinal) })
        {
            File.WriteAllText(details, damaged);
            Assert.AreEqual(GameSaveDetails.Damaged, library.List().Entries.Single(e => e.Name == "slot0000").Details, damaged);
        }
        File.WriteAllBytes(details, [0xFF, 0xFE]);
        Assert.AreEqual(GameSaveDetails.Damaged, library.List().Entries.Single(e => e.Name == "slot0000").Details);
        File.WriteAllText(details, new string(' ', 17 * 1024));
        Assert.AreEqual(GameSaveDetails.Damaged, library.List().Entries.Single(e => e.Name == "slot0000").Details, "Over the details bound.");

        // Details written for another snapshot (an interrupted overwrite): shown, but marked stale.
        var other = Path.Combine(_directory, "slot0001" + GameSaveLibrary.DetailsSuffix);
        var bytes = library.List().Entries.Single(e => e.Name == "slot0001").Bytes;
        File.WriteAllText(other, File.ReadAllText(other).Replace($"\"snapshotBytes\":{bytes}", $"\"snapshotBytes\":{bytes + 1}", StringComparison.Ordinal));
        var stale = library.List().Entries.Single(e => e.Name == "slot0001");
        Assert.AreEqual((GameSaveDetails.Stale, "Before the cave"), (stale.Details, stale.DisplayDescription));
    }

    [TestMethod]
    public void DetailsKeepThePortraitTheStoryStateAndTheSecondsAndOldDetailsStillRead()
    {
        using var owner = GoldenSession();
        var library = Library();
        var summary = Summary() with { GameTime = new(1885, 3, 14, 9, 30, 41), Portrait = 1004, StoryState = 7 };
        Assert.IsTrue(library.Create(owner.Capture(), summary).Saved);
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        var entries = library.List().Entries;
        Assert.AreEqual((GameSaveDetails.Present, summary), (entries.Single(e => e.Name == "slot0000").Details, entries.Single(e => e.Name == "slot0000").Summary),
            "ARC-UI-067: schema 2 records what the original's save info shows (ledger MW22).");
        Assert.IsNull(entries.Single(e => e.Name == "slot0001").Summary!.StoryState, "No story state was given: written as null.");

        // A details file of schema 1 (written before ARC-UI-067) reads without the new fields.
        var details = Path.Combine(_directory, "slot0001" + GameSaveLibrary.DetailsSuffix);
        var bytes = entries.Single(e => e.Name == "slot0001").Bytes;
        File.WriteAllText(details, "{\"format\":\"arcanum-steamwoven-save-details\",\"schema\":1,\"snapshotBytes\":" + bytes
            + ",\"description\":\"Old\",\"hero\":\"Hero\",\"level\":3,\"location\":\"Crash Site\",\"map\":1,\"gameTime\":[1885,3,14,9,30]}");
        var old = library.List().Entries.Single(e => e.Name == "slot0001");
        Assert.AreEqual((GameSaveDetails.Present, Summary("Old")), (old.Details, old.Summary), "Portrait 0, no story state and second 0.");
    }

    [TestMethod]
    public void TheAutoSlotCannotBeDeletedAndADeleteRemovesTheSaveDetailsAndPreview()
    {
        using var owner = GoldenSession();
        var library = Library();
        Assert.IsTrue(library.Write(GameSaveLibrary.AutoName, owner.Capture(), Summary(), Png).Saved);
        Assert.IsTrue(library.Create(owner.Capture(), Summary(), Png).Saved);
        var files = Directory.GetFiles(_directory).Length;

        var protectedDelete = library.Delete(GameSaveLibrary.AutoName);
        Assert.AreEqual((GameSaveFailure.Protected, false), (protectedDelete.Failure, protectedDelete.Deleted));
        Assert.AreEqual(files, Directory.GetFiles(_directory).Length, "The refused delete removed nothing.");

        var deleted = library.Delete("slot0000");
        Assert.AreEqual((GameSaveFailure.None, (string?)null), (deleted.Failure, deleted.Warning));
        CollectionAssert.AreEquivalent(new[] { "quicksave.json", "quicksave.meta.json", "quicksave.png" }, Directory.GetFiles(_directory).Select(Path.GetFileName).ToArray());
        Assert.AreEqual(GameSaveFailure.NoSuchSave, library.Delete("slot0000").Failure);
        Assert.AreEqual(GameSaveFailure.BadName, library.Delete("../quicksave").Failure);
        Assert.AreEqual(GameSaveFailure.BadName, library.Delete("slot1").Failure);
        Assert.AreEqual(GameSaveFailure.BadName, library.Write("slot9999", owner.Capture(), Summary()).Failure, "Numbered saves stop at slot9998.");
    }

    [TestMethod]
    public void AnInvalidSummaryOrPreviewWritesNothing()
    {
        using var owner = GoldenSession();
        var library = Library();
        foreach (var summary in new[] { Summary(new string('d', 81)), Summary("two\nlines"), Summary() with { HeroName = new string('h', 65) },
            Summary() with { Location = "\t" }, Summary() with { Level = -1 }, Summary() with { GameTime = new(1885, 13, 1, 0, 0) },
            Summary() with { GameTime = new(1885, 1, 1, 24, 0) }, Summary() with { RuntimeMap = -1 } })
        {
            var refused = library.Create(owner.Capture(), summary);
            Assert.AreEqual(GameSaveFailure.InvalidSummary, refused.Failure, refused.Detail);
        }
        Assert.AreEqual("THUMBNAIL_NOT_PNG", library.Create(owner.Capture(), Summary(), new byte[] { 1, 2, 3 }).Detail);
        Assert.AreEqual("THUMBNAIL_TOO_LARGE", Library(new(MaxThumbnailBytes: 9)).Create(owner.Capture(), Summary(), Png).Detail);
        Assert.AreEqual(GameSaveFailure.BadName, library.Write("Slot0001", owner.Capture(), Summary()).Failure);
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length, "Every refusal came before a write.");
    }

    [TestMethod]
    public void AFailedSnapshotWriteKeepsThePreviousSaveAndItsDetails()
    {
        using var owner = GoldenSession();
        var library = Library();
        Assert.IsTrue(library.Write("slot0003", owner.Capture(), Summary("kept"), Png).Saved);
        var before = Directory.GetFiles(_directory).ToDictionary(f => f, File.ReadAllBytes);
        // A directory in the temporary file's place makes the snapshot write fail.
        Directory.CreateDirectory(library.Slot("slot0003").TemporaryPath);
        var failed = library.Write("slot0003", owner.Capture(), Summary("lost"));
        Assert.AreEqual((GameSaveFailure.SnapshotFailed, SaveSlotFailure.WriteFailed), (failed.Failure, failed.Snapshot!.Failure));
        foreach (var (path, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), path);
        Assert.AreEqual("kept", library.List().Entries.Single().DisplayDescription);
    }

    [TestMethod]
    public void UnrelatedFilesAreNotSavesAndTheEntryBoundIsReported()
    {
        using var owner = GoldenSession();
        var library = Library(new(MaxEntries: 2));
        foreach (var file in new[] { "notes.json", "slot12.json", "SLOT0099.json", "slot0005.meta.json", "slot0006.png", "slot0007.json.tmp" })
            File.WriteAllText(Path.Combine(_directory, file), "x");
        Assert.AreEqual(0, library.List().Entries.Count);
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        Assert.AreEqual("slot0000", library.List().Entries.Single().Name, "An orphaned details file does not take a number.");
        Assert.IsTrue(library.Create(owner.Capture(), Summary()).Saved);
        Assert.IsTrue(library.Write("slot0004", owner.Capture(), Summary()).Saved);
        var list = library.List();
        Assert.AreEqual((2, true, GameSaveFailure.None), (list.Entries.Count, list.Truncated, list.Failure));
        Assert.AreEqual(GameSaveFailure.ListFailed, library.Create(owner.Capture(), Summary()).Failure, "A new number is not taken from a partial list.");
        Assert.IsTrue(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_directory, "slot0000.meta.json"))).Contains("\"snapshotBytes\":", StringComparison.Ordinal));
    }
}
