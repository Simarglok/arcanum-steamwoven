using Arcanum.Session;
using Arcanum.Simulation.Equipment;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-019: a point of the inventory window's doll-and-grid area: a grid cell (row × 10 + column), a wear slot, or neither.</summary>
public readonly record struct AreaInventoryHit(int? Cell, EquipmentSlot? Slot)
{
    public static AreaInventoryHit None => default;
}

/// <summary>ARC-UI-019: the item held on the cursor: its entity, the wear slot it was taken from (null: the grid) and the grab's offset in
/// grid locations (the press cell minus the item's slot; 0 for a worn item and for an occupant a swap put on the cursor).</summary>
public sealed record AreaInventoryHeld(string Entity, EquipmentSlot? Worn, int Offset);

public enum AreaInventoryDropKind { PutBack, GridMove, TakeOffTo, Wear }

/// <summary>ARC-UI-019: what a release does with the held item: put it back, move it in the grid, take it off into a grid slot, or put it
/// into a wear slot.</summary>
public sealed record AreaInventoryDrop(AreaInventoryDropKind Kind, AreaInventoryHeld Held, int? GridSlot = null, EquipmentSlot? Slot = null);

/// <summary>
/// ARC-UI-019: the original window's doll and grid in its own frame (the hit test 0x575CB0 after it lowers y by 41, ledger IW14): the nine
/// wear-slot rectangles, tested first, and the 10 × 12 grid of 32-pixel cells at (368, 8); and the press and release rules of IW2, IW10–IW13.
/// Godot-free: the panel draws this frame scaled and asks it what a press or a release means.
/// </summary>
public static class AreaInventoryDrag
{
    public const int CellPixels = 32;
    public const float GridX = 368, GridY = 8;
    public const float GridRight = GridX + SessionHeroGrid.Columns * CellPixels, GridBottom = GridY + SessionHeroGrid.Rows * CellPixels;
    /// <summary>The frame's size: the grid's right edge and the boots' bottom edge (331 + 64).</summary>
    public const float FrameWidth = GridRight, FrameHeight = 395;

    /// <summary>The wear slots' rectangles (x, y, width, height) in the frame, the original's table 0x5CAC58.</summary>
    public static IReadOnlyDictionary<EquipmentSlot, (float X, float Y, float Width, float Height)> SlotRects { get; } =
        new Dictionary<EquipmentSlot, (float, float, float, float)>
        {
            [EquipmentSlot.Helmet] = (151, 107, 64, 64), [EquipmentSlot.Ring1] = (247, 107, 32, 32), [EquipmentSlot.Ring2] = (279, 107, 32, 32),
            [EquipmentSlot.Medallion] = (247, 139, 64, 32), [EquipmentSlot.Weapon] = (23, 170, 96, 128), [EquipmentSlot.Shield] = (247, 171, 96, 128),
            [EquipmentSlot.Armor] = (119, 171, 128, 160), [EquipmentSlot.Gauntlets] = (55, 107, 64, 64), [EquipmentSlot.Boots] = (150, 331, 64, 64),
        };

    /// <summary>The slot or cell under a point of the frame: the wear slots first, then the grid; neither outside both.</summary>
    public static AreaInventoryHit HitTest(float x, float y)
    {
        foreach (var (slot, rect) in SlotRects)
            if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height) return new(null, slot);
        if (x < GridX || y < GridY || x >= GridRight || y >= GridBottom) return AreaInventoryHit.None;
        return new((int)((y - GridY) / CellPixels) * SessionHeroGrid.Columns + (int)((x - GridX) / CellPixels), null);
    }

    /// <summary>The grid item that covers a cell, or null.</summary>
    public static AreaGridItem? ItemAt(IReadOnlyList<AreaGridItem> grid, int cell)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (row, column) = (cell / SessionHeroGrid.Columns, cell % SessionHeroGrid.Columns);
        return grid.FirstOrDefault(item => row >= item.Slot / SessionHeroGrid.Columns && row < item.Slot / SessionHeroGrid.Columns + item.Height &&
            column >= item.Slot % SessionHeroGrid.Columns && column < item.Slot % SessionHeroGrid.Columns + item.Width);
    }

    /// <summary>A left press with nothing held (IW2): the item under the point goes on the cursor; a grid item keeps the grab's offset, a worn
    /// item none. Null over an empty cell or slot or outside both. No check runs on the press.</summary>
    public static AreaInventoryHeld? Press(AreaInventoryView view, AreaInventoryHit hit)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (hit.Slot is { } slot)
            return view.Doll.FirstOrDefault(entry => entry.Slot == slot)?.Entity is { } worn ? new(worn, slot, 0) : null;
        return hit.Cell is { } cell && ItemAt(view.Grid, cell) is { } item ? new(item.Entity, null, cell - item.Slot) : null;
    }

    /// <summary>
    /// A left release with an item held (IW10–IW13): on a grid cell the target slot is the cell minus the grab's offset (a plain subtraction
    /// in grid locations, unclamped; a negative one puts the item back), a grid move for a grid item and a take-off into that slot for a
    /// worn one; on a wear slot a wear move (its own slot puts it back); anywhere else the item goes back. The session decides the rest.
    /// </summary>
    public static AreaInventoryDrop Release(AreaInventoryHeld held, AreaInventoryHit hit)
    {
        ArgumentNullException.ThrowIfNull(held);
        if (hit.Cell is { } cell)
        {
            var target = cell - held.Offset;
            if (target < 0) return new(AreaInventoryDropKind.PutBack, held);
            return new(held.Worn is null ? AreaInventoryDropKind.GridMove : AreaInventoryDropKind.TakeOffTo, held, GridSlot: target);
        }
        if (hit.Slot is { } slot && slot != held.Worn) return new(AreaInventoryDropKind.Wear, held, Slot: slot);
        return new(AreaInventoryDropKind.PutBack, held);
    }
}
