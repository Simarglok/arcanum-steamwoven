namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-060: the inventory window's interface mode, the open's third argument (0x5727B0, ledger IW26, IW33): the player's own window
/// (mode 0; modes 5 and 6 draw as it), barter (mode 1), loot (mode 2; mode 4, not traced, draws as it) and steal (mode 3, the loot
/// window's frame). The viewer opens the own, loot and steal windows in the inventory panel; the barter window is its own panel, whose
/// original look (ARC-UI-064) draws the same frame in mode 1.
/// </summary>
public enum AreaInventoryMode { Own, Barter, Loot, Steal }

/// <summary>ARC-UI-060: what a left release in the round view does (0x574559..0x5745F8, ledger IW34, IW18).</summary>
public enum AreaRoundViewRelease
{
    /// <summary>Nothing held: the window closes, in every mode.</summary>
    Close,
    /// <summary>An item held outside the own window: it goes back (0x575770) and the window closes.</summary>
    PutBackAndClose,
    /// <summary>An item held in the own window: the map drop with flag 4 (IW18), which leaves the item on the game view's cursor for a
    /// throw. NOT READY in the viewer (no game cursor holds an item, and the hero throws no pack item): the panel puts it back and the
    /// window stays open, as a release elsewhere in the frame.</summary>
    MapDrop,
}

/// <summary>
/// ARC-UI-054: the round view in the inventory window's top-left corner (ledger IW33, IW34). The open (0x5727B0 at 0x5732D5) centres the
/// game view on the window's holder, and the interface's view-window mode 1 (0x550DA0, refreshed by 0x551080 after every redraw of the game
/// view) copies the game window's square of 178 × 178 at (311, 96), whose centre is 15 pixels above the middle of the 800 × 400 view, into
/// an 89 × 89 rectangle of the window: the copy is scaled to half (0x521000 stretches a blit of unequal sizes); the mode's art is drawn
/// over it. ARC-UI-060: by the window's mode (<see cref="AreaInventoryMode"/>), the rectangle is (11, 9) with art 233 in the own window
/// (modes 0, 5 and 6), (16, 17) with art 231 in the barter window (mode 1) and (16, 17) with art 232 in the loot and steal windows (modes
/// 2, 3 and 4); a left release inside it (0x551000: a square test, not a circle) is <see cref="Release"/>'s. Godot-free.
/// </summary>
public static class AreaInventoryHole
{
    /// <summary>The rectangle's side in frame pixels and the side of the game view's square it shows.</summary>
    public const int Side = 89, SourceSide = 178;
    /// <summary>The source square's centre above the game view's middle (the square's centre (400, 185) of the 800 × 400 view, 0x5C63B0).</summary>
    public const int SourceRaise = 15;
    /// <summary>The arts over the copy (all three in <see cref="AreaInventoryArtContent.Numbers"/>; ARC-UI-064 drew 231 in the barter
    /// window's original look).</summary>
    public const int InventoryArt = 233, BarterArt = 231, LootArt = 232;

    /// <summary>The rectangle's corner in the frame.</summary>
    public static (int X, int Y) Corner(AreaInventoryMode mode) => Checked(mode) == AreaInventoryMode.Own ? (11, 9) : (16, 17);

    /// <summary>The art drawn over the copy.</summary>
    public static int Art(AreaInventoryMode mode) => Checked(mode) switch
    {
        AreaInventoryMode.Own => InventoryArt,
        AreaInventoryMode.Barter => BarterArt,
        _ => LootArt,
    };

    /// <summary>True when a frame point lies inside the rectangle (left and top edges in, right and bottom out).</summary>
    public static bool Contains(AreaInventoryMode mode, float x, float y)
    {
        var (left, top) = Corner(mode);
        return x >= left && x < left + Side && y >= top && y < top + Side;
    }

    /// <summary>A left release inside the rectangle (IW34): with nothing held the window closes; with an item held the own window (mode 0
    /// only: modes 5 and 6, which the viewer never opens, put it back) makes the map drop (IW18) and every other mode puts the item back
    /// and closes.</summary>
    public static AreaRoundViewRelease Release(AreaInventoryMode mode, bool held) =>
        Checked(mode) == AreaInventoryMode.Own && held ? AreaRoundViewRelease.MapDrop
        : held ? AreaRoundViewRelease.PutBackAndClose : AreaRoundViewRelease.Close;

    /// <summary>The screen square the rectangle shows: centred <see cref="SourceRaise"/> game pixels above the holder's foot point, its
    /// side <see cref="SourceSide"/> game pixels, at <paramref name="zoom"/> screen pixels per game pixel.</summary>
    public static (float X, float Y, float Side) Source(float footX, float footY, float zoom)
    {
        if (!(zoom > 0) || !float.IsFinite(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom));
        var side = SourceSide * zoom;
        return (footX - side / 2, footY - SourceRaise * zoom - side / 2, side);
    }

    private static AreaInventoryMode Checked(AreaInventoryMode mode) =>
        Enum.IsDefined(mode) ? mode : throw new ArgumentOutOfRangeException(nameof(mode));
}
