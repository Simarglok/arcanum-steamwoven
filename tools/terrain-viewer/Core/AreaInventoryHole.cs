namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-054: the round view in the inventory window's top-left corner (ledger IW33, IW34). The open (0x5727B0 at 0x5732D5) centres the
/// game view on the window's holder, and the interface's view-window mode 1 (0x550DA0, refreshed by 0x551080 after every redraw of the game
/// view) copies the game window's square of 178 × 178 at (311, 96), whose centre is 15 pixels above the middle of the 800 × 400 view, into
/// an 89 × 89 rectangle of the window: the copy is scaled to half (0x521000 stretches a blit of unequal sizes); the mode's art is drawn
/// over it. The rectangle is (11, 9) with art 233 in modes 0, 5 and 6, (16, 17) with art 232 in the loot modes 2, 3 and 4 (art 231 in the
/// barter mode 1, not built here). A left release inside the rectangle (0x551000: a square test, not a circle) with nothing held closes the
/// window; with an item held, modes other than 0 put it back and close (IW18). Godot-free.
/// </summary>
public static class AreaInventoryHole
{
    /// <summary>The rectangle's side in frame pixels and the side of the game view's square it shows.</summary>
    public const int Side = 89, SourceSide = 178;
    /// <summary>The source square's centre above the game view's middle (the square's centre (400, 185) of the 800 × 400 view, 0x5C63B0).</summary>
    public const int SourceRaise = 15;
    public const int InventoryArt = 233, LootArt = 232;

    /// <summary>The rectangle's corner in the frame: the player's own window (mode 0) or a loot window.</summary>
    public static (int X, int Y) Corner(bool loot) => loot ? (16, 17) : (11, 9);

    /// <summary>The art drawn over the copy.</summary>
    public static int Art(bool loot) => loot ? LootArt : InventoryArt;

    /// <summary>True when a frame point lies inside the rectangle (left and top edges in, right and bottom out).</summary>
    public static bool Contains(bool loot, float x, float y)
    {
        var (left, top) = Corner(loot);
        return x >= left && x < left + Side && y >= top && y < top + Side;
    }

    /// <summary>The screen square the rectangle shows: centred <see cref="SourceRaise"/> game pixels above the holder's foot point, its
    /// side <see cref="SourceSide"/> game pixels, at <paramref name="zoom"/> screen pixels per game pixel.</summary>
    public static (float X, float Y, float Side) Source(float footX, float footY, float zoom)
    {
        if (!(zoom > 0) || !float.IsFinite(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom));
        var side = SourceSide * zoom;
        return (footX - side / 2, footY - SourceRaise * zoom - side / 2, side);
    }
}
