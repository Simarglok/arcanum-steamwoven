using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-051: the bug report's 120 s log, its folder beside the game or in the user folder, and its files.</summary>
[TestClass]
public sealed class BugReportTests
{
    private static readonly Guid Module = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly ActorPlacement Outside = new(new(1), new(92910, 82598), NeighborDirection.XPlus);
    private static readonly DateTime Taken = new(2026, 10, 28, 17, 43, 30, DateTimeKind.Utc);
    // A 1×1 PNG: the save list accepts only a PNG preview.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("arc-bug-report-").FullName;

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

    private static BugReportFacts Facts() => new("Arcanum Steamwoven alpha · main cde7aecc01 · 2026-10-28 (commit cde7aecc0123)", "Steamwoven",
        "Crash Site cave (runtime map 2)", "map 2 cell (92910,82598) facing XPlus", "combat mode on, real time", [("Windows", "inventory")]);

    private static BugReportContent Content(string text = "The door | did not open\nsecond line", byte[]? screenshot = null, BugReportSave? save = null,
        IReadOnlyList<BugReportLine>? log = null) =>
        new(text, Facts(), log ?? [new(Taken.AddSeconds(-2.5), 0, BugReportLineKind.Log, "ARC-WORLD-011 quick-save {}"), new(Taken, 2500, BugReportLineKind.Event, "session map=2")],
            0, Taken, new DateTime(2026, 10, 28, 20, 43, 30, DateTimeKind.Local), screenshot, screenshot is null ? "headless run" : null, save,
            save is null ? "no game session" : null);

    [TestMethod]
    public void The_log_keeps_the_last_120_seconds()
    {
        var now = 0L;
        var log = new BugReportLog(monotonicMilliseconds: () => now, utcNow: () => Taken);
        log.Add(BugReportLineKind.Log, "first");
        now = 60_000; log.Add(BugReportLineKind.Error, "second");
        now = 120_000; Assert.AreEqual(2, log.Recent().Count, "a line exactly 120 s old is still kept");
        now = 120_001;
        CollectionAssert.AreEqual(new[] { "second" }, log.Recent().Select(line => line.Text).ToArray());
        log.Add(BugReportLineKind.Event, "third");
        now = 240_000;
        CollectionAssert.AreEqual(new[] { "third" }, log.Recent().Select(line => line.Text).ToArray());
        now = 400_000;
        Assert.AreEqual(0, log.Recent().Count);
        Assert.AreEqual(0, log.Dropped, "lines that age out are not dropped early");
    }

    [TestMethod]
    public void A_burst_drops_its_oldest_lines_early_and_counts_them()
    {
        var log = new BugReportLog(new BugReportLogLimits { MaxLines = 16, MaxLineChars = 80, MaxChars = 400 }, () => 0, () => Taken);
        for (var i = 0; i < 40; i++) log.Add(BugReportLineKind.Log, $"line {i:00}");
        var kept = log.Recent();
        Assert.AreEqual(16, kept.Count);
        Assert.AreEqual("line 24", kept[0].Text);
        Assert.AreEqual(24, log.Dropped);
        for (var i = 0; i < 10; i++) log.Add(BugReportLineKind.Log, new string('x', 80));
        Assert.IsTrue(log.Recent().Sum(line => line.Text.Length) <= 400, "the character bound holds");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BugReportLog(new BugReportLogLimits { WindowMilliseconds = 0 }));
    }

    [TestMethod]
    public void A_line_is_one_bounded_line_and_the_log_takes_lines_from_any_thread()
    {
        Assert.AreEqual("a | b c", BugReportLog.Clean("a\r\n\nb\tc\u0007\n", 80));
        Assert.AreEqual(new string('x', 79) + "…", BugReportLog.Clean(new string('x', 200), 80));
        var log = new BugReportLog();
        log.Add(BugReportLineKind.Log, " \n ");
        log.Add(BugReportLineKind.Log, null);
        Assert.AreEqual(0, log.Recent().Count, "a blank line is not kept");
        Parallel.For(0, 4_000, i => log.Add(BugReportLineKind.Log, "parallel " + i));
        Assert.AreEqual(4_000, log.Recent().Count);
    }

    [TestMethod]
    public void The_launch_folder_is_the_folder_holding_the_app_or_the_exe()
    {
        var root = Path.Combine(_directory, "Games");
        Assert.AreEqual(root, BugReportLocation.LaunchFolder(Path.Combine(root, "Arcanum Steamwoven.app", "Contents", "MacOS", "Arcanum Steamwoven")));
        Assert.AreEqual(Path.Combine(root, "Arcanum Steamwoven"), BugReportLocation.LaunchFolder(Path.Combine(root, "Arcanum Steamwoven", "Arcanum Steamwoven.exe")));
        Assert.AreEqual(Path.Combine(root, "Contents", "MacOS"), BugReportLocation.LaunchFolder(Path.Combine(root, "Contents", "MacOS", "x")),
            "a MacOS folder outside a bundle is the executable's own folder");
        var user = Path.Combine(_directory, "user", "reports");
        Assert.AreEqual(new BugReportPlaces(Path.Combine(root, "reports"), null, user),
            BugReportLocation.Places(true, Path.Combine(root, "Arcanum Steamwoven.app", "Contents", "MacOS", "Arcanum Steamwoven"), user));
        Assert.AreEqual(new BugReportPlaces(null, null, user), BugReportLocation.Places(false, Path.Combine(root, "godot"), user),
            "a run that is not a packaged game writes to the user folder");
        var translocated = BugReportLocation.Places(true, "/private/var/folders/x/T/AppTranslocation/0A/d/Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven", user);
        Assert.IsNull(translocated.Primary);
        StringAssert.Contains(translocated.PrimaryRefusal, "translocated");
    }

    [TestMethod]
    public void A_report_beside_the_game_holds_the_text_state_log_screenshot_and_a_save_the_ordinary_loader_reads()
    {
        using var session = Session();
        var (save, refusal) = BugReportCapture.Save(session, Outside, s => AreaSaveList.Summary(s, "Bug report", "Crash Site"), Png);
        Assert.IsNull(refusal);
        var places = new BugReportPlaces(Path.Combine(_directory, "game", "reports"), null, Path.Combine(_directory, "user", "reports"));
        var result = BugReportWriter.Write(Content(screenshot: Png, save: save), places);
        Assert.IsTrue(result.Saved, result.Status);
        Assert.AreEqual(Path.Combine(_directory, "game", "reports", "2026-10-28_20-43-30"), result.Folder);
        Assert.IsFalse(result.Fallback);
        Assert.AreEqual(0, result.Failed.Count, string.Join("; ", result.Failed));
        CollectionAssert.AreEquivalent(new[] { "screenshot.png", "log.txt", "save/slot0000.json", "report.md" }, result.Written.ToArray());
        StringAssert.StartsWith(result.Status, "report saved to " + result.Folder);

        var report = File.ReadAllText(Path.Combine(result.Folder!, "report.md"));
        foreach (var expected in new[] { "> The door | did not open", "> second line", "| Build | Arcanum Steamwoven alpha · main cde7aecc01", "| Rules | Steamwoven |",
            "| Map | Crash Site cave (runtime map 2) |", "| Hero | map 2 cell (92910,82598) facing XPlus |", "| Combat | combat mode on, real time |",
            "| Windows | inventory |", "2026-10-28T17:43:30Z", "`save/slot0000.json`", "never commit" })
            StringAssert.Contains(report, expected);
        var log = File.ReadAllLines(Path.Combine(result.Folder!, "log.txt"));
        Assert.AreEqual(4, log.Length);
        StringAssert.StartsWith(log[2], "2026-10-28T17:43:27.500Z   -2.500 s log   ARC-WORLD-011 quick-save");
        StringAssert.Contains(log[3], "event session map=2");
        CollectionAssert.AreEqual(Png, File.ReadAllBytes(Path.Combine(result.Folder!, "screenshot.png")));

        // The save is a save of the ordinary save list: listed with its details and preview, and loaded by the quick-load path.
        var library = new GameSaveLibrary(Path.Combine(result.Folder!, "save"));
        var entry = library.List().Entries.Single();
        Assert.AreEqual(("slot0000", GameSaveDetails.Present, "Bug report", true), (entry.Name, entry.Details, entry.DisplayDescription, entry.HasThumbnail));
        using var other = Session();
        using var loaded = AreaQuickSave.Load(library.Slot(entry.Name), other, _ => null).Owner ?? throw new AssertFailedException("the report's save did not load");
        Assert.AreEqual(Outside, loaded.QueryActor());
    }

    [TestMethod]
    public void A_folder_beside_the_game_that_cannot_be_written_falls_back_to_the_user_folder_and_says_why()
    {
        var blocked = Path.Combine(_directory, "game", "reports");
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "a file where the folder would go");
        var result = BugReportWriter.Write(Content(), new(blocked, null, Path.Combine(_directory, "user", "reports")));
        Assert.IsTrue(result.Saved, result.Status);
        Assert.IsTrue(result.Fallback);
        StringAssert.StartsWith(result.Folder, Path.Combine(_directory, "user", "reports"));
        StringAssert.Contains(result.Status, "the folder beside the game could not be used");
        var report = File.ReadAllText(Path.Combine(result.Folder!, "report.md"));
        StringAssert.Contains(report, "This folder is in the user folder because the folder beside the game could not be used");
        StringAssert.Contains(report, "`screenshot.png`: missing: headless run.");
        StringAssert.Contains(report, "`save/`: no save: no game session.");
        CollectionAssert.AreEquivalent(new[] { "log.txt", "report.md" }, result.Written.ToArray());

        var refused = BugReportWriter.Write(Content(), new(null, "macOS runs this app from a read-only translocated copy", Path.Combine(_directory, "user", "reports")));
        Assert.IsTrue(refused.Saved && refused.Fallback);
        Assert.AreEqual(Path.Combine(_directory, "user", "reports", "2026-10-28_20-43-30_2"), refused.Folder, "a second report in the same second gets _2");
        StringAssert.Contains(refused.Status, "translocated");

        var nowhere = Path.Combine(_directory, "user-file");
        File.WriteAllText(nowhere, "x");
        var failed = BugReportWriter.Write(Content(), new(blocked, null, nowhere));
        Assert.IsFalse(failed.Saved);
        Assert.IsNull(failed.Folder);
        Assert.AreEqual(2, failed.Failed.Count, failed.Status);
        StringAssert.StartsWith(failed.Status, "the report could not be written: beside the game:");
    }

    [TestMethod]
    public void A_refused_placement_takes_no_save()
    {
        using var session = Session();
        var (save, refusal) = BugReportCapture.Save(session, Outside with { Map = new(0) }, s => AreaSaveList.Summary(s, "x", "x"), null);
        Assert.IsNull(save);
        StringAssert.Contains(refusal, "InvalidActor");
    }
}
