using Arcanum.Data.Area;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-103 (MIG9 S3, K1 §3.2): World reads a map's mobile rows into the session's residency rows, over an invented map, no original data. The
/// rows' holders come from the inventory seed, their flags from the object flags field (slot 18), statics are not read.
/// </summary>
[TestClass]
public sealed class MapResidentRowsTests
{
    private const int Map = 5;
    private const uint Inventory = 0x1000, Dynamic = 0x2000, Off = 0x2, Destroyed = 0x1;

    private static AreaObjectKey Key(int index, AreaSourceKind kind = AreaSourceKind.Mobile) =>
        new(new string('a', 64), Map, kind, 1, index, index * 100L, index.ToString("x").PadLeft(64, '0'), 100);

    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaObjectRecord Row(int index, int type, uint flags, long? x, long? y, AreaSourceKind kind = AreaSourceKind.Mobile) =>
        new(Key(index, kind), 0, type, 1, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
            [new AreaFieldValue(18, AreaPresence.Value, AreaValueOrigin.Instance, flags, [], null)], true, true, true);

    // An NPC (0) holding a sword (1), a container (2) holding nothing, a ground apple (3), an off and destroyed stone (4), a dynamic scenery (5),
    // a detached ring (6, flagged held, no list) and a static wall read from a mobile file (7, OF_DYNAMIC clear).
    private static (AreaInventoryMap Inventory, AreaObjectRecord[] Objects) Fixture(bool window = false)
    {
        var objects = new[]
        {
            Row(0, 16, 0, 10, 20), Row(1, 5, Inventory, 10, 20), Row(2, 2, 0, 70, 20), Row(3, 9, 0, 90, 200), Row(4, 14, Off | Destroyed, 91, 200),
            Row(5, 3, Dynamic, 12, 22), Row(6, 14, Inventory, null, null), Row(7, 0, 0, 1, 1),
        };
        var item = new SessionItemFacts(new(SessionItemType.Weapon, 1, 0, 0), 1, 0, -1, 1, []);
        var seed = new SessionMapInventorySeed(
            [new SessionSeedHolder(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)]), new SessionSeedHolder(Entity(2), SessionHolderKind.Container, false, false, [], [])],
            [new SessionSeedItem(Entity(1), item, null), new SessionSeedItem(Entity(3), item with { Kind = item.Kind with { Type = SessionItemType.Food } }, new(90, 200)),
             new SessionSeedItem(Entity(4), item with { Kind = item.Kind with { Type = SessionItemType.Generic } }, new(91, 200))]) { Window = window };
        var rows = new[] { 0, 1, 2, 3, 4 }.ToDictionary(Entity, index => objects[index], StringComparer.Ordinal);
        return (new AreaInventoryMap(Map, seed, rows), objects);
    }

    /// <summary>A session whose hero stands on <paramref name="map"/> (invented profile and flags).</summary>
    internal static GameSessionOwner Session(int map)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.Parse("11111111-1111-1111-1111-111111111111")), new ModuleId(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var created = GameSessionOwner.Create(new GameSessionDescriptor(
            new ProfileBinding(1, "steamwoven-normalized-clock-v1", "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52", new string('a', 64), new string('c', 64), 1885, 15),
            new GlobalFlagOwnerDescriptor(identity, new OwnerEpoch(Guid.Parse("33333333-3333-3333-3333-333333333333")), 0, [new FlagId(3)], []),
            new ActorPlacement(new SessionMapId(map), new(100, 200), NeighborDirection.XPlus)));
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    [TestMethod]
    public void A_maps_mobile_rows_become_residency_rows_with_their_holders_and_flags()
    {
        var (inventory, objects) = Fixture();
        var build = MapResidentRows.Build(inventory, [.. objects, Row(8, 16, 0, 1, 1, AreaSourceKind.Static)]);
        Assert.AreEqual(MapResidentRowsStatus.Ready, build.Status, build.Detail);
        Assert.AreEqual(1, build.StaticRows, "the wall without OF_DYNAMIC is static (RL1)");
        var rows = build.Rows!.ToDictionary(row => row.Entity);
        Assert.AreEqual(7, rows.Count, "the static-source row and the static wall are not read");
        Assert.AreEqual(new SessionResidentRow(Entity(0), SessionResidentKind.Npc, new CellCoord(10, 20)), rows[Entity(0)]);
        Assert.AreEqual(new SessionResidentRow(Entity(1), SessionResidentKind.Weapon, null, Entity(0)), rows[Entity(1)], "held: the seed's holder, no cell");
        Assert.AreEqual(new SessionResidentRow(Entity(4), SessionResidentKind.Generic, new CellCoord(91, 200)) { Off = true, Destroyed = true }, rows[Entity(4)]);
        Assert.AreEqual(new SessionResidentRow(Entity(5), SessionResidentKind.Scenery, new CellCoord(12, 22)) { Dynamic = true }, rows[Entity(5)]);
        Assert.AreEqual(new SessionResidentRow(Entity(6), SessionResidentKind.Generic, null), rows[Entity(6)], "detached: no place");

        // Pushed into the session of the same map: every row is a resident.
        using var session = Session(Map);
        Assert.AreEqual(new ResidencyOpenResult(ResidencyStatus.Open, 7, string.Empty), MapResidentRows.Open(session, new SessionMapId(Map), build));
        CollectionAssert.AreEqual(new[] { Entity(0), Entity(5) }, session.QueryResidentsIn(new(Map), new(0, 0)).Residents.Select(r => r.Entity).ToArray());
    }

    [TestMethod]
    public void A_window_or_a_row_the_session_cannot_hold_is_refused()
    {
        var (window, objects) = Fixture(window: true);
        Assert.AreEqual(MapResidentRowsStatus.WindowOnly, MapResidentRows.Build(window, objects).Status);
        var (inventory, _) = Fixture();
        Assert.AreEqual(MapResidentRowsStatus.Refused, MapResidentRows.Build(inventory, [Row(9, 18, 0, 1, 1)]).Status, "a type outside the original's");
        Assert.AreEqual(MapResidentRowsStatus.Refused, MapResidentRows.Build(inventory, [Row(9, 15, 0, 1, 1)]).Status, "a PC record");
        Assert.AreEqual(MapResidentRowsStatus.Refused, MapResidentRows.Build(inventory, [Row(9, 14, 0, null, 1)]).Status, "on the ground with no location");
        Assert.AreEqual(MapResidentRowsStatus.Refused, MapResidentRows.Build(inventory, [Row(9, 14, 0, -1, 1)]).Status, "a location the session cannot hold");
        Assert.AreEqual(MapResidentRowsStatus.MapMismatch, MapResidentRows.Build(inventory, [Row(9, 14, 0, 1, 1) with { Key = Key(9) with { RuntimeMap = Map + 1 } }]).Status,
            "a row of another map");
        var refused = MapResidentRows.Build(inventory, [Row(9, 18, 0, 1, 1)]);
        Assert.IsNull(refused.Rows);
    }
}
