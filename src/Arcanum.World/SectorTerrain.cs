using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Fixes;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>What a sector read gave: the evidence, or why not (no exception for an ordinary failure).</summary>
public enum SectorTerrainStatus
{
    /// <summary>The sector's terrain, objects and passability were read.</summary>
    Ready,
    /// <summary>The sector lies outside the map's size (the original's sector lock refuses it, ARC-DATA-031).</summary>
    OutsideMap,
    /// <summary>The sector has no own file and no terrain template, or the map's source is one the owner does not model (the original fills it with a
    /// tile family, ARC-DATA-031): there is no evidence to give.</summary>
    Unsupported,
    /// <summary>A source failed to read or its evidence is incomplete (the detail names the stage).</summary>
    Refused,
}

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §3.5): the terrain of one sector of a map: the object rows the sector's cells hold (its static rows and the mobile rows
/// standing there, with the fields the content stack gives them) and the step evidence of its cells, an <see cref="AreaStepSnapshot"/> that admits
/// this sector only. Evidence of several sectors joins into one that admits them all (<see cref="AreaStepSnapshot.Union"/>), so a step across a
/// sector border is assessed as a window holding both would assess it.
/// </summary>
public sealed class SectorTerrain
{
    public SectorTerrain(ResidentSector sector, AreaStepSnapshot steps, IReadOnlyList<AreaObjectRecord> objects)
    { Sector = sector; Steps = steps; Objects = objects; }

    public ResidentSector Sector { get; }
    /// <summary>The evidence of this sector's cells (it admits this sector only).</summary>
    public AreaStepSnapshot Steps { get; }
    /// <summary>The object rows whose world cell lies in this sector: the sector file's objects and the map's mobile objects standing there.</summary>
    public IReadOnlyList<AreaObjectRecord> Objects { get; }
}

/// <summary>The result of <see cref="ISectorTerrain.Read"/>.</summary>
public sealed record SectorTerrainRead(SectorTerrainStatus Status, SectorTerrain? Terrain, string Detail);

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §3.5, RL11): the sector source port of the host's chart: rows and passability of one sector of the open map by its
/// coordinates, read from the content stack as the original reads a sector it loads. Derived data: the port holds no cache and keeps no state of the
/// session; the caller (the loaded-sector chart) holds what the sector cache holds. A read may take a while (it reads the sector and its ring) and
/// may run on a worker: it returns data, and the session applies the cache transitions on its own thread (K1 §6 risk 6).
/// </summary>
public interface ISectorTerrain
{
    SectorTerrainRead Read(ResidentSector sector);
}

/// <summary>
/// ARC-DEV-118: <see cref="ISectorTerrain"/> over a game folder's content stack: the sector is the single core sector of a window plan
/// (<see cref="AreaMapPlans.CreateWindow"/> with radius 0: the sector, which the plan takes from its own file or from its terrain template, and
/// its ring of context sectors), read as the area owner reads a window, with the whole-map step rules
/// (<see cref="AreaStepInspector.InspectWholeMap"/>, ARC-DATA-034); the mobile objects come from the map's aggregate, patched as the original
/// patches them at a map open. Not thread-safe (the catalog and stack are shared): one read at a time.
/// </summary>
public sealed class ContentSectorTerrain : ISectorTerrain, IDisposable
{
    private readonly ContentStack? _owned;
    private readonly ContentStack _stack;
    private readonly WorldCatalog _catalog;
    private readonly string _map;
    private readonly DataFixProfile? _data;

    /// <summary>A port over <paramref name="stack"/> (borrowed: the caller disposes it) for the map named <paramref name="mapName"/>.</summary>
    public ContentSectorTerrain(ContentStack stack, WorldCatalog catalog, string mapName, DataFixProfile? data = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _map = mapName ?? throw new ArgumentNullException(nameof(mapName));
        _data = data;
    }

    private ContentSectorTerrain(ContentStack owned, string mapName, DataFixProfile? data) : this(owned, WorldCatalog.Open(owned), mapName, data) => _owned = owned;

    /// <summary>The port over the game folder's own content stack (owned: <see cref="Dispose"/> closes it), or null with the reason.</summary>
    public static ContentSectorTerrain? Open(string gameRoot, string mapName, out string detail, DataFixProfile? data = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameRoot);
        ArgumentException.ThrowIfNullOrEmpty(mapName);
        var discovery = ContentMountPlan.Discover(gameRoot);
        if (discovery.Status != ContentMountStatus.Ready) { detail = "mount " + discovery.Status; return null; }
        var opened = ContentStack.OpenShared(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) { detail = "stack " + opened.Status; return null; }
        var port = new ContentSectorTerrain(opened.Stack!, mapName, data);
        if (port._catalog.Find(mapName) is null) { port.Dispose(); detail = "unknown map " + mapName; return null; }
        detail = "ready";
        return port;
    }

    public SectorTerrainRead Read(ResidentSector sector)
    {
        if (sector.X < 0 || sector.Y < 0) return new(SectorTerrainStatus.OutsideMap, null, "negative sector");
        var created = AreaMapPlans.CreateWindow(_stack, _catalog, _map, new(sector.X, sector.Y), 0, _data);
        switch (created.Status)
        {
            case AreaMapPlanStatus.Ready: break;
            case AreaMapPlanStatus.OutsideMap: return new(SectorTerrainStatus.OutsideMap, null, "outside the map");
            case AreaMapPlanStatus.UnsupportedSource: return new(SectorTerrainStatus.Unsupported, null, created.Detail ?? "unsupported source");
            default: return new(SectorTerrainStatus.Refused, null, $"plan {created.Status} {created.Detail}".TrimEnd());
        }
        var plan = created.Plan!;
        try
        {
            using var owner = plan.Open();
            var inspection = owner.Inspect();
            if (!inspection.IsComplete) return new(SectorTerrainStatus.Refused, null, $"snapshot incomplete: {inspection.Issues.Count} issue(s)");
            var snapshot = inspection.Snapshot!;
            var steps = AreaStepInspector.InspectWholeMap(snapshot, _stack, plan.Map.AggregatePath, 1);
            var objects = snapshot.Objects.Where(o => o.WorldX is { } x && o.WorldY is { } y && x >> 6 == sector.X && y >> 6 == sector.Y).ToArray();
            return new(SectorTerrainStatus.Ready, new(sector, steps, objects), $"{objects.Length} objects");
        }
        catch (Exception error) when (error is AreaStepInvocationException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new(SectorTerrainStatus.Refused, null, error.GetType().Name + (error is AreaStepInvocationException step ? $" {step.Code} at {step.Stage}" : ""));
        }
    }

    public void Dispose() => _owned?.Dispose();
}

/// <summary>
/// ARC-DEV-118: <see cref="ISectorTerrain"/> that answers the sectors a loaded window already holds from the window's own evidence (the viewer built
/// them with the window, which costs nothing now: <see cref="AreaStepSnapshot.Restrict"/> and the snapshot's objects) and leaves every other sector to
/// <paramref name="fallback"/> (<see cref="ContentSectorTerrain"/>, which reads a sector in seconds). A window's core sectors are the ones the
/// hero's cache loads first, so the arrival costs no read.
/// </summary>
public sealed class WindowSectorTerrain(AreaSnapshot window, AreaStepSnapshot steps, ISectorTerrain fallback) : ISectorTerrain
{
    private readonly AreaSnapshot _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly AreaStepSnapshot _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    private readonly ISectorTerrain _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

    public SectorTerrainRead Read(ResidentSector sector)
    {
        if (!_steps.AdmittedSectors.Contains((sector.X, sector.Y))) return _fallback.Read(sector);
        var objects = _window.Objects.Where(o => o.WorldX is { } x && o.WorldY is { } y && x >> 6 == sector.X && y >> 6 == sector.Y).ToArray();
        return new(SectorTerrainStatus.Ready, new(sector, _steps.Restrict([(sector.X, sector.Y)]), objects), $"{objects.Length} objects from the window");
    }
}
