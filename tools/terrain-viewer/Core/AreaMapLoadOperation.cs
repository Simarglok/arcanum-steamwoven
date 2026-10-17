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
        var created = AreaMapPlans.Create(stack, catalog, mapName);
        if (created.Status != AreaMapPlanStatus.Ready) throw new InvalidOperationException("AREA_MAP_PLAN_" + created.Status.ToString().ToUpperInvariant());
        var mapPlan = created.Plan!;
        RequireSame(mapPlan.Files.Where(f => f.Key.Role == AreaSourceRole.Resource2).Select(f => f.Path), arguments.ResourcePath);
        RequireSame(mapPlan.Files.Where(f => f.Key.Role == AreaSourceRole.Resource1).Select(f => f.Path), arguments.ActorResourcePath!);
        Console.WriteLine($"ARC-RENDER-017 load stage=plan map={mapPlan.Map.Name} catalogue={mapPlan.Plan.CatalogueMap} sectors={mapPlan.Map.SectorFiles.Count}");
        token.ThrowIfCancellationRequested();

        using var owner = mapPlan.Open();
        AreaStepSnapshot? steps = null;
        var area = AreaLoadOperation.ReadWholeMap(generation, owner, token, snapshot =>
        {
            // Step evidence from this same strict publication, and one chart over every sector of the map.
            steps = AreaStepInspector.InspectWholeMap(snapshot, stack, mapPlan.Map.AggregatePath, generation);
            var chart = Chart(snapshot);
            return chart with { Spawn = chart.ToChart(Spawn(steps, mapPlan.StartTile, snapshot)) };
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
        AreaLightingPlan? lit = null; var lightsFailure = LightingFailure.None;
        if (lighting.Failure == LightingFailure.None)
        {
            var lights = StationaryLightReader.Read(stack, snapshot, token);
            lightsFailure = lights.Failure;
            lit = new(lighting.Source!, AreaAmbientPolicy.Channels(snapshot, plan))
            { Local = lights.Failure == LightingFailure.None ? AreaLocalLighting.Create(snapshot, plan.Chart, lights.Source!) : null };
        }
        Console.WriteLine($"ARC-RENDER-017 load stage=lighting result={lighting.Failure} stationary={lightsFailure}");
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
        area = area with { Lighting = lit, UnlitReason = lit is null ? lighting.Failure : null,
            UnlitLightsReason = lit is not null && lightsFailure != LightingFailure.None ? lightsFailure : null, FinalUsage = owner.Usage, ElapsedMilliseconds = elapsed };
        var mf = new InputFingerprint(moduleIdentity.Length, moduleIdentity.Sha256); var rf = new InputFingerprint(resourceIdentity.Length, resourceIdentity.Sha256);
        token.ThrowIfCancellationRequested();
        return new(generation, terrain, new Dictionary<TerrainTextureKey, DecodedTerrainTexture>(), terrainCommands.Length,
            terrainCommands.Count(command => command.Phase == 0), terrainCommands.Count(command => command.Phase == 1), 0, 0,
            mf, rf, mf, rf, area.CpuRgbaBytes + actor.CheckedRgbaBytes, elapsed, 1)
        { Area = area, ActorPlan = actor.Plan, ActorTextures = actor.Textures, AreaStepSnapshot = traversal,
            ActorResourceFingerprint = new(actorIdentity.Length, actorIdentity.Sha256), ActorResourceFingerprintAfter = new(actorIdentity.Length, actorIdentity.Sha256) };
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

    /// <summary>The map-list arrival tile when the walker can stand on it; otherwise the first core tile (by row, then
    /// column) where it can. A tile qualifies with open terrain and a cardinal neighbour it can step to and back from
    /// (so no body blocks the tile itself).</summary>
    internal static AreaStepCell Spawn(AreaStepSnapshot steps, WorldLocation? start, AreaSnapshot snapshot)
    {
        if (start is { } tile && CanStep(steps, new(tile.X, tile.Y))) return new(tile.X, tile.Y);
        foreach (var terrain in snapshot.Terrain.Where(t => t.Scope == AreaScope.Core).OrderBy(t => t.WorldY).ThenBy(t => t.WorldX))
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
