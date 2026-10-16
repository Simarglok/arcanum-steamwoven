using Arcanum.Data.Hd;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What the frame source knows about one HD question right now.</summary>
public enum HdSourceState { Unknown, Pending, Ready, Refused }
/// <param name="Rgba">Exactly <c>Scale·w × Scale·h × 4</c> bytes when <see cref="State"/> is Ready.</param>
public readonly record struct HdSourceStatus(HdSourceState State, HdFallbackReason? Reason = null, byte[]? Rgba = null);

/// <summary>The presentation layer's frame source for one active pack (ARC-RENDER-013 Frame
/// binding). It resolves HD frames off the main thread through <see cref="HdPack.ResolveSprite"/>
/// (file, hash, PNG, size and mask checks against this device's Legacy frame), keeps each
/// refusal as a per-frame reason, and holds decoded pixels only until the caller has uploaded
/// them (<see cref="Release"/>), so there is no whole-area HD CPU repository. Without a pack
/// every question is <see cref="HdFallbackReason.NoPack"/>. Safe for concurrent use; the
/// caller polls <see cref="CompletedCount"/> to learn that pending work finished.</summary>
public sealed class HdFrameSource : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<HdSpriteQuery, Slot> _slots = new();
    private readonly SemaphoreSlim _workers;
    private CancellationTokenSource _cancel = new();
    private long _completed;
    private bool _disposed;

    private sealed class Slot
    {
        public HdSourceState State;
        public HdFallbackReason? Reason;
        public byte[]? Rgba;
    }

    /// <param name="pack">The opened pack, or null (every frame <see cref="HdFallbackReason.NoPack"/>).</param>
    /// <param name="parallelism">Concurrent decodes; 0 picks half the processors (at least 1).</param>
    public HdFrameSource(HdPack? pack, int parallelism = 0)
    {
        if (parallelism < 0) throw new ArgumentOutOfRangeException(nameof(parallelism));
        Pack = pack;
        _workers = new SemaphoreSlim(parallelism > 0 ? parallelism : Math.Max(1, Environment.ProcessorCount / 2));
    }

    public HdPack? Pack { get; }
    /// <summary>The pack scale N, or 1 without a pack.</summary>
    public int Scale => Pack?.Scale ?? 1;
    /// <summary>Resolutions finished since creation (Ready or Refused); a change means a
    /// resubmission may now bind frames that were Pending.</summary>
    public long CompletedCount => Interlocked.Read(ref _completed);
    /// <summary>Questions being decoded or decoded and not yet released.</summary>
    public int PendingCount { get { lock (_gate) return _slots.Values.Count(s => s.State == HdSourceState.Pending); } }
    public int ReadyCount { get { lock (_gate) return _slots.Values.Count(s => s.State == HdSourceState.Ready); } }
    /// <summary>Decoded HD bytes held until upload.</summary>
    public long ReadyBytes { get { lock (_gate) return _slots.Values.Sum(s => (long)(s.Rgba?.Length ?? 0)); } }

    /// <summary>The current state without starting work. Without a pack: Refused NoPack; a
    /// rejected pack: Refused PackRejected.</summary>
    public HdSourceStatus Peek(HdSpriteQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Pack is null) return new(HdSourceState.Refused, HdFallbackReason.NoPack);
        if (Pack.Review == HdReviewStatus.Rejected) return new(HdSourceState.Refused, HdFallbackReason.PackRejected);
        lock (_gate)
            return _slots.TryGetValue(query, out var slot) ? new(slot.State, slot.Reason, slot.Rgba) : new(HdSourceState.Unknown);
    }

    /// <summary>The current state, starting a decode when the question is new.
    /// <paramref name="legacyRgba"/> is called once, on this thread, only to start one: it
    /// returns this installation's decoded Legacy frame (w × h × 4 bytes hashing to the
    /// query), the reference of the mask check.</summary>
    public HdSourceStatus Request(HdSpriteQuery query, Func<byte[]> legacyRgba)
    {
        ArgumentNullException.ThrowIfNull(legacyRgba);
        var known = Peek(query);
        if (known.State != HdSourceState.Unknown) return known;
        byte[] legacy;
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_slots.TryGetValue(query, out var raced)) return new(raced.State, raced.Reason, raced.Rgba);
            legacy = legacyRgba();
            token = _cancel.Token;
            _slots[query] = new Slot { State = HdSourceState.Pending };
        }
        var pack = Pack!;
        _ = Task.Run(async () =>
        {
            HdSourceStatus outcome;
            try
            {
                await _workers.WaitAsync(token).ConfigureAwait(false);
                try { outcome = Resolve(pack, query, legacy, token); }
                finally { _workers.Release(); }
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            lock (_gate)
            {
                // A reset or dispose dropped this question; its result belongs to nobody.
                if (token.IsCancellationRequested || !_slots.TryGetValue(query, out var slot) || slot.State != HdSourceState.Pending) return;
                slot.State = outcome.State; slot.Reason = outcome.Reason; slot.Rgba = outcome.Rgba;
            }
            Interlocked.Increment(ref _completed);
        });
        return new(HdSourceState.Pending);
    }

    private static HdSourceStatus Resolve(HdPack pack, HdSpriteQuery query, byte[] legacy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        HdFrameResult result;
        // A query that contradicts its own pixels is a caller defect; it still only costs
        // this frame its HD texture, never the view.
        try { result = pack.ResolveSprite(query, legacy); }
        catch (ArgumentException) { return new(HdSourceState.Refused, HdFallbackReason.Invalid); }
        return result.Status switch
        {
            HdFrameStatus.Hd => new(HdSourceState.Ready, null, result.Rgba),
            HdFrameStatus.PackRejected => new(HdSourceState.Refused, HdFallbackReason.PackRejected),
            HdFrameStatus.FrameMissing => new(HdSourceState.Refused, HdFallbackReason.FrameMissing),
            HdFrameStatus.SourceMismatch => new(HdSourceState.Refused, HdFallbackReason.SourceMismatch),
            HdFrameStatus.FrameRejected => new(HdSourceState.Refused, HdFallbackReason.FrameRejected),
            _ => new(HdSourceState.Refused, HdFallbackReason.Invalid),
        };
    }

    /// <summary>Drops the decoded pixels of a Ready question (uploaded, or no longer wanted);
    /// a later request decodes it again. Refusals and pending work are kept.</summary>
    public void Release(HdSpriteQuery query)
    {
        lock (_gate)
            if (_slots.TryGetValue(query, out var slot) && slot.State == HdSourceState.Ready) _slots.Remove(query);
    }

    /// <summary>Drops the pixels of every Ready question not in <paramref name="keep"/>.</summary>
    public void ReleaseReadyExcept(IReadOnlySet<HdSpriteQuery> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        lock (_gate)
            foreach (var query in _slots.Where(pair => pair.Value.State == HdSourceState.Ready && !keep.Contains(pair.Key)).Select(pair => pair.Key).ToArray())
                _slots.Remove(query);
    }

    /// <summary>Cancels pending work and forgets everything (map change, reload, exit): no
    /// staging buffer outlives it.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _cancel.Cancel(); _cancel.Dispose(); _cancel = new CancellationTokenSource();
            _slots.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cancel.Cancel(); _cancel.Dispose();
            _slots.Clear();
        }
    }
}
