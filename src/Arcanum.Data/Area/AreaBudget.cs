namespace Arcanum.Data.Area;

// All counter mutations are committed only after every affected domain admits.
// Retained ownership is separate from cumulative allocation; releasing ownership
// never refunds real work. The owner serializes access to this object.
internal sealed class AreaBudget : IDatEntryAdmission
{
    private readonly AreaLoaderOptions _limits;
    private long _readReserved, _readActual, _managed, _phase, _retained, _requests, _stored, _decoded, _rgba, _encoded, _copies;
    private bool _frameActive;
    private long _frameAllocation;
    private long _frameHeld;
    private AreaBudgetPhase _phaseId = AreaBudgetPhase.O;
    private readonly long[] _visits = new long[11];
    private readonly long[] _cumulative = new long[11];
    private readonly long[] _highWater = new long[11];

    internal AreaBudget(AreaLoaderOptions limits) { _limits = limits; _visits[0] = 1; }
    internal AreaBudgetContext Context { get; set; } = new(AreaBudgetCallsite.Owner);
    internal AreaBudgetRejection? FirstRejection { get; private set; }
    internal AreaBudgetUsage Usage => new(_readReserved, _readActual, _managed, _phase, _retained, _requests, _stored, _decoded, _rgba, _encoded, _copies);
    internal IReadOnlyList<AreaBudgetPhaseUsage> History => Array.AsReadOnly(Enumerable.Range(0, 11)
        .Select(i => new AreaBudgetPhaseUsage((AreaBudgetPhase)i, _visits[i], _cumulative[i], _highWater[i])).ToArray());

    internal void BeginPhase(AreaBudgetPhase phase)
    {
        if (_frameActive) throw new InvalidOperationException("A component must unwind before its enclosing phase ends.");
        _phaseId = phase; _phase = 0;
        _visits[(int)phase] = checked(_visits[(int)phase] + 1);
    }
    private long Admit(AreaBudgetDomain domain, long used, long requested, long limit)
    {
        // Subtraction is safe only after validating both operands. Never perform
        // overflowing addition and reclassify it as malformed source data.
        if (used < 0 || requested < 0 || limit < 0 || used > limit || requested > limit - used)
        {
            FirstRejection ??= new(domain, used, requested, limit, _phaseId, Context,
                _frameActive, _frameAllocation, Usage, Usage);
            throw new AreaException(new(AreaErrorCode.BudgetExceeded, AreaStage.Input, Context.Offset, Context.SourceIndex));
        }
        return used + requested;
    }
    private (long Total, long Phase, long Component) Allocation(long bytes, bool component = false)
    {
        var total = Admit(AreaBudgetDomain.Managed, _managed, bytes, _limits.MaxManagedBytes);
        var phase = Admit(AreaBudgetDomain.Phase, _phase, bytes, _limits.MaxBuildPhaseBytes);
        var frame = _frameActive || component ? Admit(AreaBudgetDomain.Component, _frameActive ? _frameAllocation : 0, bytes, _limits.MaxFrameOperationBytes) : 0;
        return (total, phase, frame);
    }
    private void CommitAllocation((long Total, long Phase, long Component) next)
    {
        var delta = next.Total - _managed;
        _managed = next.Total; _phase = next.Phase;
        if (_frameActive) _frameAllocation = next.Component;
        var index = (int)_phaseId;
        _cumulative[index] = checked(_cumulative[index] + delta);
        _highWater[index] = Math.Max(_highWater[index], _phase);
    }
    internal void Reserve(long bytes) => CommitAllocation(Allocation(bytes));
    void IDatEntryAdmission.Admit(long bytes) => Reserve(bytes);
    internal void Retain(long bytes)
    {
        Own(bytes, bytes);
    }
    internal void Own(long allocated, long held)
    {
        var retained = Admit(AreaBudgetDomain.Retained, _retained, held, _limits.MaxRetainedBytes);
        var allocation = Allocation(allocated);
        CommitAllocation(allocation); _retained = retained;
    }
    // For a previously admitted graph whose allocation is already charged.
    internal void Hold(long bytes) => _retained = Admit(AreaBudgetDomain.Retained, _retained, bytes, _limits.MaxRetainedBytes);
    // Optional ownership (a bounded memo): holds only while a quarter of the retained domain
    // stays free for mandatory admissions after it, and never records a rejection.
    internal bool TryHold(long bytes)
    {
        var reserve = _limits.MaxRetainedBytes / 4;
        if (bytes < 0 || _retained < 0 || _retained > _limits.MaxRetainedBytes || bytes > _limits.MaxRetainedBytes - reserve - _retained) return false;
        _retained += bytes; return true;
    }
    internal void Release(long bytes)
    {
        if (bytes < 0 || bytes > _retained) throw new InvalidOperationException("Invalid retained ownership release.");
        _retained -= bytes;
    }
    internal void ReadReserve(int requested) => _readReserved = Admit(AreaBudgetDomain.Read, _readReserved, requested, _limits.MaxReadBytes);
    internal void ReadActual(int actual, int requested)
    {
        if (actual < 0 || actual > requested) throw new IOException("The input stream returned an invalid byte count.");
        _readActual = Admit(AreaBudgetDomain.Read, _readActual, actual, _readReserved);
    }
    internal void StartFrame(long stored, long decoded, long rgba = 0, long encoded = 0)
    {
        if (_frameActive) throw new InvalidOperationException("Nested frame components are not supported.");
        var requests = Admit(AreaBudgetDomain.Requests, _requests, 1, _limits.MaxFrameRequests);
        var s = Admit(AreaBudgetDomain.Stored, _stored, stored, _limits.MaxFrameStoredBytes);
        var d = Admit(AreaBudgetDomain.Decoded, _decoded, decoded, _limits.MaxFrameDecodedBytes);
        var r = Admit(AreaBudgetDomain.Rgba, _rgba, rgba, _limits.MaxFrameRgbaBytes);
        var e = Admit(AreaBudgetDomain.Encoded, _encoded, encoded, _limits.MaxFrameEncodedBytes);
        var held = Admit(AreaBudgetDomain.Retained, _retained, _limits.MaxFrameOperationBytes, _limits.MaxRetainedBytes);
        _requests = requests; _stored = s; _decoded = d; _rgba = r; _encoded = e;
        _retained = held; _frameHeld = _limits.MaxFrameOperationBytes;
        _frameActive = true; _frameAllocation = 0;
    }
    internal void CompleteFrame(long rgba, long encoded)
    {
        var r = Admit(AreaBudgetDomain.Rgba, _rgba, rgba, _limits.MaxFrameRgbaBytes);
        var e = Admit(AreaBudgetDomain.Encoded, _encoded, encoded, _limits.MaxFrameEncodedBytes);
        var bytes = Admit(AreaBudgetDomain.Component, 256, rgba, long.MaxValue);
        var allocation = Allocation(bytes);
        CommitAllocation(allocation); _rgba = r; _encoded = e;
    }
    internal void CompleteFrameAllocation(long rgba) => Reserve(Admit(AreaBudgetDomain.Component, 256, rgba, long.MaxValue));
    internal void FrameAllocation(long bytes) => Reserve(bytes);
    internal void EndFrame()
    {
        Release(_frameHeld); _frameHeld = 0; _frameActive = false;
    }
    internal void Copy(int bytes)
    {
        var copies = Admit(AreaBudgetDomain.Copy, _copies, bytes, long.MaxValue);
        var allocation = Allocation(Admit(AreaBudgetDomain.Component, 32, bytes, long.MaxValue), component: true);
        CommitAllocation(allocation); _copies = copies;
    }
}
