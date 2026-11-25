using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-3101: where the lens of a kit window surface stands. The five surfaces (<c>disc_boxes</c> of <c>tools/steamwoven_ui/zone_specs</c>) keep the original's lens ring pixels where the ring was measured on each, so the ring is part of the
/// kit's background and the window draws no ring art over it (one ring, until the kit's lens ring part replaces it). The circle inside the ring is measured on the built surfaces: the inner edge of the ring's gold (red at least 70, red minus
/// blue at least 50, a least-squares circle through the first gold pixel of 360 rays, outliers beyond 5 master pixels dropped) is a circle of 46.3 units radius on all five (the window's unit is 3.6 master pixels, the master's top 150 rows are
/// the bevel band), centred at (55.87, 51.62) in the inventory, (56.71, 52.61) in the character sheet, (69.83, 66.80) in the logbook and the reading window and (94.79, 68.78) in the schematic window. The lens is the circle of 46 units round
/// the centre, rounded to whole units: its square is <see cref="AreaLensPlace.SurfaceSide"/> units, at the corners below. The game view fills the circle and the background leaves the circle out, not the square (the square's corners are the
/// surface's own wood). Godot-free.
/// </summary>
public static class AreaKitLens
{
    private static readonly Dictionary<SteamwovenKitWindow, (int X, int Y)> Corners = new()
    {
        [SteamwovenKitWindow.Inventory] = (10, 6),
        [SteamwovenKitWindow.Character] = (11, 7),
        [SteamwovenKitWindow.Logbook] = (24, 21),
        [SteamwovenKitWindow.Schematic] = (49, 23),
    };

    /// <summary>The corner of the lens square of the surface of <paramref name="window"/>, or null (no surface with a ring: the loot, barter and map backgrounds, or no kit).</summary>
    public static (int X, int Y)? CornerOf(SteamwovenKitWindow? window) => window is { } known && Corners.TryGetValue(known, out var corner) ? corner : null;
}
