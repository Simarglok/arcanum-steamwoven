using Arcanum.Simulation.Character;
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
    // ARC-MAGIC-026: the local player's maintain bar (the original's table 0x5CB3A8), changed with the runs; not saved, like them.
    private MaintainBar _maintainBar = MaintainBar.Empty;
    // The run rules acting now (a call or a magic-tech event): the bar question of a host port is answered against the bar after
    // the notes they left so far (a run ended earlier in the same pass frees its slot first, as in the original).
    private SpellRunSystem? _activeSpellSystem;

    /// <summary>The session's spell runs (ARC-MAGIC-010; not saved).</summary>
    public SpellRunTable QuerySpellRuns() { ThrowIfDisposed(); return _spellRuns; }

    /// <summary>ARC-MAGIC-026: the local player's maintain bar (not saved, like the runs).</summary>
    public MaintainBar QueryMaintainBar() { ThrowIfDisposed(); return _maintainBar; }

    /// <summary>ARC-MAGIC-026: the maintain bar's open slots (0x57C4B0): the hero's effective intelligence / 4, at most 5; all 5
    /// without a hero, as the slot draw takes it (0x5533C0).</summary>
    public int QueryMaintainBarOpenSlots()
    {
        ThrowIfDisposed();
        return HeroEffectiveStat(CharacterStat.Intelligence) is { } intelligence ? MaintainBarRules.OpenSlots(intelligence) : MaintainBarRules.Slots;
    }

    /// <summary>
    /// ARC-MAGIC-026: the answer of the maintain bar to a run's maintenance tick (0x4604A0 → 0x57C290), for a host's
    /// <see cref="ISpellRunHost.MaintainBarHasSlot"/>: whether the bar, after the notes the acting run rules left so far, shows the run
    /// or has an empty open slot for it. The add itself follows the rules' <see cref="SpellRunNoteKind.MaintainBarAdd"/> note when
    /// the change is committed.
    /// </summary>
    public bool MaintainBarTakes(SpellRun run)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(run);
        var intelligence = HeroEffectiveStat(CharacterStat.Intelligence) ?? 0;
        var bar = _activeSpellSystem is { } system ? MaintainBarRules.Apply(_maintainBar, system.Notes, id => system.Table[id], intelligence) : _maintainBar;
        return MaintainBarRules.Add(bar, run.Id, run.Spell, intelligence).Added;
    }

    /// <summary>
    /// ARC-MAGIC-026: a click on maintain-bar slot <paramref name="slot"/> (0x57C370): a slot showing a run turns to stopping and its
    /// run is interrupted through the run rules (<see cref="SessionSpellRunActionKind.Interrupt"/>, 0x457110) with the host's world;
    /// the run's end frees the slot. Refused, in order: a slot outside 0..4 or a call from inside the run rules
    /// (<see cref="GameSessionFailure.InvalidSpellRun"/>); the version. A slot that shows no run is NoChange; otherwise as
    /// <see cref="ActOnSpellRun"/>, and the slot's stopping state is a change even when the interrupt changes no run.
    /// </summary>
    public SessionSpellRunResult StopMaintainedSpell(GameSessionVersion expected, ISpellRunHost host, int slot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(host);
        if (slot is < 0 or >= MaintainBarRules.Slots || _spellRunsBusy) return RejectRun(GameSessionFailure.InvalidSpellRun);
        if (!IsCurrent(expected)) return RejectRun(GameSessionFailure.StaleVersion);
        if (MaintainBarRules.Stop(_maintainBar, slot) is not var (stopping, run))
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, [], Version);
        return ActCore(host, new(SessionSpellRunActionKind.Interrupt, run), stopping);
    }

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
        return ActCore(host, action, _maintainBar);
    }

    // An admitted action on the runs, starting from <paramref name="bar"/> (the session's bar, or the bar with a clicked slot
    // stopping, ARC-MAGIC-026).
    private SessionSpellRunResult ActCore(ISpellRunHost host, SessionSpellRunAction action, MaintainBar bar)
    {
        var before = _maintainBar;
        var system = NewSpellSystem(host);
        _spellRunsBusy = true;
        _activeSpellSystem = system;
        try { _maintainBar = bar; Act(system, action); }
        finally { _spellRunsBusy = false; _activeSpellSystem = null; _maintainBar = before; }
        if (!system.Changed(_spellRuns))
        {
            // Its payments come back for the host to apply, so their last cost follows (not saved, no revision).
            _spellLastCost = system.LastCostCharged;
            // Only the bar can have changed (a clicked slot's stopping state with an interrupt that changed no run; defensive, as an
            // interrupt of a run the bar holds always sets it).
            var after = MaintainBarAfter(system, bar);
            if (after.SameAs(_maintainBar)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, system.Notes, Version);
            if (_revision == long.MaxValue) return RejectRun(GameSessionFailure.RevisionExhausted);
            _maintainBar = after;
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, system.Notes, Version);
        }
        if (!SpellEventsFit(system.Queued.Count)) return RejectRun(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return RejectRun(GameSessionFailure.RevisionExhausted);
        CommitSpellSystem(system, bar);
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
        _activeSpellSystem = system;
        try { system.Fire(run, code); }
        finally { _spellRunsBusy = false; _activeSpellSystem = null; }
        if (!system.Changed(_spellRuns))
        {
            // No bar change without a table change here: a removal comes with its run's end or free, an add with its tick.
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
        CommitSpellSystem(system, _maintainBar);
        Revise();
        _passSpellRuns?.Add(new(firing.Event, run, code, true, system.Notes));
        return GameEventHandlerOutcome.Completed;
    }

    // ARC-MAGIC-026: the bar after the run rules' notes (adds and removes in order, the freed runs' slots emptied).
    private MaintainBar MaintainBarAfter(SpellRunSystem system, MaintainBar bar) =>
        MaintainBarRules.Apply(bar, system.Notes, id => system.Table[id], HeroEffectiveStat(CharacterStat.Intelligence) ?? 0);

    /// <summary>The hero's effective stat with its life's poison and crippled legs (the cast host's and the maintain bar's reading);
    /// null without a hero.</summary>
    internal int? HeroEffectiveStat(CharacterStat stat)
    {
        if (QueryHeroLife() is not { } life) return null;
        var state = life.Record.State;
        return QueryHeroStats(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)))?.Snapshot?.GetEffective(stat);
    }

    private SpellRunSystem NewSpellSystem(ISpellRunHost host) =>
        new(_spellRuns, host, _rules.Magic, _events.QueryPending().Where(IsSpellTick).Select(e => (int)e.Payload.Values[1]), _spellLastCost);

    private static bool IsSpellTick(GameEvent e) => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[2] == (long)SpellRunEventCode.Tick;

    private bool SpellEventsFit(int added) =>
        _events.QueryPending().Count(e => e.Kind == SessionEventKinds.SpellRun) + added <= SessionEventKinds.MaxSpellRunEvents &&
        _events.Count + added <= _events.Limits.MaxPending && _events.NextSequence <= long.MaxValue - added;

    private void CommitSpellSystem(SpellRunSystem system, MaintainBar bar)
    {
        _maintainBar = MaintainBarAfter(system, bar);
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

    /// <summary>ARC-MAGIC-022: the spell charges (field 0x69) the session left on <paramref name="entity"/> after spending some, or null
    /// when it spent none (the item's own field holds them). A charge left also means the item's magical flag (0x4) is set.</summary>
    public int? QueryItemCharges(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) && _durability.Charges.TryGetValue(entity, out var left) ? left : null;
    }

    /// <summary>
    /// ARC-MAGIC-022: a spell payment's spent charge (<see cref="SpellPayment.ChargeSpent"/>, the cast host's payment of an item source):
    /// one of the item's positive charges goes, from the session's count or else <paramref name="baseline"/> (the item's own field), and
    /// the item is marked magical (0x450420, ledger DV1). Refused for an invalid entity, a baseline outside 1..1,000,000 while the session
    /// holds no count
    /// (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an item the session does not track or deleted
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>) and a full record budget. No charge left is NoChange (the payment refuses
    /// that before it spends). One revision.
    /// </summary>
    public GameSessionMutationResult SpendItemCharge(GameSessionVersion expected, string entity, int baseline)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        var recorded = _durability.Charges.TryGetValue(entity, out var left);
        // The baseline matters only until the session holds a count.
        if (!recorded && baseline is < 1 or > SessionItemUseLimits.MaxUses) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (!TracksItem(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        var current = recorded ? left : baseline;
        if (current <= 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (!recorded && _durability.Count >= _limits.MaxDurabilityRecords) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _durability.Charges[entity] = current - 1;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-MAGIC-022: the destruction of a food or scroll the hero used (0x462CC0's tail, 0x43CCA0; the cast host clears the runs'
    /// sources, 0x4574D0): the item leaves the hero's lists and is deleted, as a destroying break deletes an item. Refused for an
    /// invalid entity (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an item the hero does not hold or wears
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>), an item that is not food or a scroll (<see cref="GameSessionFailure.InventoryMismatch"/>)
    /// and a full durability budget (the deletion's record). The item's destroy script (point 2) is the host's; the session does not
    /// run it. One revision.
    /// </summary>
    public GameSessionMutationResult DestroyUsedItem(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (HeldKind(entity) is not { } kind || _equipment.SlotOf(entity) is not null)
            return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        if (kind.Type is not (SessionItemType.Food or SessionItemType.Scroll)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InventoryMismatch, Version);
        if (NewDurabilityRecord(entity, destroyed: true) && _durability.Count >= _limits.MaxDurabilityRecords)
            return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        Delete(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    private SessionSpellRunResult RejectRun(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, [], Version);
}
