using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b): the whole mobile read's rows over invented facts, no original data. Holders come from the lists (the original finds a
/// holder's items through its list, RL3), an OF_INVENTORY item no list names is detached, statics are counted and left out, and anything the
/// residency cannot hold is a typed refusal that names the object.
/// </summary>
[TestClass]
public sealed class WholeMapMobileRowsTests
{
    private const uint Dynamic = 0x2000, Inventory = 0x1000, Off = 0x2, Destroyed = 0x1;
    private const int Wall = 0, Container = 2, Scenery = 3, Weapon = 5, Gold = 8, Pc = 15, Npc = 16;

    private static long Packed(long x, long y) => x | y << 32;
    private static MobileObjectFacts Fact(string id, int type, uint flags = 0, long? at = null, params string[] held) => new("oid:" + id, type, flags, at, held.Select(h => "oid:" + h).ToArray());

    [TestMethod]
    public void Lists_decide_the_holder_and_unlisted_inventory_items_are_detached()
    {
        var build = WholeMapMobileRows.Build([
            Fact("npc", Npc, 0, Packed(70, 80), "sword", "coin"),
            Fact("chest", Container, 0, Packed(10, 11), "gem"),
            Fact("sword", Weapon, Inventory), Fact("coin", Gold, Inventory), Fact("gem", Weapon, Inventory),
            Fact("lost", Weapon, Inventory, Packed(1, 1)),
            Fact("floor", Weapon, 0, Packed(5, 6)),
        ]);
        Assert.AreEqual(MapResidentRowsStatus.Ready, build.Status, build.Detail);
        var rows = build.Rows!.ToDictionary(row => row.Entity);
        Assert.AreEqual(7, rows.Count);
        Assert.AreEqual(new CellCoord(70, 80), rows["oid:npc"].Cell);
        Assert.AreEqual("oid:npc", rows["oid:sword"].Holder);
        Assert.IsNull(rows["oid:sword"].Cell);
        Assert.AreEqual("oid:chest", rows["oid:gem"].Holder);
        Assert.IsNull(rows["oid:lost"].Holder, "flagged held but no list names it");
        Assert.IsNull(rows["oid:lost"].Cell, "a detached item has no place");
        Assert.AreEqual(new CellCoord(5, 6), rows["oid:floor"].Cell);
    }

    [TestMethod]
    public void Statics_are_left_out_unless_dynamic_and_the_flags_are_carried()
    {
        var build = WholeMapMobileRows.Build([
            Fact("wall", Wall, 0, Packed(1, 2)), Fact("blood", Scenery, Dynamic, Packed(3, 4)), Fact("tree", Scenery, 0, Packed(5, 6)),
            Fact("gone", Weapon, Off | Destroyed, Packed(7, 8)),
        ]);
        Assert.AreEqual(MapResidentRowsStatus.Ready, build.Status, build.Detail);
        Assert.AreEqual(2, build.StaticRows);
        var rows = build.Rows!.ToDictionary(row => row.Entity);
        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows["oid:blood"].Dynamic);
        Assert.IsTrue(rows["oid:gone"].Off && rows["oid:gone"].Destroyed);
        Assert.IsFalse(rows["oid:blood"].Off);
    }

    [TestMethod]
    public void A_row_the_residency_cannot_hold_is_a_typed_refusal_naming_it()
    {
        void Refused(string why, params MobileObjectFacts[] facts)
        {
            var build = WholeMapMobileRows.Build(facts);
            Assert.AreEqual(MapResidentRowsStatus.Refused, build.Status, why);
            Assert.IsNull(build.Rows, why);
            StringAssert.Contains(build.Detail, why, build.Detail);
        }
        Refused("a PC record", Fact("hero", Pc, 0, Packed(1, 1)));
        Refused("type 18", Fact("odd", 18, 0, Packed(1, 1)));
        Refused("a duplicate object id", Fact("a", Weapon, 0, Packed(1, 1)), Fact("a", Weapon, 0, Packed(2, 2)));
        Refused("which the map does not hold", Fact("chest", Container, 0, Packed(1, 1), "ghost"));
        Refused("which is no item", Fact("chest", Container, 0, Packed(1, 1), "npc"), Fact("npc", Npc, 0, Packed(2, 2)));
        Refused("listed by two holders", Fact("a", Container, 0, Packed(1, 1), "i"), Fact("b", Container, 0, Packed(2, 2), "i"), Fact("i", Weapon, Inventory));
        Refused("no location the session holds", Fact("floor", Weapon, 0, null));
        Refused("no location the session holds", Fact("far", Weapon, 0, Packed(1L << 28, 0)));
    }

    [TestMethod]
    public void The_rows_open_a_residency_and_the_holders_items_follow_their_holder()
    {
        var build = WholeMapMobileRows.Build([
            Fact("npc", Npc, 0, Packed(70, 80), "sword"), Fact("sword", Weapon, Inventory),
            Fact("floor", Weapon, 0, Packed(5, 6)),
        ]);
        using var session = MapResidentRowsTests.Session(24);
        var opened = MapResidentRows.Open(session, new SessionMapId(24), build);
        Assert.AreEqual(ResidencyStatus.Open, opened.Status, opened.Detail);
        var residents = session.QueryResidents(new SessionMapId(24)).Residents.ToDictionary(r => r.Entity);
        Assert.AreEqual("oid:npc", residents["oid:sword"].Holder);
        Assert.IsNull(residents["oid:sword"].Sector, "a held item is in no sector");
        Assert.AreEqual(new ResidentSector(1, 1), residents["oid:npc"].Sector);
    }
}
