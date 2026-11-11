using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DEV-375: the resident LRU store and the resident visual memo a map plan's builds share.</summary>
[TestClass]
public sealed class AreaResidentVisualsTests
{
    [TestMethod]
    public void Lru_evicts_the_least_recently_used_entry_by_cost_and_by_count()
    {
        var store = new ResidentLru<string, int>(maxCost: 10, maxCount: 3);
        Assert.IsTrue(store.Add("a", 1, 4)); Assert.IsTrue(store.Add("b", 2, 4));
        Assert.IsTrue(store.TryGet("a", out _), "touching a makes b the oldest");
        Assert.IsTrue(store.Add("c", 3, 4));
        Assert.IsFalse(store.TryGet("b", out _)); Assert.IsTrue(store.TryGet("a", out _)); Assert.IsTrue(store.TryGet("c", out _));
        Assert.AreEqual(8, store.Cost); Assert.AreEqual(2, store.Count);
        // The count bound: three cheap entries stay, a fourth evicts the oldest.
        store.Clear();
        foreach (var key in new[] { "w", "x", "y", "z" }) store.Add(key, 0, 1);
        Assert.AreEqual(3, store.Count); Assert.IsFalse(store.TryGet("w", out _)); Assert.IsTrue(store.TryGet("z", out _));
    }

    [TestMethod]
    public void Lru_refuses_a_value_over_the_cost_bound_replaces_equal_keys_and_clears_its_counters()
    {
        var store = new ResidentLru<string, string>(maxCost: 8, maxCount: 8);
        Assert.IsFalse(store.Add("big", "x", 9)); Assert.AreEqual(0, store.Count);
        Assert.IsTrue(store.Add("k", "one", 3)); Assert.IsTrue(store.Add("k", "two", 5));
        Assert.AreEqual(1, store.Count); Assert.AreEqual(5, store.Cost);
        Assert.IsTrue(store.TryGet("k", out var value)); Assert.AreEqual("two", value);
        Assert.IsFalse(store.TryGet("missing", out _));
        Assert.AreEqual((1L, 1L), store.Lookups);
        store.Clear();
        Assert.AreEqual(0, store.Count); Assert.AreEqual(0, store.Cost); Assert.AreEqual((0L, 0L), store.Lookups);
        Throws<ArgumentOutOfRangeException>(() => new ResidentLru<int, int>(0, 1));
        Throws<ArgumentOutOfRangeException>(() => new ResidentLru<int, int>(1, 0));
    }

    [TestMethod]
    public void A_later_build_serves_its_visuals_from_the_memo_and_matches_a_build_without_one()
    {
        var memo = new AreaResidentVisuals();
        AreaVisualRecord[] cold;
        using (var plain = new Fixture([(67108865, [Record(Id(0))])])) cold = plain.Owner.LoadStrict().Visuals.ToArray();
        AreaVisualRecord[] first, second; long firstRequests, secondRequests;
        using (var one = new Fixture([(67108865, [Record(Id(0))])], limits: new() { ResidentVisuals = memo }))
        {
            first = one.Owner.LoadStrict().Visuals.ToArray(); firstRequests = one.Owner.Usage.FrameRequests;
        }
        Assert.IsTrue(memo.Count > 0, "a complete build commits its records");
        Assert.AreEqual(0, memo.Lookups.Hits);
        using (var two = new Fixture([(67108865, [Record(Id(0))])], limits: new() { ResidentVisuals = memo }))
        {
            second = two.Owner.LoadStrict().Visuals.ToArray(); secondRequests = two.Owner.Usage.FrameRequests;
        }
        Assert.IsTrue(memo.Lookups.Hits > 0, "the second build meets the first's selections");
        CollectionAssert.AreEqual(cold, first); CollectionAssert.AreEqual(cold, second);
        Assert.IsTrue(firstRequests > 0 && secondRequests < firstRequests, $"served selections are not decoded again ({firstRequests} then {secondRequests})");
    }

    [TestMethod]
    public void A_source_with_other_bytes_does_not_hit_the_memo()
    {
        var memo = new AreaResidentVisuals();
        using (var one = new Fixture([(67108865, [Record(Id(0))])], limits: new() { ResidentVisuals = memo })) one.Owner.LoadStrict();
        var committed = memo.Count; var hits = memo.Lookups.Hits;
        Assert.IsTrue(committed > 0);
        // The same selections out of a resource archive whose ART bytes differ: another source SHA-256, so nothing is served.
        var changed = Art(); changed[1185] ^= 1;
        byte[] tile = System.Text.Encoding.ASCII.GetBytes("{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}");
        byte[] facade = System.Text.Encoding.ASCII.GetBytes("{0}{fixture}"), scenery = System.Text.Encoding.ASCII.GetBytes("{0}{fixture.art}");
        (string, byte[])[] entries = [("tile.mes", tile), ("facade.mes", facade), ("scenery.mes", scenery), ("art/tile/stonebse0a.art", changed), ("art/scenery/fixture.art", changed)];
        using (var two = new Fixture([(67108865, [Record(Id(0))])], limits: new() { ResidentVisuals = memo }, resourceEntries: entries)) two.Owner.LoadStrict();
        Assert.AreEqual(hits, memo.Lookups.Hits, "no selection of the changed archive was served from the memo");
        Assert.IsTrue(memo.Count > committed, "the changed archive's records are kept under their own identity");
    }

    [TestMethod]
    public void Frames_of_a_served_build_are_the_same_pixels_as_those_of_a_cold_build()
    {
        var memo = new AreaResidentVisuals();
        byte[][] Frames(AreaLoaderOptions limits)
        {
            using var fixture = new Fixture([(67108865, [Record(Id(0))])], limits: limits);
            var snapshot = fixture.Owner.LoadStrict();
            using var batch = fixture.Owner.BeginFrameBatch(snapshot);
            var copies = snapshot.Visuals.Select(v => batch.CopySelectedFrame(v.Key)).ToArray();
            batch.Complete();
            return copies;
        }
        var cold = Frames(new());
        _ = Frames(new() { ResidentVisuals = memo });
        var served = Frames(new() { ResidentVisuals = memo });
        Assert.IsTrue(memo.Lookups.Hits > 0);
        Assert.AreEqual(cold.Length, served.Length);
        for (var i = 0; i < cold.Length; i++) CollectionAssert.AreEqual(cold[i], served[i]);
    }

    private static byte[] GuidId(byte first) { var id = Id(2); for (var i = 0; i < 16; i++) id[8 + i] = (byte)(first + i); return id; }

    [TestMethod]
    public void A_later_build_serves_the_editor_records_from_the_memo_and_matches_a_build_without_one()
    {
        byte[][] records = [Record(GuidId(0), 274877907008), Record(GuidId(16), 274877907009)];
        var memo = new AreaResidentEditorRecords();
        AreaReconciliation cold;
        using (var plain = new Fixture([(67108865, [])], records)) cold = plain.Owner.LoadStrict().Reconciliation;
        using (var one = new Fixture([(67108865, [])], records, limits: new() { ResidentEditorRecords = memo }))
            Assert.AreEqual(cold, one.Owner.LoadStrict().Reconciliation);
        Assert.AreEqual(1, memo.Count, "a complete build commits its pass"); Assert.AreEqual(2, memo.Facts); Assert.AreEqual(0, memo.Lookups.Hits);
        using (var two = new Fixture([(67108865, [])], records, limits: new() { ResidentEditorRecords = memo }))
        {
            var served = two.Owner.LoadStrict();
            Assert.AreEqual(1, memo.Lookups.Hits, "the second build meets the first's editor records");
            Assert.AreEqual(cold, served.Reconciliation);
            Assert.AreEqual(2, served.Objects.Count);
        }
    }

    [TestMethod]
    public void Editor_records_of_another_module_are_not_served_and_a_mismatch_still_fails_and_commits_nothing()
    {
        byte[][] records = [Record(GuidId(0), 274877907008), Record(GuidId(16), 274877907009)];
        var memo = new AreaResidentEditorRecords();
        using (var one = new Fixture([(67108865, [])], records, limits: new() { ResidentEditorRecords = memo })) one.Owner.LoadStrict();
        // Another module (an editor record that is not the aggregate's): another identity, so the pass is read again and the mismatch is found.
        using (var bad = new Fixture([(67108865, [])], records, [records[0], Record(GuidId(32))], limits: new() { ResidentEditorRecords = memo }))
        {
            var report = bad.Owner.Inspect();
            Assert.IsFalse(report.IsComplete);
            Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.InputIdentityMismatch && i.Stage == AreaStage.Correspondence));
        }
        Assert.AreEqual(0, memo.Lookups.Hits, "the other module did not hit"); Assert.AreEqual(1, memo.Count, "a failed pass commits nothing");
        // A served pass is still compared with the aggregate: the same module bytes against another aggregate fail on a hit.
        using (var other = new Fixture([(67108865, [])], [records[0], Record(GuidId(48))], records, limits: new() { ResidentEditorRecords = memo }))
        {
            Assert.IsFalse(other.Owner.Inspect().IsComplete);
        }
        Assert.AreEqual(1, memo.Count);
    }

    [TestMethod]
    public void A_build_that_is_cancelled_or_whose_input_changed_commits_nothing()
    {
        var memo = new AreaResidentVisuals();
        Hook? resource = null;
        using (var fixture = new Fixture([(67108865, [Record(Id(0))])], limits: new() { ResidentVisuals = memo },
            streamFactory: (key, bytes) => { var stream = new Hook(bytes); if (key.Role == AreaSourceRole.Resource1) resource = stream; return stream; }))
        {
            using var cancel = new CancellationTokenSource();
            resource!.OnRead = () => cancel.Cancel();
            Throws<OperationCanceledException>(() => fixture.Owner.Inspect(cancel.Token));
            Assert.AreEqual(0, memo.Count, "a cancelled build commits nothing");
            // A changed input fails the closing authentication (or the build) and commits nothing either.
            resource.OnRead = () => { resource.OnRead = null; resource.Bytes[0] ^= 1; };
            var report = fixture.Owner.Inspect();
            Assert.IsFalse(report.IsComplete);
            Assert.AreEqual(0, memo.Count, "a build that did not authenticate commits nothing");
            resource.Bytes[0] ^= 1;
            Assert.IsTrue(fixture.Owner.Inspect().IsComplete);
            Assert.IsTrue(memo.Count > 0);
        }
    }

    private sealed class Hook : MemoryStream
    {
        internal Hook(byte[] bytes) : base(bytes, false) { Bytes = bytes; }
        internal byte[] Bytes { get; }
        internal Action? OnRead { get; set; }
        public override int Read(byte[] buffer, int offset, int count) { OnRead?.Invoke(); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { OnRead?.Invoke(); return base.Read(buffer); }
    }
}
