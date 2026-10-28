using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-047: the packaged build's commit and date line.</summary>
[TestClass]
public sealed class ViewerBuildInfoTests
{
    private static ViewerBuildInfo Read(params (string Key, string? Value)[] pairs) =>
        ViewerBuildInfo.FromMetadata(pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));

    [TestMethod]
    public void PackagedCommitAndDateAreShown()
    {
        var info = Read((ViewerBuildInfo.CommitKey, "CDE7AECC0123456789ABCDEF0123456789ABCDEF"), (ViewerBuildInfo.DateKey, "2026-10-28"), ("Other", "x"));
        Assert.IsTrue(info.IsPackage);
        Assert.AreEqual("cde7aecc0123456789abcdef0123456789abcdef", info.Commit);
        Assert.AreEqual("Arcanum Steamwoven alpha · main cde7aecc01 · 2026-10-28", info.Text);
    }

    [TestMethod]
    public void ShortCommitWithoutDateIsShown()
    {
        var info = Read((ViewerBuildInfo.CommitKey, "abc1234"), (ViewerBuildInfo.DateKey, "28.10.2026"));
        Assert.AreEqual("Arcanum Steamwoven alpha · main abc1234", info.Text);
    }

    [TestMethod]
    public void MissingOrBrokenStampIsASourceRun()
    {
        foreach (var info in new[] { Read(), Read((ViewerBuildInfo.CommitKey, "")), Read((ViewerBuildInfo.CommitKey, "main")),
                     Read((ViewerBuildInfo.CommitKey, "abc123")), Read((ViewerBuildInfo.CommitKey, "abc1234 rm -rf")), Read((ViewerBuildInfo.DateKey, "2026-10-28")) })
        {
            Assert.IsFalse(info.IsPackage);
            Assert.AreEqual("Arcanum Steamwoven · source run (not a packaged build)", info.Text);
        }
    }
}
