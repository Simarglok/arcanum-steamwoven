using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-094 (MIG7c, TM2, GR10): where a map NPC was left is the object state of its directory entry (the field <c>npc.pos</c>), saved
/// in the objects section and read back by the identity, so a load stands the NPC where it was.</summary>
[TestClass]
public sealed class NpcPlacementStateTests
{
    private const string Guard = "oid:guard";

    [TestMethod]
    public void ThePackingRoundTripsAndRefusesWhatDoesNotFit()
    {
        foreach (var facing in Enum.GetValues<NeighborDirection>())
        {
            var value = SessionNpcPlacement.Encode(new CellCoord(1409, 1314), facing);
            Assert.AreEqual((new CellCoord(1409, 1314), facing), SessionNpcPlacement.Decode(value));
        }
        var edge = new CellCoord((int)(SessionNpcPlacement.Axis - 1), 0);
        Assert.AreEqual((edge, NeighborDirection.YMinus), SessionNpcPlacement.Decode(SessionNpcPlacement.Encode(edge, NeighborDirection.YMinus)));
        Assert.IsFalse(SessionNpcPlacement.Fits(new CellCoord(-1, 0), NeighborDirection.XPlus));
        Assert.IsFalse(SessionNpcPlacement.Fits(new CellCoord(0, (int)SessionNpcPlacement.Axis), NeighborDirection.XPlus));
        Assert.IsFalse(SessionNpcPlacement.Fits(new CellCoord(0, 0), (NeighborDirection)9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SessionNpcPlacement.Encode(new CellCoord(-1, 0), NeighborDirection.XPlus));
        Assert.IsNull(SessionNpcPlacement.Decode(-1));
        Assert.IsNull(SessionNpcPlacement.Decode(SessionNpcPlacement.Axis << 32));
    }

    [TestMethod]
    public void APlaceIsRecordedByTheNpcsIdentityOnTheMapThatListsIt()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryNpcPlace(Guard));
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(1409, 1314), NeighborDirection.XPlus), owner, 1);
        Assert.AreEqual(new SessionNpcPlace(Guard, Exterior, new CellCoord(1409, 1314), NeighborDirection.XPlus), owner.QueryNpcPlace(Guard));
        CollectionAssert.AreEqual(new[] { new SessionNpcPlace(Guard, Exterior, new CellCoord(1409, 1314), NeighborDirection.XPlus) }, owner.QueryNpcPlaces(Exterior).ToArray());
        Assert.AreEqual(0, owner.QueryNpcPlaces(Cave).Count, "another map lists none");
        // It is the NPC's own field, among its entry's state, apart from a companion's cell.
        CollectionAssert.AreEqual(new[] { "npc.pos" }, owner.QueryObjectState(Guard).Select(field => field.Property).ToArray());
        Assert.IsNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity(Guard, "npc.cell")));

        // The same place again is no change and no revision; a step and a turn are changes of the one record.
        var version = owner.Version;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(1409, 1314), NeighborDirection.XPlus).Disposition);
        Assert.AreEqual(version, owner.Version);
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(1410, 1314), NeighborDirection.XPlus), owner, 2);
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(1410, 1314), NeighborDirection.YPlus), owner, 3);
        Assert.AreEqual(1, owner.QueryNpcPlaces(Exterior).Count);
        Assert.AreEqual(new CellCoord(1410, 1314), owner.QueryNpcPlace(Guard)!.Value.World);
        Assert.AreEqual(NeighborDirection.YPlus, owner.QueryNpcPlace(Guard)!.Value.Facing);

        // Places come back by entity, ordinally.
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, "oid:baker", new CellCoord(5, 6), NeighborDirection.XMinus), owner, 4);
        CollectionAssert.AreEqual(new[] { "oid:baker", Guard }, owner.QueryNpcPlaces(Exterior).Select(place => place.Entity).ToArray());
    }

    [TestMethod]
    public void ARefusedPlaceChangesNothing()
    {
        using var owner = Create();
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(-1, 5), NeighborDirection.XPlus).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.RecordNpcPlace(owner.Version, Exterior, "bad id", new CellCoord(1, 5), NeighborDirection.XPlus).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.RecordNpcPlace(owner.Version, Exterior, "sector:5:2", new CellCoord(1, 5), NeighborDirection.XPlus).Failure, "a static object has no place");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RecordNpcPlace(new GameSessionVersion(owner.Version.Owner, owner.Version.Revision + 1), Exterior, Guard, new CellCoord(1, 5), NeighborDirection.XPlus).Failure);
        Assert.AreEqual(version, owner.Version);
        Assert.IsNull(owner.QueryNpcPlace(Guard));
        // The budget of a map counts the place like any change.
        using var tight = Create(new GameSessionLimits(maxChangesPerMap: 1));
        Changed(tight.RecordNpcPlace(tight.Version, Exterior, Guard, new CellCoord(1, 5), NeighborDirection.XPlus), tight, 1);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, tight.RecordNpcPlace(tight.Version, Exterior, "oid:baker", new CellCoord(1, 5), NeighborDirection.XPlus).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, tight.RecordNpcPlace(tight.Version, Exterior, Guard, new CellCoord(2, 5), NeighborDirection.XPlus).Disposition,
            "the NPC's own record is not a second change");
    }

    [TestMethod]
    public void APlaceSurvivesASaveAndALoad()
    {
        using var owner = Create();
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(1410, 1314), NeighborDirection.YPlus), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"schemaVersion\":81,");
        StringAssert.Contains(text, "\"state\":[{\"entity\":\"oid:guard\",\"property\":\"npc.pos\",\"map\":79,\"value\":");
        Assert.AreEqual(0, owner.Capture().Maps.Count, "the place is an object field, not a map change");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(new SessionNpcPlace(Guard, Exterior, new CellCoord(1410, 1314), NeighborDirection.YPlus), restored.QueryNpcPlace(Guard));
        CollectionAssert.AreEqual(owner.QueryNpcPlaces(Exterior).ToArray(), restored.QueryNpcPlaces(Exterior).ToArray());
    }

    [TestMethod]
    public void ARestoreRefusesAPlaceThatIsNotAnObjectsOwn()
    {
        using var owner = Create();
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(1, 5), NeighborDirection.XPlus), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var value = SessionNpcPlacement.Encode(new CellCoord(1, 5), NeighborDirection.XPlus);
        StringAssert.Contains(text, $"\"value\":{value}}}");
        // A place left in the maps section is a damaged schema 64 save (it is an object field).
        var inMaps = text.Replace("\"maps\":[]", $"\"maps\":[{{\"map\":79,\"changes\":[{{\"scope\":\"entity\",\"entity\":\"oid:guard\",\"property\":\"npc.pos\",\"value\":{value}}}]}}]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(inMaps));
        // A static object is no walking NPC, so it has no place.
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"entity\":\"oid:guard\"", "\"entity\":\"sector:5:2\"")));
    }

    [TestMethod]
    public void ASchema63SaveUpgradesWithNoPlace()
    {
        using var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("oid:npc", "npc.cell"), 7), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema63(text);
        StringAssert.Contains(older, "\"schemaVersion\":63,");
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(upgraded), "nothing moves: the same text with the new schema number");
        using var restored = Restore(upgraded);
        Assert.AreEqual(0, restored.QueryNpcPlaces(Exterior).Count);
        // The step does not repair: a schema 63 save without the objects state is damaged.
        var damaged = older.Replace(",\"state\":[{", ",\"rows\":[{");
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(damaged).Failure);
    }

    private static GameSessionFailure RestoreFailure(string text) =>
        GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot, RestoreOptions()).Failure;
}
