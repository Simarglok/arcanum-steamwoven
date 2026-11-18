namespace Arcanum.TerrainViewer.Core;

/// <summary>The counts of the frames a <see cref="SessionEventLogThrottle"/> summed into one log line.</summary>
public sealed record SessionEventLogTally(int Frames, int Fired, int Dropped, int ScriptCalls, int ScriptCallsRun, int Waits, int Decays, int Restocks);

/// <summary>
/// ARC-DEV-1397: decides when the pump's per-frame event counts reach the log. The pump runs once per frame and the session fires
/// its own events (AI ticks, regeneration steps) in most frames, so a line per frame filled the bug report's 120 s window with
/// thousands of lines that carry no news. A frame where something notable happened (an event dropped, a wait, a decay or a restock
/// came due, a call was refused, the step bound or the pass budget cut the frame) is logged at once with the quiet frames
/// before it summed in; quiet frames are summed and logged at most once per <see cref="QuietIntervalMilliseconds"/>. Dropped events are always notable, so
/// never held; held quiet counts are logged with the next line (the last quiet second can be left unlogged at the end of a window). Not thread-safe (the viewer's frame loop is its one caller).
/// </summary>
public sealed class SessionEventLogThrottle
{
    /// <summary>The longest span of quiet frames summed into one line.</summary>
    public const long QuietIntervalMilliseconds = 1_000;

    private long? _lastLoggedMs;
    private SessionEventLogTally _pending = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Takes one frame's counts; returns the tally to log now (this frame and the quiet frames held back before it), or null when
    /// the frame is held for a later line. A clock that moves backward (a new map's driver) counts as an elapsed interval.
    /// </summary>
    public SessionEventLogTally? Offer(long nowMilliseconds, int fired, int dropped, int scriptCalls, int scriptCallsRun, int waits, int decays, int restocks, bool notable)
    {
        _pending = new(_pending.Frames + 1, _pending.Fired + fired, _pending.Dropped + dropped, _pending.ScriptCalls + scriptCalls,
            _pending.ScriptCallsRun + scriptCallsRun, _pending.Waits + waits, _pending.Decays + decays, _pending.Restocks + restocks);
        var due = notable || _lastLoggedMs is not { } last || nowMilliseconds < last || nowMilliseconds - last >= QuietIntervalMilliseconds;
        if (!due) return null;
        var tally = _pending;
        _pending = new(0, 0, 0, 0, 0, 0, 0, 0);
        _lastLoggedMs = nowMilliseconds;
        return tally;
    }
}
