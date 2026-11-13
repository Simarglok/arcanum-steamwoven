using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-WORLD-1525 (ledger VB3-1, VBG14, 0x4AD0B0): the bed an NPC lies in is an object field of its directory entry (<c>npc.bed</c>), written and removed by the host, saved with the session; an older save holds none. Ids and maps are invented.
/// </summary>
[TestClass]
public sealed class NpcBedStateTests
{
    private const string Virgil = "oid:virgil";

    [TestMethod]
    public void The_code_names_a_static_bed_by_its_sector_and_ordinal_and_never_zero()
    {
        Assert.AreEqual(1L, SessionNpcBed.Code(0, 0));
        Assert.AreEqual(8L, SessionNpcBed.Code(0, 7));
        Assert.AreEqual(((5L << 20) | 3) + 1, SessionNpcBed.Code(5, 3));
        Assert.AreNotEqual(SessionNpcBed.Code(1, 0), SessionNpcBed.Code(0, 1));
        Assert.IsNull(SessionNpcBed.Code(-1, 0));
        Assert.IsNull(SessionNpcBed.Code(0, -1));
        Assert.IsNull(SessionNpcBed.Code(0, 1 << 20));
        Assert.IsNull(SessionNpcBed.Code(1L << 42, 0));
    }

    [TestMethod]
    public void A_bed_is_written_replaced_and_removed_as_an_object_field()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryNpcBed(Virgil));
        Changed(owner.WriteNpcBed(owner.Version, Virgil, Exterior, 8), owner, 1);
        Assert.AreEqual(8L, owner.QueryNpcBed(Virgil));
        CollectionAssert.AreEquivalent(new[] { "npc.bed" }, owner.QueryObjectState(Virgil).Select(field => field.Property).ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteNpcBed(owner.Version, Virgil, Exterior, 8).Disposition);
        Changed(owner.WriteNpcBed(owner.Version, Virgil, Exterior, 9), owner, 2);
        Assert.AreEqual(9L, owner.QueryNpcBed(Virgil));
        Changed(owner.WriteNpcBed(owner.Version, Virgil, Cave, null), owner, 3);
        Assert.IsNull(owner.QueryNpcBed(Virgil), "removed, wherever the record sat");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteNpcBed(owner.Version, Virgil, Exterior, null).Disposition);
        Assert.AreEqual(0, owner.Capture().Maps.Count, "an object field, not a map change");
    }

    [TestMethod]
    public void The_bed_survives_a_save_and_a_load_and_an_older_save_has_none()
    {
        using var owner = Create();
        Changed(owner.WriteNpcBed(owner.Version, Virgil, Exterior, 8), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"property\":\"npc.bed\",\"map\":79,\"value\":8");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(8L, restored.QueryNpcBed(Virgil));
        using var plain = Restore(Decode(GameSessionSnapshotCodec.Encode(Create().Capture())));
        Assert.IsNull(plain.QueryNpcBed(Virgil));
    }

    [TestMethod]
    public void A_code_of_a_far_sector_survives_a_save_and_the_record_moves_with_the_map_it_is_written_on()
    {
        using var owner = Create();
        var far = SessionNpcBed.Code((1290L << 26) | 1452, 3) ?? throw new AssertFailedException("code");
        Changed(owner.WriteNpcBed(owner.Version, Virgil, Exterior, far), owner, 1);
        Changed(owner.WriteNpcBed(owner.Version, Virgil, Cave, far - 1), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(text, "npc\\.bed").Count, "one record whatever map it names");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(far - 1, restored.QueryNpcBed(Virgil));
    }

    [TestMethod]
    public void A_refused_write_changes_nothing()
    {
        using var owner = Create();
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteNpcBed(owner.Version, "bad id", Exterior, 8).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteNpcBed(owner.Version, "sector:5:2", Exterior, 8).Failure, "a static object has none");
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteNpcBed(owner.Version, Virgil, Exterior, 0).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.WriteNpcBed(owner.Version, Virgil, new SessionMapId(0), 8).Failure);
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteNpcBed(new GameSessionVersion(version.Owner, version.Revision + 5), Virgil, Exterior, 8).Failure);
    }
}
