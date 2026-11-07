using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;
using Arcanum.World;

namespace Arcanum.World;

/// <summary>What <see cref="AreaResidency.Open(GameSessionOwner, SessionMapId, MapResidentRowsBuild, SectorMapExtent, CellCoord?)"/> did: the
/// residency's open, the sector cache's (null when the residency did not open) and the arrival pre-cache's touch (null without a hero cell or a
/// cache).</summary>
public sealed record AreaResidencyOpen(ResidencyOpenResult Residency, SectorsOpenStatus? Sectors, SectorMapExtent Extent, SectorTouchResult? Arrival);

/// <summary>
/// ARC-DEV-104 (MIG9 S4, K1 §3.3, §3.4, RL11): the viewer's side of the open map's residency. When the AI host of a whole map is built and the
/// session has no sector cache for it, the viewer opens the residency from the map's rows (World's <see cref="MapResidentRows"/>), opens the sector
/// cache over the map's sector extent and touches the 3 × 3 sectors around the hero (the arrival pre-cache), which folds them and gives their NPCs
/// their first tick. Each frame it touches the hero's view square (<see cref="ViewCells"/>), the stand-in for the drawn view rectangle. A window of
/// the main map opens from its whole mobile read (ARC-DEV-118, <see cref="AreaWholeMobile"/>) since S5b.
/// </summary>
public static class AreaResidency
{
    /// <summary>Half the side of the hero's view square in cells: the original's 800 × 600 view of 80 × 40-pixel tiles spans about 15 tiles
    /// each way of its centre.</summary>
    public const int ViewHalf = 16;

    /// <summary>The sector extent of a whole map: one past its largest sector coordinate on each axis (1 × 1 for a map with no sector).</summary>
    public static SectorMapExtent Extent(AreaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Sectors.Count == 0 ? new(1, 1)
            : new(snapshot.Sectors.Max(s => s.Coordinate.X) + 1, snapshot.Sectors.Max(s => s.Coordinate.Y) + 1);
    }

    /// <summary>One cell of each of the 3 × 3 sectors around the hero's (the arrival pre-cache); sectors left of or above the map are left out.</summary>
    public static IReadOnlyList<CellCoord> ArrivalCells(CellCoord hero)
    {
        var centre = ResidentSector.Of(hero);
        var cells = new List<CellCoord>(9);
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (centre.X + dx >= 0 && centre.Y + dy >= 0) cells.Add(new((centre.X + dx) << ResidentSector.Shift, (centre.Y + dy) << ResidentSector.Shift));
        return cells;
    }

    /// <summary>The corners of the hero's view square (<see cref="ViewHalf"/> cells each way, clamped at 0): their sectors are every sector the
    /// square meets, since its side is shorter than a sector's.</summary>
    public static IReadOnlyList<CellCoord> ViewCells(CellCoord hero)
    {
        int left = Math.Max(0, hero.X - ViewHalf), top = Math.Max(0, hero.Y - ViewHalf), right = checked(hero.X + ViewHalf), bottom = checked(hero.Y + ViewHalf);
        return [new(left, top), new(right, top), new(left, bottom), new(right, bottom)];
    }

    /// <summary>Opens the residency and the sector cache of a loaded whole map and pre-caches around the hero; null for a window of the main map or
    /// when the session already holds the map's sector cache.</summary>
    public static AreaResidencyOpen? Open(GameSessionOwner session, AreaSnapshot snapshot, AreaInventoryMap inventory, CellCoord? hero)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(inventory);
        var map = new SessionMapId(inventory.RuntimeMap);
        if (snapshot.Window is not null || session.QuerySectors(map) is not null) return null;
        return Open(session, map, MapResidentRows.Build(snapshot, inventory), Extent(snapshot), hero);
    }

    /// <summary>
    /// ARC-DEV-118 (MIG9 S5b): <see cref="Open(GameSessionOwner, AreaSnapshot, AreaInventoryMap, CellCoord?)"/> for a loaded area of either kind: a whole map
    /// opens from its snapshot as before; a window of the main map opens from the map's whole mobile read (<see cref="AreaLoadResult.WholeMobile"/>, its 7,399
    /// rows) and its size in sectors, so the main map has a residency and a sector cache like any whole map. Null when the session already holds the map's
    /// sector cache (a window move keeps it), or for an area with neither a whole map's snapshot nor a whole mobile read.
    /// </summary>
    public static AreaResidencyOpen? Open(GameSessionOwner session, AreaLoadResult area, AreaInventoryMap inventory, CellCoord? hero)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(inventory);
        if (area.Snapshot.Window is null) return Open(session, area.Snapshot, inventory, hero);
        return area.WholeMobile is { } whole ? OpenWhole(session, new SessionMapId(inventory.RuntimeMap), whole, hero) : null;
    }

    /// <summary>ARC-DEV-118: opens the residency and sector cache of <paramref name="map"/> from its whole mobile read and pre-caches around the hero; null when the
    /// session already holds the map's sector cache.</summary>
    public static AreaResidencyOpen? OpenWhole(GameSessionOwner session, SessionMapId map, AreaWholeMobile whole, CellCoord? hero)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(whole);
        return session.QuerySectors(map) is not null ? null : Open(session, map, whole.Rows, whole.Extent, hero);
    }

    /// <summary>Opens the residency of <paramref name="map"/> from <paramref name="build"/>, its sector cache over <paramref name="extent"/>, and
    /// touches the arrival pre-cache around <paramref name="hero"/>.</summary>
    public static AreaResidencyOpen Open(GameSessionOwner session, SessionMapId map, MapResidentRowsBuild build, SectorMapExtent extent, CellCoord? hero)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(build);
        var residency = MapResidentRows.Open(session, map, build);
        if (residency.Status != ResidencyStatus.Open) return new(residency, null, extent, null);
        var sectors = session.OpenSectors(map, extent);
        if (sectors != SectorsOpenStatus.Open || hero is not { } cell) return new(residency, sectors, extent, null);
        return new(residency, sectors, extent, session.TouchSectors(session.Version, map, ArrivalCells(cell)));
    }
}
