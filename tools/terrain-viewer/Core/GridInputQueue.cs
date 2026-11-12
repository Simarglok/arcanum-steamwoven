using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Collect complete observation timestamps before closing a runtime watermark.</summary>
public sealed class GridInputQueue
{
    private readonly SortedDictionary<SimulationTime, List<MovementInput>> _pending = new(Comparer<SimulationTime>.Create((a,b)=>a.Value.CompareTo(b.Value)));
    private long _sequence;
    private ActorIntent _lastIntent = ActorIntent.Neutral;
    private IGridSession? _owner;
    private SimulationTime? _newest;
    public SimulationTime? CompleteFrontier { get; private set; }
    /// <summary>The newest observation time seen so far (null before the first); a later observation must not be older.</summary>
    public SimulationTime? Newest => _newest;
    /// <summary>Whether any observed material input still waits, including inputs after the complete frontier.</summary>
    public bool HasPending => _pending.Count > 0;
    public void Observe(IGridSession session, SimulationTime time, ActorIntent intent)
    {
        if (_owner is not null && !ReferenceEquals(_owner, session))
            throw new InvalidOperationException("GRID_OBSERVATION_RETIRED_SESSION");
        if (!time.IsValid || time.Value < session.State.Time.Value)
            throw new InvalidOperationException("GRID_OBSERVATION_LATE_OR_INVALID");
        if (_newest is { } newest && time.Value < newest.Value)
            throw new InvalidOperationException("GRID_OBSERVATION_NONMONOTONIC");
        var material = intent != _lastIntent;
        if (material && session.State.IsClosed && time.Value == session.State.Time.Value)
            throw new InvalidOperationException("GRID_OBSERVATION_CLOSED_TIMESTAMP");
        if (material && (_pending.Sum(p=>p.Value.Count) >= session.State.Limits.MaxInputs || _sequence == long.MaxValue))
            throw new InvalidOperationException("GRID_OBSERVATION_CAPACITY");
        // A strictly newer monotonic receipt proves that the older tick is
        // complete. Never close the newest tick: more callbacks can share it.
        _owner = session;
        if (_newest is { } prior && time.Value > prior.Value) CompleteFrontier = prior;
        _newest = time;
        if (!material) return;
        if (!_pending.TryGetValue(time, out var inputs)) _pending.Add(time, inputs = []);
        inputs.Add(MovementInput.Set(_sequence, intent)); _sequence = checked(_sequence+1); _lastIntent = intent;
    }
    public InputBatch[] CompleteThrough(SimulationTime time) => _pending.Where(p=>p.Key.Value<=time.Value).Select(p=>new InputBatch(p.Key,p.Value.ToArray())).ToArray();
    public void PublishedThrough(SimulationTime time)
    {
        foreach(var key in _pending.Keys.Where(t=>t.Value<=time.Value).ToArray()) _pending.Remove(key);
    }
}
