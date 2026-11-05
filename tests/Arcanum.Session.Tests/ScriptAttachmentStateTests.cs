using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-096 (MIG8, TM9, GR7): the script attachments of a map-data object (the dialog, use and scr&lt;point&gt; parts: script number, local
/// flags, counters) are fields of the object, found by its identity like its other fields (a mobile object's alone, a static object's by its map and
/// entity together). A mobile object's record is one whatever map it names, so it travels with the object; the snapshot saves them in the objects
/// section's state; the 65 to 66 step moves the keys of an older save out of the maps.</summary>
[TestClass]
public sealed class ScriptAttachmentStateTests
{
    private static readonly MapChangeKey DialogFlags = MapChangeKey.ForEntity("oid:npc", "dialog.lf");
    private static readonly MapChangeKey DialogCounters = MapChangeKey.ForEntity("oid:npc", "dialog.counters");
    private static readonly MapChangeKey DialogScript = MapChangeKey.ForEntity("oid:npc", "dialog.script");
    private static readonly MapChangeKey Heartbeat = MapChangeKey.ForEntity("oid:npc", "scr19.lf");
    private static readonly MapChangeKey StaticUse = MapChangeKey.ForEntity("sector:5:2", "use.lf");
    private static readonly MapChangeKey StaticUseScript = MapChangeKey.ForEntity("sector:5:2", "use.script");

    [TestMethod]
    public void TheAttachmentPartsOfAnObjectAreItsFieldsAndLeaveTheMapsSection()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, DialogFlags, 5), new(Exterior, DialogCounters, 258), new(Exterior, Heartbeat, 1),
            new(Exterior, StaticUse, 4), new(Exterior, MapChangeKey.ForSector(5, "door.open"), 1)]), owner, 1);

        CollectionAssert.AreEqual(new[] { ("dialog.counters", 79, 258L), ("dialog.lf", 79, 5L), ("scr19.lf", 79, 1L) },
            owner.QueryObjectState("oid:npc").Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray());
        CollectionAssert.AreEqual(new[] { ("use.lf", 79, 4L) },
            owner.QueryObjectState(Exterior, "sector:5:2").Select(field => (field.Property, field.Map.RuntimeMap, field.Value)).ToArray());
        Assert.AreEqual(0, owner.QueryObjectState(Cave, "sector:5:2").Count, "the same sector and ordinal on another map is another object");

        var snapshot = owner.Capture();
        CollectionAssert.AreEqual(new[] { "oid:npc/dialog.counters", "oid:npc/dialog.lf", "oid:npc/scr19.lf", "sector:5:2/use.lf" },
            snapshot.Objects.State.Select(field => field.Entity + "/" + field.Property).ToArray());
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open") }, snapshot.Maps.Single().Changes.Select(change => change.Key).ToArray());
        Assert.AreEqual(5, owner.QueryMapChanges(Exterior).Count, "the map query still lists the map's changes, the fields included");
    }

    [TestMethod]
    public void OnlyTheCanonicalSpellingOfAnAttachmentPartIsAField()
    {
        using var owner = Create();
        var names = new[] { "use.script", "dialog.lf", "scr0.counters", "scr10.script", "scr19.lf", "scr35.counters",
            // one slot, one name: use and dialog are the points 1 and 9; no leading zero, no point beyond 35, no other part
            "scr1.lf", "scr9.lf", "scr01.lf", "scr36.lf", "scr100.lf", "scr-1.lf", "scr.lf", "use.flags", "dialog.", "use", "scr10.LF" };
        var expected = new[] { true, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false };
        for (var i = 0; i < names.Length; i++)
            Assert.AreEqual(expected[i], ObjectStateVocabulary.Attachment(names[i]), names[i]);
        var keys = names.Select(name => MapChangeKey.ForEntity("oid:npc", name)).ToArray();
        Changed(owner.ApplyChanges(owner.Version, null, keys.Select(key => new MapChangeEdit(Exterior, key, 1)).ToArray()), owner, 1);
        CollectionAssert.AreEqual(names.Where((_, i) => expected[i]).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            owner.QueryObjectState("oid:npc").Select(field => field.Property).ToArray());
        Assert.AreEqual(names.Count(name => !ObjectStateVocabulary.Attachment(name)), owner.Capture().Maps.Single().Changes.Count, "the rest stay in the maps");
        // An entity that is neither a mobile nor a static map object keeps its attachment keys per map (the PC and the session's own entities).
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("npc-1", "dialog.lf"), 3), owner, 2);
        Assert.AreEqual(0, owner.QueryObjectState("npc-1").Count);
    }

    [TestMethod]
    public void AMobileObjectsAttachmentTravelsWithIt()
    {
        using var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Exterior, DialogFlags, 5), owner, 1);
        // The identity read finds the record wherever it sits; the map read finds it on the map it names only.
        Assert.AreEqual((5L, 5L, (long?)null), (owner.QueryObjectChange(Cave, DialogFlags), owner.QueryObjectChange(Exterior, DialogFlags), owner.QueryMapChange(Cave, DialogFlags)));
        Assert.AreEqual(Exterior, owner.QueryObjectRecordMap(DialogFlags));
        // A write on the map the object stands on now moves the record; there is still one.
        Changed(owner.SetMapChange(owner.Version, Cave, DialogFlags, 7), owner, 2);
        Assert.AreEqual((7L, 7L, (long?)null), (owner.QueryObjectChange(Exterior, DialogFlags), owner.QueryObjectChange(Cave, DialogFlags), owner.QueryMapChange(Exterior, DialogFlags)));
        Assert.AreEqual(1, owner.QueryObjectState("oid:npc").Count);
        Assert.AreEqual(Cave, owner.QueryObjectRecordMap(DialogFlags));
        // A clear that names the map the record is on removes it; one that names another map finds none.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearMapChange(owner.Version, Exterior, DialogFlags).Disposition);
        Changed(owner.ClearMapChange(owner.Version, Cave, DialogFlags), owner, 3);
        Assert.IsNull(owner.QueryObjectChange(Exterior, DialogFlags));
        Assert.IsNull(owner.QueryObjectRecordMap(DialogFlags));
    }

    [TestMethod]
    public void AStaticObjectsAttachmentStaysOnItsMap()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, StaticUse, 4), new(Cave, StaticUse, 8), new(Exterior, StaticUseScript, 790)]), owner, 1);
        Assert.AreEqual((4L, 8L), (owner.QueryObjectChange(Exterior, StaticUse)!.Value, owner.QueryObjectChange(Cave, StaticUse)!.Value));
        Assert.IsNull(owner.QueryObjectRecordMap(StaticUse), "a static's record never moves, so it names no other map");
        CollectionAssert.AreEqual(new[] { (Exterior, 4L), (Cave, 8L) }, owner.QueryEntityChanges("sector:5:2", "use.lf").ToArray());
        Changed(owner.ClearMapChange(owner.Version, Cave, StaticUse), owner, 2);
        Assert.AreEqual(4L, owner.QueryObjectChange(Exterior, StaticUse)!.Value);
        // A key that is no object field is read as the map's.
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForSector(5, "door.open"), 1), owner, 3);
        Assert.AreEqual((1L, (long?)null), (owner.QueryObjectChange(Exterior, MapChangeKey.ForSector(5, "door.open"))!.Value, owner.QueryObjectChange(Cave, MapChangeKey.ForSector(5, "door.open"))));
    }

    [TestMethod]
    public void TheAttachmentsSurviveASaveAndALoadAndCountAgainstTheMapBudgets()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Cave, DialogFlags, 9), new(Cave, DialogScript, 12), new(Exterior, StaticUse, 4)]), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "{\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"map\":80,\"value\":9}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual((9L, 12L, 4L), (restored.QueryObjectChange(Exterior, DialogFlags)!.Value, restored.QueryObjectChange(Exterior, DialogScript)!.Value,
            restored.QueryObjectChange(Exterior, StaticUse)!.Value));
        Assert.AreEqual(2, restored.QueryMapChanges(Cave).Count, "the fields count under the map their record names");

        using var tight = Create(new GameSessionLimits(maxChangesPerMap: 2));
        Changed(tight.ApplyChanges(tight.Version, null, [new(Exterior, DialogFlags, 1), new(Exterior, DialogScript, 1)]), tight, 1);
        Rejected(tight.SetMapChange(tight.Version, Exterior, DialogCounters, 1), tight, GameSessionFailure.BudgetExceeded, 1);
        // The record of a mobile object moves to the map with room; the map it leaves is freed.
        Changed(tight.SetMapChange(tight.Version, Cave, DialogFlags, 1), tight, 2);
        Changed(tight.SetMapChange(tight.Version, Exterior, DialogCounters, 1), tight, 3);
    }

    // MIG8 (ARC-DEV-096, schema 66): a schema 65 save keeps the attachments of mobile and static objects as keys of the maps section, one list per map.
    // The step moves them to the objects section's state, merges them into the rows already there in canonical order, keeps every other change where
    // it was and drops a map left empty. The text is written by hand as the schema 65 build wrote it.
    [TestMethod]
    public void ASchema65SaveMovesItsAttachmentsOutOfTheMaps()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string EmptyState = "\"lastPoolNumber\":0,\"state\":[],\"links\":[]}";
        StringAssert.Contains(text, EmptyState);
        const string Maps65 = "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":1}," +
            "{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"value\":5},{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"scr19.counters\",\"value\":2}," +
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"use.lf\",\"value\":4},{\"scope\":\"entity\",\"entity\":\"npc-1\",\"property\":\"dialog.lf\",\"value\":6}]}," +
            "{\"map\":80,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"value\":9},{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"use.lf\",\"value\":8}]}," +
            "{\"map\":81,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:crate\",\"property\":\"use.script\",\"value\":790}]}]";
        const string State65 = "\"lastPoolNumber\":0,\"state\":[{\"entity\":\"oid:npc\",\"property\":\"npc.cell\",\"map\":79,\"value\":7}],\"links\":[]}";
        var older = SnapshotUpgradeTests.Schema65(text).Replace("\"maps\":[]", Maps65).Replace(EmptyState, State65);
        StringAssert.Contains(older, "\"schemaVersion\":65");
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        // The object that held dialog.lf under two maps keeps the lowest map's value; a static keeps one record per map.
        CollectionAssert.AreEqual(new[]
            {
                "oid:crate/use.script/81/790", "oid:npc/dialog.lf/79/5", "oid:npc/npc.cell/79/7", "oid:npc/scr19.counters/79/2",
                "sector:5:2/use.lf/79/4", "sector:5:2/use.lf/80/8",
            },
            upgraded.Objects.State.Select(field => $"{field.Entity}/{field.Property}/{field.Map.RuntimeMap}/{field.Value}").ToArray());
        Assert.AreEqual(1, upgraded.Maps.Count, "maps 80 and 81 held only attachments and are dropped");
        CollectionAssert.AreEqual(new[] { MapChangeKey.ForSector(5, "door.open"), MapChangeKey.ForEntity("npc-1", "dialog.lf") },
            upgraded.Maps.Single().Changes.Select(change => change.Key).ToArray(), "an entity that is no map object keeps its keys");
        using var restored = Restore(upgraded);
        Assert.AreEqual((5L, 4L, 8L), (restored.QueryObjectChange(Cave, DialogFlags)!.Value, restored.QueryObjectChange(Exterior, StaticUse)!.Value, restored.QueryObjectChange(Cave, StaticUse)!.Value));
        Assert.AreEqual(upgraded.Objects.State.Count, restored.Capture().Objects.State.Count);

        // A save with no attachment upgrades to the same text with only the schema changed.
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(Decode(SnapshotUpgradeTests.Schema65(text))));
        // A second record of one map, entity and part is not moved, and the strict restore refuses it.
        var twice = older.Replace("{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"use.lf\",\"value\":4}",
            "{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"use.lf\",\"value\":4},{\"scope\":\"entity\",\"entity\":\"sector:5:2\",\"property\":\"use.lf\",\"value\":3}");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(twice));
        var twiceMobile = older.Replace("{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"value\":5}",
            "{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"value\":5},{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"value\":6}");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(twiceMobile), "the same part twice under one map is damage for a mobile object too");
        // A step that cannot read a change leaves it for the strict decode: a change with no value is damage, not a guess.
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(older.Replace(",\"value\":790}", "}")).Failure);
        // The step does not repair a damaged state: an attachment already among the rows of schema 65, rows out of order and a missing state are refused,
        // whether or not a change moves.
        var withAttachmentRow = older.Replace("\"state\":[{", "\"state\":[{\"entity\":\"oid:npc\",\"property\":\"dialog.counters\",\"map\":79,\"value\":1},{");
        var refused = GameSessionSnapshotCodec.TryDecode(withAttachmentRow);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.objects.state[0].property"), (refused.Failure, refused.Path));
        var twoRows = SnapshotUpgradeTests.Schema65(text).Replace(EmptyState, "\"lastPoolNumber\":0,\"state\":[{\"entity\":\"oid:b\",\"property\":\"npc.cell\",\"map\":79,\"value\":1},{\"entity\":\"oid:a\",\"property\":\"npc.cell\",\"map\":79,\"value\":1}],\"links\":[]}");
        var outOfOrder = GameSessionSnapshotCodec.TryDecode(twoRows);
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.objects.state[1]"), (outOfOrder.Failure, outOfOrder.Path));
        var noState = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"state\":[{", ",\"stateX\":[{"));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.objects.state"), (noState.Failure, noState.Path));
    }

    [TestMethod]
    public void ARestoreRefusesAnAttachmentInTheMapsSectionAndOneOnTheWrongEntity()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, DialogFlags, 5)]), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Row = "{\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"map\":79,\"value\":5}";
        StringAssert.Contains(text, Row);
        // An attachment key in the maps section is a damaged save: the 65 to 66 step moves them, so schema 66 never holds one there.
        var inMaps = text.Replace("\"maps\":[]", "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:npc\",\"property\":\"dialog.lf\",\"value\":5}]}]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(inMaps));
        // The same part on an entity that is no map object, and a part that is not canonical, are not object fields.
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"entity\":\"oid:npc\"", "\"entity\":\"npc-1\"")));
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"property\":\"dialog.lf\"", "\"property\":\"scr9.lf\"")));
        // A mobile object's attachment names one map: the same part twice is refused.
        var twice = text.Replace(Row, Row + "," + Row.Replace("\"map\":79", "\"map\":80"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(twice));
    }

    private static GameSessionFailure RestoreFailure(string text) =>
        GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot, RestoreOptions()).Failure;
}
