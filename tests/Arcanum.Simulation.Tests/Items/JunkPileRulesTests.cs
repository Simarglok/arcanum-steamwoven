using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>ARC-WORLD-063: the original's inventory grid (0x461780, 0x466260, 0x4675A0) and junk pile placement and dissolve (0x466EF0,
/// 0x463540). Values are invented.</summary>
[TestClass]
public sealed class JunkPileRulesTests
{
    private static bool[] Empty() => new bool[InventoryGridRules.ContainerRows * InventoryGridRules.Columns];

    [TestMethod]
    public void AnItemTakesItsArtInThirtyTwoPixelCellsAndOneCellWithoutArt()
    {
        Assert.AreEqual((1, 1), InventoryGridRules.CellsOf(null));
        Assert.AreEqual((1, 1), InventoryGridRules.CellsOf((32, 1)));
        Assert.AreEqual((2, 3), InventoryGridRules.CellsOf((33, 96)));
        Assert.AreEqual((1 << 27, 1), InventoryGridRules.CellsOf((0, 32)), "a side of 0 pixels wraps to 2^27 cells (unsigned)");
    }

    [TestMethod]
    public void TheGridMarksEachItemsRectangleAndSkipsWornAndQuickSlots()
    {
        var grid = InventoryGridRules.Occupied([new(12, 2, 3), new(1003, 1, 1), new(2005, 1, 1)], InventoryGridRules.ContainerRows);
        Assert.AreEqual(6, grid.Count(cell => cell));
        Assert.IsTrue(grid[12] && grid[13] && grid[22] && grid[23] && grid[32] && grid[33]);
        Assert.AreEqual(InventoryGridRules.CritterRows * 10, InventoryGridRules.Occupied([], InventoryGridRules.CritterRows).Length);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryGridRules.Occupied([new(9, 2, 1)], InventoryGridRules.ContainerRows),
            "a rectangle past the last column");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryGridRules.Occupied([], 13));
        Assert.IsFalse(InventoryGridRules.Fits(new(950, 1, 2), InventoryGridRules.ContainerRows), "past the last row");
        Assert.IsTrue(InventoryGridRules.Fits(new(959, 1, 1), InventoryGridRules.ContainerRows));
    }

    [TestMethod]
    public void TheFreeSlotIsTheFirstTopLeftCellRowByRow()
    {
        var grid = Empty();
        Assert.AreEqual(0, InventoryGridRules.FindSlot(grid, 1, 1));
        grid[0] = grid[1] = true;
        Assert.AreEqual(2, InventoryGridRules.FindSlot(grid, 2, 2));
        Assert.AreEqual(2, InventoryGridRules.FindSlot(grid, 2, 1));
        Assert.AreEqual(10, InventoryGridRules.FindSlot(grid, 10, 1), "a full-width item goes to the first free row");
        // A column blocked on the second row pushes a tall item along the first row.
        grid[13] = true;
        Assert.AreEqual(4, InventoryGridRules.FindSlot(grid, 2, 2));
        Assert.AreEqual(-1, InventoryGridRules.FindSlot(grid, 11, 1), "wider than the grid");
        Assert.AreEqual(-1, InventoryGridRules.FindSlot(grid, 1, 97), "taller than its rows");
        var full = Enumerable.Repeat(true, 960).ToArray();
        full[959] = false;
        Assert.AreEqual(959, InventoryGridRules.FindSlot(full, 1, 1));
        Assert.AreEqual(-1, InventoryGridRules.FindSlot(full, 2, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryGridRules.FindSlot(grid, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryGridRules.FindSlot(new bool[15], 1, 1));
    }

    [TestMethod]
    public void ADropJoinsTheFirstPileWithRoomOrGathersTheLooseItemsIntoANewOne()
    {
        var full = Enumerable.Repeat(true, 960).ToArray();
        var withRoom = Empty();
        withRoom[0] = true;
        var join = JunkPileRules.Place([full, withRoom], [new(1, 1, true)], 1, 1);
        Assert.AreEqual((JunkPilePlacementKind.JoinPile, 1, 1), (join.Kind, join.Pile, join.Slot), "a full pile is passed over");

        Assert.AreEqual(JunkPilePlacementKind.Loose, JunkPileRules.Place([full], [], 1, 1).Kind, "no room and nothing loose: it lies loose");
        Assert.AreEqual(JunkPilePlacementKind.Loose, JunkPileRules.Place([], [], 1, 1).Kind);

        // A new pile takes the loose items in tile order, each at its first free slot, then the dropped item; one that may not move
        // (its get script refuses) stays on the ground.
        var made = JunkPileRules.Place([full], [new(2, 2, true), new(1, 1, false), new(1, 1, true)], 1, 3);
        Assert.AreEqual(JunkPilePlacementKind.NewPile, made.Kind);
        CollectionAssert.AreEqual(new[] { 0, -1, 2 }, made.Swept.ToArray());
        Assert.AreEqual(3, made.Slot);

        // A dropped item too big for the new pile lies beside it; a loose item too big stays out of it.
        var beside = JunkPileRules.Place([], [new(11, 1, true)], 1, 97);
        Assert.AreEqual((JunkPilePlacementKind.NewPile, -1), (beside.Kind, beside.Slot));
        CollectionAssert.AreEqual(new[] { -1 }, beside.Swept.ToArray(), "the original leaves the new pile empty");
    }

    [TestMethod]
    public void APileWithOneItemOrNoneDissolves()
    {
        Assert.AreEqual(JunkPileDissolve.Remove, JunkPileRules.Dissolve(0));
        Assert.AreEqual(JunkPileDissolve.Release, JunkPileRules.Dissolve(1));
        Assert.AreEqual(JunkPileDissolve.Keep, JunkPileRules.Dissolve(2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => JunkPileRules.Dissolve(-1));
        Assert.AreEqual((3023, 3043), (JunkPileRules.Description, JunkPileRules.Prototype));
    }
}
