using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-042: the tile under a point of the AREA viewer's world space, for the sound listener (the original puts it on the tile
/// at the centre of the view, docs/research/audio-evidence.md SN7). A chart cell (u, v) is drawn as the diamond centred on
/// <c>(40·(v − u) + 40, 20·(u + v) + 20)</c> (the actor's foot anchor, ARC-RENDER-011); the point's cell is the nearest centre
/// in cell coordinates.
/// </summary>
public static class AreaSoundListener
{
    public static CellCoord ChartCell(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var dx = x - AreaGridSession.LocationOriginX; var dy = y - 20;
        var u = Math.Round(dy / 40 - dx / 80, MidpointRounding.AwayFromZero);
        var v = Math.Round(dy / 40 + dx / 80, MidpointRounding.AwayFromZero);
        if (u is < int.MinValue or > int.MaxValue || v is < int.MinValue or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(x));
        return new((int)u, (int)v);
    }
}
