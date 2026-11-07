namespace Arcanum.TerrainViewer.Core;

/// <summary>What one frame of the town walk chain did.</summary>
public enum AreaTownWalkTick { Idle, Waiting, Replaced, EndedShort, Started, Finished }

/// <summary>
/// ARC-UI-092 (ledger WM107, WM108, WM123, WM124): the PC's walk through the town waypoints. Travel starts the walk to the first waypoint as a ground click does and
/// the others follow it one walk after another (the original queues a walk goal per waypoint on the PC's goal stack and plays them in order). What a failed or replaced
/// walk does to the goals beneath it is not read (F0), so the project's choice: a walk that was replaced (a click, the stick) or ended short of its tile (stopped,
/// blocked) drops the rest. A waypoint whose walk the host refuses is skipped, so the next one starts a fresh walk (WM108). Godot-free; the host asks each frame.
/// </summary>
public sealed class AreaTownWalkChain<T> where T : struct, IEquatable<T>
{
    private readonly Queue<T> _queue = new();
    private T? _expected;

    /// <summary>The waypoints still to be walked after the one under way.</summary>
    public int Pending => _queue.Count;
    /// <summary>The tile the walk under way was asked to reach.</summary>
    public T? Expected => _expected;
    public bool Active => _queue.Count > 0 || _expected is not null;

    /// <summary>Travel: the first walk was asked for (<paramref name="firstAccepted"/> false when the host refused it); the rest wait.</summary>
    public void Begin(T first, bool firstAccepted, IEnumerable<T> rest)
    {
        Clear();
        _expected = firstAccepted ? first : null;
        foreach (var cell in rest) _queue.Enqueue(cell);
    }

    public void Clear() { _queue.Clear(); _expected = null; }

    /// <summary>
    /// One frame. <paramref name="walkGoal"/> is the goal of the PC's walk under way (null when it walks none), <paramref name="standingAt"/> the tile the PC stands on and
    /// <paramref name="start"/> asks the host for a walk to a tile (true when it began).
    /// </summary>
    public AreaTownWalkTick Tick(T? walkGoal, T standingAt, Func<T, bool> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (!Active) return AreaTownWalkTick.Idle;
        if (walkGoal is { } goal)
        {
            if (_expected is { } expected && !goal.Equals(expected)) { Clear(); return AreaTownWalkTick.Replaced; }
            return AreaTownWalkTick.Waiting;
        }
        if (_expected is { } wanted && !standingAt.Equals(wanted)) { Clear(); return AreaTownWalkTick.EndedShort; }
        _expected = null;
        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (!start(next)) continue;
            _expected = next;
            return AreaTownWalkTick.Started;
        }
        return AreaTownWalkTick.Finished;
    }
}
