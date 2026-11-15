using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>ARC-UI-025: the inventory window's drop and use icons (the player's own window; IW6).</summary>
public enum AreaInventoryIcon { Drop, Use }

/// <summary>ARC-UI-019: a point of the inventory window's doll-and-grid area: a grid cell (row × 10 + column), a wear slot, or neither;
/// ARC-UI-025: or one of the icons.</summary>
public readonly record struct AreaInventoryHit(int? Cell, EquipmentSlot? Slot, AreaInventoryIcon? Icon = null)
{
    public static AreaInventoryHit None => default;
}

/// <summary>ARC-UI-019: the item held on the cursor: its entity, the wear slot it was taken from (null: the grid) and the grab's offset in
/// grid locations (the press cell minus the item's slot; 0 for a worn item and for an occupant a swap put on the cursor).</summary>
public sealed record AreaInventoryHeld(string Entity, EquipmentSlot? Worn, int Offset)
{
    /// <summary>ARC-UI-052: the item is the looted holder's (picked from its grid in the loot window), not the hero's.</summary>
    public bool Partner { get; init; }
}

public enum AreaInventoryDropKind { PutBack, GridMove, TakeOffTo, Wear, DropToGround, UseIcon, Take, Plant, Buy, Sell, Gamble, Give, TakeWear, BuyWear, SellWear }

/// <summary>ARC-UI-019: what a release does with the held item: put it back, move it in the grid, take it off into a grid slot, or put it
/// into a wear slot; ARC-UI-025: drop it on the hero's tile (the drop icon) or use it (the use icon); ARC-UI-052: take the looted holder's
/// item (released on the hero's grid in the loot window); ARC-UI-065: plant the hero's item on the NPC whose pocket the hero picks (released
/// on its grid in the steal window); ARC-UI-064: buy the merchant's item (released on the hero's grid in the barter window) or sell the
/// hero's (released on the merchant's grid); ARC-UI-1740: bet the merchant's item (released on the bet box); ARC-UI-1925: give the hero's item
/// into the looted holder (<see cref="AreaInventoryDropKind.Give"/>, 0x575360), take the holder's item and wear it in the released hero slot
/// (<see cref="AreaInventoryDropKind.TakeWear"/>, the loot window's 0x575200 on a slot; in the steal window a steal attempt) or buy the merchant's item and wear it
/// (<see cref="AreaInventoryDropKind.BuyWear"/>); ARC-UI-2138: sell the hero's item onto a slot of the merchant's doll, which the merchant wears (<see cref="AreaInventoryDropKind.SellWear"/>, 0x575360 with 1000 + slot).</summary>
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
    /// <summary>ARC-UI-025: the icons' top-left corners in the frame (the original's 703, 385 and 703, 331 on the window, 41 lower than the
    /// frame: IW6, IW14). ARC-UI-051: their size is the size of the art the redraw last drew there (0x6813B8, 0x681388), 80 × 43 for every
    /// state of both boxes (224, 225, 342, 343; IW23).</summary>
    public const float IconX = 703, DropIconY = 385 - 41, UseIconY = 331 - 41, IconWidth = 80, IconHeight = 43;
    /// <summary>ARC-UI-051: the frame's size, the window's 800 × 400 (IW20).</summary>
    public const float FrameWidth = AreaInventoryArtLayout.Width, FrameHeight = AreaInventoryArtLayout.Height;

    /// <summary>The wear slots' rectangles (x, y, width, height) in the frame, the original's table 0x5CAC58.</summary>
    public static IReadOnlyDictionary<EquipmentSlot, (float X, float Y, float Width, float Height)> SlotRects { get; } =
        new Dictionary<EquipmentSlot, (float, float, float, float)>
        {
            [EquipmentSlot.Helmet] = (151, 107, 64, 64), [EquipmentSlot.Ring1] = (247, 107, 32, 32), [EquipmentSlot.Ring2] = (279, 107, 32, 32),
            [EquipmentSlot.Medallion] = (247, 139, 64, 32), [EquipmentSlot.Weapon] = (23, 170, 96, 128), [EquipmentSlot.Shield] = (247, 171, 96, 128),
            [EquipmentSlot.Armor] = (119, 171, 128, 160), [EquipmentSlot.Gauntlets] = (55, 107, 64, 64), [EquipmentSlot.Boots] = (150, 331, 64, 64),
        };

    /// <summary>The slot or cell under a point of the frame: the wear slots first, then the grid, then the icons; none outside all.</summary>
    [OriginalFunction(0x575CB0, OriginalFunctionScope.Partial)]
    public static AreaInventoryHit HitTest(float x, float y)
    {
        foreach (var (slot, rect) in SlotRects)
            if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height) return new(null, slot);
        if (x >= IconX && x < IconX + IconWidth)
        {
            if (y >= DropIconY && y < DropIconY + IconHeight) return new(null, null, AreaInventoryIcon.Drop);
            if (y >= UseIconY && y < UseIconY + IconHeight) return new(null, null, AreaInventoryIcon.Use);
        }
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
    /// item none. Null over an empty cell or slot or outside both. No check runs on the press. ARC-UI-025: a right release with nothing held
    /// names the same item (IW5), which the app may use.</summary>
    [OriginalFunction(0x575FA0, OriginalFunctionScope.Partial)]
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
    [OriginalFunction(0x573850, OriginalFunctionScope.Partial)]
    public static AreaInventoryDrop Release(AreaInventoryHeld held, AreaInventoryHit hit)
    {
        ArgumentNullException.ThrowIfNull(held);
        // ARC-UI-025: the drop icon drops the item on the hero's tile, the use icon uses it (IW6); the app decides the rest.
        if (hit.Icon is { } icon) return new(icon == AreaInventoryIcon.Drop ? AreaInventoryDropKind.DropToGround : AreaInventoryDropKind.UseIcon, held);
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
