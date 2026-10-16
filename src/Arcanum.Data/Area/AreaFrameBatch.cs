namespace Arcanum.Data.Area;

public sealed partial class AreaDataOwner
{
    private AreaFrameBatch? _batch;
    private readonly HashSet<AreaInputKey> _batchAuthenticated = [];

    /// <summary>Starts one serialized multi-frame copy of this owner's current publication.
    /// Each source is whole-input authenticated before its first frame in the batch and once
    /// more by <see cref="AreaFrameBatch.Complete"/>; every frame keeps the lease's entry,
    /// payload, geometry, palette and pixel identity checks. Copies are trustworthy only after
    /// Complete returns. The batch occupies the single lease slot until disposed.</summary>
    public AreaFrameBatch BeginFrameBatch(AreaSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            Alive(); cancellationToken.ThrowIfCancellationRequested();
            RequirePublished(snapshot);
            if (_lease is not null || _batch is not null) throw Fail(AreaErrorCode.LeaseLimit, AreaStage.Frame);
            _batchAuthenticated.Clear();
            _batch = new AreaFrameBatch(this, snapshot);
            return _batch;
        }
    }

    internal byte[] BatchCopy(AreaFrameBatch batch, AreaVisualKey visual, CancellationToken token)
    {
        lock (_gate)
        {
            Alive(); RequireBatch(batch);
            // Any failure, cancellation included, faults the whole batch: no later copy or
            // Complete can follow a change that was observed, even if it is reverted.
            try
            {
                token.ThrowIfCancellationRequested();
                RequirePublished(batch.Snapshot);
                var descriptor = batch.Snapshot.Visuals.SingleOrDefault(v => v.Key == visual) ?? throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Frame);
                _budget.BeginPhase(AreaBudgetPhase.F);
                var source = descriptor.Entry.Source;
                ArtFrame frame; long held;
                try
                {
                    // First use of a source is authenticated inside the admitted frame,
                    // exactly as a lease authenticates before decoding.
                    frame = DecodeDescriptor(descriptor, token, authenticateBefore: !_batchAuthenticated.Contains(source), authenticateAfter: false);
                    _batchAuthenticated.Add(source);
                    token.ThrowIfCancellationRequested();
                    held = checked(frame.Rgba.Count + 8192L);
                    _budget.Hold(held);
                }
                finally { _budget.EndFrame(); }
                // Same accounting as AcquireSelectedFrame + CopyRgba + lease release.
                try
                {
                    _budget.BeginPhase(AreaBudgetPhase.C);
                    _budget.Context = new(AreaBudgetCallsite.ConsumerCopy);
                    _budget.Copy(frame.Rgba.Count);
                    return frame.Rgba.ToArray();
                }
                finally { _budget.Release(held); }
            }
            catch { batch.MarkFaulted(); throw; }
        }
    }

    internal void BatchComplete(AreaFrameBatch batch, CancellationToken token)
    {
        lock (_gate)
        {
            Alive(); RequireBatch(batch);
            try
            {
                token.ThrowIfCancellationRequested();
                RequirePublished(batch.Snapshot);
                _budget.BeginPhase(AreaBudgetPhase.F);
                foreach (var identity in _plan.Identities)
                    if (_batchAuthenticated.Contains(identity.Key)) Authenticate(identity, token);
                batch.MarkCompleted();
            }
            catch { batch.MarkFaulted(); throw; }
        }
    }

    internal void BatchRelease(AreaFrameBatch batch)
    {
        lock (_gate) { if (ReferenceEquals(_batch, batch)) { batch.Revoke(); _batch = null; _batchAuthenticated.Clear(); } }
    }

    private void RequirePublished(AreaSnapshot snapshot)
    {
        if (!ReferenceEquals(snapshot.Owner, _identity) || !ReferenceEquals(snapshot, _published) || snapshot.Generation != _generation)
            throw Fail(AreaErrorCode.SnapshotMismatch, AreaStage.Frame);
    }
    private void RequireBatch(AreaFrameBatch batch)
    {
        if (!ReferenceEquals(_batch, batch) || batch.IsCompleted || batch.IsFaulted) throw new ObjectDisposedException(nameof(AreaFrameBatch));
    }
}

/// <summary>One open multi-frame copy. Returned arrays are independent copies; a caller must
/// discard all of them unless <see cref="Complete"/> succeeds. Any failed copy or Complete
/// faults the batch, which then refuses further use. Disposal frees the lease slot.</summary>
public sealed class AreaFrameBatch : IDisposable
{
    private readonly AreaDataOwner _owner;
    private bool _revoked;
    internal AreaFrameBatch(AreaDataOwner owner, AreaSnapshot snapshot) { _owner = owner; Snapshot = snapshot; }
    internal AreaSnapshot Snapshot { get; }
    public bool IsCompleted { get; private set; }
    public bool IsFaulted { get; private set; }
    public byte[] CopySelectedFrame(AreaVisualKey visual, CancellationToken cancellationToken = default)
    { Live(); return _owner.BatchCopy(this, visual, cancellationToken); }
    public void Complete(CancellationToken cancellationToken = default) { Live(); _owner.BatchComplete(this, cancellationToken); }
    public void Dispose() => _owner.BatchRelease(this);
    internal void MarkCompleted() => IsCompleted = true;
    internal void MarkFaulted() => IsFaulted = true;
    internal void Revoke() => _revoked = true;
    private void Live() { if (_revoked) throw new ObjectDisposedException(nameof(AreaFrameBatch)); }
}
