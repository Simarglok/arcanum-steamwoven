using Arcanum.Data;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Closed numeric actor catalogues; no names, appearance discovery or general actor records.</summary>
public static class ActorDiagnosticSource
{
    // Historical standalone preview constants retained for compatibility only.
    public const int ModuleOrdinal = 643;
    public const uint BaseSelector = 0xc9001810;
    public const uint IdleSelector = 0xc9001810;
    public const uint WalkSelector = 0xc9001850;
    public const int IdleResourceOrdinal = 4753;
    public const int WalkResourceOrdinal = 4754;
    public const int Palette = 1;
    public const int PhysicalGroups = 8;
    public const int IdleFrames = 15;
    public const int WalkFrames = 8;
    public const int IdleFps = 8;
    public const int WalkFps = 15;

    public static ActorDiagnosticLoad Create(DatArchive module, DatArchive resource, ActorDiagnosticPlacementInput input)
        => Create(module, resource, input, 0, 0, CancellationToken.None);

    public static ActorDiagnosticLoad Create(DatArchive module, DatArchive resource, ActorDiagnosticPlacementInput input,
        int alreadyRetainedCount, long alreadyCheckedRgbaBytes, CancellationToken cancellationToken, Action<int>? retainedFrameCheckpoint = null,
        TerrainViewerMode mode = TerrainViewerMode.ActorDiagnostic)
        => CreateWithBudget(module, resource, input, alreadyRetainedCount, alreadyCheckedRgbaBytes, cancellationToken,
            TerrainLoadBudget.Default, point => retainedFrameCheckpoint?.Invoke(point.RetainedCount), mode);

    internal static ActorDiagnosticLoad CreateWithBudget(DatArchive module, DatArchive resource, ActorDiagnosticPlacementInput input,
        int alreadyRetainedCount, long alreadyCheckedRgbaBytes, CancellationToken cancellationToken, TerrainLoadBudget budget,
        Action<TerrainLoadCheckpoint>? retainedFrameCheckpoint, TerrainViewerMode mode,
        ActorDiagnosticProfile? profile = null, bool authenticateOriginalMetadata = true)
    {
        profile ??= ActorDiagnosticProfile.ForMode(mode);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(resource);
        if (input is null || input.MapX is null || input.MapY is null || input.CameraOriginX is null || input.CameraOriginY is null
            || input.InstanceOffsetX is null || input.InstanceOffsetY is null || input.Shrunk is null)
            throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        if (input.RenderBranch != ActorDiagnosticRenderBranch.NormalUncached || input.ScalePercent != 100 || input.Shrunk != false)
            throw Failure(TerrainViewerErrorCode.ActorRenderState, TerrainViewerFailureStage.ActorRenderState);
        if (!Enum.IsDefined(input.Action)) throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        var facing = profile.Facing(input.Action, input.Facing);
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.ModuleOrdinal is int moduleOrdinal && module.Entries.Count(entry => entry.Ordinal == moduleOrdinal) != 1)
            throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        var catalog = new List<ActorDiagnosticFrame>(profile.DistinctKeys);
        var textures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>();
        var checkedRgbaBytes = alreadyCheckedRgbaBytes;
        foreach (var action in profile.Actions)
            checkedRgbaBytes = DecodeAction(resource, profile.Resource(action), profile, authenticateOriginalMetadata,
                catalog, textures, alreadyRetainedCount, checkedRgbaBytes, cancellationToken, budget, retainedFrameCheckpoint, mode);
        if (textures.Count != profile.DistinctKeys || catalog.Count != profile.DistinctKeys
            || (authenticateOriginalMetadata && profile.OriginalRgbaBytes is long expectedBytes && checkedRgbaBytes - alreadyCheckedRgbaBytes != expectedBytes))
            throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        var selected = catalog.SingleOrDefault(frame => frame.TextureKey.PhysicalGroup == facing.PhysicalGroup && frame.TextureKey.Frame == input.Frame
            && frame.TextureKey.Selector == profile.Resource(input.Action).Selector);
        if (selected is null) throw Failure(TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        var plannedInput = input with { Width = selected.Width, Height = selected.Height, HotX = selected.HotX, HotY = selected.HotY,
            ArtOffsetX = selected.ArtOffsetX, ArtOffsetY = selected.ArtOffsetY };
        var plan = ActorDiagnosticScenePlan.CreateSynthetic(plannedInput, catalog, profile);
        return new ActorDiagnosticLoad(plan, textures, checkedRgbaBytes);
    }

    private static long DecodeAction(DatArchive resource, ActorDiagnosticResource descriptor, ActorDiagnosticProfile profile, bool authenticateOriginalMetadata,
        ICollection<ActorDiagnosticFrame> catalog, IDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> textures, int alreadyRetainedCount,
        long checkedRgbaBytes, CancellationToken cancellationToken, TerrainLoadBudget budget, Action<TerrainLoadCheckpoint>? retainedFrameCheckpoint, TerrainViewerMode mode)
    {
        var entries = resource.Entries.Where(candidate => candidate.Ordinal == descriptor.Ordinal).ToArray();
        if (entries.Length != 1) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = resource.ReadEntry(entries[0]);
            // ARC-RENDER-014: the install-independent ART identity of the frames read below.
            var art = HdFrameQueries.ArtIdentity(bytes, entries[0].RawNameBytes);
            var options = new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = descriptor.PhysicalGroups * descriptor.FramesPerGroup };
            var metadata = ArtDecoder.Inspect(bytes, options);
            if (metadata.Header.PhysicalGroupCount != descriptor.PhysicalGroups || metadata.Header.FramesPerGroup != descriptor.FramesPerGroup
                || metadata.Header.FramesPerSecond != descriptor.RawFramesPerSecond || metadata.Header.FramesPerSecond <= 0
                || metadata.Header.PalettePresenceWords.Count <= descriptor.Palette || metadata.Header.PalettePresenceWords[descriptor.Palette] == 0)
                throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
            if (authenticateOriginalMetadata) profile.ValidateOriginalMetadata(descriptor.Action, metadata);
            foreach (var group in profile.UsedGroups(descriptor.Action))
            for (var frame = 0; frame < descriptor.FramesPerGroup; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = new ActorDiagnosticTextureKey(descriptor.Ordinal, descriptor.Selector, group, frame, descriptor.Palette);
                if (textures.ContainsKey(key)) continue;
                var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frame, descriptor.Palette), options);
                if (decoded.EffectivePaletteSlot != descriptor.Palette) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                var geometry = decoded.Frame;
                // Decoder storage is transient. Account the exact unique key and
                // checked RGBA BEFORE retaining/copying into the load catalogue.
                try
                {
                    checkedRgbaBytes = budget.Add(checkedRgbaBytes, checked(alreadyRetainedCount + textures.Count), textures.Count,
                        Math.Min(budget.Actor, profile.DistinctKeys), decoded.Width, decoded.Height, mode, profile);
                }
                catch (TerrainViewerException exception) when (exception.Error.Code is TerrainViewerErrorCode.ResourceLimit or TerrainViewerErrorCode.DecodeFailure)
                { throw Failure(exception.Error.Code, TerrainViewerFailureStage.ActorDecode); }
                var rgba = decoded.Rgba.ToArray();
                textures.Add(key, new DecodedActorTexture(key, decoded.Width, decoded.Height, rgba)
                { HdQuery = HdFrameQueries.FromArt(art.PayloadSha256, art.ViewLength, art.NameSha256, group, frame, decoded.EffectivePaletteSlot, decoded.Width, decoded.Height, rgba) });
                catalog.Add(new ActorDiagnosticFrame(key, decoded.Width, decoded.Height, geometry.HotX, geometry.HotY,
                    geometry.OffsetX, geometry.OffsetY, descriptor.FramesPerGroup, descriptor.RawFramesPerSecond));
                retainedFrameCheckpoint?.Invoke(new("actor", textures.Count, alreadyRetainedCount + textures.Count, checkedRgbaBytes));
                cancellationToken.ThrowIfCancellationRequested();
            }
            return checkedRgbaBytes;
        }
        catch (TerrainViewerException) { throw; }
        catch (DatException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArtException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (IOException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
    }

    private static TerrainViewerException Failure(TerrainViewerErrorCode code, TerrainViewerFailureStage stage) => new(new TerrainViewerError(code, Stage: stage));

    /// <summary>Path-free identity boundary shared by production and fixture tests.</summary>
    public static void VerifyIdentity(InputFingerprint actual, long expectedSize, string expectedHash, string? requestedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        if (actual.Size != expectedSize || !string.Equals(actual.Sha256, expectedHash, StringComparison.Ordinal)
            || (requestedHash is not null && !string.Equals(actual.Sha256, requestedHash, StringComparison.Ordinal)))
            throw Failure(TerrainViewerErrorCode.ActorFingerprint, TerrainViewerFailureStage.ActorFingerprint);
    }
}

/// <summary>Owns one actor archive with path-free open failures and idempotent disposal.</summary>
public sealed class ActorDiagnosticSourceOperation : IDisposable
{
    private readonly Func<DatArchive> _openArchive;
    private DatArchive? _archive;
    private int _disposed;

    public ActorDiagnosticSourceOperation(Func<DatArchive> openArchive)
        => _openArchive = openArchive ?? throw new ArgumentNullException(nameof(openArchive));

    public ActorDiagnosticLoad Execute(DatArchive module, ActorDiagnosticPlacementInput input, int alreadyRetainedCount,
        long alreadyCheckedRgbaBytes, TerrainViewerProgress progress, CancellationToken cancellationToken,
        Action<int>? retainedFrameCheckpoint = null, TerrainViewerMode mode = TerrainViewerMode.ActorDiagnostic)
        => ExecuteWithBudget(module, input, alreadyRetainedCount, alreadyCheckedRgbaBytes, progress, cancellationToken,
            TerrainLoadBudget.Default, point => retainedFrameCheckpoint?.Invoke(point.RetainedCount), mode);

    internal ActorDiagnosticLoad ExecuteWithBudget(DatArchive module, ActorDiagnosticPlacementInput input, int alreadyRetainedCount,
        long alreadyCheckedRgbaBytes, TerrainViewerProgress progress, CancellationToken cancellationToken, TerrainLoadBudget budget,
        Action<TerrainLoadCheckpoint>? retainedFrameCheckpoint, TerrainViewerMode mode,
        ActorDiagnosticProfile? profile = null, bool authenticateOriginalMetadata = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { _archive ??= _openArchive(); }
        catch (DatException) { throw OpenFailure(progress); }
        catch (IOException) { throw OpenFailure(progress); }
        catch (UnauthorizedAccessException) { throw OpenFailure(progress); }
        ArgumentNullException.ThrowIfNull(module);
        return ActorDiagnosticSource.CreateWithBudget(module, _archive, input, alreadyRetainedCount, alreadyCheckedRgbaBytes,
            cancellationToken, budget, retainedFrameCheckpoint, mode, profile, authenticateOriginalMetadata);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _archive?.Dispose();
    }

    private static TerrainViewerException OpenFailure(TerrainViewerProgress progress)
        => new(new TerrainViewerError(TerrainViewerErrorCode.ActorFingerprint, Stage: TerrainViewerFailureStage.ActorFingerprint, Progress: progress));
}

public sealed record DecodedActorTexture(ActorDiagnosticTextureKey Key, int Width, int Height, byte[] Rgba)
{
    /// <summary>ARC-RENDER-014: the portable HD question of this frame (null for fixtures
    /// without an ART identity, which then draw Legacy as <c>FrameMissing</c>).</summary>
    public Arcanum.Data.Hd.HdSpriteQuery? HdQuery { get; init; }
}
public sealed record ActorDiagnosticLoad(ActorDiagnosticScenePlan Plan, IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> Textures,
    long CheckedRgbaBytes);
