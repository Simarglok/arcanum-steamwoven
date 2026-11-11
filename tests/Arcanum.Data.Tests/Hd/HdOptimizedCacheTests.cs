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
}
