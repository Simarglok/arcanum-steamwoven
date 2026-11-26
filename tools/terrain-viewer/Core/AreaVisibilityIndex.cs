using System.Numerics;
using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>A coarse world grid over draw-command destinations that returns, for a view
/// rectangle, a superset of the commands whose destination meets it, in ascending command
/// order. Callers keep their own exact intersection test, so drawing order and the
/// visible set are unchanged; the index only avoids scanning every command when the view
/// covers a small part of the area. A view spanning more grid cells (empty ones included)
/// than <see cref="ScanFraction"/> of the occupied cells returns every command, which is
/// then cheaper than merging cells.</summary>
public sealed class AreaVisibilityIndex
{
    public const int CellSize = 512;
    public const double ScanFraction = 0.5;
    private readonly Dictionary<(int X, int Y), int[]> _cells;
    private readonly int _count;
    private readonly int[] _all;
    // ARC-RENDER-3418: a moving view asks every frame, so the marks and the answer are reused.
    private readonly ulong[] _marks;
    private readonly List<int> _found = [];

    public AreaVisibilityIndex(IReadOnlyList<TerrainRect> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        _count = destinations.Count; _all = Enumerable.Range(0, _count).ToArray();
        _marks = new ulong[(_count + 63) / 64];
        var cells = new Dictionary<(int X, int Y), List<int>>();
        for (var index = 0; index < destinations.Count; index++)
        {
            var d = destinations[index];
            // Inclusive of the far edge plus one cell: the index may over-report, never under-report,
            // even where a float view rectangle rounds a far coordinate past the exact integer end.
            var (x0, x1) = (FloorDiv(d.X), FloorDiv((long)d.X + d.Width) + 1); var (y0, y1) = (FloorDiv(d.Y), FloorDiv((long)d.Y + d.Height) + 1);
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                if (!cells.TryGetValue((x, y), out var list)) cells.Add((x, y), list = []);
                list.Add(index);
            }
        }
        _cells = cells.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public int Count => _count;
    public int CellCount => _cells.Count;

    /// <summary>Ascending indices of the commands that may meet [left, right] x [top, bottom]. The list is the index's own:
    /// it holds until the next call, and calls are made by one thread.</summary>
    public IReadOnlyList<int> Candidates(double left, double top, double right, double bottom)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom)) return _all;
        if (right < left || bottom < top) return Array.Empty<int>();
        // Outside the int grid range nothing is skipped: fall back to every command.
        if (Math.Abs(left) > int.MaxValue / 2.0 || Math.Abs(right) > int.MaxValue / 2.0 || Math.Abs(top) > int.MaxValue / 2.0 || Math.Abs(bottom) > int.MaxValue / 2.0) return _all;
        var (x0, x1) = (FloorDiv((long)Math.Floor(left)), FloorDiv((long)Math.Floor(right)));
        var (y0, y1) = (FloorDiv((long)Math.Floor(top)), FloorDiv((long)Math.Floor(bottom)));
        if ((double)(x1 - x0 + 1) * (y1 - y0 + 1) > ScanFraction * _cells.Count) return _all;
        int first = _marks.Length, last = -1;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            if (_cells.TryGetValue((x, y), out var list))
            {
                foreach (var index in list) _marks[index >> 6] |= 1UL << index;
                // A cell's list is ascending and never empty.
                first = Math.Min(first, list[0] >> 6); last = Math.Max(last, list[^1] >> 6);
            }
        _found.Clear();
        for (var word = first; word <= last; word++)
        {
            for (var bits = _marks[word]; bits != 0; bits &= bits - 1) _found.Add((word << 6) + BitOperations.TrailingZeroCount(bits));
            _marks[word] = 0;
        }
        return _found;
    }

    private static int FloorDiv(long value) => (int)Math.Floor(value / (double)CellSize);
}
