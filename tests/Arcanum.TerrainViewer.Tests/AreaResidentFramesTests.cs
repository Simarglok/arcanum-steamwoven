using Arcanum.Data.Area;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-375: the resident frame store a map load serves its frames from.</summary>
[TestClass]
public sealed class AreaResidentFramesTests
{
    private static (AreaDataOwner Owner, AreaSnapshot Snapshot, AreaScenePlan Plan) Open(AreaOwnerFixture fixture)
    {
        var snapshot = fixture.Owner.LoadStrict();
        return (fixture.Owner, snapshot, AreaScenePlan.Create(snapshot));
    }

    [TestMethod]
    public void A_second_copy_is_served_from_the_store_without_a_batch_and_keeps_the_pixels_and_the_order()
    {
        using var fixture = new AreaOwnerFixture();
        var (owner, snapshot, plan) = Open(fixture);
        var store = new AreaResidentFrames(); var limits = new AreaLoaderOptions();
        var first = AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, default, limits, store);
        Assert.IsTrue(first.Count > 0);
        Assert.AreEqual(first.Count, store.Count);
        var usage = owner.Usage;
        var second = AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, default, limits, store);
        Assert.AreEqual(usage, owner.Usage, "an all-hit copy requests, decodes and copies nothing");
        CollectionAssert.AreEqual(first.Keys.ToArray(), second.Keys.ToArray());
        foreach (var key in first.Keys) Assert.AreSame(first[key], second[key]);
        Assert.AreEqual((long)first.Count, store.Lookups.Hits);
        using (var other = new AreaOwnerFixture())
        {
            var foreign = Assert.Throws<AreaException>(() => AreaLoadOperation.CopyMapFrames(other.Owner, snapshot, plan, default, limits, store));
            Assert.AreEqual(AreaErrorCode.SnapshotMismatch, foreign.Issue.Code, "an all-hit copy still refuses a snapshot its owner did not publish");
        }
        // The same frames read without a store are the same pixels.
        var plain = AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, default, limits, null);
        foreach (var key in first.Keys) CollectionAssert.AreEqual(first[key].Rgba, plain[key].Rgba);
        Assert.AreEqual(first.Count, store.Count, "a copy without a store leaves the store alone");
    }

    [TestMethod]
    public void A_partly_resident_copy_reads_only_the_other_frames()
    {
        using var fixture = new AreaOwnerFixture();
        var (owner, snapshot, plan) = Open(fixture);
        var all = AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, default, new(), null);
        Assert.IsTrue(all.Count >= 2);
        var store = new AreaResidentFrames();
        var kept = all.Keys.First();
        store.Add(kept, all[kept]);
        var before = owner.Usage.FrameRequests;
        var mixed = AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, default, new(), store);
        Assert.AreEqual(all.Count - 1, owner.Usage.FrameRequests - before);
        Assert.AreSame(all[kept], mixed[kept]);
        CollectionAssert.AreEqual(all.Keys.ToArray(), mixed.Keys.ToArray());
        foreach (var key in all.Keys) CollectionAssert.AreEqual(all[key].Rgba, mixed[key].Rgba);
        Assert.AreEqual(all.Count, store.Count, "the frames that were read are added");
    }

    [TestMethod]
    public void A_cancelled_copy_adds_no_frame_and_a_size_mismatch_is_a_miss()
    {
        using var fixture = new AreaOwnerFixture();
        var (owner, snapshot, plan) = Open(fixture);
        var store = new AreaResidentFrames();
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, cancel.Token, new(), store));
        }
        Assert.AreEqual(0, store.Count);
        var frames = AreaLoadOperation.CopyMapFrames(owner, snapshot, plan, default, new(), store);
        var key = frames.Keys.First(); var frame = frames[key];
        Assert.IsTrue(store.TryGet(key, frame.Width, frame.Height, out _));
        Assert.IsFalse(store.TryGet(key, frame.Width + 1, frame.Height, out _), "a descriptor of another size is not served the kept frame");
    }

    [TestMethod]
    public void The_store_is_bounded_by_bytes_and_by_count_and_clears()
    {
        var key = (int n) => new AreaFrameKey(new(new(AreaSourceRole.Resource1), n, 0, 1, 1, 0, "n"), 0, 0, 0, "p", "payload" + n, 1, "rgba" + n);
        var frame = () => new DecodedAreaFrame(1, 1, new byte[4]);
        var store = new AreaResidentFrames(maxBytes: 12, maxFrames: 100);
        for (var n = 0; n < 5; n++) store.Add(key(n), frame());
        Assert.AreEqual(3, store.Count); Assert.AreEqual(12, store.Bytes);
        Assert.IsFalse(store.TryGet(key(0), 1, 1, out _), "the oldest went first"); Assert.IsTrue(store.TryGet(key(4), 1, 1, out _));
        store.Clear();
        Assert.AreEqual(0, store.Count); Assert.AreEqual(0, store.Bytes);
        var counted = new AreaResidentFrames(maxBytes: 1024, maxFrames: 2);
        for (var n = 0; n < 4; n++) counted.Add(key(n), frame());
        Assert.AreEqual(2, counted.Count);
    }
}
