using System.Collections.Immutable;
using Arcanum.Simulation;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;

namespace Arcanum.Session;

/// <summary>Why the session refused a goal-table call (ARC-DEV-132); a refused call changes nothing.</summary>
public enum SessionGoalFailure
{
    None,
    /// <summary>An owner that is no session entity, a pace or level out of range, a run that is not the stepped one's.</summary>
    InvalidRequest,
    /// <summary>A step was opened or closed inside an event pass, or a call came while the spell-run rules hold the queue. ARC-DEV-134 (MIG13 S4): a start, an interrupt,
    /// a kill or a stack change may come from a handler (the AI tick's perform starts goals inside its pass, as the original's heartbeat does); its step waits for the next pass.</summary>
    PassInProgress,
    /// <summary>No step is open for the handle: none is open, or the open one is another run's (<see cref="GameSessionOwner.EndGoalStep"/>).</summary>
    NotStepping,
    /// <summary>The handle names no run in the table (a freed slot, a reused slot's old id).</summary>
    NoRun,
    /// <summary>The run's first step could not be queued: the run was not started.</summary>
    EventBudget,
}

/// <summary>
/// A start through the session (<see cref="GameSessionOwner.StartGoal"/>): the session's refusal, or the start rules' outcome with the new run's handle and the runs the
/// start's interrupts ended (their events are already removed; their end nodes are the caller's: the AI host runs them, ARC-DEV-135). <see cref="Outcome"/> is null for a refusal of the session.
/// </summary>
public sealed record SessionGoalStartResult(SessionGoalFailure Failure, GoalStartOutcome? Outcome, GoalHandle? Handle, ImmutableArray<GoalRunEnd> Ended)
{
    public bool Started => Outcome == GoalStartOutcome.Started;
}

/// <summary>
/// An interrupt or a kill through the session: the session's refusal, whether the table's call succeeded, and each run's step in order (the ended runs' events are already
/// removed; the run being stepped is only marked done and ends with its step).
/// </summary>
public sealed record SessionGoalChange(SessionGoalFailure Failure, bool Succeeded, ImmutableArray<GoalInterruptStep> Steps)
{
    /// <summary>The runs that ended, in the order they ended.</summary>
    public IEnumerable<GoalRunEnd> Ended => Steps.Where(s => s.Ended is not null).Select(s => s.Ended!);
}

/// <summary>A goal step that came due in a pass: its queue record, the run's handle and owner, and the animation time of its list when it fired. The pass only
/// reports it; the frame step hands it to the host after the pass, as it does the real-time steps.</summary>
public sealed record SessionGoalStepFiring(GameEvent Event, GoalHandle Handle, string Owner, long NowMs);

/// <summary>
/// The host's answer to one step of a run (<see cref="GameSessionOwner.EndGoalStep"/>): the run as the step left it (null keeps the table's run; same handle and owner), and
/// the real milliseconds after which it steps again (the original's frame delay, 1..<see cref="GameSessionOwner.MaxGoalStepDelayMs"/>, converted to animation time ×8,
/// TL40), or null when the run ends.
/// </summary>
public readonly record struct SessionGoalStepAnswer(long? NextDelayMs, GoalRun? Run = null, bool Abandoned = false)
{
    /// <summary>The run ends with this step (the stepper ran the end nodes of what it popped).</summary>
    public static SessionGoalStepAnswer End { get; } = new(null);

    /// <summary>The step could not be completed (the host threw, a firing was never handed to it): the run ends with the end nodes of every goal still on
    /// its stack reported, as for a refused repeat.</summary>
    public static SessionGoalStepAnswer Abandon { get; } = new(null, null, true);

    /// <summary>The run steps again <paramref name="delayMs"/> real milliseconds from now.</summary>
    public static SessionGoalStepAnswer After(long delayMs, GoalRun? run = null) => new(delayMs, run);
}

/// <summary>What closing a step did.</summary>
public enum SessionGoalStepOutcome
{
    /// <summary>The run goes on: its next step is queued (<see cref="SessionGoalStepResult.NextDueMs"/>).</summary>
    Continued,
    /// <summary>The run ended: the host answered the end, or the run was interrupted or killed while it stepped.</summary>
    Ended,
    /// <summary>The run ended because its next step could not be queued (a delay out of range, a run that is not the stepped one's, a full queue) or the
    /// step was abandoned; the end nodes of the goals still on its stack are reported.</summary>
    Refused,
}

/// <summary>A closed step (<see cref="GameSessionOwner.EndGoalStep"/>): the session's refusal (nothing changed), or the outcome with the ended run or the next due time.</summary>
public sealed record SessionGoalStepResult(SessionGoalFailure Failure, SessionGoalStepOutcome Outcome, GoalRunEnd? Ended, long? NextDueMs);

/// <summary>
/// ARC-DEV-132 (MIG13 S3, docs/architecture/goal-table.md §3.1, §3.2): the session owns the one goal table (<see cref="GoalRunTable"/>, the 216 slots of every animated
/// object's runs) and the <c>goal.step</c> kind on the animation domain. No caller outside the session writes a run: a start, an interrupt, a kill and a step go through
/// these calls, and every end of a run removes its pending step by slot <b>and</b> run id (the original compares only the slot, GS7: a deliberate, safer difference with no
/// visible effect). A run belongs to the active map (the actor's): a start is for an owner on it, a map change ends every run at level 6 and removes its step (GS19), an
/// NPC that departs to another map loses its runs (killed, as the original kills an object's runs when it leaves with its map, 0x423FF0), and before every pass a run
/// whose step went missing or was carried off the active map is killed, so no goal step is ever parked and no run is left without one. Neither the table nor the kind is saved (MIG14): a captured session holds no run and a restored one starts with none.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The first step's delay after a start: 5 ms of animation time (0x44D730 queues the pair {0, 5} with no conversion, GS7).</summary>
    public const long FirstGoalStepDelayMs = 5;
    /// <summary>ARC-DEV-136 (MIG13 S5b, TL41, GT17): the first step's delay for an owner that is not the current participant of a round: a 0 ms delay stored as 1 ms, straight in the live
    /// list (0x44D730 → 0x45B820) instead of 5 ms. The hit reactions, the dying, the knockback and the goals scripts or the combat code push for a critter off its turn get their first frame this way.</summary>
    public const long RoundFirstGoalStepDelayMs = 1;
    /// <summary>Animation-clock milliseconds per real millisecond of a frame delay: 0x423C80 shifts the delay left by three (TL40).</summary>
    public const long GoalStepClockRate = 8;
    /// <summary>The longest frame delay a step answers, in real milliseconds: one real minute (project policy; the original's paces are 1000 / the art's frame rate).</summary>
    public const long MaxGoalStepDelayMs = 60_000;

    private GoalRunTable _goals = GoalRunTable.Empty;
    private GoalHandle? _goalStepOpen;
    // The runs a pass reported whose step the frame step has not opened yet (a nested pass, run by a host between, must not take them for lost).
    private readonly HashSet<GoalHandle> _goalStepsReported = [];
    private List<SessionGoalStepFiring>? _passGoalSteps;
    // ARC-DEV-134: the runs the session ended by itself (a map's close, a departure, a lost step, a refused step close), whose end nodes no caller was handed.
    private readonly List<GoalRunEnd> _goalEndsUnclaimed = [];
    // ARC-DEV-135: the stepped run the session itself marked done (a map's close, a departure) while its step was open: its close ends it, and that end is unclaimed too (a caller's
    // own mark is the caller's to settle: it runs the end nodes at once).
    private readonly HashSet<GoalHandle> _goalMarkedBySession = [];
    /// <summary>The most unclaimed ends kept (<see cref="DrainGoalEnds"/>); older ones are dropped first.</summary>
    public const int MaxUnclaimedGoalEnds = GoalRunTable.SlotCount * 2;

    /// <summary>
    /// ARC-DEV-134 (MIG13 S4): the runs the session ended by itself since the last call, in order, and forgets them: a map's close at level 6 (GS19), an NPC's departure,
    /// a run whose step went missing (the pass's reconcile) and a step close the session refused. No caller was handed their end nodes, so the host whose runs they were
    /// runs its own end nodes (ARC-DEV-135: every end's, the casts' among them). At most <see cref="MaxUnclaimedGoalEnds"/> are kept.
    /// </summary>
    public ImmutableArray<GoalRunEnd> DrainGoalEnds()
    {
        ThrowIfDisposed();
        var drained = _goalEndsUnclaimed.ToImmutableArray();
        _goalEndsUnclaimed.Clear();
        return drained;
    }

    // The stepped run a session-made sweep or kill only marked done: its close will report its end (see _goalMarkedBySession).
    private void MarkedBySession(IEnumerable<GoalInterruptStep> steps)
    {
        foreach (var step in steps)
            if (step.Outcome == GoalInterruptOutcome.MarkedDone) _goalMarkedBySession.Add(step.Handle);
    }

    private void Unclaimed(IEnumerable<GoalRunEnd> ended)
    {
        _goalEndsUnclaimed.AddRange(ended);
        if (_goalEndsUnclaimed.Count > MaxUnclaimedGoalEnds) _goalEndsUnclaimed.RemoveRange(0, _goalEndsUnclaimed.Count - MaxUnclaimedGoalEnds);
    }

    /// <summary>The goal table now: an immutable value, so every query of the rules (<see cref="GoalRunTable.MainRunOf"/>, <see cref="GoalRunTable.RunRunningGoal"/>,
    /// <see cref="GoalRunTable.EqualGoalQueued"/>, the significant-goal count, an owner's runs in slot order) reads it without a copy.</summary>
    public GoalRunTable QueryGoals() { ThrowIfDisposed(); return _goals; }

    /// <summary>The animation time at which the run's next step is due, or null when none is pending (no such run, or the run is being stepped).</summary>
    public long? QueryGoalStep(GoalHandle handle)
    {
        ThrowIfDisposed();
        var pending = _events.QueryPending(SessionEventKinds.GoalStep).FirstOrDefault(e => SessionEventKinds.NamesRun(e, handle));
        return pending?.DeadlineMs;
    }

    /// <summary>
    /// Starts a run by the start rules (<see cref="GoalStartRules.Start"/>: gate, duplicate, interrupts, loading, destroyed, a free slot) for an owner on the active map,
    /// and queues its first step <see cref="FirstGoalStepDelayMs"/> after the animation clock (GS7). The runs the start's interrupts ended lose their steps here. Refused by
    /// the session (nothing changed): an owner that is no session entity, a pace out of range, a call while the spell-run rules hold the queue (a call inside a pass is
    /// admitted since ARC-DEV-134: its first step is the next pass's); a start whose first step the queue does not take is
    /// taken back (its slot freed) and refused with <see cref="SessionGoalFailure.EventBudget"/>, after its interrupts, which stay.
    /// </summary>
    public SessionGoalStartResult StartGoal(GoalStartRequest request, IGoalStartPorts ports)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ports);
        if (!SessionValidation.Entity(request.Owner) || request.PaceMs is < 1 or > (int)MaxGoalStepDelayMs || request.Plan is null || request.Frame is null)
            return new(SessionGoalFailure.InvalidRequest, null, null, []);
        if (TableBusy()) return new(SessionGoalFailure.PassInProgress, null, null, []);
        var started = GoalStartRules.Start(_goals, request, ports);
        _goals = started.Table;
        SettleEnded(started.Ended);
        FreedRuns(started.Ended);
        if (started.Outcome != GoalStartOutcome.Started || started.Handle is not { } handle) return new(SessionGoalFailure.None, started.Outcome, null, started.Ended);
        if (ScheduleGoalStep(handle, request.Owner, FirstStepDelay(request.Owner)) is null)
        {
            _goals = _goals.Free(handle).Table;
            return new(SessionGoalFailure.EventBudget, started.Outcome, null, started.Ended);
        }
        return new(SessionGoalFailure.None, started.Outcome, handle, started.Ended);
    }

    /// <summary>Interrupts one run at <paramref name="level"/> (0..6) by the rule (<see cref="GoalRunTable.Interrupt"/>); an ended run loses its step.</summary>
    public SessionGoalChange InterruptGoal(GoalHandle handle, int level)
    {
        ThrowIfDisposed();
        if (level is < 0 or > GoalInterruptRules.MaxLevel) return new(SessionGoalFailure.InvalidRequest, false, []);
        if (TableBusy()) return new(SessionGoalFailure.PassInProgress, false, []);
        var result = _goals.Interrupt(handle, level);
        return Apply(result.Table, result.Step.Succeeded, [result.Step]);
    }

    /// <summary>Interrupts every run of <paramref name="owner"/> in slot order (<see cref="GoalRunTable.InterruptOwner"/>: the first refusal stops the walk, GS17).</summary>
    public SessionGoalChange InterruptGoals(string owner, int level, bool quiet = false)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(owner) || level is < 0 or > GoalInterruptRules.MaxLevel) return new(SessionGoalFailure.InvalidRequest, false, []);
        if (TableBusy()) return new(SessionGoalFailure.PassInProgress, false, []);
        var result = _goals.InterruptOwner(owner, level, quiet);
        return Apply(result.Table, result.Succeeded, result.Steps);
    }

    /// <summary>
    /// ARC-DEV-135 (MIG13 S5): the sweep at a round's start (<see cref="GoalRunTable.InterruptForRoundStart"/>, 0x424310, GS19): every run whose current goal is not "not
    /// counted" is interrupted at level 3 by the rule, a refusal is recorded and the sweep goes on. The original's round start then interrupts each participant's runs again
    /// at level 3 (<see cref="InterruptGoals"/>, which also reaches the "not counted" runs the sweep skipped). The call always succeeds, as the original's returns 1; the ended
    /// runs lose their steps here and their end nodes are the caller's (the AI host's).
    /// </summary>
    public SessionGoalChange SweepGoalsForRoundStart()
    {
        ThrowIfDisposed();
        if (TableBusy()) return new(SessionGoalFailure.PassInProgress, false, []);
        var swept = _goals.InterruptForRoundStart();
        return Apply(swept.Table, true, swept.Steps);
    }

    /// <summary>Kills one run (<see cref="GoalRunTable.KillRun"/>, GS21): no rule, no level.</summary>
    public SessionGoalChange KillGoal(GoalHandle handle)
    {
        ThrowIfDisposed();
        if (TableBusy()) return new(SessionGoalFailure.PassInProgress, false, []);
        var result = _goals.KillRun(handle);
        return Apply(result.Table, result.Step.Succeeded, [result.Step]);
    }

    /// <summary>Kills every run of <paramref name="owner"/> (<see cref="GoalRunTable.Kill"/>: an object destroyed or removed with its map).</summary>
    public SessionGoalChange KillGoals(string owner)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(owner)) return new(SessionGoalFailure.InvalidRequest, false, []);
        if (TableBusy()) return new(SessionGoalFailure.PassInProgress, false, []);
        var result = _goals.Kill(owner);
        return Apply(result.Table, result.Steps.All(s => s.Succeeded), result.Steps);
    }

    /// <summary>
    /// ARC-DEV-134 (MIG13 S4): replaces the goal stack of a run that is not being stepped: the AI host's queued cast (the new casting goal inserted beneath the stack, the
    /// original's 0x44D9B0, GS14, after the goals of a magic run whose slot was lent are taken out, QC6). The run's pending step is kept. Refused (nothing changed): no such
    /// run (<see cref="SessionGoalFailure.NoRun"/>), the run being stepped (its step's answer would overwrite the stack: <see cref="SessionGoalFailure.InvalidRequest"/>),
    /// a stack a run cannot hold (none or more than eight goals, a goal with no descriptor), the spell-run rules holding the queue.
    /// </summary>
    public SessionGoalFailure ReplaceGoalFrames(GoalHandle handle, ImmutableArray<GoalFrame> frames)
    {
        ThrowIfDisposed();
        if (TableBusy()) return SessionGoalFailure.PassInProgress;
        if (!_goals.TryGet(handle, out var run)) return SessionGoalFailure.NoRun;
        if (_goals.SteppingSlot == handle.Slot || run.Done) return SessionGoalFailure.InvalidRequest;
        if (frames.IsDefaultOrEmpty || frames.Length > GoalRun.MaxFrames || frames.Any(f => f is null || !GoalDescriptors.Has(f.Goal))) return SessionGoalFailure.InvalidRequest;
        if (!_goals.TryReplace(run.WithFrames(frames), out var table)) return SessionGoalFailure.NoRun;
        _goals = table;
        return SessionGoalFailure.None;
    }

    /// <summary>
    /// Opens the step of a run whose <c>goal.step</c> came due (the frame step's call after the pass, <see cref="SessionGoalStepFiring"/>): the table marks the run as the one
    /// being stepped, so an interrupt or a kill during the step only marks it done and the step's close ends it (GS23). One step is open at a time; refused (null) inside a
    /// pass, with a step open, or for a handle that names no run. A pending step of the run (none after a firing) is removed.
    /// </summary>
    public GoalRun? BeginGoalStep(GoalHandle handle)
    {
        ThrowIfDisposed();
        if (GoalsBusy() || _goalStepOpen is not null) return null;
        SweepExhaustedGoals();
        if (!_goals.TryGet(handle, out var run)) return null;
        RemoveGoalStep(handle);
        _goalStepsReported.Remove(handle);
        _goals = _goals.WithStepping(handle.Slot);
        _goalStepOpen = handle;
        return run;
    }

    /// <summary>
    /// Closes the open step of <paramref name="handle"/> with the host's answer. A run marked done during the step (an interrupt, a kill, the map's close) or answered with the
    /// end is freed (<see cref="GoalRunEnd"/>: the marked run's end nodes top down; none for the host's end, whose stepper ran them); otherwise the answered run replaces the table's
    /// and its next step is queued the answered delay ×8 after the animation clock <b>now</b> (relative to the clock at firing, the original's default without
    /// <c>-animcatchup</c>: a jump fires a pending step once and never replays the missed ones, TL22, TL40). A delay out of range, an answered run with another handle or
    /// owner, a full queue or an abandoned step end the run (<see cref="SessionGoalStepOutcome.Refused"/>, with the end nodes of the goals still on its stack), so no
    /// run is left without a step. A run marked by a game reset would be reported as interrupted (no reset path reaches a stepped run yet).
    /// </summary>
    [OriginalFunction(0x423C80, OriginalFunctionScope.Partial)]
    public SessionGoalStepResult EndGoalStep(GoalHandle handle, SessionGoalStepAnswer answer)
    {
        ThrowIfDisposed();
        if (_goalStepOpen != handle) return new(SessionGoalFailure.NotStepping, SessionGoalStepOutcome.Refused, null, null);
        if (GoalsBusy()) return new(SessionGoalFailure.PassInProgress, SessionGoalStepOutcome.Refused, null, null);
        _goalStepOpen = null;
        _goals = _goals.WithStepping(null);
        if (!_goals.TryGet(handle, out var run)) return new(SessionGoalFailure.NoRun, SessionGoalStepOutcome.Ended, null, null);
        if (run.Done)
        {
            var marked = Finish(run, run.Flags.HasFlag(GoalRunFlags.Killed) ? GoalEndKind.Killed : GoalEndKind.Interrupted, endNodes: true);
            if (_goalMarkedBySession.Remove(handle)) Unclaimed([marked]);
            return new(SessionGoalFailure.None, SessionGoalStepOutcome.Ended, marked, null);
        }
        if (answer.Abandoned) return Refused(run);
        var stepped = answer.Run ?? run;
        if (stepped.Handle != handle || !string.Equals(stepped.Owner, run.Owner, StringComparison.Ordinal)) return Refused(run);
        if (answer.NextDelayMs is not { } delay || stepped.Done)
            return new(SessionGoalFailure.None, SessionGoalStepOutcome.Ended, Finish(stepped, GoalEndKind.Finished, endNodes: false), null);
        if (delay is < 1 or > MaxGoalStepDelayMs) return Refused(stepped);
        if (_goals.TryReplace(stepped, out var replaced)) _goals = replaced;
        if (ScheduleGoalStep(handle, run.Owner, delay * GoalStepClockRate) is not { } due) return Refused(stepped);
        return new(SessionGoalFailure.None, SessionGoalStepOutcome.Continued, null, due);
    }

    // ARC-DEV-135 (MIG13 S5): the head of the stepper (0x423566, GS2, GT12): when a start found the table full, the next step of any run interrupts every run at level 3 and clears
    // the flag, then goes on with its own run. A run the sweep ended loses its step and its end nodes go to the unclaimed ends (the host that owns the runs claims them at its next
    // frame or step, `DrainGoalEnds`). Differs from the original in one case (goal-table.md §4): when the sweep ends the very run that is about to step (its current goal has priority
    // 3 or less) the original goes on stepping a freed slot, which was not shown to be reachable; here the step finds its run gone and is skipped (the frame step cannot open it).
    private void SweepExhaustedGoals()
    {
        if (!_goals.ExhaustionPending) return;
        var swept = _goals.SweepIfExhausted();
        _goals = swept.Table;
        SettleEnded(swept.Ended);
        Unclaimed(swept.Ended);
        FreedRuns(swept.Ended);
    }

    private SessionGoalStepResult Refused(GoalRun run)
    {
        var ended = Finish(run, GoalEndKind.Finished, endNodes: true);
        Unclaimed([ended]);
        return new(SessionGoalFailure.None, SessionGoalStepOutcome.Refused, ended, null);
    }

    // A pass or the spell-run rules hold the queue: the steps wait (the frame step opens them after the pass).
    private bool GoalsBusy() => _passWorld is not null || _spellRunsBusy;

    // ARC-DEV-134: the spell-run rules hold the queue: the table waits. A pass does not hold it (a handler starts, interrupts and kills runs; the events it queues or
    // removes are the next pass's, and a reported step whose run ended is skipped when the frame step cannot open it).
    private bool TableBusy() => _spellRunsBusy;

    // ARC-DEV-136 (TL41, GT17): a run started in a round for an owner that is not the current participant (the hero's turn, another critter's, a critter outside the round) gets its first
    // frame at 1 ms instead of 5 ms.
    private long FirstStepDelay(string owner) =>
        _combat.Round is { } round && !string.Equals(round.Actor.Entity, owner, StringComparison.Ordinal) ? RoundFirstGoalStepDelayMs : FirstGoalStepDelayMs;

    // Queues the run's step `delayMs` of animation time from now (keyed by owner, slot and id) and records the due time on the run; null when the queue refuses.
    private long? ScheduleGoalStep(GoalHandle handle, string owner, long delayMs)
    {
        var clocks = EventClocks(_clock.QueryClockSnapshot().GameTimestampMs);
        var scheduled = _events.ScheduleAfter(SessionEventKinds.GoalStep,
            GameEventPayload.Of([new(_actor.Map.RuntimeMap, owner)], handle.Slot, handle.RunId, clocks.AnimationMs), delayMs, clocks);
        if (scheduled.Disposition is not (GameEventScheduleDisposition.Added or GameEventScheduleDisposition.Replaced) || scheduled.Event is not { } added) return null;
        if (_goals.TryGet(handle, out var run) && _goals.TryReplace(run with { DueMs = added.DeadlineMs }, out var table)) _goals = table;
        return added.DeadlineMs;
    }

    private void RemoveGoalStep(GoalHandle handle)
    {
        _events.RemoveAll(SessionEventKinds.GoalStep, e => SessionEventKinds.NamesRun(e, handle));
        _goalStepsReported.Remove(handle);
    }

    private void SettleEnded(IEnumerable<GoalRunEnd> ended)
    {
        foreach (var end in ended) RemoveGoalStep(end.Run.Handle);
    }

    private SessionGoalChange Apply(GoalRunTable table, bool succeeded, ImmutableArray<GoalInterruptStep> steps)
    {
        _goals = table;
        var ended = steps.Where(s => s.Ended is not null).Select(s => s.Ended!).ToArray();
        SettleEnded(ended);
        FreedRuns(ended);
        return new(SessionGoalFailure.None, succeeded, steps);
    }

    // Frees a run the step ended; the end nodes of the goals on its stack run top down (GS23) unless the stepper already ran them (the host's end).
    private GoalRunEnd Finish(GoalRun run, GoalEndKind kind, bool endNodes)
    {
        RemoveGoalStep(run.Handle);
        var freed = _goals.Free(run.Handle);
        _goals = freed.Table;
        var ended = run with { Done = true };
        var end = new GoalRunEnd(ended, kind, endNodes ? ended.Frames.Reverse().Select(f => f.Goal).ToImmutableArray() : [], freed.SignificantGoalsLeft);
        FreedRuns([end]);
        return end;
    }

    // GS19: the map closes (the actor moved to another map): every run ends at level 6 and loses its step, wherever a relocation carried it. The run being stepped is
    // marked and its step's close ends it. The end nodes of the ended runs are unclaimed ends (`DrainGoalEnds`): the owners' host runs them.
    private void CloseGoalMap()
    {
        if (_goals.InUseCount == 0) return;
        var swept = _goals.ClearAll();
        // ARC-DEV-135: the table-full flag is not touched: the original writes it only in the allocator and clears it only at the stepper's head (GT22), so a map change keeps it
        // and the next step of any run, on the next map, sweeps once.
        _goals = swept.Table;
        SettleEnded(swept.Ended);
        Unclaimed(swept.Ended);
        MarkedBySession(swept.Steps);
        FreedRuns(swept.Ended);
    }

    // For the tests: a run's step lost the way a stray relocation or a dropped firing would lose it.
    internal void LoseGoalStepForTests(GoalHandle handle) => _events.RemoveAll(SessionEventKinds.GoalStep, e => SessionEventKinds.NamesRun(e, handle));

    // A goal step came due: it is reported for the frame step, which runs the host after the pass. A step whose run is gone (none should be: every end removes
    // its step) is refused and reported to nobody.
    internal GameEventHandlerOutcome FireGoalStep(GameEventFiring firing)
    {
        var e = firing.Event;
        var handle = new GoalHandle((int)e.Payload.Values[0], (int)e.Payload.Values[1]);
        if (!_goals.TryGet(handle, out var run) || !string.Equals(run.Owner, e.Payload.Entities[0].Code, StringComparison.Ordinal)) return GameEventHandlerOutcome.Refused;
        if (_passGoalSteps is null) return GameEventHandlerOutcome.Refused;
        _passGoalSteps.Add(new(e, handle, run.Owner, firing.NowMs));
        _goalStepsReported.Add(handle);
        return GameEventHandlerOutcome.Completed;
    }

    // ARC-DEV-132: an NPC that departs to another map takes no run with it (its steps are removed wherever the relocation carried them). The end nodes of
    // the killed runs are unclaimed ends too.
    private void KillDepartingGoals(string entity)
    {
        if (_goals.InUseCount == 0) return;
        var killed = _goals.Kill(entity);
        _goals = killed.Table;
        SettleEnded(killed.Ended);
        Unclaimed(killed.Ended);
        MarkedBySession(killed.Steps);
        FreedRuns(killed.Ended);
    }

    // Before a pass: a run that is not being stepped, was not reported by an earlier pass for the frame step to open, and has no pending step on the active map
    // (a host that threw before its step closed, a step a relocation carried to another map) is killed and any stray step removed, so no run waits for a step
    // that will not come. Every pass's reported steps are opened by the frame step (`FrameStep`, the only caller of the passes; it abandons the ones it cannot
    // run). Returns the runs it ended (their end nodes are unclaimed ends).
    private ImmutableArray<GoalRunEnd> ReconcileGoalSteps()
    {
        if (_goals.InUseCount == 0) return [];
        var map = _actor.Map.RuntimeMap;
        var pending = _events.QueryPending(SessionEventKinds.GoalStep).Where(e => e.ScopeMap == map)
            .Select(e => new GoalHandle((int)e.Payload.Values[0], (int)e.Payload.Values[1])).ToHashSet();
        var ended = ImmutableArray.CreateBuilder<GoalRunEnd>();
        foreach (var run in _goals.Runs.ToArray())
        {
            if (_goalStepOpen == run.Handle || _goalStepsReported.Contains(run.Handle) || pending.Contains(run.Handle)) continue;
            var killed = _goals.KillRun(run.Handle);
            _goals = killed.Table;
            RemoveGoalStep(run.Handle);
            if (killed.Step.Ended is { } end) ended.Add(end);
        }
        Unclaimed(ended);
        FreedRuns(ended);
        return ended.ToImmutable();
    }
}

internal sealed class GoalStepHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireGoalStep(firing) ?? GameEventHandlerOutcome.Refused;
}
