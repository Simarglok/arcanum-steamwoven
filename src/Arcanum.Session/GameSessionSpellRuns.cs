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
/// ending of its runs (demaintain), the clearing of its runs' sources or (ARC-DEV-259) the runs' share of its destruction.</summary>
public enum SessionSpellRunActionKind
{
    Begin, Callback, EndCallback, Interrupt, ForceEnd, Free, FreeCharged, EndByEvent, DetachSource, ClearSource, Demaintain, ClearSources, ObjectGone,
    /// <summary>ARC-MAGIC-034 (MA-3): the runs' half of the Dispel (0x451C40) on the object, the run being processed (<see cref="SessionSpellRunAction.Run"/>, -1 none) left alone.</summary>
    Dispel,
}

/// <summary>One action: its kind and the run it names (for <see cref="SessionSpellRunActionKind.ObjectGone"/> and <see cref="SessionSpellRunActionKind.Dispel"/>, the run being processed, left alone; -1 none), or the object for <see cref="SessionSpellRunActionKind.Demaintain"/> and
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
        var objectScoped = action.Kind is SessionSpellRunActionKind.Demaintain or SessionSpellRunActionKind.ClearSources or SessionSpellRunActionKind.ObjectGone or SessionSpellRunActionKind.Dispel;
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
        finally { _spellRunsBusy = false; _activeSpellSystem = null; _maintainBar = before; DrainCritterStarts(); }
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
            case SessionSpellRunActionKind.ObjectGone: system.ObjectGone(action.Object!, action.Run); break;
            case SessionSpellRunActionKind.Dispel: system.Dispel(action.Object!, action.Run); break;
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
        finally { _spellRunsBusy = false; _activeSpellSystem = null; DrainCritterStarts(); }
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
        return QueryHeroStats(HeroStatContext(Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)))?.Snapshot?.GetEffective(stat);
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
        // ARC-DEV-352 (SUM7): a freed run's summoned list goes with its slot; the objects stay.
        foreach (var note in system.Notes)
            if (note.Kind == SpellRunNoteKind.Freed) _inventory.Objects.ReleaseRun(note.Run);
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
                EventClocks(now));
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
    /// (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an item the session deleted
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>), the 65,537th count (<see cref="GameSessionLimits.MaxItemCharges"/>, which no other record shares) and an exhausted revision counter.
    /// ARC-MAGIC-2434: the original's payment always writes field 105 of the source it was given, so the spend needs no tracking: the host's item lookup names the item, whatever seed
    /// the session has attached, and a full durability budget cannot lose it. No charge left is NoChange (the payment refuses that before it spends). One revision.
    /// </summary>
    public GameSessionMutationResult SpendItemCharge(GameSessionVersion expected, string entity, int baseline)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        var recorded = _durability.Charges.TryGetValue(entity, out var left);
        // The baseline matters only until the session holds a count.
        if (!recorded && baseline is < 1 or > SessionItemUseLimits.MaxUses) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (IsItemDestroyed(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        var current = recorded ? left : baseline;
        if (current <= 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (!recorded && _durability.Charges.Count >= _limits.MaxItemCharges) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _durability.Charges[entity] = current - 1;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-MAGIC-1146 (the Recharge component): the item's spell charges (field 0x69) grow by <paramref name="amount"/>, not above <paramref name="maximum"/> and only while they are below it: from the session's count
    /// or else <paramref name="baseline"/> (the item's own field), as <see cref="SpendItemCharge"/> keeps it. A count that already reaches the maximum is <see cref="GameSessionDisposition.NoChange"/>, as is a sum equal to it.
    /// Refused for an invalid entity, a baseline or a resulting count outside the saved bounds 0..1,000,000 (<see cref="GameSessionFailure.InvalidItem"/>; the original's negative count, unlimited charges, is not kept by the session),
    /// a stale version, an item the session deleted (<see cref="GameSessionFailure.ItemNotAvailable"/>), a full charge budget (<see cref="GameSessionLimits.MaxItemCharges"/>) and an exhausted revision counter. One revision.
    /// ARC-MAGIC-2443: the original's handler always writes field 105 of an item target, so the recharge asks no tracked item: the host's item lookup names the item and its own field is the baseline, whatever map seed the session has attached (as <see cref="SpendItemCharge"/>, ARC-MAGIC-2434). Like a spent charge, a recorded count also marks the item magical
    /// when the host reads it (<see cref="QueryItemCharges"/>): the original's recharge writes the field only.
    /// </summary>
    public GameSessionMutationResult RechargeItemCharges(GameSessionVersion expected, string entity, int baseline, int amount, int maximum)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        var recorded = _durability.Charges.TryGetValue(entity, out var left);
        var current = recorded ? left : baseline;
        if (current is < 0 or > SessionItemUseLimits.MaxUses) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (IsItemDestroyed(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        // 0x454920: charges that already reach the maximum are left alone; below it the sum is clipped at the maximum.
        if (current >= maximum) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        var sum = (int)Math.Min((long)current + amount, maximum);
        if (sum is < 0 or > SessionItemUseLimits.MaxUses) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (sum == current) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (!recorded && _durability.Charges.Count >= _limits.MaxItemCharges) return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _durability.Charges[entity] = sum;
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
    public GameSessionMutationResult DestroyUsedItem(GameSessionVersion expected, string entity, ISessionItemScriptHost? scripts = null)
    {
        var destroyed = DestroyUsedItemWithLines(expected, entity, scripts);
        return new(destroyed.Disposition, destroyed.Failure, destroyed.Version);
    }

    /// <summary>
    /// ARC-SCRIPT-2881: <see cref="DestroyUsedItem"/> with the lines the removal's remove-item script (28) printed (<see cref="InventoryMutationResult.Messages"/>, in order, with the run summaries in
    /// <see cref="InventoryMutationResult.Scripts"/>) for the host to post as it posts any script's printed line; the result's <see cref="InventoryMutationResult.Item"/> is the destroyed entity.
    /// </summary>
    public InventoryMutationResult DestroyUsedItemWithLines(GameSessionVersion expected, string entity, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (UsedItemDestructionRefusalCore(entity) is { } refusal) return RejectItem(refusal);
        if (scripts is null && HeroItemPoints(entity).Contains(SessionScriptPoint.RemoveItem)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        // ARC-SCRIPT-2854 (IE3): with a script host the removal's remove-item script (28) runs with the hero as the triggerer, made and prepared before anything applies and committed with the deletion.
        var removal = scripts is null ? new RemovalScripted(null, null) : RunRemovalScript(entity, null, HeroItemPoints(entity), scripts);
        if (removal.Done is { } refused) return refused;
        removal.Runs?.Commit();
        DestroyObject(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, entity, null, Version) { Scripts = removal.Runs?.Runs ?? [], Messages = removal.Runs?.Messages ?? [] };
    }

    // The script points the session knows of an item the hero holds (its seed row's, the slots kept or inherited).
    private IReadOnlyList<int> HeroItemPoints(string entity) => HeldKind(entity) is { } kind ? HeroNamedItem(entity, kind).ScriptPoints : [];

    /// <summary>ARC-SCRIPT-1481: why <see cref="DestroyUsedItem"/> would refuse the item (not held or worn: <see cref="GameSessionFailure.ItemNotAvailable"/>; not food or a scroll: <see cref="GameSessionFailure.InventoryMismatch"/>;
    /// full durability budget: <see cref="GameSessionFailure.BudgetExceeded"/>), or null when it would destroy it; changes nothing. A use script run checks it before it applies any effect.</summary>
    public GameSessionFailure? UsedItemDestructionRefusal(string entity, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (UsedItemDestructionRefusalCore(entity) is { } refusal) return refusal;
        // ARC-SCRIPT-2854: with a script host the dry run of the removal's remove-item script finds a run the session cannot apply before the calling script applies anything.
        // Without one an item with a remove-item script is refused, never deleted with the script skipped (the spell path's spent scroll or food: #2881).
        if (scripts is null && HeroItemPoints(entity).Contains(SessionScriptPoint.RemoveItem)) return GameSessionFailure.ItemScriptUnsupported;
        if (scripts is not null && RunRemovalScript(entity, null, HeroItemPoints(entity), scripts).Done is { } dry) return dry.Failure;
        return null;
    }

    // The refusals of the destruction that no script decides.
    private GameSessionFailure? UsedItemDestructionRefusalCore(string entity)
    {
        if (!SessionValidation.Entity(entity)) return GameSessionFailure.InvalidItem;
        if (HeldKind(entity) is not { } kind || _equipment.SlotOf(entity) is not null) return GameSessionFailure.ItemNotAvailable;
        if (kind.Type is not (SessionItemType.Food or SessionItemType.Scroll)) return GameSessionFailure.InventoryMismatch;
        if (NewDurabilityRecord(entity, destroyed: true) && _durability.Count >= _limits.MaxDurabilityRecords) return GameSessionFailure.BudgetExceeded;
        return null;
    }

    /// <summary>
    /// ARC-DEV-259 (B14): the destruction of an item lying on the ground, which the hero placed (thrown, dropped, made, kit) or an NPC's seed item
    /// lying where it was dropped or thrown (ARC-DEV-262, <see cref="QueryDroppedItem"/>), by a spell's
    /// Destroy component (0x451AF0 → 0x43CCA0): a thrown grenade destroys itself this way after its explosion. The item leaves the ground and is
    /// deleted, as a destroying break deletes an item. Refused for an invalid entity (<see cref="GameSessionFailure.InvalidItem"/>), a stale
    /// version, an item that is not on the ground (<see cref="GameSessionFailure.ItemNotAvailable"/>: a held, worn or already destroyed one is
    /// left as it is, as the original leaves a destroyed object) and a full durability budget. The item's destroy script (point 2) and the
    /// spell runs that name the item (<see cref="SessionSpellRunActionKind.ObjectGone"/>) are the host's. One revision.
    /// </summary>
    public GameSessionMutationResult DestroyLyingItem(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        // ARC-DEV-262: an NPC's seed item lying where a critical dropped it or its NPC threw it is on the ground too.
        if (_inventory.PlacedItem(entity) is null && DroppedItemOf(entity) is null || _durability.Destroyed.Contains(entity))
            return new(GameSessionDisposition.Rejected, GameSessionFailure.ItemNotAvailable, Version);
        if (NewDurabilityRecord(entity, destroyed: true) && _durability.Count >= _limits.MaxDurabilityRecords)
            return new(GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        DestroyObject(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-DEV-301: where the session tells a cast host that a critter's life step ended or detached its spell runs (set by <see cref="SessionSpellCaster"/>; none: nobody is told). The runs themselves are the host's to act on
    /// (the run rules need its world, and the session refuses a run action while they act), so the step only says which ending it calls for: <see cref="SessionSpellRunActionKind.Demaintain"/> or
    /// <see cref="SessionSpellRunActionKind.ObjectGone"/>, with the critter's entity (the hero's is the player entity).
    /// </summary>
    internal Action<SessionSpellRunActionKind, string>? CritterRunEnd { get; set; }

    /// <summary>
    /// ARC-DEV-312 (exe read 2026-11-10, ledger DY1–DY3): the host's dying script (attachment point 12, 0x45DA57 inside the kill 0x45DA20) of an NPC whose life step just killed it, asked after the hit points are
    /// written and before the death processing (the death time, the combat mode exit, the spell runs' ending, the wait timer, and in the callers the experience, the kill log and the kill notice). The request names the
    /// NPC, the map its step ran on and the killer (<see cref="SessionSpellCaster.Hero"/> for the hero, none for a damage with no source); it returns true for run default (the death processing goes on) and false for skip default,
    /// which stops the whole processing (<see cref="SessionLifeResult.DeathStopped"/>). None set: the processing goes on as before. The hero's death has no script here. The host runs inside the life step: it may write the session
    /// (the script's own stores) but not apply the same victim's life again.
    /// </summary>
    public Func<SessionDyingRequest, bool>? DyingScript { get; set; }

    /// <summary>
    /// ARC-SCRIPT-2859 (exe read 2026-11-19, LK1–LK3): the host's leader-killing script (attachment point 20, 0x14) of one follower of the killer's PC leader, asked in the kill step (0x45DA20) after the kill's experience award (and, on the attack paths, the kill log; the session has no kill alignment step): for each direct follower of the hero, in array order, when an NPC the hero or a follower the hero leads killed has died and its death was processed (a death the dying script stopped asks nothing). The request names the dead NPC
    /// (the triggerer), the map the step ran on and the follower (the attachee); the PC leader is the extra object, always the hero. The answer is ignored, so the host returns nothing; a host that throws is not asked again for the other
    /// followers of that death. None set: nothing runs. The host runs inside the damage step: it may write the session (the script's own stores) but not apply the same victim's life again.
    /// </summary>
    public Action<SessionLeaderKillingRequest>? LeaderKillingScript { get; set; }

    /// <summary>
    /// ARC-COMBAT-1141 (GT198, GT210–GT212): the host's taking-damage script (attachment point 0x17) of an NPC whose damage the session applies from its own life calls that no hit path asks for: the script kill
    /// <see cref="GameSessionOwner.Kill"/> (0x45D900) and the poison damage tick (0x4B1170, <see cref="GameSessionOwner.PoisonDamageTick"/> and its event), both with no attacker. It is asked at the start of the damage apply, before the
    /// life step is computed; true is run default (the step goes on), false skip default: a vetoed kill takes no hit points and starts no death, and still pins the hit-point damage at 32000
    /// (<see cref="LifeRules.KillVetoed"/>), a vetoed poison tick writes nothing and the tick goes on. The hero has no script here and is never asked; a host that throws counts as run default. None set: no question. The host runs before
    /// the life step: it may write the session (a version it moved is followed), but must not apply the same victim's life.
    /// </summary>
    public Func<SessionTakingDamageRequest, bool>? TakingDamageScript { get; set; }

    /// <summary>
    /// ARC-SCRIPT-2853 (exe read, ledger PI1, VP6, TQ19; 0x461AB0 → 0x441980): the host's drop script (attachment point 5) of an item that a critter drops through a critical, an arm hit or a spell's weapon drop, asked after the drop's distance is drawn and before the no-drop check:
    /// the holder is the triggerer, the item the attachee. <see cref="SessionDropScriptAnswer.SkipDefault"/> refuses the drop (<see cref="SessionCriticalStepOutcome.Refused"/>, the item stays), <see cref="SessionDropScriptAnswer.NotModelled"/> leaves it unapplied
    /// (<see cref="SessionCriticalStepOutcome.ScriptUnsupported"/>). The host may write the session (a version it moved is followed). A host that throws counts as not modelled. None set: an item with a drop script stays unapplied, as before. The body decay's drop runs no drop script (0x463860, ledger VP24).
    /// </summary>
    public Func<SessionDropScriptRequest, SessionDropScriptAnswer>? DropScript { get; set; }

    /// <summary>
    /// ARC-SCRIPT-2856 (exe read 0x467860, ledger PI2, VP1, TQ20): the host's item script context of an NPC as the wearer of the item event scripts its worn item's removal runs (<see cref="SessionNpcWieldScripts"/>: the item script host, the NPC's life target and
    /// stored stats; null when the host does not know the NPC). A critter drop (a critical, an arm hit, a spell's weapon block) of an NPC's worn item that has a wield-off (25) or remove-item (28) script runs them with the NPC as the triggerer through it and applies their
    /// changes with the drop; with none set, or for a script the session cannot apply, the item stays (<see cref="SessionCriticalStepOutcome.ScriptUnsupported"/>).
    /// </summary>
    public Func<string, SessionNpcWieldScripts?>? NpcWearerScripts { get; set; }

    /// <summary>ARC-SCRIPT-2856: where the host takes the printed lines and the eye candy requests of the item scripts a drop's removal ran (<see cref="NpcWearerScripts"/>), after their changes are applied; the drop's results carry no such lines.</summary>
    public Action<IReadOnlyList<SessionScriptMessage>, IReadOnlyList<Arcanum.Scripting.Scr.ScrEyeCandy>>? ItemRunOutput { get; set; }

    /// <summary>
    /// ARC-AI-1530 (exe read 2026-11-13, ledger VB2-6): the host's enter-combat (slot 13, 0x4AAE29) and exit-combat (slot 14, 0x4AAF32) script of an NPC the danger setter 0x4AABE0 has just put into or taken out of FIGHT, the target (the
    /// new one, or the requested one on an exit, none when it has none) as the triggerer and the NPC as the attachee. It runs after the setter's writes are committed and its answer is ignored, as the original's: for an entering call the state bits are
    /// written before it and rules 8 (the reaction lowered toward the hero) and 9 (the focus := the target) after it, so the session holds those two back until the script has run. With a host set, every setter call of the session (the host's own, the attack
    /// event, the follower rule, the orders) asks it; the host runs inside the commit, after the revision: it may write the session. None set: no script and the rules are written with the rest.
    /// </summary>
    public Action<SessionDangerHook>? DangerHookScript { get; set; }

    /// <summary>
    /// ARC-AI-1134 (exe read 2026-11-12, ledger DY3, DY7): the host's kill notification (0x4AA1B0 inside the kill 0x45DA20) of an NPC whose death the life step processed (the dying script allowed it). The session has already
    /// written the victim's combat focus (field 0x11B) := the killer and committed the step's revision; the host then runs <see cref="Arcanum.Simulation.Ai.MoraleRules.KillNotification"/> over its world and applies the plan
    /// (<see cref="ApplyKillNotification"/>), and ends the victim's flee goals. The request names the NPC, the map its step ran on and the killer (<see cref="SessionSpellCaster.Hero"/> for the hero, none for a damage with no source).
    /// A host that throws counts as no notification. None set: nobody is told. The hero's death has none (the kill's NPC branch only).
    /// </summary>
    public Action<SessionKillNoticeRequest>? KillNotice { get; set; }

    /// <summary>
    /// ARC-DEV-301 (exe read 2026-11-08): the two endings of a critter's runs that the original's life paths call. The fatigue setter (0x45D5A0) takes a critter that was not unconscious to unconscious and demaintains it (0x457450: the
    /// runs it is the source of, but those of technology or with item triggers and no maintenance period, end by event); this is the same transition that fires its going unconscious (<see cref="Arcanum.Simulation.Magic.ItemSpellTriggerRules.LifeStep"/>), so a knock-out
    /// by damage that leaves the fatigue at zero, a payment or a drain counts alike. The kill (0x45DA20, after its dying script allowed it) tells the runs the critter is gone (0x459740, <see cref="SpellRunSystem.ObjectGone"/>): the runs it is the parent of end by event, those it is the source of lose the source and the active
    /// runs that name it as their other object end. A critter that both goes unconscious and dies in one step does both, in that order.
    /// </summary>
    // ARC-DEV-312: the kill's ending (ObjectGone) is told only once the dying script allowed the death (killRuns); the fatigue setter's demaintain comes with the damage, before the script.
    private void TellCritterRunEnds(SessionLifeTarget target, LifeOutcome outcome, LifeFacts facts, bool killRuns = true, bool demaintain = true)
    {
        if (CritterRunEnd is not { } tell) return;
        var entity = target.IsHero ? SessionEventKinds.PlayerEntity : target.Entity!;
        if (demaintain && !LifeRules.IsUnconscious(outcome.Before, facts) && LifeRules.IsUnconscious(outcome.After, facts)) tell(SessionSpellRunActionKind.Demaintain, entity);
        if (killRuns && outcome.Events.HasFlag(LifeEvents.Died)) tell(SessionSpellRunActionKind.ObjectGone, entity);
    }

    private SessionSpellRunResult RejectRun(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, [], Version);
}
