using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// An event-pass host that can process spell runs (ARC-MAGIC-010): the world the run rules ask
/// (<see cref="ISpellRunHost"/>). A pass given another <see cref="ISessionEventWorld"/> consumes due magic-tech events without
/// running them and reports them as not run.
/// </summary>
public interface ISessionSpellWorld : ISessionEventWorld
{
    ISpellRunHost SpellRuns { get; }
}

/// <summary>A magic-tech event that came due in a pass: the event, its run and code, whether the host ran it, and the rules'
/// notes for the host.</summary>
public sealed record SessionSpellRunFiring(GameEvent Event, int Run, SpellRunEventCode Code, bool Ran, IReadOnlyList<SpellRunNote> Notes);

/// <summary>What a host asks of the session's runs (ARC-MAGIC-010): the casting goal's release frame (BEGIN), an animation's
/// CALLBACK or END_CALLBACK, an interrupt (the maintain bar's or the script's stop), a forced end, a free (charged when the goal
/// ended before BEGIN), an end by event, the source detached or cleared — each of one run — and, of an object, the faint's
/// ending of its runs (demaintain) or the clearing of its runs' sources.</summary>
public enum SessionSpellRunActionKind
{
    Begin, Callback, EndCallback, Interrupt, ForceEnd, Free, FreeCharged, EndByEvent, DetachSource, ClearSource, Demaintain, ClearSources,
}

/// <summary>One action: its kind and the run it names, or the object for <see cref="SessionSpellRunActionKind.Demaintain"/> and
/// <see cref="SessionSpellRunActionKind.ClearSources"/>.</summary>
public sealed record SessionSpellRunAction(SessionSpellRunActionKind Kind, int Run = -1, string? Object = null);

/// <summary>A spell-run change: the disposition, the failure, the creation's answer (a start) and the rules' notes for the
/// host, and the version.</summary>
public sealed record SessionSpellRunResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SpellRunCreation? Creation,
    IReadOnlyList<SpellRunNote> Notes, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    // The original's run table (ARC-MAGIC-010). Not saved, like its magic-tech events, until the saved runs (MT9b).
    private SpellRunTable _spellRuns = SpellRunTable.Empty;
    private List<SessionSpellRunFiring>? _passSpellRuns;
    // Set while the run rules act (a call or a magic-tech event): a host port calling back into the runs is refused, so no
    // change is overwritten by the outer call's table.
    private bool _spellRunsBusy;
    // The last positive cost a payment charged (the original's global 0x5E762C, which its reflection charges, MG15); not saved.
    private int _spellLastCost;

    /// <summary>The session's spell runs (ARC-MAGIC-010; not saved).</summary>
    public SpellRunTable QuerySpellRuns() { ThrowIfDisposed(); return _spellRuns; }

    /// <summary>
    /// Starts a run (0x455AC0, 0x455C30, ARC-MAGIC-009's <see cref="SpellRunRules.Create"/>): the invocation check, a slot and
    /// the run; a run without a parent gets its BEGIN as a process event 2 ms later (the creation's), a run with one waits for
    /// its casting goal's release frame (<see cref="SessionSpellRunActionKind.Begin"/>). Refused, in order: a call from inside the
    /// run rules (a host port; <see cref="GameSessionFailure.InvalidSpellRun"/>); the version; the rules
    /// (<see cref="GameSessionFailure.SpellRunRefused"/> with the creation's status); the event budget; the revision.
    /// </summary>
    public SessionSpellRunResult StartSpellRun(GameSessionVersion expected, SpellRunRequest request, SpellRunRecord record, ISpellInvocationWorld world,
        ISpellRollSource rolls, bool magicDisabled = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        if (_spellRunsBusy) return RejectRun(GameSessionFailure.InvalidSpellRun);
        if (!IsCurrent(expected)) return RejectRun(GameSessionFailure.StaleVersion);
        var creation = SpellRunRules.Create(_spellRuns, request, record, world, rolls, _rules.Magic, magicDisabled);
        if (creation.Status != SpellRunCreationStatus.Created)
            return new(GameSessionDisposition.Rejected, GameSessionFailure.SpellRunRefused, creation, [], Version);
        SpellRunEvent[] queued = creation.BeginEvent ? [new(creation.Run!.Id, SpellRunEventCode.Process, SpellRunSystem.ParentlessBeginDelayMs)] : [];
        if (!SpellEventsFit(queued.Length)) return RejectRun(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return RejectRun(GameSessionFailure.RevisionExhausted);
        _spellRuns = creation.Table;
        QueueSpellEvents(queued);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, creation, [], Version);
    }

    /// <summary>
    /// Acts on the session's runs through the run rules (<see cref="SpellRunSystem"/>) with the host's world: the table changes,
    /// the events the rules queue are added, the ticks they remove are removed, and the notes are handed back. Refused, in order:
    /// an undefined action, a run outside 0..511, a missing object, or a call from inside the run rules (a host port;
    /// <see cref="GameSessionFailure.InvalidSpellRun"/>); the version; the event budget (the runs stay as they were; what the host's
    /// ports already did is the host's); the revision. An action that changes nothing is <see cref="GameSessionDisposition.NoChange"/>;
    /// its notes (payments, messages) still come back and are the host's to apply.
    /// </summary>
    public SessionSpellRunResult ActOnSpellRun(GameSessionVersion expected, ISpellRunHost host, SessionSpellRunAction action)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(action);
        var objectScoped = action.Kind is SessionSpellRunActionKind.Demaintain or SessionSpellRunActionKind.ClearSources;
        if (!Enum.IsDefined(action.Kind) || (objectScoped ? !SessionValidation.Entity(action.Object) : action.Run is < 0 or >= SpellRunTable.Slots)
            || _spellRunsBusy)
            return RejectRun(GameSessionFailure.InvalidSpellRun);
        if (!IsCurrent(expected)) return RejectRun(GameSessionFailure.StaleVersion);
        var system = NewSpellSystem(host);
        _spellRunsBusy = true;
        try { Act(system, action); }
        finally { _spellRunsBusy = false; }
        if (!system.Changed(_spellRuns))
        {
            // Its payments come back for the host to apply, so their last cost follows (not saved, no revision).
            _spellLastCost = system.LastCostCharged;
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, system.Notes, Version);
        }
        if (!SpellEventsFit(system.Queued.Count)) return RejectRun(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return RejectRun(GameSessionFailure.RevisionExhausted);
        CommitSpellSystem(system);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, system.Notes, Version);
    }

    private static void Act(SpellRunSystem system, SessionSpellRunAction action)
    {
        switch (action.Kind)
        {
            case SessionSpellRunActionKind.Begin: system.Begin(action.Run); break;
            case SessionSpellRunActionKind.Callback: system.SetAndProcess(action.Run, SpellRunAction.Callback); break;
            case SessionSpellRunActionKind.EndCallback: system.SetAndProcess(action.Run, SpellRunAction.EndCallback); break;
            case SessionSpellRunActionKind.Interrupt: system.Interrupt(action.Run); break;
            case SessionSpellRunActionKind.ForceEnd: system.ForceEnd(action.Run); break;
            case SessionSpellRunActionKind.Free: system.Free(action.Run, chargeBegin: false); break;
            case SessionSpellRunActionKind.FreeCharged: system.Free(action.Run, chargeBegin: true); break;
            case SessionSpellRunActionKind.EndByEvent: system.EndByEvent(action.Run); break;
            case SessionSpellRunActionKind.DetachSource: system.DetachSource(action.Run); break;
            case SessionSpellRunActionKind.ClearSource: system.ClearSource(action.Run); break;
            case SessionSpellRunActionKind.Demaintain: system.Demaintain(action.Object!); break;
            case SessionSpellRunActionKind.ClearSources: system.ClearSources(action.Object!); break;
        }
    }

    // A magic-tech event (0x459430): through the pass host's run rules, or consumed and reported not run.
    internal GameEventHandlerOutcome FireSpellRun(GameEventFiring firing)
    {
        var run = (int)firing.Event.Payload.Values[1];
        var code = (SpellRunEventCode)firing.Event.Payload.Values[2];
        if (_passWorld is not ISessionSpellWorld { SpellRuns: { } host } || _spellRunsBusy)
        {
            _passSpellRuns?.Add(new(firing.Event, run, code, false, []));
            return GameEventHandlerOutcome.Refused;
        }
        var system = NewSpellSystem(host);
        _spellRunsBusy = true;
        try { system.Fire(run, code); }
        finally { _spellRunsBusy = false; }
        if (!system.Changed(_spellRuns))
        {
            _spellLastCost = system.LastCostCharged;
            _passSpellRuns?.Add(new(firing.Event, run, code, true, system.Notes));
            return GameEventHandlerOutcome.Completed;
        }
        // The event left the queue; what the rules queue must fit, or the step is not applied.
        if (!SpellEventsFit(system.Queued.Count) || _revision == long.MaxValue)
        {
            _passSpellRuns?.Add(new(firing.Event, run, code, false, system.Notes));
            return GameEventHandlerOutcome.Refused;
        }
        CommitSpellSystem(system);
        Revise();
        _passSpellRuns?.Add(new(firing.Event, run, code, true, system.Notes));
        return GameEventHandlerOutcome.Completed;
    }

    private SpellRunSystem NewSpellSystem(ISpellRunHost host) =>
        new(_spellRuns, host, _rules.Magic, _events.QueryPending().Where(IsSpellTick).Select(e => (int)e.Payload.Values[1]), _spellLastCost);

    private static bool IsSpellTick(GameEvent e) => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[2] == (long)SpellRunEventCode.Tick;

    private bool SpellEventsFit(int added) =>
        _events.QueryPending().Count(e => e.Kind == SessionEventKinds.SpellRun) + added <= SessionEventKinds.MaxSpellRunEvents &&
        _events.Count + added <= _events.Limits.MaxPending && _events.NextSequence <= long.MaxValue - added;

    private void CommitSpellSystem(SpellRunSystem system)
    {
        _spellRuns = system.Table;
        _spellLastCost = system.LastCostCharged;
        foreach (var run in system.RemovedEvents)
            _events.RemoveAll(SessionEventKinds.SpellRun, e => e.Payload.Values[1] == run);
        foreach (var run in system.RemovedTicks)
            _events.RemoveAll(SessionEventKinds.SpellRun, e => e.Payload.Values[1] == run && e.Payload.Values[2] == (long)SpellRunEventCode.Tick);
        QueueSpellEvents(system.Queued);
    }

    private void QueueSpellEvents(IReadOnlyList<SpellRunEvent> events)
    {
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        foreach (var e in events)
        {
            var scheduled = _events.ScheduleAfter(SessionEventKinds.SpellRun, GameEventPayload.Of([], now, e.Run, (long)e.Code), Math.Max(1, e.DelayMs),
                new(0, now, 0));
            // Every refusal was checked before (the kind keeps any number), so a failure here is a defect.
            if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("An admitted magic-tech event could not be scheduled.");
        }
    }

    /// <summary>
    /// ARC-MAGIC-014: a spell payment's fatigue (<see cref="SpellPayment.FatigueDamageAdded"/>, the cast host's payment of a run's
    /// stage or a reflection): the payer's fatigue damage grows by it through the life domain, as the original's payment writes it
    /// (0x450420; losing consciousness ends a subturn like any life step). 1..1,000,000.
    /// </summary>
    public SessionLifeResult PaySpellFatigue(GameSessionVersion expected, SessionLifeTarget payer, int fatigue)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(payer);
        return Life(expected, payer, fatigue is >= 1 and <= LifeLimits.MaxAmount, (state, facts, _) => LifeRules.ChargeFatigue(state, facts, fatigue));
    }

    private SessionSpellRunResult RejectRun(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, [], Version);
}
