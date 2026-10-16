using Arcanum.Simulation.Movement;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Screen-to-cell picking for the whole-core AREA chart (ARC-WORLD-007, Project policy). A chart cell (x,y) is the
/// terrain diamond the AREA plan draws: its tile rectangle starts at <c>(40(y-x)+1, 20(y+x))</c>, so the diamond
/// centre is <c>(40(y-x)+40, 20(y+x)+20)</c>, the same point AREA scenery anchors use (CE <c>location_xy</c> plus
/// the object <c>+40,+20</c>). Since ARC-RENDER-011 the HumanFixed actor stands on the same centre
/// (<see cref="AreaGridSession.LocationOriginX"/>); picking follows the drawn ground, not the actor sprite.
/// </summary>
public static class AreaCellPicking
{
    /// <summary>Largest picked coordinate magnitude; anything farther is not a chart cell and is not picked.</summary>
    public const int Reach = 1 << 20;

    public static Vec2 Center(CellCoord cell) => new(40.0 * (cell.Y - (double)cell.X) + 40, 20.0 * (cell.Y + (double)cell.X) + 20);

    /// <summary>The cell whose diamond holds the chart-space point. Points on a shared diamond edge go to the larger
    /// map coordinate. Returns null for non-finite or out-of-reach points.</summary>
    public static CellCoord? Pick(Vec2 chart)
    {
        if (!double.IsFinite(chart.X) || !double.IsFinite(chart.Y)) return null;
        var u = (chart.X - 40) / 40; // y - x
        var v = (chart.Y - 20) / 20; // y + x
        var x = Math.Floor((v - u) / 2 + .5);
        var y = Math.Floor((u + v) / 2 + .5);
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > Reach || Math.Abs(y) > Reach) return null;
        return new((int)x, (int)y);
    }
}
