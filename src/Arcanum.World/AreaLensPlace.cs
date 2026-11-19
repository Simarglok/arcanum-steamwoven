namespace Arcanum.World;

/// <summary>
/// ARC-UI-2714: where the round view (the lens) of a menu window stands. The original's arts put it at five different places (the inventory 11, 9; the character sheet
/// 12, 10; the loot and barter windows 16, 17; the logbook, the map and the reading window 25, 24; the schematics 50, 26; each 89 × 89 units from the window's corner).
/// With the generated interface (the Steamwoven layout) the generated window arts carry the hole at one common place, <see cref="X"/>, <see cref="Y"/>, so that
/// switching between windows leaves the lens where it was. It is the inventory window's own place, because that window's content fixes it: the item grid starts at y 107, right
/// under the lens's 89 units from y 9, so a lens any lower would cut into the grid (the median, 16, 17, would need the whole grid and the equipment doll moved 8 units down).
/// Every other window's lens only moves toward the corner, into wood that holds nothing (the loot and barter windows 5, 8 units; the sheet 1, 1; the logbook, the map and the
/// reading window 14, 15; the schematics 39, 17), so no window's content moves. <see cref="Unified"/> follows the generated interface (the art drawn is then the generated one);
/// the Original look keeps every window's own place. Godot-free.
/// </summary>
public static class AreaLensPlace
{
    /// <summary>The common place's corner, in window units.</summary>
    public const int X = 11, Y = 9;

    /// <summary>True while the generated window arts are drawn (the viewer sets it with the interface setting): the lenses stand at <see cref="X"/>, <see cref="Y"/>.</summary>
    public static bool Unified { get; set; }

    /// <summary>The corner a window's lens stands at: <paramref name="original"/> (the window's own place in the original) or the common place while <see cref="Unified"/>.</summary>
    public static (int X, int Y) Corner((int X, int Y) original) => Unified ? (X, Y) : original;
}
