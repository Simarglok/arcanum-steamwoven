using System.Diagnostics;
using System.Globalization;

namespace Arcanum.World;

/// <summary>ARC-RENDER-3126 (critical click-to-action lag at night): the diagnostic switch and the per-second measurement of the animated
/// lights' view pushes. <c>ARC_LIGHT_ANIM=0</c> makes the viewer not push a new lighting view when an animated light changes its pose (the
/// animation time still runs); every other value, or none, leaves the default behaviour untouched. With the timing probe on
/// (<see cref="PerfProbe.Enabled"/>) <see cref="Frame"/> prints one line a second, <c>ARC-DEV-367 perf light-anim ...</c>, with the frames and
/// the average fps of the window, the pushes and their milliseconds, the managed bytes they allocated, the canvas's view rebuilds and their milliseconds (a push only stores the view;
/// the lattice and body-colour rebuild is made at the next draw) and the collections of the window.
/// It measures and gates nothing.</summary>
public sealed class LightAnimationProbe
{
    /// <summary>The prefix of the line the probe prints.</summary>
    public const string Prefix = PerfProbe.Prefix + " light-anim";

    /// <summary>True when the environment asks the animated lights not to push views (a diagnostic; read once).</summary>
    public static bool Disabled { get; } = IsDisabled(Environment.GetEnvironmentVariable("ARC_LIGHT_ANIM"));

    /// <summary>Whether a switch value turns the push off: exactly <c>0</c> (spaces ignored).</summary>
    public static bool IsDisabled(string? value) => value?.Trim() == "0";

    private readonly double _windowMs;
    private long _windowStart;
    private int _frames, _pushes, _rebuilds;
    private double _pushMs, _pushMaxMs, _rebuildMs, _rebuildMaxMs;
    private long _pushAllocated;
    private int _gen0, _gen1, _gen2;

    /// <summary>The viewer's one probe: the lighting tick counts the frames and the pushes, the canvas the view rebuilds it makes after a push.</summary>
    public static LightAnimationProbe Shared { get; } = new();

    /// <summary>A probe printing every <paramref name="windowSeconds"/> seconds.</summary>
    public LightAnimationProbe(double windowSeconds = 1) => _windowMs = windowSeconds * 1000;

    /// <summary>Notes one push of <paramref name="ms"/> milliseconds that allocated <paramref name="allocatedBytes"/> managed bytes.</summary>
    public void Push(double ms, long allocatedBytes)
    {
        _pushes++; _pushMs += ms; _pushAllocated += allocatedBytes;
        if (ms > _pushMaxMs) _pushMaxMs = ms;
    }

    /// <summary>Notes one rebuild of the canvas's visible view (the draw-time work a pushed lighting view causes: the lattice and the body colours)
    /// of <paramref name="ms"/> milliseconds, made on the main thread.</summary>
    public void Rebuild(double ms)
    {
        _rebuilds++; _rebuildMs += ms;
        if (ms > _rebuildMaxMs) _rebuildMaxMs = ms;
    }

    /// <summary>Notes one frame at the monotonic <paramref name="timestamp"/> (<see cref="Stopwatch.GetTimestamp"/>) and, when the window is over,
    /// prints the line and starts the next. <paramref name="engineFps"/> is the engine's instantaneous rate, <paramref name="animatedLights"/> the
    /// map's animated lights (0 on a map without them). Does nothing while the probe is off.</summary>
    public void Frame(long timestamp, double engineFps, int animatedLights)
    {
        if (!PerfProbe.Enabled) return;
        if (_windowStart == 0)
        {
            Reset(timestamp);
            return;
        }
        _frames++;
        var elapsedMs = Stopwatch.GetElapsedTime(_windowStart, timestamp).TotalMilliseconds;
        if (elapsedMs < _windowMs) return;
        PerfProbe.Sink(Line(elapsedMs, engineFps, animatedLights));
        Reset(timestamp);
    }

    private string Line(double elapsedMs, double engineFps, int animatedLights)
    {
        static string N(double value) => PerfProbe.Text(value);
        var gen = $"{GC.CollectionCount(0) - _gen0}/{GC.CollectionCount(1) - _gen1}/{GC.CollectionCount(2) - _gen2}";
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix} switch={(Disabled ? "off" : "on")} lights={animatedLights} frames={_frames} fps-avg={N(_frames * 1000.0 / elapsedMs)} fps-engine={N(engineFps)} " +
            $"pushes={_pushes} push-ms-total={N(_pushMs)} push-ms-max={N(_pushMaxMs)} push-alloc-kib={_pushAllocated / 1024} " +
            $"view-rebuilds={_rebuilds} view-ms-total={N(_rebuildMs)} view-ms-max={N(_rebuildMaxMs)} gc={gen} t={N(PerfProbe.SinceProcessStartMs)}");
    }

    private void Reset(long timestamp)
    {
        _windowStart = timestamp;
        (_frames, _pushes, _rebuilds, _pushMs, _pushMaxMs, _rebuildMs, _rebuildMaxMs, _pushAllocated) = (0, 0, 0, 0, 0, 0, 0, 0);
        (_gen0, _gen1, _gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }
}
