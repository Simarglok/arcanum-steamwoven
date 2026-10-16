using Arcanum.Data.Hd;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One draw's HD outcome: the HD texture to bind, or the reason it draws Legacy.
/// A default value (no texture, no reason) means the draw was not an HD candidate.</summary>
public readonly record struct HdBinding<TTexture>(TTexture? Texture, HdFallbackReason? Reason) where TTexture : class
{
    public bool IsHd => Texture is not null;
    public bool Candidate => Texture is not null || Reason is not null;
}

/// <summary>One HD candidate of a submission, in draw order. <paramref name="Query"/> null
/// with a <paramref name="Reason"/> is a draw already refused by the caller (for example
/// <see cref="HdFallbackReason.HdAxisLimit"/>); both null is not a candidate.</summary>
public readonly record struct HdDraw(HdSpriteQuery? Query, Func<byte[]>? Legacy, HdFallbackReason? Reason = null)
{
    public static HdDraw None => default;
}

/// <summary>The HD texture cache of one domain (AREA or WORLD), owned by the presentation
/// layer (ARC-RENDER-013 Budgets): textures keyed by the pack's frame question inside one pack
/// identity, admitted per submission in draw order against the domain budget, kept after they
/// leave the view within the retain limits (least recently used evicted first, before any new
/// upload), and uploaded on the caller's thread in bounded batches. A frame that does not fit
/// draws Legacy with <see cref="HdFallbackReason.HdBudget"/>; exceeding a budget never throws.
/// Legacy textures and budgets are not touched. Texture creation and disposal are delegates so
/// the policy is testable without a GPU.</summary>
public sealed class HdResidency<TTexture> : IDisposable where TTexture : class
{
    private readonly Func<int, int, byte[], TTexture> _create;
    private readonly Action<TTexture> _dispose;
    private readonly Dictionary<HdSpriteQuery, (TTexture Texture, long Bytes, long LastUse)> _resident = new();
    private long _submission;

    /// <param name="create">Makes a texture from w, h and RGBA (the caller's GPU upload).</param>
    /// <param name="maxUploadsPerSubmission">Uploads per submission; the rest stay Pending
    /// and <see cref="UploadsDeferred"/> asks for another submission.</param>
    public HdResidency(Func<int, int, byte[], TTexture> create, Action<TTexture> dispose, HdDomainBudget budget,
        int maxUploadsPerSubmission = 64, long maxUploadBytesPerSubmission = 64L * 1024 * 1024)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        if (budget.VisibleBytes < 0 || budget.RetainFrames < 0 || budget.RetainBytes < 0 || budget.TotalBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (maxUploadsPerSubmission <= 0 || maxUploadBytesPerSubmission <= 0) throw new ArgumentOutOfRangeException(nameof(maxUploadsPerSubmission));
        MaxUploadsPerSubmission = maxUploadsPerSubmission; MaxUploadBytesPerSubmission = maxUploadBytesPerSubmission;
    }

    public HdDomainBudget Budget { get; }
    public int MaxUploadsPerSubmission { get; }
    public long MaxUploadBytesPerSubmission { get; }
    public int Count => _resident.Count;
    public long Bytes { get; private set; }
    /// <summary>Distinct HD frames and bytes admitted by the last submission; the rest of
    /// <see cref="Count"/>/<see cref="Bytes"/> is retained.</summary>
    public int VisibleFrames { get; private set; }
    public long VisibleBytes { get; private set; }
    public long CreatedCount { get; private set; }
    public long DisposedCount { get; private set; }
    /// <summary>The last submission left Ready frames unuploaded (batch limit).</summary>
    public bool UploadsDeferred { get; private set; }

    /// <summary>Binds one submission. <paramref name="draws"/> is in draw order; the result
    /// has one binding per draw. Admission is a function of the draw order and the Legacy
    /// frame sizes only: a frame whose <c>N²·w·h·4</c> bytes would exceed the visible budget
    /// is <see cref="HdFallbackReason.HdBudget"/> whether or not it is decoded yet. Admitted
    /// frames bind once resident; otherwise they are requested from <paramref name="source"/>
    /// and bind when Ready and within the upload batch (Pending until then).</summary>
    public HdBinding<TTexture>[] Submit(IReadOnlyList<HdDraw> draws, HdFrameSource source)
    {
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(source);
        _submission++;
        var scale = source.Scale;
        var bindings = new HdBinding<TTexture>[draws.Count];
        var admitted = new Dictionary<HdSpriteQuery, long>();
        var order = new List<(HdSpriteQuery Query, Func<byte[]> Legacy)>();
        long visible = 0;
        for (var index = 0; index < draws.Count; index++)
        {
            var draw = draws[index];
            if (draw.Query is not { } query)
            {
                if (draw.Reason is { } refused) bindings[index] = new(null, refused);
                continue;
            }
            if (admitted.ContainsKey(query)) continue;
            var known = source.Peek(query);
            if (known.State == HdSourceState.Refused) { bindings[index] = new(null, known.Reason); continue; }
            var bytes = checked((long)scale * scale * query.Legacy.Width * query.Legacy.Height * 4);
            if (visible + bytes > Budget.VisibleBytes) { bindings[index] = new(null, HdFallbackReason.HdBudget); continue; }
            visible += bytes; admitted.Add(query, bytes); order.Add((query, draw.Legacy ?? throw new ArgumentException("An HD candidate needs its Legacy pixels.", nameof(draws))));
        }

        // Frames that left the view are retained within the limits, least recently used
        // evicted first, before anything new is uploaded.
        var retainBytesLimit = Budget.TotalBytes is { } total ? Math.Max(0, Math.Min(Budget.RetainBytes, total - visible)) : Budget.RetainBytes;
        var retained = _resident.Where(pair => !admitted.ContainsKey(pair.Key)).OrderBy(pair => pair.Value.LastUse).Select(pair => pair.Key).ToList();
        var retainedBytes = retained.Sum(key => _resident[key].Bytes); var retainedCount = retained.Count;
        foreach (var key in retained)
        {
            if (retainedCount <= Budget.RetainFrames && retainedBytes <= retainBytesLimit) break;
            retainedBytes -= _resident[key].Bytes; retainedCount--;
            Evict(key);
        }

        var ready = new Dictionary<HdSpriteQuery, HdBinding<TTexture>>();
        int uploads = 0; long uploadBytes = 0; UploadsDeferred = false;
        foreach (var (query, legacy) in order)
        {
            if (_resident.TryGetValue(query, out var resident))
            {
                _resident[query] = resident with { LastUse = _submission };
                ready[query] = new(resident.Texture, null);
                continue;
            }
            var status = source.Request(query, legacy);
            switch (status.State)
            {
                case HdSourceState.Refused: ready[query] = new(null, status.Reason); break;
                case HdSourceState.Ready when uploads < MaxUploadsPerSubmission && (uploads == 0 || uploadBytes + status.Rgba!.Length <= MaxUploadBytesPerSubmission):
                {
                    var texture = _create(scale * query.Legacy.Width, scale * query.Legacy.Height, status.Rgba!);
                    CreatedCount++; uploads++; uploadBytes += status.Rgba!.Length;
                    _resident[query] = (texture, admitted[query], _submission); Bytes += admitted[query];
                    source.Release(query);
                    ready[query] = new(texture, null);
                    break;
                }
                case HdSourceState.Ready: UploadsDeferred = true; ready[query] = new(null, HdFallbackReason.Pending); break;
                default: ready[query] = new(null, HdFallbackReason.Pending); break;
            }
        }
        // Decoded pixels nobody draws now are dropped (decoded again if they come back).
        source.ReleaseReadyExcept(admitted.Keys.ToHashSet());
        for (var index = 0; index < draws.Count; index++)
            if (draws[index].Query is { } query && ready.TryGetValue(query, out var binding) && bindings[index].Reason is null) bindings[index] = binding;
        VisibleFrames = admitted.Count(pair => ready.TryGetValue(pair.Key, out var b) && b.IsHd);
        VisibleBytes = admitted.Where(pair => ready.TryGetValue(pair.Key, out var b) && b.IsHd).Sum(pair => pair.Value);
        return bindings;
    }

    /// <summary>A switch to Legacy: nothing is visible, HD textures are kept down to the
    /// retain limits.</summary>
    public void ReleaseToRetained(HdFrameSource source) => Submit(Array.Empty<HdDraw>(), source);

    /// <summary>Releases every HD texture (pack change, map change, reload, exit).</summary>
    public void Clear()
    {
        foreach (var key in _resident.Keys.ToArray()) Evict(key);
        VisibleFrames = 0; VisibleBytes = 0; UploadsDeferred = false;
    }

    private void Evict(HdSpriteQuery key)
    {
        var entry = _resident[key];
        _resident.Remove(key); Bytes -= entry.Bytes;
        _dispose(entry.Texture); DisposedCount++;
    }

    public void Dispose() => Clear();
}
