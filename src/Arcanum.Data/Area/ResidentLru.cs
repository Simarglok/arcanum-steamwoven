namespace Arcanum.Data.Area;

/// <summary>
/// ARC-DEV-375: a thread-safe, process-resident least-recently-used store bounded by a total cost and an entry count. A <see cref="TryGet"/>
/// touches the entry; <see cref="Add"/> evicts from the least recently used end until both bounds hold, and refuses a value whose own
/// cost exceeds the cost bound. The load worker is the only writer in practice; the lock keeps overlapped reloads and tests safe.
/// </summary>
public sealed class ResidentLru<TKey, TValue> where TKey : notnull
{
    private sealed record Entry(TKey Key, TValue Value, long Cost);

    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _order = new(); // first = most recently used
    private long _cost, _hits, _misses;

    public ResidentLru(long maxCost, int maxCount)
    {
        if (maxCost <= 0) throw new ArgumentOutOfRangeException(nameof(maxCost));
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        MaxCost = maxCost; MaxCount = maxCount;
    }

    public long MaxCost { get; }
    public int MaxCount { get; }
    public int Count { get { lock (_gate) return _index.Count; } }
    public long Cost { get { lock (_gate) return _cost; } }
    /// <summary>Lookups that found an entry / did not, since construction or the last <see cref="Clear"/>.</summary>
    public (long Hits, long Misses) Lookups { get { lock (_gate) return (_hits, _misses); } }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _order.Remove(node); _order.AddFirst(node); _hits++;
                value = node.Value.Value; return true;
            }
            _misses++;
        }
        value = default!; return false;
    }

    /// <summary>Stores the value as the most recently used entry (replacing an equal key). A value costing more than <see cref="MaxCost"/> is not stored.</summary>
    public bool Add(TKey key, TValue value, long cost)
    {
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        lock (_gate)
        {
            if (_index.Remove(key, out var old)) { _order.Remove(old); _cost -= old.Value.Cost; }
            if (cost > MaxCost) return false;
            _index[key] = _order.AddFirst(new Entry(key, value, cost)); _cost += cost;
            while ((_cost > MaxCost || _index.Count > MaxCount) && _order.Last is { } last)
            {
                _order.RemoveLast(); _index.Remove(last.Value.Key); _cost -= last.Value.Cost;
            }
            return _index.ContainsKey(key);
        }
    }

    /// <summary>Empties the store and its counters.</summary>
    public void Clear() { lock (_gate) { _index.Clear(); _order.Clear(); _cost = 0; _hits = 0; _misses = 0; } }
}
