using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One sector of the overview: its coordinate, its base terrain type, the colour drawn and its diamond in the plan's
/// chart space (top, right, bottom, left).</summary>
public sealed record AreaOverviewSector(AreaSectorCoordinate Sector, int Type, TerrainColour Colour, IReadOnlyList<(double X, double Y)> Corners);

/// <summary>
/// ARC-RENDER-034: the terrain overview of a map loaded as a window (the main world map) beyond the window's drawn core: every
/// sector within <see cref="Radius"/> of the window's centre that the window does not draw is one flat diamond in the colour of
/// its base terrain type (the type's editor colour in <c>terrain.mes</c>, <see cref="TerrainEditorColours"/>; a type without one
/// takes <see cref="FallbackColour"/>), from the map's terrain grid. No object, critter or tile is drawn there: only the window
/// holds loaded content. The overview widens the camera's bounds (<see cref="Bounds"/>) so the zoom floor of ARC-RENDER-032
/// relaxes to the viewer's ordinary 1/8. A Steamwoven viewer feature: the original has no zoom, so no parity applies.
/// </summary>
public sealed class AreaTerrainOverview
{
    /// <summary>Sectors drawn on each side of the window's centre: at the ordinary 1/8 a 2560 × 1440 view (20,480 × 11,520 px)
    /// around a cell of the core stays inside the diamond of 13 × 13 sectors (one sector is 5,120 × 2,560 px).</summary>
    public const int Radius = 6;
    /// <summary>The 13 × 13 square of sectors around the centre (an overview holds this less the core: 160 for a core of 3 × 3).</summary>
    public const int MaxSectors = (2 * Radius + 1) * (2 * Radius + 1);
    private const double SectorHalfWidth = 2560, SectorHalfHeight = 1280;
    public static readonly TerrainColour FallbackColour = new(96, 96, 96);
    private const int SectorTiles = 64;

    private AreaTerrainOverview(IReadOnlyList<AreaOverviewSector> sectors, TerrainRect bounds, int missingColours, int coreRadius)
    { Sectors = sectors; Bounds = bounds; MissingColours = missingColours; CoreRadius = coreRadius; }

    /// <summary>The window's drawn core radius the overview surrounds.</summary>
    public int CoreRadius { get; }

    /// <summary>
    /// The smallest zoom at which a view of <paramref name="viewportWidth"/> × <paramref name="viewportHeight"/> centred anywhere
    /// in the core stays inside the diamond of the core and the overview (half-extents (2R+1)·2,560 × (2R+1)·1,280): a power of two,
    /// at least the ordinary 1/8 (2560 × 1440 reaches 1/8; 3840 × 2160 stops at 1/4). Near the map's edge, where the overview is
    /// cut, a corner of the view can still fall outside it.
    /// </summary>
    public double Floor(double viewportWidth, double viewportHeight)
    {
        if (!(viewportWidth > 0) || !(viewportHeight > 0) || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight))
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        double a = (2 * Radius + 1) * SectorHalfWidth, b = (2 * Radius + 1) * SectorHalfHeight;
        // The view's centre in the core diamond: the farthest it reaches is from a core corner.
        var core = (2 * CoreRadius + 1) * SectorHalfWidth / a;
        var exact = (viewportWidth / (2 * a) + viewportHeight / (2 * b)) / (1 - core);
        return Math.Clamp(Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(exact, .125)))), .125, 8);
    }

    public IReadOnlyList<AreaOverviewSector> Sectors { get; }
    /// <summary>The window's drawn bounds grown to hold every overview diamond.</summary>
    public TerrainRect Bounds { get; }
    /// <summary>Overview sectors drawn in <see cref="FallbackColour"/> (their type has no editor colour).</summary>
    public int MissingColours { get; }

    /// <summary>The overview around <paramref name="centre"/>; <paramref name="coreRadius"/> is the window's drawn core radius
    /// (1: 3 × 3 sectors), <paramref name="windowBounds"/> its drawn bounds in the chart space of <paramref name="chart"/>.
    /// Sectors outside the grid are not drawn.</summary>
    public static AreaTerrainOverview Create(WorldTerrainGrid grid, IReadOnlyList<TerrainColour?> colours, AreaSectorCoordinate centre, int coreRadius,
        AreaChart chart, TerrainRect windowBounds)
    {
        ArgumentNullException.ThrowIfNull(grid); ArgumentNullException.ThrowIfNull(colours); ArgumentNullException.ThrowIfNull(chart);
        if (coreRadius is < 0 or >= Radius) throw new ArgumentOutOfRangeException(nameof(coreRadius));
        var sectors = new List<AreaOverviewSector>(MaxSectors);
        long left = windowBounds.X, top = windowBounds.Y, right = (long)windowBounds.X + windowBounds.Width, bottom = (long)windowBounds.Y + windowBounds.Height;
        var missing = 0;
        for (var dy = -Radius; dy <= Radius; dy++)
            for (var dx = -Radius; dx <= Radius; dx++)
            {
                if (Math.Abs(dx) <= coreRadius && Math.Abs(dy) <= coreRadius) continue;
                long sx = centre.X + dx, sy = centre.Y + dy;
                if (sx < 0 || sy < 0 || sx >= grid.Header.WidthSectors || sy >= grid.Header.HeightSectors) continue;
                var word = new TerrainWord(grid[(int)sx, (int)sy]);
                if (word.Value == TerrainWord.OutsideGrid) continue;
                TerrainColour colour;
                if (word.Base < colours.Count && colours[word.Base] is { } known) colour = known;
                else { colour = FallbackColour; missing++; }
                var corners = Diamond(chart, sx, sy);
                sectors.Add(new(new(sx, sy), word.Base, colour, corners));
                foreach (var (x, y) in corners)
                {
                    left = Math.Min(left, (long)Math.Floor(x)); right = Math.Max(right, (long)Math.Ceiling(x));
                    top = Math.Min(top, (long)Math.Floor(y)); bottom = Math.Max(bottom, (long)Math.Ceiling(y));
                }
            }
        var bounds = new TerrainRect(checked((int)left), checked((int)top), checked((int)(right - left)), checked((int)(bottom - top)));
        return new(sectors.AsReadOnly(), bounds, missing, coreRadius);
    }

    /// <summary>A sector's diamond in the plan's chart space: the outer corners of its corner tiles, a tile at chart cell (x,y)
    /// having its top at (40 + 40(y−x), 20(y+x)) and being 80 × 40 (ARC-RENDER-011).</summary>
    public static IReadOnlyList<(double X, double Y)> Diamond(AreaChart chart, long sectorX, long sectorY)
    {
        ArgumentNullException.ThrowIfNull(chart);
        long x0 = sectorX * SectorTiles, y0 = sectorY * SectorTiles, x1 = x0 + SectorTiles - 1, y1 = y0 + SectorTiles - 1;
        (double X, double Y) Top(long x, long y)
        {
            var c = chart.ToChart(new AreaStepCell(x, y));
            return (40.0 + 40.0 * (c.Y - c.X), 20.0 * (c.Y + c.X));
        }
        var (tx, ty) = Top(x0, y0); var (rx, ry) = Top(x0, y1); var (bx, by) = Top(x1, y1); var (lx, ly) = Top(x1, y0);
        return [(tx, ty), (rx + 40, ry + 20), (bx, by + 40), (lx - 40, ly + 20)];
    }
}
