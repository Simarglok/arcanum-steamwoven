using System.Diagnostics;
using System.Globalization;

namespace Arcanum.World;

/// <summary>ARC-DEV-367 (O10 of the performance diagnosis): the standing timing probe. Off unless <c>ARC_PERF_PROBE</c> is set (not
/// empty, not 0) or the smoke stage list names <c>perf</c> or <c>perf-menu</c> (<c>ARC_AREA_MAP_SMOKE_ONLY</c>); then each
/// <see cref="Mark"/>, <see cref="Span"/> and <see cref="Lap"/> prints one line starting <c>ARC-DEV-367 perf</c> with the name, the time
/// since the process started and, for a span, its milliseconds and the managed allocation and collections inside it. Off, every call is
/// a flag test and nothing is printed or allocated. Lines are read from the log (<c>grep 'ARC-DEV-367 perf'</c>); the probe measures,
/// it gates nothing. Names are dotted (<c>load.worker.plan</c>); a span measured on a worker thread counts that thread's allocation.</summary>
public static class PerfProbe
{
    /// <summary>The prefix of every line the probe prints.</summary>
    public const string Prefix = "ARC-DEV-367 perf";

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly double ProcessOffsetMs = ProcessAgeMs();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, double> Marked = [];
    private static readonly Dictionary<string, double> Measured = [];

    /// <summary>True when the probe prints and records; set once from the environment, or by <see cref="Enable"/>.</summary>
    public static bool Enabled { get; private set; } = FromEnvironment();

    /// <summary>Where the lines go (the log); a test replaces it.</summary>
    public static Action<string> Sink { get; set; } = Console.WriteLine;

    /// <summary>Turns the probe on or off (the viewer's perf stage; tests).</summary>
    public static void Enable(bool on = true) => Enabled = on;

    /// <summary>Milliseconds since the process started (its start time read once at first use, then the monotonic clock).</summary>
    public static double SinceProcessStartMs => ProcessOffsetMs + Clock.Elapsed.TotalMilliseconds;

    /// <summary>A point in time: prints its name and the time since the process started; the latest time of a name is kept for <see cref="TimeOf"/>.</summary>
    public static void Mark(string name)
    {
        if (!Enabled) return;
        var at = SinceProcessStartMs;
        lock (Gate) Marked[name] = at;
        Sink($"{Prefix} mark name={name} t={Text(at)}");
    }

    /// <summary>The time since process start of the latest <see cref="Mark"/> of this name, or null when it was not marked.</summary>
    public static double? TimeOf(string name) { lock (Gate) return Marked.TryGetValue(name, out var at) ? at : null; }

    /// <summary>The milliseconds of the last finished <see cref="Span"/> of this name, or null when none finished.</summary>
    public static double? MillisecondsOf(string name) { lock (Gate) return Measured.TryGetValue(name, out var ms) ? ms : null; }

    /// <summary>Times the code up to the span's disposal: <c>using (PerfProbe.Span("x")) ...</c>.</summary>
    public static PerfSpan Span(string name) => Enabled ? new(name) : default;

    /// <summary>Times a call and returns its result.</summary>
    public static T Measure<T>(string name, Func<T> work) { using (Span(name)) return work(); }

    /// <summary>Times a call.</summary>
    public static void Measure(string name, Action work) { using (Span(name)) work(); }

    /// <summary>Consecutive steps of one sequence: <c>var lap = PerfProbe.Lap("publish"); lap.Step("a"); ...; lap.Step("b"); ...; lap.Dispose();</c>
    /// prints a span <c>publish.a</c> and <c>publish.b</c> and one <c>publish</c> for the whole.</summary>
    public static PerfLap Lap(string group) => new(group);

    internal static void Record(string name, double ms) { lock (Gate) Measured[name] = ms; }

    internal static void Print(string name, double ms, long allocated, int gen0, int gen1, int gen2)
    {
        Record(name, ms);
        Sink($"{Prefix} span name={name} ms={Text(ms)} alloc-kib={allocated / 1024} gc={gen0}/{gen1}/{gen2} t={Text(SinceProcessStartMs)}");
    }

    /// <summary>A time or size the way the probe prints it (one decimal, invariant).</summary>
    public static string Text(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    internal static bool FromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("ARC_PERF_PROBE") is { Length: > 0 } on && on != "0") return true;
        return Environment.GetEnvironmentVariable("ARC_AREA_MAP_SMOKE_ONLY") is { Length: > 0 } stages
            && stages.Split(',', StringSplitOptions.TrimEntries).Any(stage => stage is "perf" or "perf-menu");
    }

    private static double ProcessAgeMs()
    {
        try { return Math.Max(0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return 0; }
    }
}

/// <summary>One timed region of <see cref="PerfProbe.Span"/>; the default value (probe off) does nothing.</summary>
public readonly struct PerfSpan : IDisposable
{
    private readonly string? _name;
    private readonly long _start;
    private readonly long _allocated;
    private readonly int _gen0, _gen1, _gen2;

    internal PerfSpan(string name)
    {
        _name = name;
        (_gen0, _gen1, _gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        _allocated = GC.GetAllocatedBytesForCurrentThread();
        _start = Stopwatch.GetTimestamp();
    }

    /// <summary>Prints the span.</summary>
    public void Dispose()
    {
        if (_name is null) return;
        var ms = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        PerfProbe.Print(_name, ms, GC.GetAllocatedBytesForCurrentThread() - _allocated,
            GC.CollectionCount(0) - _gen0, GC.CollectionCount(1) - _gen1, GC.CollectionCount(2) - _gen2);
    }
}

/// <summary>The steps of one sequence (<see cref="PerfProbe.Lap"/>); a lap made while the probe is off does nothing.</summary>
public sealed class PerfLap : IDisposable
{
    private readonly string? _group;
    private PerfSpan _whole;
    private PerfSpan _step;
    private bool _open;

    internal PerfLap(string group)
    {
        if (!PerfProbe.Enabled) return;
        _group = group; _whole = new(group);
    }

    /// <summary>Ends the step in progress and starts <paramref name="name"/>.</summary>
    public void Step(string name)
    {
        if (_group is null) return;
        EndStep();
        _step = new($"{_group}.{name}"); _open = true;
    }

    /// <summary>Ends the last step and the whole.</summary>
    public void Dispose()
    {
        if (_group is null) return;
        EndStep(); _whole.Dispose();
    }

    private void EndStep() { if (_open) _step.Dispose(); _open = false; }
}
