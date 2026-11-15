using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-2279: the soak run's settings and summary.</summary>
[TestClass]
public sealed class ViewerSoakTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values)
        => name => values.Where(pair => pair.Name == name).Select(pair => pair.Value).FirstOrDefault();

    [TestMethod]
    public void NoLogVariableMeansNoSoak()
    {
        Assert.IsNull(ViewerSoakSettings.Read(Env(("ARC_SOAK_LOG", "  ")), out var blank));
        Assert.IsNull(blank);
        Assert.IsNull(ViewerSoakSettings.Read(Env(), out var problem));
        Assert.IsNull(problem);
        Assert.IsNull(ViewerSoakSettings.Read(Env(("ARC_SOAK_WINDOW", "editor")), out problem));
        Assert.IsNull(problem);
    }

    [TestMethod]
    public void DefaultsAndEveryVariable()
    {
        var plain = ViewerSoakSettings.Read(Env(("ARC_SOAK_LOG", "/tmp/soak.csv")), out var problem);
        Assert.IsNull(problem);
        Assert.AreEqual(new ViewerSoakSettings("/tmp/soak.csv", 30, null, ViewerSoakWindow.None), plain);
        var full = ViewerSoakSettings.Read(Env(("ARC_SOAK_LOG", "/tmp/soak.csv"), ("ARC_SOAK_WINDOW", "editor"), ("ARC_SOAK_INTERVAL", "5"), ("ARC_SOAK_MINUTES", "31")), out problem);
        Assert.IsNull(problem);
        Assert.AreEqual(new ViewerSoakSettings("/tmp/soak.csv", 5, 31, ViewerSoakWindow.Editor), full);
        Assert.AreEqual(ViewerSoakWindow.NewCharacter, ViewerSoakSettings.Read(Env(("ARC_SOAK_LOG", "x"), ("ARC_SOAK_WINDOW", "new-character")), out _)!.Window);
    }

    [TestMethod]
    [DataRow("ARC_SOAK_WINDOW", "shop")]
    [DataRow("ARC_SOAK_INTERVAL", "0")]
    [DataRow("ARC_SOAK_INTERVAL", "3601")]
    [DataRow("ARC_SOAK_INTERVAL", "-5")]
    [DataRow("ARC_SOAK_INTERVAL", "abc")]
    [DataRow("ARC_SOAK_MINUTES", "0")]
    [DataRow("ARC_SOAK_MINUTES", "1441")]
    [DataRow("ARC_SOAK_MINUTES", "1.5")]
    public void BadValuesAreRefusedWithAReason(string name, string value)
    {
        Assert.IsNull(ViewerSoakSettings.Read(Env(("ARC_SOAK_LOG", "/tmp/soak.csv"), (name, value)), out var problem));
        StringAssert.Contains(problem, name);
    }

    [TestMethod]
    public void ALogPathWithAControlCharacterIsRefused()
    {
        Assert.IsNull(ViewerSoakSettings.Read(Env(("ARC_SOAK_LOG", "a\nb")), out var problem));
        StringAssert.Contains(problem, "ARC_SOAK_LOG");
    }

    private static ViewerSoakSample Sample(int seconds, long texture, long gc = 100, long workingSet = 1000, string window = "CharacterEditor")
        => new(seconds, window, 59.94, texture, 7, 8, 5000 + seconds, 700, 300, 0, 9, gc, 50, 3, 2, 1, workingSet, 33);

    [TestMethod]
    public void RowMatchesTheHeaderColumns()
    {
        var row = Sample(30, 1048576).Line();
        Assert.AreEqual(ViewerSoakSample.Header.Split(',').Length, row.Split(',').Length);
        Assert.IsTrue(row.StartsWith("30,CharacterEditor,59.9,1048576,", StringComparison.Ordinal), row);
    }

    [TestMethod]
    public void SummaryLeavesOutTheWarmupAndShowsTheChange()
    {
        var mib = 1024L * 1024;
        var samples = new[] { Sample(0, 5 * mib), Sample(60, 9 * mib), Sample(120, 10 * mib, workingSet: 100 * mib), Sample(1800, 12 * mib, workingSet: 103 * mib) };
        var summary = ViewerSoakReport.Summary(samples);
        StringAssert.Contains(summary, "samples=4 span=1800s window=CharacterEditor");
        StringAssert.Contains(summary, "texture 10.0MiB -> 12.0MiB (+2.0MiB)");
        StringAssert.Contains(summary, "workingSet 100.0MiB -> 103.0MiB (+3.0MiB)");
        StringAssert.Contains(summary, "threads 33.0 -> 33.0 (0.0)");
        Assert.IsFalse(summary.Contains("warm-up", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AShortRunUsesEveryReadingAndSaysSo()
    {
        var summary = ViewerSoakReport.Summary([Sample(0, 1048576), Sample(30, 2097152)]);
        StringAssert.Contains(summary, "run shorter than 120s of warm-up");
        StringAssert.Contains(summary, "texture 1.0MiB -> 2.0MiB (+1.0MiB)");
        Assert.AreEqual("ARC-DEV-2279 soak summary: no samples", ViewerSoakReport.Summary([]));
    }
}
