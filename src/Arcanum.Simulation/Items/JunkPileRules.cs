namespace Arcanum.Simulation.Items;

/// <summary>An item in a holder's inventory grid: its slot (row × <see cref="InventoryGridRules.Columns"/> + column, the original's
/// inventory location, field 0x5E) and its size in cells.</summary>
public readonly record struct InventoryGridItem(int Slot, int Width, int Height);

/// <summary>
/// ARC-WORLD-063: the original's inventory grid of a holder (0x466260, 0x466310, 0x4675A0, 0x461780, read in the retail 1.0.7.4
/// executable by two readers, ledger "Junk piles" JP3): ten columns, 96 rows for a container and 12 for a critter; an item takes
/// its inventory art's size in 32-pixel cells (1 × 1 when the art does not read) from its slot, and a new item goes to the first
/// top-left cell, row by row and column by column, where its whole rectangle is free.
/// </summary>
public static class InventoryGridRules
{
    public const int Columns = 10;
    public const int ContainerRows = 96;
    public const int CritterRows = 12;
    public const int CellPixels = 32;

    /// <summary>
    /// An item's size in cells from its inventory art's frame in pixels (0x461780): each side (pixels − 1) / 32 + 1 in unsigned
    /// 32-bit arithmetic, so a side of 0 pixels is 2^27 cells and never fits; null (the art does not read) is 1 × 1.
    /// </summary>
    [OriginalFunction(0x461780, OriginalFunctionScope.Full)]
    public static (int Width, int Height) CellsOf((uint Width, uint Height)? artPixels) =>
        artPixels is not { } px ? (1, 1) : ((int)(unchecked(px.Width - 1) / CellPixels + 1), (int)(unchecked(px.Height - 1) / CellPixels + 1));

    /// <summary>
    /// The holder's occupied cells (0x466260, 0x466310): each item marks its rectangle from its slot. Items in the worn slots
    /// (1000..1008) and the quick slots (2000..2009) take no cell. A rectangle that leaves the grid is refused here: the original
    /// writes past its buffer (crash class), and callers keep their slots inside it (<see cref="Fits"/>).
    /// </summary>
    [OriginalFunction(0x466260, OriginalFunctionScope.Full)]
    public static bool[] Occupied(IEnumerable<InventoryGridItem> items, int rows)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (rows is not (ContainerRows or CritterRows)) throw new ArgumentOutOfRangeException(nameof(rows));
        var grid = new bool[rows * Columns];
        foreach (var item in items)
        {
            if (item.Slot is >= 1000 and <= 1008 or >= 2000 and <= 2009) continue;
            if (!Fits(item, rows)) throw new ArgumentOutOfRangeException(nameof(items), "An item's rectangle leaves the grid.");
            for (var row = 0; row < item.Height; row++)
                for (var column = 0; column < item.Width; column++)
                    grid[item.Slot + row * Columns + column] = true;
        }
        return grid;
    }

    /// <summary>Whether an item's rectangle lies inside a grid of <paramref name="rows"/> rows.</summary>
    public static bool Fits(InventoryGridItem item, int rows) =>
        item.Slot >= 0 && item.Width >= 1 && item.Height >= 1 && item.Slot / Columns + (long)item.Height <= rows &&
        item.Slot % Columns + (long)item.Width <= Columns;

    /// <summary>
    /// The first free slot for an item of <paramref name="width"/> × <paramref name="height"/> cells (0x4675A0): rows from the top,
    /// columns from the left, the first top-left cell whose whole rectangle is free; −1 when none is (an item wider than the grid or
    /// taller than its rows included).
    /// </summary>
    [OriginalFunction(0x4675A0, OriginalFunctionScope.Full)]
    public static int FindSlot(IReadOnlyList<bool> occupied, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        if (occupied.Count % Columns != 0 || occupied.Count / Columns is not (ContainerRows or CritterRows)) throw new ArgumentOutOfRangeException(nameof(occupied));
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(width < 1 ? nameof(width) : nameof(height));
        var rows = occupied.Count / Columns;
        for (var row = 0; row <= rows - height; row++)
            for (var column = 0; column <= Columns - width; column++)
                if (Free(occupied, row * Columns + column, width, height)) return row * Columns + column;
        return -1;
    }

    private static bool Free(IReadOnlyList<bool> occupied, int slot, int width, int height)
    {
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
                if (occupied[slot + row * Columns + column]) return false;
        return true;
    }
}

/// <summary>Where a dropped item goes (0x466EF0): into a junk pile that lies on the tile, into a new junk pile made with the loose items
/// there, or loose onto the tile.</summary>
public enum JunkPilePlacementKind
{
    /// <summary>A junk pile on the tile takes the item at <see cref="JunkPilePlacement.Slot"/>.</summary>
    JoinPile,
    /// <summary>A new junk pile is made on the tile and the loose items it can hold are moved into it, in tile order.</summary>
    NewPile,
    /// <summary>The item lies loose on the tile (0x4415C0).</summary>
    Loose,
}

/// <summary>One loose item on the drop's tile: its size and whether the move into a new pile may take it (the pickup 0x4617F0, whose
/// get script can refuse it).</summary>
public readonly record struct JunkPileLooseItem(int Width, int Height, bool Movable);

/// <summary>
/// A drop's placement: the kind; for <see cref="JunkPilePlacementKind.JoinPile"/> the pile's index among the tile's piles; for
/// <see cref="JunkPilePlacementKind.NewPile"/> each loose item's slot in the new pile (−1 for one left on the ground) in tile order;
/// and the dropped item's slot (−1: it lies loose, beside a new pile when one was made).
/// </summary>
public sealed record JunkPilePlacement(JunkPilePlacementKind Kind, int Pile, IReadOnlyList<int> Swept, int Slot);

/// <summary>What happens to a junk pile after an item left it (0x463540).</summary>
public enum JunkPileDissolve
{
    /// <summary>Two or more items: nothing.</summary>
    Keep,
    /// <summary>No item: the pile is destroyed.</summary>
    Remove,
    /// <summary>One item: it leaves the pile, the pile is destroyed and the item is dropped again on the pile's tile.</summary>
    Release,
}

/// <summary>
/// ARC-WORLD-063: the original's junk piles (containers of description 3023, prototype 3043), read in the retail 1.0.7.4 executable
/// by two readers (ledger "Junk piles" JP1–JP8).
/// </summary>
public static class JunkPileRules
{
    /// <summary>The description number of a junk pile (container prototype 3043).</summary>
    public const int Description = ItemDecayRules.JunkPileDescription;

    /// <summary>The prototype a new junk pile is created from (description 3023 + 20).</summary>
    public const int Prototype = Description + 20;

    /// <summary>
    /// The placement of a dropped item of <paramref name="width"/> × <paramref name="height"/> cells (0x466EF0): the tile's junk
    /// piles, in tile order (each the grid of its items, <see cref="InventoryGridRules.Occupied"/> with container rows), are tried
    /// first and the first with a free slot takes it. Else, when loose items lie on the tile, a new pile is made: each movable item
    /// enters it at its first free slot in tile order (one that does not fit or may not move stays on the ground), then the dropped
    /// item when it fits; a dropped item that does not fit lies loose beside it. Else the item lies loose.
    /// </summary>
    [OriginalFunction(0x466EF0, OriginalFunctionScope.Full)]
    public static JunkPilePlacement Place(IReadOnlyList<IReadOnlyList<bool>> piles, IReadOnlyList<JunkPileLooseItem> loose, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(piles);
        ArgumentNullException.ThrowIfNull(loose);
        for (var pile = 0; pile < piles.Count; pile++)
            if (InventoryGridRules.FindSlot(piles[pile], width, height) is var slot and >= 0) return new(JunkPilePlacementKind.JoinPile, pile, [], slot);
        if (loose.Count == 0) return new(JunkPilePlacementKind.Loose, -1, [], -1);
        var grid = new bool[InventoryGridRules.ContainerRows * InventoryGridRules.Columns];
        var swept = new int[loose.Count];
        for (var i = 0; i < loose.Count; i++)
        {
            swept[i] = loose[i].Movable ? InventoryGridRules.FindSlot(grid, loose[i].Width, loose[i].Height) : -1;
            if (swept[i] >= 0) Mark(grid, swept[i], loose[i].Width, loose[i].Height);
        }
        return new(JunkPilePlacementKind.NewPile, -1, Array.AsReadOnly(swept), InventoryGridRules.FindSlot(grid, width, height));
    }

    /// <summary>The check after an item left a junk pile (0x463540), by the items it still holds.</summary>
    [OriginalFunction(0x463540, OriginalFunctionScope.Partial)]
    public static JunkPileDissolve Dissolve(int items) => items switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(items)),
        0 => JunkPileDissolve.Remove,
        1 => JunkPileDissolve.Release,
        _ => JunkPileDissolve.Keep,
    };

    private static void Mark(bool[] grid, int slot, int width, int height)
    {
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
                grid[slot + row * InventoryGridRules.Columns + column] = true;
    }
}
