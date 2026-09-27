using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

public sealed record InputFingerprint(long Size, string Sha256);
public sealed record DecodedTerrainTexture(TerrainTextureKey Key, int Width, int Height, byte[] Rgba);
public sealed record TerrainLoadResult(long Generation, TerrainScenePlan Plan, IReadOnlyDictionary<TerrainTextureKey, DecodedTerrainTexture> Textures,
    int ResolvedCount, int TileCount, int FacadeCount, int NormalizationRecoveredCount, int NormalizationLookupCount,
    InputFingerprint ModuleFingerprint, InputFingerprint ResourceFingerprint, long ElapsedMilliseconds, int MaximumObservedConcurrency);

public sealed class TerrainLoadCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _generation;
    private int _activeOperations;
    private int _maximumObservedConcurrency;
    private bool _disposed;

    public long BeginGeneration() => Interlocked.Increment(ref _generation);
    public int MaximumObservedConcurrency => Volatile.Read(ref _maximumObservedConcurrency);

    public async Task<TerrainLoadResult> LoadAsync(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(arguments);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var active = Interlocked.Increment(ref _activeOperations);
        UpdateMaximum(active);
        try
        {
            return await Task.Run(() => LoadCore(generation, arguments), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeOperations);
            _gate.Release();
        }
    }

    private static TerrainLoadResult LoadCore(long generation, TerrainViewerArguments arguments)
    {
        var started = Environment.TickCount64;
        try
        {
            var moduleBefore = Fingerprint(arguments.ModulePath);
            var resourceBefore = Fingerprint(arguments.ResourcePath);
            Verify(moduleBefore, CrashSiteProfile.ModuleSize, CrashSiteProfile.ModuleSha256, arguments.ExpectedModuleSha256);
            Verify(resourceBefore, CrashSiteProfile.ResourceSize, CrashSiteProfile.ResourceSha256, arguments.ExpectedResourceSha256);
            TerrainLoadReport report;
            Dictionary<TerrainTextureKey, DecodedTerrainTexture> textures;
            using (var module = DatArchive.Open(arguments.ModulePath, BoundedDatOptions()))
            using (var resource = DatArchive.Open(arguments.ResourcePath, BoundedDatOptions()))
            {
                var source = new TerrainResourceSource(resource, arguments.SourceId, CrashSiteProfile.TileMesEntry, CrashSiteProfile.FacadeMesEntry,
                    TerrainPathProfile.CeNonWindowsMacOs);
                report = TerrainLoader.Inspect(module, CrashSiteProfile.CreateSelector(), source);
                if (!report.IsComplete || report.Outcomes.Count != CrashSiteProfile.CellCount)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, report.ResolvedRecords.Count, report.Issues.Count));
                var tileCount = report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Tile);
                var facadeCount = report.ResolvedRecords.Count(record => record.Kind == TerrainKind.Facade);
                var recoveredCount = report.ResolvedRecords.Count(record => record.WasNormalizationRecovered);
                var lookupCount = report.ResolvedRecords.Where(record => record.WasNormalizationRecovered).Select(record => record.LookupId).Distinct().Count();
                if (tileCount != CrashSiteProfile.TileCount || facadeCount != CrashSiteProfile.FacadeCount
                    || recoveredCount != CrashSiteProfile.NormalizationRecoveredCount || lookupCount != CrashSiteProfile.NormalizationLookupCount)
                    throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, report.ResolvedRecords.Count, report.Issues.Count));
                var sector = TerrainLoader.Load(module, CrashSiteProfile.CreateSelector(), source);
                var plan = TerrainScenePlan.Create(sector);
                textures = new Dictionary<TerrainTextureKey, DecodedTerrainTexture>();
                var checkedBytes = 0L;
                foreach (var record in sector.Records)
                {
                    var key = TerrainTextureKey.FromRecord(record);
                    if (textures.ContainsKey(key)) continue;
                    var frame = TerrainLoader.DecodeSelectedFrame(resource, record, new TerrainLoaderOptions().ArtOptions);
                    checkedBytes = TerrainCachePolicy.AddCheckedFrame(checkedBytes, textures.Count, frame.Width, frame.Height);
                    textures.Add(key, new DecodedTerrainTexture(key, frame.Width, frame.Height, frame.Rgba.ToArray()));
                }
                var moduleAfter = Fingerprint(arguments.ModulePath);
                var resourceAfter = Fingerprint(arguments.ResourcePath);
                if (moduleBefore != moduleAfter || resourceBefore != resourceAfter) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.FingerprintMismatch));
                return new TerrainLoadResult(generation, plan, textures, report.ResolvedRecords.Count,
                    tileCount, facadeCount, recoveredCount, lookupCount,
                    moduleBefore, resourceBefore, Environment.TickCount64 - started, 1);
            }
        }
        catch (TerrainViewerException) { throw; }
        catch (FileNotFoundException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable)); }
        catch (DirectoryNotFoundException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable)); }
        catch (UnauthorizedAccessException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable)); }
        catch (TerrainIncompleteException exception) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.IncompleteSector, exception.Report.ResolvedRecords.Count, exception.Report.Issues.Count)); }
        catch (TerrainLimitExceededException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit)); }
        catch (ArtLimitExceededException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit)); }
        catch (TerrainException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InvalidSector)); }
        catch (ArtException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.DecodeFailure)); }
        catch (IOException) { throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable)); }
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
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.FingerprintMismatch));
    }
    private void UpdateMaximum(int active)
    {
        int observed;
        while (active > (observed = Volatile.Read(ref _maximumObservedConcurrency)) && Interlocked.CompareExchange(ref _maximumObservedConcurrency, active, observed) != observed) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _gate.Dispose(); }
}
