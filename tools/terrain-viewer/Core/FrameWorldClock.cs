using Arcanum.Simulation.Clock;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-125 (MIG12 S6, docs/architecture/clocks-frame.md §3.3): the viewer's frame participant that carries the clocks of the last
/// frame to the systems that read time between frames (the hero's walk, attack and cast, projectiles, spell missiles, eye candy, the
/// dying and lying animations, body moves, blood pools, scenery and the light animation). It is registered on the driver's
/// <see cref="FrameStep"/>, so it runs after the event passes of every frame, and it moves no clock. Until its first step it holds no
/// clocks (<see cref="Clocks"/> is null) and a reader falls back to the host's own time.
/// </summary>
public sealed class FrameClockTap : IFrameParticipant
{
    /// <summary>The clocks at the end of the last frame (null before the first one).</summary>
    public FrameClocks? Clocks { get; private set; }

    /// <summary>The pause and round flags at the end of the last frame.</summary>
    public FrameFlags Flags { get; private set; }

    /// <summary>How many frames this tap has seen.</summary>
    public long Frames { get; private set; }

    public void Step(FrameClocks clocks, FrameFlags flags)
    {
        Clocks = clocks;
        Flags = flags;
        Frames++;
    }
}

/// <summary>
/// ARC-DEV-125: one monotone timeline, in microseconds, for the viewer's world time. While a <see cref="FrameClockTap"/> that has seen a
/// frame is the source, the timeline moves with the session's <em>world milliseconds</em> (admitted animation time ÷ 8: it equals host
/// time while the game runs, stops with the Esc pause, is cut at 250 ms a frame like the original's ping, and is not moved by jumps, so
/// a round's second or a world-map step does not fast-forward a walk, §3.3); with no tap, or before its first frame, it follows the host's
/// time. A change of source (a new tap after a load or a rebuilt driver, a map without a driver, a tap's first frame) <em>continues from
/// the last value</em>: the new source is read as an offset from it, so the time never steps back and never jumps forward by what the
/// frame clock held back (a pause, a cut frame), and every deadline a consumer stored stays valid. Not thread-safe (main thread).
/// </summary>
public sealed class WorldTimeline
{
    /// <summary>One world millisecond is a frame clock's eighth times 125 µs.</summary>
    public const long MicrosecondsPerWorldEighth = 1000 / FrameClockKeeper.WorldUnitsPerMillisecond;

    private static readonly object HostSource = new();
    private long? _last;
    private object? _source;
    private long _base;

    /// <summary>The newest value read (microseconds), or null before the first read.</summary>
    public long? Last => _last;

    /// <summary>Whether the last read followed a frame clock rather than the host's time.</summary>
    public bool FollowsFrames { get; private set; }

    /// <summary>Reads the world time in microseconds: the tap's frame clock when it has one, else <paramref name="hostMicros"/>.</summary>
    public long Read(FrameClockTap? tap, long hostMicros)
    {
        object source; long own;
        if (tap?.Clocks is { } clocks) { source = tap; own = checked(clocks.WorldEighthMs * MicrosecondsPerWorldEighth); }
        else { source = HostSource; own = hostMicros; }
        FollowsFrames = !ReferenceEquals(source, HostSource);
        if (!ReferenceEquals(_source, source)) { _source = source; _base = (_last ?? own) - own; }
        var value = _base + own;
        if (_last is { } last && value < last) value = last;
        return (_last = value).Value;
    }
}
