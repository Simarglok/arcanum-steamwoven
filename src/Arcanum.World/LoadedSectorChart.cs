using System.Collections.Immutable;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Residency;

namespace Arcanum.World;

/// <summary>What one <see cref="LoadedSectorChart.Sync"/> did.</summary>
public sealed record LoadedSectorChartSync(IReadOnlyList<ResidentSector> Read, IReadOnlyList<ResidentSector> Dropped, IReadOnlyList<(ResidentSector Sector, string Detail)> Failed)
{
    /// <summary>Whether the chart's evidence changed (a sector was read or dropped).</summary>
    public bool Changed => Read.Count > 0 || Dropped.Count > 0;
}

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §3.5, RL11): the host's chart over the loaded sectors. It holds the evidence (<see cref="SectorTerrain"/>) of exactly the
/// sectors the session's sector cache has loaded, read through the <see cref="ISectorTerrain"/> port, and gives it as one
/// <see cref="AreaStepSnapshot"/> that admits their union (<see cref="Steps"/>): a cell of a sector the cache has not loaded is outside the admitted
/// area, as a cell outside the map. <see cref="Sync"/> follows the cache: a sector that left it is dropped, a sector that entered it is read (a failed read
/// is remembered until the sector leaves the cache, so a source that cannot be read is not asked again every frame). The chart is derived data, built
/// and dropped with the cache; it keeps no state of the session and is never saved. <b>Threads:</b> one thread syncs at a time (a read may take seconds
/// and may run on a worker); <see cref="Steps"/>, <see cref="Sectors"/> and <see cref="Terrain"/> read the last published state from any thread, and
/// each <see cref="Sync"/> publishes its result as one replacement, so a reader never sees a half-synced chart.
/// </summary>
public sealed class LoadedSectorChart(ISectorTerrain terrain)
{
    private readonly ISectorTerrain _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
    private readonly Dictionary<long, string> _failed = [];
    private Published _published = new(ImmutableSortedDictionary<long, SectorTerrain>.Empty, null);

    private sealed record Published(ImmutableSortedDictionary<long, SectorTerrain> Held, AreaStepSnapshot? Steps);

    /// <summary>The evidence of every sector the chart holds as one snapshot, or null while it holds none. Replaced (never mutated) by a
    /// <see cref="Sync"/> that changed the set.</summary>
    public AreaStepSnapshot? Steps => Volatile.Read(ref _published).Steps;

    /// <summary>The sectors the chart holds evidence of, ascending sector id.</summary>
    public IReadOnlyList<ResidentSector> Sectors => Volatile.Read(ref _published).Held.Values.Select(s => s.Sector).ToArray();

    /// <summary>The terrain rows of a held sector, or null.</summary>
    public SectorTerrain? Terrain(ResidentSector sector) => sector.X >= 0 && sector.Y >= 0
        && Volatile.Read(ref _published).Held.TryGetValue(SectorCacheRules.SectorId(sector.X, sector.Y), out var held) ? held : null;

    /// <summary>Makes the chart hold exactly the sectors <paramref name="loaded"/> lists (the cache's slots), reading the new ones through the port
    /// in ascending id. A default array (no cache) empties the chart.</summary>
    public LoadedSectorChartSync Sync(ImmutableArray<SectorCacheSlot> loaded)
    {
        var wanted = loaded.IsDefault ? [] : loaded.Select(slot => slot.Sector).ToHashSet();
        var held = _published.Held;
        var dropped = new List<ResidentSector>();
        foreach (var id in held.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            dropped.Add(held[id].Sector);
            held = held.Remove(id);
        }
        foreach (var id in _failed.Keys.Where(id => !wanted.Contains(id)).ToArray()) _failed.Remove(id);
        var read = new List<ResidentSector>();
        var failed = new List<(ResidentSector, string)>();
        foreach (var id in wanted.Order())
        {
            if (held.ContainsKey(id) || _failed.ContainsKey(id)) continue;
            var sector = new ResidentSector((int)(id & (SectorMapExtent.MaximumPerAxis - 1)), (int)(id >> 26));
            var result = _terrain.Read(sector);
            if (result is { Status: SectorTerrainStatus.Ready, Terrain: { } ready }) { held = held.Add(id, ready); read.Add(sector); }
            else { _failed.Add(id, $"{result.Status} {result.Detail}"); failed.Add((sector, $"{result.Status} {result.Detail}")); }
        }
        if (read.Count > 0 || dropped.Count > 0)
            Volatile.Write(ref _published, new(held, held.IsEmpty ? null : AreaStepSnapshot.Union(held.Values.Select(s => s.Steps).ToArray())));
        return new(read, dropped, failed);
    }
}
