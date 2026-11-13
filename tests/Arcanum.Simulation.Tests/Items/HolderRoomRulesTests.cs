using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

// ARC-AI-1654 (0x466510's room test: 0x4664C0 -> 0x466260, 0x466310, 0x4675A0): the first free slot of a holder's grid for an item. Sizes and locations are invented.
[TestClass]
public sealed class HolderRoomRulesTests
{
    private static InventoryGridRules.HolderListItem At(int location, int width = 1, int height = 1) => new(location, width, height);

    private static int Slot(int rows, int width, int height, params InventoryGridRules.HolderListItem[] list) => InventoryGridRules.HolderFreeSlot(list, rows, width, height);

    [TestMethod]
    public void An_empty_grid_gives_the_first_cell_and_items_mark_their_rectangles()
    {
        Assert.AreEqual(0, Slot(InventoryGridRules.CritterRows, 2, 2));
        Assert.AreEqual(2, Slot(InventoryGridRules.CritterRows, 1, 1, At(0, 2, 2)), "a 2 x 2 item at the corner leaves the third cell");
        Assert.AreEqual(20, Slot(InventoryGridRules.CritterRows, 3, 1, At(0, 10, 2)), "two rows taken");
    }

    [TestMethod]
    public void The_worn_and_quick_slot_items_take_no_cell()
    {
        Assert.AreEqual(0, Slot(InventoryGridRules.CritterRows, 1, 1, At(1000, 5, 5), At(1008), At(2000), At(2009, 9, 9)));
    }

    [TestMethod]
    public void A_critters_grid_has_twelve_rows_and_a_containers_ninety_six()
    {
        // Twelve full rows: no place for one more cell in a critter's grid, row 12 in a container's.
        var rows = Enumerable.Range(0, 12).Select(row => At(row * 10, 10, 1)).ToArray();
        Assert.AreEqual(-1, Slot(InventoryGridRules.CritterRows, 1, 1, rows));
        Assert.AreEqual(120, Slot(InventoryGridRules.ContainerRows, 1, 1, rows));
        Assert.AreEqual(-1, Slot(InventoryGridRules.CritterRows, 1, 13), "an item taller than the grid never fits");
        Assert.AreEqual(-1, Slot(InventoryGridRules.CritterRows, 11, 1), "nor one wider than ten cells");
    }

    [TestMethod]
    public void The_rectangle_must_be_free_as_a_whole()
    {
        // One cell taken in each of the first two rows leaves 1 x 2 places only from the second column.
        Assert.AreEqual(1, Slot(InventoryGridRules.CritterRows, 1, 2, At(0), At(10)));
        // Every row has a taken cell in the middle: a 10-wide item finds a row only below them.
        var holes = Enumerable.Range(0, 11).Select(row => At(row * 10 + 5)).ToArray();
        Assert.AreEqual(110, Slot(InventoryGridRules.CritterRows, 10, 1, holes));
    }

    [TestMethod]
    public void An_item_without_a_place_takes_the_first_free_one_after_the_placed_items_in_list_order()
    {
        // Two made items (no place) in the list: the first takes cell 0, the second the next free one; the placed 1 x 1 at cell 1 stays where it is.
        Assert.AreEqual(3, Slot(InventoryGridRules.CritterRows, 1, 1, At(-1), At(1), At(-1)));
        // Without placed items the unplaced ones fill the grid from the first cell.
        var many = Enumerable.Range(0, 120).Select(_ => At(-1)).ToArray();
        Assert.AreEqual(-1, Slot(InventoryGridRules.CritterRows, 1, 1, many));
        Assert.AreEqual(119, Slot(InventoryGridRules.CritterRows, 1, 1, many.Take(119).ToArray()));
    }

    [TestMethod]
    public void An_unplaced_item_that_finds_no_place_takes_none()
    {
        // A 10 x 12 slab fills the grid; the unplaced item after it finds nothing and so does the question.
        Assert.AreEqual(-1, Slot(InventoryGridRules.CritterRows, 1, 1, At(-1, 10, 12)));
        Assert.AreEqual(0, Slot(InventoryGridRules.CritterRows, 1, 1, At(-1, 10, 13)), "an unplaced item taller than the grid takes no cell");
    }

    [TestMethod]
    public void A_rectangle_over_the_right_edge_continues_in_the_next_row_and_one_past_the_buffer_is_dropped()
    {
        // The original's buffer is flat: a 3-wide item at column 8 marks cells 8, 9 and 10 (the next row's first cell).
        Assert.AreEqual(1, Slot(InventoryGridRules.CritterRows, 1, 1, At(8, 3, 1), At(0)), "cell 0 taken, 1 free; and cell 10 is marked");
        Assert.AreEqual(11, Slot(InventoryGridRules.CritterRows, 1, 1, At(8, 3, 1), At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7)));
        // A location past the 960-cell buffer has no known place: it takes the first free cell.
        Assert.AreEqual(1, Slot(InventoryGridRules.CritterRows, 1, 1, At(5000)));
        // A rectangle that ends past the buffer marks what is inside it and does not throw.
        Assert.AreEqual(0, Slot(InventoryGridRules.CritterRows, 1, 1, At(955, 10, 3)));
    }

    [TestMethod]
    public void An_item_of_the_largest_size_costs_no_more_than_the_buffer()
    {
        // A frame of 0 pixels is 2^27 cells a side (0x461780): the marking stops at the end of the buffer and the item takes the grid from its slot on.
        Assert.AreEqual(-1, Slot(InventoryGridRules.CritterRows, 1, 1, At(0, 1 << 27, 1 << 27)));
        Assert.AreEqual(0, Slot(InventoryGridRules.CritterRows, 1, 1, At(-1, 1 << 27, 1 << 27)), "an unplaced one wider than the grid takes none");
    }

    [TestMethod]
    public void An_item_with_a_side_below_one_cell_is_skipped_and_a_wanted_one_is_refused()
    {
        Assert.AreEqual(0, Slot(InventoryGridRules.CritterRows, 1, 1, At(0, 0, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Slot(InventoryGridRules.CritterRows, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Slot(7, 1, 1));
    }
}
