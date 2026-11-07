using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;
using Arcanum.World;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-178 (bug B19, ledger GA3): the area at a tile of the start map, which get area (84) stores for any object when the shown map's map-list
/// area is 0 and the shown map is the start map (the nearest area whose location lies in a sector of the same non-zero town map as the object's
/// own sector, 0x4CB630). The areas come from the world-map data (<see cref="AreaWorldMapData"/>); the town map of the object's sector from the
/// sectors the shown map loaded, and of an area's own location's sector from the world-map data.
/// </summary>
public static class AreaStartMapAreas
{
    /// <summary>The area at <paramref name="cell"/>, or null when a town map this caller does not hold is needed (an object whose sector is not among
    /// <paramref name="shownSectors"/>); the dispatch then ends as not modelled.</summary>
    public static int? AreaAt(AreaWorldMapData data, IReadOnlyList<AreaSectorRecord> shownSectors, CellCoord cell)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(shownSectors);
        // A sector the shown map loaded reads its metadata word (0 for one without its own file or without the section); another is the area table's.
        var shown = new Dictionary<TravelSector, int>();
        foreach (var sector in shownSectors) shown[new(sector.Coordinate.X, sector.Coordinate.Y)] = sector.Present ? sector.Townmap ?? 0 : 0;
        int? TownMap(TravelSector sector) =>
            shown.TryGetValue(sector, out var loaded) ? loaded : data.AreaTownMaps.TryGetValue(sector, out var known) ? known : null;
        return WorldTravelGeometry.AreaAt(data.TravelAreas, new(cell.X, cell.Y), TownMap);
    }
}
