using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

// ARC-DEV-091 (MIG6d-3): the object directory's records of junk piles and pools of blood (GameSessionObjectRecords). Values are invented.
[TestClass]
public sealed class ObjectRecordsTests
{
    private static readonly SessionMapId Map = new(79);
    private static readonly CellCoord Cell = new(10, 20);

    private static SessionJunkPile Pile(string entity, params string[] items) =>
        new(entity, Map, Cell, Array.AsReadOnly(items.Select((item, slot) => new SessionJunkPileItem(item, slot, 1, 1)).ToArray()));

    private static SessionBloodPool Pool(string entity) => new(entity, "oid:wolf", 5, Map, Cell, 1, 2, 9);

    [TestMethod]
    public void AnItemFindsItsPileAndAPileItsRecordInOneStep()
    {
        var records = new ObjectRecords();
        records.AddPile(Pile("junk:1", "apple", "pear"));
        records.AddPile(Pile("junk:2", "cup"));
        Assert.AreEqual("junk:1", records.PileOf("pear")!.Entity);
        Assert.AreEqual("junk:2", records.PileOf("cup")!.Entity);
        Assert.IsNull(records.PileOf("none"));
        Assert.AreEqual(3, records.PileItems);
        CollectionAssert.AreEqual(new[] { "junk:1", "junk:2" }, records.Piles.Select(pile => pile.Entity).ToArray(), "made order");

        var left = records.LeaveItem("apple")!;
        CollectionAssert.AreEqual(new[] { "pear" }, left.Items.Select(item => item.Entity).ToArray());
        Assert.IsNull(records.PileOf("apple"));
        Assert.AreEqual(2, records.PileItems);
        Assert.IsNull(records.LeaveItem("apple"), "the item is in no pile now");
        CollectionAssert.AreEqual(new[] { "junk:1", "junk:2" }, records.Piles.Select(pile => pile.Entity).ToArray(), "a pile keeps its place when it changes");

        Assert.IsTrue(records.RemovePile("junk:1"));
        Assert.IsNull(records.PileOf("pear"), "a removed pile holds no item");
        Assert.IsNull(records.Pile("junk:1"));
        Assert.IsFalse(records.RemovePile("junk:1"));
        Assert.AreEqual(1, records.PileItems);
    }

    [TestMethod]
    public void ABrokenInvariantIsRefusedNotStored()
    {
        var records = new ObjectRecords();
        records.AddPile(Pile("junk:1", "apple"));
        Assert.ThrowsExactly<InvalidOperationException>(() => records.AddPile(Pile("junk:1", "pear")), "one entity, one pile");
        Assert.ThrowsExactly<InvalidOperationException>(() => records.AddPile(Pile("junk:2", "apple")), "an item lies in one pile");
        Assert.ThrowsExactly<InvalidOperationException>(() => records.ReplacePile(Pile("junk:9", "pear")), "no such pile");
        records.AddPile(Pile("junk:2", "cup"));
        Assert.ThrowsExactly<InvalidOperationException>(() => records.ReplacePile(Pile("junk:2", "cup", "apple")), "an item of another pile");
        Assert.AreEqual("junk:1", records.PileOf("apple")!.Entity, "the refusal changed nothing");
        records.AddPool(Pool("pool:1"));
        Assert.ThrowsExactly<InvalidOperationException>(() => records.AddPool(Pool("pool:1")));
    }

    [TestMethod]
    public void PoolsAreFoundByEntityAndCapturedWithTheMarks()
    {
        var records = new ObjectRecords { LastJunkNumber = 4, LastPoolNumber = 6 };
        records.AddPool(Pool("pool:5"));
        records.AddPool(Pool("pool:6"));
        Assert.AreEqual("pool:6", records.Pool("pool:6")!.Entity);
        Assert.IsTrue(records.RemovePool("pool:5"));
        Assert.IsFalse(records.RemovePool("pool:5"));
        records.AddPile(Pile("junk:4", "apple"));
        var captured = records.Capture();
        CollectionAssert.AreEqual(new[] { "pool:6" }, captured.BloodPools.Select(pool => pool.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "junk:4" }, captured.JunkPiles.Select(pile => pile.Entity).ToArray());
        Assert.AreEqual((4, 6), (captured.LastJunkNumber, captured.LastPoolNumber), "the marks stay above numbers that were given up");
    }
}
