using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-014: the HD path of the AREA and WORLD canvas. Under profile Hd each
/// character and object draw asks the frame source for its HD frame; a valid one is bound with
/// the scaled crop and the unchanged destination, anything else draws Legacy with a typed
/// reason (counted per submission). Terrain, terrain chunks and walls stay Legacy in this
/// stage. Plans, picking, routes, light, the wall check and Session never see HD.</summary>
public partial class TerrainCanvas
{
    private VisualProfile _visualProfile = VisualProfile.Legacy;
    private HdFrameSource? _hdSource;
    private HdDeviceBudget? _hdBudget;
    private HdResidency<ImageTexture>? _hdArea;
    private HdResidency<ImageTexture>? _hdWorld;
    private long _hdSeenCompletions = -1;
    // The pack-less source made for Hd without a selected source; owned (disposed) here.
    private HdFrameSource? _hdOwnedSource;
    /// <summary>Walls (ART class 1) stay Legacy in this stage.</summary>
    private static bool HdArtClassEligible(int artClass) => artClass != 1;

    public VisualProfile VisualProfile => _visualProfile;
    public HdFrameSource? HdSource => _hdSource;
    public HdDeviceBudget? HdBudget => _hdBudget;
    /// <summary>Counts of the last AREA and WORLD submissions (HD draws and Legacy draws by reason).</summary>
    public HdSubmissionStats AreaHdStats { get; } = new();
    public HdSubmissionStats WorldHdStats { get; } = new();
    public int HdResidentCount => (_hdArea?.Count ?? 0) + (_hdWorld?.Count ?? 0);
    public long HdResidentBytes => (_hdArea?.Bytes ?? 0) + (_hdWorld?.Bytes ?? 0);
    internal long HdCreatedCount => (_hdArea?.CreatedCount ?? 0) + (_hdWorld?.CreatedCount ?? 0);
    internal long HdDisposedCount => (_hdArea?.DisposedCount ?? 0) + (_hdWorld?.DisposedCount ?? 0);
    internal long HdAreaVisibleBytes => _hdArea?.VisibleBytes ?? 0;

    /// <summary>Selects the visual profile and the frame source it uses (null with Hd = every
    /// frame <see cref="HdFallbackReason.NoPack"/>). A new source releases every HD texture of
    /// the previous one; a switch to Legacy keeps HD textures down to the retain limits. Takes
    /// effect from the next submission; the plan, camera, light and wall fade are untouched.</summary>
    public void SetVisualProfile(VisualProfile profile, HdFrameSource? source, HdDeviceBudget? budget = null)
    {
        if (!ReferenceEquals(source, _hdSource) || !Equals(budget, _hdBudget)) ReleaseHd();
        if (_hdOwnedSource is { } owned && !ReferenceEquals(owned, source)) { if (ReferenceEquals(_hdSource, owned)) _hdSource = null; owned.Dispose(); _hdOwnedSource = null; }
        _hdSource = source; _hdBudget = budget;
        _visualProfile = profile;
        if (profile == VisualProfile.Legacy && _hdSource is { } legacySource)
        {
            // Pending decodes are dropped (their pixels would sit outside every budget) and HD
            // textures are kept down to the retain limits.
            legacySource.CancelPending();
            _hdArea?.ReleaseToRetained(legacySource); _hdWorld?.ReleaseToRetained(legacySource);
            AreaHdStats.Clear(); WorldHdStats.Clear();
        }
        _areaView = null; _hdSeenCompletions = -1; QueueRedraw();
        // Rebind the AREA items now: none may keep sampling an HD texture released above.
        if (_area is not null && IsInsideTree())
        {
            try { UpdateAreaVisible(); }
            catch (Exception error) { AreaFailure?.Invoke(error); }
        }
    }

    private bool HdActive => _visualProfile == VisualProfile.Hd;
    // The frame source used under Hd: the selected one, or a pack-less one (NoPack everywhere).
    private HdFrameSource ActiveHdSource => _hdSource ??= _hdOwnedSource = new HdFrameSource(null);
    private HdDeviceBudget ActiveHdBudget => _hdBudget ?? HdDeviceBudget.Ceiling(Math.Max(2, ActiveHdSource.Scale));

    private HdResidency<ImageTexture> HdResidency(ref HdResidency<ImageTexture>? residency, Func<HdDeviceBudget, HdDomainBudget> domain)
        => residency ??= new HdResidency<ImageTexture>(CreateHdTexture, texture => texture.Dispose(), domain(ActiveHdBudget));

    private static ImageTexture CreateHdTexture(int width, int height, byte[] rgba)
    {
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>True when pending HD work finished or uploads were deferred since the last
    /// submission, so the same view must be submitted again to bind those frames.</summary>
    private bool HdRefreshDue()
    {
        if (!HdActive || _hdSource is not { } source) return false;
        return source.CompletedCount != _hdSeenCompletions || _hdArea?.UploadsDeferred == true || _hdWorld?.UploadsDeferred == true;
    }

    /// <summary>Releases every HD texture and drops pending decodes (map change, reload, exit, pack change).</summary>
    internal void ReleaseHd()
    {
        _hdArea?.Dispose(); _hdArea = null; _hdWorld?.Dispose(); _hdWorld = null;
        _hdSource?.Reset();
        AreaHdStats.Clear(); WorldHdStats.Clear(); _hdSeenCompletions = -1;
    }

    // AREA HD textures and pending work belong to the published map; WORLD republishes its plan
    // on every actor step (ClearArea runs then too), which must not drop WORLD HD textures.
    private void ReleaseAreaHd()
    {
        if (_hdArea is null) return;
        _hdArea.Dispose(); _hdArea = null; _hdSource?.Reset(); AreaHdStats.Clear(); _hdSeenCompletions = -1;
    }

    // AREA: one binding per draw, Legacy (default) for every non-candidate.
    private HdBinding<ImageTexture>[] BindAreaHd(IReadOnlyList<AreaDrawCommand> draw)
    {
        AreaHdStats.Clear();
        if (!HdActive) return new HdBinding<ImageTexture>[draw.Count];
        var source = ActiveHdSource; var scale = source.Scale;
        _hdSeenCompletions = source.CompletedCount;
        var draws = new HdDraw[draw.Count];
        for (var index = 0; index < draw.Count; index++)
        {
            var command = draw[index];
            if (_areaChunkCommands.ContainsKey(command) || command.Identity is null) continue; // terrain stays Legacy
            if (command.Identity == ActorSentinel)
            {
                var key = _actorPlan!.Current.TextureKey;
                if (!_textures!.TryGetActorHd(key, out var query, out var pixels)) { draws[index] = new(null, null, HdFallbackReason.FrameMissing); continue; }
                draws[index] = HdCrop.Fits(new(0, 0, query.Legacy.Width, query.Legacy.Height), scale) ? new(query, () => pixels)
                    : new(null, null, HdFallbackReason.HdAxisLimit);
                continue;
            }
            if (!HdArtClassEligible(command.ArtClass)) continue;
            if (!HdCrop.Fits(command.Source, scale)) { draws[index] = new(null, null, HdFallbackReason.HdAxisLimit); continue; }
            var frame = _areaFrames![command.Frame];
            draws[index] = new(HdFrameQueries.FromArea(command.Frame, frame), () => frame.Rgba);
        }
        var bindings = HdResidency(ref _hdArea, HdDomainBudget.Area).Submit(draws, source);
        foreach (var binding in bindings) if (binding.Candidate) AreaHdStats.Add(binding.Reason);
        return bindings;
    }

    // WORLD: statics then the actor, in draw order. Source crops come from the Legacy frame size.
    private HdBinding<ImageTexture>[] BindWorldHd(IReadOnlyList<(Arcanum.Data.Hd.HdSpriteQuery? Query, byte[]? Pixels)> draws)
    {
        WorldHdStats.Clear();
        if (!HdActive) return new HdBinding<ImageTexture>[draws.Count];
        var source = ActiveHdSource;
        _hdSeenCompletions = source.CompletedCount;
        var requests = draws.Select(draw => draw.Query is { } query
            ? HdCrop.Fits(new(0, 0, query.Legacy.Width, query.Legacy.Height), source.Scale) ? new HdDraw(query, () => draw.Pixels!) : new HdDraw(null, null, HdFallbackReason.HdAxisLimit)
            : new HdDraw(null, null, HdFallbackReason.FrameMissing)).ToArray();
        var bindings = HdResidency(ref _hdWorld, HdDomainBudget.World).Submit(requests, source);
        foreach (var binding in bindings) if (binding.Candidate) WorldHdStats.Add(binding.Reason);
        return bindings;
    }

    private void ProcessWorldHd()
    {
        if (_area is null && _plan is not null && HdRefreshDue()) QueueRedraw();
    }

    /// <summary>HUD text: profile, pack, counts of the last submission and HD residency.</summary>
    public string HdStatusText()
    {
        if (!HdActive) return _hdSource?.Pack is { } idle ? $"visual Legacy (HD pack {idle.Id} {idle.Scale}x loaded)" : "visual Legacy";
        var source = ActiveHdSource;
        var pack = source.Pack is { } p ? $"pack {p.Id} {p.Scale}x {p.Review.ToString().ToLowerInvariant()}" : "no pack";
        var stats = _area is not null ? AreaHdStats : WorldHdStats;
        return $"visual HD {pack}: {stats.Format()}, HD textures {HdResidentCount}/{HdResidentBytes} bytes, decoding {source.PendingCount}; terrain and walls Legacy";
    }
}
