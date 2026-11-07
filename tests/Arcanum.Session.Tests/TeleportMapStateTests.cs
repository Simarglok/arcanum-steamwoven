using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-116 (MIG11 S5, FM6, FM10, GR15, ledger WS3): an NPC's teleport map is the object state of its directory entry (the field
/// <c>npc.teleport</c>, schema 69). It is written while it is 0 by the getter's rule, by the preparation of every NPC a change of place moves (the
/// hero's followers, a non-player mover) and read by the route steps; it is saved with the session. Ids, maps and cells are invented.
/// </summary>
[TestClass]
public sealed class TeleportMapStateTests
{
    private const string Virgil = "oid:virgil", Sogg = "oid:sogg", Wolf = "oid:wolf";

    private sealed class Host(IReadOnlyList<MapChangeEdit>? edits = null) : IChangeOfPlaceHost
    {
        public bool DestinationListed(int map) => true;
        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => new();
        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) => ChangeOfPlaceReadiness.Ready;
        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) =>
            new(request.Destination, NeighborDirection.YPlus, edits ?? []);
        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request) => true;
    }

    [TestMethod]
    public void The_getters_write_sets_the_open_map_once_and_leaves_a_set_one_alone()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryTeleportMap(Virgil));
        Changed(owner.EnsureTeleportMap(owner.Version, Virgil, Exterior), owner, 1);
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Virgil));
        var version = owner.Version;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EnsureTeleportMap(owner.Version, Virgil, Cave).Disposition, "WS3: written only while it is 0");
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Virgil));
        // It is the NPC's own field, a mobile object field of its entry that names the teleport map.
        CollectionAssert.AreEqual(new[] { "npc.teleport" }, owner.QueryObjectState(Virgil).Select(field => field.Property).ToArray());
        Assert.AreEqual(Exterior, owner.QueryObjectState(Virgil).Single().Map);
        Assert.AreEqual(0, owner.Capture().Maps.Count, "an object field, not a map change");
    }

    [TestMethod]
    public void A_refused_write_changes_nothing()
    {
        using var owner = Create();
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.EnsureTeleportMap(owner.Version, "bad id", Exterior).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.EnsureTeleportMap(owner.Version, "sector:5:2", Exterior).Failure, "a static object is no NPC");
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.EnsureTeleportMap(owner.Version, "npc-1", Exterior).Failure, "only a mobile object has the field");
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.EnsureTeleportMap(owner.Version, Virgil, new SessionMapId(0)).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion,
            owner.EnsureTeleportMap(new GameSessionVersion(owner.Version.Owner, owner.Version.Revision + 1), Virgil, Exterior).Failure);
        Assert.AreEqual(version, owner.Version);
        Assert.IsNull(owner.QueryTeleportMap(Virgil));
        // The budget counts the field like any change.
        using var tight = Create(new GameSessionLimits(maxChangesPerMap: 1));
        Changed(tight.EnsureTeleportMap(tight.Version, Virgil, Exterior), tight, 1);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, tight.EnsureTeleportMap(tight.Version, Sogg, Exterior).Failure);
    }

    [TestMethod]
    public void The_heros_change_of_place_gives_every_traveller_the_map_it_leaves_in_the_same_revision()
    {
        using var owner = Create();
        foreach (var follower in new[] { Virgil, Sogg }) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, follower).Disposition);
        // Sogg has one already: FM6 writes only while the field is 0.
        Changed(owner.EnsureTeleportMap(owner.Version, Sogg, Cave), owner, 3);
        var list = owner.QueryTransferMoveList(new());
        var revision = owner.Version.Revision;
        var result = owner.ApplyChanges(owner.Version, new ActorPlacement(Cave, new(10, 20), NeighborDirection.XPlus), [], list);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the move and the fields");
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Virgil), "the map the hero left, not the one it arrived on");
        Assert.AreEqual(Cave.RuntimeMap, owner.QueryTeleportMap(Sogg), "an earlier teleport map stays");
        // The second move writes none: the field is set.
        var again = owner.ApplyChanges(owner.Version, new ActorPlacement(Exterior, new(5, 5), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new()));
        Assert.AreEqual(GameSessionDisposition.Changed, again.Disposition);
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Virgil));
    }

    [TestMethod]
    public void A_move_on_the_same_map_prepares_the_travellers_too()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Virgil).Disposition);
        var result = owner.ApplyChanges(owner.Version, new ActorPlacement(Exterior, new(5, 5), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new()));
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Virgil), "FM6 runs for both cases");
    }

    [TestMethod]
    public void A_party_with_no_mobile_object_ids_gets_no_field()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "npc-1").Disposition);
        var result = owner.ApplyChanges(owner.Version, new ActorPlacement(Cave, new(10, 20), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new()));
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(0, owner.Capture().Maps.Sum(map => map.Changes.Count), "an entity that is not a mobile object is left out");
        Assert.IsNull(owner.QueryTeleportMap("npc-1"));
    }

    [TestMethod]
    public void A_non_player_mover_gets_its_teleport_map_with_its_move_on_the_open_map_and_on_another()
    {
        foreach (var map in new[] { Exterior.RuntimeMap, Cave.RuntimeMap })
        {
            using var owner = Create();
            var submitted = owner.RequestChangeOfPlace(Wolf, ChangeOfPlaceCaller.NpcWaypoint, map, new(10, 20), ChangeOfPlaceFlags.None, 0, _ => true);
            Assert.IsTrue(submitted.Accepted);
            var run = owner.ProcessChangeOfPlace(new Host());
            Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run.Status, run.Failure.ToString());
            Assert.AreEqual(Exterior.RuntimeMap, owner.QueryTeleportMap(Wolf), "the map it left, whether or not it goes to another");
            Assert.AreEqual(Exterior, owner.QueryActor().Map, "the hero stays where it is");
        }
    }

    [TestMethod]
    public void A_mover_that_has_a_teleport_map_keeps_it()
    {
        using var owner = Create();
        Changed(owner.EnsureTeleportMap(owner.Version, Wolf, Cave), owner, 1);
        Assert.IsTrue(owner.RequestChangeOfPlace(Wolf, ChangeOfPlaceCaller.NpcWaypoint, Cave.RuntimeMap, new(10, 20), ChangeOfPlaceFlags.None, 0, _ => true).Accepted);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, owner.ProcessChangeOfPlace(new Host()).Status);
        Assert.AreEqual(Cave.RuntimeMap, owner.QueryTeleportMap(Wolf), "the homecoming's own move never rewrites the tie");
    }

    [TestMethod]
    public void A_teleport_map_survives_a_save_and_a_load()
    {
        using var owner = Create();
        Changed(owner.EnsureTeleportMap(owner.Version, Virgil, Exterior), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"schemaVersion\":74,");
        StringAssert.Contains(text, "\"state\":[{\"entity\":\"oid:virgil\",\"property\":\"npc.teleport\",\"map\":79,\"value\":79}]");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(Exterior.RuntimeMap, restored.QueryTeleportMap(Virgil));
        // A teleport map left in the maps section is a damaged save (it is an object field), and a static object has none.
        var inMaps = text.Replace("\"maps\":[]", "\"maps\":[{\"map\":79,\"changes\":[{\"scope\":\"entity\",\"entity\":\"oid:virgil\",\"property\":\"npc.teleport\",\"value\":79}]}]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(inMaps));
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, RestoreFailure(text.Replace("\"entity\":\"oid:virgil\"", "\"entity\":\"sector:5:2\"")));
    }

    [TestMethod]
    public void The_sixty_eight_to_sixty_nine_step_moves_nothing_and_needs_the_objects_state()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema68(current);
        StringAssert.Contains(older, "\"schemaVersion\":68,\"world\"");
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(Decode(older)), "only the schema differs");
        // A schema 68 text that carries a teleport map is the older build's text that never wrote one: the field is refused as unknown to it only when
        // it is kept out of the objects section, so the step leaves the rows it finds for the strict decode.
        Changed(owner.EnsureTeleportMap(owner.Version, Virgil, Exterior), owner, 1);
        var withField = SnapshotUpgradeTests.Schema68(GameSessionSnapshotCodec.Encode(owner.Capture()));
        using var restored = Restore(Decode(withField));
        Assert.AreEqual(Exterior.RuntimeMap, restored.QueryTeleportMap(Virgil));
        // The step needs the objects state the older schema always had.
        var missing = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"state\":[]", ""));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.objects.state"), (missing.Failure, missing.Path));
        var wrong = GameSessionSnapshotCodec.TryDecode(older.Replace("\"state\":[]", "\"state\":{}"));
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.objects.state"), (wrong.Failure, wrong.Path));
    }

    private static GameSessionFailure RestoreFailure(string text) =>
        GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot, RestoreOptions()).Failure;
}
