using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-SCRIPT-1164 (E5-b, ledger WO5): a map's blocked-sector list (the original's <c>map.sbf</c>) is sector-scope map changes, so it is saved with the session without a schema step. A script's toggle sector blocked
/// (106) writes it and the world-map route reads it. Sector ids and maps are invented.
/// </summary>
[TestClass]
public sealed class SectorBlockedStateTests
{
    [TestMethod]
    public void A_sector_is_blocked_while_its_change_is_set_and_the_list_is_the_maps()
    {
        using var owner = Create();
        Assert.IsFalse(owner.QuerySectorBlocked(Exterior, 12, 34));
        Changed(owner.SetSectorBlocked(owner.Version, Exterior, 12, 34, true), owner, 1);
        Assert.IsTrue(owner.QuerySectorBlocked(Exterior, 12, 34));
        Assert.IsTrue(owner.QuerySectorBlocked(Exterior, 12L, 34L), "the world map's 64-bit numbers");
        Assert.IsFalse(owner.QuerySectorBlocked(Cave, 12, 34), "another map's list is its own");
        Assert.IsFalse(owner.QuerySectorBlocked(Exterior, 34, 12), "x and y are not exchangeable");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetSectorBlocked(owner.Version, Exterior, 12, 34, true).Disposition, "already blocked");
        Changed(owner.SetSectorBlocked(owner.Version, Exterior, 1, 2, true), owner, 2);
        CollectionAssert.AreEqual(new[] { (1, 2), (12, 34) }, owner.QueryBlockedSectors(Exterior).OrderBy(sector => sector.Y).ToArray());
        Changed(owner.SetSectorBlocked(owner.Version, Exterior, 12, 34, false), owner, 3);
        Assert.IsFalse(owner.QuerySectorBlocked(Exterior, 12, 34));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetSectorBlocked(owner.Version, Exterior, 12, 34, false).Disposition, "already free");
        CollectionAssert.AreEqual(new[] { (1, 2) }, owner.QueryBlockedSectors(Exterior).ToArray());
    }

    [TestMethod]
    public void The_packed_id_is_the_originals_and_a_negative_sector_is_refused()
    {
        // 0x4CFC50: y shifted up by 26 bits ORed with the low 26 bits of x.
        Assert.AreEqual((34L << 26) | 12, SessionSectorBlock.Pack(12, 34));
        Assert.AreEqual((12, 34), SessionSectorBlock.Unpack(SessionSectorBlock.Pack(12, 34)!.Value));
        Assert.IsNull(SessionSectorBlock.Pack(-1, 0));
        // An x beyond the 26 packed bits would alias another sector, so it is refused like a negative one.
        Assert.IsNull(SessionSectorBlock.Pack(0x4000000, 3));
        Assert.IsNotNull(SessionSectorBlock.Pack(0x3FFFFFF, 3));
        using var owner = Create();
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.SetSectorBlocked(owner.Version, Exterior, -2, 5, true).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.SetSectorBlocked(owner.Version, new SessionMapId(0), 1, 2, true).Failure);
        Assert.IsFalse(owner.QuerySectorBlocked(Exterior, -2, 5));
        Assert.IsFalse(owner.QuerySectorBlocked(Exterior, (long)int.MaxValue + 1, 5));
        Assert.AreEqual(version, owner.Version);
    }

    [TestMethod]
    public void The_blocked_list_is_saved_and_restored_without_a_new_schema()
    {
        using var owner = Create();
        Changed(owner.SetSectorBlocked(owner.Version, Exterior, 12, 34, true), owner, 1);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.IsTrue(restored.QuerySectorBlocked(Exterior, 12, 34));
        CollectionAssert.AreEqual(new[] { (12, 34) }, restored.QueryBlockedSectors(Exterior).ToArray());
    }
}
