using System.Globalization;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One map of the module (ARC-RENDER-3107): its folder name, its sector files, the terrain size in sectors (0 when it has no grid) and the
/// map's own start tile.</summary>
public sealed record AreaTownMapMapRow(string Map, int SectorFiles, long TerrainWidth, long TerrainHeight, long? StartX, long? StartY);

/// <summary>The sectors of one map whose metadata word names one town map: the count and the sector bounds.</summary>
public sealed record AreaTownMapSectorRow(string Map, int TownMap, int Sectors, int MinX, int MinY, int MaxX, int MaxY);

/// <summary>
/// ARC-RENDER-3107: which game map each town map is drawn from. The original shows the town map whose number is the first metadata word of the sector
/// the party stands in (GA3, <c>AreaWorldMap.ReadAreaTownMaps</c>), so a scan of every sector file of every map of a module gives, per town map number, the
/// map folder and the sectors that carry it. Godot-free; reads sector files only (a few seconds for the main map).
/// </summary>
public static class AreaTownMapScan
{
    /// <summary>The maps and the town-map sectors of <paramref name="modulePath"/>; null (with the reason in <paramref name="detail"/>) when the content stack does not open.</summary>
    public static (IReadOnlyList<AreaTownMapMapRow> Maps, IReadOnlyList<AreaTownMapSectorRow> Sectors)? Scan(string modulePath, out string detail)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) { detail = $"not read ({found.Status})"; return null; }
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) { detail = $"not read (content stack {opened.Status})"; return null; }
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        var maps = new List<AreaTownMapMapRow>();
        var sectors = new List<AreaTownMapSectorRow>();
        foreach (var map in catalog.Maps.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            maps.Add(new(map.Name, map.SectorFiles.Count, map.Terrain?.WidthSectors ?? 0, map.Terrain?.HeightSectors ?? 0, map.StartLocation?.X, map.StartLocation?.Y));
            var byTown = new Dictionary<int, List<WorldSectorCoordinate>>();
            foreach (var coordinate in map.SectorFiles)
                if (catalog.ReadSector(map, coordinate).Sector?.MetadataAndSound is { Count: > 0 } metadata && metadata[0] != 0)
                {
                    if (!byTown.TryGetValue(metadata[0], out var list)) byTown[metadata[0]] = list = [];
                    list.Add(coordinate);
                }
            foreach (var (town, list) in byTown.OrderBy(p => p.Key))
                sectors.Add(new(map.Name, town, list.Count, list.Min(c => c.X), list.Min(c => c.Y), list.Max(c => c.X), list.Max(c => c.Y)));
        }
        detail = $"{maps.Count} maps, {sectors.Count} map/town pairs";
        return (maps, sectors);
    }

    /// <summary>The two tables as TSV text (maps, then town-map sectors), the form the render tool reads.</summary>
    public static (string Maps, string Sectors) ToTsv(IReadOnlyList<AreaTownMapMapRow> maps, IReadOnlyList<AreaTownMapSectorRow> sectors)
    {
        var m = new StringBuilder("map\tsector_files\tterrain_width\tterrain_height\tstart_x\tstart_y\n");
        foreach (var row in maps)
            m.Append(CultureInfo.InvariantCulture, $"{row.Map}\t{row.SectorFiles}\t{row.TerrainWidth}\t{row.TerrainHeight}\t{row.StartX}\t{row.StartY}\n");
        var s = new StringBuilder("map\ttownmap\tsectors\tmin_x\tmin_y\tmax_x\tmax_y\n");
        foreach (var row in sectors)
            s.Append(CultureInfo.InvariantCulture, $"{row.Map}\t{row.TownMap}\t{row.Sectors}\t{row.MinX}\t{row.MinY}\t{row.MaxX}\t{row.MaxY}\n");
        return (m.ToString(), s.ToString());
    }
}
