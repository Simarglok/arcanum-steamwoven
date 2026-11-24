using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-INPUT-2517 (VCG16, 0x551F80 / 0x4F28A0 / 0x440FC0 / 0x440FF0 / 0x4B9130 / 0x4B98B0 / 0x440B40): the tile box inside which the left-press pick
/// hides dead critters. The pick mask (combat mode, no Alt key) hides the PC's whole follower tree and the dead critters of this box; a corpse outside it stays
/// pickable. The box is the bounding tile box of a screen-sized pixel rectangle centred on the PC's tile (0x440FF0: the rectangle's origin is the PC tile's
/// screen point minus the screen point of the tile under the view centre, its size the game view's), each corner turned into tile coordinates by 0x4B98B0
/// (divisions by 40 truncating toward zero), clamped to the map by 0x4B9130, which gives no box when it is empty on either axis.
/// </summary>
public static class PickHideRules
{
    /// <summary>An inclusive tile box (0x440B40 tests the minimum and the maximum inclusively on both axes).</summary>
    public readonly record struct Box(long MinX, long MaxX, long MinY, long MaxY)
    {
        public bool Contains(long x, long y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }

    /// <summary>The box of the dead critters the pick hides, or null when the original lists nothing (the box is empty on an axis).</summary>
    /// <remarks>0x440FF0 builds the rectangle and 0x4B9130 turns it into the tile box.</remarks>
    /// <param name="heroX">The PC's tile (world cells, 0x440FC0 reads its location).</param>
    /// <param name="heroY">The PC's tile (world cells).</param>
    /// <param name="centreX">The point of the world pixel frame of the tile projection (<see cref="LineOfFireRules.Screen"/>, the view origin removed) that lies under the view's centre pixel.</param>
    /// <param name="centreY">Its Y.</param>
    /// <param name="viewWidth">The game view's size in pixels (0x51CEB0 of the view window).</param>
    /// <param name="viewHeight">The game view's height.</param>
    /// <param name="mapWidth">The map's tile width (0x4B9130 clamps to it); a large value when the caller does not know it (a corpse beyond the map does not exist).</param>
    /// <param name="mapHeight">The map's tile height.</param>
    public static Box? DeadBox(long heroX, long heroY, long centreX, long centreY, int viewWidth, int viewHeight, long mapWidth = int.MaxValue, long mapHeight = int.MaxValue)
    {
        if (viewWidth <= 0 || viewHeight <= 0) return null;
        // The tile under the view centre (0x4B8730 on the centre pixel), its screen point, and the PC tile's (0x4B8680): both without the view origin.
        var ct = LineOfFireRules.Tile(centreX, centreY, _ => true);
        if (ct is null) return null;
        var (hx, hy) = LineOfFireRules.Screen(new CellCoord(checked((int)heroX), checked((int)heroY)));
        var (cx, cy) = LineOfFireRules.Screen(ct.Value);
        // The rectangle's origin relative to the view's, taken back to the world frame by the view origin (the centre pixel minus the world point under it).
        long originX = hx - cx - viewWidth / 2 + centreX, originY = hy - cy - viewHeight / 2 + centreY;
        // 0x4B9130: X minimum from the top-right corner, Y minimum from the top-left, X maximum from the bottom-left, Y maximum from the bottom-right.
        var minX = TileX(originX + viewWidth, originY);
        var minY = TileY(originX, originY);
        var maxX = TileX(originX, originY + viewHeight);
        var maxY = TileY(originX + viewWidth, originY + viewHeight);
        if (minX > maxX || minY > maxY) return null;
        minX = Clamp(minX, mapWidth); maxX = Clamp(maxX, mapWidth); minY = Clamp(minY, mapHeight); maxY = Clamp(maxY, mapHeight);
        return minX != maxX && minY != maxY ? new Box(minX, maxX, minY, maxY) : null;
    }

    // 0x4B98B0 in the normal view: the two tile coordinates of a pixel, each an integer division by 40 truncating toward zero.
    private static long TileX(long px, long py) => (py - (px >> 1)) / LineOfFireRules.HalfWidth;
    private static long TileY(long px, long py) => (py + (px >> 1)) / LineOfFireRules.HalfWidth;

    // 0x4B9130: a negative bound goes to 0, one at or beyond the map's size to the last tile.
    private static long Clamp(long value, long size) => value < 0 ? 0 : value >= size ? size - 1 : value;
}
