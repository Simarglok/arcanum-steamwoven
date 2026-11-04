using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

// ARC-DEV-090 (MIG6d-2, TM2, GR17): the hero's lists (kit, taken, made, placed) and the given records as directory entries found by entity.
// Entities and kinds are invented.
[TestClass]
public sealed class HeroListsTests
{
    private static readonly SessionItemKind Generic = new(SessionItemType.Generic, 3600, 1, 2), Food = new(SessionItemType.Food, 3601, 3, 4);
    private static readonly SessionMapId Map = new(1), Origin = new(2);
    private static readonly CellCoord Cell = new(10, 20), Other = new(11, 20);

    private static SessionPlacedItem Placed(string entity, SessionPlacedSource source = SessionPlacedSource.Made) =>
        new(entity, Generic, source, null, Map, Cell);

    [TestMethod]
    public void AnItemIsFoundByItsEntityInTheListThatHoldsIt()
    {
        var lists = new HeroLists();
        lists.AddKit(new("kit:1", Generic));
        lists.AddPc(new("oid:a", Origin, Food));
        lists.AddMade(new("made:1", Generic, 7));
        lists.AddPlaced(Placed("made:2"));

        Assert.AreEqual(HeroPlace.Kit, lists.PlaceOf("kit:1"));
        Assert.AreEqual(HeroPlace.Pc, lists.PlaceOf("oid:a"));
        Assert.AreEqual(HeroPlace.Made, lists.PlaceOf("made:1"));
        Assert.AreEqual(HeroPlace.Placed, lists.PlaceOf("made:2"));
        Assert.IsNull(lists.PlaceOf("kit:2"));
        Assert.AreEqual(Origin, lists.PcItem("oid:a")!.Value.Origin);
        Assert.AreEqual(7, lists.MadeItem("made:1")!.Value.Complexity);
        Assert.IsNull(lists.KitItem("oid:a"), "another list's item is not a kit item");
        Assert.IsNull(lists.PlacedItem("made:1"));
        Assert.IsTrue(lists.InPack("kit:1") && lists.InPack("oid:a") && lists.InPack("made:1"));
        Assert.IsFalse(lists.InPack("made:2"), "a placed item lies on the ground, not in the pack");
        Assert.AreEqual(Food, lists.PackKind("oid:a"));
        Assert.IsNull(lists.PackKind("made:2"));
        CollectionAssert.AreEqual(new[] { "kit:1", "oid:a", "made:1" }, lists.PackKinds.Select(item => item.Entity).ToArray(), "kit, taken, made in list order");
    }

    [TestMethod]
    public void TheKitKeepsKitNumberOrderWhenAnItemReturns()
    {
        var lists = new HeroLists();
        lists.AddKit(new("kit:1", Generic));
        lists.AddKit(new("kit:3", Generic));
        lists.InsertKit(new("kit:2", Food));
        lists.InsertKit(new("kit:4", Food));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3", "kit:4" }, lists.Kit.Select(item => item.Entity).ToArray());
        Assert.AreEqual(HeroPlace.Kit, lists.Remove("kit:2"));
        Assert.IsNull(lists.PlaceOf("kit:2"));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3", "kit:4" }, lists.Kit.Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void RemoveTakesAnItemOutOfWhicheverListHoldsIt()
    {
        var lists = new HeroLists();
        lists.AddPc(new("oid:a", Origin, Food));
        lists.AddMade(new("made:1", Generic));
        lists.AddPlaced(Placed("made:2"));
        Assert.AreEqual(HeroPlace.Pc, lists.Remove("oid:a"));
        Assert.AreEqual(HeroPlace.Made, lists.Remove("made:1"));
        Assert.AreEqual(HeroPlace.Placed, lists.Remove("made:2"));
        Assert.IsNull(lists.Remove("made:2"), "a second remove finds nothing");
        Assert.AreEqual(0, lists.Held);
        Assert.AreEqual(0, lists.Pc.Count + lists.Made.Count + lists.Placed.Count);
    }

    [TestMethod]
    public void AnItemIsInOnePlaceAtATime()
    {
        var lists = new HeroLists();
        lists.AddMade(new("made:1", Generic));
        Assert.ThrowsExactly<InvalidOperationException>(() => lists.AddPlaced(Placed("made:1")));
        Assert.ThrowsExactly<InvalidOperationException>(() => lists.AddPc(new("made:1", Origin, Generic)));
        Assert.AreEqual(1, lists.Made.Count);
        Assert.AreEqual(0, lists.Placed.Count);
        lists.Remove("made:1");
        lists.AddPlaced(Placed("made:1"));
        Assert.AreEqual(HeroPlace.Placed, lists.PlaceOf("made:1"));
    }

    [TestMethod]
    public void AThrownItemKeepsItsPlaceInThePlacedOrder()
    {
        var lists = new HeroLists();
        lists.AddPlaced(Placed("made:1"));
        lists.AddPlaced(Placed("made:2"));
        Assert.IsTrue(lists.ReplacePlaced(Placed("made:1") with { Cell = Other }));
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, lists.Placed.Select(item => item.Entity).ToArray());
        Assert.AreEqual(Other, lists.PlacedItem("made:1")!.Cell);
        lists.AddMade(new("made:3", Generic));
        Assert.IsFalse(lists.ReplacePlaced(Placed("made:3")), "an item that is not placed is not replaced");
        Assert.IsFalse(lists.ReplacePlaced(Placed("made:9")));
    }

    [TestMethod]
    public void TheHeroOwnsEveryListedItemButTheOnesAScriptCreated()
    {
        var lists = new HeroLists();
        lists.AddKit(new("kit:1", Generic));
        lists.AddPlaced(Placed("made:1", SessionPlacedSource.Made));
        lists.AddPlaced(Placed("made:2", SessionPlacedSource.Created));
        Assert.AreEqual(2, lists.Held);
        Assert.AreEqual(1, lists.Created);
    }

    [TestMethod]
    public void GivenRecordsAreFoundByEntityInOrdinalOrder()
    {
        var lists = new HeroLists();
        lists.SetGiven(new("oid:b", Origin));
        lists.SetGiven(new("kit:2", null));
        Assert.IsTrue(lists.IsGiven("kit:2"));
        Assert.IsFalse(lists.IsGiven("kit:1"));
        Assert.AreEqual(Origin, lists.GivenOf("oid:b")!.Value.Origin);
        CollectionAssert.AreEqual(new[] { "kit:2", "oid:b" }, lists.GivenAll.Select(item => item.Entity).ToArray());
        Assert.AreEqual(2, lists.GivenCount);
        Assert.AreEqual("kit:2", lists.TakeGiven("kit:2")!.Value.Entity);
        Assert.IsNull(lists.TakeGiven("kit:2"));
        Assert.IsNull(lists.GivenOf("kit:2"));
        Assert.AreEqual(1, lists.GivenCount);
    }
}
