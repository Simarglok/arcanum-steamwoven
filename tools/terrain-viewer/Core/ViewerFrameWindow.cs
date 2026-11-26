using System.Globalization;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-3128: what <see cref="ViewerFrameWindow"/> saw over its window: how many frames, the span they cover and the frame
/// cost (average, 95th percentile, maximum, in ms) and the collections per second of each generation over that span.</summary>
public readonly record struct ViewerFrameStats(int Frames, double SpanSeconds, double AverageMs, double P95Ms, double MaxMs, double Gen0PerSecond, double Gen1PerSecond, double Gen2PerSecond, double WorkAverageMs = 0, double WorkMaxMs = 0)
{
    /// <summary>The stats as the resource line's tail: <c>frameMs=avg/p95/max gcRate=g0/g1/g2 over=Ns</c> (the collections per second of generation 0, 1 and 2).</summary>
    public string Text()
    {
        var c = CultureInfo.InvariantCulture;
        return $"frameMs={AverageMs.ToString("0.0", c)}/{P95Ms.ToString("0.0", c)}/{MaxMs.ToString("0.0", c)}"
            + $" gcRate={Gen0PerSecond.ToString("0.00", c)}/{Gen1PerSecond.ToString("0.00", c)}/{Gen2PerSecond.ToString("0.00", c)}/s over={SpanSeconds.ToString("0", c)}s";
    }
}

/// <summary>
/// ARC-DEV-3128: the viewer's recent frame cost. Godot's <c>fps</c> monitor is the last second's count, so the bug report's Resources line
/// said fps=23 where the true median frame was 47 fps; this keeps every frame of the last <see cref="WindowSeconds"/> seconds (the gap since
/// the previous frame and the collection counts at that frame) in a fixed ring, so a reading can give the average, 95th percentile and
/// maximum frame time and the collections per second. Recording allocates nothing; the sort for the percentile uses one scratch array
/// that is allocated once (at the first reading) and reused. Not thread safe: the viewer's main thread records and reads.
/// </summary>
public sealed class ViewerFrameWindow
{
    /// <summary>How long a frame stays in the window, in seconds (the period of the resource line).</summary>
    public const int WindowSeconds = 30;
    /// <summary>The ring's size; a frame rate above this over <see cref="WindowSeconds"/> keeps only the newest frames (the span says how many seconds that is).</summary>
    public const int Capacity = 8192;

    private readonly long _windowMicros;
    private readonly long[] _atMicros;
    private readonly float[] _frameMs, _workMs;
    private readonly int[] _gen0, _gen1, _gen2;
    private float[]? _scratch;
    private int _head;   // the oldest kept frame
    private int _count;
    private long _previousMicros = -1;

    public ViewerFrameWindow(int windowSeconds = WindowSeconds, int capacity = Capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _windowMicros = windowSeconds * 1_000_000L;
        _atMicros = new long[capacity]; _frameMs = new float[capacity]; _workMs = new float[capacity];
        _gen0 = new int[capacity]; _gen1 = new int[capacity]; _gen2 = new int[capacity];
    }

    /// <summary>How many frames the window holds now.</summary>
    public int Count => _count;

    /// <summary>Notes a frame that began at <paramref name="nowMicros"/> (a monotonic clock) with the process's collection counts; its cost is
    /// the time since the previous call (the first call only sets the start). Frames older than the window are dropped.
    /// <paramref name="workMs"/> (ARC-DEV-3389) is the time the caller itself worked in the frame, 0 where it is not measured.</summary>
    public void Record(long nowMicros, int gen0, int gen1, int gen2, float workMs = 0)
    {
        var previous = _previousMicros;
        _previousMicros = nowMicros;
        if (previous < 0) return;
        if (nowMicros < previous) { _count = 0; return; } // a clock that went back: start the window afresh (the next frame is measured from this stamp)
        var capacity = _atMicros.Length;
        // The newest kept frame stays even when it is older than the window, so a stall longer than the window is still read (as the span between two frames).
        while (_count > 1 && nowMicros - _atMicros[_head] > _windowMicros) { _head = (_head + 1) % capacity; _count--; }
        if (_count == capacity) { _head = (_head + 1) % capacity; _count--; }
        var slot = (_head + _count) % capacity;
        _atMicros[slot] = nowMicros; _frameMs[slot] = (nowMicros - previous) / 1000f; _workMs[slot] = workMs;
        _gen0[slot] = gen0; _gen1[slot] = gen1; _gen2[slot] = gen2;
        _count++;
    }

    /// <summary>The stats of the frames held, or null when fewer than two frames are held (no span to put a rate on).</summary>
    public ViewerFrameStats? Read()
    {
        if (_count < 2) return null;
        var capacity = _atMicros.Length;
        var scratch = _scratch ??= new float[capacity];
        double sum = 0, workSum = 0; float max = 0, workMax = 0;
        for (var i = 0; i < _count; i++)
        {
            var value = _frameMs[(_head + i) % capacity];
            scratch[i] = value; sum += value; if (value > max) max = value;
            var work = _workMs[(_head + i) % capacity];
            workSum += work; if (work > workMax) workMax = work;
        }
        Array.Sort(scratch, 0, _count);
        var rank = (int)Math.Ceiling(0.95 * _count) - 1; // nearest rank
        var first = _head; var last = (_head + _count - 1) % capacity;
        // The span is first frame's start to last frame's start: the first kept frame's own cost lies before its stamp, so it is left out of the rate.
        var span = (_atMicros[last] - _atMicros[first]) / 1_000_000.0;
        if (span <= 0) return null;
        return new ViewerFrameStats(_count, span, sum / _count, scratch[rank], max,
            (_gen0[last] - _gen0[first]) / span, (_gen1[last] - _gen1[first]) / span, (_gen2[last] - _gen2[first]) / span, workSum / _count, workMax);
    }
}
