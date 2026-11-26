namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-RENDER-3416: which pool slot's kept state each slot of a new submission takes. A command is known by its key and keeps the state it
/// had in the previous submission wherever it moved in the draw order; the rest of the states go to the other slots, a slot's own state first.
/// The answer is always a permutation of the slots, so every state stays with exactly one slot. The buffers are kept between submissions.
/// </summary>
public sealed class AreaSlotAssignment<TKey> where TKey : struct, IEquatable<TKey>
{
    // The slot that held each key after the previous submission.
    private readonly Dictionary<TKey, int> _held = new();
    private int[] _source = [];
    private bool[] _taken = [];

    /// <summary>
    /// Answers, for each of <paramref name="slots"/> slots, the previous slot whose state it takes now; slot i below the count of
    /// <paramref name="wanted"/> draws <c>wanted[i]</c> next. A key drawn twice keeps one of its states. Slots dropped since the previous
    /// call (<paramref name="slots"/> smaller) are forgotten. The answer is valid until the next call.
    /// </summary>
    public ReadOnlySpan<int> Assign(IReadOnlyList<TKey> wanted, int slots)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentOutOfRangeException.ThrowIfLessThan(slots, wanted.Count);
        if (_source.Length < slots)
        {
            var size = Math.Max(slots, 2 * _source.Length);
            _source = new int[size]; _taken = new bool[size];
        }
        Array.Clear(_taken, 0, slots);
        var count = wanted.Count;
        for (var index = 0; index < count; index++)
        {
            if (_held.Remove(wanted[index], out var from) && from < slots) { _source[index] = from; _taken[from] = true; }
            else _source[index] = -1;
        }
        // The slots without a match take their own state when it is free, else the lowest free one.
        for (var index = 0; index < count; index++)
            if (_source[index] < 0 && !_taken[index]) { _source[index] = index; _taken[index] = true; }
        var free = 0;
        for (var index = 0; index < slots; index++)
        {
            if (index < count && _source[index] >= 0) continue;
            var from = index >= count && !_taken[index] ? index : -1;
            if (from < 0)
            {
                while (_taken[free]) free++;
                from = free;
            }
            _source[index] = from; _taken[from] = true;
        }
        _held.Clear();
        for (var index = 0; index < count; index++) _held.TryAdd(wanted[index], index);
        return _source.AsSpan(0, slots);
    }

    /// <summary>Forgets every key (the slots were dropped).</summary>
    public void Reset() => _held.Clear();
}
