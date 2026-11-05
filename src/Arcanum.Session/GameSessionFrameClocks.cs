using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// MIG12 S3 (ARC-DEV-122, docs/architecture/clocks-frame.md §3.1, §3.6): the session owns the three clocks of the original (TL15),
/// real, game and animation. The game clock stays <see cref="GameClockOwner"/> (its token, revision, pause and calendar); the real and
/// animation stamps, the world milliseconds and the frame admission are the session's <see cref="FrameClockKeeper"/>, which no caller
/// outside the session can move. A frame (<see cref="AdvanceFrame"/>) admits one host stamp; a jump (<see cref="JumpClocks"/>, and every
/// explicit wait) adds to game and animation together. The event queue is handed all three clocks.
/// </summary>
public sealed partial class GameSessionOwner
{
    private readonly FrameClockKeeper _frame;

    /// <summary>The clocks a frame participant reads now: real, game, animation and the run-local world milliseconds.</summary>
    public FrameClocks QueryFrameClocks()
    {
        ThrowIfDisposed();
        return _frame.ClocksAt(_clock.QueryClockSnapshot().GameTimestampMs);
    }

    /// <summary>
    /// One host frame (TL14): observes <paramref name="hostMs"/> and moves the real clock by the admitted interval <c>i</c> and the
    /// animation clock and the world milliseconds by <c>8i</c> unless the game clock is paused, with the session's own flags (the pause
    /// of the game clock; a turn-based round stops game time only). A <paramref name="frozen"/> frame (a loop of the original that
    /// does not ping: message box, movie, slide show, fade, loading; TL25) observes nothing, so the next frame credits at most 250 ms.
    /// The game part of <see cref="FrameAdvanceResult.Admission"/> is <b>not</b> applied here: the caller hands it to the event pump,
    /// which steps it at each event deadline through <see cref="AdvanceGameTime"/> with <see cref="ClockAdvanceReason.OrdinaryAdvance"/>
    /// (S4 moves that pump into the world runtime and steps the three clocks together). Refused with nothing changed: a negative host
    /// stamp or a clock within one admission of the 64-bit limit (<see cref="FrameAdvanceResult.Failure"/>). Not a session revision:
    /// the game clock keeps its own, the real and animation stamps are saved as they stand.
    /// </summary>
    public FrameAdvanceResult AdvanceFrame(long hostMs, bool frozen = false)
    {
        ThrowIfDisposed();
        var pause = _clock.QueryClockSnapshot().PauseDisposition;
        return _frame.Advance(hostMs, new(pause.Paused, _combat.Round is not null, frozen));
    }

    /// <summary>
    /// A time jump (TL26): <paramref name="durationMs"/> added to game and animation time together, also while paused or in a round,
    /// firing nothing itself (due events fire at the next pass); a zero duration is 1 ms. The world milliseconds do not move (a jump
    /// must not fast-forward a walk or a projectile). The world-map travel step, a round's second, a timed teleport and a script's
    /// time action are jumps; so is every <see cref="ClockAdvanceReason.ExplicitWait"/> of <see cref="AdvanceGameTime"/>. Refused
    /// with nothing changed: a negative duration (<see cref="ClockFailure.InvalidDuration"/>), a stale token, or a stamp, calendar
    /// or revision the game clock or the animation clock cannot take.
    /// </summary>
    public ClockMutationResult JumpClocks(ClockToken token, long durationMs)
    {
        ThrowIfDisposed();
        if (durationMs < 0) return _clock.AdvanceGameTime(token, durationMs, ClockAdvanceReason.ExplicitWait);
        return AdvanceClock(token, durationMs == 0 ? 1 : durationMs, ClockAdvanceReason.ExplicitWait);
    }

    /// <summary>Forgets the frame admission reference, so the next frame admits nothing (a new scene binds to the session).</summary>
    public void ResetFrameReference()
    {
        ThrowIfDisposed();
        _frame.ResetReference();
    }

    // The three clocks the event queue schedules and fires against, with the game stamp the caller already read.
    private GameEventClocks EventClocks(long gameMs) => new(_frame.RealMs, gameMs, _frame.AnimationMs);

    // Whether a jump of `durationMs` fits the animation clock; the game clock checks its own half.
    private bool CanJumpAnimation(long durationMs) => _frame.PlanJump(durationMs).Failure == FrameClockFailure.None;
}
