using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

/// <summary>Opt-in check of every installed ART file (DAT_ROOT = the game folder) against
/// docs/research/data-census.md: 437 carry bytes after the last frame, all read leniently.</summary>
[TestClass]
public sealed class ArtStackRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_art_file_reads_when_trailing_bytes_are_allowed()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ART check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var lenient = new ArtDecoderOptions { AllowTrailingBytes = true };
        int files = 0, strictRefusals = 0, tailed = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".art", StringComparison.Ordinal)))
        {
            var bytes = stack.Read(stack.Resolve(key).Winner!).Bytes!;
            files++;
            var metadata = ArtDecoder.Inspect(bytes, lenient);
            if (metadata.TrailingBytes > 0) tailed++;
            try { ArtDecoder.Inspect(bytes); } catch (ArtFormatException) { strictRefusals++; }
        }
        Assert.AreEqual(13_993, files);
        Assert.AreEqual(437, tailed);
        Assert.AreEqual(tailed, strictRefusals);
    }
}
