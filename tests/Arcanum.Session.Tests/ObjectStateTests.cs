using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-092 (MIG7a, TM2, GR5): the fields of mobile map-data objects are the object's, found by its identity; schema 62 saves them
/// in the objects section and the 61 to 62 step moves the entity changes there.</summary>
[TestClass]
public sealed class ObjectStateTests
{
    private static readonly MapChangeKey Cell = MapChangeKey.ForEntity("oid:npc", "npc.cell");
    private static readonly MapChangeKey Away = MapChangeKey.ForEntity("oid:npc", "npc.map");
    private static readonly MapChangeKey Off = MapChangeKey.ForEntity("oid:crate", "obj.off");
    // A static object's entity is positional and its changes stay per map; a script attachment stays per map until MIG8.
    private static readonly MapChangeKey StaticLock = MapChangeKey.ForEntity("sector:5:2", "lock.flags");
    private static readonly MapChangeKey Dialog = MapChangeKey.ForEntity("oid:npc", "dialog.lf");
    private static readonly MapChangeKey Door = MapChangeKey.ForSector(5, "door.open");

    [TestMethod]
    public void AMobileObjectsFieldsAreItsOwnAndLeaveTheMapsSection()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, Cell, 7), new(Exterior, Away, 80), new(Exterior, Off, 1),
            new(Exterior, StaticLock, 3), new(Exterior, Dialog, 5), new(Exterior, Door, 1)]), owner, 1);

        var fields = owner.QueryObjectState("oid:npc");
        CollectionAssert.AreEqual(new[] { ("npc.cell", 79, 7L), ("npc.map", 79, 80L) },
            fields.Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray(), "by property, with the map the record names");
        Assert.AreEqual(0, owner.QueryObjectState("oid:nobody").Count);
        Assert.AreEqual(0, owner.QueryObjectState("sector:5:2").Count, "a static object's change is not an object field");
        Assert.AreEqual(0, owner.QueryObjectState("oid:npc").Count(field => field.Property == "dialog.lf"), "the script attachment waits for MIG8");

        var snapshot = owner.Capture();
        CollectionAssert.AreEqual(new[] { "oid:crate/obj.off", "oid:npc/npc.cell", "oid:npc/npc.map" },
            snapshot.Objects.State.Select(field => field.Entity + "/" + field.Property).ToArray(), "by entity then property");
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open"), Dialog, StaticLock },
            snapshot.Maps.Single().Changes.Select(change => change.Key).ToArray(), "the maps section keeps every other change");

        // The map query still lists the map's changes, the fields included, in canonical order; the identity query finds a field wherever it is.
        CollectionAssert.AreEqual(new[] { Door, Off, Cell, Away, StaticLock, Dialog }.OrderBy(key => key, MapChangeKeyComparer.Instance).ToArray(),
            owner.QueryMapChanges(Exterior).Select(change => change.Key).ToArray());
        CollectionAssert.AreEqual(new[] { (Exterior, 7L) }, owner.QueryEntityChanges("oid:npc", "npc.cell").ToArray());
        Assert.AreEqual((7L, (long?)null), (owner.QueryMapChange(Exterior, Cell)!.Value, owner.QueryMapChange(Cave, Cell)));
    }

    [TestMethod]
    public void TheFieldsSurviveASaveAndALoad()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Cave, Cell, 9), new(Exterior, Off, 1), new(Exterior, Door, 1)]), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"state\":[{\"entity\":\"oid:crate\",\"property\":\"obj.off\",\"map\":79,\"value\":1}," +
            "{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":80,\"value\":9}]}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual((9L, (long?)null, 1L), (restored.QueryMapChange(Cave, Cell)!.Value, restored.QueryMapChange(Exterior, Cell), restored.QueryMapChange(Exterior, Off)!.Value));
        Assert.AreEqual(1, restored.QueryObjectState("oid:npc").Count);
    }

    [TestMethod]
    public void ARecordIsOneWhateverMapItIsWrittenOn()
    {
        using var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Exterior, Cell, 7), owner, 1);
        // A write that names another map moves the record; the map it left no longer has it, the identity query finds it on the new one.
        Changed(owner.SetMapChange(owner.Version, Cave, Cell, 7), owner, 2);
        Assert.AreEqual(((long?)null, 7L), (owner.QueryMapChange(Exterior, Cell), owner.QueryMapChange(Cave, Cell)));
        Assert.AreEqual(0, owner.QueryMapChanges(Exterior).Count);
        CollectionAssert.AreEqual(new[] { (Cave, 7L) }, owner.QueryEntityChanges("oid:npc", "npc.cell").ToArray());
        Assert.AreEqual(1, owner.QueryObjectState("oid:npc").Count);
        // Clearing it on the map it left is nothing; on its own map it returns the field to its baseline.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearMapChange(owner.Version, Exterior, Cell).Disposition);
        Changed(owner.ClearMapChange(owner.Version, Cave, Cell), owner, 3);
        Assert.AreEqual(0, owner.QueryObjectState("oid:npc").Count);
        Assert.AreEqual(0, owner.Capture().Objects.State.Count);
    }

    [TestMethod]
    public void TheFieldsCountAgainstTheMapAndSessionBudgets()
    {
        using var perMap = Create(new GameSessionLimits(maxChangesPerMap: 1));
        Changed(perMap.SetMapChange(perMap.Version, Exterior, Cell, 1), perMap, 1);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameFailureOf(perMap.SetMapChange(perMap.Version, Exterior, Off, 1)), "the map holds one change, a field is one");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameFailureOf(perMap.SetMapChange(perMap.Version, Exterior, Door, 1)));
        Changed(perMap.SetMapChange(perMap.Version, Cave, Off, 1), perMap, 2);
        // A record that moves onto a full map is refused, and nothing moves.
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameFailureOf(perMap.SetMapChange(perMap.Version, Exterior, Off, 2)));
        Assert.AreEqual(Cave, perMap.QueryObjectState("oid:crate").Single().Map);

        using var total = Create(new GameSessionLimits(maxTotalChanges: 2));
        Changed(total.ApplyChanges(total.Version, null, [new(Exterior, Cell, 1), new(Exterior, Door, 1)]), total, 1);
        Rejected(total.ApplyChanges(total.Version, null, [new(Exterior, Off, 1)]), total, GameSessionFailure.BudgetExceeded, 1);
        // A move within the step and a removal beside a new field fit; nothing grows.
        Changed(total.ApplyChanges(total.Version, null, [new(Exterior, Cell, null), new(Exterior, Off, 1)]), total, 2);
        Changed(total.ApplyChanges(total.Version, null, [new(Exterior, Off, null), new(Cave, Off, 1)]), total, 3);
        Assert.AreEqual(Cave, total.QueryObjectState("oid:crate").Single().Map);
    }

    private static GameSessionFailure GameFailureOf(GameSessionMutationResult result) => result.Failure;

    // MIG7a (ARC-DEV-092, schema 62): a schema 61 save keeps the entity changes of mobile objects in the maps section. The step moves them to
    // the objects section's state by entity then property, keeps the other changes where they were, drops a map left empty, and for one key under
    // two maps keeps the lowest map's value. The text is written by hand as the schema 61 build wrote it (a literal, not made by the code under test).
    [TestMethod]
    public void ASchema61SaveMovesItsObjectFieldsOutOfTheMaps()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string EmptyObjects = "\"lastPoolNumber\":0,\"state\":[]}";
        StringAssert.Contains(text, EmptyObjects);
        const string Maps61 = "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":1}," +
            "{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"value\":7},{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.map\",\"value\":80}," +
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"value\":3}]}," +
            "{\"map\":80,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:crate\",\"property\":\"obj.off\",\"value\":1},{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"value\":99}]}," +
            "{\"map\":81,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:door\",\"property\":\"lock.kind\",\"value\":1}]}]";
        var older = text.Replace("\"maps\":[]", Maps61).Replace("\"schemaVersion\":62,\"world\"", "\"schemaVersion\":61,\"world\"").Replace(EmptyObjects, "\"lastPoolNumber\":0}");
        StringAssert.Contains(older, "\"schemaVersion\":61");
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        CollectionAssert.AreEqual(new[] { "oid:crate/obj.off/80/1", "oid:door/lock.kind/81/1", "oid:npc/npc.cell/79/7", "oid:npc/npc.map/79/80" },
            upgraded.Objects.State.Select(field => $"{field.Entity}/{field.Property}/{field.Map.RuntimeMap}/{field.Value}").ToArray());
        Assert.AreEqual(1, upgraded.Maps.Count, "maps 80 and 81 held only object fields and are dropped");
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open"), StaticLock }, upgraded.Maps.Single().Changes.Select(change => change.Key).ToArray());
        using var restored = Restore(upgraded);
        Assert.AreEqual(7L, restored.QueryMapChange(Exterior, Cell));
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(upgraded, RestoreOptions()).Failure);
        // A step that cannot read a change leaves it for the strict decode: a field with no value is damage, not a guess.
        var noValue = older.Replace(",\"value\":99}", "}");
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(noValue).Failure);
        // A schema 61 text that already has a state is refused.
        var already = older.Replace("\"lastPoolNumber\":0}", "\"lastPoolNumber\":0,\"state\":[]}");
        var refused = GameSessionSnapshotCodec.TryDecode(already);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.objects.state"), (refused.Failure, refused.Path));
    }

    [TestMethod]
    public void ARestoreRefusesFieldsThatAreNotAnObjectsOwn()
    {
        using var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Exterior, Cell, 7), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Row = "{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":79,\"value\":7}";
        StringAssert.Contains(text, Row);
        // A routed key in the maps section is a damaged save: the 61 to 62 step moves them, so schema 62 never holds one there.
        var inMaps = text.Replace("\"maps\":[]", "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"value\":7}]}]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(inMaps));
        // A property that is not an object field, an entity that is not a mobile object and a map that is not a map.
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"property\":\"npc.cell\",\"map\":79", "\"property\":\"dialog.lf\",\"map\":79")));
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"entity\":\"oid:npc\",\"property\":\"npc.cell\"", "\"entity\":\"sector:5:2\",\"property\":\"npc.cell\"")));
        // Decode refuses a row out of order and a row with a bad code.
        var two = text.Replace(Row, Row + ",{\"entity\":\"oid:a\",\"property\":\"npc.cell\",\"map\":79,\"value\":1}");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(two).Failure);
        var bad = text.Replace("\"entity\":\"oid:npc\"", "\"entity\":\"bad id\"");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(bad).Failure);
    }

    private static GameSessionFailure RestoreFailure(string text) =>
        GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot, RestoreOptions()).Failure;
}
