namespace Arcanum.Simulation.Clock;

/// <summary>What one frame is, to the clocks (docs/architecture/clocks-frame.md §3.1, §3.4): the pause stops game and
/// animation time, a round stops game time only, and a frozen frame is one in which the original does not ping at all
/// (message box, movie, slide show, fade, loading; TL25): no host stamp is observed and the reference stays.</summary>
public readonly record struct FrameFlags(bool Paused, bool Round, bool Frozen);

/// <summary>Milliseconds admitted to each clock by one frame, before the owners apply them (TL14): the real interval
/// <c>i</c>, game <c>8i</c> unless paused or in a round, animation <c>8i</c> unless paused.</summary>
public readonly record struct FrameAdmission(long RealMs, long GameMs, long AnimationMs);

/// <summary>The clocks a frame participant reads (K15). <see cref="WorldEighthMs"/> counts the admitted animation time
/// (jumps excluded) in 1/8 ms, so nothing is rounded away; <see cref="WorldMs"/> is the run-local world millisecond the
/// viewer's tuned speeds and walk durations are stated in: it equals real time while neither paused nor frozen, stops
/// with the pause but not with a round, and a time jump does not move it (a jump must not fast-forward a walk).</summary>
public readonly record struct FrameClocks(long RealMs, long GameMs, long AnimationMs, long WorldEighthMs)
{
    public long WorldMs => WorldEighthMs / FrameClockKeeper.WorldUnitsPerMillisecond;
}

public enum FrameClockFailure { None, InvalidHostTime, InvalidDuration, InvalidState, TimeOverflow }

public readonly record struct FrameAdvanceResult(FrameClockFailure Failure, FrameAdmission Admission);
/// <summary><see cref="AppliedMs"/> is the duration added to game and animation time, after a zero duration became 1 ms.</summary>
public readonly record struct FrameJumpResult(FrameClockFailure Failure, long AppliedMs);
public readonly record struct FrameClockKeeperResult(FrameClockFailure Failure, FrameClockKeeper? Keeper);

/// <summary>
/// The real and animation clocks, the world milliseconds and the frame admission of the session (K15, TM6), kept beside
/// the game clock: <see cref="GameClockOwner"/> stays the only writer of the game stamp and its calendar, so a game
/// duration is reported in <see cref="FrameAdmission"/> and by <see cref="PlanJump"/> for the session to apply, and
/// <see cref="ClocksAt"/> composes the clocks a participant reads from the game stamp the owner holds.
/// Real starts at 1 ms (TL15), animation at the game start time. A frame admits through <see cref="GameTimeFlow"/>
/// (same 5 ms floor with the reference left in place, same reference-then-cut order, same 250 ms cut, first and
/// backwards stamps admit nothing). Mutations are atomic: a refused call changes nothing. The admission reference is
/// not part of the saved state (a restored keeper credits nothing at its first frame). No Godot, timers or
/// threads; caller-serialized; the host stamp is a 64-bit millisecond count with no wrap.
/// </summary>
public sealed class FrameClockKeeper
{
    /// <summary>The original's real clock starts at 1 ms, not 0 (TL15).</summary>
    public const long StartRealMs = 1;
    /// <summary>Animation and game time run 8 times the admitted real interval (TL14).</summary>
    public const long RateNumerator = GameTimeFlow.GameMillisecondsPerHostMillisecond;
    /// <summary>World time is kept in 1/8 ms units, so that one admitted animation millisecond is one unit.</summary>
    public const long WorldUnitsPerMillisecond = RateNumerator;

    // The most any one admission adds to a stamp; a stamp closer than this to the limit refuses the frame.
    private const long Headroom = GameTimeFlow.MaxAdmittedMs * RateNumerator;

    private readonly GameTimeFlow _flow = new();
    private long _real;
    private long _animation;
    private long _worldEighths;

    private FrameClockKeeper(long real, long animation) { _real = real; _animation = animation; }

    /// <summary>A keeper for a new session: real at 1 ms, animation at <paramref name="animationStartMs"/> (the start
    /// time, as the game clock), world time at 0.</summary>
    public static FrameClockKeeperResult Start(long animationStartMs) => Restore(StartRealMs, animationStartMs);

    /// <summary>A keeper at saved real and animation stamps (schema 70); the world time and the admission reference
    /// are run-local and start fresh. Refused: a real stamp under 1 ms or a negative animation stamp.</summary>
    public static FrameClockKeeperResult Restore(long realMs, long animationMs) =>
        realMs < StartRealMs || animationMs < 0
            ? new(FrameClockFailure.InvalidState, null)
            : new(FrameClockFailure.None, new FrameClockKeeper(realMs, animationMs));

    public long RealMs => _real;
    public long AnimationMs => _animation;
    public long WorldMs => _worldEighths / WorldUnitsPerMillisecond;

    /// <summary>The clocks a participant reads for the game stamp the clock owner holds now.</summary>
    public FrameClocks ClocksAt(long gameMs) => new(_real, gameMs, _animation, _worldEighths);

    /// <summary>Observes the host stamp and moves real, animation and world time by what it admits, by the frame's
    /// flags. A frozen frame observes nothing and admits nothing. Refused, with nothing changed: a negative host stamp
    /// (<see cref="FrameClockFailure.InvalidHostTime"/>), or a stamp within one admission of the 64-bit limit
    /// (<see cref="FrameClockFailure.TimeOverflow"/>). The game part of the result is for the session to apply to the
    /// game clock; it is not kept here.</summary>
    public FrameAdvanceResult Advance(long hostMs, FrameFlags flags)
    {
        if (flags.Frozen) return new(FrameClockFailure.None, default);
        if (hostMs < 0) return new(FrameClockFailure.InvalidHostTime, default);
        if (_real > long.MaxValue - Headroom || _animation > long.MaxValue - Headroom || _worldEighths > long.MaxValue - Headroom)
            return new(FrameClockFailure.TimeOverflow, default);
        var real = _flow.ObserveInterval(hostMs);
        var scaled = real * RateNumerator;
        var animation = flags.Paused ? 0 : scaled;
        var game = flags.Paused || flags.Round ? 0 : scaled;
        _real += real;
        _animation += animation;
        _worldEighths += animation;
        return new(FrameClockFailure.None, new(real, game, animation));
    }

    /// <summary>What a time jump of <paramref name="durationMs"/> would add to game and animation time, without
    /// changing anything (TL26): a zero duration is 1 ms, pause and round are ignored, nothing fires. Refused: a
    /// negative duration, or one that overflows the animation stamp.</summary>
    public FrameJumpResult PlanJump(long durationMs)
    {
        if (durationMs < 0) return new(FrameClockFailure.InvalidDuration, 0);
        var applied = durationMs == 0 ? 1 : durationMs;
        return applied > long.MaxValue - _animation ? new(FrameClockFailure.TimeOverflow, 0) : new(FrameClockFailure.None, applied);
    }

    /// <summary>Adds a time jump to the animation clock (the session adds the same amount to game time in the same
    /// call, <see cref="PlanJump"/> first when it must not half-apply). The world time does not move: a jump must not
    /// fast-forward a walk, a projectile or a goal run.</summary>
    public FrameJumpResult Jump(long durationMs)
    {
        var plan = PlanJump(durationMs);
        if (plan.Failure == FrameClockFailure.None) _animation += plan.AppliedMs;
        return plan;
    }

    /// <summary>Forgets the admission reference, so the next frame admits nothing (a new scene, a load).</summary>
    public void ResetReference() => _flow.Reset();
}
