using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-079 (MIG4, TM3, V4): what one read of a home map (whole, or a window of it) gives: the records the resolver needs
/// (<see cref="Rows"/>), the bodies of the NPCs that can follow (<see cref="Templates"/>: speaker row, draw commands and visuals) and the
/// frames those bodies draw. All of it is derived from the map's stored data; nothing here is state.
/// </summary>
/// <param name="Map">The runtime map number.</param>
/// <param name="Rows">The map's (or window's) inventory seed and object rows.</param>
/// <param name="Templates">The bodies of the speakers with follow facts that the read listed.</param>
/// <param name="Frames">The frames the templates' commands draw.</param>
/// <param name="CoreSectors">The sectors (world cell &gt;&gt; 6) a window holds as core; null for a whole map.</param>
public sealed record AreaHomeRead(int Map, AreaInventoryMap Rows, IReadOnlyList<AreaCompanionTemplate> Templates,
    IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames, IReadOnlySet<(long X, long Y)>? CoreSectors)
{
    /// <summary>Whether the read holds the cell: a whole map holds every cell, a window the cells of its core sectors.</summary>
    public bool Covers(CellCoord cell) => CoreSectors is null || CoreSectors.Contains((cell.X >> 6, cell.Y >> 6));

    /// <summary>The body of <paramref name="entity"/> in this read, or null.</summary>
    public AreaCompanionTemplate? Template(string entity) => Templates.FirstOrDefault(t => string.Equals(t.Entity, entity, StringComparison.Ordinal));

    /// <summary>The frames of <paramref name="template"/>'s draw commands, as far as the read kept them.</summary>
    public IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> FramesOf(AreaCompanionTemplate template)
    {
        var own = new Dictionary<AreaFrameKey, DecodedAreaFrame>();
        foreach (var key in template.Commands.Select(c => c.Frame).Distinct())
            if (Frames.TryGetValue(key, out var frame)) own[key] = frame;
        return own;
    }

    /// <summary>The read of a map the viewer has just loaded (its own scene and rows): the bodies of its speakers with their frames from
    /// the shown frame repository.</summary>
    public static AreaHomeRead Of(AreaInventoryMap rows, IReadOnlyList<AreaCompanionTemplate> templates, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> shown,
        AreaSnapshot snapshot)
    {
        var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame>();
        foreach (var key in templates.SelectMany(t => t.Commands).Select(c => c.Frame).Distinct())
            if (shown.TryGetValue(key, out var frame)) frames[key] = frame;
        return new(snapshot.RuntimeMap, rows, templates, frames, snapshot.Window is null ? null : CoreOf(snapshot));
    }

    internal static IReadOnlySet<(long X, long Y)> CoreOf(AreaSnapshot snapshot) =>
        snapshot.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).Select(s => (s.Coordinate.X, s.Coordinate.Y)).ToHashSet();
}

/// <summary>What <see cref="IAreaHomeSource.Read"/> found: the read or why not (no exception for an ordinary failure).</summary>
public sealed record AreaHomeLoad(AreaHomeRead? Read, string Detail);

/// <summary>
/// ARC-DEV-079 (MIG4): where a home map's data comes from when the viewer does not hold it. The viewer reads it from the player's game
/// folder (<see cref="AreaMapHomes"/>); a test gives synthetic reads.
/// </summary>
public interface IAreaHomeSource
{
    /// <summary>Reads map <paramref name="runtimeMap"/>: the whole map, or when it opens only as a window the window around
    /// <paramref name="near"/> (a world cell the map's data lists an object on). A failure is a result, not an exception.</summary>
    AreaHomeLoad Read(int runtimeMap, CellCoord? near, CancellationToken token);
}

/// <summary>
/// ARC-DEV-079 (MIG4, TM3): the viewer's derived copies of home maps, so a companion's body and base record come from its home map's
/// data wherever it stands, on any map and after a quick load or a fresh run (FM3, FM14), instead of a run-local cache of bodies the run
/// happened to see. Reads are asked of an <see cref="IAreaHomeSource"/> once per map (a window once per window), held bounded by
/// <see cref="MaxReads"/> (least recently used first) and a failed read is remembered for the directory's life. A read the viewer made
/// itself at a map change is <see cref="Remember"/>ed, so a companion carried from a map the viewer just left needs no read. Caller-serialized.
/// </summary>
public sealed class AreaHomeDirectory(IAreaHomeSource? source, int maxReads = AreaHomeDirectory.DefaultMaxReads)
{
    public const int DefaultMaxReads = 8;

    private readonly List<(AreaHomeRead Read, long Used)> _reads = [];
    private readonly Dictionary<(int Map, long Sector), string> _failed = [];
    private long _clock;

    public int MaxReads { get; } = maxReads is >= 1 and <= 64 ? maxReads : throw new ArgumentOutOfRangeException(nameof(maxReads));

    /// <summary>Whether a source can read maps the directory does not hold.</summary>
    public bool CanRead => source is not null;

    /// <summary>The reads held now, most recently used last.</summary>
    public IReadOnlyList<AreaHomeRead> Reads => _reads.OrderBy(entry => entry.Used).Select(entry => entry.Read).ToArray();

    /// <summary>Keeps a read the viewer made itself (the map it has just loaded), replacing a held read of the same map and window.</summary>
    public void Remember(AreaHomeRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _reads.RemoveAll(entry => entry.Read.Map == read.Map && SameWindow(entry.Read, read));
        _reads.Add((read, ++_clock));
        while (_reads.Count > MaxReads) _reads.Remove(_reads.MinBy(entry => entry.Used));
    }

    /// <summary>The held read of <paramref name="map"/> that holds <paramref name="near"/> (any held read of the map when null), or null;
    /// it reads nothing.</summary>
    public AreaHomeRead? Held(int map, CellCoord? near = null)
    {
        for (var i = 0; i < _reads.Count; i++)
        {
            var (read, _) = _reads[i];
            if (read.Map != map || (near is { } cell && !read.Covers(cell))) continue;
            _reads[i] = (read, ++_clock);
            return read;
        }
        return null;
    }

    /// <summary>
    /// The home map's data: a held read of the map (holding <paramref name="near"/> when one is given), else one read from the source
    /// around <paramref name="near"/>. A map that opens only as a window and has no <paramref name="near"/> is a typed failure (nothing is
    /// guessed).
    /// </summary>
    public AreaHomeLoad Home(int map, CellCoord? near = null, CancellationToken token = default)
    {
        if (Held(map, near) is { } held) return new(held, string.Empty);
        if (source is null) return new(null, "no source reads maps");
        var key = (map, near is { } cell ? Sector(cell) : -1L);
        if (_failed.TryGetValue(key, out var failure)) return new(null, failure);
        AreaHomeLoad load;
        try { load = source.Read(map, near, token); }
        // An ordinary failure is a result; anything else the source throws is the same failure, except a cancelled read, which is asked again.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Failed(key, $"the source threw {error.GetType().Name}: {error.Message}");
        }
        if (load.Read is not { } read) return Failed(key, load.Detail);
        if (read.Map != map) return Failed(key, $"the source returned map {read.Map}");
        if (near is { } wanted && !read.Covers(wanted)) return Failed(key, $"the read of map {map} does not hold the cell {wanted.X},{wanted.Y}");
        Remember(read);
        return new(read, string.Empty);
    }

    /// <summary>The body of <paramref name="entity"/> from its home map's data, with the frames it draws, or why there is none.</summary>
    public (AreaCompanionTemplate? Template, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames, string Detail) Body(string entity, int home,
        CellCoord? near, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // A read already held that lists the entity answers without the cell (a whole map, or the window the viewer shows).
        for (var i = 0; i < _reads.Count; i++)
        {
            var (read, _) = _reads[i];
            if (read.Map != home || read.Template(entity) is not { } template) continue;
            _reads[i] = (read, ++_clock);
            return (template, read.FramesOf(template), string.Empty);
        }
        var load = Home(home, near, token);
        if (load.Read is not { } found) return (null, new Dictionary<AreaFrameKey, DecodedAreaFrame>(), load.Detail);
        return found.Template(entity) is { } body ? (body, found.FramesOf(body), string.Empty)
            : (null, new Dictionary<AreaFrameKey, DecodedAreaFrame>(), $"map {home} lists no body for the entity" + (near is null ? " (no stored cell recorded)" : string.Empty));
    }

    private AreaHomeLoad Failed((int Map, long Sector) key, string detail)
    {
        _failed[key] = detail;
        return new(null, detail);
    }

    private static long Sector(CellCoord cell) => ((long)(cell.X >> 6) << 32) | (uint)(cell.Y >> 6);

    private static bool SameWindow(AreaHomeRead a, AreaHomeRead b) => a.CoreSectors is null
        ? b.CoreSectors is null
        : b.CoreSectors is not null && a.CoreSectors.SetEquals(b.CoreSectors);
}

/// <summary>
/// ARC-DEV-079 (MIG4): the viewer's home-map source: one strict read of a map of the game folder's module content stack (the whole map,
/// or a window of 3×3 core sectors around the stored cell when the map has more sector files than a whole-map plan admits), giving the
/// resolver's rows, the speakers' bodies (scene plan commands and visuals) and the frames those bodies draw, and nothing else (no lighting,
/// no art beyond those frames). It does not compare the viewer's archive arguments with the stack's sources or the expected digests: the
/// stack comes from the same game folder as the viewer's, and the read is derived data. <paramref name="facts"/> says which speakers can
/// follow (the viewer's companion facts of the module). A map the list does not name, one with no sector files, a window with no stored
/// cell to centre on, or one that does not read is a typed failure, never an exception.
/// </summary>
public sealed class AreaMapHomes(TerrainViewerArguments launch, Func<AreaSpeaker, AreaCompanionFacts?> facts) : IAreaHomeSource
{
    public AreaHomeLoad Read(int runtimeMap, CellCoord? near, CancellationToken token)
    {
        try { return ReadChecked(runtimeMap, near, token); }
        catch (Exception error) when (error is InvalidOperationException or AreaException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(null, $"map {runtimeMap} did not read: {error.GetType().Name} {error.Message}");
        }
    }

    private AreaHomeLoad ReadChecked(int runtimeMap, CellCoord? near, CancellationToken token)
    {
        var discovery = ContentMountPlan.Discover(launch.GameRoot ?? throw new InvalidOperationException("AREA_HOMES_GAME_ROOT_REQUIRED"));
        if (discovery.Status != ContentMountStatus.Ready) return new(null, "mount " + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(null, "stack " + opened.Status);
        using var stack = opened.Stack!;
        var list = MapList.Read(stack);
        if (list.Status != MapListStatus.Read) return new(null, "the map list did not read: " + list.Status);
        if (list.ByRuntime(runtimeMap) is not { } entry) return new(null, $"the map list has no runtime map {runtimeMap}");
        var catalog = WorldCatalog.Open(stack);
        var data = launch.DataRules ?? Arcanum.Data.Fixes.DataFixProfile.Steamwoven;
        if (catalog.Find(entry.Name) is not { } map) return new(null, $"the catalogue has no map {entry.Name}");
        var window = map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles;
        if (window && near is null) return new(null, $"map {entry.Name} opens only as a window and no stored cell was recorded to centre it on");
        var created = window
            ? AreaMapPlans.CreateWindow(stack, catalog, entry.Name, WorldSectorCoordinate.Containing(new(near!.Value.X, near.Value.Y)), data: data)
            : AreaMapPlans.Create(stack, catalog, entry.Name, data);
        if (created.Status != AreaMapPlanStatus.Ready) return new(null, $"map {entry.Name} has no plan: {created.Status}");
        token.ThrowIfCancellationRequested();
        var plan = created.Plan!;
        using var owner = plan.Open();
        var snapshot = owner.LoadStrict(token);
        var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, plan.Map.AggregatePath, 1);
        var build = AreaInventoryMap.Build(snapshot, steps, token);
        if (build.Map is not { } rows) return new(null, $"map {entry.Name} has no records: {build.Status} {build.Detail}");
        var core = window ? AreaHomeRead.CoreOf(snapshot) : null;
        // The rows are what the resolver needs; the bodies are a second result: a body or a frame that does not read leaves the rows
        // (and says so), never fails the whole read.
        try
        {
            var chart = AreaMapLoadOperation.Chart(snapshot);
            var scene = AreaScenePlan.Create(snapshot, chart);
            var templates = AreaCompanionTemplate.From(AreaConversationHost.FindSpeakers(snapshot), scene, snapshot.Visuals, chart, new(snapshot.RuntimeMap), facts);
            token.ThrowIfCancellationRequested();
            var frames = AreaLoadOperation.CopyFramesOf(owner, snapshot, templates.SelectMany(t => t.Commands).Select(c => c.Frame).Distinct().ToArray(), token);
            return new(new(snapshot.RuntimeMap, rows, templates, frames, core), build.Detail);
        }
        catch (Exception error) when (error is InvalidOperationException or AreaException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(new(snapshot.RuntimeMap, rows, [], new Dictionary<AreaFrameKey, DecodedAreaFrame>(), core),
                $"{build.Detail}; no bodies: {error.GetType().Name} {error.Message}");
        }
    }
}
