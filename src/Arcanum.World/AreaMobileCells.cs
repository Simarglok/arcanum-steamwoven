using System.Collections.Concurrent;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-354: the stored cell of every mobile object of a map that opens only as a window (the main world map), by the viewer's <c>oid:</c>
/// identity: the location its map's mobile aggregate gives it, read without <c>map.patch</c>, which is the location a window plan keeps an aggregate
/// object by (<see cref="AreaMapPlans"/>: an object of the aggregate is in a window when its own serialized cell lies in one of the window's
/// sectors). The original holds every mobile object of the open map, whatever sector it stands in (ledger SS7, PS7), so an item taken in one window
/// keeps its record in every other; here a record is a row of the window around that cell, which this cell finds for an object nothing else
/// recorded a cell for (an item the hero carries, unlike an NPC's <c>npc.home</c>). Read on the calling thread once per process for a game folder and
/// map (the main map's aggregate, about 7,400 records), since the aggregate does not change; a read that failed is tried again at the next ask. A whole
/// map (it has no other window), a map the list does not name or one that does not read answers null, never an exception. Thread-safe.
/// </summary>
public static class AreaMobileCells
{
    private static readonly ConcurrentDictionary<(string Root, int Map), Lazy<IReadOnlyDictionary<string, CellCoord>?>> Held = new();

    /// <summary>The stored cell of <paramref name="entity"/> on window-only map <paramref name="runtimeMap"/> of <paramref name="gameRoot"/>, or null.</summary>
    public static CellCoord? Of(string? gameRoot, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (gameRoot is null || !entity.StartsWith("oid:", StringComparison.Ordinal)) return null;
        var key = (gameRoot, runtimeMap);
        var cells = Held.GetOrAdd(key, at => new(() => Read(at.Root, at.Map))).Value;
        // A read that failed is not kept: the next ask tries again (a whole map, which has no other window, is kept as an empty table).
        if (cells is null) { Held.TryRemove(key, out _); return null; }
        return cells.TryGetValue(entity, out var cell) ? cell : null;
    }

    private static IReadOnlyDictionary<string, CellCoord>? Read(string gameRoot, int runtimeMap)
    {
        try
        {
            var discovery = ContentMountPlan.Discover(gameRoot);
            if (discovery.Status != ContentMountStatus.Ready) return null;
            var opened = ContentStack.OpenShared(discovery.Plan!);
            if (opened.Status != ContentStackStatus.Ready) return null;
            using var stack = opened.Stack!;
            var list = MapList.Read(stack);
            if (list.Status != MapListStatus.Read || list.ByRuntime(runtimeMap) is not { } entry) return null;
            if (WorldCatalog.Open(stack) is not { } catalog || catalog.Find(entry.Name) is not { } map) return null;
            var cells = new Dictionary<string, CellCoord>(StringComparer.Ordinal);
            if (map.SectorFiles.Count <= AreaMapPlans.MaxSectorFiles) return cells;
            // The objects read before an aggregate issue keep their cells; a later one has none, and the resolver then says it found nothing.
            var mobiles = catalog.ReadMobiles(map);
            foreach (var item in mobiles.Objects)
                if (item.Fields.TryGetValue(1, out var place) && place is { Presence: WorldFieldPresence.Value, Number: { } packed }
                    && unchecked((int)packed) is var x and >= 0 && unchecked((int)(packed >> 32)) is var y and >= 0)
                    cells.TryAdd("oid:" + item.OwnIdSha256, new(x, y));
            Console.WriteLine($"ARC-DEV-354 mobile-cells map={runtimeMap} cells={cells.Count} issues={mobiles.Issues.Count}");
            return cells;
        }
        // Any failure of the read is a missing answer here, never an exception at the boundary (a Lazy would keep and rethrow it).
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Console.WriteLine($"ARC-DEV-354 mobile-cells map={runtimeMap} not read: {error.GetType().Name}");
            return null;
        }
    }

    /// <summary>Forgets what was read (tests).</summary>
    public static void Clear() => Held.Clear();
}
