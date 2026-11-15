namespace Arcanum.TerrainViewer.Core;

/// <summary>What a close request asks the host to do.</summary>
public enum QuitDecision
{
    /// <summary>Nothing new: the question is already asked, or a repeat that is not a hold yet.</summary>
    None,
    /// <summary>Ask the quit question (the original's yes/no box, message 5100).</summary>
    Ask,
    /// <summary>The key was held: quit now without the question.</summary>
    Quit,
}

/// <summary>
/// ARC-DEV-2270: decides what a close request does, Godot-free. Every close request (Cmd+Q on macOS, Alt+F4, the window's close button)
/// asks the original's quit question first (the original's window procedure turns WM_CLOSE into that question, ledger SF3/DB1). A held
/// quit key repeats as a stream of close requests (the operating system's key repeat; the key itself never reaches the game), and with
/// <c>holdToQuit</c> on a stream that keeps up for <see cref="HoldMs"/> quits without waiting for an answer, as a browser does. A gap
/// longer than <see cref="RepeatGapMs"/> ends a stream (the key's initial repeat delay is such a gap, so the hold is counted from its
/// first repeat), so slow presses one after the other never add up to a hold. Caller-serialized.
/// </summary>
public sealed class QuitRequestGate
{
    /// <summary>How long a stream of repeats must last (from its first repeat) to count as a hold.</summary>
    public const long HoldMs = 1500;
    /// <summary>The longest gap between two requests of one stream of key repeats.</summary>
    public const long RepeatGapMs = 300;

    private long _last;
    private long? _streakStart;

    /// <summary>True from the first request until <see cref="Answered"/>: the question is on screen.</summary>
    public bool Asking { get; private set; }

    /// <summary>One close request at <paramref name="nowMs"/> (a monotonic millisecond clock).</summary>
    public QuitDecision Request(long nowMs, bool holdToQuit)
    {
        if (!Asking)
        {
            Asking = true; _last = nowMs; _streakStart = null;
            return QuitDecision.Ask;
        }
        var gap = nowMs - _last;
        _last = nowMs;
        if (gap < 0 || gap > RepeatGapMs) { _streakStart = null; return QuitDecision.None; }
        _streakStart ??= nowMs - gap;
        return holdToQuit && nowMs - _streakStart.Value >= HoldMs ? QuitDecision.Quit : QuitDecision.None;
    }

    /// <summary>The question was answered no (or dismissed): the next request asks again.</summary>
    public void Answered() { Asking = false; _streakStart = null; }
}
