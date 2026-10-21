using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-056: the PC's known world-map areas and last discovered area in the story (ledger WT2, WT3).
[TestClass]
public sealed class AreaStateTests
{
    [TestMethod]
    public void Discovering_an_area_knows_it_once_and_records_it_as_the_last()
    {
        using var owner = Create();
        Assert.IsFalse(owner.IsAreaKnown(4));
        Changed(owner.DiscoverArea(owner.Version, 4), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DiscoverArea(owner.Version, 4).Disposition);
        Changed(owner.DiscoverArea(owner.Version, 2), owner, 2);
        CollectionAssert.AreEqual(new[] { 2, 4 }, owner.QueryKnownAreas().ToArray());
        Assert.AreEqual(2, owner.QueryLastDiscoveredArea());
        Changed(owner.ClearLastDiscoveredArea(owner.Version), owner, 3);
        Assert.AreEqual(0, owner.QueryLastDiscoveredArea());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearLastDiscoveredArea(owner.Version).Disposition);
        Rejected(owner.DiscoverArea(owner.Version, 0), owner, GameSessionFailure.InvalidArea, 3);
        Rejected(owner.DiscoverArea(owner.Version, SessionStoryLimits.LastArea + 1), owner, GameSessionFailure.InvalidArea, 3);
        Assert.IsFalse(owner.IsAreaKnown(0));
    }

    [TestMethod]
    public void Known_areas_survive_a_save_and_a_bad_record_is_refused()
    {
        using var owner = Create();
        Changed(owner.DiscoverArea(owner.Version, 11), owner, 1);
        Changed(owner.DiscoverArea(owner.Version, 3), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"knownAreas\":[3,11],\"lastArea\":3");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(new[] { 3, 11 }, restored.QueryKnownAreas().ToArray());
        Assert.AreEqual(3, restored.QueryLastDiscoveredArea());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // Not ascending, out of range, or a last area the PC does not know.
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(text.Replace("[3,11]", "[11,3]")).Failure);
        Assert.AreNotEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(text.Replace("[3,11]", "[0,3]")).Failure);
        var unknownLast = GameSessionSnapshotCodec.TryDecode(text.Replace("\"lastArea\":3", "\"lastArea\":5"));
        Assert.AreEqual(SnapshotDecodeFailure.None, unknownLast.Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(unknownLast.Snapshot!, RestoreOptions()).Failure);
    }
}
