using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// MIG12 S8 (ARC-DEV-127, docs/architecture/clocks-frame.md §3.5): the chains the original keeps on the real-time list (TL32 kinds 12
/// and 14), which run behind the Esc menu because real time does. <see cref="WorldMapStep"/> is the world-map journey's travel step
/// (kind 14, TL28: a first step 50 ms after the start, then one every 62 ms of real time, each with its time jump), and
/// <see cref="TurnBasedStep"/> is the turn-based event (kind 12, TL39), the host's pass over the round's subturns.
/// </summary>
public enum SessionRealStep { WorldMapStep = 1, TurnBasedStep = 2 }

/// <summary>A real-time step that came due in a pass: its queue record, the step and the real time of its list when it fired. The pass only
/// reports it; the frame step hands it to the host after the pass (the steps' hosts act on the session, which a handler may not).</summary>
public sealed record SessionRealStepFiring(GameEvent Event, SessionRealStep Step, long NowMs);

public sealed partial class GameSessionOwner
{
    /// <summary>The longest delay a real-time step is scheduled with: one real minute (project policy; the original's are 2, 50 and 62 ms).</summary>
    public const long MaxRealStepDelayMs = 60_000;

    private List<SessionRealStepFiring>? _passRealSteps;

    /// <summary>
    /// Schedules <paramref name="step"/> on the real list <paramref name="delayMs"/> (1..<see cref="MaxRealStepDelayMs"/>) after the real
    /// clock's current value, as the original's journey and its turn-based event schedule themselves. One pending event of each step at
    /// most: with one pending the call changes nothing (<see cref="GameSessionDisposition.NoChange"/>, the pending one keeps its
    /// deadline, as the original does not queue a second turn-based event, TL39). The events are not saved and need no expected version:
    /// no saved state changes. Refused (<see cref="GameSessionDisposition.Rejected"/>) with a delay out of range or a full queue.
    /// </summary>
    public GameSessionDisposition ScheduleRealStep(SessionRealStep step, long delayMs)
    {
        ThrowIfDisposed();
        var kind = KindOf(step);
        if (delayMs is < 1 or > MaxRealStepDelayMs) return GameSessionDisposition.Rejected;
        var clocks = EventClocks(_clock.QueryClockSnapshot().GameTimestampMs);
        var scheduled = _events.ScheduleAfter(kind, GameEventPayload.Of([], clocks.RealMs), delayMs, clocks);
        return scheduled.Disposition switch
        {
            GameEventScheduleDisposition.Added => GameSessionDisposition.Changed,
            GameEventScheduleDisposition.KeptExisting => GameSessionDisposition.NoChange,
            _ => GameSessionDisposition.Rejected,
        };
    }

    /// <summary>The real time at which <paramref name="step"/> is due, or null when none is pending.</summary>
    public long? QueryRealStep(SessionRealStep step)
    {
        ThrowIfDisposed();
        var pending = _events.QueryPending(KindOf(step));
        return pending.IsEmpty ? null : pending[0].DeadlineMs;
    }

    /// <summary>Removes the pending event of <paramref name="step"/> (the journey ended, the round ended); false when none was pending.</summary>
    public bool CancelRealStep(SessionRealStep step)
    {
        ThrowIfDisposed();
        return _events.RemoveFirst(KindOf(step), _ => true) is not null;
    }

    private static string KindOf(SessionRealStep step) => step switch
    {
        SessionRealStep.WorldMapStep => SessionEventKinds.WorldMapStep,
        SessionRealStep.TurnBasedStep => SessionEventKinds.TurnBasedStep,
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    // A step's event came due: it is reported for the frame step, which runs the host after the pass.
    internal GameEventHandlerOutcome FireRealStep(SessionRealStep step, GameEventFiring firing)
    {
        _passRealSteps?.Add(new(firing.Event, step, firing.NowMs));
        return GameEventHandlerOutcome.Completed;
    }
}

internal sealed class RealStepHandler(SessionEventSink sink, SessionRealStep step) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireRealStep(step, firing) ?? GameEventHandlerOutcome.Refused;
}
