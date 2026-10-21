using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Lighting;
using Arcanum.Data.Maps;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-RENDER-017: loads one interior of the game folder's module content stack in the
/// AREA viewer under a whole-map plan (ARC-DATA-019) with whole-map step evidence (ARC-WORLD-018).
/// ARC-RENDER-024: a map with more sector files than a whole-map plan admits (the main world map) loads as a window
/// (ARC-DATA-032) around the transfer arrival, <see cref="TerrainViewerArguments.AreaMapSector"/> or the map-list start
/// tile, with window step evidence (ARC-DATA-034); the viewer moves the window as the actor walks.
/// The pinned exterior path (<see cref="AreaLoadOperation"/>) is untouched.</summary>
internal static class AreaMapLoadOperation
{
    private static readonly DatReaderOptions ViewerArchiveOptions = new() { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 };

    internal static TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken token)
    {
        var started = Environment.TickCount64;
        var mapName = arguments.AreaMapName ?? throw new InvalidOperationException("AREA_MAP_REQUIRED");
        var discovery = ContentMountPlan.Discover(arguments.GameRoot ?? throw new InvalidOperationException("AREA_MAP_GAME_ROOT_REQUIRED"));
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidOperationException("AREA_MAP_MOUNT_" + discovery.Status.ToString().ToUpperInvariant());
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidOperationException("AREA_MAP_STACK_" + opened.Status.ToString().ToUpperInvariant());
        using var stack = opened.Stack!;
        // The viewer's own archive arguments must be the stack's sources the plan reads.
        RequireSame(stack.Plan.SearchOrder.Where(s => s.Role == ContentSourceRole.ModuleArchive).Select(s => s.Path), arguments.ModulePath);
        var catalog = WorldCatalog.Open(stack);
        var centre = WindowCentre(stack, catalog, mapName, arguments);
        var created = centre is { } around ? AreaMapPlans.CreateWindow(stack, catalog, mapName, new(checked((int)around.X), checked((int)around.Y)))
            : AreaMapPlans.Create(stack, catalog, mapName);
        if (created.Status != AreaMapPlanStatus.Ready) throw new InvalidOperationException("AREA_MAP_PLAN_" + created.Status.ToString().ToUpperInvariant());
        var mapPlan = created.Plan!;
        RequireSame(mapPlan.Files.Where(f => f.Key.Role == AreaSourceRole.Resource2).Select(f => f.Path), arguments.ResourcePath);
        RequireSame(mapPlan.Files.Where(f => f.Key.Role == AreaSourceRole.Resource1).Select(f => f.Path), arguments.ActorResourcePath!);
        Console.WriteLine($"ARC-RENDER-017 load stage=plan map={mapPlan.Map.Name} catalogue={mapPlan.Plan.CatalogueMap} sectors={mapPlan.Map.SectorFiles.Count}" +
            (centre is { } shown ? $" window={shown.X},{shown.Y}" : string.Empty));
        token.ThrowIfCancellationRequested();
        // ARC-WORLD-031: the map's transitions and the maps this mode can open, from the same stack.
        var (transitions, destinations, transitionsFailure) = Transitions(stack, catalog, mapPlan.Map);
        Console.WriteLine($"ARC-WORLD-031 load stage=transitions sites={transitions?.Sites.Count} unconditional={transitions?.Unconditional.Count()} issues={transitions?.Issues.Count} destinations={destinations?.Count} failure={transitionsFailure}");
        token.ThrowIfCancellationRequested();

        using var owner = mapPlan.Open();
        AreaStepSnapshot? steps = null;
        var area = AreaLoadOperation.ReadWholeMap(generation, owner, token, snapshot =>
        {
            // Step evidence from this same strict publication, and one chart over every sector of the map.
            steps = AreaStepInspector.InspectWholeMap(snapshot, stack, mapPlan.Map.AggregatePath, generation);
            var chart = Chart(snapshot);
            // The arrival when admitted, else near the sector asked, else the map-list start (ARC-RENDER-024).
            return chart with { Spawn = chart.ToChart(Spawn(steps, mapPlan.StartTile, snapshot, arguments.AreaMapArrival, arguments.AreaMapSector)) };
        }, mapPlan.Limits);
        var snapshot = area.Snapshot; var plan = area.Plan;
        var moduleIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Module);
        var resourceIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource2);
        var actorIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource1);
        AreaLoadOperation.VerifyRequested(moduleIdentity, arguments.ExpectedModuleSha256);
        AreaLoadOperation.VerifyRequested(resourceIdentity, arguments.ExpectedResourceSha256);
        AreaLoadOperation.VerifyRequested(actorIdentity, arguments.ExpectedActorResourceSha256);
        var traversal = steps ?? throw new InvalidOperationException("AREA_MAP_STEPS_REQUIRED");
        Console.WriteLine($"ARC-RENDER-017 load stage=area-steps cells={traversal.AdmittedCells} unknown={traversal.TerrainUnknownCells + traversal.UnknownObjects} spawn={plan.Chart.Spawn}");
        token.ThrowIfCancellationRequested();

        using var module = DatArchive.Open(arguments.ModulePath, ViewerArchiveOptions);
        using var actorResource = DatArchive.Open(arguments.ActorResourcePath!, ViewerArchiveOptions);
        // ARC-WORLD-020: the AREA game view also draws the human's run ART, on maps as on the exterior.
        var actor = ActorDiagnosticSource.CreateWithBudget(module, actorResource, arguments.ActorInput!, 0, 0, token, TerrainLoadBudget.Default,
            null, TerrainViewerMode.WorldDiagnostic, ActorDiagnosticProfile.HumanFixedArea);
        if (checked(area.CpuRgbaBytes + actor.CheckedRgbaBytes) > TerrainCachePolicy.MaxRgbaBytes)
            throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Frame));
        // Day/night and stationary lights when the map's sources admit them; otherwise the map is drawn unlit and
        // says why. ARC-DATA-027: the lighting files come from the content stack in the phase the original reads
        // them. The three viewer archives read outside the owner are re-verified against the snapshot's identities
        // below; the step evidence's rule payloads from other stack sources are not (it is labelled structural).
        var lighting = AreaLightingReader.Read(stack, snapshot, token);
        AreaLightingPlan? lit = null; var lightsFailure = LightingFailure.None; var objectsFailure = LightingFailure.None; var objectLights = 0;
        var lightingStarted = Environment.TickCount64;
        if (lighting.Failure == LightingFailure.None)
        {
            var lights = StationaryLightReader.Read(stack, snapshot, token);
            lightsFailure = lights.Failure;
            // ARC-RENDER-030: the object lights join the stationary ones; when they cannot be read the map keeps the
            // stationary lights alone and says why.
            var objects = lights.Failure == LightingFailure.None ? ObjectLightReader.Read(stack, snapshot, token) : null;
            objectsFailure = objects?.Failure ?? LightingFailure.None;
            AreaLocalLighting? local = null;
            if (lights.Failure == LightingFailure.None && objects?.Source is { } source)
            {
                // The object lights widen the lattice and bring their saved frames: when either is out of reach, the map
                // keeps its stationary lights alone.
                try { local = AreaLocalLighting.Create(snapshot, plan.Chart, lights.Source!, source); }
                catch (InvalidOperationException refused) when (refused.Message is "LIGHT_LATTICE_LIMIT" or "LIGHT_FRAME_OUT_OF_RANGE")
                { objectsFailure = refused.Message == "LIGHT_LATTICE_LIMIT" ? LightingFailure.LimitExceeded : LightingFailure.LightArtUnsupported; }
            }
            if (lights.Failure == LightingFailure.None && local is null) local = AreaLocalLighting.Create(snapshot, plan.Chart, lights.Source!);
            objectLights = local?.ObjectLightCount ?? 0;
            lit = new(lighting.Source!, AreaAmbientPolicy.Channels(snapshot, plan)) { Local = local, ObjectLightsFailure = objectsFailure };
        }
        // ARC-WORLD-057: an unlit map's session starts from the module's start clock alone.
        var unlitStartup = lit is null ? AreaLightingReader.ReadStartup(stack) : null;
        Console.WriteLine($"ARC-RENDER-017 load stage=lighting result={lighting.Failure} stationary={lightsFailure} objects={objectsFailure} " +
            $"object-lights={objectLights} ms={Environment.TickCount64 - lightingStarted}");
        AreaLoadOperation.VerifyFiles([(arguments.ModulePath, moduleIdentity), (arguments.ResourcePath, resourceIdentity),
            (arguments.ActorResourcePath!, actorIdentity)], token);

        var terrainCommands = plan.Commands.Where(command => command.Identity is null).ToArray();
        var terrain = TerrainScenePlan.CreateSynthetic(plan.WorldBounds, terrainCommands.Select((command, index) =>
            new TerrainPlanInput(index, new(command.Frame.PayloadSha256, command.Frame.Entry.Ordinal, command.Frame.Entry.Flags,
                command.Frame.Entry.DecodedBytes, command.Frame.Entry.NameSha256, command.Frame.Group, command.Frame.Frame, command.Frame.Palette),
                command.Phase == 0 ? TerrainKind.Tile : TerrainKind.Facade, checked((int)(command.MapX - plan.Chart.OriginX)),
                checked((int)(command.MapY - plan.Chart.OriginY)), command.FlipX ? 0xC1u : 0u, command.Source.Width, command.Source.Height,
                new(TerrainDiagnosticMode.EditorDiagnostic, command.Phase, command.MapY, command.MapX))));
        var elapsed = Environment.TickCount64 - started;
        area = area with { ArtSources = mapPlan.Files.Where(f => f.Member is null && f.Key.Role is not (AreaSourceRole.Loose or AreaSourceRole.Module or AreaSourceRole.Prototype or AreaSourceRole.NormalGameMobile))
                .GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First().Path),
            Lighting = lit, UnlitReason = lit is null ? lighting.Failure : null,
            UnlitLightsReason = lit is not null && lightsFailure != LightingFailure.None ? lightsFailure : null, FinalUsage = owner.Usage, ElapsedMilliseconds = elapsed };
        var mf = new InputFingerprint(moduleIdentity.Length, moduleIdentity.Sha256); var rf = new InputFingerprint(resourceIdentity.Length, resourceIdentity.Sha256);
        token.ThrowIfCancellationRequested();
        return new(generation, terrain, new Dictionary<TerrainTextureKey, DecodedTerrainTexture>(), terrainCommands.Length,
            terrainCommands.Count(command => command.Phase == 0), terrainCommands.Count(command => command.Phase == 1), 0, 0,
            mf, rf, mf, rf, area.CpuRgbaBytes + actor.CheckedRgbaBytes, elapsed, 1)
        { Area = area, ActorPlan = actor.Plan, ActorTextures = actor.Textures, AreaStepSnapshot = traversal,
            MapTransitions = transitions, MapDestinations = destinations, MapTransitionsFailure = transitionsFailure, AreaMapWindowCentre = centre,
            UnlitSessionStartup = unlitStartup?.Startup, UnlitSessionStartupFailure = unlitStartup?.Failure,
            ActorResourceFingerprint = new(actorIdentity.Length, actorIdentity.Sha256), ActorResourceFingerprintAfter = new(actorIdentity.Length, actorIdentity.Sha256) };
    }

    /// <summary>ARC-RENDER-024: the centre of the window a map opens as, or null when it loads whole. A map with more sector
    /// files than <see cref="AreaMapPlans.MaxSectorFiles"/> opens as a window around the sector of the transfer's (or quick
    /// load's, or window move's) arrival, else the asked sector, else the sector of its map-list start tile (the new-game
    /// start of the main map: the map list's start tile, not <c>gameinit.mes</c>, whose start map and location are unused).</summary>
    internal static AreaSectorCoordinate? WindowCentre(ContentStack stack, WorldCatalog catalog, string mapName, TerrainViewerArguments arguments)
    {
        if (catalog.Find(mapName) is not { } map || map.SectorFiles.Count <= AreaMapPlans.MaxSectorFiles) return null;
        if (arguments.AreaMapArrival is null && arguments.AreaMapSector is { } asked) return asked;
        var tile = arguments.AreaMapArrival ?? MapList.Read(stack).Find(map.Name)?.Start ?? throw new InvalidOperationException("AREA_MAP_WINDOW_NO_START");
        var sector = WorldSectorCoordinate.Containing(tile);
        return new(sector.X, sector.Y);
    }

    /// <summary>The widest chart (in sectors) a whole map may span; the largest installed interior spans 14.</summary>
    internal const int MaxChartSectors = 32;

    /// <summary>The whole map's chart: origin at the first core sector row and column, and a square cell range
    /// covering every core sector (the largest installed interior spans 14 sectors, 896 cells).</summary>
    internal static AreaChart Chart(AreaSnapshot snapshot) =>
        Chart(snapshot.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).Select(s => s.Coordinate), snapshot.RuntimeMap);

    internal static AreaChart Chart(IEnumerable<AreaSectorCoordinate> coreSectors, int runtimeMap)
    {
        var core = coreSectors.ToArray();
        if (core.Length == 0) throw new InvalidOperationException("AREA_CHART_CORE_REQUIRED");
        long minX = core.Min(c => c.X), minY = core.Min(c => c.Y);
        var span = Math.Max(core.Max(c => c.X) - minX, core.Max(c => c.Y) - minY) + 1;
        if (span > MaxChartSectors) throw new InvalidOperationException("AREA_MAP_CHART_TOO_LARGE");
        return new AreaChart(checked(minX * 64), checked(minY * 64), default, $"MAP_{runtimeMap}_WHOLE_MAP_CHART")
            { MinimumCell = 0, MaximumCell = checked((int)(span * 64 - 1)) };
    }

    /// <summary>ARC-WORLD-031: the map's transitions (ARC-DATA-025) and every listed map with a sector file, which map mode
    /// can open (whole, or as a window since ARC-RENDER-024), by runtime number. An unlisted map has no transitions (the
    /// reason is returned instead).</summary>
    internal static (MapTransitionSet? Transitions, IReadOnlyDictionary<int, AreaMapDestination>? Destinations, string? Failure) Transitions(
        ContentStack stack, WorldCatalog catalog, WorldMap map)
    {
        var list = MapList.Read(stack);
        if (list.Status != MapListStatus.Read) return (null, null, "MAP_LIST_" + list.Status.ToString().ToUpperInvariant());
        if (list.Find(map.Name) is null) return (null, null, "MAP_NOT_LISTED");
        using var scripts = Arcanum.Data.Scripts.ScriptContent.Over(stack);
        var transitions = MapTransitions.Read(catalog, list, map, scripts);
        var destinations = new Dictionary<int, AreaMapDestination>();
        foreach (var entry in list.Entries)
            if (catalog.Find(entry.Name) is { SectorFiles.Count: > 0 } folder)
                destinations.TryAdd(entry.Runtime, new(entry.Runtime, entry.Catalogue, folder.Name));
        return (transitions, destinations, null);
    }

    /// <summary>A transfer's arrival tile (ARC-WORLD-031) when the step evidence admits it: the original places the player
    /// there without a passability check (W9). ARC-RENDER-024: with a sector asked (<paramref name="near"/>), the core tile
    /// nearest that sector's middle where the walker can stand. Otherwise the map-list arrival tile when the walker can stand
    /// on it, else the first core tile (by row, then column) where it can. A tile qualifies with open terrain and a cardinal
    /// neighbour it can step to and back from (so no body blocks the tile itself).</summary>
    internal static AreaStepCell Spawn(AreaStepSnapshot steps, WorldLocation? start, AreaSnapshot snapshot, WorldLocation? arrival = null,
        AreaSectorCoordinate? near = null)
    {
        if (arrival is { } authored && steps.IsAdmitted(new(authored.X, authored.Y))) return new(authored.X, authored.Y);
        var core = snapshot.Terrain.Where(t => t.Scope == AreaScope.Core);
        if (near is { } sector)
        {
            long midX = sector.OriginX + 32, midY = sector.OriginY + 32;
            core = core.OrderBy(t => Math.Abs(t.WorldX - midX) + Math.Abs(t.WorldY - midY)).ThenBy(t => t.WorldY).ThenBy(t => t.WorldX);
        }
        else
        {
            if (start is { } tile && CanStep(steps, new(tile.X, tile.Y))) return new(tile.X, tile.Y);
            core = core.OrderBy(t => t.WorldY).ThenBy(t => t.WorldX);
        }
        foreach (var terrain in core)
            if (CanStep(steps, new(terrain.WorldX, terrain.WorldY))) return new(terrain.WorldX, terrain.WorldY);
        throw new InvalidOperationException("AREA_MAP_NO_WALKABLE_TILE");
    }

    private static bool CanStep(AreaStepSnapshot steps, AreaStepCell cell)
    {
        if (!steps.IsAdmitted(cell) || steps.Terrain(cell) is not { Disposition: CardinalTraversalDisposition.Open }) return false;
        for (var direction = 1; direction < 8; direction += 2)
        {
            var (dx, dy) = direction switch { 1 => (-1, 0), 3 => (0, 1), 5 => (1, 0), _ => (0, -1) };
            var next = new AreaStepCell(cell.X + dx, cell.Y + dy);
            if (!steps.IsAdmitted(next)) continue;
            // Out and back: the tile can be left, and it can be entered (no body blocks it).
            if (Open(steps, cell, next, direction) && Open(steps, next, cell, (direction + 4) % 8)) return true;
        }
        return false;
    }

    private static bool Open(AreaStepSnapshot steps, AreaStepCell from, AreaStepCell to, int direction)
    {
        var step = steps.Assess(steps.Request(from, to, (CardinalTraversalDirection)direction));
        return step.Coverage == CardinalTraversalCoverage.Supported && step.Disposition == CardinalTraversalDisposition.Open;
    }

    // Exactly one stack source, and the viewer argument names it (full paths, ordinal: no case folding or symlink
    // resolution, so a differently spelled path is refused, never guessed).
    private static void RequireSame(IEnumerable<string> expected, string actual)
    {
        var sources = expected.Take(2).ToArray();
        if (sources.Length != 1 || !string.Equals(Path.GetFullPath(sources[0]), Path.GetFullPath(actual), StringComparison.Ordinal))
            throw new InvalidOperationException("AREA_MAP_SOURCES_MISMATCH");
    }
}
