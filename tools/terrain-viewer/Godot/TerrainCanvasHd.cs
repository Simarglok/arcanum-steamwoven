using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-014: the HD path of the AREA and WORLD canvas. Under profile Hd each
/// character and object draw asks the frame source for its HD frame; a valid one is bound with
/// the scaled crop and the unchanged destination, anything else draws Legacy with a typed
/// reason (counted per submission). ARC-RENDER-016 adds walls and the composed level-0 terrain
/// chunks (tiles and facades) of the AREA; per-command terrain, decimated chunks (zoom below 1)
/// and WORLD terrain stay Legacy. Plans, picking, routes, light, the wall check and Session never see HD. ARC-UI-006 adds
/// the requested switch with its timed commit (<see cref="RequestVisualProfile"/>).</summary>
public partial class TerrainCanvas
{
    // ARC-UI-006: the active profile (it draws) and a pending switch with its timed commit.
    private readonly VisualProfileSwitch _visualSwitch = new(VisualProfile.Legacy);
    private HdFrameSource? _hdSource;
    private HdDeviceBudget? _hdBudget;
    private HdResidency<ImageTexture>? _hdArea;
    // ARC-RENDER-016: AREA terrain chunks under their own (terrain) budget.
    private HdResidency<ImageTexture>? _hdTerrain;
    private HdResidency<ImageTexture>? _hdWorld;
    private long _hdSeenCompletions = -1;
    // The pack-less source made for Hd without a selected source; owned (disposed) here.
    private HdFrameSource? _hdOwnedSource;
    // ARC-RENDER-016: the HD question of each level-0 chunk of the published map, made once (one
    // compose and hash per chunk); dropped with the map's HD textures.
    private readonly Dictionary<(int X, int Y), HdSpriteQuery> _hdChunkQueries = new();

    public VisualProfile VisualProfile => _visualSwitch.Active;
    /// <summary>The requested profile being prepared (ARC-UI-006), or null.</summary>
    public VisualProfile? PendingVisualProfile => _visualSwitch.Pending;
    /// <summary>The profile a toggle asks for: the other one than pending, else than active.</summary>
    public VisualProfile ToggledVisualProfile => _visualSwitch.Toggled;
    public long VisualGeneration => _visualSwitch.Generation;
    /// <summary>Commits of a requested switch so far, and how the last one went.</summary>
    public long VisualCommits { get; private set; }
    public VisualCommitRecord? LastVisualCommit { get; private set; }
    /// <summary>The monotonic millisecond clock of the timed commit; replaceable by self-tests.</summary>
    internal Func<long> VisualClock { get; set; } = () => (long)Time.GetTicksMsec();
    public HdFrameSource? HdSource => _hdSource;
    public HdDeviceBudget? HdBudget => _hdBudget;
    /// <summary>Counts of the last AREA and WORLD submissions (HD draws and Legacy draws by reason).</summary>
    public HdSubmissionStats AreaHdStats { get; } = new();
    public HdSubmissionStats WorldHdStats { get; } = new();
    public int HdResidentCount => (_hdArea?.Count ?? 0) + (_hdTerrain?.Count ?? 0) + (_hdWorld?.Count ?? 0);
    public long HdResidentBytes => (_hdArea?.Bytes ?? 0) + (_hdTerrain?.Bytes ?? 0) + (_hdWorld?.Bytes ?? 0);
    /// <summary>Resident HD terrain chunks and their bytes (ARC-RENDER-016).</summary>
    public int HdTerrainResidentCount => _hdTerrain?.Count ?? 0;
    public long HdTerrainResidentBytes => _hdTerrain?.Bytes ?? 0;
    /// <summary>HD terrain chunks bound by the last AREA submission (not kept ones).</summary>
    internal int HdTerrainVisibleCount => _hdTerrain?.VisibleFrames ?? 0;
    internal long HdCreatedCount => (_hdArea?.CreatedCount ?? 0) + (_hdTerrain?.CreatedCount ?? 0) + (_hdWorld?.CreatedCount ?? 0);
    internal long HdDisposedCount => (_hdArea?.DisposedCount ?? 0) + (_hdTerrain?.DisposedCount ?? 0) + (_hdWorld?.DisposedCount ?? 0);
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
        _visualSwitch.Set(profile);
        if (profile == VisualProfile.Legacy) DropHdPreparation();
        ResubmitForVisual();
    }

    /// <summary>ARC-UI-006 toggle protocol (ARC-RENDER-013 steps 1, 3 and 4): requests
    /// <paramref name="profile"/> with a new generation. The active profile keeps drawing while the
    /// requested one is prepared for the visible set (off-thread decodes, bounded uploads); the
    /// switch commits between two submissions once every visible frame is ready or refused, or after
    /// <see cref="VisualProfileSwitch.DefaultCommitWaitMilliseconds"/> (still pending frames then draw
    /// Legacy and switch individually). A request for the active profile while a switch is pending
    /// cancels it and drops its pending decodes; HD textures already made stay within the retain
    /// limits. The plan, camera, zoom, wall fade, light, animation and Session are never touched.</summary>
    public VisualRequestOutcome RequestVisualProfile(VisualProfile profile)
    {
        var outcome = _visualSwitch.Request(profile, VisualClock());
        if (outcome == VisualRequestOutcome.NoOp) return outcome;
        if (outcome == VisualRequestOutcome.Cancelled && _visualSwitch.Active == VisualProfile.Legacy) DropHdPreparation();
        // Nothing published draws, so nothing is visible: the switch commits at once.
        if (_area is null && _plan is null) CommitVisual(settled: true, 0);
        ResubmitForVisual();
        return outcome;
    }

    // The next submission binds for the new state; AREA items are rebound now, so none keeps
    // sampling an HD texture released by a switch to Legacy.
    private void ResubmitForVisual()
    {
        _areaView = null; _hdSeenCompletions = -1; QueueRedraw();
        if (_area is not null && IsInsideTree())
        {
            try { UpdateAreaVisible(); }
            catch (Exception error) { AreaFailure?.Invoke(error); }
        }
    }

    // Pending decodes are dropped (their pixels would sit outside every budget) and HD textures are
    // kept down to the retain limits: a switch to Legacy, or a cancelled switch to HD.
    private void DropHdPreparation()
    {
        if (_hdSource is { } source)
        {
            source.CancelPending();
            _hdArea?.ReleaseToRetained(source); _hdTerrain?.ReleaseToRetained(source); _hdWorld?.ReleaseToRetained(source);
        }
        AreaHdStats.Clear(); WorldHdStats.Clear();
    }

    // Step 4: commits a pending switch when settled or timed out; a commit to Legacy releases HD
    // textures down to the retain limits. Called by each submission before it binds textures.
    private bool CommitVisual(bool settled, int pendingFrames)
    {
        var now = VisualClock(); var requested = _visualSwitch.RequestedAt; var target = _visualSwitch.Pending;
        if (!_visualSwitch.TryCommit(settled, now)) return false;
        VisualCommits++;
        LastVisualCommit = new(target!.Value, _visualSwitch.Generation, now - requested, TimedOut: !settled, pendingFrames);
        if (_visualSwitch.Active == VisualProfile.Legacy) DropHdPreparation();
        return true;
    }

    private bool HdActive => _visualSwitch.Active == VisualProfile.Hd;
    // The profile a submission binds for: the pending one if any (it may commit in this submission).
    private bool HdTarget => (_visualSwitch.Pending ?? _visualSwitch.Active) == VisualProfile.Hd;
    // The frame source used under Hd: the selected one, or a pack-less one (NoPack everywhere).
    private HdFrameSource ActiveHdSource => _hdSource ??= _hdOwnedSource = new HdFrameSource(null);
    private HdDeviceBudget ActiveHdBudget => _hdBudget ?? HdDeviceBudget.Ceiling(Math.Max(2, ActiveHdSource.Scale));

    private HdResidency<ImageTexture> HdResidency(ref HdResidency<ImageTexture>? residency, Func<HdDeviceBudget, HdDomainBudget> domain,
        Func<HdSpriteQuery, bool>? scope = null)
        => residency ??= new HdResidency<ImageTexture>(CreateHdTexture, texture => texture.Dispose(), domain(ActiveHdBudget), scope: scope);
    private static bool IsChunkQuery(HdSpriteQuery query) => query.Chunk is not null;
    private static bool IsFrameQuery(HdSpriteQuery query) => query.Chunk is null;

    private static ImageTexture CreateHdTexture(int width, int height, byte[] rgba)
    {
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>True when pending HD work finished or uploads were deferred since the last
    /// submission, so the same view must be submitted again to bind those frames.</summary>
    private bool HdRefreshDue()
    {
        // A pending switch resubmits until it commits: at once to Legacy, and to HD when decodes
        // finish, uploads were deferred or the bounded wait has passed.
        if (_visualSwitch.Pending == VisualProfile.Legacy || _visualSwitch.TimedOut(VisualClock())) return true;
        if (!HdTarget || _hdSource is not { } source) return false;
        return source.CompletedCount != _hdSeenCompletions || _hdArea?.UploadsDeferred == true || _hdTerrain?.UploadsDeferred == true || _hdWorld?.UploadsDeferred == true;
    }

    /// <summary>Releases every HD texture and drops pending decodes (map change, reload, exit, pack change).</summary>
    internal void ReleaseHd()
    {
        _hdArea?.Dispose(); _hdArea = null; _hdTerrain?.Dispose(); _hdTerrain = null; _hdWorld?.Dispose(); _hdWorld = null;
        _hdSource?.Reset(); _hdChunkQueries.Clear();
        AreaHdStats.Clear(); WorldHdStats.Clear(); _hdSeenCompletions = -1;
    }

    // AREA HD textures and pending work belong to the published map; WORLD republishes its plan
    // on every actor step (ClearArea runs then too), which must not drop WORLD HD textures.
    private void ReleaseAreaHd()
    {
        if (_hdArea is null && _hdTerrain is null) return;
        _hdArea?.Dispose(); _hdArea = null; _hdTerrain?.Dispose(); _hdTerrain = null;
        _hdSource?.Reset(); AreaHdStats.Clear(); _hdSeenCompletions = -1; _hdChunkQueries.Clear();
    }

    // AREA: one binding per draw, Legacy (default) for every non-candidate.
    private HdBinding<ImageTexture>[] BindAreaHd(IReadOnlyList<AreaDrawCommand> draw)
    {
        AreaHdStats.Clear();
        if (!HdTarget) { CommitVisual(settled: true, 0); return new HdBinding<ImageTexture>[draw.Count]; }
        var source = ActiveHdSource; var scale = source.Scale;
        _hdSeenCompletions = source.CompletedCount;
        var draws = new HdDraw[draw.Count];
        for (var index = 0; index < draw.Count; index++)
        {
            var command = draw[index];
            if (_areaChunkCommands.TryGetValue(command, out var chunk))
            {
                // Decimated chunks (zoom 2^-k with an integer origin) draw Legacy and are not candidates.
                if (AreaChunkLevel != 0) continue;
                // Without a usable pack or without chunk entries no chunk is composed for a question.
                var refused = source.Pack is not { } pack ? HdFallbackReason.NoPack : pack.Review == HdReviewStatus.Rejected ? HdFallbackReason.PackRejected
                    : pack.TerrainChunkCount == 0 ? HdFallbackReason.FrameMissing : (HdFallbackReason?)null;
                if (refused is not null) { draws[index] = new(null, null, refused); continue; }
                if (!HdCrop.Fits(command.Source, scale)) { draws[index] = new(null, null, HdFallbackReason.HdAxisLimit); continue; }
                var terrain = _areaChunks!;
                byte[]? fresh = null;
                if (!_hdChunkQueries.TryGetValue(chunk, out var chunkQuery))
                {
                    fresh = terrain.Compose(chunk, 0);
                    _hdChunkQueries[chunk] = chunkQuery = HdFrameQueries.FromTerrainChunk(terrain, chunk, fresh);
                }
                // The first request takes the bytes composed for the question; a later one composes again.
                draws[index] = new(chunkQuery, () => { var bytes = fresh ?? terrain.Compose(chunk, 0); fresh = null; return bytes; });
                continue;
            }
            if (command.Identity is null) continue; // per-command terrain stays Legacy
            if (command.Identity == ActorSentinel)
            {
                var key = _actorPlan!.Current.TextureKey;
                if (!_textures!.TryGetActorHd(key, out var query, out var pixels)) { draws[index] = new(null, null, HdFallbackReason.FrameMissing); continue; }
                draws[index] = HdCrop.Fits(new(0, 0, query.Legacy.Width, query.Legacy.Height), scale) ? new(query, () => pixels)
                    : new(null, null, HdFallbackReason.HdAxisLimit);
                continue;
            }
            if (!HdCrop.Fits(command.Source, scale)) { draws[index] = new(null, null, HdFallbackReason.HdAxisLimit); continue; }
            var frame = _areaFrames![command.Frame];
            draws[index] = new(HdFrameQueries.FromArea(command.Frame, frame), () => frame.Rgba);
        }
        // Terrain chunks go to their own residency (terrain budget), everything else to the AREA one;
        // each binds in draw order and the bindings are merged back by index.
        var isChunk = draw.Select(command => _areaChunkCommands.ContainsKey(command)).ToArray();
        var bodyBindings = HdResidency(ref _hdArea, HdDomainBudget.Area, IsFrameQuery).Submit(draws.Select((d, i) => isChunk[i] ? HdDraw.None : d).ToArray(), source);
        // The terrain residency is submitted whenever it exists (all None without chunks in view), so
        // it keeps its textures within limits, drops its own decoded pixels and clears deferred uploads.
        var chunkBindings = isChunk.Any(chunk => chunk) || _hdTerrain is not null
            ? HdResidency(ref _hdTerrain, HdDomainBudget.Terrain, IsChunkQuery).Submit(draws.Select((d, i) => isChunk[i] ? d : HdDraw.None).ToArray(), source)
            : new HdBinding<ImageTexture>[draw.Count];
        var bindings = bodyBindings.Select((binding, i) => isChunk[i] ? chunkBindings[i] : binding).ToArray();
        if (!HdActive && !CommitPreparedHd(bindings)) return new HdBinding<ImageTexture>[draw.Count];
        foreach (var binding in bindings) if (binding.Candidate) AreaHdStats.Add(binding.Reason);
        return bindings;
    }

    // WORLD: statics then the actor, in draw order. Source crops come from the Legacy frame size.
    private HdBinding<ImageTexture>[] BindWorldHd(IReadOnlyList<(Arcanum.Data.Hd.HdSpriteQuery? Query, byte[]? Pixels)> draws)
    {
        WorldHdStats.Clear();
        if (!HdTarget) { CommitVisual(settled: true, 0); return new HdBinding<ImageTexture>[draws.Count]; }
        var source = ActiveHdSource;
        _hdSeenCompletions = source.CompletedCount;
        var requests = draws.Select(draw => draw.Query is { } query
            ? HdCrop.Fits(new(0, 0, query.Legacy.Width, query.Legacy.Height), source.Scale) ? new HdDraw(query, () => draw.Pixels!) : new HdDraw(null, null, HdFallbackReason.HdAxisLimit)
            : new HdDraw(null, null, HdFallbackReason.FrameMissing)).ToArray();
        var bindings = HdResidency(ref _hdWorld, HdDomainBudget.World).Submit(requests, source);
        if (!HdActive && !CommitPreparedHd(bindings)) return new HdBinding<ImageTexture>[draws.Count];
        foreach (var binding in bindings) if (binding.Candidate) WorldHdStats.Add(binding.Reason);
        return bindings;
    }

    // A switch to HD is being prepared: these bindings were made for the visible set (decodes
    // requested, textures uploaded within the batch) while Legacy still draws. They are used, all at
    // once, only when the switch commits in this submission; otherwise this submission draws Legacy.
    private bool CommitPreparedHd(HdBinding<ImageTexture>[] bindings)
    {
        var pending = bindings.Count(binding => binding.Reason == HdFallbackReason.Pending);
        return CommitVisual(settled: pending == 0, pending);
    }

    private void ProcessWorldHd()
    {
        if (_area is null && _plan is null && _visualSwitch.Pending is not null) CommitVisual(settled: true, 0);
        if (_area is null && _plan is not null && HdRefreshDue()) QueueRedraw();
    }

    /// <summary>The world destinations of the drawn character and object items, in draw order, as
    /// a count and a SHA-256 prefix (ARC-UI-006 invariance: equal under Legacy and HD).</summary>
    internal string DrawnDestinationsForSelfTest()
    {
        var items = GetChildren().OfType<AreaDrawItem>().Where(item => item.Visible && item.Command?.Identity is not null).ToArray();
        var text = string.Join(";", items.Select(item => $"{item.Command!.Identity}@{item.Command.Destination.X},{item.Command.Destination.Y},{item.Command.Destination.Width},{item.Command.Destination.Height}"));
        return $"{items.Length} items {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16]}";
    }

    /// <summary>HUD text: profile, pack, counts of the last submission and HD residency.</summary>
    public string HdStatusText()
    {
        var switching = _visualSwitch.Pending is { } pending
            ? $"; switching to {(pending == VisualProfile.Hd ? "HD" : "Legacy")} (generation {_visualSwitch.Generation}, decoding {_hdSource?.PendingCount ?? 0})" : "";
        if (!HdActive) return (_hdSource?.Pack is { } idle ? $"visual Legacy (HD pack {idle.Id} {idle.Scale}x loaded)" : "visual Legacy") + switching;
        var source = ActiveHdSource;
        var pack = source.Pack is { } p ? $"pack {p.Id} {p.Scale}x {p.Review.ToString().ToLowerInvariant()}" : "no pack";
        var stats = _area is not null ? AreaHdStats : WorldHdStats;
        return $"visual HD {pack}: {stats.Format()}, HD textures {HdResidentCount}/{HdResidentBytes} bytes, decoding {source.PendingCount}{(_area is not null && AreaChunkLevel != 0 ? "; terrain Legacy below zoom 1" : "")}" + switching;
    }
}

/// <summary>How a requested switch committed (ARC-UI-006): the profile, its generation, the time
/// from request to commit, whether the bounded wait ended it, and how many visible frames were
/// still pending then (they draw Legacy until ready).</summary>
public sealed record VisualCommitRecord(VisualProfile Profile, long Generation, long Milliseconds, bool TimedOut, int PendingFrames);
