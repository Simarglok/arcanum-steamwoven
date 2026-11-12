using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-351 (schema 86, ledger TG9, RL22, RL27): the original's OF_TELEPORTED mark of a carried object is the object state of its directory entry (the field
/// <c>obj.teleported</c>): its record names the map the object stands on and holds the placement it was written on. The open of that map takes the mark off every
/// object that carries it there, in one revision, and reports those it can read. Ids, maps and cells are invented.
/// </summary>
[TestClass]
public sealed class TeleportedMarkStateTests
{
    private const string Virgil = "oid:virgil", Sogg = "oid:sogg", Wolf = "oid:wolf";

    private static MapChangeEdit Mark(SessionMapId map, string entity, int x, int y, NeighborDirection facing = NeighborDirection.XPlus) =>
        new(map, SessionTeleportedMark.Key(entity), SessionNpcPlacement.Encode(new(x, y), facing));

    [TestMethod]
    public void A_mark_is_an_object_field_that_names_the_map_the_object_stands_on()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [Mark(Cave, Virgil, 10, 20, NeighborDirection.YPlus)]), owner, 1);
        var field = owner.QueryObjectState(Virgil).Single();
        Assert.AreEqual(("obj.teleported", Cave), (field.Property, field.Map));
        Assert.AreEqual(0, owner.Capture().Maps.Count, "an object field, not a map change");
        var marks = owner.QueryTeleportedMarks(Cave);
        Assert.AreEqual(new SessionTeleportedObject(Virgil, new(10, 20), NeighborDirection.YPlus), marks.Single());
        Assert.AreEqual(0, owner.QueryTeleportedMarks(Exterior).Count, "a mark belongs to the map its record names");
        // The record is one wherever the object stands: a mark written on another map moves it.
        Changed(owner.ApplyChanges(owner.Version, null, [Mark(Exterior, Virgil, 1, 2)]), owner, 2);
        Assert.AreEqual(0, owner.QueryTeleportedMarks(Cave).Count);
        Assert.AreEqual(new CellCoord(1, 2), owner.QueryTeleportedMarks(Exterior).Single().Cell);
    }

    [TestMethod]
    public void The_open_of_a_map_takes_every_mark_off_in_one_revision_in_entity_order()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [Mark(Cave, Wolf, 3, 3), Mark(Cave, Virgil, 10, 20), Mark(Exterior, Sogg, 5, 5)]), owner, 1);
        var (result, objects) = owner.TakeTeleportedMarks(owner.Version, Cave);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(2L, owner.Version.Revision, "one revision for every mark taken");
        CollectionAssert.AreEqual(new[] { Virgil, Wolf }, objects.Select(o => o.Entity).ToArray());
        Assert.AreEqual(new CellCoord(10, 20), objects[0].Cell);
        Assert.AreEqual(0, owner.QueryTeleportedMarks(Cave).Count);
        Assert.AreEqual(Sogg, owner.QueryTeleportedMarks(Exterior).Single().Entity, "the other map's mark waits for its own open");
        // Each mark is taken once: the next open of the map notifies nobody and writes nothing.
        var again = owner.TakeTeleportedMarks(owner.Version, Cave);
        Assert.AreEqual(GameSessionDisposition.NoChange, again.Result.Disposition);
        Assert.AreEqual(0, again.Objects.Count);
        Assert.AreEqual(2L, owner.Version.Revision);
    }

    [TestMethod]
    public void A_refused_take_changes_nothing()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [Mark(Cave, Virgil, 10, 20)]), owner, 1);
        var stale = owner.TakeTeleportedMarks(new GameSessionVersion(owner.Version.Owner, owner.Version.Revision + 1), Cave);
        Assert.AreEqual(GameSessionFailure.StaleVersion, stale.Result.Failure);
        Assert.AreEqual(0, stale.Objects.Count);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.TakeTeleportedMarks(owner.Version, new SessionMapId(0)).Result.Failure);
        Assert.AreEqual(1, owner.QueryTeleportedMarks(Cave).Count);
        Assert.AreEqual(1L, owner.Version.Revision);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.TakeTeleportedMarks(new GameSessionVersion(owner.Version.Owner, owner.Version.Revision + 1), Exterior).Result.Failure,
            "a stale version is refused even when the map holds no mark");
    }

    [TestMethod]
    public void A_mark_the_placement_cannot_read_is_taken_off_but_not_reported()
    {
        using var owner = Create();
        Changed(owner.SetMapChange(owner.Version, Cave, SessionTeleportedMark.Key(Virgil), -5), owner, 1);
        Assert.AreEqual(0, owner.QueryTeleportedMarks(Cave).Count);
        var (result, objects) = owner.TakeTeleportedMarks(owner.Version, Cave);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(0, objects.Count);
        Assert.AreEqual(0, owner.QueryObjectState(Virgil).Count);
    }

    [TestMethod]
    public void A_static_object_has_no_mark()
    {
        using var owner = Create();
        var result = owner.SetMapChange(owner.Version, Cave, SessionTeleportedMark.Key("sector:5:2"), 1);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, "the key is an ordinary map change when the object is not mobile");
        Assert.AreEqual(0, owner.QueryTeleportedMarks(Cave).Count);
        Assert.AreEqual(0, owner.QueryObjectState(Cave, "sector:5:2").Count);
    }

    [TestMethod]
    public void A_mark_survives_a_save_and_a_load_and_is_taken_by_the_next_open()
    {
        using var owner = Create();
        Changed(owner.ApplyChanges(owner.Version, null, [Mark(Cave, Virgil, 10, 20, NeighborDirection.YPlus)]), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"schemaVersion\":96,");
        StringAssert.Contains(text, "\"property\":\"obj.teleported\",\"map\":80,\"value\":");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(new SessionTeleportedObject(Virgil, new(10, 20), NeighborDirection.YPlus), restored.TakeTeleportedMarks(restored.Version, Cave).Objects.Single());
        // A mark left in the maps section is a damaged save (it is an object field).
        var inMaps = text.Replace("\"maps\":[]", "\"maps\":[{\"map\":80,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:virgil\",\"property\":\"obj.teleported\",\"value\":42949672960}]}]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(inMaps).Snapshot, RestoreOptions()).Failure);
    }

    [TestMethod]
    public void The_eighty_five_to_eighty_six_step_moves_nothing_and_needs_the_objects_state()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema85(current);
        StringAssert.Contains(older, "\"schemaVersion\":85,\"world\"");
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(Decode(older)), "only the schema differs");
        // The step needs the objects state the older schema always had.
        var missing = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"state\":[]", ""));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.objects.state"), (missing.Failure, missing.Path));
        var wrong = GameSessionSnapshotCodec.TryDecode(older.Replace("\"state\":[]", "\"state\":{}"));
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.objects.state"), (wrong.Failure, wrong.Path));
    }
}
