using System.Collections.Immutable;

namespace Arcanum.Simulation.Magic;

/// <summary>
/// What a magic-tech event (kind 5) does when it fires (0x459430): a tick processes the run, a BEGIN run turning to MAINTAIN
/// (0x459490); an interrupt ends it (0x457110); a process event processes its current action (the BEGIN of a run without a
/// parent, queued by the creation 2 ms later).
/// </summary>
public enum SpellRunEventCode { Tick = 1, Interrupt = 2, Process = 3 }

/// <summary>A magic-tech event to queue: the run, the code and the delay in game milliseconds.</summary>
public readonly record struct SpellRunEvent(int Run, SpellRunEventCode Code, long DelayMs);

/// <summary>One entry of a run's target list (MT8): the object (none for a tile entry) and the tile.</summary>
public sealed record SpellRunTarget(SpellRunObject? Object, SpellLocation Tile);

/// <summary>A component's gate (MT8): whether its area passes for the target, the target after its re-aim (kept for the later
/// components) and the component's item triggers.</summary>
public readonly record struct SpellComponentGate(bool Passes, SpellRunTarget Target, int Triggers = 0);

/// <summary>How a component let go of the run's source during the pass: cleared (0x457530) or detached (0x4572F0, the parent
/// too when it is the source; no final END mark inside the run's own pass).</summary>
public enum SpellSourceRelease { None, Clear, Detach }

/// <summary>
/// What a component did (the component table 0x5B0BA8, MT12–MT18): the spell flags it set on the run (+0x138), the component
/// it jumps to (the loop goes on after it), whether it let go of the run's source, and an eye candy's callback — pending while
/// its animation plays (the animation calls back), or now (the animation could not start: the callback action runs at once,
/// the END callback when <see cref="EndCallback"/>).
/// </summary>
public sealed record SpellComponentOutcome(int SetSpellFlags = 0, int? JumpTo = null, SpellSourceRelease ReleasesSource = SpellSourceRelease.None,
    bool CallbackPending = false, bool CallbackNow = false, bool EndCallback = false)
{
    public static SpellComponentOutcome None { get; } = new();
}

/// <summary>What the host must do or show after the run rules acted.</summary>
public enum SpellRunNoteKind
{
    /// <summary>Apply the stage's payments (<see cref="SpellRunNote.Payments"/>).</summary>
    Payments,
    /// <summary>Show the local player spell.mes <see cref="SpellRunNote.Value"/> (601 the maintenance, 602 the checks, the shell).</summary>
    Message,
    /// <summary>Refresh the local player's maintain bar for the run (0x4604C0).</summary>
    MaintainBar,
    /// <summary>Add the run to the local player's maintain bar (0x4604A0; it had a free slot).</summary>
    MaintainBarAdd,
    /// <summary>A target resisted (0x45A520): the host's feedback.</summary>
    Resisted,
    /// <summary>A component applied to a target: the AI notices (0x453EE0, MT8's rule) are the host's.</summary>
    AiNotice,
    /// <summary>The run was freed; its tick events are removed.</summary>
    Freed,
    /// <summary>The local player was the run's target: its interface is refreshed (0x4601C0).</summary>
    TargetRefresh,
    /// <summary>The processing refused a run while another one was processed (RN8): nothing happened to it.</summary>
    ReEntryRefused,
    /// <summary>The fate "spell at maximum" was used for the run (both aptitudes 100).</summary>
    SpellAtMaximum,
}

/// <summary>A host note: its kind, the run, a value (a message number, a component) and the target or payments it concerns.</summary>
public sealed record SpellRunNote(SpellRunNoteKind Kind, int Run, int Value = 0, SpellRunTarget? Target = null,
    IReadOnlyList<SpellPayment>? Payments = null);

/// <summary>
/// The world a run's processing asks (MT9a-2): the record, the facts and ports of the stage (MT9a-1), the BEGIN flags check
/// (MT7's 0x456430), the source's spell flags, the local player, the target list (MT8's collection), the fate "spell at
/// maximum", the per-target stage (MT10), the resistance (MT6), the component gate (MT8's test and re-aim) and the component
/// itself (MT12–MT18), the time facts and stats of the scheduler, the parent's intelligence for the cap, the local player's
/// maintain bar and the BEGIN cost of a run freed before its BEGIN.
/// </summary>
public interface ISpellRunHost
{
    SpellRunRecord Record(int spell);

    SpellStageFacts StageFacts(SpellRun run);

    ISpellCellSource Cells { get; }

    ISpellRunStacking Stacking { get; }

    /// <summary>The forbidden-flags check at BEGIN (0x456430 with the parent, the target and the record).</summary>
    bool FlagsAllow(SpellRun run);

    /// <summary>The source's spell flags (field 0x14) now; 0 without a source.</summary>
    int SourceSpellFlags(SpellRun run);

    bool IsLocalPlayer(SpellRunObject? who);

    /// <summary>The action's target list (0x4F40B0 over the run's context, MT8).</summary>
    IReadOnlyList<SpellRunTarget> Collect(SpellRun run);

    /// <summary>Uses the PC's armed fate "spell at maximum" (kind 5, 0x4F5270): true when it was armed.</summary>
    bool UseSpellAtMaximum(SpellRunObject pc);

    /// <summary>The per-target stage (0x455550, MT10): false skips the target.</summary>
    bool PerTarget(SpellRun run, SpellRunTarget target);

    /// <summary>The resistance (0x4537B0, MT6): false when the target resisted.</summary>
    bool TakesEffect(SpellRun run, SpellRunTarget target);

    SpellComponentGate Gate(SpellRun run, int component, SpellRunTarget target);

    SpellComponentOutcome Apply(SpellRun run, int component, SpellRunTarget target);

    SpellTimeFacts TimeFacts(SpellRun run);

    /// <summary>A PC's or NPC's effective stat (0x4B0490); null when the object is gone.</summary>
    int? Stat(SpellRunObject who, int stat);

    /// <summary>Whether the local player's maintain bar has a free slot for the run (0x4604A0's answer; the add itself is the
    /// <see cref="SpellRunNoteKind.MaintainBarAdd"/> note).</summary>
    bool MaintainBarHasSlot(SpellRun run);

    /// <summary>The BEGIN cost a run freed before its BEGIN pays (0x4507D0); its payments.</summary>
    IReadOnlyList<SpellPayment> ChargeBegin(SpellRun run);
}

/// <summary>
/// ARC-MAGIC-010 (magic plan MT9a-2): the original's run processing, scheduler, events and interrupts over a run table, as the
/// 1.0.7.4 executable has them (contract docs/world/spell-run-rules.md "Processing and scheduling", ledger "Spell run
/// processing", RP rows). One instance per host call: it holds the table, the run being processed (the re-entrancy guard
/// 0x5B0BA4) and the callback guard (0x5E7630), and collects the events to queue, the runs whose tick events to remove and
/// the host notes. Universal: the world, the components and the per-target stage are the host's ports.
/// </summary>
public sealed class SpellRunSystem
{
    /// <summary>The delay of an interrupt event (0x4571E0) and of the BEGIN of a run without a parent (the creation).</summary>
    public const long InterruptDelayMs = 1, ParentlessBeginDelayMs = 2;
    /// <summary>The maintenance ticks of a run without a source when the record names no trigger count.</summary>
    public const int SourcelessTicks = 8;
    /// <summary>The spell flag of the anti-magic shell (OSF_ANTI_MAGIC_SHELL).</summary>
    public const int AntiMagicShell = 0x800;
    /// <summary>spell.mes messages: the maintenance cap or payment (601), the checks or a shelled source (602).</summary>
    public const int MessageMaintain = 601, MessageShelled = 602;

    private readonly ISpellRunHost _host;
    private readonly MagicAnomalyProfile _profile;
    private readonly HashSet<int> _ticks;
    private readonly List<SpellRunEvent> _queued = [];
    private readonly List<int> _removedTicks = [];
    private readonly List<SpellRunNote> _notes = [];
    private int _guard = -1;
    private bool _callbackGuard;
    // The END callback flag of an immediate callback (0x5E75D8): reset by an ordinary pass only (0x451070), not by the
    // callback's own pass (0x457060).
    private bool _endCallback;

    /// <param name="table">The run table.</param>
    /// <param name="host">The world.</param>
    /// <param name="profile">The rule mode's magic switches.</param>
    /// <param name="pendingTicks">The runs that have a tick event queued (the duplicate test of 0x4548D0).</param>
    public SpellRunSystem(SpellRunTable table, ISpellRunHost host, MagicAnomalyProfile profile, IEnumerable<int>? pendingTicks = null)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _ticks = [.. pendingTicks ?? []];
    }

    public SpellRunTable Table { get; private set; }

    /// <summary>The events to queue, in order.</summary>
    public IReadOnlyList<SpellRunEvent> Queued => _queued;

    /// <summary>The runs whose tick events to remove (a free, an interrupt, a forced end), in order.</summary>
    public IReadOnlyList<int> RemovedTicks => _removedTicks;

    public IReadOnlyList<SpellRunNote> Notes => _notes;

    // The state of one processing pass (the original's globals reset by 0x451070).
    private sealed class Pass
    {
        public bool Applied;          // 0x5E75DC: a component applied, the list was empty, or the action has no components
        public bool CallbackPending;  // 0x5E75CC: an eye candy's animation will call back
        public bool CallbackNow;      // 0x5E75D4: the callback runs at once
    }

    /// <summary>
    /// Processes a run's current action (0x451070, 0x4510F0): refused while another run is processed (RN8); then the run is
    /// marked processed; at BEGIN the forbidden flags; the stage (MT9a-1; a failed MAINTAIN ends the run at once); a shelled
    /// source; with components, the target list, the fate "spell at maximum" of a PC source, and for each target the
    /// per-target stage, the resistance and each component's gate, triggers, notices and effect; then the scheduler. A refusal
    /// of BEGIN or MAINTAIN queues the run's interrupt.
    /// </summary>
    [OriginalFunction(0x451070, OriginalFunctionScope.Partial)]
    public void Process(int id)
    {
        _endCallback = false;
        Run(id, new Pass());
    }

    [OriginalFunction(0x4510F0, OriginalFunctionScope.Partial)]
    private void Run(int id, Pass pass)
    {
        if (Table[id] is not { } run) return;
        if (_guard != -1 && _guard != id) { _notes.Add(new(SpellRunNoteKind.ReEntryRefused, id)); return; }
        run = Set(run with { Flags = run.Flags | SpellRunFlags.Processed });
        _guard = id;
        var record = _host.Record(run.Spell);
        if (run.Action == SpellRunAction.Begin && !_host.FlagsAllow(run)) { Refuse(id); return; }
        var stage = SpellRunRules.Stage(run, record, _host.StageFacts(run), _host.Cells, _host.Stacking, _profile);
        if (stage.Payments.Count > 0) _notes.Add(new(SpellRunNoteKind.Payments, id, Payments: stage.Payments));
        if (stage.Message is { } stageMessage) _notes.Add(new(SpellRunNoteKind.Message, id, stageMessage));
        if (!stage.Proceeds)
        {
            if (stage.Ends)
            {
                // A failed MAINTAIN: the maintain bar, END and its processing at once (the stage's own call, under this guard).
                _notes.Add(new(SpellRunNoteKind.MaintainBar, id));
                Set(run with { Action = SpellRunAction.End });
                Process(id);
            }
            Refuse(id);
            return;
        }
        if (!SourceUnshelled(run)) { Refuse(id); return; }
        run = Table[id]!;
        var count = record.Components(run.Action);
        if (count <= 0) pass.Applied = true;
        else Targets(id, record, count, pass);
        _guard = -1;
        Schedule(id, pass);
    }

    // A refused BEGIN or MAINTAIN: the guard drops and the run's interrupt is queued (0x4571E0); an END or a callback is left.
    private void Refuse(int id)
    {
        _guard = -1;
        if (Table[id] is { Action: < SpellRunAction.End }) EndByEvent(id);
    }

    // 0x453710: a source under an anti-magic shell refuses BEGIN and MAINTAIN unless the run itself set the shell; the local
    // player is told (602) and its maintain bar refreshed.
    [OriginalFunction(0x453710, OriginalFunctionScope.Full)]
    private bool SourceUnshelled(SpellRun run)
    {
        if (run.Source is null || run.Action >= SpellRunAction.End) return true;
        if ((_host.SourceSpellFlags(run) & AntiMagicShell) == 0 || (run.SetSpellFlags & AntiMagicShell) != 0) return true;
        if (_host.IsLocalPlayer(run.Parent))
        {
            _notes.Add(new(SpellRunNoteKind.Message, run.Id, MessageShelled));
            _notes.Add(new(SpellRunNoteKind.MaintainBar, run.Id));
        }
        return false;
    }

    // The target loop of 0x4510F0.
    private void Targets(int id, SpellRunRecord record, int count, Pass pass)
    {
        var run = Table[id]!;
        var targets = _host.Collect(run);
        if (run.Source is { Type: SpellObjectFacts.PcType } pc && _host.UseSpellAtMaximum(pc))
        {
            run = Set(run with { Source = pc with { Aptitude = 100 }, Parent = run.Parent is { } parent ? parent with { Aptitude = 100 } : null });
            _notes.Add(new(SpellRunNoteKind.SpellAtMaximum, id));
        }
        if (targets.Count == 0) pass.Applied = true;
        // The source when the target list was set up (0x453630, 0x5E75B0): a component that replaces it swaps the run's
        // source and the current target after the target's components.
        var startSource = run.Source;
        foreach (var entry in targets)
        {
            if (Table[id] is not { } current) return;
            var target = entry;
            if (!_host.PerTarget(current, target)) continue;
            if (!_host.TakesEffect(current, target)) { _notes.Add(new(SpellRunNoteKind.Resisted, id, Target: target)); continue; }
            for (var component = 0; component < count; component++)
            {
                if (Table[id] is not { } live) return;
                var gate = _host.Gate(live, component, target);
                if (!gate.Passes) continue;
                target = gate.Target;
                if (!SpellTargetRules.TriggersAllow(record.ItemTriggers, gate.Triggers, live.Triggers)) continue;
                pass.Applied = true;
                _notes.Add(new(SpellRunNoteKind.AiNotice, id, component, target));
                var outcome = _host.Apply(live, component, target) ?? SpellComponentOutcome.None;
                if (Table[id] is { } after) Set(after with { SetSpellFlags = after.SetSpellFlags | outcome.SetSpellFlags });
                if (outcome.ReleasesSource == SpellSourceRelease.Clear) ClearSource(id);
                else if (outcome.ReleasesSource == SpellSourceRelease.Detach) DetachSource(id);
                if (outcome.CallbackPending) pass.CallbackPending = true;
                if (outcome.CallbackNow) { pass.CallbackNow = true; if (outcome.EndCallback) _endCallback = true; }
                if (outcome.JumpTo is { } jump) component = Math.Min(jump, count);
            }
            // A source let go during the pass: the run's source becomes the current target and the current target the new
            // source (none), for every later target too (the snapshot is not refreshed).
            if (Table[id] is { } swapped && !Equals(swapped.Source, startSource))
            {
                var replaced = swapped.Source;
                Set(swapped with { Source = target.Object });
                target = new(replaced, target.Tile);
            }
        }
    }

    // The scheduler (0x453FA0) of a pass: its pure decision (SpellRunScheduler.Decide) over the facts it asks, then its calls.
    private void Schedule(int id, Pass pass)
    {
        if (Table[id] is not { } run) return;
        var record = _host.Record(run.Spell);
        var parent = run.Parent;
        var holder = SpellRunRules.DurationStatHolder(run);
        var facts = new SpellScheduleFacts(pass.CallbackNow, _endCallback, _callbackGuard, pass.Applied, pass.CallbackPending,
            SpellRunRules.WithinCap(run, Table, parent is not null ? _host.Stat(parent, 4) ?? 0 : 0, _host.Record), TickAdded: true,
            BarAdds: !_host.IsLocalPlayer(parent) || record.Technology || _host.MaintainBarHasSlot(run), _host.IsLocalPlayer(parent),
            _host.IsLocalPlayer(run.Target), record.Duration.Stat > -1 && holder is not null ? _host.Stat(holder, record.Duration.Stat) : null,
            _host.TimeFacts(run));
        var outcome = SpellRunScheduler.Decide(run, record, facts, _profile);
        Set(outcome.Run);
        foreach (var call in outcome.Calls)
            switch (call.Effect)
            {
                case SpellScheduleEffect.CallbackAction:
                    _callbackGuard = true;
                    CallbackNow(id, (SpellRunAction)call.Value);
                    _callbackGuard = false;
                    break;
                case SpellScheduleEffect.MaintainBarRefresh: _notes.Add(new(SpellRunNoteKind.MaintainBar, id)); break;
                case SpellScheduleEffect.Free: FreeRun(id); break;
                case SpellScheduleEffect.TargetRefresh: _notes.Add(new(SpellRunNoteKind.TargetRefresh, id)); break;
                case SpellScheduleEffect.Message: _notes.Add(new(SpellRunNoteKind.Message, id, (int)call.Value)); break;
                case SpellScheduleEffect.Process: Process(id); break;
                case SpellScheduleEffect.Tick: AddTick(id, call.Value); break;
                case SpellScheduleEffect.MaintainBarAdd:
                    if (Table[id] is { Action: not SpellRunAction.End }) _notes.Add(new(SpellRunNoteKind.MaintainBarAdd, id));
                    break;
            }
    }

    // 0x4548D0: a run keeps one tick; a second is not queued (and counts as queued).
    [OriginalFunction(0x4548D0, OriginalFunctionScope.Partial)]
    private bool AddTick(int id, long delayMs)
    {
        if (!_ticks.Add(id)) return true;
        _queued.Add(new(id, SpellRunEventCode.Tick, delayMs));
        return true;
    }

    private void FreeRun(int id)
    {
        Table = Table.Free(id);
        RemoveTicks(id);
        _notes.Add(new(SpellRunNoteKind.Freed, id));
    }

    private void RemoveTicks(int id)
    {
        _ticks.Remove(id);
        _removedTicks.Add(id);
    }

    private SpellRun Set(SpellRun run)
    {
        Table = Table.With(run);
        return run;
    }

    // 0x457030, 0x457060: the callback action at once, in the callback's own pass (its scheduler then frees the run).
    [OriginalFunction(0x457030, OriginalFunctionScope.Full)]
    [OriginalFunction(0x457060, OriginalFunctionScope.Partial)]
    private void CallbackNow(int id, SpellRunAction action)
    {
        if (Table[id] is not { } run) return;
        Set(run with { Action = action });
        Run(id, new Pass { CallbackNow = true });
    }

    /// <summary>The release frame of the casting goal (0x456F70): the run's BEGIN is processed.</summary>
    [OriginalFunction(0x456F70, OriginalFunctionScope.Full)]
    public void Begin(int id) => SetAndProcess(id, SpellRunAction.Begin);

    /// <summary>An animation's callback (0x457000): the run's CALLBACK or END_CALLBACK is processed.</summary>
    [OriginalFunction(0x457000, OriginalFunctionScope.Full)]
    public void SetAndProcess(int id, SpellRunAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (Table[id] is not { } run) return;
        Set(run with { Action = action });
        Process(id);
    }

    /// <summary>Frees a run (0x456FA0), after charging its BEGIN cost when asked (a cast whose goal ended before BEGIN).</summary>
    [OriginalFunction(0x456FA0, OriginalFunctionScope.Partial)]
    public void Free(int id, bool chargeBegin)
    {
        if (Table[id] is not { } run) return;
        if (chargeBegin && _host.ChargeBegin(run) is { Count: > 0 } payments) _notes.Add(new(SpellRunNoteKind.Payments, id, Payments: payments));
        FreeRun(id);
    }

    /// <summary>
    /// Interrupts a run (0x457110, the maintain bar's stop, the script's stop, an interrupt event): MAINTAIN turns to END when the
    /// record has END callback components, otherwise to CALLBACK when it has callback components, else END; another action
    /// turns to END_CALLBACK, CALLBACK or END the same way; its ticks are removed and it is processed.
    /// </summary>
    public void Interrupt(int id)
    {
        if (Table[id] is not { } run) return;
        Set(run with { Action = InterruptAction(run.Action, _host.Record(run.Spell)) });
        RemoveTicks(id);
        Process(id);
    }

    /// <summary>The action an interrupt turns a run to (0x457110): from MAINTAIN, END when the record has END callback components,
    /// otherwise CALLBACK when it has callback components, else END; from another action END_CALLBACK, CALLBACK or END the same
    /// way.</summary>
    [OriginalFunction(0x457110, OriginalFunctionScope.Partial)]
    public static SpellRunAction InterruptAction(SpellRunAction action, SpellRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Components(SpellRunAction.EndCallback) > 0) return action == SpellRunAction.Maintain ? SpellRunAction.End : SpellRunAction.EndCallback;
        return record.Components(SpellRunAction.Callback) > 0 ? SpellRunAction.Callback : SpellRunAction.End;
    }

    /// <summary>Ends a run by event (0x4571E0): the local player's maintain bar; a BEGIN never processed is charged and freed,
    /// otherwise its interrupt is queued 1 ms later.</summary>
    [OriginalFunction(0x4571E0, OriginalFunctionScope.Full)]
    public void EndByEvent(int id)
    {
        _notes.Add(new(SpellRunNoteKind.MaintainBar, id));
        if (Table[id] is { Action: SpellRunAction.Begin } run && (run.Flags & SpellRunFlags.Processed) == 0) Free(id, chargeBegin: true);
        else _queued.Add(new(id, SpellRunEventCode.Interrupt, InterruptDelayMs));
    }

    /// <summary>Forces a run to end (0x457270): END, its ticks removed, the forced-end flag, processed.</summary>
    [OriginalFunction(0x457270, OriginalFunctionScope.Full)]
    public void ForceEnd(int id)
    {
        if (Table[id] is not { } run) return;
        Set(run with { Action = SpellRunAction.End, Flags = run.Flags | SpellRunFlags.ForcedEnd });
        RemoveTicks(id);
        Process(id);
    }

    /// <summary>Detaches a run's source (0x4572F0): the local player's maintain bar; a parent that is the source goes too; unless
    /// the run is being processed it gets the final END mark (the original's bit 0x10, also unresistable: MG19).</summary>
    [OriginalFunction(0x4572F0, OriginalFunctionScope.Full)]
    public void DetachSource(int id)
    {
        if (Table[id] is not { } run) return;
        if (_host.IsLocalPlayer(run.Parent)) _notes.Add(new(SpellRunNoteKind.MaintainBar, id));
        var parent = run.Parent is not null && run.Source is not null && run.Parent.Id == run.Source.Id ? null : run.Parent;
        var flags = _guard == id ? run.Flags : run.Flags | SpellRunRules.FinalEnd(_profile);
        Set(run with { Source = null, Parent = parent, Flags = flags });
    }

    /// <summary>Clears a run's source (0x457530); nothing else.</summary>
    [OriginalFunction(0x457530, OriginalFunctionScope.Full)]
    public void ClearSource(int id)
    {
        if (Table[id] is { } run) Set(run with { Source = null });
    }

    /// <summary>Clears the source of every run whose parent or source is <paramref name="obj"/> (0x4574D0: the object leaves).</summary>
    [OriginalFunction(0x4574D0, OriginalFunctionScope.Full)]
    public void ClearSources(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        foreach (var run in Table.Runs.ToArray())
            if (run.Parent?.Id == obj || run.Source?.Id == obj) ClearSource(run.Id);
    }

    /// <summary>Ends by event every magic run whose source is <paramref name="obj"/> and that has no item triggers or has a
    /// maintenance period (0x457450: the caster fainted).</summary>
    [OriginalFunction(0x457450, OriginalFunctionScope.Full)]
    public void Demaintain(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        foreach (var run in Table.Runs.ToArray())
        {
            if (run.Source?.Id != obj) continue;
            var record = _host.Record(run.Spell);
            if (!record.Technology && (!record.ItemTriggers || record.MaintainPeriod > 0)) EndByEvent(run.Id);
        }
    }

    /// <summary>Interrupts the first run (in slot order) of <paramref name="spell"/> with that parent and target object
    /// (0x4573D0, the script's stop of a cast); nothing for an invocation without a source.</summary>
    [OriginalFunction(0x4573D0, OriginalFunctionScope.Full)]
    public void Stop(SpellInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.Source is null) return;
        foreach (var run in Table.Runs)
            if (run.Parent?.Id == invocation.Parent?.Id && run.Target?.Id == invocation.TargetObject?.Id && run.Spell == invocation.Spell)
            {
                Interrupt(run.Id);
                return;
            }
    }

    /// <summary>A magic-tech event fires (0x459430): a tick processes the run (a BEGIN turning to MAINTAIN, 0x459490); an
    /// interrupt interrupts it; a process event processes its current action.</summary>
    [OriginalFunction(0x459430, OriginalFunctionScope.Full)]
    public void Fire(int id, SpellRunEventCode code)
    {
        switch (code)
        {
            case SpellRunEventCode.Tick:
                _ticks.Remove(id);
                if (Table[id] is not { } run) return;
                if (run.Action == SpellRunAction.Begin) Set(run with { Action = SpellRunAction.Maintain });
                Process(id);
                break;
            case SpellRunEventCode.Interrupt: Interrupt(id); break;
            case SpellRunEventCode.Process: Process(id); break;
            // Another code does nothing (the original's handler returns at once).
        }
    }
}

/// <summary>A call of the scheduler, in order: the callback action at once (its action), the local player's maintain bar
/// refreshed, the run freed, the local target's interface refreshed, a message to the local player (its number), the run
/// processed again (its new action), a tick queued (its delay in game ms), the local player's maintain bar asked for a slot.</summary>
public enum SpellScheduleEffect { CallbackAction, MaintainBarRefresh, Free, TargetRefresh, Message, Process, Tick, MaintainBarAdd }

/// <summary>One call of the scheduler and its value (an action, a message, a delay).</summary>
public readonly record struct SpellScheduleCall(SpellScheduleEffect Effect, long Value = 0);

/// <summary>
/// What the scheduler reads besides the run and its record: the pass's callback state (now, END callback, the callback guard,
/// pending), whether a component applied, whether the run is within the maintenance cap (0x4545E0), whether a tick could be
/// queued (0x4548D0), whether the local player's maintain bar takes the run (0x4604A0, asked for a magic spell of the local
/// player), whether the parent and the target are the local player, the DurationStatInfo stat of its holder, and the time
/// facts.
/// </summary>
public sealed record SpellScheduleFacts(bool CallbackNow, bool EndCallback, bool CallbackGuard, bool Applied, bool CallbackPending, bool WithinCap,
    bool TickAdded, bool BarAdds, bool ParentLocal, bool TargetLocal, int? StatValue, SpellTimeFacts Time);

/// <summary>The scheduler's decision: the run after it and its calls in order.</summary>
public sealed record SpellScheduleOutcome(SpellRun Run, IReadOnlyList<SpellScheduleCall> Calls);

/// <summary>
/// ARC-MAGIC-010: the original's scheduler (0x453FA0, with the tick helpers 0x454790 and 0x4547F0) as a pure decision; the run
/// system carries out its calls.
/// </summary>
public static class SpellRunScheduler
{
    /// <summary>
    /// The decision after a processing pass: an immediate callback runs the callback action (once: under the callback guard the
    /// run is freed); BEGIN, MAINTAIN and CALLBACK of a run with the final END mark end; a maintained spell ends when nothing was
    /// applied, else with a source checks the cap (601 and END at once over it) and ticks after its period (the local player's
    /// maintain bar takes a magic spell, or the run turns to END and goes to the END branch), without a source counts its ticks
    /// (the record's count, 8 when none) and ends after them; a timed spell ends when nothing was applied or it has no duration,
    /// else counts its trigger and ticks after its duration; the tail ends a run that has no callback components (or a CALLBACK
    /// without END components), turns END_CALLBACK into END and waits for a pending BEGIN callback; the END branch frees the run
    /// unless an END callback is pending.
    /// </summary>
    [OriginalFunction(0x453FA0, OriginalFunctionScope.Partial)]
    public static SpellScheduleOutcome Decide(SpellRun run, SpellRunRecord record, SpellScheduleFacts facts, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(profile);
        var calls = new List<SpellScheduleCall>();
        var pending = facts.CallbackPending;
        void Release()
        {
            if (facts.ParentLocal) calls.Add(new(SpellScheduleEffect.MaintainBarRefresh));
            calls.Add(new(SpellScheduleEffect.Free));
            if (facts.TargetLocal) calls.Add(new(SpellScheduleEffect.TargetRefresh));
        }
        SpellScheduleOutcome Done() => new(run, calls);
        SpellScheduleOutcome EndBranch()
        {
            if (pending && record.Components(SpellRunAction.EndCallback) == 0) pending = false;
            if (!pending && run.Action == SpellRunAction.End) Release();
            return Done();
        }
        // A maintenance tick (0x454790, 0x4547F0): MAINTAIN, the tick, and for the local player's magic spell a bar slot or END.
        bool MaintainTick()
        {
            run = run with { Action = SpellRunAction.Maintain };
            calls.Add(new(SpellScheduleEffect.Tick, SpellRunRules.MaintainTime(record.MaintainPeriod, run.Spell, facts.Time)));
            if (!facts.TickAdded) return false;
            if (facts.ParentLocal && !record.Technology)
            {
                calls.Add(new(SpellScheduleEffect.MaintainBarAdd));
                if (!facts.BarAdds) run = run with { Action = SpellRunAction.End };
            }
            return true;
        }
        if (facts.CallbackNow)
        {
            if (!facts.CallbackGuard)
            {
                calls.Add(new(SpellScheduleEffect.CallbackAction, (long)(facts.EndCallback ? SpellRunAction.EndCallback : SpellRunAction.Callback)));
                return Done();
            }
            Release();
            return Done();
        }
        if (run.Action == SpellRunAction.End) return EndBranch();
        var end = false;
        var checkEnd = true;
        if (run.Action != SpellRunAction.EndCallback)
        {
            if ((run.Flags & SpellRunRules.FinalEnd(profile)) != 0) end = true;
            else if (record.MaintainPeriod > 0)
            {
                if (!facts.Applied) end = true;
                else if (run.Source is not null)
                {
                    if (!facts.WithinCap)
                    {
                        if (facts.ParentLocal) calls.Add(new(SpellScheduleEffect.Message, SpellRunSystem.MessageMaintain));
                        run = run with { Action = SpellRunAction.End };
                        calls.Add(new(SpellScheduleEffect.Process, (long)SpellRunAction.End));
                        return Done();
                    }
                    if (MaintainTick()) return run.Action == SpellRunAction.End ? EndBranch() : Done();
                }
                else if (run.TriggerCount == 0)
                {
                    checkEnd = false;
                    run = run with { TriggerCount = record.TriggerCount != 0 ? record.TriggerCount : SpellRunSystem.SourcelessTicks };
                    if (MaintainTick()) return Done();
                }
                else
                {
                    run = run with { TriggerCount = run.TriggerCount - 1 };
                    if (run.TriggerCount <= 0) end = true;
                    else if (MaintainTick()) return run.Action == SpellRunAction.End ? EndBranch() : Done();
                }
            }
            else if (!facts.Applied || record.Duration.None) end = true;
            else
            {
                run = SpellRunRules.CountTrigger(run, profile);
                calls.Add(new(SpellScheduleEffect.Tick, SpellRunRules.DurationTime(record.Duration, run.Spell, facts.StatValue, facts.Time)));
                if (facts.TickAdded) return Done();
            }
        }
        // The tail.
        if (record.Components(SpellRunAction.Callback) == 0 && record.Components(SpellRunAction.EndCallback) == 0) end = true;
        if (run.Action == SpellRunAction.Callback && record.Components(SpellRunAction.End) == 0) end = true;
        if (run.Action == SpellRunAction.EndCallback) { run = run with { Action = SpellRunAction.End }; end = false; }
        if (run.Action == SpellRunAction.Begin && record.Components(SpellRunAction.Callback) != 0 && pending) return Done();
        if (end)
        {
            run = run with { Action = SpellRunAction.End };
            calls.Add(new(SpellScheduleEffect.Process, (long)SpellRunAction.End));
            return Done();
        }
        return checkEnd && run.Action == SpellRunAction.End ? EndBranch() : Done();
    }
}
