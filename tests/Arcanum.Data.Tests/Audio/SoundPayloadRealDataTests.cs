using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Audio;

/// <summary>ARC-DATA-041 opt-in check of the installed original (DAT_ROOT = the game folder, module Arcanum): every WAV
/// and MP3 file of the content stack opens and is admitted. Only counts and format classes are compared.</summary>
[TestClass]
public sealed class SoundPayloadRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_sound_file_is_admitted()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = SoundCatalog.Open(stack).Catalog!;

        var keys = stack.Keys("sound/").Where(k => k.EndsWith(".wav", StringComparison.Ordinal) || k.EndsWith(".mp3", StringComparison.Ordinal)).ToArray();
        var payloads = keys.Select(k => catalog.OpenPayload(catalog.ResolveFile(k))).ToArray();
        Assert.AreEqual(3410, payloads.Length);
        foreach (var payload in payloads) Assert.AreEqual(SoundPayloadStatus.Admitted, payload.Status, $"{payload.Key}: {payload.Detail}");
        var wav = payloads.Where(p => p.Kind == SoundPayloadKind.Wav).Select(p => p.Wav!).ToArray();
        var mp3 = payloads.Where(p => p.Kind == SoundPayloadKind.Mp3).Select(p => p.Mp3!).ToArray();
        Assert.AreEqual((706, 2704), (wav.Length, mp3.Length));
        Assert.AreEqual(1, wav.Count(w => w.IgnoredTailBytes > 0), "one effect carries unrelated bytes after its samples");
        Assert.AreEqual(1, wav.Sum(w => w.Loops.Count));
        Assert.IsTrue(mp3.All(m => (m.Version, m.SampleRate) == (1, 44100)));
        Assert.AreEqual(26, keys.Count(k => k.StartsWith("sound/music/", StringComparison.Ordinal)));
    }
}
