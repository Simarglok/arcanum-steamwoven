using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>ARC-DEV-339: the arrange button's function 0x4670A0 and its column finder 0x4676A0 (ledger IW39, IW40). Values are invented.</summary>
[TestClass]
public sealed class InventoryArrangeRulesTests
{
    private static InventoryArrangeItem Item(string key, int location, int width = 1, int height = 1) => new(key, location, width, height);

    private static string Layout(IReadOnlyList<InventoryArrangePlacement>? placed) =>
        placed is null ? "none" : string.Join(" ", placed.Select(p => $"{p.Key}@{p.Slot}"));

    [TestMethod]
    public void TheTallestThenWidestItemsComeFirstAndTakeTheFirstFreePlaceOfTheLayout()
    {
        InventoryArrangeItem[] list = [Item("a", 0), Item("b", 1, 2, 2), Item("c", 2, 1, 3)];
        Assert.AreEqual("c@0 b@1 a@3", Layout(InventoryArrangeRules.Arrange(list, 12, InventoryArrangeLayout.Horizontal)), "rows: c 1 x 3 at the corner, b beside it, a after b");
        Assert.AreEqual("c@0 b@30 a@50", Layout(InventoryArrangeRules.Arrange(list, 12, InventoryArrangeLayout.Vertical)), "columns: column 0 takes c, b under it, a under b");
    }

    [TestMethod]
    public void AWiderItemOfTheSameHeightGoesFirstAndTiesDoNotMove()
    {
        Assert.AreEqual("b@0 a@2", Layout(InventoryArrangeRules.Arrange([Item("a", 0), Item("b", 1, 2, 1)], 12, InventoryArrangeLayout.Horizontal)));
        Assert.AreEqual("a@0 b@1 c@2", Layout(InventoryArrangeRules.Arrange([Item("a", 0), Item("b", 1), Item("c", 2)], 12, InventoryArrangeLayout.Horizontal)));
        // The exchange sort puts the later, taller item in the first position and the displaced one in its place: [a, b, c(2)] gives [c, b, a].
        Assert.AreEqual("c@0 b@1 a@2", Layout(InventoryArrangeRules.Arrange([Item("a", 0), Item("b", 1), Item("c", 2, 1, 2)], 12, InventoryArrangeLayout.Horizontal)));
    }

    [TestMethod]
    public void WornAndQuickSlotItemsStayOutAndTheLastItemTakesTheirPlaceInTheList()
    {
        Assert.AreEqual("a@0", Layout(InventoryArrangeRules.Arrange([Item("w", 1000), Item("a", 0), Item("q", 2009)], 12, InventoryArrangeLayout.Horizontal)));
        // The worn first item is swapped with the last, so the others are laid out c, a, b: ties keep that order.
        Assert.AreEqual("c@0 a@1 b@2", Layout(InventoryArrangeRules.Arrange([Item("w", 1008), Item("a", 0), Item("b", 1), Item("c", 2)], 12, InventoryArrangeLayout.Horizontal)));
        // A swapped-in last item that is itself left out is examined too: [w1, a, w2] gives a alone.
        Assert.AreEqual("a@0", Layout(InventoryArrangeRules.Arrange([Item("w1", 1000), Item("a", 0), Item("w2", 1001)], 12, InventoryArrangeLayout.Horizontal)));
        Assert.AreEqual(0, InventoryArrangeRules.Arrange([Item("w", 1000), Item("q", 2000)], 12, InventoryArrangeLayout.Horizontal)!.Count);
        Assert.AreEqual(0, InventoryArrangeRules.Arrange([], 12, InventoryArrangeLayout.Vertical)!.Count);
        Assert.IsTrue(InventoryArrangeRules.Left(1000) && InventoryArrangeRules.Left(1008) && InventoryArrangeRules.Left(2000) && InventoryArrangeRules.Left(2009));
        Assert.IsFalse(InventoryArrangeRules.Left(999) || InventoryArrangeRules.Left(1009) || InventoryArrangeRules.Left(1999) || InventoryArrangeRules.Left(2010) || InventoryArrangeRules.Left(-1));
    }

    [TestMethod]
    public void AnItemWithoutAPlaceStopsTheWholeArrangement()
    {
        // 13 rows of 10 cells in a 12-row grid.
        var list = Enumerable.Range(0, 13).Select(i => Item("r" + i, i, 10, 1)).ToArray();
        Assert.IsNull(InventoryArrangeRules.Arrange(list, InventoryGridRules.CritterRows, InventoryArrangeLayout.Horizontal));
        Assert.IsNull(InventoryArrangeRules.Arrange(list, InventoryGridRules.CritterRows, InventoryArrangeLayout.Vertical));
        Assert.AreEqual(13, InventoryArrangeRules.Arrange(list, InventoryGridRules.ContainerRows, InventoryArrangeLayout.Vertical)!.Count, "a container's 96 rows hold them");
        Assert.IsNull(InventoryArrangeRules.Arrange([Item("wide", 0, 11, 1)], 12, InventoryArrangeLayout.Horizontal), "wider than the grid");
    }

    [TestMethod]
    public void TheColumnFinderScansColumnsFromTheLeftAndRowsFromTheTop()
    {
        var occupied = new bool[InventoryGridRules.CritterRows * 10];
        Assert.AreEqual(0, InventoryArrangeRules.FindSlotByColumns(occupied, 2, 2));
        occupied[0] = true;
        Assert.AreEqual(10, InventoryArrangeRules.FindSlotByColumns(occupied, 1, 1), "row 1 of column 0, where the row finder would say 1");
        Assert.AreEqual(1, InventoryGridRules.FindSlot(occupied, 1, 1));
        Assert.AreEqual(10, InventoryArrangeRules.FindSlotByColumns(occupied, 1, 11), "eleven rows from row 1 fit column 0 exactly");
        Assert.AreEqual(1, InventoryArrangeRules.FindSlotByColumns(occupied, 1, 12), "a full column goes to column 1");
        Assert.AreEqual(-1, InventoryArrangeRules.FindSlotByColumns(occupied, 1, 13));
        Assert.AreEqual(-1, InventoryArrangeRules.FindSlotByColumns(occupied, 11, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryArrangeRules.FindSlotByColumns(new bool[11], 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryArrangeRules.FindSlotByColumns(occupied, 0, 1));
    }

    [TestMethod]
    public void BadInputIsRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryArrangeRules.Arrange([Item("a", 0, 0, 1)], 12, InventoryArrangeLayout.Horizontal));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryArrangeRules.Arrange([Item("a", 0)], 13, InventoryArrangeLayout.Horizontal));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InventoryArrangeRules.Arrange([Item("a", 0)], 12, (InventoryArrangeLayout)5));
    }
}
