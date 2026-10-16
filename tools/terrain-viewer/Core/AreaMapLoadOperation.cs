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
        RequireSame(stack.Plan.SearchOrder.Single(s => s.Role == ContentSourceRole.ModuleArchive).Path, arguments.ModulePath);
        var catalog = WorldCatalog.Open(stack);
        var created = AreaMapPlans.Create(stack, catalog, mapName);
        if (created.Status != AreaMapPlanStatus.Ready) throw new InvalidOperationException("AREA_MAP_PLAN_" + created.Status.ToString().ToUpperInvariant());
        var mapPlan = created.Plan!;
        RequireSame(mapPlan.Files.Single(f => f.Key.Role == AreaSourceRole.Resource2).Path, arguments.ResourcePath);
        RequireSame(mapPlan.Files.Single(f => f.Key.Role == AreaSourceRole.Resource1).Path, arguments.ActorResourcePath!);
        Console.WriteLine($"ARC-RENDER-017 load stage=plan map={mapPlan.Map.Name} catalogue={mapPlan.Plan.CatalogueMap} sectors={mapPlan.Map.SectorFiles.Count}");
        token.ThrowIfCancellationRequested();

        using var owner = mapPlan.Open();
        AreaStepSnapshot? steps = null;
        var area = AreaLoadOperation.ReadArea(generation, owner, token, snapshot =>
        {
            // Step evidence from this same strict publication, and one chart over every sector of the map.
            steps = AreaStepInspector.InspectWholeMap(snapshot, stack, mapPlan.Map.AggregatePath, generation);
            var chart = Chart(snapshot);
            return chart with { Spawn = chart.ToChart(Spawn(steps, mapPlan.StartTile, snapshot)) };
        }, mapPlan.Limits, wholeMap: true);
        var snapshot = area.Snapshot; var plan = area.Plan;
        var traversal = steps ?? throw new InvalidOperationException("AREA_MAP_STEPS_REQUIRED");
        Console.WriteLine($"ARC-RENDER-017 load stage=area-steps cells={traversal.AdmittedCells} unknown={traversal.TerrainUnknownCells + traversal.UnknownObjects} spawn={plan.Chart.Spawn}");
        token.ThrowIfCancellationRequested();

        using var module = DatArchive.Open(arguments.ModulePath, ViewerArchiveOptions);
        using var actorResource = DatArchive.Open(arguments.ActorResourcePath!, ViewerArchiveOptions);
        var actor = ActorDiagnosticSource.Create(module, actorResource, arguments.ActorInput!, 0, 0, token, mode: TerrainViewerMode.WorldDiagnostic);
        if (checked(area.CpuRgbaBytes + actor.CheckedRgbaBytes) > TerrainCachePolicy.MaxRgbaBytes)
            throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Frame));
        // Day/night and stationary lights when the map's sources admit them; otherwise the map is drawn unlit.
        var lighting = AreaLightingReader.Read(module, snapshot, token);
        AreaLightingPlan? lit = null;
        if (lighting.Failure == LightingFailure.None)
        {
            using var artResource = DatArchive.Open(arguments.ResourcePath, ViewerArchiveOptions);
            var lights = StationaryLightReader.Read(artResource, snapshot, token);
            lit = new(lighting.Source!, AreaAmbientPolicy.Channels(snapshot, plan))
            { Local = lights.Failure == LightingFailure.None ? AreaLocalLighting.Create(snapshot, plan.Chart, lights.Source!) : null };
        }
        Console.WriteLine($"ARC-RENDER-017 load stage=lighting result={lighting.Failure}");

        var terrainCommands = plan.Commands.Where(command => command.Identity is null).ToArray();
        var terrain = TerrainScenePlan.CreateSynthetic(plan.WorldBounds, terrainCommands.Select((command, index) =>
            new TerrainPlanInput(index, new(command.Frame.PayloadSha256, command.Frame.Entry.Ordinal, command.Frame.Entry.Flags,
                command.Frame.Entry.DecodedBytes, command.Frame.Entry.NameSha256, command.Frame.Group, command.Frame.Frame, command.Frame.Palette),
                command.Phase == 0 ? TerrainKind.Tile : TerrainKind.Facade, checked((int)(command.MapX - plan.Chart.OriginX)),
                checked((int)(command.MapY - plan.Chart.OriginY)), command.FlipX ? 0xC1u : 0u, command.Source.Width, command.Source.Height,
                new(TerrainDiagnosticMode.EditorDiagnostic, command.Phase, command.MapY, command.MapX))));
        var moduleIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Module);
        var resourceIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource2);
        var actorIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource1);
        var elapsed = Environment.TickCount64 - started;
        area = area with { Lighting = lit, FinalUsage = owner.Usage, ElapsedMilliseconds = elapsed };
        var mf = new InputFingerprint(moduleIdentity.Length, moduleIdentity.Sha256); var rf = new InputFingerprint(resourceIdentity.Length, resourceIdentity.Sha256);
        token.ThrowIfCancellationRequested();
        return new(generation, terrain, new Dictionary<TerrainTextureKey, DecodedTerrainTexture>(), terrainCommands.Length,
            terrainCommands.Count(command => command.Phase == 0), terrainCommands.Count(command => command.Phase == 1), 0, 0,
            mf, rf, mf, rf, area.CpuRgbaBytes + actor.CheckedRgbaBytes, elapsed, 1)
        { Area = area, ActorPlan = actor.Plan, ActorTextures = actor.Textures, AreaStepSnapshot = traversal,
            ActorResourceFingerprint = new(actorIdentity.Length, actorIdentity.Sha256), ActorResourceFingerprintAfter = new(actorIdentity.Length, actorIdentity.Sha256) };
    }

    /// <summary>The whole map's chart: origin at the first core sector row and column, and a square cell range
    /// covering every core sector (the largest installed interior spans 14 sectors, 896 cells).</summary>
    internal static AreaChart Chart(AreaSnapshot snapshot)
    {
        var core = snapshot.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).Select(s => s.Coordinate).ToArray();
        if (core.Length == 0) throw new InvalidOperationException("AREA_CHART_CORE_REQUIRED");
        long minX = core.Min(c => c.X), minY = core.Min(c => c.Y);
        var span = Math.Max(core.Max(c => c.X) - minX, core.Max(c => c.Y) - minY) + 1;
        return new AreaChart(checked(minX * 64), checked(minY * 64), default, $"MAP_{snapshot.RuntimeMap}_WHOLE_MAP_CHART")
            { MinimumCell = 0, MaximumCell = checked((int)(span * 64 - 1)) };
    }

    /// <summary>The map-list arrival tile when the walker can step from it; otherwise the first tile
    /// (by row, then column) of the map that has open terrain and at least one open step.</summary>
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
            var step = steps.Assess(steps.Request(cell, next, (CardinalTraversalDirection)direction));
            if (step.Coverage == CardinalTraversalCoverage.Supported && step.Disposition == CardinalTraversalDisposition.Open) return true;
        }
        return false;
    }

    private static void RequireSame(string expected, string actual)
    {
        if (!string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.Ordinal))
            throw new InvalidOperationException("AREA_MAP_SOURCES_MISMATCH");
    }
}
