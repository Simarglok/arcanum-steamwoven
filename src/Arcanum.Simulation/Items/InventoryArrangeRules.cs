namespace Arcanum.Simulation.Items;

/// <summary>The two layouts of the inventory window's arrange button (ARC-DEV-339): the button flips between them on every press.</summary>
public enum InventoryArrangeLayout
{
    /// <summary>Row by row, each item at the first free cell from the left (0x4675A0); the first press after the window opens.</summary>
    Horizontal,
    /// <summary>Column by column, each item at the first free cell from the top (0x4676A0); the second press, then back to horizontal.</summary>
    Vertical,
}

/// <summary>An item of a holder's list for <see cref="InventoryArrangeRules.Arrange"/>: a caller's key, its inventory location (field 0x5E:
/// a grid slot, 1000..1008 worn, 2000..2009 on a quick slot, anything else, such as −1, not placed) and its size in cells.</summary>
public readonly record struct InventoryArrangeItem(string Key, int Location, int Width, int Height);

/// <summary>An item's new grid slot.</summary>
public readonly record struct InventoryArrangePlacement(string Key, int Slot);

/// <summary>
/// ARC-DEV-339: the arrange button of the original's inventory window, read in the retail 1.0.7.4 executable (ledger IW37–IW40): the
/// button (art 340 <c>tiny_butt</c>, 763, 197) calls 0x4670A0 for the player with a flag it flips after every press (0 after the window
/// opens). The function takes the holder's list, leaves out every item on a worn or quick slot (the item is swapped with the list's last
/// one and the count shrinks, so the order of the rest changes), sorts the others by height, then width, largest first (an exchange sort:
/// each position takes any later item that is larger), and lays them out one by one on an empty grid at the first free place
/// (<see cref="InventoryGridRules.FindSlot"/> for the horizontal layout, <see cref="FindSlotByColumns"/> for the vertical one). When one
/// item finds no place the function stops with nothing written; otherwise it writes every slot (no insert check, no weight, no script
/// event, no action points). Godot-free.
/// </summary>
public static class InventoryArrangeRules
{
    /// <summary>Whether an inventory location is a worn slot (1000..1008) or a quick slot (2000..2009): the arrangement leaves such an item out.</summary>
    public static bool Left(int location) => location is >= 1000 and <= 1008 or >= 2000 and <= 2009;

    /// <summary>
    /// The first free slot of the vertical layout (0x4676A0): columns from the left, rows from the top within a column, the first top-left
    /// cell whose whole rectangle is free; −1 when none is.
    /// </summary>
    [OriginalFunction(0x4676A0, OriginalFunctionScope.Full)]
    public static int FindSlotByColumns(IReadOnlyList<bool> occupied, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        if (occupied.Count % InventoryGridRules.Columns != 0 || occupied.Count / InventoryGridRules.Columns is not (InventoryGridRules.ContainerRows or InventoryGridRules.CritterRows))
            throw new ArgumentOutOfRangeException(nameof(occupied));
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(width < 1 ? nameof(width) : nameof(height));
        var rows = occupied.Count / InventoryGridRules.Columns;
        for (var column = 0; column <= InventoryGridRules.Columns - width; column++)
            for (var row = 0; row <= rows - height; row++)
                if (Free(occupied, row * InventoryGridRules.Columns + column, width, height)) return row * InventoryGridRules.Columns + column;
        return -1;
    }

    /// <summary>
    /// The arrangement of a holder's list (0x4670A0): every item that is not left out gets its new slot, in the order the function lays
    /// them out; null when an item finds no place (the original then writes nothing); an empty list when nothing is to be arranged.
    /// <paramref name="list"/> is the holder's list in list order, items on worn and quick slots included (they decide the order of the
    /// rest); a size below one cell on a side is refused. PROJECT: the multiplayer branch (0x4EDE80, a client asks the server) is not built.
    /// </summary>
    [OriginalFunction(0x4670A0, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<InventoryArrangePlacement>? Arrange(IReadOnlyList<InventoryArrangeItem> list, int rows, InventoryArrangeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (rows is not (InventoryGridRules.ContainerRows or InventoryGridRules.CritterRows)) throw new ArgumentOutOfRangeException(nameof(rows));
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        if (list.Any(item => item.Width < 1 || item.Height < 1)) throw new ArgumentOutOfRangeException(nameof(list), "An item's size is below one cell.");
        var arr = list.ToArray();
        var count = arr.Length;
        // Leave out the worn and quick-slot items: each goes to the end of the list and the item that came from the end is examined next.
        for (var i = 0; i < count;)
        {
            if (!Left(arr[i].Location)) { i++; continue; }
            (arr[i], arr[count - 1]) = (arr[count - 1], arr[i]);
            count--;
        }
        if (count == 0) return [];
        // Exchange sort: position i takes any later item with a greater height, or an equal height and a greater width (ties do not move).
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
                if (arr[j].Height > arr[i].Height || arr[j].Height == arr[i].Height && arr[j].Width > arr[i].Width)
                    (arr[i], arr[j]) = (arr[j], arr[i]);
        var occupied = new bool[rows * InventoryGridRules.Columns];
        var placed = new InventoryArrangePlacement[count];
        for (var i = 0; i < count; i++)
        {
            var item = arr[i];
            var slot = item.Width > InventoryGridRules.Columns || item.Height > rows ? -1
                : layout == InventoryArrangeLayout.Horizontal ? InventoryGridRules.FindSlot(occupied, item.Width, item.Height) : FindSlotByColumns(occupied, item.Width, item.Height);
            if (slot < 0) return null;
            for (var row = 0; row < item.Height; row++)
                for (var column = 0; column < item.Width; column++)
                    occupied[slot + row * InventoryGridRules.Columns + column] = true;
            placed[i] = new(item.Key, slot);
        }
        return Array.AsReadOnly(placed);
    }

    private static bool Free(IReadOnlyList<bool> occupied, int slot, int width, int height)
    {
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
                if (occupied[slot + row * InventoryGridRules.Columns + column]) return false;
        return true;
    }
}
