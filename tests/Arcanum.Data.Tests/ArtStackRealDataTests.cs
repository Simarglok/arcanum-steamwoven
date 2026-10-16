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
        int files = 0, strictRefusals = 0, tailed = 0, oneByte = 0, upTo64 = 0, longer = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".art", StringComparison.Ordinal)))
        {
            var bytes = stack.Read(stack.Resolve(key).Winner!).Bytes!;
            files++;
            var metadata = ArtDecoder.Inspect(bytes, lenient);
            if (metadata.TrailingBytes > 0)
            {
                tailed++;
                if (metadata.TrailingBytes == 1) oneByte++; else if (metadata.TrailingBytes <= 64) upTo64++; else longer++;
                // Every frame of a tailed file decodes to exactly width x height pixels.
                var palette = Enumerable.Range(0, 4).First(slot => metadata.Header.PalettePresenceWords[slot] != 0);
                for (var group = 0; group < metadata.Header.PhysicalGroupCount; group++)
                    for (var frame = 0; frame < metadata.Header.FramesPerGroup; frame++)
                    {
                        var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frame, palette), lenient);
                        Assert.AreEqual(decoded.Frame.Width * decoded.Frame.Height * 4, decoded.Rgba.Count, key);
                    }
            }
            try { ArtDecoder.Inspect(bytes); } catch (ArtFormatException) { strictRefusals++; }
        }
        Assert.AreEqual(13_993, files);
        Assert.AreEqual(437, tailed);
        Assert.AreEqual(tailed, strictRefusals);
        CollectionAssert.AreEqual(new[] { 44, 134, 259 }, new[] { oneByte, upTo64, longer });
    }
}
