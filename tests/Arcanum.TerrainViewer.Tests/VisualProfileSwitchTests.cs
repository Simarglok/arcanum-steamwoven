using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-006: the Godot-free parts of the toggle protocol (ARC-RENDER-013) — the saved
/// profile codec, the generation/commit state machine with its bounded wait, and the choice of
/// the active pack among discovered ones. Packs are synthetic and written to a temporary
/// directory.</summary>
[TestClass]
public sealed class VisualProfileSwitchTests
{
    [TestMethod]
    public void Codec_round_trips_and_loads_anything_else_as_legacy()
    {
        Assert.AreEqual("{\"schema\":1,\"profile\":\"Legacy\"}", VisualProfileCodec.Serialize(VisualProfile.Legacy));
        Assert.AreEqual("{\"schema\":1,\"profile\":\"Hd\"}", VisualProfileCodec.Serialize(VisualProfile.Hd));
        foreach (var profile in Enum.GetValues<VisualProfile>())
            Assert.AreEqual(profile, VisualProfileCodec.DeserializeOrLegacy(VisualProfileCodec.Serialize(profile)));
        Assert.AreEqual(VisualProfile.Hd, VisualProfileCodec.DeserializeOrLegacy(" { \"profile\" : \"Hd\", \"schema\" : 1 } "));
        foreach (var text in new string?[]
                 {
                     null, "", "not json", "[]", "\"Hd\"", "{}", "{\"schema\":1}", "{\"profile\":\"Hd\"}",
                     "{\"schema\":2,\"profile\":\"Hd\"}", "{\"schema\":1.5,\"profile\":\"Hd\"}", "{\"schema\":\"1\",\"profile\":\"Hd\"}",
                     "{\"schema\":1,\"profile\":\"hd\"}", "{\"schema\":1,\"profile\":\"HD\"}", "{\"schema\":1,\"profile\":1}",
                     "{\"schema\":1,\"profile\":\"Hd\",\"pack\":\"x\"}", "{\"schema\":1,\"profile\":\"Hd\",\"profile\":\"Hd\"}",
                     "{\"schema\":1,\"profile\":\"Hd\"", "{\"schema\":1,\"profile\":null}",
                     "{\"schema\":1,\"profile\":\"Hd\uD800\"}", "{\"schema\":1,\"profile\":\"Hd\"}\uD800",
                 })
            Assert.AreEqual(VisualProfile.Legacy, VisualProfileCodec.DeserializeOrLegacy(text), text ?? "null");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => VisualProfileCodec.Serialize((VisualProfile)7));
    }

    [TestMethod]
    public void A_request_for_the_active_profile_is_a_no_op_and_spends_no_generation()
    {
        var change = new VisualProfileSwitch(VisualProfile.Legacy);
        Assert.AreEqual(VisualRequestOutcome.NoOp, change.Request(VisualProfile.Legacy, 10));
        Assert.AreEqual(0, change.Generation);
        Assert.IsNull(change.Pending);
        Assert.AreEqual(VisualProfile.Hd, change.Toggled);
        Assert.IsFalse(change.TryCommit(settled: true, 10_000));
        Assert.AreEqual(VisualProfile.Legacy, change.Active);
    }

    [TestMethod]
    public void The_active_profile_draws_until_every_visible_frame_is_settled()
    {
        var change = new VisualProfileSwitch(VisualProfile.Legacy);
        Assert.AreEqual(VisualRequestOutcome.Started, change.Request(VisualProfile.Hd, 1_000));
        Assert.AreEqual((VisualProfile?)VisualProfile.Hd, change.Pending);
        Assert.AreEqual(1, change.Generation);
        Assert.AreEqual(VisualProfile.Legacy, change.Toggled, "a toggle while HD is pending asks for Legacy back");
        // Not settled and inside the wait: Legacy keeps drawing.
        Assert.IsFalse(change.TryCommit(settled: false, 1_000));
        Assert.IsFalse(change.TryCommit(settled: false, 1_499));
        Assert.AreEqual(VisualProfile.Legacy, change.Active);
        // A repeated request for the pending profile changes nothing and keeps the wait.
        Assert.AreEqual(VisualRequestOutcome.NoOp, change.Request(VisualProfile.Hd, 1_400));
        Assert.AreEqual(1, change.Generation);
        Assert.AreEqual(1_000, change.RequestedAt);
        Assert.IsTrue(change.TryCommit(settled: true, 1_200));
        Assert.AreEqual(VisualProfile.Hd, change.Active);
        Assert.IsNull(change.Pending);
        Assert.AreEqual(VisualRequestOutcome.NoOp, change.Request(VisualProfile.Hd, 1_300));
    }

    [TestMethod]
    public void The_bounded_wait_commits_with_frames_still_pending()
    {
        var change = new VisualProfileSwitch(VisualProfile.Legacy);
        Assert.AreEqual(500, change.CommitWaitMilliseconds);
        change.Request(VisualProfile.Hd, 2_000);
        Assert.IsFalse(change.TimedOut(2_499));
        Assert.IsTrue(change.TimedOut(2_500));
        Assert.IsTrue(change.TryCommit(settled: false, 2_500));
        Assert.AreEqual(VisualProfile.Hd, change.Active);
        Assert.IsFalse(change.TimedOut(9_999), "nothing pending, nothing times out");

        var instant = new VisualProfileSwitch(VisualProfile.Hd, commitWaitMilliseconds: 0);
        instant.Request(VisualProfile.Legacy, 5);
        Assert.IsTrue(instant.TryCommit(settled: false, 5));
        Assert.AreEqual(VisualProfile.Legacy, instant.Active);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VisualProfileSwitch(VisualProfile.Legacy, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VisualProfileSwitch((VisualProfile)9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => instant.Request((VisualProfile)9, 0));
    }

    [TestMethod]
    public void A_newer_request_cancels_the_pending_switch()
    {
        var change = new VisualProfileSwitch(VisualProfile.Legacy);
        change.Request(VisualProfile.Hd, 0);
        Assert.AreEqual(VisualRequestOutcome.Cancelled, change.Request(VisualProfile.Legacy, 100));
        Assert.AreEqual(2, change.Generation);
        Assert.IsNull(change.Pending);
        Assert.IsFalse(change.TryCommit(settled: true, 10_000), "a cancelled switch never commits");
        Assert.AreEqual(VisualProfile.Legacy, change.Active);
        // A new request starts a new wait.
        Assert.AreEqual(VisualRequestOutcome.Started, change.Request(VisualProfile.Hd, 700));
        Assert.AreEqual(3, change.Generation);
        Assert.IsFalse(change.TimedOut(1_100));
        Assert.IsTrue(change.TimedOut(1_200));
    }

    [TestMethod]
    public void Set_applies_at_once_and_drops_a_pending_switch()
    {
        var change = new VisualProfileSwitch(VisualProfile.Legacy);
        change.Set(VisualProfile.Legacy);
        Assert.AreEqual(0, change.Generation, "setting the active profile again changes nothing");
        change.Request(VisualProfile.Hd, 0);
        change.Set(VisualProfile.Legacy);
        Assert.AreEqual(2, change.Generation);
        Assert.IsNull(change.Pending);
        change.Set(VisualProfile.Hd);
        Assert.AreEqual(VisualProfile.Hd, change.Active);
        Assert.AreEqual(3, change.Generation);
    }

    [TestMethod]
    public void The_active_pack_is_the_first_usable_one_in_discovery_order()
    {
        var root = Directory.CreateTempSubdirectory("arc-ui-006-packs-").FullName;
        try
        {
            var rgba = new byte[3 * 2 * 4]; rgba[3] = 255;
            var frame = new HdNearestPack.Frame(HdFrameQueries.FromArt(new string('a', 64), 4096, new string('b', 64), 0, 0, 0, 3, 2, rgba), rgba);
            var collection = Path.Combine(root, "collection");
            HdNearestPack.Write(Path.Combine(collection, "a-rejected"), "a-rejected", 4, [frame], HdReviewStatus.Rejected);
            HdNearestPack.Write(Path.Combine(collection, "b-good"), "b-good", 2, [frame]);
            HdNearestPack.Write(Path.Combine(collection, "c-good"), "c-good", 4, [frame]);
            Directory.CreateDirectory(Path.Combine(collection, "d-broken"));
            File.WriteAllText(Path.Combine(collection, "d-broken", "manifest.json"), "{");
            var found = HdPack.Discover([new(HdPackLocationKind.Collection, collection)]);
            Assert.AreEqual(4, found.Count);
            Assert.AreEqual("b-good", HdActivePack.Choose(found)!.Result.Pack!.Id, "a rejected pack is skipped while a usable one exists");

            var onlyRejected = HdPack.Discover([new(HdPackLocationKind.Pack, Path.Combine(collection, "d-broken")), new(HdPackLocationKind.Pack, Path.Combine(collection, "a-rejected"))]);
            Assert.AreEqual("a-rejected", HdActivePack.Choose(onlyRejected)!.Result.Pack!.Id, "a rejected pack is still active when it is the only one opened");
            Assert.IsNull(HdActivePack.Choose(HdPack.Discover([new(HdPackLocationKind.Pack, Path.Combine(collection, "d-broken"))])));
            Assert.IsNull(HdActivePack.Choose([]));
            // Locations keep their order: an explicit pack before the collection wins.
            var ordered = HdPack.Discover([new(HdPackLocationKind.Pack, Path.Combine(collection, "c-good")), new(HdPackLocationKind.Collection, collection)]);
            Assert.AreEqual("c-good", HdActivePack.Choose(ordered)!.Result.Pack!.Id);
        }
        finally { Directory.Delete(root, true); }
    }
}
