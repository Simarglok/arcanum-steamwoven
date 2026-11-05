using System.Collections.Concurrent;
using Arcanum.Data.Area;
using Arcanum.Data.Fixes;
using Arcanum.Data.Maps;
using Arcanum.Simulation.Residency;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-118 (MIG9 S5b): a map's whole mobile read for its residency: the session rows, and the map's size in sectors for its sector cache.</summary>
public sealed record AreaWholeMobile(string Map, MapResidentRowsBuild Rows, SectorMapExtent Extent);

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §6 risk 1): reads the whole mobile aggregate of a map the viewer loads as a window (the main world map), once per process
/// and data rules, since every window move loads again and the aggregate does not change: <see cref="WholeMapMobileRows"/> over the map's aggregate
/// with the map patch applied as the original applies it, and the extent of the map's sectors (its <c>map.prp</c> size in tiles over 64). Thread-safe.
/// </summary>
public static class AreaWholeMobileCache
{
    private static readonly ConcurrentDictionary<(string Map, bool ZeroClears), Lazy<AreaWholeMobile>> Held = new();

    /// <summary>The map's whole mobile read for the data rules <paramref name="data"/> (the map patch's script-zero switch is the only rule that differs).</summary>
    public static AreaWholeMobile Get(Arcanum.Data.Content.ContentStack stack, WorldCatalog catalog, WorldMap map, DataFixProfile? data)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(map);
        var zero = (data ?? DataFixProfile.Original).Applies("MP7");
        return Held.GetOrAdd((map.Name, zero), _ => new(() => Read(stack, catalog, map, data))).Value;
    }

    private static AreaWholeMobile Read(Arcanum.Data.Content.ContentStack stack, WorldCatalog catalog, WorldMap map, DataFixProfile? data)
    {
        var rows = WholeMapMobileRows.Rows(catalog, map, AreaMapPlans.ReadMapPatch(stack, data));
        return new(map.Name, rows, new(map.Properties.WidthTiles / 64, map.Properties.HeightTiles / 64));
    }

    /// <summary>Forgets what was read (tests).</summary>
    public static void Clear() => Held.Clear();
}
