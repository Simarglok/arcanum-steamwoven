namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-INPUT-1400: the viewer's evidence of how often one physical mouse press reaches it. The owner's reports showed single left
/// presses on the open spell cursor making 2 to 4 casts within 1 to 20 ms; the viewer handles each event once, so the duplicates are
/// either several press events from the system or one event handled twice. This record keeps, per button, the previous press of each
/// <see cref="Stage"/> and writes one log line per press that says when (microseconds), in which frame, from which device, where,
/// and how far from the previous press of the same button and stage. It decides nothing and swallows nothing: the original has no
/// guard (the cursor stays open after a cast, so each press is a cast), and a guard is justified only once this log shows what the
/// platform sends.
/// </summary>
public sealed class MousePressTrace
{
    /// <summary>A gap below this (microseconds) marks the press as part of a burst in the line (the owner's reports: 1 to 20 ms).</summary>
    public const long BurstGapMicroseconds = 50_000;

    /// <summary>Where in the viewer the press was seen.</summary>
    public enum Stage
    {
        /// <summary>The viewer's first input handler (before the GUI).</summary>
        Input,
        /// <summary>The unhandled-input handler (no GUI control took it): the world's click.</summary>
        Unhandled,
    }

    /// <summary>One press as the viewer saw it.</summary>
    public readonly record struct Press(Stage Stage, string Button, long Microseconds, long Frame, int Device, double X, double Y,
        long EventId, bool DoubleClick, int ButtonMask);

    private readonly Dictionary<(Stage, string), (Press Press, int Burst)> _last = new();

    /// <summary>Notes a press and returns the line to write to the log.</summary>
    public string Note(Press press)
    {
        var key = (press.Stage, press.Button);
        var gap = -1L;
        var burst = 1;
        var sameEvent = false;
        var samePosition = false;
        if (_last.TryGetValue(key, out var previous))
        {
            gap = press.Microseconds - previous.Press.Microseconds;
            sameEvent = press.EventId != 0 && press.EventId == previous.Press.EventId;
            samePosition = press.X == previous.Press.X && press.Y == previous.Press.Y;
            burst = gap >= 0 && gap < BurstGapMicroseconds ? previous.Burst + 1 : 1;
        }
        _last[key] = (press, burst);
        var line = FormattableString.Invariant($"ARC-INPUT-1400 press stage={press.Stage} button={press.Button} t={press.Microseconds / 1000.0:F3}ms frame={press.Frame} device={press.Device} pos=({press.X:F1},{press.Y:F1}) id={press.EventId} double={press.DoubleClick} mask={press.ButtonMask}");
        line += gap < 0 ? " gap=first" : FormattableString.Invariant($" gap={gap / 1000.0:F3}ms same-pos={samePosition} same-event={sameEvent}");
        return burst > 1 ? line + FormattableString.Invariant($" BURST={burst}") : line;
    }
}
