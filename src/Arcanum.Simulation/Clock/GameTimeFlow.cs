namespace Arcanum.Simulation.Clock;

/// <summary>
/// Host adapter from monotonic host milliseconds to admitted game milliseconds (FR-LIGHT-002), kept apart
/// from the clock authority. It follows the pinned CE ping admission (timeevent.c, non-network path): an
/// elapsed interval under <see cref="MinAdmittedMs"/> is not admitted and does not move the reference, an
/// admitted interval moves the reference and is clamped to <see cref="MaxAdmittedMs"/> (the excess is
/// discarded, not a backlog), and game time gains <see cref="GameMillisecondsPerHostMillisecond"/> times
/// the admitted interval. The rate is the CE source observation, not an original-runtime measurement
/// (NOT_VERIFIED). Pausing is the clock's business: the host keeps observing while paused, the clock
/// ignores ordinary advances, so a resume has no catch-up. Caller-serialized; no timers or threads.
/// </summary>
public sealed class GameTimeFlow
{
    public const long MinAdmittedMs = 5;
    public const long MaxAdmittedMs = 250;
    public const long GameMillisecondsPerHostMillisecond = 8;
    private long? _reference;

    /// <summary>Game milliseconds admitted by this observation; zero for the first observation, an interval
    /// under the minimum, or a host time that went backwards (which restarts the reference there). Host time is
    /// nonnegative.</summary>
    public long Observe(long hostMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hostMilliseconds);
        if (_reference is not { } reference || hostMilliseconds < reference) { _reference = hostMilliseconds; return 0; }
        var elapsed = hostMilliseconds - reference;
        if (elapsed < MinAdmittedMs) return 0;
        _reference = hostMilliseconds;
        return Math.Min(elapsed, MaxAdmittedMs) * GameMillisecondsPerHostMillisecond;
    }

    /// <summary>Forgets the reference, so the next observation admits nothing (a new scene or session).</summary>
    public void Reset() => _reference = null;
}
