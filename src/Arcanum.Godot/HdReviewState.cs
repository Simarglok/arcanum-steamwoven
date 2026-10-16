using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Hd;

namespace Arcanum.Godot;

/// <summary>How the before/after review shows the current frame: one of the two, an
/// automatic alternation (for the demo clip) or both side by side.</summary>
internal enum HdViewMode { Legacy, Hd, Flip, Split }

/// <summary>One background lookup: which pack frame is the shown ART frame, and its pair.</summary>
internal sealed record HdReviewRequest(long Generation, HdReviewIndex Index, SelectedArtSnapshot Snapshot, string? ArtSha256, int Group, int Frame, int Palette);

/// <param name="Entry">The pack frame of the shown ART frame, or null when the pack has none.</param>
/// <param name="Frame">The before/after pair (null when there is no entry).</param>
/// <param name="Error">An unexpected failure of the lookup (its exception type); nothing else is set.</param>
/// <param name="Cached">The entry's pair is already in the viewer's texture cache, so it
/// was not compared again (<see cref="Frame"/> is null).</param>
internal sealed record HdReviewResult(HdReviewRequest Request, string ArtSha256, HdPackEntry? Entry, HdReviewFrame? Frame, string? Error = null, bool Cached = false);

/// <summary>Godot-free state of the ARC-RENDER-015 review: the opened pack and its index,
/// the view mode and flip clock, and at most one background comparison (plus one latest
/// pending request), whose result is accepted only while its generation is current. The
/// ART payload hash of the selected entry is computed once per selection.</summary>
internal sealed class HdReviewState
{
    private readonly object _gate = new();
    private Task<HdReviewResult>? _running;
    private HdReviewRequest? _pending;
    private long _generation;
    private (SelectedArtSnapshot Snapshot, string Sha)? _artSha;
    private double _flipElapsed;

    internal HdReviewIndex? Index { get; private set; }
    internal HdPack? Pack => Index?.Pack;
    internal HdViewMode Mode { get; private set; } = HdViewMode.Legacy;
    internal double FlipSeconds { get; private set; } = 1.0;
    /// <summary>In <see cref="HdViewMode.Flip"/>: whether HD is the side shown now.</summary>
    internal bool FlipShowsHd { get; private set; }
    /// <summary>The result for the frame on screen (null while none has arrived).</summary>
    internal HdReviewResult? Current { get; private set; }
    internal long Generation => Interlocked.Read(ref _generation);

    internal static string NameSha256(DatEntry entry) => Convert.ToHexString(SHA256.HashData(entry.RawNameBytes.ToArray())).ToLowerInvariant();

    internal void SetPack(HdPack? pack)
    {
        Index = pack is null ? null : new HdReviewIndex(pack);
        Invalidate();
    }

    /// <summary>After a review save: same entries and pixels, new marks. The shown result
    /// keeps its pair with the entry's new mark.</summary>
    internal void ReplacePack(HdPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        Index = new HdReviewIndex(pack);
        if (Current is { Entry: { } entry } current)
            Current = current with { Entry = pack.Entries[entry.Index], Frame = current.Frame is null ? null : current.Frame with { Entry = pack.Entries[entry.Index] } };
    }

    internal void SetMode(HdViewMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        _flipElapsed = 0;
        // A flip starts on the original, then shows HD: before, then after.
        FlipShowsHd = false;
    }

    internal void SetFlipSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0.1 || seconds > 60) throw new ArgumentOutOfRangeException(nameof(seconds), "Flip period must be 0.1..60 seconds.");
        FlipSeconds = seconds;
    }

    /// <summary>Advances the flip clock; true when the shown side changed.</summary>
    internal bool AdvanceFlip(double delta)
    {
        if (Mode != HdViewMode.Flip || !double.IsFinite(delta) || delta <= 0) return false;
        _flipElapsed += delta;
        if (_flipElapsed < FlipSeconds) return false;
        _flipElapsed %= FlipSeconds;
        FlipShowsHd = !FlipShowsHd;
        return true;
    }

    /// <summary>The canvas view for the current mode and flip side.</summary>
    internal FrameView CanvasView => Mode switch
    {
        HdViewMode.Hd => FrameView.Hd,
        HdViewMode.Split => FrameView.Split,
        HdViewMode.Flip => FlipShowsHd ? FrameView.Hd : FrameView.Legacy,
        _ => FrameView.Legacy,
    };

    /// <summary>Forgets the shown frame and any running result (a new frame, ART, DAT or pack).</summary>
    internal void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate) _pending = null;
        Current = null;
    }

    /// <summary>Asks for the pack frame of the shown frame. Without a pack this only clears
    /// the shown result.</summary>
    internal void Request(SelectedArtSnapshot snapshot, int group, int frame, int palette)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Invalidate();
        if (Index is not { } index) return;
        var sha = _artSha is { } known && ReferenceEquals(known.Snapshot, snapshot) ? known.Sha : null;
        var request = new HdReviewRequest(Generation, index, snapshot, sha, group, frame, palette);
        lock (_gate)
        {
            if (_running is null) _running = Task.Run(() => Execute(request));
            else _pending = request;
        }
    }

    /// <summary>Main thread, without waiting: when the selected ART's hash is known and its
    /// frame either has no pack entry or an entry whose pair <paramref name="isCached"/>,
    /// that answer becomes <see cref="Current"/> at once (smooth HD playback). Otherwise null:
    /// use <see cref="Request"/>.</summary>
    internal HdReviewResult? TryCached(SelectedArtSnapshot snapshot, int group, int frame, int palette, Func<HdPackEntry, bool> isCached)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(isCached);
        if (Index is not { } index || _artSha is not { } known || !ReferenceEquals(known.Snapshot, snapshot)) return null;
        var entry = index.Find(known.Sha, snapshot.Bytes.Length, group, frame, palette);
        if (entry is not null && !isCached(entry)) return null;
        Invalidate();
        var request = new HdReviewRequest(Generation, index, snapshot, known.Sha, group, frame, palette);
        return Current = new HdReviewResult(request, known.Sha, entry, null, Cached: entry is not null);
    }

    /// <summary>Main thread: takes a finished result. Returns it only when it belongs to the
    /// current generation (it then becomes <see cref="Current"/>); starts the pending request.</summary>
    internal HdReviewResult? Poll()
    {
        Task<HdReviewResult>? finished;
        lock (_gate)
        {
            if (_running is not { IsCompleted: true }) return null;
            finished = _running;
            _running = _pending is { } next ? Task.Run(() => Execute(next)) : null;
            _pending = null;
        }
        if (!finished.IsCompletedSuccessfully) return null;
        var result = finished.Result;
        _artSha = (result.Request.Snapshot, result.ArtSha256);
        if (result.Request.Generation != Generation || !ReferenceEquals(result.Request.Index, Index)) return null;
        Current = result;
        return result;
    }

    /// <summary>Self-test only: polls until a current result arrives or the timeout passes.</summary>
    internal HdReviewResult? WaitForResult(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Poll() is { } result) return result;
            Thread.Sleep(2);
        }
        return null;
    }

    private static HdReviewResult Execute(HdReviewRequest request)
    {
        var bytes = request.Snapshot.Bytes;
        var sha = request.ArtSha256 ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        try
        {
            var entry = request.Index.Find(sha, bytes.Length, request.Group, request.Frame, request.Palette);
            var frame = entry is null ? null : HdReviewComparison.Compare(request.Index.Pack, entry, bytes, sha, ViewerState.DecoderOptions);
            return new(request, sha, entry, frame);
        }
        catch (Exception error)
        {
            return new(request, sha, null, null, error.GetType().Name);
        }
    }
}
