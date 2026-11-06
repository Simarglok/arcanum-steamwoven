using Arcanum.Simulation;
using Arcanum.Simulation.Goals;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-136 (MIG13 S5b, docs/architecture/goal-table.md §3.7, TL39): the round's "all significant animations finished" callback of the animation system (0x4B7080, installed
/// at the round start through 0x423FE0). The table counts the significant goals (priority 2 or more, no no-interrupt flag, not "not counted": <see cref="GoalRunTable.SignificantGoalCount"/>,
/// derived from the runs); every free of a slot that leaves the count at zero calls the callback (0x44CF20, after <b>every</b> free, GT11), and the callback is what asks for the
/// turn-based event (kind 12, 2 ms ahead, one pending) and feeds the stall detector. This replaces the round host's own "twice unchanged" check, which called the completion step every
/// frame; the stall detector itself stays (TL39, <see cref="CombatDomain.Stall"/>).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The real milliseconds after which the callback asks for the turn-based event (0x4B7080 queues kind 12 two milliseconds ahead, TL39).</summary>
    public const long TurnBasedEventDelayMs = 2;

    // ARC-DEV-136: > 0 while the round start's interrupts run (the callback is registered after the sweep and the round flag set after the participants' interrupts, TH10: the check
    // returns at once while the flag is 0, so no interrupt of a round's start calls it).
    private int _roundCallbackSuppressed;

    /// <summary>The callbacks (0x4B7080) that came due since this owner was built and went past the hero's test (a diagnostic for the overlay and tests); whether or not an event was already pending.</summary>
    public long AnimationCallbacks { get; private set; }

    /// <summary>The callbacks whose request for the turn-based event the session refused (a full queue), since this owner was built. The viewer runs the handler's pass itself for each, so the round never
    /// stalls on a refusal (the original's queue cannot be full).</summary>
    public long TurnBasedEventRefusals { get; private set; }

    /// <summary>
    /// ARC-DEV-136: holds the round callback while the disposable lives (the round start's sweep and the participants' interrupts, TH10, which come before the original registers the
    /// callback and sets the round flag). Frees during it call nothing; nesting counts.
    /// </summary>
    public IDisposable SuppressRoundCallback()
    {
        ThrowIfDisposed();
        _roundCallbackSuppressed++;
        return new RoundCallbackHold(this);
    }

    private sealed class RoundCallbackHold(GameSessionOwner owner) : IDisposable
    {
        private GameSessionOwner? _owner = owner;
        public void Dispose()
        {
            if (_owner is not { } held) return;
            _owner = null;
            held._roundCallbackSuppressed--;
        }
    }

    /// <summary>
    /// The callback (0x4B7080, TL39, TH5): called by the table when a slot is freed with no significant goal left, and by a host whose own actions are no runs yet (the hero's, until S10-S13)
    /// when they have ended and no significant goal is left. With no round active, or held (<see cref="SuppressRoundCallback"/>), it does nothing. A hero with action points keeps its subturn:
    /// nothing follows (TL39: a PC-type object with action points left; the session adds that the hero must be active, as the handler always did, so a stunned or knocked-out hero never holds the round).
    /// Otherwise the stall detector sees the current participant with its action points (<see cref="CombatDomain.Stall"/>: the same participant seen again with unchanged
    /// action points sets the forced flag, and with the original's A11 the reference of a new participant is the previous one's action points) and the turn-based event (kind 12) is asked for
    /// <see cref="TurnBasedEventDelayMs"/> ahead unless one is pending (<see cref="ScheduleRealStep"/> keeps the pending one); the detector is fed before the pending test, whether or not an event is pending
    /// (goal-table.md GT24, read in ARC-DEV-136). Not saved state: no revision moves.
    /// True when the callback asked for an event or one was already pending; a refused request (a full queue) is counted in <see cref="TurnBasedEventRefusals"/>.
    /// </summary>
    [OriginalFunction(0x4B7080, OriginalFunctionScope.Partial)]
    public bool AnimationsDone()
    {
        ThrowIfDisposed();
        if (_roundCallbackSuppressed > 0 || _combat.Round is not { } round) return false;
        // The hero (or any PC) with action points keeps its turn (TB6); an inactive hero never holds the round with its points (the same rule as the completion step).
        if (round.Actor.IsHero && round.ActionPoints > 0 && HeroStatus().IsActive) return false;
        AnimationCallbacks++;
        _combat.Stall = _combat.Stall.Observe(round.Actor, round.ActionPoints, _rules.Combat.StallCheckKeepsPreviousActionPoints);
        return RequestTurnBasedEvent();
    }

    /// <summary>Asks for the turn-based event (kind 12) <see cref="TurnBasedEventDelayMs"/> ahead unless one is pending: the callback's request, and the round host's request again when a pass found the
    /// hero's viewer-hosted action playing or was skipped for a transfer, reset or reload. True when an event was asked for or one was already pending; a refusal (a full queue) is counted in
    /// <see cref="TurnBasedEventRefusals"/> for the viewer to answer.</summary>
    public bool RequestTurnBasedEvent()
    {
        ThrowIfDisposed();
        if (ScheduleRealStep(SessionRealStep.TurnBasedStep, TurnBasedEventDelayMs) != GameSessionDisposition.Rejected) return true;
        TurnBasedEventRefusals++;
        return false;
    }

    // 0x44CF20 calls the callback after every free that leaves the counter at 0: one call for each ended run whose free saw no significant goal left.
    private void FreedRuns(IEnumerable<GoalRunEnd> ended)
    {
        foreach (var end in ended)
            if (end.AnimationsDone) AnimationsDone();
    }
}
