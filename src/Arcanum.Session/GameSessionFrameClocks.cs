using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Events;
using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// MIG12 S3 (ARC-DEV-122, docs/architecture/clocks-frame.md §3.1, §3.6): the session owns the three clocks of the original (TL15),
/// real, game and animation. The game clock stays <see cref="GameClockOwner"/> (its token, revision, pause and calendar); the real and
/// animation stamps, the world milliseconds and the frame admission are the session's <see cref="FrameClockKeeper"/>, which no caller
/// outside the session can move. A frame (<see cref="BeginFrame"/>) admits one host stamp; a jump (<see cref="JumpClocks"/>, and every
/// explicit wait) adds to game and animation together. The event queue is handed all three clocks.
/// MIG12 S4 (ARC-DEV-123): ordinary game time moves only inside a frame (<see cref="BeginFrame"/>, <see cref="SessionFrame"/>): the
/// public clock call that advanced it without one is gone (<see cref="AdvanceGameTime"/> is internal).
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

    private SessionFrame? _openFrame;

    /// <summary>
    /// MIG12 S4, the only path of ordinary game time (TL14): opens the frame of one host stamp. The stamp is observed (a frozen
    /// frame, a loop of the original that does not ping, observes nothing; TL25) and the admitted interval <c>i</c> becomes a
    /// frame of <c>8i</c> units that the caller spends with <see cref="SessionFrame.Step"/> and closes with
    /// <see cref="SessionFrame.End"/>; no clock has moved yet. Each step moves real, game and animation time together by the
    /// session's own flags (the pause of the game clock; a turn-based round stops game time only), so the frame step can end a
    /// step at each event deadline. Refused with nothing changed: a negative host stamp, a clock within one admission of the
    /// 64-bit limit, or a frame already open (<see cref="FrameClockFailure.InvalidState"/>).
    /// </summary>
    public SessionFrameResult BeginFrame(long hostMs, bool frozen = false)
    {
        ThrowIfDisposed();
        if (_openFrame is { IsOpen: true }) return new(FrameClockFailure.InvalidState, null);
        var admitted = _frame.Admit(hostMs, FrameFlagsNow(frozen));
        if (admitted.Failure != FrameClockFailure.None) return new(admitted.Failure, null);
        return new(FrameClockFailure.None, _openFrame = new SessionFrame(this, admitted.Admission, admitted.Admission.RealMs * FrameClockKeeper.RateNumerator));
    }

    // The flags of this moment: the game clock's pause, and a round of the session; read again at every step.
    [OriginalFunction(0x45B300, OriginalFunctionScope.Partial)]
    private FrameFlags FrameFlagsNow(bool frozen = false) => new(_clock.QueryClockSnapshot().PauseDisposition.Paused, _combat.Round is not null, frozen);

    internal FrameStepResult StepFrame(SessionFrame frame, long units)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(frame, _openFrame) || !frame.IsOpen) return new(FrameClockFailure.InvalidState, ClockFailure.None, 0, default);
        if (units <= 0 || units > frame.RemainingUnits) return new(FrameClockFailure.InvalidDuration, ClockFailure.None, 0, default);
        var flags = FrameFlagsNow();
        var gameMs = flags.Paused || flags.Round ? 0 : units;
        // The keeper's limit first (a jump made inside a pass can bring a stamp near it), so a refusal leaves nothing half applied.
        var limit = _frame.CanStep(units, flags);
        if (limit != FrameClockFailure.None) return new(limit, ClockFailure.None, 0, flags);
        if (gameMs > 0)
        {
            // Then the game clock, which can refuse too (calendar, revision); the keeper cannot refuse after its own check.
            var game = AdvanceClock(_clock.QueryClockSnapshot().Token, gameMs, ClockAdvanceReason.OrdinaryAdvance);
            if (game.Disposition == ClockMutationDisposition.Rejected) return new(FrameClockFailure.None, game.Failure, 0, flags);
        }
        var moved = _frame.Step(units, flags);
        if (moved != FrameClockFailure.None) return new(moved, ClockFailure.None, gameMs, flags);
        return new(FrameClockFailure.None, ClockFailure.None, gameMs, flags);
    }

    internal void EndFrame(SessionFrame frame)
    {
        if (ReferenceEquals(frame, _openFrame)) _openFrame = null;
    }

    // The units of the open frame after which a clock reaches a deadline (see FrameClockKeeper.UnitsUntil), by the flags of this moment.
    internal long? UnitsUntil(Arcanum.Simulation.Events.GameEventDomain domain, long deadlineMs) =>
        _frame.UnitsUntil(domain, deadlineMs, _clock.QueryClockSnapshot().GameTimestampMs, FrameFlagsNow());

    /// <summary>
    /// A time jump (TL26): <paramref name="durationMs"/> added to game and animation time together, also while paused or in a round,
    /// firing nothing itself (due events fire at the next pass); a zero duration is 1 ms. The world milliseconds do not move (a jump
    /// must not fast-forward a walk or a projectile). The world-map travel step, a round's second and a timed change of place are jumps
    /// (a script's time action will be one when it exists); so is every positive <see cref="ClockAdvanceReason.ExplicitWait"/> of
    /// <see cref="AdvanceGameTime"/> (a zero explicit wait stays NoChange there; only this call makes 0 into 1 ms). Refused
    /// with nothing changed: a negative duration (<see cref="ClockFailure.InvalidDuration"/>), a stale token, or a stamp, calendar
    /// or revision the game clock or the animation clock cannot take.
    /// </summary>
    [OriginalFunction(0x45C200, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45C1C0, OriginalFunctionScope.Full)]
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

    internal FrameFlags FrameFlagsOf(SessionFrame frame) => FrameFlagsNow();

    // The three clocks the event queue schedules and fires against, with the game stamp the caller already read.
    private GameEventClocks EventClocks(long gameMs) => new(_frame.RealMs, gameMs, _frame.AnimationMs);

    // For the tests: the clocks every schedule and pass hands the queue now.
    internal GameEventClocks QueryEventClocks() => EventClocks(_clock.QueryClockSnapshot().GameTimestampMs);

    // Whether a jump of `durationMs` fits the animation clock; the game clock checks its own half.
    private bool CanJumpAnimation(long durationMs) => _frame.PlanJump(durationMs).Failure == FrameClockFailure.None;
}

/// <summary>The outcome of <see cref="GameSessionOwner.BeginFrame"/>: a typed refusal, or the open frame.</summary>
public readonly record struct SessionFrameResult(FrameClockFailure Failure, SessionFrame? Frame);

/// <summary>One step of an open frame: a refusal of the keeper (<see cref="Failure"/>) or of the game clock
/// (<see cref="ClockFailure"/>) with nothing moved, the game milliseconds the step applied (0 in a pause or a round) and the flags it
/// ran under.</summary>
public readonly record struct FrameStepResult(FrameClockFailure Failure, ClockFailure ClockFailure, long GameMs, FrameFlags Flags)
{
    public bool Ok => Failure == FrameClockFailure.None && ClockFailure == ClockFailure.None;
}

/// <summary>
/// MIG12 S4 (docs/architecture/clocks-frame.md §3.2, §3.3): the open frame of one admitted host stamp. Its length is <c>8i</c> units
/// for an admitted real interval <c>i</c>: the real clock moves <c>1/8 ms</c> a unit and game and animation time 1 ms a unit (the
/// original's ×8). <see cref="Step"/> spends units; the frame step spends them at event deadlines. A frame is closed by
/// <see cref="End"/>; one frame is open at a time. Obtained only from <see cref="GameSessionOwner.BeginFrame"/>.
/// </summary>
public sealed class SessionFrame
{
    private readonly GameSessionOwner _owner;

    internal SessionFrame(GameSessionOwner owner, FrameAdmission admission, long units)
    {
        _owner = owner; Admission = admission; TotalUnits = RemainingUnits = units; IsOpen = true;
    }

    /// <summary>What the stamp admitted, by the flags at the time it was observed.</summary>
    public FrameAdmission Admission { get; }
    public long TotalUnits { get; }
    public long RemainingUnits { get; private set; }
    public bool IsOpen { get; private set; }

    /// <summary>The clocks a participant reads now, in the middle of the frame or after it.</summary>
    public FrameClocks Clocks => _owner.QueryFrameClocks();

    /// <summary>The flags a step taken now would run under (the pause of the game clock and the round of the session).</summary>
    public FrameFlags Flags => _owner.FrameFlagsOf(this);

    /// <summary>Spends <paramref name="units"/> (1..<see cref="RemainingUnits"/>) on the three clocks together; refused with
    /// nothing moved on a closed frame, a bad length, or a clock that cannot take it.</summary>
    public FrameStepResult Step(long units)
    {
        var result = _owner.StepFrame(this, units);
        if (result.Ok) RemainingUnits -= units;
        return result;
    }

    /// <summary>The units after which the clock of <paramref name="domain"/> reaches <paramref name="deadlineMs"/> under the
    /// current flags, or null when it does not move or the deadline is not ahead of it.</summary>
    public long? UnitsUntil(Arcanum.Simulation.Events.GameEventDomain domain, long deadlineMs) => IsOpen ? _owner.UnitsUntil(domain, deadlineMs) : null;

    /// <summary>Closes the frame. Units not spent are lost (the real clock stays behind by their share).</summary>
    public void End()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _owner.EndFrame(this);
    }
}
