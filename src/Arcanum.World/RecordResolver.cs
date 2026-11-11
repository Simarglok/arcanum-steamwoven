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
    /// ARC-DEV-337 (B52): the rows of the window of a map that opens only as a window (the main world map) that lists <paramref name="entity"/>, read around
    /// the NPC's own stored cell; asked when the map the resolver holds does not list the NPC (another window of the same map is shown). A source that
    /// reads whole maps only answers null (the default): the map it gave is the whole map, so a window of it would list nothing more.
    /// </summary>
    MapRecordsLoad LoadAround(int runtimeMap, string entity, CancellationToken token) => new(null, "the source reads no other window");
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
    // ARC-DEV-337 (B52): more windows of a map number, each read around an NPC the held map does not list (a companion of another window of the main
    // map, which the held window of that map number cannot answer): at most MaxWindows, the least recently read leaves first. Derived rows, never
    // given and never counted against the map limit. A read that found nothing is asked again after MissRetry further asks of the same NPC.
    private const int MaxWindows = 4;
    private const int MissRetry = 32;
    private readonly List<AreaInventoryMap> _windows = [];
    private readonly Dictionary<string, AreaInventoryMap> _windowIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Map, string Entity), int> _windowMissed = [];
    // ARC-DEV-359 (E1-c): the one-row maps of the critters the session created (a created object has no map file: its row is built from its prototype), derived data the host sets
    // again whenever the session's created critters change. Never counted against the map limit, never evicted, bounded by the session's created-object bound.
    private readonly Dictionary<string, AreaInventoryMap> _created = new(StringComparer.Ordinal);
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

    /// <summary>
    /// ARC-DEV-359 (E1-c): sets the one-row maps of the critters the session created (each lists one NPC holder and its row, on the runtime map the critter stands on); a critter's row resolves by
    /// its entity wherever the home map is, as a map NPC's does. Replaces the earlier set; the maps are not <see cref="Maps"/> (they hold no items and no seed of their own to attach).
    /// </summary>
    public void SetCreated(IEnumerable<AreaInventoryMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _created.Clear();
        foreach (var map in maps)
            foreach (var holder in map.Seed.Holders) _created[holder.Entity] = map;
        CreatedVersion++;
    }

    /// <summary>Counts the sets of <see cref="SetCreated"/>: a reader that caches what it derived from the created critters asks again when it moves.</summary>
    public int CreatedVersion { get; private set; }

    /// <summary>The NPC holders of the created critters standing on <paramref name="runtimeMap"/> (each with an empty T0 list: what the session added to it is the session's, ARC-DEV-386).</summary>
    public IEnumerable<string> CreatedOn(int runtimeMap) => _created.Where(pair => pair.Value.RuntimeMap == runtimeMap).Select(pair => pair.Key);

    /// <summary>The map that lists the created critter <paramref name="entity"/>, or null.</summary>
    public AreaInventoryMap? CreatedMap(string entity) => _created.GetValueOrDefault(entity);

    /// <summary>ARC-DEV-386: the one-row maps of the created critters standing on <paramref name="runtimeMap"/>, in no set order.</summary>
    public IEnumerable<AreaInventoryMap> CreatedMapsOn(int runtimeMap) => _created.Values.Where(map => map.RuntimeMap == runtimeMap);

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
        if (_created.TryGetValue(entity, out var created)) return Found(entity, created);
        if (_windowIndex.TryGetValue(entity, out var window)) return Found(entity, window);
        if (home is not { } homeMap) return new(RecordStatus.UnknownEntity, null, "no held map lists the entity and no home map is named");
        if (_loaded.ContainsKey(homeMap) || _given.ContainsKey(homeMap)) return ReadWindow(entity, homeMap, token);
        var read = Read(homeMap, token);
        if (read.Status != RecordStatus.Found) return read;
        // ARC-DEV-337: the map just read is a window that does not list him (the run left another window of it behind): the one around his cell may.
        return _index.TryGetValue(entity, out var now) && now == homeMap ? Found(entity, Held(homeMap)!) : ReadWindow(entity, homeMap, token);
    }

    /// <summary>
    /// ARC-DEV-337 (B52): the held map of <paramref name="homeMap"/> does not list the entity; when the source can read the window around the entity's own
    /// stored cell (<see cref="IMapRecordSource.LoadAround"/>) and it lists the entity, that window is kept beside the held maps (at most
    /// <see cref="MaxWindows"/>, the least recently read leaves first) and answers the entity and the NPCs it lists. A read that found nothing is asked again
    /// only after <see cref="MissRetry"/> more asks of the same entity: the stored cell it needs may be written meanwhile.
    /// </summary>
    private RecordLookup ReadWindow(string entity, int homeMap, CancellationToken token)
    {
        RecordLookup Miss() => new(RecordStatus.UnknownEntity, null, $"map {homeMap} does not list the entity");
        if (_source is null) return Miss();
        if (_windowMissed.TryGetValue((homeMap, entity), out var asks))
        {
            _windowMissed[(homeMap, entity)] = asks + 1;
            if ((asks + 1) % MissRetry != 0) return Miss();
        }
        MapRecordsLoad load;
        try { load = _source.LoadAround(homeMap, entity, token); }
        catch (Exception error) when (error is not OperationCanceledException) { _windowMissed.TryAdd((homeMap, entity), 0); return Miss(); }
        if (load.Map is not { } map || map.RuntimeMap != homeMap || map.Seed.Holders.Count + map.Seed.Items.Count > Limits.MaxRowsPerMap || !map.TryGetRow(entity, out _))
        {
            _windowMissed.TryAdd((homeMap, entity), 0);
            return Miss();
        }
        _windowMissed.Remove((homeMap, entity));
        var same = _windows.FindIndex(held => ReferenceEquals(held, map));
        if (same >= 0) _windows.RemoveAt(same);
        _windows.Add(map);
        while (_windows.Count > MaxWindows) _windows.RemoveAt(0);
        _windowIndex.Clear();
        foreach (var kept in _windows)
            foreach (var listed in kept.Seed.Holders.Select(holder => holder.Entity).Concat(kept.Seed.Items.Select(item => item.Entity)))
                if (!_index.ContainsKey(listed)) _windowIndex[listed] = kept;
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
