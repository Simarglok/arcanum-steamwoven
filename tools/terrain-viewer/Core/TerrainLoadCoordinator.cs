using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

namespace Arcanum.TerrainViewer.Core;

public sealed record InputFingerprint(long Size, string Sha256);
public sealed record DecodedTerrainTexture(TerrainTextureKey Key, int Width, int Height, byte[] Rgba);
public sealed record DecodedStaticTexture(StaticSceneryTextureKey Key, int Width, int Height, byte[] Rgba)
{
    /// <summary>ARC-RENDER-014: the portable HD question of this frame, or null (draws Legacy as <c>FrameMissing</c>).</summary>
    public Arcanum.Data.Hd.HdSpriteQuery? HdQuery { get; init; }
}
public sealed record TerrainLoadResult(long Generation, TerrainScenePlan Plan, IReadOnlyDictionary<TerrainTextureKey, DecodedTerrainTexture> Textures,
    int ResolvedCount, int TileCount, int FacadeCount, int NormalizationRecoveredCount, int NormalizationLookupCount,
    InputFingerprint ModuleFingerprint, InputFingerprint ResourceFingerprint, InputFingerprint ModuleFingerprintAfter,
    InputFingerprint ResourceFingerprintAfter, long CheckedRgbaBytes, long ElapsedMilliseconds, int MaximumObservedConcurrency)
{
    public StaticSceneryScenePlan? StaticPlan { get; init; }
    public DiagnosticScenePlan? DiagnosticPlan { get; init; }
    public IReadOnlyDictionary<StaticSceneryTextureKey, DecodedStaticTexture> StaticTextures { get; init; } = new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>();
    public ActorDiagnosticScenePlan? ActorPlan { get; init; }
    public IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> ActorTextures { get; init; } = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>();
    public InputFingerprint? ActorResourceFingerprint { get; init; }
    public InputFingerprint? ActorResourceFingerprintAfter { get; init; }
    public InputFingerprint? ActorPrototypeFingerprint { get; init; }
    public InputFingerprint? ActorPrototypeFingerprintAfter { get; init; }
    public int StaticCensusCount { get; init; }
    public int StaticOutsideScopeCount { get; init; }
    public CardinalTraversalSnapshot? TraversalSnapshot { get; init; }
    /// <summary>Whole-core T0 step evidence of a crash-site-area load; the bounded cardinal snapshot stays WORLD-only.</summary>
    public AreaStepSnapshot? AreaStepSnapshot { get; init; }
    public AreaLoadResult? Area { get; init; }
    /// <summary>ARC-WORLD-009: the cave map loaded with the area when its object file is given.</summary>
    public AreaCaveLoad? Cave { get; init; }
    /// <summary>ARC-WORLD-027: the pre-made hero the session starts as (ready), when <c>--hero</c> is given.</summary>
    public AreaHeroLoad? Hero { get; init; }
    /// <summary>ARC-WORLD-031 (map mode): the map's transitions (ARC-DATA-025) and the maps map mode can open, by runtime
    /// number; null with <see cref="MapTransitionsFailure"/> when the map list does not list the map.</summary>
    public Arcanum.Data.Maps.MapTransitionSet? MapTransitions { get; init; }
    public IReadOnlyDictionary<int, AreaMapDestination>? MapDestinations { get; init; }
    public string? MapTransitionsFailure { get; init; }
    /// <summary>ARC-WORLD-081 (map mode, a window): the sector scripts of the window's template sectors (ledger PG9); empty for a whole map.</summary>
    public IReadOnlyList<Arcanum.Data.Maps.MapPlacedScript> MapTemplateScripts { get; init; } = [];
    /// <summary>ARC-RENDER-024 (map mode): the centre sector of a window load (a map too large to load whole), else null.</summary>
    public Arcanum.Data.Area.AreaSectorCoordinate? AreaMapWindowCentre { get; init; }
    /// <summary>ARC-RENDER-034: a window's terrain overview beyond its core (null for a whole map, or when the terrain grid or
    /// types could not be read: <see cref="AreaOverviewFailure"/> says why).</summary>
    public AreaTerrainOverview? AreaOverview { get; init; }
    public string? AreaOverviewFailure { get; init; }
    /// <summary>ARC-WORLD-057 (map mode): the module's start clock when the map is drawn unlit, so its game session still
    /// starts; null when the map is lit (its lighting carries the clock) or the clock cannot be read.</summary>
    public Arcanum.Data.Lighting.StartupClockDeclaration? UnlitSessionStartup { get; init; }
    /// <summary>Why <see cref="UnlitSessionStartup"/> is null on an unlit map (null when the map is lit).</summary>
    public Arcanum.Data.Lighting.LightingFailure? UnlitSessionStartupFailure { get; init; }
}

/// <summary>The cave's strict snapshot, scene, frames and step evidence, bound to the same generation.</summary>
public sealed record AreaCaveLoad(AreaLoadResult Area, AreaStepSnapshot Steps, AreaChart Chart);

/// <summary>
/// One caller-owned archive/decode operation. A fresh instance owns precisely
/// one load's archives and is disposed by <see cref="TerrainLoadCoordinator"/>
/// after either success or failure, before the serialized gate is released.
/// </summary>
public interface ITerrainLoadOperation : IDisposable
{
    TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken);
}

public interface ITerrainLoadOperationFactory
{
    ITerrainLoadOperation Create();
}

public sealed class TerrainLoadCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ITerrainLoadOperationFactory _operationFactory;
    private readonly Action? _afterPermitAcquired;
    private readonly object _lifetimeGate = new();
    private TaskCompletionSource _registeredLoadsDrained = CompletedDrain();
    private long _generation;
    private int _activeOperations;
    private int _registeredLoads;
    private int _maximumObservedConcurrency;
    private bool _disposed;

    public TerrainLoadCoordinator(ITerrainLoadOperationFactory? operationFactory = null, Action? afterPermitAcquired = null)
    {
        _operationFactory = operationFactory ?? new ProductionTerrainLoadOperationFactory();
        _afterPermitAcquired = afterPermitAcquired;
    }

    public long BeginGeneration() => Interlocked.Increment(ref _generation);
    public int MaximumObservedConcurrency => Volatile.Read(ref _maximumObservedConcurrency);
    public bool IsCurrentGeneration(long generation) => Volatile.Read(ref _generation) == generation;

    /// <summary>Runs a publication action only for the current monotonic generation.</summary>
    public bool TryPublish(long generation, Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (_disposed || !IsCurrentGeneration(generation)) return false;
        publish();
        return true;
    }

    public async Task<TerrainLoadResult> LoadAsync(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        RegisterLoad();
        var acquired = false;
        ITerrainLoadOperation? operation = null;
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
            var token = linkedCancellation.Token;
            await _gate.WaitAsync(token).ConfigureAwait(false);
            acquired = true;
            // Account for the acquired permit before observing cancellation.
            // The finally below releases every acquired permit and must therefore
            // decrement only a count that was definitely incremented.  Doing the
            // cancellation check first left a narrow acquired-but-unregistered
            // window that could drive the count negative during coordinator exit.
            var active = Interlocked.Increment(ref _activeOperations);
            UpdateMaximum(active);
            _afterPermitAcquired?.Invoke();
            token.ThrowIfCancellationRequested();
            operation = _operationFactory.Create();
            // The worker task always runs so that an operation which was created
            // reaches its disposal finally before the serialization gate opens.
            // Execute, rather than Task.Run, observes caller cancellation.
            var result = await Task.Run(() => operation.Execute(generation, arguments, token), CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result with { MaximumObservedConcurrency = MaximumObservedConcurrency };
        }
        finally
        {
            try { operation?.Dispose(); }
            finally
            {
                if (acquired)
                {
                    Interlocked.Decrement(ref _activeOperations);
                    _gate.Release();
                }
                DeregisterLoad();
            }
        }
    }

    private void RegisterLoad()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registeredLoads++ == 0) _registeredLoadsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void DeregisterLoad()
    {
        lock (_lifetimeGate)
        {
            if (--_registeredLoads == 0) _registeredLoadsDrained.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedDrain()
    {
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        drain.SetResult();
        return drain;
    }

    private sealed class ProductionTerrainLoadOperationFactory : ITerrainLoadOperationFactory
    {
        public ITerrainLoadOperation Create() => new ProductionTerrainLoadOperation();
    }

    /// <summary>
    /// Production delegates directly to the existing DatArchive/TerrainLoader
    /// APIs. The archive fields deliberately outlive Execute until the
    /// coordinator's per-load finally invokes Dispose.
    /// </summary>
    internal sealed class ProductionTerrainLoadOperation : ITerrainLoadOperation
    {
        private readonly TerrainLoadAdmission _admission;
        private readonly TerrainLoadBudget _budget;
        private readonly Action<TerrainLoadCheckpoint>? _checkpoint;
        internal ProductionTerrainLoadOperation(TerrainLoadAdmission? admission = null, TerrainLoadBudget? budget = null,
            Action<TerrainLoadCheckpoint>? checkpoint = null)
        {
            _admission = admission ?? new TerrainLoadAdmission();
            _budget = budget ?? TerrainLoadBudget.Default;
            _budget.Validate();
            _checkpoint = checkpoint;
        }
        private DatArchive? _module;
        private DatArchive? _resource;
        private ActorDiagnosticSourceOperation? _actorOperation;
        private int _disposed;

        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
        {
            if (arguments.Mode == TerrainViewerMode.CrashSiteArea)
                return AreaLoadOperation.Execute(generation, arguments, cancellationToken);
            var started = Environment.TickCount64;
            var progress = TerrainViewerProgress.Empty;
            var actorProfile = _admission.ActorProfile(arguments.Mode);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moduleBefore = Fingerprint(arguments.ModulePath);
                cancellationToken.ThrowIfCancellationRequested();
                var resourceBefore = Fingerprint(arguments.ResourcePath);
                cancellationToken.ThrowIfCancellationRequested();
                Verify(moduleBefore, _admission.Module.Size, _admission.Module.Sha256, arguments.ExpectedModuleSha256);
                Verify(resourceBefore, _admission.Resource.Size, _admission.Resource.Sha256, arguments.ExpectedResourceSha256);
                InputFingerprint? actorResourceBefore = null;
                InputFingerprint? prototypeBefore = null;
                if (arguments.Mode is TerrainViewerMode.ActorDiagnostic or TerrainViewerMode.WorldDiagnostic)
                {
                    actorResourceBefore = FingerprintActorFile(arguments.ActorResourcePath!);
                    cancellationToken.ThrowIfCancellationRequested();
                    prototypeBefore = FingerprintActorPrototypeRoot(arguments.PrototypePath!);
                    ActorDiagnosticSource.VerifyIdentity(actorResourceBefore, _admission.ActorResource.Size, _admission.ActorResource.Sha256, arguments.ExpectedActorResourceSha256);
                    ActorDiagnosticSource.VerifyIdentity(prototypeBefore, _admission.Prototypes.Size, _admission.Prototypes.Sha256, null);
                }
                TerrainLoadReport report;
                Dictionary<TerrainTextureKey, DecodedTerrainTexture> textures;
                StaticSceneryScenePlan? staticPlan = null;
                var staticTextures = new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>();
                ActorDiagnosticScenePlan? actorPlan = null;
                var actorTextures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>();
                var staticCensus = 0;
                var staticOutsideScope = 0;
                CardinalTraversalSnapshot? traversal = null;
                _module = _admission.OpenArchive(arguments.ModulePath);
                cancellationToken.ThrowIfCancellationRequested();
                _resource = _admission.OpenArchive(arguments.ResourcePath);
                cancellationToken.ThrowIfCancellationRequested();
                var source = new TerrainResourceSource(_resource, arguments.SourceId, CrashSiteProfile.TileMesEntry, CrashSiteProfile.FacadeMesEntry,
                    TerrainPathProfile.NonWindowsMacOs);
                report = TerrainLoader.Inspect(_module, _admission.Selector, source);
                cancellationToken.ThrowIfCancellationRequested();
                if (!report.IsComplete || report.Outcomes.Count != CrashSiteProfile.CellCount)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, report.ResolvedRecords.Count, report.Issues.Count, TerrainViewerFailureStage.TerrainCensus, progress));
                var tileCount = report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Tile);
                var facadeCount = report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Facade);
                var recoveredCount = report.ResolvedRecords.Count(record => record.WasNormalizationRecovered);
                var lookupCount = report.ResolvedRecords.Where(record => record.WasNormalizationRecovered).Select(record => record.LookupId).Distinct().Count();
                if (!_admission.AcceptCensus(tileCount, facadeCount, recoveredCount, lookupCount))
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, report.ResolvedRecords.Count, report.Issues.Count, TerrainViewerFailureStage.TerrainCensus, progress));
                progress = new TerrainViewerProgress(report.ResolvedRecords.Count, tileCount, facadeCount);
                var sector = TerrainLoader.Load(_module, _admission.Selector, source);
                cancellationToken.ThrowIfCancellationRequested();
                var plan = TerrainScenePlan.Create(sector);
                textures = new Dictionary<TerrainTextureKey, DecodedTerrainTexture>();
                var checkedBytes = 0L;
                foreach (var record in sector.Records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = TerrainTextureKey.FromRecord(record);
                    if (textures.ContainsKey(key)) continue;
                    var frame = TerrainLoader.DecodeSelectedFrame(_resource, record, new TerrainLoaderOptions().ArtOptions);
                    checkedBytes = _budget.Add(checkedBytes, textures.Count, textures.Count, _budget.Terrain, frame.Width, frame.Height, arguments.Mode, actorProfile);
                    textures.Add(key, new DecodedTerrainTexture(key, frame.Width, frame.Height, frame.Rgba.ToArray()));
                    _checkpoint?.Invoke(new("terrain", textures.Count, textures.Count, checkedBytes));
                }
                if (arguments.Mode is TerrainViewerMode.CombinedStaticDiagnostic or TerrainViewerMode.WorldDiagnostic)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var inspection = _admission.InspectStatics(arguments, _module, _resource);
                        cancellationToken.ThrowIfCancellationRequested();
                        staticCensus = inspection.TerminalRecordCount;
                        staticOutsideScope = inspection.ResourceOutsideScopeCount;
                        progress = progress.WithStaticCensus(staticCensus, inspection.ResourceResolvedCount, staticOutsideScope);
                        try { staticPlan = StaticSceneryScenePlan.FromAuthenticatedInspection(inspection); }
                        catch (TerrainViewerException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticPlan, Stage: TerrainViewerFailureStage.Plan, Progress: progress)); }
                        foreach (var record in inspection.Records.Where(item => item.SourceIndex is >= 0 and <= 4))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var command = staticPlan.Commands.Single(item => item.SourceIndex == record.SourceIndex);
                            if (staticTextures.ContainsKey(command.TextureKey)) continue;
                            ArtFrame frame;
                            try { frame = StaticObjectLoader.DecodeSelectedFrame(_resource, record, new StaticObjectLoaderOptions().ArtOptions); }
                            catch (StaticObjectException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticDecode, Stage: TerrainViewerFailureStage.StaticDecode, Progress: progress)); }
                            cancellationToken.ThrowIfCancellationRequested();
                            checkedBytes = _budget.Add(checkedBytes, textures.Count + staticTextures.Count, staticTextures.Count, _budget.Statics, frame.Width, frame.Height, arguments.Mode, actorProfile);
                            var rgba = frame.Rgba.ToArray();
                            staticTextures.Add(command.TextureKey, new DecodedStaticTexture(command.TextureKey, frame.Width, frame.Height, rgba)
                            { HdQuery = StaticHdQuery(_resource, record, frame, rgba) });
                            _checkpoint?.Invoke(new("static", staticTextures.Count, textures.Count + staticTextures.Count, checkedBytes));
                        }
                        if (staticTextures.Count > TerrainCachePolicy.MaxStaticDistinctKeys)
                            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit, Stage: TerrainViewerFailureStage.StaticDecode, Progress: progress));
                    }
                    catch (TerrainViewerException) { throw; }
                    catch (StaticObjectException exception)
                    {
                        var stage = exception.Stage switch
                        {
                            StaticObjectFailureStage.FingerprintInput => TerrainViewerFailureStage.StaticFingerprint,
                            StaticObjectFailureStage.Mapping or StaticObjectFailureStage.Art => TerrainViewerFailureStage.StaticResource,
                            _ => TerrainViewerFailureStage.StaticCensus
                        };
                        throw new TerrainViewerException(new TerrainViewerError(exception.Stage == StaticObjectFailureStage.FingerprintInput ? TerrainViewerErrorCode.StaticFingerprint : TerrainViewerErrorCode.StaticResource, Stage: stage, Progress: progress));
                    }
                }
                if (arguments.Mode is TerrainViewerMode.ActorDiagnostic or TerrainViewerMode.WorldDiagnostic)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // This archive has a deliberately separate authenticated role:
                        // terrain resolution never consults it, and actor ordinal lookup
                        // never consults the terrain resource.
                        _actorOperation = new ActorDiagnosticSourceOperation(() => _admission.OpenArchive(arguments.ActorResourcePath!));
                        var actor = _actorOperation.ExecuteWithBudget(_module, arguments.ActorInput!, textures.Count + staticTextures.Count, checkedBytes,
                            progress, cancellationToken, _budget, count => _checkpoint?.Invoke(count), arguments.Mode,
                            actorProfile, _admission.AuthenticateOriginalActorMetadata);
                        if (actor.Textures.Count != actorProfile.DistinctKeys)
                            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorMetadata, Stage: TerrainViewerFailureStage.ActorMetadata, Progress: progress));
                        checkedBytes = actor.CheckedRgbaBytes;
                        foreach (var item in actor.Textures) actorTextures.Add(item.Key, item.Value);
                        actorPlan = actor.Plan;
                    }
                    catch (TerrainViewerException exception)
                    {
                        throw new TerrainViewerException(exception.Error with { Progress = exception.Error.Progress ?? progress });
                    }

                }
                cancellationToken.ThrowIfCancellationRequested();
                if (arguments.Mode == TerrainViewerMode.WorldDiagnostic)
                {
                    traversal = _admission.InspectTraversal(arguments, _module, _resource, generation);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (traversal.EvidenceEpoch != generation || !_admission.AcceptTraversalIdentity(traversal, moduleBefore, resourceBefore))
                        throw new TerrainViewerException(new(TerrainViewerErrorCode.FingerprintMismatch, Stage: TerrainViewerFailureStage.Fingerprint));
                }
                _checkpoint?.Invoke(new("complete", actorTextures.Count, textures.Count + staticTextures.Count + actorTextures.Count, checkedBytes));
                cancellationToken.ThrowIfCancellationRequested();
                var moduleAfter = Fingerprint(arguments.ModulePath);
                cancellationToken.ThrowIfCancellationRequested();
                var resourceAfter = Fingerprint(arguments.ResourcePath);
                InputFingerprint? actorResourceAfter = null;
                InputFingerprint? prototypeAfter = null;
                if (arguments.Mode is TerrainViewerMode.ActorDiagnostic or TerrainViewerMode.WorldDiagnostic)
                {
                    actorResourceAfter = FingerprintActorFile(arguments.ActorResourcePath!);
                    prototypeAfter = FingerprintActorPrototypeRoot(arguments.PrototypePath!);
                }
                if (actorResourceBefore is not null && (actorResourceBefore != actorResourceAfter || prototypeBefore != prototypeAfter))
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint,
                        Stage: TerrainViewerFailureStage.ActorFingerprint, Progress: progress));
                if (moduleBefore != moduleAfter || resourceBefore != resourceAfter)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.FingerprintMismatch,
                        Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress));
                return new TerrainLoadResult(generation, plan, textures, report.ResolvedRecords.Count,
                    tileCount, facadeCount, recoveredCount, lookupCount, moduleBefore, resourceBefore, moduleAfter,
                    resourceAfter, checkedBytes, Environment.TickCount64 - started, 0)
                {
                    StaticPlan = staticPlan,
                    ActorPlan = actorPlan,
                    DiagnosticPlan = arguments.Mode == TerrainViewerMode.WorldDiagnostic
                        ? DiagnosticScenePlan.CreateWorldDiagnostic(plan, staticPlan!, actorPlan!, DiagnosticWorldBounds.Create(plan, staticPlan!, actorPlan!), progress)
                        : DiagnosticScenePlan.Create(plan, staticPlan, actorPlan, progress),
                    StaticTextures = staticTextures,
                    ActorTextures = actorTextures,
                    ActorResourceFingerprint = actorResourceBefore,
                    ActorResourceFingerprintAfter = actorResourceAfter,
                    ActorPrototypeFingerprint = prototypeBefore,
                    ActorPrototypeFingerprintAfter = prototypeAfter,
                    StaticCensusCount = staticCensus,
                    StaticOutsideScopeCount = staticOutsideScope,
                    TraversalSnapshot = traversal
                };
            }
            catch (TerrainViewerException) { throw; }
            // Cancellation is lifecycle control flow, not a diagnostic failure.
            // The app must never publish a cancellation HUD over a new generation
            // or an exiting tree.
            catch (OperationCanceledException) { throw; }
            catch (CardinalTraversalInvocationException exception)
            {
                throw new TerrainViewerException(new(exception.Code == CardinalTraversalInvocationCode.LimitExceeded
                    ? TerrainViewerErrorCode.ResourceLimit : TerrainViewerErrorCode.FingerprintMismatch,
                    Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress));
            }
            catch (FileNotFoundException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable, Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress)); }
            catch (DirectoryNotFoundException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable, Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress)); }
            catch (UnauthorizedAccessException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable, Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress)); }
            catch (TerrainIncompleteException exception) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, exception.Report.ResolvedRecords.Count, exception.Report.Issues.Count, TerrainViewerFailureStage.TerrainCensus, progress)); }
            catch (TerrainLimitExceededException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit, Stage: TerrainViewerFailureStage.TerrainDecode, Progress: progress)); }
            catch (ArtLimitExceededException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit, Stage: TerrainViewerFailureStage.TerrainDecode, Progress: progress)); }
            catch (TerrainException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InvalidSector, Stage: TerrainViewerFailureStage.TerrainDecode, Progress: progress)); }
            catch (ArtException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.DecodeFailure, Stage: TerrainViewerFailureStage.TerrainDecode, Progress: progress)); }
            catch (IOException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable, Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress)); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _actorOperation?.Dispose();
            _resource?.Dispose();
            _module?.Dispose();
        }
    }


    // ARC-RENDER-014: the static's ART identity, read from the same selected archive entry the
    // frame was decoded from (matched by ordinal); null when it cannot be read again.
    private static Arcanum.Data.Hd.HdSpriteQuery? StaticHdQuery(DatArchive resource, StaticObjectRecord record, ArtFrame frame, byte[] rgba)
    {
        if (record.ArtMetadata is not { } metadata) return null;
        try
        {
            var entries = resource.Entries.Where(entry => entry.Ordinal == metadata.ResourceIdentity.DatOrdinal).ToArray();
            if (entries.Length != 1) return null;
            var art = HdFrameQueries.ArtIdentity(resource.ReadEntry(entries[0]), entries[0].RawNameBytes);
            return HdFrameQueries.FromArt(art.PayloadSha256, art.ViewLength, art.NameSha256, frame.GroupIndex, frame.FrameIndex,
                frame.EffectivePaletteSlot, frame.Width, frame.Height, rgba);
        }
        catch (Exception error) when (error is DatException or IOException) { return null; }
    }

    private static InputFingerprint Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }
    private static void Verify(InputFingerprint actual, long expectedSize, string expectedSha256, string? requestedSha256)
    {
        if (actual.Size != expectedSize || !string.Equals(actual.Sha256, expectedSha256, StringComparison.Ordinal)
            || (requestedSha256 is not null && !string.Equals(actual.Sha256, requestedSha256, StringComparison.Ordinal)))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.FingerprintMismatch, Stage: TerrainViewerFailureStage.Fingerprint));
    }

    private static InputFingerprint FingerprintActorFile(string path)
    {
        try { return Fingerprint(path); }
        catch (FileNotFoundException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint)); }
        catch (DirectoryNotFoundException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint)); }
        catch (UnauthorizedAccessException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint)); }
    }
    private static InputFingerprint FingerprintActorPrototypeRoot(string root)
    {
        try
        {
            if (!Directory.Exists(root)) throw new IOException();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            Span<byte> leafLength = stackalloc byte[4];
            Span<byte> selectorBytes = stackalloc byte[4];
            Span<byte> length = stackalloc byte[8];
            foreach (var path in Directory.EnumerateFiles(root, "*.pro", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > 1024 * 1024) throw new IOException();
                var leaf = Encoding.UTF8.GetBytes(Path.GetFileName(path));
                BinaryPrimitives.WriteInt32LittleEndian(leafLength, leaf.Length);
                hash.AppendData(leafLength); hash.AppendData(leaf);
                var selector = CanonicalPrototypeSelector(Path.GetFileName(path));
                hash.AppendData([selector is null ? (byte)0 : (byte)1]);
                if (selector is int aid)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(selectorBytes, aid);
                    hash.AppendData(selectorBytes);
                }
                var fingerprint = Fingerprint(path);
                total = checked(total + fingerprint.Size);
                if (total > 256L * 1024 * 1024) throw new IOException();
                BinaryPrimitives.WriteInt64LittleEndian(length, fingerprint.Size);
                hash.AppendData(length); hash.AppendData(Convert.FromHexString(fingerprint.Sha256));
            }
            return new InputFingerprint(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (IOException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint)); }
        catch (UnauthorizedAccessException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint)); }
        catch (OverflowException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint)); }
    }
    private static int? CanonicalPrototypeSelector(string leaf)
    {
        if (leaf.Length < 10 || leaf[6] != ' ' || leaf[7] != '-' || leaf[8] != ' ' || !leaf[..6].All(char.IsAsciiDigit)) return null;
        return int.TryParse(leaf.AsSpan(0, 6), out var aid) ? aid : null;
    }
    private void UpdateMaximum(int active)
    {
        int observed;
        while (active > (observed = Volatile.Read(ref _maximumObservedConcurrency)) && Interlocked.CompareExchange(ref _maximumObservedConcurrency, active, observed) != observed) { }
    }
    public void Dispose()
    {
        Task drain;
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _generation);
            _lifetimeCancellation.Cancel();
            drain = _registeredLoadsDrained.Task;
        }
        // Operations run on the coordinator worker pool and never capture
        // Godot's synchronization context, so this drains their disposal and
        // gate-release finally before the semaphore itself is disposed.
        drain.GetAwaiter().GetResult();
        _gate.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
