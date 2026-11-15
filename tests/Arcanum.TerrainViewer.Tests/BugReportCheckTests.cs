using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-2189: the owner check a bug report is tagged with: the active-check file, the id format and the report.md line
/// (synthetic files only).</summary>
[TestClass]
public sealed class BugReportCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("arc-bug-check-").FullName;

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string Write(string text, DateTime? modifiedUtc = null)
    {
        var path = Path.Combine(_directory, BugReportCheck.FileName);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modifiedUtc ?? Now.AddMinutes(-5));
        return path;
    }

    [TestMethod]
    [DataRow("ARC-DEV-2189#3", "ARC-DEV-2189#3")]
    [DataRow("  ARC-UI-7#12 \n", "ARC-UI-7#12")]
    [DataRow("ARC-DEV-2189#0", null)]
    [DataRow("ARC-DEV-2189", null)]
    [DataRow("arc-dev-2189#3", null)]
    [DataRow("ARC-DEV-2189#3 and more", null)]
    [DataRow("ARC-DEV-X#3", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void Normalize_accepts_only_a_step_id(string? text, string? expected) => Assert.AreEqual(expected, BugReportCheck.Normalize(text));

    [TestMethod]
    public void The_active_file_gives_its_first_line()
    {
        Assert.AreEqual("ARC-DEV-2189#3", BugReportCheck.ReadActive(Write("ARC-DEV-2189#3\nignored second line\n"), Now));
    }

    [TestMethod]
    public void A_missing_stale_oversized_or_malformed_file_is_no_check()
    {
        Assert.IsNull(BugReportCheck.ReadActive(null, Now));
        Assert.IsNull(BugReportCheck.ReadActive(Path.Combine(_directory, "nothing.txt"), Now), "missing");
        Assert.IsNull(BugReportCheck.ReadActive(_directory, Now), "a folder is not a file");
        Assert.IsNull(BugReportCheck.ReadActive(Write("ARC-DEV-2189#3\n", Now - BugReportCheck.MaxAge - TimeSpan.FromMinutes(1)), Now), "stale");
        Assert.AreEqual("ARC-DEV-2189#3", BugReportCheck.ReadActive(Write("ARC-DEV-2189#3\n", Now - BugReportCheck.MaxAge), Now), "exactly at the limit still counts");
        Assert.IsNull(BugReportCheck.ReadActive(Write("ARC-DEV-2189#3\n" + new string('x', 400)), Now), "oversized");
        Assert.IsNull(BugReportCheck.ReadActive(Write("not a check\n"), Now), "malformed");
        Assert.IsNull(BugReportCheck.ReadActive(Write(""), Now), "empty");
    }

    [TestMethod]
    public void The_file_is_found_above_a_start_folder_and_the_variable_wins()
    {
        var qa = Directory.CreateDirectory(Path.Combine(_directory, "local", "qa")).FullName;
        var file = Path.Combine(qa, BugReportCheck.FileName); File.WriteAllText(file, "ARC-DEV-2189#3\n");
        var deep = Directory.CreateDirectory(Path.Combine(_directory, "artifacts", "packages", "macos")).FullName;
        Assert.AreEqual(Path.GetFullPath(file), BugReportCheck.FindActiveFile([null, deep], false, null), "found by walking up three levels");
        Assert.IsNull(BugReportCheck.FindActiveFile([deep], true, null), "an automated run never looks around");
        Assert.AreEqual("custom.txt", BugReportCheck.FindActiveFile([deep], true, "custom.txt"), "the variable names the file, also in an automated run");
        Assert.AreEqual("custom.txt", BugReportCheck.FindActiveFile([deep], false, "custom.txt"));
        File.Delete(file);
        Assert.IsNull(BugReportCheck.FindActiveFile([deep], false, null), "no file, no check");
    }

    [TestMethod]
    public void The_report_carries_the_check_line_before_the_first_heading()
    {
        var markdown = BugReportWriter.Markdown(Content("ARC-DEV-2189#3"), null, [BugReportWriter.LogFile], [], null);
        StringAssert.Contains(markdown, "# Bug report 2026-11-15 14:00:00\n\nCheck: ARC-DEV-2189#3\n\n## What the player wrote\n");
        Assert.AreEqual("ARC-DEV-2189#3", BugReportCheck.ReadReportLine(markdown));
        Assert.AreEqual(1, markdown.Split("\nCheck: ").Length - 1, "exactly one line");
    }

    [TestMethod]
    public void An_untagged_or_badly_tagged_report_has_no_check_line()
    {
        foreach (var check in new string?[] { null, "", "  ", "not a step", "ARC-DEV-2189#3\n## injected" })
        {
            var markdown = BugReportWriter.Markdown(Content(check), null, [BugReportWriter.LogFile], [], null);
            Assert.IsFalse(markdown.Contains("Check: ", StringComparison.Ordinal), $"check '{check}' writes no line");
            Assert.IsNull(BugReportCheck.ReadReportLine(markdown));
        }
    }

    [TestMethod]
    public void The_player_cannot_forge_the_line_and_the_report_still_reads_back()
    {
        var text = "Check: ARC-DEV-1#1\nCheck: ARC-DEV-2#2";
        var forged = BugReportWriter.Markdown(Content(null, text), null, [BugReportWriter.LogFile], [], null);
        Assert.IsNull(BugReportCheck.ReadReportLine(forged), "the player's lines are quoted and sit below the first heading");
        var tagged = BugReportWriter.Markdown(Content("ARC-DEV-2189#3", text), null, [BugReportWriter.LogFile], [], null);
        Assert.AreEqual("ARC-DEV-2189#3", BugReportCheck.ReadReportLine(tagged));
    }

    [TestMethod]
    public void A_report_is_written_with_the_check_line_into_its_folder()
    {
        var result = BugReportWriter.Write(Content("ARC-DEV-2189#3"), new BugReportPlaces(null, null, _directory));
        Assert.IsTrue(result.Saved, result.Status);
        Assert.AreEqual("ARC-DEV-2189#3", BugReportCheck.ReadReportLine(File.ReadAllText(Path.Combine(result.Folder!, BugReportWriter.ReportFile))));
    }

    private static BugReportContent Content(string? check, string text = "The door did not open") =>
        new(text, new("build", "Steamwoven", "Crash Site cave", "map 2", "combat mode on", []), [], 0, Now, new DateTime(2026, 11, 15, 14, 0, 0, DateTimeKind.Local),
            null, "headless run", null, "no game session", check);
}
