using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-AI-1314 (ledger VA18, VA19, VAG3, WS4, WS9): an NPC's waypoint index (the original's field 0x120) and standpoint latch (NPC flag 0x200) are object fields of its directory entry
/// (<c>npc.route.index</c>, <c>npc.route.latch</c>), written by one route step in one revision with the step's teleport map, and saved with the session. A value equal to the map row's own leaves no record. Ids and maps are invented.
/// </summary>
[TestClass]
public sealed class NpcRouteStateTests
{
    private const string Virgil = "oid:virgil";

    [TestMethod]
    public void One_step_writes_the_index_the_latch_and_the_teleport_map_in_one_revision()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryRouteIndex(Virgil));
        Assert.IsNull(owner.QueryStandpointLatch(Virgil));
        var before = owner.Version;
        Changed(owner.WriteRouteStep(owner.Version, Virgil, Exterior, index: 2, dataIndex: 0, latch: true, dataLatch: false, ensureTeleportMap: true), owner, 1);
        Assert.AreEqual(2, owner.QueryRouteIndex(Virgil));
        Assert.AreEqual(true, owner.QueryStandpointLatch(Virgil));
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Virgil));
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision for the three writes");
        CollectionAssert.AreEquivalent(new[] { "npc.route.index", "npc.route.latch", "npc.teleport" }, owner.QueryObjectState(Virgil).Select(field => field.Property).ToArray());
        Assert.AreEqual(0, owner.Capture().Maps.Count, "object fields, not map changes");
    }

    [TestMethod]
    public void A_value_equal_to_the_rows_own_leaves_no_record_and_removes_one()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteRouteStep(owner.Version, Virgil, Exterior, index: 1, dataIndex: 1, latch: false, dataLatch: false).Disposition);
        Assert.AreEqual(0, owner.QueryObjectState(Virgil).Count);
        Changed(owner.WriteRouteStep(owner.Version, Virgil, Exterior, index: 2, dataIndex: 1), owner, 1);
        Changed(owner.WriteRouteStep(owner.Version, Virgil, Cave, index: 1, dataIndex: 1), owner, 2);
        Assert.IsNull(owner.QueryRouteIndex(Virgil), "back at the row's index: the record is gone, wherever it sat");
        // A latch the row's own bit sets: clearing it is a record of 0, setting it again removes the record.
        Changed(owner.WriteRouteStep(owner.Version, Virgil, Exterior, latch: false, dataLatch: true), owner, 3);
        Assert.AreEqual(false, owner.QueryStandpointLatch(Virgil));
        Changed(owner.WriteRouteStep(owner.Version, Virgil, Exterior, latch: true, dataLatch: true), owner, 4);
        Assert.IsNull(owner.QueryStandpointLatch(Virgil));
    }

    [TestMethod]
    public void The_route_state_survives_a_save_and_a_load_and_an_older_save_has_none()
    {
        using var owner = Create();
        Changed(owner.WriteRouteStep(owner.Version, Virgil, Exterior, index: 3, dataIndex: 0, latch: true, dataLatch: false), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"property\":\"npc.route.index\",\"map\":79,\"value\":3");
        StringAssert.Contains(text, "\"property\":\"npc.route.latch\",\"map\":79,\"value\":1");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(3, restored.QueryRouteIndex(Virgil));
        Assert.AreEqual(true, restored.QueryStandpointLatch(Virgil));
        // A save without either field (every older save) loads with none: the row's index and bit stand.
        using var plain = Restore(Decode(GameSessionSnapshotCodec.Encode(Create().Capture())));
        Assert.IsNull(plain.QueryRouteIndex(Virgil));
        Assert.IsNull(plain.QueryStandpointLatch(Virgil));
    }

    [TestMethod]
    public void A_refused_write_changes_nothing()
    {
        using var owner = Create();
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteRouteStep(owner.Version, "bad id", Exterior, index: 1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteRouteStep(owner.Version, "sector:5:2", Exterior, index: 1).Failure, "a static object has none");
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteRouteStep(owner.Version, Virgil, Exterior, index: -1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.WriteRouteStep(owner.Version, Virgil, new SessionMapId(0), index: 1).Failure);
        Assert.AreEqual(version, owner.Version);
    }
}
