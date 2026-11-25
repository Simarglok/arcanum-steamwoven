namespace Arcanum.World;

/// <summary>
/// ARC-UI-2714: where the round view (the lens) of a menu window stands. The original's arts put it at five different places (the inventory 11, 9; the character sheet
/// 12, 10; the loot and barter windows 16, 17; the logbook, the map and the reading window 25, 24; the schematics 50, 26; each 89 × 89 units from the window's corner).
/// With a kit window background (the Steamwoven layout, ARC-UI-3143: the generated window arts are retired; the fallback is the HD pack's art) the background carries the hole at one common place, <see cref="X"/>, <see cref="Y"/>, so that
/// switching between windows leaves the lens where it was. It is the inventory window's own place, because that window's content fixes it: the item grid starts at y 107, right
/// under the lens's 89 units from y 9, so a lens any lower would cut into the grid (the median, 16, 17, would need the whole grid and the equipment doll moved 8 units down).
/// Every other window's lens only moves toward the corner, into wood that holds nothing (the loot and barter windows 5, 8 units; the sheet 1, 1; the logbook, the map and the
/// reading window 14, 15; the schematics 39, 17), so no window's content moves. <see cref="Unified"/> follows the kit background in use (the art drawn is then the kit's);
/// the pack's art keeps every window's own place.
/// ARC-UI-3101: four of the kit's window surfaces (the inventory, the character sheet, the logbook and the schematic window) carry their own lens ring (the original's ring pixels, 46 units from the lens centre at the inner edge
/// of the gold), each where it was measured, not at the common place: while such a surface shows (<see cref="Surface"/>) the lens is the circle inside that ring, the window draws no ring art over it (one ring only) and the game view fills
/// the circle (the corner and the side <see cref="SurfaceSide"/> of its square). Godot-free.
/// </summary>
public static class AreaLensPlace
{
    /// <summary>The common place's corner, in window units.</summary>
    public const int X = 11, Y = 9;

    /// <summary>True while a kit window background is published (the viewer sets it with the background): the lenses stand at <see cref="X"/>, <see cref="Y"/>.</summary>
    public static bool Unified { get; set; }

    /// <summary>ARC-UI-3101: the side of the square round the lens circle of a kit surface that carries its ring, in window units (the circle's diameter, 2 x 46).</summary>
    public const int SurfaceSide = 92;

    /// <summary>ARC-UI-3101: the corner of the lens square of the kit surface that shows now, or null (no surface with a ring: the original's arts or the common place stand). The viewer sets it with the kit background (<c>AreaKitLens</c>).</summary>
    public static (int X, int Y)? Surface { get; set; }

    /// <summary>ARC-UI-3101: the surface carries the lens ring, so the window draws no ring art over the game view.</summary>
    public static bool SurfaceRing => Surface is not null;

    /// <summary>ARC-UI-3101: the side of the lens's square now, in window units: the original's 89, or <see cref="SurfaceSide"/> over a kit surface with a ring.</summary>
    public static int Side => Surface is null ? 89 : SurfaceSide;

    /// <summary>The corner a window's lens stands at: the shown kit surface's lens (<see cref="Surface"/>), else <paramref name="original"/> (the window's own place in the original) or the common place while <see cref="Unified"/>.</summary>
    public static (int X, int Y) Corner((int X, int Y) original) => Surface ?? (Unified ? (X, Y) : original);
}
