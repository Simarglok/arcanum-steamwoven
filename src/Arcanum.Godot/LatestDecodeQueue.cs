namespace Arcanum.Godot;

// A deliberately small latest-only state machine for the viewer's bounded decode pipeline.
// It has no Godot dependency so the deterministic self-test can exercise active/pending
// transitions without timing or worker-thread races.
internal sealed class LatestDecodeQueue<TRequest> where TRequest : class
{
    private TRequest? _active;
    private TRequest? _pending;
    private TRequest? _failed;

    internal TRequest? Active => _active;
    internal TRequest? Pending => _pending;
    internal TRequest? Failed => _failed;

    internal bool TryStart(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Equals(_active, request) || Equals(_pending, request) || Equals(_failed, request))
        {
            return false;
        }

        if (_active is not null)
        {
            _pending = request;
            return false;
        }

        _active = request;
        return true;
    }

    internal void InvalidatePending()
    {
        _pending = null;
        _failed = null;
    }

    internal void MarkFailed(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _failed = request;
    }

    internal TRequest? Complete(TRequest completed, Func<TRequest, bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(isCurrent);
        if (!Equals(_active, completed))
        {
            return null;
        }

        _active = null;
        var next = _pending;
        _pending = null;
        if (next is null || !isCurrent(next))
        {
            return null;
        }

        _active = next;
        return next;
    }
}
