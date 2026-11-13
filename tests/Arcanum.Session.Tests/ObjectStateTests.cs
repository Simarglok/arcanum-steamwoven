using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-092 (MIG7a, TM2, GR5), ARC-DEV-093 (MIG7b): the fields of map-data objects are the object's, found by its identity (a mobile
/// object's alone, a static object's by its map and entity together); schema 62 saves the mobile ones in the objects section, schema 63 the static
/// ones too, and the 61 to 62 and 62 to 63 steps move the entity changes there.</summary>
[TestClass]
public sealed class ObjectStateTests
{
    private static readonly MapChangeKey Cell = MapChangeKey.ForEntity("oid:npc", "npc.cell");
    private static readonly MapChangeKey Away = MapChangeKey.ForEntity("oid:npc", "npc.map");
    private static readonly MapChangeKey Off = MapChangeKey.ForEntity("oid:crate", "obj.off");
    // A static object's entity is positional (the sector and the ordinal), so its identity is the map and the entity together. A key that is no field of the
    // object (a note here) stays per map; the script attachments are fields since MIG8 (ScriptAttachmentStateTests).
    private static readonly MapChangeKey StaticLock = MapChangeKey.ForEntity("sector:5:2", "lock.flags");
    private static readonly MapChangeKey StaticOff = MapChangeKey.ForEntity("sector:5:2", "obj.off");
    private static readonly MapChangeKey StaticNote = MapChangeKey.ForEntity("sector:5:2", "note.text");
    private static readonly MapChangeKey Note = MapChangeKey.ForEntity("oid:npc", "note.text");
    private static readonly MapChangeKey Door = MapChangeKey.ForSector(5, "door.open");

    [TestMethod]
    public void AMobileObjectsFieldsAreItsOwnAndLeaveTheMapsSection()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, Cell, 7), new(Exterior, Away, 80), new(Exterior, Off, 1),
            new(Exterior, StaticLock, 3), new(Exterior, Note, 5), new(Exterior, Door, 1)]), owner, 1);

        var fields = owner.QueryObjectState("oid:npc");
        CollectionAssert.AreEqual(new[] { ("npc.cell", 79, 7L), ("npc.map", 79, 80L) },
            fields.Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray(), "by property, with the map the record names");
        Assert.AreEqual(0, owner.QueryObjectState("oid:nobody").Count);
        CollectionAssert.AreEqual(new[] { ("lock.flags", 79, 3L) },
            owner.QueryObjectState("sector:5:2").Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray(), "a static object's lock is its own field, on its map");
        Assert.AreEqual(0, owner.QueryObjectState("oid:npc").Count(field => field.Property == "note.text"), "a key that is no field of the object stays in its map");

        var snapshot = owner.Capture();
        CollectionAssert.AreEqual(new[] { "oid:crate/obj.off", "oid:npc/npc.cell", "oid:npc/npc.map", "sector:5:2/lock.flags" },
            snapshot.Objects.State.Select(field => field.Entity + "/" + field.Property).ToArray(), "by entity then property");
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open"), Note },
            snapshot.Maps.Single().Changes.Select(change => change.Key).ToArray(), "the maps section keeps every other change");

        // The map query still lists the map's changes, the fields included, in canonical order; the identity query finds a field wherever it is.
        CollectionAssert.AreEqual(new[] { Door, Off, Cell, Away, StaticLock, Note }.OrderBy(key => key, MapChangeKeyComparer.Instance).ToArray(),
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
            "{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":80,\"value\":9}],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}");
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

    // One routed key listed under two maps in one step is walked in order, so the later edit is not judged against the state before the step.
    [TestMethod]
    public void ARoutedKeyUnderTwoMapsInOneStepIsWalkedInOrder()
    {
        using var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Exterior, Cell, 7), owner, 1);
        // The record moves to the cave and back: it ends where and as it was.
        Changed(owner.ApplyChanges(owner.Version, null, [new(Cave, Cell, 7), new(Exterior, Cell, 7)]), owner, 2);
        Assert.AreEqual((7L, (long?)null), (owner.QueryMapChange(Exterior, Cell)!.Value, owner.QueryMapChange(Cave, Cell)));
        Assert.AreEqual(1, owner.QueryObjectState("oid:npc").Count);
        // The record moves to the cave and the removal on the map it left is nothing: it stays on the cave.
        Changed(owner.ApplyChanges(owner.Version, null, [new(Cave, Cell, 5), new(Exterior, Cell, null)]), owner, 3);
        Assert.AreEqual((Cave, 5L), (owner.QueryObjectState("oid:npc").Single().Map, owner.QueryObjectState("oid:npc").Single().Value));
        Changed(owner.ClearMapChange(owner.Version, Cave, Cell), owner, 4);
        Assert.AreEqual(0, owner.Capture().Objects.State.Count);
        // A removal on a map the record is not on is nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyChanges(owner.Version, null, [new(Cave, Cell, null)]).Disposition);
    }

    [TestMethod]
    public void AMoveOfTheOnlyRecordOfAMapFitsTheMapBudgetAsTheStepDoes()
    {
        using var owner = Create(new GameSessionLimits(maxMaps: 1));
        Changed(owner.SetMapChange(owner.Version, Exterior, Cell, 7), owner, 1);
        Changed(owner.SetMapChange(owner.Version, Cave, Cell, 7), owner, 2);
        Assert.AreEqual(Cave, owner.QueryObjectState("oid:npc").Single().Map);
        // A second record on a third map still needs a map of its own.
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameFailureOf(owner.SetMapChange(owner.Version, Exterior, Off, 1)));
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, Cell, 8)]), owner, 3);
    }

    // MIG7b (ARC-DEV-093): the same sector and ordinal on two maps are two objects. A static's record is on the map it is written on and never moves.
    [TestMethod]
    public void AStaticObjectIsIdentifiedByItsMapAndEntityTogether()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, StaticLock, 3), new(Cave, StaticLock, 9), new(Exterior, StaticOff, 1), new(Exterior, StaticNote, 4)]), owner, 1);

        // Each map reads its own record; the same key on the two maps is two records, and a write on the cave does not move the exterior's.
        Assert.AreEqual((3L, 9L), (owner.QueryMapChange(Exterior, StaticLock)!.Value, owner.QueryMapChange(Cave, StaticLock)!.Value));
        Assert.AreEqual(((long?)null, (long?)null), (owner.QueryMapChange(new SessionMapId(81), StaticLock), owner.QueryMapChange(Cave, StaticOff)));
        CollectionAssert.AreEqual(new[] { (Exterior, 3L), (Cave, 9L) }, owner.QueryEntityChanges("sector:5:2", "lock.flags").ToArray());
        // The identity query by map answers one object; by entity it answers the records of every map, by property then map.
        CollectionAssert.AreEqual(new[] { ("lock.flags", 79, 3L), ("obj.off", 79, 1L) },
            owner.QueryObjectState(Exterior, "sector:5:2").Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray());
        CollectionAssert.AreEqual(new[] { ("lock.flags", 80, 9L) },
            owner.QueryObjectState(Cave, "sector:5:2").Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray());
        CollectionAssert.AreEqual(new[] { ("lock.flags", 79, 3L), ("lock.flags", 80, 9L), ("obj.off", 79, 1L) },
            owner.QueryObjectState("sector:5:2").Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray());
        Assert.AreEqual(0, owner.QueryObjectState(new SessionMapId(81), "sector:5:2").Count);
        // A mobile object does not depend on the map the query names.
        Assert.AreEqual(0, owner.QueryObjectState(Cave, "oid:npc").Count);
        Changed(owner.SetMapChange(owner.Version, Exterior, Cell, 7), owner, 2);
        Assert.AreEqual(1, owner.QueryObjectState(Cave, "oid:npc").Count, "a mobile identity names no map");
        // The map query lists the statics' records on the map they name, beside the script attachment that stays per map.
        CollectionAssert.AreEqual(new[] { Cell, StaticOff, StaticLock, StaticNote }.OrderBy(key => key, MapChangeKeyComparer.Instance).ToArray(),
            owner.QueryMapChanges(Exterior).Select(change => change.Key).ToArray());
        CollectionAssert.AreEqual(new[] { StaticLock }, owner.QueryMapChanges(Cave).Select(change => change.Key).ToArray());

        // The snapshot keeps the records apart, by entity, property, then map; the script attachment stays in the maps section.
        var snapshot = owner.Capture();
        CollectionAssert.AreEqual(new[] { "oid:npc/npc.cell/79", "sector:5:2/lock.flags/79", "sector:5:2/lock.flags/80", "sector:5:2/obj.off/79" },
            snapshot.Objects.State.Select(field => $"{field.Entity}/{field.Property}/{field.Map.RuntimeMap}").ToArray());
        CollectionAssert.AreEqual(new[] { StaticNote }, snapshot.Maps.Single().Changes.Select(change => change.Key).ToArray());
        var text = GameSessionSnapshotCodec.Encode(snapshot);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual((3L, 9L), (restored.QueryMapChange(Exterior, StaticLock)!.Value, restored.QueryMapChange(Cave, StaticLock)!.Value));

        // A clear on one map leaves the other object alone; a no-change write is nothing.
        Changed(owner.ClearMapChange(owner.Version, Exterior, StaticLock), owner, 3);
        Assert.AreEqual((long?)null, owner.QueryMapChange(Exterior, StaticLock));
        Assert.AreEqual(9L, owner.QueryMapChange(Cave, StaticLock));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearMapChange(owner.Version, Exterior, StaticLock).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetMapChange(owner.Version, Cave, StaticLock, 9).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyChanges(owner.Version, null, [new(Cave, StaticLock, 9), new(Exterior, StaticLock, null)]).Disposition);
    }

    [TestMethod]
    public void AStaticRecordCountsAgainstItsOwnMapAndTheSession()
    {
        using var perMap = Create(new GameSessionLimits(maxChangesPerMap: 1));
        Changed(perMap.SetMapChange(perMap.Version, Exterior, StaticLock, 1), perMap, 1);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameFailureOf(perMap.SetMapChange(perMap.Version, Exterior, StaticOff, 1)), "the map holds one change");
        // The same key on another map is another record with its own map's room, and nothing moves.
        Changed(perMap.SetMapChange(perMap.Version, Cave, StaticLock, 2), perMap, 2);
        Assert.AreEqual((1L, 2L), (perMap.QueryMapChange(Exterior, StaticLock)!.Value, perMap.QueryMapChange(Cave, StaticLock)!.Value));

        using var total = Create(new GameSessionLimits(maxTotalChanges: 2));
        Changed(total.ApplyChanges(total.Version, null, [new(Exterior, StaticLock, 1), new(Cave, StaticLock, 1)]), total, 1);
        Rejected(total.ApplyChanges(total.Version, null, [new(Exterior, StaticOff, 1)]), total, GameSessionFailure.BudgetExceeded, 1);
        Changed(total.ApplyChanges(total.Version, null, [new(Cave, StaticLock, null), new(Exterior, StaticOff, 1)]), total, 2);

        using var maps = Create(new GameSessionLimits(maxMaps: 1));
        Changed(maps.SetMapChange(maps.Version, Exterior, StaticLock, 1), maps, 1);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameFailureOf(maps.SetMapChange(maps.Version, Cave, StaticLock, 1)), "a static never moves, so the cave needs a map of its own");
    }

    // MIG7b (ARC-DEV-093, schema 63): a schema 62 save keeps the changes of static objects in the maps section, one list per map. The step moves them
    // to the objects section's state with the map each was under, merges them into the rows that are already there in canonical order (entity, property,
    // map), keeps every other change where it was and drops a map left empty. The text is written by hand as the schema 62 build wrote it.
    [TestMethod]
    public void ASchema62SaveMovesItsStaticObjectFieldsOutOfTheMaps()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string EmptyState = "\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}";
        StringAssert.Contains(text, EmptyState);
        const string Maps62 = "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":1}," +
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"value\":3},{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"obj.off\",\"value\":1}," +
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"note.text\",\"value\":4}]}," +
            "{\"map\":80,\"changes\":[{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"value\":9},{\"scope\":\"entity\",\"entity\":\"sector:1:7\",\"property\":\"trap.state\",\"value\":2}]}," +
            "{\"map\":81,\"changes\":[{\"scope\":\"entity\",\"entity\":\"sector:3:1\",\"property\":\"obj.destroyed\",\"value\":1}]}]";
        const string State62 = "\"lastPoolNumber\":0,\"state\":[{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":79,\"value\":7}]}";
        var older = SnapshotUpgradeTests.WithoutFrameClocks(SnapshotUpgradeTests.WithoutTownMaps(SnapshotUpgradeTests.WithoutCritterEffects(text).Replace(NoGoals + NoSpells + NoNewspaper + NoGenerators + NoFlights, "").Replace(NoEncounters, "").Replace(NoBarLights, ""))).Replace("\"maps\":[]", Maps62).Replace(EmptyState, State62).Replace("\"schemaVersion\":98,\"world\"", "\"schemaVersion\":62,\"world\"").Replace(NoScripts, "").Replace(NoResidency, "").Replace(SnapshotUpgradeTests.NoItemFields, "").Replace(SnapshotUpgradeTests.NoHolderSlots, "");
        StringAssert.Contains(older, "\"schemaVersion\":62");
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        CollectionAssert.AreEqual(new[] { "oid:npc/npc.cell/79/7", "sector:1:7/trap.state/80/2", "sector:3:1/obj.destroyed/81/1", "sector:5:2/lock.flags/79/3", "sector:5:2/lock.flags/80/9", "sector:5:2/obj.off/79/1" },
            upgraded.Objects.State.Select(field => $"{field.Entity}/{field.Property}/{field.Map.RuntimeMap}/{field.Value}").ToArray());
        Assert.AreEqual(1, upgraded.Maps.Count, "maps 80 and 81 held only static fields and are dropped");
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open"), StaticNote }, upgraded.Maps.Single().Changes.Select(change => change.Key).ToArray());
        using var restored = Restore(upgraded);
        Assert.AreEqual((3L, 9L), (restored.QueryMapChange(Exterior, StaticLock)!.Value, restored.QueryMapChange(Cave, StaticLock)!.Value));
        Assert.AreEqual(2L, restored.QueryMapChange(Cave, MapChangeKey.ForEntity("sector:1:7", "trap.state")));
        Assert.AreEqual(upgraded.Objects.State.Count, restored.Capture().Objects.State.Count);

        // A save with no static change upgrades to the same text with only the schema changed.
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(Decode(SnapshotUpgradeTests.Schema62(text))));
        // A second record of one map, entity and property is not moved and the strict restore refuses it (the same key twice in one map is damage).
        var twice = older.Replace("{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"note.text\",\"value\":4}",
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"value\":4}");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(twice).Snapshot, RestoreOptions()).Failure);
        // A step that cannot read a change leaves it for the strict decode: a change with no value is damage, not a guess.
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(older.Replace(",\"value\":9}", "}")).Failure);
        // The step does not repair a damaged state: a `sector:` row or rows out of order in a schema 62 save are refused, whether or not a change moves.
        var staticRow = "{\"entity\":\"sector:1:1\",\"property\":\"obj.off\",\"map\":79,\"value\":1}";
        var withStaticRow = older.Replace("\"state\":[{", "\"state\":[" + staticRow + ",{");
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.objects.state[0]"), (GameSessionSnapshotCodec.TryDecode(withStaticRow).Failure, GameSessionSnapshotCodec.TryDecode(withStaticRow).Path));
        var twoRows = SnapshotUpgradeTests.WithoutFrameClocks(SnapshotUpgradeTests.WithoutTownMaps(SnapshotUpgradeTests.WithoutCritterEffects(text).Replace(NoGoals + NoSpells + NoNewspaper + NoGenerators + NoFlights, "").Replace(NoEncounters, "").Replace(NoBarLights, ""))).Replace(EmptyState, "\"lastPoolNumber\":0,\"state\":[{\"entity\":\"oid:b\",\"property\":\"npc.cell\",\"map\":79,\"value\":1},{\"entity\":\"oid:a\",\"property\":\"npc.cell\",\"map\":79,\"value\":1}]}")
            .Replace("\"schemaVersion\":98,\"world\"", "\"schemaVersion\":62,\"world\"").Replace(NoScripts, "").Replace(NoResidency, "").Replace(SnapshotUpgradeTests.NoItemFields, "").Replace(SnapshotUpgradeTests.NoHolderSlots, "");
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.objects.state[1]"), (GameSessionSnapshotCodec.TryDecode(twoRows).Failure, GameSessionSnapshotCodec.TryDecode(twoRows).Path));
        var moving = twoRows.Replace("\"maps\":[]", "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"obj.off\",\"value\":1}]}]");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(moving).Failure);
        // The step needs the state of schema 62.
        var noState = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"state\":[{", ",\"stateX\":[{"));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.objects.state"), (noState.Failure, noState.Path));
    }

    [TestMethod]
    public void ARestoreRefusesStaticFieldsThatAreNotAStaticsOwn()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, StaticLock, 3), new(Cave, StaticLock, 9)]), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Rows = "{\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"map\":79,\"value\":3},{\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"map\":80,\"value\":9}";
        StringAssert.Contains(text, Rows);
        Assert.AreEqual(GameSessionFailure.None, RestoreFailure(text));
        // A static has no NPC or item field; a script attachment is not an object field.
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"property\":\"lock.flags\",\"map\":80", "\"property\":\"npc.cell\",\"map\":80")));
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"property\":\"lock.flags\",\"map\":80", "\"property\":\"npc.cell\",\"map\":80")));
        // The rows are in order by map; a row out of order, or the same map twice, is not canonical.
        var swapped = text.Replace(Rows, "{\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"map\":80,\"value\":9},{\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"map\":79,\"value\":3}");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(swapped).Failure);
        var same = text.Replace("\"map\":80,\"value\":9", "\"map\":79,\"value\":9");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(same).Failure);
        // A crafted save with more static rows on one map than the map budget allows is refused by the restore, not decoded away.
        using var crowded = Create();
        Changed(crowded.ApplyChanges(crowded.Version, null, [new(Exterior, StaticLock, 3), new(Exterior, StaticOff, 1)]), crowded, 1);
        var budget = Decode(GameSessionSnapshotCodec.Encode(crowded.Capture()));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameSessionOwner.Restore(budget, RestoreOptions(new GameSessionLimits(maxChangesPerMap: 1))).Failure);
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(budget, RestoreOptions(new GameSessionLimits(maxChangesPerMap: 2))).Failure);
        // A mobile object with two records is still the owner's refusal (one record whatever map it names).
        using var mobile = Create();
        Changed(mobile.SetMapChange(mobile.Version, Exterior, Cell, 7), mobile, 1);
        var mobileText = GameSessionSnapshotCodec.Encode(mobile.Capture());
        const string Row = "{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":79,\"value\":7}";
        StringAssert.Contains(mobileText, Row);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(mobileText.Replace(Row, Row + ",{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":80,\"value\":8}")));
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
        const string EmptyObjects = "\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}";
        StringAssert.Contains(text, EmptyObjects);
        const string Maps61 = "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":1}," +
            "{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"value\":7},{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.map\",\"value\":80}," +
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"value\":3}]}," +
            "{\"map\":80,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:crate\",\"property\":\"obj.off\",\"value\":1},{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"value\":99}]}," +
            "{\"map\":81,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:door\",\"property\":\"lock.kind\",\"value\":1}]}]";
        var older = SnapshotUpgradeTests.WithoutFrameClocks(SnapshotUpgradeTests.WithoutTownMaps(SnapshotUpgradeTests.WithoutCritterEffects(text).Replace(NoGoals + NoSpells + NoNewspaper + NoGenerators + NoFlights, "").Replace(NoEncounters, "").Replace(NoBarLights, ""))).Replace("\"maps\":[]", Maps61).Replace("\"schemaVersion\":98,\"world\"", "\"schemaVersion\":61,\"world\"").Replace(NoScripts, "").Replace(NoResidency, "").Replace(SnapshotUpgradeTests.NoItemFields, "").Replace(SnapshotUpgradeTests.NoHolderSlots, "").Replace(EmptyObjects, "\"lastPoolNumber\":0}");
        StringAssert.Contains(older, "\"schemaVersion\":61");
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        // The 61 to 62 step moves the mobile objects' fields; the 62 to 63 step (MIG7b) then moves the static lock, which a schema 61 build kept in the maps.
        CollectionAssert.AreEqual(new[] { "oid:crate/obj.off/80/1", "oid:door/lock.kind/81/1", "oid:npc/npc.cell/79/7", "oid:npc/npc.map/79/80", "sector:5:2/lock.flags/79/3" },
            upgraded.Objects.State.Select(field => $"{field.Entity}/{field.Property}/{field.Map.RuntimeMap}/{field.Value}").ToArray());
        Assert.AreEqual(1, upgraded.Maps.Count, "maps 80 and 81 held only object fields and are dropped");
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open") }, upgraded.Maps.Single().Changes.Select(change => change.Key).ToArray());
        using var restored = Restore(upgraded);
        Assert.AreEqual(7L, restored.QueryMapChange(Exterior, Cell));
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(upgraded, RestoreOptions()).Failure);
        // A step that cannot read a change leaves it for the strict decode: a field with no value is damage, not a guess.
        var noValue = older.Replace(",\"value\":99}", "}");
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(noValue).Failure);
        // A schema 61 text that already has a state is refused.
        var already = older.Replace("\"lastPoolNumber\":0}", "\"lastPoolNumber\":0,\"state\":[],\"links\":[]}");
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
        // A static object's field in the maps section is as damaged: the 62 to 63 step moves them.
        var staticInMaps = text.Replace("\"maps\":[]", "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"lock.flags\",\"value\":3}]}]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(staticInMaps));
        // A property that is not an object field and an entity that is not a mobile object.
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"property\":\"npc.cell\",\"map\":79", "\"property\":\"note.text\",\"map\":79")));
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
