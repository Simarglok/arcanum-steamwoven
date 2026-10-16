using System.Text.Json.Nodes;
using Arcanum.Data.Area;
using Arcanum.Data.Hd;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-014: the Godot-free HD path — frame questions, the scaled crop and its
/// axis limit, the nearest diagnostic pack, the asynchronous frame source with every per-frame
/// refusal, and the per-domain residency (draw-order admission, retain limits, upload
/// batches). Every frame is a synthetic pattern written to a temporary directory.</summary>
[TestClass]
public sealed class HdPresentationTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => _root = Directory.CreateTempSubdirectory("arc-hd-render-").FullName;

    [TestCleanup]
    public void Remove() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class FakeTexture(int width, int height, byte[] rgba)
    {
        public int Width { get; } = width; public int Height { get; } = height; public byte[] Rgba { get; } = rgba;
        public bool Disposed { get; set; }
    }

    private static byte[] Pattern(int width, int height, int seed)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            if ((x * 3 + y * 5 + seed) % 7 == 0) continue; // transparent hole
            rgba[at] = (byte)(x * 29 + seed); rgba[at + 1] = (byte)(y * 13 + seed); rgba[at + 2] = (byte)(x ^ y ^ seed); rgba[at + 3] = 255;
        }
        return rgba;
    }

    private static HdNearestPack.Frame Frame(string name, int width, int height, int seed, int group = 0, int frame = 0, int palette = 0, HdReviewStatus review = HdReviewStatus.Unreviewed)
    {
        var rgba = Pattern(width, height, seed);
        var query = HdFrameQueries.FromArt(HdFrameQueries.Sha256(System.Text.Encoding.UTF8.GetBytes("art:" + name)), 4096,
            HdFrameQueries.Sha256(System.Text.Encoding.UTF8.GetBytes("name:" + name)), group, frame, palette, width, height, rgba);
        return new(query, rgba, review);
    }

    private HdPack WritePack(string id, int scale, IEnumerable<HdNearestPack.Frame> frames, HdReviewStatus review = HdReviewStatus.Unreviewed)
    {
        var directory = HdNearestPack.Write(Path.Combine(_root, id), id, scale, frames, review);
        var open = HdPack.Open(directory);
        Assert.AreEqual(HdPackOpenStatus.Opened, open.Status, open.Detail);
        return open.Pack!;
    }

    private static void Drain(HdFrameSource source)
    {
        var deadline = Environment.TickCount64 + 20_000;
        while (source.PendingCount > 0)
        {
            if (Environment.TickCount64 > deadline) Assert.Fail("HD frame source did not finish");
            Thread.Sleep(5);
        }
    }

    private static HdResidency<FakeTexture> Residency(HdDomainBudget budget, int uploads = 64)
        => new((w, h, rgba) => new FakeTexture(w, h, rgba), texture => texture.Disposed = true, budget, uploads);

    private static HdDraw Draw(HdNearestPack.Frame frame) => new(frame.Query, () => frame.Rgba);

    [TestMethod]
    public void ScaledCropAndAxisLimit()
    {
        Assert.AreEqual(new TerrainRect(8, 12, 64, 40), HdCrop.Scale(new TerrainRect(2, 3, 16, 10), 4));
        Assert.AreEqual(new TerrainRect(2, 3, 16, 10), HdCrop.Scale(new TerrainRect(2, 3, 16, 10), 1));
        // N·(c + m) ≤ 65534 on each axis: 16,383 fits at 4×, 16,384 does not; 32,767 fits at 2×.
        Assert.IsTrue(HdCrop.Fits(new TerrainRect(0, 0, 16383, 10), 4));
        Assert.IsFalse(HdCrop.Fits(new TerrainRect(0, 0, 16384, 10), 4));
        Assert.IsFalse(HdCrop.Fits(new TerrainRect(1, 0, 16383, 10), 4));
        Assert.IsFalse(HdCrop.Fits(new TerrainRect(0, 0, 10, 16384), 4));
        Assert.IsTrue(HdCrop.Fits(new TerrainRect(0, 0, 32767, 32767), 2));
        Assert.IsFalse(HdCrop.Fits(new TerrainRect(0, 0, 32768, 1), 2));
    }

    [TestMethod]
    public void AreaQuestionComesFromTheFrameKey()
    {
        var entry = new AreaEntryIdentity(new(AreaSourceRole.Resource2), 7, 0, 1, 2, 3, new string('b', 64));
        var key = new AreaFrameKey(entry, 2, 5, 9, "synthetic", new string('a', 64), 4096, new string('c', 64));
        var query = HdFrameQueries.FromArea(key, new DecodedAreaFrame(3, 4, new byte[48]));
        Assert.AreEqual(new HdSpriteKey(new string('a', 64), 4096, 5, 9, 2), query.Key);
        Assert.AreEqual(new string('b', 64), query.NameSha256);
        Assert.AreEqual(new HdLegacyFrame(3, 4, new string('c', 64)), query.Legacy);
        var rgba = Pattern(3, 4, 1);
        var art = HdFrameQueries.FromArt(new string('a', 64), 4096, new string('b', 64), 1, 2, 3, 3, 4, rgba);
        Assert.AreEqual(HdFrameQueries.Sha256(rgba), art.Legacy.RgbaSha256);
        Assert.ThrowsExactly<ArgumentException>(() => HdFrameQueries.FromArt(new string('a', 64), 4096, new string('b', 64), 0, 0, 0, 3, 4, new byte[47]));
        var identity = HdFrameQueries.ArtIdentity(new byte[] { 1, 2, 3 }, new byte[] { 65 });
        Assert.AreEqual(3, identity.ViewLength);
        Assert.AreEqual(HdFrameQueries.Sha256(new byte[] { 65 }), identity.NameSha256);
    }

    [TestMethod]
    public void NearestPackRoundTripsAtTwoAndFour()
    {
        foreach (var scale in new[] { 2, 4 })
        {
            var frames = new[] { Frame("a", 5, 3, 1), Frame("b", 7, 9, 2, group: 3, frame: 11, palette: 1) };
            var pack = WritePack($"nearest-{scale}", scale, frames);
            Assert.AreEqual(scale, pack.Scale);
            foreach (var frame in frames)
            {
                var result = pack.ResolveSprite(frame.Query, frame.Rgba);
                Assert.AreEqual(HdFrameStatus.Hd, result.Status);
                CollectionAssert.AreEqual(HdNearestPack.Replicate(frame.Rgba, frame.Query.Legacy.Width, frame.Query.Legacy.Height, scale), result.Rgba);
            }
        }
        // Every HD texel is its parent's; the tint changes colour only, never alpha.
        var legacy = Pattern(2, 2, 0);
        var hd = HdNearestPack.Replicate(legacy, 2, 2, 4, tintColumn: 1, tint: 0xFF);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var parent = ((y / 4) * 2 + x / 4) * 4; var at = (y * 8 + x) * 4;
            Assert.AreEqual(legacy[parent + 3], hd[at + 3]);
            Assert.AreEqual(x % 4 == 1 && legacy[parent + 3] != 0 ? (byte)(legacy[parent] ^ 0xFF) : legacy[parent], hd[at]);
        }
    }

    [TestMethod]
    public void FrameSourceRefusesPerFrameWithTypedReasons()
    {
        using (var none = new HdFrameSource(null))
        {
            var frame = Frame("x", 4, 4, 1);
            Assert.AreEqual(new HdSourceStatus(HdSourceState.Refused, HdFallbackReason.NoPack), none.Request(frame.Query, () => frame.Rgba));
            Assert.AreEqual(1, none.Scale);
        }
        var good = Frame("good", 6, 5, 1);
        var rejected = Frame("rejected", 6, 5, 2, review: HdReviewStatus.Rejected);
        var tampered = Frame("tampered", 6, 5, 3);
        var pack = WritePack("reasons", 4, [good, rejected, tampered]);
        // Damage the third frame's file after the pack was written: Invalid (hash).
        var file = pack.Entries[2].File;
        var path = Path.Combine([pack.Directory, .. file.Split('/')]);
        var bytes = File.ReadAllBytes(path); bytes[^20] ^= 0x55; File.WriteAllBytes(path, bytes);
        using var source = new HdFrameSource(pack, 2);
        Assert.AreEqual(4, source.Scale);
        var otherArt = new HdSpriteQuery(good.Query.Key with { ArtSha256 = new string('d', 64) }, good.Query.NameSha256, good.Query.Legacy);
        var missing = Frame("missing", 6, 5, 4);
        var otherPixels = new HdSpriteQuery(good.Query.Key, good.Query.NameSha256, new HdLegacyFrame(6, 5, missing.Query.Legacy.RgbaSha256));
        Assert.AreEqual(HdSourceState.Unknown, source.Peek(good.Query).State);
        Assert.AreEqual(HdSourceState.Pending, source.Request(good.Query, () => good.Rgba).State);
        source.Request(rejected.Query, () => rejected.Rgba); source.Request(tampered.Query, () => tampered.Rgba);
        source.Request(otherArt, () => good.Rgba); source.Request(missing.Query, () => missing.Rgba);
        source.Request(otherPixels, () => missing.Rgba);
        Drain(source);
        var ready = source.Peek(good.Query);
        Assert.AreEqual(HdSourceState.Ready, ready.State);
        CollectionAssert.AreEqual(HdNearestPack.Replicate(good.Rgba, 6, 5, 4), ready.Rgba);
        Assert.AreEqual(HdFallbackReason.FrameRejected, source.Peek(rejected.Query).Reason);
        Assert.AreEqual(HdFallbackReason.Invalid, source.Peek(tampered.Query).Reason);
        Assert.AreEqual(HdFallbackReason.SourceMismatch, source.Peek(otherArt).Reason, "known ART name, other ART bytes");
        Assert.AreEqual(HdFallbackReason.FrameMissing, source.Peek(missing.Query).Reason);
        Assert.AreEqual(HdFallbackReason.SourceMismatch, source.Peek(otherPixels).Reason, "same key, other Legacy pixels");
        Assert.AreEqual(6, source.CompletedCount);
        Assert.AreEqual(4L * 6 * 4 * 5 * 4, source.ReadyBytes);
        source.Release(good.Query);
        Assert.AreEqual(HdSourceState.Unknown, source.Peek(good.Query).State, "released pixels are decoded again on request");
        Assert.AreEqual(HdFallbackReason.FrameRejected, source.Peek(rejected.Query).Reason, "refusals are kept");
        source.Reset();
        Assert.AreEqual(HdSourceState.Unknown, source.Peek(rejected.Query).State);

        var rejectedPack = WritePack("rejected-pack", 2, [Frame("r", 3, 3, 1)], HdReviewStatus.Rejected);
        using var rejectedSource = new HdFrameSource(rejectedPack);
        Assert.AreEqual(HdFallbackReason.PackRejected, rejectedSource.Request(good.Query, () => good.Rgba).Reason);
    }

    [TestMethod]
    public void ResetDropsWorkThatFinishesLater()
    {
        var frames = Enumerable.Range(0, 24).Select(i => Frame("f" + i, 40, 30, i)).ToArray();
        using var source = new HdFrameSource(WritePack("reset", 4, frames), 1);
        foreach (var frame in frames) source.Request(frame.Query, () => frame.Rgba);
        source.Reset();
        Thread.Sleep(200);
        Assert.AreEqual(0, source.PendingCount);
        Assert.AreEqual(0, source.ReadyCount, "no staging buffer outlives a reset");
        source.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => source.Request(frames[0].Query, () => frames[0].Rgba));
    }

    [TestMethod]
    public void ResidencyBindsInDrawOrderAndSharesTextures()
    {
        var a = Frame("a", 8, 6, 1); var b = Frame("b", 4, 4, 2);
        using var source = new HdFrameSource(WritePack("bind", 4, [a, b]), 2);
        using var residency = Residency(new HdDomainBudget(1 << 30, 512, 1 << 30));
        HdDraw[] draws = [HdDraw.None, Draw(a), Draw(b), Draw(a), new(null, null, HdFallbackReason.HdAxisLimit)];
        var first = residency.Submit(draws, source);
        Assert.IsFalse(first[0].Candidate);
        Assert.AreEqual(HdFallbackReason.Pending, first[1].Reason);
        Assert.AreEqual(HdFallbackReason.Pending, first[3].Reason, "a repeated question shares its state");
        Assert.AreEqual(HdFallbackReason.HdAxisLimit, first[4].Reason);
        Drain(source);
        var second = residency.Submit(draws, source);
        Assert.IsTrue(second[1].IsHd && second[2].IsHd);
        Assert.AreSame(second[1].Texture, second[3].Texture);
        Assert.AreEqual(32, second[1].Texture!.Width); Assert.AreEqual(24, second[1].Texture!.Height);
        CollectionAssert.AreEqual(HdNearestPack.Replicate(a.Rgba, 8, 6, 4), second[1].Texture!.Rgba);
        Assert.AreEqual(2, residency.Count); Assert.AreEqual(2, residency.VisibleFrames);
        Assert.AreEqual(16L * (8 * 6 + 4 * 4) * 4, residency.Bytes);
        Assert.AreEqual(0, source.ReadyCount, "uploaded pixels are released by the source");
        var third = residency.Submit(draws, source);
        Assert.AreSame(second[1].Texture, third[1].Texture, "resident textures are reused");
        Assert.AreEqual(2, residency.CreatedCount);
        residency.Clear();
        Assert.IsTrue(second[1].Texture!.Disposed && second[2].Texture!.Disposed);
        Assert.AreEqual(0, residency.Count); Assert.AreEqual(0, residency.Bytes);
    }

    [TestMethod]
    public void ResidencyBudgetIsDeterministicInDrawOrder()
    {
        var a = Frame("a", 8, 8, 1); var b = Frame("b", 8, 8, 2); var c = Frame("c", 2, 2, 3);
        using var source = new HdFrameSource(WritePack("budget", 2, [a, b, c]), 2);
        // a and b are 1,024 HD bytes each at 2×, c 64: the limit admits a, refuses b, admits c.
        using var residency = Residency(new HdDomainBudget(1024 + 64, 512, 1 << 20));
        var first = residency.Submit([Draw(a), Draw(b), Draw(c)], source);
        Assert.AreEqual(HdFallbackReason.Pending, first[0].Reason);
        Assert.AreEqual(HdFallbackReason.HdBudget, first[1].Reason, "refused by size before it is decoded");
        Assert.AreEqual(HdFallbackReason.Pending, first[2].Reason);
        Drain(source);
        Assert.AreEqual(HdSourceState.Unknown, source.Peek(b.Query).State, "a refused frame is never decoded");
        var second = residency.Submit([Draw(a), Draw(b), Draw(c)], source);
        Assert.IsTrue(second[0].IsHd); Assert.AreEqual(HdFallbackReason.HdBudget, second[1].Reason); Assert.IsTrue(second[2].IsHd);
        // Another order admits b first: admission follows the draw order only.
        var swapped = residency.Submit([Draw(b), Draw(a), Draw(c)], source);
        Assert.AreEqual(HdFallbackReason.Pending, swapped[0].Reason); Assert.AreEqual(HdFallbackReason.HdBudget, swapped[1].Reason);

        using var off = Residency(new HdDomainBudget(0, 0, 0));
        using var offSource = new HdFrameSource(source.Pack);
        var none = off.Submit([Draw(a), Draw(c)], offSource);
        Assert.IsTrue(none.All(binding => binding.Reason == HdFallbackReason.HdBudget), "a 0 budget turns HD off");
        Assert.AreEqual(0, offSource.PendingCount);
    }

    [TestMethod]
    public void ResidencyRetainsLeastRecentlyUsedWithinLimits()
    {
        var frames = Enumerable.Range(0, 4).Select(i => Frame("r" + i, 4, 4, i)).ToArray();
        using var source = new HdFrameSource(WritePack("retain", 2, frames), 2);
        // Each frame is 256 HD bytes at 2×: keep at most 2 frames after they leave the view.
        using var residency = Residency(new HdDomainBudget(1 << 20, 2, 1 << 20));
        HdBinding<FakeTexture>[] Show(params int[] which)
        {
            var draws = which.Select(i => Draw(frames[i])).ToArray();
            residency.Submit(draws, source); Drain(source);
            return residency.Submit(draws, source);
        }
        var t0 = Show(0)[0].Texture!; var t1 = Show(1)[0].Texture!; var t2 = Show(2)[0].Texture!;
        Assert.AreEqual(3, residency.Count, "two retained plus one visible");
        var t3 = Show(3)[0].Texture!;
        Assert.IsTrue(t0.Disposed, "the least recently used frame went first");
        Assert.IsFalse(t1.Disposed || t2.Disposed || t3.Disposed);
        Assert.AreSame(t1, Show(1)[0].Texture, "a retained frame binds again without an upload");
        residency.ReleaseToRetained(source);
        Assert.AreEqual(2, residency.Count, "a switch to Legacy keeps the retain limit");
        Assert.AreEqual(0, residency.VisibleFrames);

        // WORLD: one byte limit for visible and kept frames.
        using var world = Residency(new HdDomainBudget(512, 10, 512, 512));
        var draws01 = new[] { Draw(frames[0]), Draw(frames[1]) };
        world.Submit(draws01, source); Drain(source); world.Submit(draws01, source);
        Assert.AreEqual(512, world.Bytes);
        var draws2 = new[] { Draw(frames[2]) };
        world.Submit(draws2, source); Drain(source); world.Submit(draws2, source);
        Assert.IsTrue(world.Bytes <= 512, "visible + retained never exceed the WORLD limit");
        Assert.AreEqual(2, world.Count);
    }

    [TestMethod]
    public void ResidencyUploadsInBoundedBatches()
    {
        var frames = Enumerable.Range(0, 3).Select(i => Frame("u" + i, 4, 4, i)).ToArray();
        using var source = new HdFrameSource(WritePack("batch", 2, frames), 3);
        using var residency = Residency(new HdDomainBudget(1 << 20, 0, 0), uploads: 1);
        var draws = frames.Select(Draw).ToArray();
        residency.Submit(draws, source); Drain(source);
        var first = residency.Submit(draws, source);
        Assert.AreEqual(1, first.Count(binding => binding.IsHd));
        Assert.AreEqual(2, first.Count(binding => binding.Reason == HdFallbackReason.Pending));
        Assert.IsTrue(residency.UploadsDeferred);
        residency.Submit(draws, source);
        var third = residency.Submit(draws, source);
        Assert.IsTrue(third.All(binding => binding.IsHd));
        Assert.IsFalse(residency.UploadsDeferred);
    }

    [TestMethod]
    public void StatsAndDomainBudgets()
    {
        var stats = new HdSubmissionStats();
        stats.Add(null); stats.Add(null); stats.Add(HdFallbackReason.Pending); stats.Add(HdFallbackReason.FrameMissing); stats.Add(HdFallbackReason.Pending);
        Assert.AreEqual("HD 2, Legacy 3 (FrameMissing 1, Pending 2)", stats.Format());
        stats.Clear();
        Assert.AreEqual("HD 0, Legacy 0", stats.Format());
        var ceiling = HdDeviceBudget.Ceiling(4);
        var area = HdDomainBudget.Area(ceiling);
        Assert.AreEqual(16L * 81_853_096, area.VisibleBytes);
        Assert.AreEqual(512, area.RetainFrames); Assert.AreEqual(512L * 1024 * 1024, area.RetainBytes);
        Assert.IsNull(area.TotalBytes);
        var world = HdDomainBudget.World(ceiling);
        Assert.AreEqual(1L << 30, world.VisibleBytes); Assert.AreEqual(1L << 30, world.TotalBytes);
        Assert.AreEqual(184 + 5, world.RetainFrames);
    }

    [TestMethod]
    public void NearestPackRefusesForeignPixels()
    {
        var frame = Frame("p", 3, 3, 1);
        var forged = frame with { Rgba = Pattern(3, 3, 2) };
        Assert.ThrowsExactly<ArgumentException>(() => HdNearestPack.Write(Path.Combine(_root, "forged"), "forged", 4, [forged]));
        var directory = HdNearestPack.Write(Path.Combine(_root, "once"), "once", 4, [frame, frame]);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))!;
        Assert.AreEqual(1, manifest["entries"]!.AsArray().Count, "a repeated question is written once");
        Assert.ThrowsExactly<IOException>(() => HdNearestPack.Write(directory, "once", 4, [frame]));
    }
}
