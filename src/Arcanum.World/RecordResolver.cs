using Arcanum.Data.Area;

namespace Arcanum.World;

/// <summary>ARC-DEV-072 (MIG1, TM3): the explicit bounds of a <see cref="RecordResolver"/>.</summary>
/// <param name="MaxMaps">Maps held at once: the maps the resolver was given plus those it read on demand. When the given maps
/// already fill it, nothing is read on demand; otherwise the least recently used on-demand map goes first.</param>
/// <param name="MaxRowsPerMap">Rows (items and holders) one map may have; a map with more is refused, never partly held.</param>
public sealed record RecordResolverLimits(int MaxMaps = 8, int MaxRowsPerMap = 100_000)
{
    public bool Valid => MaxMaps is >= 1 and <= 512 && MaxRowsPerMap is >= 1 and <= 1_000_000;
}

/// <summary>What <see cref="IMapRecordSource.Load"/> found: the map's records or why not (no exception for an ordinary failure).</summary>
public sealed record MapRecordsLoad(AreaInventoryMap? Map, string Detail);

/// <summary>
/// ARC-DEV-072 (MIG1, TM3): where a map's object rows come from when the resolver does not hold the map. The viewer reads them from
/// the player's game folder; a test gives synthetic maps. It reads rows only (no scene, no art) and answers a map it cannot read
/// with <see cref="MapRecordsLoad.Map"/> null and a reason.
/// </summary>
public interface IMapRecordSource
{
    MapRecordsLoad Load(int runtimeMap, CancellationToken token);

    /// <summary>
    /// ARC-DEV-337 (B52): the rows of a window of a map that opens only as a window (the main world map) around an NPC of it that stands elsewhere,
    /// asked when the window the resolver holds for that map number does not list the NPC (another window of the same map is shown, so the
    /// NPC's own is not held). Defaults to <see cref="Load"/>; a source that answers from windows the run left behind skips them here.
    /// </summary>
    MapRecordsLoad LoadAround(int runtimeMap, CancellationToken token) => Load(runtimeMap, token);
}

public enum RecordStatus
{
    /// <summary>The entity's record was found.</summary>
    Found,
    /// <summary>No held map has the entity and no home map was named (or the named one does not list it).</summary>
    UnknownEntity,
    /// <summary>The home map is not held and there is no source to read it from, or the source could not read it.</summary>
    HomeUnread,
    /// <summary>Reading the home map would pass <see cref="RecordResolverLimits"/>.</summary>
    LimitExceeded,
}

/// <summary>An entity's base record by identity: the map whose stored objects hold it (its home map, wherever it stands now), that
/// map's records and the entity's object row.</summary>
public sealed record ResolvedRecord(string Entity, int HomeMap, AreaInventoryMap Map, AreaObjectRecord Row);

public sealed record RecordLookup(RecordStatus Status, ResolvedRecord? Record, string Detail)
{
    public static RecordLookup Of(ResolvedRecord record) => new(RecordStatus.Found, record, string.Empty);
}

/// <summary>
/// ARC-DEV-072 (MIG1, TM3, FM3, FM4): the one query that answers "what is this object's base record", by identity alone. In the
/// original an object carries every field wherever it stands; here an NPC's record is its object row in its home map's stored
/// data, so the resolver finds the row in any map it holds and, when the entity is not among them, reads the home map the caller
/// names (the session's <see cref="EntityHomes"/>) from an <see cref="IMapRecordSource"/>. Nothing here is state: the held maps are
/// derived data, rebuilt freely, so a quick load or a fresh run loses nothing. The maps it was given are never dropped; maps read on
/// demand are bounded by <see cref="RecordResolverLimits"/> and leave least recently used first. A map the source could not read is
/// remembered as unread (the source is not asked again in this resolver's life). Caller-serialized; no Godot.
/// </summary>
public sealed class RecordResolver
{
    private readonly Dictionary<int, AreaInventoryMap> _given = [];
    private readonly Dictionary<int, (AreaInventoryMap Map, long Used)> _loaded = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (RecordStatus Status, string Detail)> _unread = [];
    // ARC-DEV-337 (B52): one more window per map number, read around an NPC the held window does not list (a companion of another window of the main
    // map, which the held window of that map number cannot answer). Derived rows, never given and never counted against the map limit.
    private readonly Dictionary<int, AreaInventoryMap> _windows = [];
    private readonly Dictionary<string, int> _windowIndex = new(StringComparer.Ordinal);
    private readonly HashSet<(int Map, string Entity)> _windowMissed = [];
    private AreaInventoryMap[]? _maps;
    private readonly IMapRecordSource? _source;
    private long _clock;

    /// <param name="given">The maps the caller already holds (the run's loaded maps), kept for the resolver's life. More maps than
    /// <see cref="RecordResolverLimits.MaxMaps"/> or a map with more rows than <see cref="RecordResolverLimits.MaxRowsPerMap"/> is an
    /// <see cref="ArgumentException"/> (a programming error of the caller, not a runtime boundary).</param>
    /// <param name="source">Where other maps' rows come from; null: only the given maps resolve.</param>
    public RecordResolver(IEnumerable<AreaInventoryMap> given, IMapRecordSource? source = null, RecordResolverLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(given);
        Limits = limits ?? new();
        if (!Limits.Valid) throw new ArgumentOutOfRangeException(nameof(limits));
        _source = source;
        foreach (var map in given)
        {
            if (map is null || _given.ContainsKey(map.RuntimeMap)) throw new ArgumentException("RECORD_RESOLVER_MAP_DUPLICATE", nameof(given));
            if (map.Seed.Holders.Count + map.Seed.Items.Count > Limits.MaxRowsPerMap) throw new ArgumentException("RECORD_RESOLVER_ROWS_LIMIT", nameof(given));
            _given.Add(map.RuntimeMap, map);
        }
        if (_given.Count > Limits.MaxMaps) throw new ArgumentException("RECORD_RESOLVER_MAPS_LIMIT", nameof(given));
        foreach (var map in _given.Values.OrderBy(map => map.RuntimeMap)) Index(map);
    }

    public RecordResolverLimits Limits { get; }

    /// <summary>Whether a source can read maps the resolver does not hold.</summary>
    public bool CanRead => _source is not null;

    /// <summary>Every map held now: the given ones and those read on demand, in map order.</summary>
    public IReadOnlyList<AreaInventoryMap> Maps =>
        _maps ??= _given.Values.Concat(_loaded.Values.Select(entry => entry.Map)).OrderBy(map => map.RuntimeMap).ToArray();

    /// <summary>The maps read on demand now, in map order (the given maps are the caller's own).</summary>
    public IReadOnlyList<AreaInventoryMap> ReadMaps => Maps.Where(map => _loaded.ContainsKey(map.RuntimeMap)).ToArray();

    /// <summary>Whether the resolver holds the map (it never reads one here).</summary>
    public bool Holds(int runtimeMap) => _given.ContainsKey(runtimeMap) || _loaded.ContainsKey(runtimeMap);

    /// <summary>Whether the map is one the resolver read on demand (not one it was given).</summary>
    public bool IsRead(int runtimeMap) => _loaded.ContainsKey(runtimeMap);

    /// <summary>The held map, or null; it does not read a map (a map read on demand counts as used).</summary>
    public AreaInventoryMap? Held(int runtimeMap)
    {
        if (_given.TryGetValue(runtimeMap, out var map)) return map;
        if (!_loaded.TryGetValue(runtimeMap, out var loaded)) return null;
        _loaded[runtimeMap] = (loaded.Map, ++_clock);
        return loaded.Map;
    }

    /// <summary>The map that holds the entity's row among the held maps, or null.</summary>
    public int? HeldHome(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _index.TryGetValue(entity, out var map) ? map : null;
    }

    /// <summary>
    /// The entity's base record. It is found among the held maps first; otherwise, with <paramref name="home"/> (the map the session
    /// names as the entity's home) the resolver reads that map from its source, holds it, and looks the entity up in it.
    /// </summary>
    public RecordLookup Resolve(string entity, int? home = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_index.TryGetValue(entity, out var held)) return Found(entity, Held(held)!);
        if (_windowIndex.TryGetValue(entity, out var windowMap)) return Found(entity, _windows[windowMap]);
        if (home is not { } homeMap) return new(RecordStatus.UnknownEntity, null, "no held map lists the entity and no home map is named");
        if (_loaded.ContainsKey(homeMap) || _given.ContainsKey(homeMap)) return ReadWindow(entity, homeMap, token);
        var read = Read(homeMap, token);
        if (read.Status != RecordStatus.Found) return read;
        return _index.TryGetValue(entity, out var now) && now == homeMap
            ? Found(entity, Held(homeMap)!) : new(RecordStatus.UnknownEntity, null, $"map {homeMap} does not list the entity");
    }

    /// <summary>
    /// ARC-DEV-337 (B52): the held map of <paramref name="homeMap"/> does not list the entity; when the source can read another window of that map
    /// (<see cref="IMapRecordSource.LoadAround"/>) and it lists the entity, that window is kept as the map's one extra window and answers the entity.
    /// A miss is remembered per map and entity, so the source is not asked again. NOT READY: companions of two other windows of one map (the extra
    /// window is replaced by the later read).
    /// </summary>
    private RecordLookup ReadWindow(string entity, int homeMap, CancellationToken token)
    {
        var miss = new RecordLookup(RecordStatus.UnknownEntity, null, $"map {homeMap} does not list the entity");
        if (_source is null || _windowMissed.Contains((homeMap, entity))) return miss;
        MapRecordsLoad load;
        try { load = _source.LoadAround(homeMap, token); }
        catch (Exception error) when (error is not OperationCanceledException) { _windowMissed.Add((homeMap, entity)); return miss; }
        if (load.Map is not { } map || map.RuntimeMap != homeMap || map.Seed.Holders.Count + map.Seed.Items.Count > Limits.MaxRowsPerMap || !map.TryGetRow(entity, out _))
        {
            _windowMissed.Add((homeMap, entity));
            return miss;
        }
        _windows[homeMap] = map;
        foreach (var stale in _windowIndex.Where(pair => pair.Value == homeMap).Select(pair => pair.Key).ToArray()) _windowIndex.Remove(stale);
        foreach (var listed in map.Seed.Holders.Select(holder => holder.Entity).Concat(map.Seed.Items.Select(item => item.Entity)))
            if (!_index.ContainsKey(listed)) _windowIndex[listed] = homeMap;
        return Found(entity, map);
    }

    /// <summary>Reads (when it is not held) and returns the map, or the reason it cannot.</summary>
    public (RecordStatus Status, AreaInventoryMap? Map, string Detail) Map(int runtimeMap, CancellationToken token = default)
    {
        if (Held(runtimeMap) is { } map) return (RecordStatus.Found, map, string.Empty);
        var read = Read(runtimeMap, token);
        return read.Status == RecordStatus.Found ? (RecordStatus.Found, Held(runtimeMap), string.Empty) : (read.Status, null, read.Detail);
    }

    private RecordLookup Read(int runtimeMap, CancellationToken token)
    {
        if (_source is null) return new(RecordStatus.HomeUnread, null, "no source reads maps");
        if (_unread.TryGetValue(runtimeMap, out var why)) return new(why.Status, null, why.Detail);
        if (_given.Count >= Limits.MaxMaps) return new(RecordStatus.LimitExceeded, null, $"the {Limits.MaxMaps} maps held are all given");
        MapRecordsLoad load;
        try { load = _source.Load(runtimeMap, token); }
        // A source reports an ordinary failure as a result; anything else it throws is the same failure here, never an exception at the
        // boundary. A cancelled read is not a failure of the map: it is asked again.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Unread(runtimeMap, RecordStatus.HomeUnread, $"the source threw {error.GetType().Name}: {error.Message}");
        }
        if (load.Map is not { } map) return Unread(runtimeMap, RecordStatus.HomeUnread, load.Detail);
        if (map.RuntimeMap != runtimeMap) return Unread(runtimeMap, RecordStatus.HomeUnread, $"the source returned map {map.RuntimeMap}");
        if (map.Seed.Holders.Count + map.Seed.Items.Count > Limits.MaxRowsPerMap)
            return Unread(runtimeMap, RecordStatus.LimitExceeded, $"{map.Seed.Holders.Count + map.Seed.Items.Count} rows pass the limit of {Limits.MaxRowsPerMap}");
        while (_given.Count + _loaded.Count >= Limits.MaxMaps) Evict();
        _loaded[runtimeMap] = (map, ++_clock);
        _maps = null;
        Index(map);
        return new(RecordStatus.Found, null, string.Empty);
    }

    private RecordLookup Unread(int runtimeMap, RecordStatus status, string detail)
    {
        _unread[runtimeMap] = (status, detail);
        return new(status, null, detail);
    }

    private void Evict()
    {
        var oldest = _loaded.MinBy(pair => pair.Value.Used);
        _loaded.Remove(oldest.Key);
        _maps = null;
        // Rebuilt from the maps still held, so an entity a remaining map also lists (a corrupt id on two maps) stays found.
        _index.Clear();
        foreach (var held in _given.Values.OrderBy(map => map.RuntimeMap).Concat(_loaded.Values.Select(entry => entry.Map).OrderBy(map => map.RuntimeMap))) Index(held);
    }

    private void Index(AreaInventoryMap map)
    {
        foreach (var entity in map.Seed.Holders.Select(holder => holder.Entity).Concat(map.Seed.Items.Select(item => item.Entity)))
            _index.TryAdd(entity, map.RuntimeMap);
    }

    private RecordLookup Found(string entity, AreaInventoryMap map) =>
        map.TryGetRow(entity, out var row) ? RecordLookup.Of(new(entity, map.RuntimeMap, map, row)) : new(RecordStatus.UnknownEntity, null, "the map has no row for the entity");
}
