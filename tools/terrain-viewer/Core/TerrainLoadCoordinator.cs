using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

public sealed record InputFingerprint(long Size, string Sha256);
public sealed record DecodedTerrainTexture(TerrainTextureKey Key, int Width, int Height, byte[] Rgba);
public sealed record DecodedStaticTexture(StaticSceneryTextureKey Key, int Width, int Height, byte[] Rgba);
public sealed record TerrainLoadResult(long Generation, TerrainScenePlan Plan, IReadOnlyDictionary<TerrainTextureKey, DecodedTerrainTexture> Textures,
    int ResolvedCount, int TileCount, int FacadeCount, int NormalizationRecoveredCount, int NormalizationLookupCount,
    InputFingerprint ModuleFingerprint, InputFingerprint ResourceFingerprint, InputFingerprint ModuleFingerprintAfter,
    InputFingerprint ResourceFingerprintAfter, long CheckedRgbaBytes, long ElapsedMilliseconds, int MaximumObservedConcurrency)
{
    public StaticSceneryScenePlan? StaticPlan { get; init; }
    public DiagnosticScenePlan? DiagnosticPlan { get; init; }
    public IReadOnlyDictionary<StaticSceneryTextureKey, DecodedStaticTexture> StaticTextures { get; init; } = new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>();
    public int StaticCensusCount { get; init; }
    public int StaticOutsideScopeCount { get; init; }
}

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
    private sealed class ProductionTerrainLoadOperation : ITerrainLoadOperation
    {
        private DatArchive? _module;
        private DatArchive? _resource;
        private int _disposed;

        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
        {
            var started = Environment.TickCount64;
            var progress = TerrainViewerProgress.Empty;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moduleBefore = Fingerprint(arguments.ModulePath);
                cancellationToken.ThrowIfCancellationRequested();
                var resourceBefore = Fingerprint(arguments.ResourcePath);
                cancellationToken.ThrowIfCancellationRequested();
                Verify(moduleBefore, CrashSiteProfile.ModuleSize, CrashSiteProfile.ModuleSha256, arguments.ExpectedModuleSha256);
                Verify(resourceBefore, CrashSiteProfile.ResourceSize, CrashSiteProfile.ResourceSha256, arguments.ExpectedResourceSha256);
                TerrainLoadReport report;
                Dictionary<TerrainTextureKey, DecodedTerrainTexture> textures;
                StaticSceneryScenePlan? staticPlan = null;
                var staticTextures = new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>();
                var staticCensus = 0;
                var staticOutsideScope = 0;
                _module = DatArchive.Open(arguments.ModulePath, BoundedDatOptions());
                cancellationToken.ThrowIfCancellationRequested();
                _resource = DatArchive.Open(arguments.ResourcePath, BoundedDatOptions());
                cancellationToken.ThrowIfCancellationRequested();
                var source = new TerrainResourceSource(_resource, arguments.SourceId, CrashSiteProfile.TileMesEntry, CrashSiteProfile.FacadeMesEntry,
                    TerrainPathProfile.CeNonWindowsMacOs);
                report = TerrainLoader.Inspect(_module, CrashSiteProfile.CreateSelector(), source);
                cancellationToken.ThrowIfCancellationRequested();
                if (!report.IsComplete || report.Outcomes.Count != CrashSiteProfile.CellCount)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, report.ResolvedRecords.Count, report.Issues.Count, TerrainViewerFailureStage.TerrainCensus, progress));
                var tileCount = report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Tile);
                var facadeCount = report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Facade);
                var recoveredCount = report.ResolvedRecords.Count(record => record.WasNormalizationRecovered);
                var lookupCount = report.ResolvedRecords.Where(record => record.WasNormalizationRecovered).Select(record => record.LookupId).Distinct().Count();
                if (tileCount != CrashSiteProfile.TileCount || facadeCount != CrashSiteProfile.FacadeCount
                    || recoveredCount != CrashSiteProfile.NormalizationRecoveredCount || lookupCount != CrashSiteProfile.NormalizationLookupCount)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, report.ResolvedRecords.Count, report.Issues.Count, TerrainViewerFailureStage.TerrainCensus, progress));
                progress = new TerrainViewerProgress(report.ResolvedRecords.Count, tileCount, facadeCount);
                var sector = TerrainLoader.Load(_module, CrashSiteProfile.CreateSelector(), source);
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
                    checkedBytes = TerrainCachePolicy.AddCheckedFrame(checkedBytes, textures.Count, frame.Width, frame.Height);
                    textures.Add(key, new DecodedTerrainTexture(key, frame.Width, frame.Height, frame.Rgba.ToArray()));
                }
                if (arguments.Mode == TerrainViewerMode.CombinedStaticDiagnostic)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var validation = StaticObjectLoader.ValidateOriginalData(arguments.ModulePath, arguments.ResourcePath, arguments.PrototypePath!);
                        cancellationToken.ThrowIfCancellationRequested();
                        staticCensus = validation.Inspection.TerminalRecordCount;
                        staticOutsideScope = validation.Inspection.ResourceOutsideScopeCount;
                        progress = progress.WithStaticCensus(staticCensus, validation.Inspection.ResourceResolvedCount, staticOutsideScope);
                        try { staticPlan = StaticSceneryScenePlan.FromAuthenticatedInspection(validation.Inspection); }
                        catch (TerrainViewerException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticPlan, Stage: TerrainViewerFailureStage.Plan, Progress: progress)); }
                        foreach (var record in validation.Inspection.Records.Where(item => item.SourceIndex is >= 0 and <= 4))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var command = staticPlan.Commands.Single(item => item.SourceIndex == record.SourceIndex);
                            if (staticTextures.ContainsKey(command.TextureKey)) continue;
                            ArtFrame frame;
                            try { frame = StaticObjectLoader.DecodeSelectedFrame(_resource, record, new StaticObjectLoaderOptions().ArtOptions); }
                            catch (StaticObjectException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticDecode, Stage: TerrainViewerFailureStage.StaticDecode, Progress: progress)); }
                            cancellationToken.ThrowIfCancellationRequested();
                            checkedBytes = TerrainCachePolicy.AddCheckedFrame(checkedBytes, textures.Count + staticTextures.Count, frame.Width, frame.Height);
                            staticTextures.Add(command.TextureKey, new DecodedStaticTexture(command.TextureKey, frame.Width, frame.Height, frame.Rgba.ToArray()));
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
                cancellationToken.ThrowIfCancellationRequested();
                var moduleAfter = Fingerprint(arguments.ModulePath);
                cancellationToken.ThrowIfCancellationRequested();
                var resourceAfter = Fingerprint(arguments.ResourcePath);
                if (moduleBefore != moduleAfter || resourceBefore != resourceAfter)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.FingerprintMismatch,
                        Stage: TerrainViewerFailureStage.Fingerprint, Progress: progress));
                return new TerrainLoadResult(generation, plan, textures, report.ResolvedRecords.Count,
                    tileCount, facadeCount, recoveredCount, lookupCount, moduleBefore, resourceBefore, moduleAfter,
                    resourceAfter, checkedBytes, Environment.TickCount64 - started, 0)
                {
                    StaticPlan = staticPlan,
                    DiagnosticPlan = DiagnosticScenePlan.Create(plan, staticPlan, progress),
                    StaticTextures = staticTextures,
                    StaticCensusCount = staticCensus,
                    StaticOutsideScopeCount = staticOutsideScope
                };
            }
            catch (TerrainViewerException) { throw; }
            // Cancellation is lifecycle control flow, not a diagnostic failure.
            // The app must never publish a cancellation HUD over a new generation
            // or an exiting tree.
            catch (OperationCanceledException) { throw; }
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
            _resource?.Dispose();
            _module?.Dispose();
        }
    }

    private static DatReaderOptions BoundedDatOptions() => new() { MaxArchiveBytes = uint.MaxValue, MaxStoredEntryBytes = 1024 * 1024, MaxDecodedEntryBytes = 1024 * 1024 };
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
