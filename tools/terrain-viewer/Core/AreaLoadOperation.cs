using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

namespace Arcanum.TerrainViewer.Core;

public sealed record DecodedAreaFrame(int Width, int Height, byte[] Rgba);
public sealed record AreaLoadResult(long Generation, AreaSnapshot Snapshot, AreaScenePlan Plan,
    IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames, AreaBudgetUsage InitialUsage, AreaBudgetUsage FinalUsage,
    long CpuRgbaBytes, long AllocatedBytes, long ManagedBytes, long ProcessBytes, long ElapsedMilliseconds)
{
    /// <summary>ARC-RENDER-010 day/night inputs; absent for structural fixtures (drawn unlit).</summary>
    public AreaLightingPlan? Lighting { get; init; }
    /// <summary>ARC-RENDER-011 wall check inputs, validated at load (empty when the area has no walls).</summary>
    public IReadOnlyList<AreaWallPiece> Walls { get; init; } = Array.Empty<AreaWallPiece>();
    /// <summary>ARC-RENDER-017: why a whole map is drawn unlit (its lighting sources were not admitted); null otherwise.</summary>
    public LightingFailure? UnlitReason { get; init; }
    /// <summary>ARC-RENDER-017: why a lit whole map has no stationary lights; null otherwise.</summary>
    public LightingFailure? UnlitLightsReason { get; init; }
    /// <summary>ARC-COMBAT-005: the archive file of each input key the snapshot's art came from (empty for fixtures), where a
    /// fallen pose is read on demand (<see cref="AreaFallenPoses"/>).</summary>
    public IReadOnlyDictionary<AreaInputKey, string> ArtSources { get; init; } = new Dictionary<AreaInputKey, string>();
}

/// <summary>Admitted CPU frame repository limits; GPU residency is a subset.</summary>
public static class AreaRepositoryPolicy
{
    /// <summary>The pinned plan's own limits (<see cref="Arcanum.Data.Area.AreaLoaderOptions.PinnedVisuals"/> and
    /// <see cref="Arcanum.Data.Area.AreaLoaderOptions.PinnedFrameRgbaBytes"/>, the exterior's totals); only the pinned
    /// profiles' frame copy uses them (ARC-DATA-029).</summary>
    public const int MaxFrames = Arcanum.Data.Area.AreaLoaderOptions.PinnedVisuals;
    public const long MaxCpuBytes = Arcanum.Data.Area.AreaLoaderOptions.PinnedFrameRgbaBytes;
    /// <summary>ARC-RENDER-017: a whole interior map's CPU repository (every distinct frame it draws), and since
    /// ARC-DATA-029 the bound of the visible frames on the GPU. Census of the 80 interiors: at most 4,557 frames and
    /// 56.2 MiB (docs/world/limits-census.md).</summary>
    public const int MaxMapFrames = 16384;
    public const long MaxMapCpuBytes = 160L * 1024 * 1024;
}

/// <summary>Finite per-generation CPU repository, copied through one strict DATA009
/// owner frame batch. No provider decode is repeated by a camera pan.</summary>
internal static class AreaLoadOperation
{
    internal const int MaxFrames = AreaRepositoryPolicy.MaxFrames;
    internal const long MaxCpuBytes = AreaRepositoryPolicy.MaxCpuBytes;
    internal static TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken token)
    {
        // ARC-WORLD-027: the hero is read first (cheap); an explicit --hero that cannot be read fails the load (no silent
        // diagnostic fallback).
        AreaHeroLoad? hero = null;
        if (arguments.Hero is { } premade)
        {
            hero = AreaStartingHero.Load(arguments.ModulePath, premade);
            if (hero.Status != AreaHeroStatus.Ready) throw new InvalidOperationException($"AREA_HERO_{hero.Status} {hero.Detail}");
            Console.WriteLine($"ARC-WORLD-027 load stage=hero name={premade.Name} prototype={hero.Definition!.Prototype} flag={hero.Flag} gold={hero.Definition.Gold} kit={hero.Definition.Kit.Count} dropped={hero.DroppedChanges}");
            token.ThrowIfCancellationRequested();
        }
        var result = arguments.AreaMapName is not null ? AreaMapLoadOperation.Execute(generation, arguments, token) : ExecuteExterior(generation, arguments, token);
        return hero is null ? result : result with { Hero = hero };
    }

    private static TerrainLoadResult ExecuteExterior(long generation, TerrainViewerArguments arguments, CancellationToken token)
    {
        var started = Environment.TickCount64; var allocated = GC.GetTotalAllocatedBytes();
        token.ThrowIfCancellationRequested();
        using var owner = AreaOriginalValidator.OpenFiles(arguments.ModulePath, arguments.ActorResourcePath!, arguments.ResourcePath,
            arguments.AreaMobilePath!, arguments.PrototypePath!);
        Console.WriteLine("ARC-RENDER-007 load stage=authenticated");
        var area = ReadArea(generation, owner, token);
        var snapshot = area.Snapshot; var plan = area.Plan; var retained = area.CpuRgbaBytes;
        var moduleIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Module);
        var resourceIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource2);
        var actorIdentity = snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource1);
        VerifyRequested(moduleIdentity, arguments.ExpectedModuleSha256);
        VerifyRequested(resourceIdentity, arguments.ExpectedResourceSha256);
        VerifyRequested(actorIdentity, arguments.ExpectedActorResourceSha256);
        token.ThrowIfCancellationRequested();
        // Whole-core T0 step evidence from this same strict publication and the pinned rule payloads.
        // The Data construction is not cancellable; cancellation is observed right after it.
        var traversal = AreaStepInspector.ValidateOriginalData(snapshot, arguments.ModulePath, arguments.ResourcePath,
            arguments.AreaEffectsPath!, arguments.AreaPatchPath!, arguments.AreaMobilePath!, generation);
        Console.WriteLine($"ARC-RENDER-007 load stage=area-steps cells={traversal.AdmittedCells} unknown={traversal.TerrainUnknownCells + traversal.UnknownObjects}");
        token.ThrowIfCancellationRequested();
        VerifyAreaSteps(generation, snapshot, traversal);
        using var module = DatArchive.Open(arguments.ModulePath, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        using var actorResource = DatArchive.Open(arguments.ActorResourcePath!, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        // ARC-WORLD-020: the AREA game view also draws the human's run ART.
        var actor = ActorDiagnosticSource.CreateWithBudget(module, actorResource, arguments.ActorInput!, 0, 0, token, TerrainLoadBudget.Default,
            null, TerrainViewerMode.WorldDiagnostic, ActorDiagnosticProfile.HumanFixedArea);
        if (checked(retained + actor.CheckedRgbaBytes) > TerrainCachePolicy.MaxRgbaBytes)
            throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Frame));
        // Lighting rules and sector scheme words come from the same module archive, bound to this
        // snapshot's sector identities; the module file is re-verified below.
        var lighting = AreaLightingReader.Read(module, snapshot, token);
        if (lighting.Failure != LightingFailure.None) throw new InvalidOperationException("AREA_LIGHTING_" + lighting.Failure);
        Console.WriteLine($"ARC-RENDER-010 load stage=lighting mapDefault={lighting.Source!.MapDefaultScheme} schemes={lighting.Source.Schemes.Count} sectors={lighting.Source.SectorSchemes.Count}");
        // ARC-RENDER-012: stationary sector lights from the snapshot's retained wire, their ART from the
        // terrain resource (Resource2), which is re-verified below with the module.
        using var artResource = DatArchive.Open(arguments.ResourcePath, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var local = ReadLocalLighting(artResource, snapshot, AreaChart.Exterior, token);
        // The three archives read outside the owner (actor, lighting) are re-verified after those reads,
        // hashed in parallel. With a cave, its owner's own opening authentication of the same three
        // paths runs later still and must reproduce these identities (checked below), so it serves as
        // this check (ARC-RENDER-009).
        if (arguments.AreaCaveMobilePath is null)
            VerifyFiles([(arguments.ModulePath, moduleIdentity), (arguments.ResourcePath, resourceIdentity), (arguments.ActorResourcePath!, actorIdentity)], token);
        // A genuine terrain plan supplies the legacy result's geometry, not a
        // five-object/one-sector stand-in. Area publication uses Plan below.
        var terrainCommands = plan.Commands.Where(command => command.Identity is null).ToArray();
        var terrain = TerrainScenePlan.CreateSynthetic(plan.WorldBounds, terrainCommands.Select((command, index) =>
            new TerrainPlanInput(index, new(command.Frame.PayloadSha256, command.Frame.Entry.Ordinal, command.Frame.Entry.Flags,
                command.Frame.Entry.DecodedBytes, command.Frame.Entry.NameSha256, command.Frame.Group, command.Frame.Frame, command.Frame.Palette),
                command.Phase == 0 ? TerrainKind.Tile : TerrainKind.Facade, command.MapX - AreaScenePlan.CameraOriginX,
                command.MapY - AreaScenePlan.CameraOriginY, command.FlipX ? 0xC1u : 0u, command.Source.Width, command.Source.Height,
                new(TerrainDiagnosticMode.EditorDiagnostic, command.Phase, command.MapY, command.MapX))));
        // ARC-WORLD-009: the cave under its own pinned profile, its chart starting at the exterior endpoint's arrival.
        AreaCaveLoad? cave = null;
        if (arguments.AreaCaveMobilePath is { } caveMobile)
        {
            using var caveOwner = AreaOriginalValidator.OpenCaveFiles(arguments.ModulePath, arguments.ActorResourcePath!, arguments.ResourcePath,
                caveMobile, arguments.PrototypePath!);
            var entrance = snapshot.Endpoints.Single();
            // One strict load: the chart is derived from the snapshot ReadArea publishes.
            var caveArea = ReadArea(generation, caveOwner, token, loaded =>
            {
                var chart = AreaChart.For(loaded);
                return chart with { Spawn = chart.ToChart(new(entrance.ArrivalX, entrance.ArrivalY)) };
            });
            // The cave owner authenticated these paths after the actor and lighting reads (see above).
            foreach (var identity in new[] { moduleIdentity, resourceIdentity, actorIdentity })
                if (!caveArea.Snapshot.Inputs.Any(cave => cave.Key == identity.Key && cave.Length == identity.Length && cave.Sha256 == identity.Sha256))
                    throw new AreaException(new(AreaErrorCode.InputChangedDuringRead, AreaStage.Input));
            var caveChart = caveArea.Plan.Chart;
            var caveSteps = AreaStepInspector.ValidateOriginalData(caveArea.Snapshot, arguments.ModulePath, arguments.ResourcePath,
                arguments.AreaEffectsPath!, arguments.AreaPatchPath!, caveMobile, generation);
            if (caveSteps.EvidenceEpoch != generation || caveSteps.InputIdentity.AreaProfile != caveArea.Snapshot.Profile ||
                caveSteps.InputIdentity.Kind != AreaStepInputKind.AuthenticatedOriginal || !caveSteps.IsAdmitted(caveChart.ToWorld(caveChart.Spawn)) ||
                !caveArea.Snapshot.Inputs.All(i => caveSteps.InputIdentity.Sources.Any(s => s.Length == i.Length && s.Sha256 == i.Sha256)) ||
                entrance.RuntimeDestination != caveArea.Snapshot.RuntimeMap || entrance.CatalogueDestination != caveArea.Snapshot.CatalogueMap)
                throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Publication));
            if (checked(retained + actor.CheckedRgbaBytes + caveArea.CpuRgbaBytes) > TerrainCachePolicy.MaxRgbaBytes)
                throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Frame));
            retained = checked(retained + caveArea.CpuRgbaBytes);
            // ARC-RENDER-012: the cave's own ambient source (its map default and sector word) and its lights.
            var caveLighting = AreaLightingReader.Read(module, caveArea.Snapshot, token);
            if (caveLighting.Failure != LightingFailure.None) throw new InvalidOperationException("AREA_CAVE_LIGHTING_" + caveLighting.Failure);
            var caveLocal = ReadLocalLighting(artResource, caveArea.Snapshot, caveChart, token);
            Console.WriteLine($"ARC-RENDER-012 load stage=cave-lighting mapDefault={caveLighting.Source!.MapDefaultScheme} schemes={caveLighting.Source.Schemes.Count} lights={caveLocal.LightCount}");
            caveArea = caveArea with { Lighting = new(caveLighting.Source, AreaAmbientPolicy.Channels(caveArea.Snapshot, caveArea.Plan)) { Local = caveLocal } };
            cave = new(caveArea, caveSteps, caveChart);
            Console.WriteLine($"ARC-WORLD-009 load stage=cave visuals={caveArea.Snapshot.Visuals.Count} cells={caveSteps.AdmittedCells} walls={caveSteps.WallEdges}");
            // The cave lighting reads came after the cave owner's authentication: verify both archives again.
            VerifyFiles([(arguments.ModulePath, moduleIdentity), (arguments.ResourcePath, resourceIdentity)], token);
        }
        var elapsed = Environment.TickCount64 - started;
        var artSources = new Dictionary<AreaInputKey, string>
        { [new(AreaSourceRole.Resource1)] = arguments.ActorResourcePath!, [new(AreaSourceRole.Resource2)] = arguments.ResourcePath };
        if (cave is not null) cave = cave with { Area = cave.Area with { ArtSources = artSources } };
        area = area with { ArtSources = artSources, Lighting = new(lighting.Source, AreaAmbientPolicy.Channels(snapshot, plan)) { Local = local }, FinalUsage = owner.Usage, AllocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
            ManagedBytes = GC.GetTotalMemory(false), ProcessBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,
            ElapsedMilliseconds = elapsed };
        var mf = new InputFingerprint(moduleIdentity.Length, moduleIdentity.Sha256); var rf = new InputFingerprint(resourceIdentity.Length, resourceIdentity.Sha256);
        token.ThrowIfCancellationRequested();
        return new(generation, terrain, new Dictionary<TerrainTextureKey, DecodedTerrainTexture>(), terrainCommands.Length,
            terrainCommands.Count(command => command.Phase == 0), terrainCommands.Count(command => command.Phase == 1), 0, 0,
            mf, rf, mf, rf, retained + actor.CheckedRgbaBytes, elapsed, 1)
        { Area = area, ActorPlan = actor.Plan, ActorTextures = actor.Textures, AreaStepSnapshot = traversal,
            ActorResourceFingerprint = new(actorIdentity.Length, actorIdentity.Sha256), ActorResourceFingerprintAfter = new(actorIdentity.Length, actorIdentity.Sha256), Cave = cave };
    }

    private static AreaLocalLighting ReadLocalLighting(DatArchive artResource, AreaSnapshot snapshot, AreaChart chart, CancellationToken token)
    {
        var lights = StationaryLightReader.Read(artResource, snapshot, token);
        if (lights.Failure != LightingFailure.None) throw new InvalidOperationException("AREA_STATIONARY_LIGHTS_" + lights.Failure);
        Console.WriteLine($"ARC-RENDER-012 load stage=stationary-lights map={snapshot.CatalogueMap} lights={lights.Source!.Lights.Count} frames={lights.Source.Frames.Count}");
        return AreaLocalLighting.Create(snapshot, chart, lights.Source);
    }

    // Bind the whole-core step evidence to this AREA generation. The source check is a
    // consistency check (sources derive from the same Area inputs); the inspector's own
    // payload pins and strict-publication check are the authentication. Visuals never grant movement.
    internal static void VerifyAreaSteps(long generation, AreaSnapshot area, AreaStepSnapshot steps)
    {
        if (steps.EvidenceEpoch != generation || steps.InputIdentity.Kind != AreaStepInputKind.AuthenticatedOriginal ||
            steps.InputIdentity.AreaProfile != area.Profile || steps.AdmittedSectors.Count != 9 ||
            !area.Inputs.All(i => steps.InputIdentity.Sources.Any(s => s.Length == i.Length && s.Sha256 == i.Sha256)) ||
            !steps.IsAdmitted(AreaGridSession.Cell(AreaGridSession.Spawn)))
            throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Publication));
    }

    // Bind the separately authenticated traversal to this AREA generation and
    // input identities. Full exterior visuals never enlarge its proven zone.
    internal static void VerifyTraversal(long generation, AreaInputIdentity module, AreaInputIdentity resource,
        CardinalTraversalSnapshot traversal)
    {
        var sector = traversal.InputIdentity.Sector;
        if (traversal.EvidenceEpoch != generation || traversal.AdmissionPolicy != CardinalTraversalAdmissionPolicy.BoundedAdjacent ||
            traversal.InputIdentity.Kind != CardinalTraversalInputKind.AuthenticatedOriginal ||
            traversal.InputIdentity.OriginalIdentity is not { } original ||
            module.Key != new AreaInputKey(AreaSourceRole.Module) || resource.Key != new AreaInputKey(AreaSourceRole.Resource2) ||
            original.Module.Length != module.Length || original.Module.Sha256 != module.Sha256 ||
            original.Resource.Length != resource.Length || original.Resource.Sha256 != resource.Sha256 ||
            sector.SectorX != 1452 || sector.SectorY != 1290 || sector.PackedId != 86570436012 ||
            sector.OriginX != AreaScenePlan.CameraOriginX || sector.OriginY != AreaScenePlan.CameraOriginY ||
            sector.Width != 64 || sector.Height != 64)
            throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Publication));
    }

    // Complete strict AREA repository phase, shared by ordinary Execute and
    // structural coordinator tests. The caller owns and disposes the Data owner;
    // actor/traversal authentication stays in Execute, never in this test seam.
    // chartFor (ARC-WORLD-009) charts a map other than the exterior from its own strict snapshot.
    internal static AreaLoadResult ReadArea(long generation, AreaDataOwner owner, CancellationToken token, Func<AreaSnapshot, AreaChart>? chartFor = null)
        => Read(generation, owner, token, chartFor, null);

    /// <summary>ARC-RENDER-017: <see cref="ReadArea"/> for a whole interior, with the map plan's limits and the map
    /// repository caps (<see cref="AreaRepositoryPolicy.MaxMapFrames"/>/<see cref="AreaRepositoryPolicy.MaxMapCpuBytes"/>).</summary>
    internal static AreaLoadResult ReadWholeMap(long generation, AreaDataOwner owner, CancellationToken token, Func<AreaSnapshot, AreaChart> chartFor,
        AreaLoaderOptions limits) => Read(generation, owner, token, chartFor, limits ?? throw new ArgumentNullException(nameof(limits)));

    // Self-tests reach ReadArea and CopyFrames by reflection with their exact parameter lists; keep those unchanged.
    private static AreaLoadResult Read(long generation, AreaDataOwner owner, CancellationToken token, Func<AreaSnapshot, AreaChart>? chartFor,
        AreaLoaderOptions? mapLimits)
    {
        var started = Environment.TickCount64; var allocated = GC.GetTotalAllocatedBytes();
        token.ThrowIfCancellationRequested();
        var snapshot = owner.LoadStrict(token);
        Console.WriteLine("ARC-RENDER-007 load stage=strict-snapshot");
        var plan = AreaScenePlan.Create(snapshot, chartFor?.Invoke(snapshot));
        Console.WriteLine($"ARC-RENDER-007 load stage=scene-planned commands={plan.Commands.Count}");
        var walls = AreaWallCheck.Pieces(snapshot, plan);
        var initial = owner.Usage;
        var frames = mapLimits is not null
            ? Copy(owner, snapshot, plan, token, AreaRepositoryPolicy.MaxMapFrames, AreaRepositoryPolicy.MaxMapCpuBytes, null, mapLimits)
            : CopyFrames(owner, snapshot, plan, token);
        var retained = frames.Values.Sum(frame => (long)frame.Rgba.Length);
        Console.WriteLine($"ARC-RENDER-007 load stage=frames-retained frames={frames.Count} bytes={retained}");
        token.ThrowIfCancellationRequested();
        return new(generation, snapshot, plan, frames, initial, owner.Usage, retained,
            GC.GetTotalAllocatedBytes() - allocated, GC.GetTotalMemory(false),
            System.Diagnostics.Process.GetCurrentProcess().WorkingSet64, Environment.TickCount64 - started) { Walls = walls };
    }

    // Internal production stage shared with structural owner-path tests. Tests
    // may lower limits or observe a copied frame, never substitute parsing,
    // strict publication, selected-frame acquisition or copying.
    internal static IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> CopyFrames(AreaDataOwner owner,
        AreaSnapshot snapshot, AreaScenePlan plan, CancellationToken token, int maxFrames = MaxFrames,
        long maxCpuBytes = MaxCpuBytes, Action<int, long>? copied = null)
        => Copy(owner, snapshot, plan, token, maxFrames, maxCpuBytes, copied, null);

    private static IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Copy(AreaDataOwner owner, AreaSnapshot snapshot, AreaScenePlan plan,
        CancellationToken token, int maxFrames, long maxCpuBytes, Action<int, long>? copied, AreaLoaderOptions? limits)
    {
        // A whole map (ARC-RENDER-017) passes its own plan limits and the larger map repository caps.
        var mapCaps = limits is not null;
        if (maxFrames < 0 || maxFrames > (mapCaps ? AreaRepositoryPolicy.MaxMapFrames : MaxFrames) ||
            maxCpuBytes < 0 || maxCpuBytes > (mapCaps ? AreaRepositoryPolicy.MaxMapCpuBytes : MaxCpuBytes))
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        token.ThrowIfCancellationRequested();
        var needed = plan.Frames.Distinct().ToHashSet();
        var selected = snapshot.Visuals.GroupBy(AreaFrameKey.From).Where(group => needed.Contains(group.Key))
            .ToDictionary(group => group.Key, group => group.First());
        if (!needed.SetEquals(selected.Keys)) throw new InvalidOperationException("AREA_FRAME_DESCRIPTOR_REQUIRED");
        var initial = owner.Usage;
        limits ??= new AreaLoaderOptions();
        var predictedBytes = selected.Values.Sum(visual => checked((long)visual.Width * visual.Height * 4));
        // Pre-admit the cumulative Data domains before the frame batch.
        if (selected.Count > maxFrames || predictedBytes > maxCpuBytes ||
            checked(initial.FrameRequests + selected.Count) > limits.MaxFrameRequests ||
            checked(initial.FrameRgba + predictedBytes) > limits.MaxFrameRgbaBytes ||
            checked(initial.FrameStored + selected.Values.Sum(v => (long)v.Entry.StoredBytes)) > limits.MaxFrameStoredBytes ||
            checked(initial.FrameDecoded + selected.Values.Sum(v => (long)v.Entry.DecodedBytes)) > limits.MaxFrameDecodedBytes ||
            checked(initial.FrameEncoded + selected.Values.Sum(v => (long)v.EncodedBytes)) > limits.MaxFrameEncodedBytes)
            throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Frame));
        var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame>(); long retained = 0;
        // One batch: each source is authenticated before its first frame and again at
        // Complete; nothing is returned unless Complete succeeds.
        using var batch = owner.BeginFrameBatch(snapshot, token);
        foreach (var pair in selected)
        {
            token.ThrowIfCancellationRequested();
            retained = checked(retained + (long)pair.Value.Width * pair.Value.Height * 4);
            if (retained > maxCpuBytes || frames.Count >= maxFrames) throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Frame));
            var rgba = batch.CopySelectedFrame(pair.Value.Key, token);
            if (rgba.Length != checked(pair.Value.Width * pair.Value.Height * 4))
                throw new AreaException(new(AreaErrorCode.InputChangedDuringRead, AreaStage.Frame));
            frames.Add(pair.Key, new(pair.Value.Width, pair.Value.Height, rgba));
            copied?.Invoke(frames.Count, retained);
        }
        token.ThrowIfCancellationRequested();
        batch.Complete(token);
        return new ReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>(frames);
    }

    internal static void VerifyRequested(AreaInputIdentity identity, string? requested)
    { if (requested is not null && requested != identity.Sha256) throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Input)); }
    internal static void VerifyFiles(IReadOnlyList<(string Path, AreaInputIdentity Identity)> files, CancellationToken token)
    {
        var failures = new Exception?[files.Count];
        Parallel.For(0, files.Count, index =>
        {
            try { VerifyFile(files[index].Path, files[index].Identity, token); }
            catch (Exception error) { failures[index] = error; }
        });
        foreach (var failure in failures)
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void VerifyFile(string path, AreaInputIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != identity.Length || Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != identity.Sha256)
            throw new AreaException(new(AreaErrorCode.InputChangedDuringRead, AreaStage.Input));
        token.ThrowIfCancellationRequested();
    }
}
