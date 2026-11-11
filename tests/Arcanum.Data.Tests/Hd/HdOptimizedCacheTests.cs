using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

/// <summary>ARC-DEV-376: the optimized texture cache store and the pack calls the viewer's cache path uses (synthetic frames only).</summary>
[TestClass]
public sealed class HdOptimizedCacheTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hdc-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Sha(string seed) => SyntheticHdPack.Sha(seed);

    private static byte[] Chain(int w, int h, byte fill = 7) => Enumerable.Repeat(fill, (int)HdOptimizedCache.MipBytes(w, h)).ToArray();

    [TestMethod]
    public void Levels_and_bytes_follow_the_godot_chain_down_to_one_texel()
    {
        Assert.AreEqual(3, HdOptimizedCache.MipLevels(4, 4));
        Assert.AreEqual(4 * 4 * 4 + 2 * 2 * 4 + 4, HdOptimizedCache.MipBytes(4, 4));
        Assert.AreEqual(3, HdOptimizedCache.MipLevels(5, 3));
        Assert.AreEqual(5 * 3 * 4 + 2 * 1 * 4 + 4, HdOptimizedCache.MipBytes(5, 3));
        Assert.AreEqual(1, HdOptimizedCache.MipLevels(1, 1));
        Assert.AreEqual(4, HdOptimizedCache.MipBytes(1, 1));
        Assert.AreEqual(11, HdOptimizedCache.MipLevels(1180, 900));
    }

    [TestMethod]
    public void A_written_chain_reads_back_and_a_wrong_size_or_name_is_not_an_entry()
    {
        var cache = new HdOptimizedCache(_root);
        var sha = Sha("a");
        Assert.IsFalse(cache.Has(sha, 8, 6));
        Assert.IsNull(cache.TryRead(sha, 8, 6));
        var chain = Chain(8, 6);
        chain[3] = 99;
        Assert.IsTrue(cache.Write(sha, 8, 6, chain));
        Assert.IsTrue(cache.Has(sha, 8, 6));
        CollectionAssert.AreEqual(chain, cache.TryRead(sha, 8, 6));
        Assert.IsNull(cache.TryRead(sha, 6, 8), "another size");
        Assert.IsFalse(cache.Has(sha, 16, 12));
        // a copy under another name is not that PNG's entry (the header names its source)
        var other = Sha("b");
        Directory.CreateDirectory(Path.GetDirectoryName(cache.PathFor(other))!);
        File.Copy(cache.PathFor(sha), cache.PathFor(other));
        Assert.IsNull(cache.TryRead(other, 8, 6));
        Assert.IsFalse(cache.Write(sha, 8, 6, new byte[10]), "not a whole chain");
        Assert.ThrowsExactly<ArgumentException>(() => cache.PathFor("../x"));
    }

    [TestMethod]
    public void A_truncated_garbage_or_other_version_file_is_not_an_entry_and_can_be_replaced()
    {
        var cache = new HdOptimizedCache(_root);
        var sha = Sha("c");
        Assert.IsTrue(cache.Write(sha, 4, 4, Chain(4, 4)));
        var path = cache.PathFor(sha);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^1]);
        Assert.IsFalse(cache.Has(sha, 4, 4));
        File.WriteAllBytes(path, [.. bytes, 0]);
        Assert.IsNull(cache.TryRead(sha, 4, 4));
        var versioned = (byte[])bytes.Clone(); versioned[4] = 2;
        File.WriteAllBytes(path, versioned);
        Assert.IsFalse(cache.Has(sha, 4, 4));
        File.WriteAllBytes(path, "garbage"u8.ToArray());
        Assert.IsFalse(cache.Has(sha, 4, 4));
        Assert.IsTrue(cache.Write(sha, 4, 4, Chain(4, 4, 9)), "an invalid file is replaced");
        Assert.IsTrue(cache.TryRead(sha, 4, 4)!.All(b => b == 9));
        Assert.AreEqual(0, Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.part").Count());
    }

    [TestMethod]
    public void Remove_unreferenced_drops_what_no_png_names_any_more_and_partial_files()
    {
        var cache = new HdOptimizedCache(_root);
        string keep = Sha("keep"), gone = Sha("gone");
        Assert.IsTrue(cache.Write(keep, 2, 2, Chain(2, 2)));
        Assert.IsTrue(cache.Write(gone, 2, 2, Chain(2, 2)));
        File.WriteAllBytes(cache.PathFor(keep) + ".1-1.part", [1]);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "stays");
        Assert.AreEqual(2, cache.RemoveUnreferenced(new HashSet<string> { keep }));
        Assert.IsTrue(cache.Has(keep, 2, 2));
        Assert.IsFalse(cache.Has(gone, 2, 2));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "notes.txt")));
        Assert.IsTrue(cache.SizeBytes() > 0);
    }

    private (HdPack Pack, SyntheticHdPack.Frame Frame, byte[] Hd) InterfacePack(int tint = 0)
    {
        var recipe = new SyntheticHdPack(Path.Combine(_root, "ref"), 2).RecipeText.Replace("\"kinds\":[\"critter\",\"scenery\"]", "\"kinds\":[\"interface\"]");
        var pack = new SyntheticHdPack(Path.Combine(_root, "pack"), 2, recipe);
        var frame = SyntheticHdPack.Legacy("panel", 6, 5, seed: 3);
        var hd = SyntheticHdPack.Hd(frame, 2, tint);
        pack.Add(frame, hd);
        var result = HdPack.Open(pack.Write());
        Assert.AreEqual(HdPackOpenStatus.Opened, result.Status, result.Detail);
        return (result.Pack!, frame, hd);
    }

    [TestMethod]
    public void The_pack_names_the_file_of_a_frame_decodes_it_and_checks_prepared_pixels_like_a_resolve()
    {
        var (pack, frame, hd) = InterfacePack();
        var entry = pack.Entries.Single();
        Assert.AreEqual(entry.Sha256, pack.SpriteFileSha256(frame.Query));
        Assert.AreSame(entry, pack.InterfaceSprites.Single());
        CollectionAssert.AreEqual(hd, pack.DecodeFrame(entry, out var problem));
        Assert.AreEqual(HdInvalidReason.None, problem);

        var ok = pack.CheckPrepared(frame.Query, frame.Rgba, hd);
        Assert.AreEqual((HdFrameStatus.Hd, 12, 10), (ok.Status, ok.Width, ok.Height));
        Assert.AreEqual(HdInvalidReason.SizeMismatch, pack.CheckPrepared(frame.Query, frame.Rgba, hd[..^4]).Invalid);
        var opaque = hd.ToArray();
        for (var i = 3; i < opaque.Length; i += 4) opaque[i] = 255; // fills the Legacy holes: the legacy alpha rule refuses it, as ResolveSprite would
        Assert.AreEqual((HdFrameStatus.Invalid, HdInvalidReason.AlphaMismatch), (pack.CheckPrepared(frame.Query, frame.Rgba, opaque).Status, pack.CheckPrepared(frame.Query, frame.Rgba, opaque).Invalid));
        var file = Path.Combine(pack.Directory, entry.File.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(file, "x");
        Assert.IsNull(pack.SpriteFileSha256(frame.Query), "a pack file of another size is not bound from the cache");
        File.Delete(file);
        Assert.IsNull(pack.SpriteFileSha256(frame.Query), "a missing pack file is not bound from the cache");
        var unknown = SyntheticHdPack.Legacy("other", 6, 5, seed: 4);
        Assert.IsNull(pack.SpriteFileSha256(unknown.Query));
        Assert.AreEqual(HdFrameStatus.FrameMissing, pack.CheckPrepared(unknown.Query, unknown.Rgba, hd).Status);
    }

    [TestMethod]
    public void A_pack_without_interface_slices_lists_no_interface_sprites()
    {
        var pack = new SyntheticHdPack(Path.Combine(_root, "world"), 2);
        var frame = SyntheticHdPack.Legacy("tree", 4, 4, seed: 1);
        pack.Add(frame);
        var opened = HdPack.Open(pack.Write()).Pack!;
        Assert.AreEqual(0, opened.InterfaceSprites.Count());
    }

    [TestMethod]
    public void An_alpha_pass_is_remembered_by_png_legacy_frame_and_policy_and_survives_a_new_cache_object()
    {
        string png = Sha("png"), legacy = Sha("legacy"), policy = "LegacySmooth:900";
        var cache = new HdOptimizedCache(_root);
        Assert.IsFalse(cache.AlphaVerdictKnown(png, legacy, policy));
        Assert.IsTrue(cache.RecordAlphaVerdict(png, legacy, policy));
        Assert.IsTrue(cache.RecordAlphaVerdict(png, legacy, policy), "twice is one record");
        Assert.AreEqual(24, new FileInfo(Path.Combine(_root, HdOptimizedCache.VerdictFile)).Length);
        Assert.IsTrue(cache.AlphaVerdictKnown(png, legacy, policy));
        Assert.IsFalse(cache.AlphaVerdictKnown(png, Sha("other legacy"), policy), "another Legacy frame");
        Assert.IsFalse(cache.AlphaVerdictKnown(png, legacy, "Legacy:1000"), "another policy");
        Assert.IsFalse(cache.AlphaVerdictKnown(Sha("other png"), legacy, policy), "another PNG");
        Assert.IsTrue(new HdOptimizedCache(_root).AlphaVerdictKnown(png, legacy, policy), "read back by a later run");
    }

    [TestMethod]
    public void A_torn_verdict_tail_is_cut_and_stale_verdicts_go_with_their_png()
    {
        string keep = Sha("keep"), gone = Sha("gone"), legacy = Sha("legacy");
        var cache = new HdOptimizedCache(_root);
        Assert.IsTrue(cache.RecordAlphaVerdict(keep, legacy, "Soft:900"));
        Assert.IsTrue(cache.RecordAlphaVerdict(gone, legacy, "Soft:900"));
        var path = Path.Combine(_root, HdOptimizedCache.VerdictFile);
        File.WriteAllBytes(path, [.. File.ReadAllBytes(path), 1, 2, 3]); // a crash in the middle of a record
        var later = new HdOptimizedCache(_root);
        Assert.IsTrue(later.AlphaVerdictKnown(keep, legacy, "Soft:900"));
        Assert.AreEqual(48, new FileInfo(path).Length, "the torn tail is cut back to a record boundary");
        Assert.IsTrue(later.RecordAlphaVerdict(keep, legacy, "Soft:800"));
        Assert.IsTrue(new HdOptimizedCache(_root).AlphaVerdictKnown(keep, legacy, "Soft:800"), "a record after the cut tail is not shifted");
        later.RemoveUnreferenced(new HashSet<string> { keep });
        Assert.IsTrue(later.AlphaVerdictKnown(keep, legacy, "Soft:900"));
        Assert.IsFalse(later.AlphaVerdictKnown(gone, legacy, "Soft:900"));
        Assert.IsFalse(new HdOptimizedCache(_root).AlphaVerdictKnown(gone, legacy, "Soft:900"));
        Assert.AreEqual(48, new FileInfo(path).Length);
    }

    [TestMethod]
    public void A_remembered_pass_skips_the_alpha_pass_and_a_failed_one_is_never_remembered()
    {
        var (pack, frame, hd) = InterfacePack();
        var cache = new HdOptimizedCache(_root + "-cache");
        try
        {
            var opaque = hd.ToArray();
            for (var i = 3; i < opaque.Length; i += 4) opaque[i] = 255;
            Assert.AreEqual(HdInvalidReason.AlphaMismatch, pack.CheckPrepared(frame.Query, frame.Rgba, opaque, cache).Invalid);
            Assert.IsFalse(File.Exists(Path.Combine(cache.Root, HdOptimizedCache.VerdictFile)), "a refusal leaves no verdict");
            Assert.AreEqual(HdFrameStatus.Hd, pack.CheckPrepared(frame.Query, frame.Rgba, hd, cache).Status);
            Assert.IsTrue(File.Exists(Path.Combine(cache.Root, HdOptimizedCache.VerdictFile)), "a pass is recorded");
            // The caller vouches that the pixels are the PNG's: once that PNG passed, the per-texel pass is not repeated (these pixels would fail it).
            Assert.AreEqual(HdFrameStatus.Hd, pack.CheckPrepared(frame.Query, frame.Rgba, opaque, cache).Status);
            Assert.AreEqual(HdInvalidReason.AlphaMismatch, pack.CheckPrepared(frame.Query, frame.Rgba, opaque, new HdOptimizedCache(_root + "-other")).Invalid, "another cache has no verdict");
            Assert.AreEqual(HdInvalidReason.AlphaMismatch, pack.CheckPrepared(frame.Query, frame.Rgba, opaque).Invalid, "without a cache every call checks");
            // size, key and Legacy checks still run with a verdict present
            Assert.AreEqual(HdInvalidReason.SizeMismatch, pack.CheckPrepared(frame.Query, frame.Rgba, hd[..^4], cache).Invalid);
            Assert.ThrowsExactly<ArgumentException>(() => pack.CheckPrepared(frame.Query, new byte[frame.Rgba.Length], hd, cache));
        }
        finally
        {
            foreach (var dir in new[] { _root + "-cache", _root + "-other" }) if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Verdicts_stay_correct_under_concurrent_records_from_two_cache_objects_on_one_folder()
    {
        var a = new HdOptimizedCache(_root);
        var b = new HdOptimizedCache(_root);
        Parallel.For(0, 64, i => (i % 2 == 0 ? a : b).RecordAlphaVerdict(Sha("p" + i), Sha("l"), "Legacy:1000"));
        var fresh = new HdOptimizedCache(_root);
        for (var i = 0; i < 64; i++) Assert.IsTrue(fresh.AlphaVerdictKnown(Sha("p" + i), Sha("l"), "Legacy:1000"), "png " + i);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.part").Length);
    }

    // Pins the behaviour of the alpha rules next to HdAlphaCheck.Version: a remembered pass is only as good as the rule that gave it, so a change of any rule
    // that changes this string must bump Version (and then the expected string here).
    [TestMethod]
    public void The_alpha_rules_keep_their_outcomes_until_the_version_is_bumped()
    {
        const int W = 6, H = 5, S = 2;
        var legacy = new byte[W * H * 4];
        for (var i = 0; i < W * H; i++) { legacy[i * 4 + 3] = (byte)((i % 7 == 0 || i % 11 == 3) ? 0 : 255); legacy[i * 4] = (byte)i; }
        byte[] Hd(Func<int, int, byte, byte> alpha)
        {
            var hd = new byte[W * S * H * S * 4];
            for (var y = 0; y < H * S; y++) for (var x = 0; x < W * S; x++)
                hd[(y * W * S + x) * 4 + 3] = alpha(x, y, legacy[((y / S) * W + x / S) * 4 + 3]);
            return hd;
        }
        var variants = new[]
        {
            Hd((x, y, a) => a),                                        // exact parent alpha
            Hd((x, y, a) => 255),                                      // everything opaque
            Hd((x, y, a) => a == 0 ? (byte)0 : (byte)(x % 2 == 0 ? 255 : 100)), // soft inside the silhouette
            Hd((x, y, a) => (byte)(a == 0 && x % S == 0 ? 255 : a)),    // growth outside the silhouette
            Hd((x, y, a) => 0),                                        // nothing
            Hd((x, y, a) => (byte)(a == 255 && x % S == 1 ? 200 : a)),  // translucent half of every opaque texel
            Hd((x, y, a) => (byte)(a == 255 && x % 5 == 0 ? 0 : a)),    // holes cut in the silhouette
        };
        var text = string.Concat(new[] { HdAlphaMode.Legacy, HdAlphaMode.Soft, HdAlphaMode.LegacySmooth, HdAlphaMode.Masked }.Select(mode =>
            string.Concat(variants.Select(hd => HdAlphaCheck.Matches(hd, legacy, W, H, S, new HdAlphaPolicy(mode, 900)) ? '1' : '0')) + "|"));
        Assert.AreEqual("1000000|1000010|1000000|1111111|", text, "alpha rules changed: bump HdAlphaCheck.Version and update this string");
    }
}
