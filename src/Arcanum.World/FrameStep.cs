using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;

namespace Arcanum.World;

/// <summary>
/// MIG12 S4 (docs/architecture/clocks-frame.md §3.3, TM6, TM11): a system that moves with the frame and reads its clocks, run once per
/// frame after the event passes, in registration order (the AI host, the goal runners, projectiles, missiles, eye candy and scenery
/// register here in the slices S5 and S6). The port lets the hosts that still live in the viewer be called without the world runtime
/// referencing them. A participant reads <see cref="FrameClocks"/> only (<see cref="FrameClocks.WorldMs"/> for the viewer's tuned
/// speeds); it must not read Godot time and must not move a clock.
/// </summary>
public interface IFrameParticipant
{
    /// <param name="clocks">The clocks at the end of the frame, after every event of the frame fired.</param>
    /// <param name="flags">The pause and round flags at the end of the frame (a participant gates its own work by them).</param>
    void Step(FrameClocks clocks, FrameFlags flags);
}

/// <summary>
/// MIG12 S8 (ARC-DEV-127, docs/architecture/clocks-frame.md §3.5): the host of the chains the original keeps on the real-time list: the
/// world-map journey's travel step and the turn-based event. The session's pass only reports a step that came due
/// (<see cref="SessionRealStepFiring"/>); the frame step calls the host right after that pass, outside it, so the host acts on the session as any
/// caller does (a time jump, a discovery, a round's subturns), and runs one more pass at once so the events a jump made due fire in the same frame
/// (TL20). Real time runs behind the Esc menu, so a step falls due behind it too.
/// </summary>
public interface IRealStepHost
{
    /// <summary>Runs one step that fell due. Returns the real milliseconds after which the step is due again (1..<see cref="GameSessionOwner.MaxRealStepDelayMs"/>,
    /// counted from now, as the original's handler adds its next event), or null when the chain ends.</summary>
    long? RunRealStep(SessionRealStep step, FrameClocks clocks);
}

/// <summary>
/// ARC-DEV-132 (MIG13 S3, docs/architecture/goal-table.md §3.2): the host of the goal runs' steps. The session's pass only reports a <c>goal.step</c> that came due
/// (<see cref="SessionGoalStepFiring"/>); the frame step opens the run's step (<see cref="GameSessionOwner.BeginGoalStep"/>), calls the host with the clocks at the
/// step's due time, closes the step with the answer (<see cref="GameSessionOwner.EndGoalStep"/>: the next step queued from now, or the run's end) and runs one more
/// pass. One mechanism steps every run, in deadline order. The host steps the run (S4: the stepper of the world runtime over the viewer's node ports); while the step
/// is open an interrupt or a kill of the run only marks it done, and the close ends it.
/// </summary>
public interface IGoalStepHost
{
    /// <summary>Steps <paramref name="run"/> once. Returns the run as the step left it and the real milliseconds of its next frame delay, or the end
    /// (<see cref="SessionGoalStepAnswer.End"/>).</summary>
    SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks);
}

/// <summary>Bounds of one <see cref="FrameStep"/> frame: sub-steps per frame (beyond them the rest of the frame is one step and its
/// events fire late, <see cref="FrameStepReport.Coalesced"/>), and passes per sub-step while a pass leaves due events behind its
/// budget (the rest wait for the next step or frame).</summary>
public sealed record FrameStepLimits
{
    public int MaxStepsPerFrame { get; init; } = 64;
    public int MaxPassesPerStep { get; init; } = 8;

    internal void Validate()
    {
        if (MaxStepsPerFrame is < 1 or > 4096 || MaxPassesPerStep is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(FrameStepLimits), "Frame step limits must be 1..4096 steps and 1..64 passes.");
    }
}

/// <summary>
/// What one frame (or jump) did: sub-steps and passes, the events taken (fired or dropped), the deferred script calls that came due and
/// how many ran, whether the step bound folded the rest of the frame into one step, whether due events were left behind the pass
/// budget, the queue after the call (pending events and the next game-time deadline of the active map, or null), the clocks at its end,
/// whether the game clock's hour changed (the lighting hour event: the host re-selects its scheme) and how many participants ran.
/// </summary>
public sealed record FrameStepReport(int Steps, int Passes, int Fired, int Dropped, int ScriptCalls, int ScriptCallsRun, bool Coalesced,
    bool BudgetLeft, int Pending, long? NextDeadlineMs)
{
    /// <summary>MIG12 S8: the real-time steps that came due and were run by the host (<see cref="IRealStepHost"/>), in order.</summary>
    public IReadOnlyList<SessionRealStep> RealSteps { get; init; } = [];

    /// <summary>MIG12 S8: the real-time steps that came due with no host registered (consumed unrun).</summary>
    public int RealStepsUnhandled { get; init; }

    /// <summary>MIG12 S8: the repeats the host asked for that the session refused (a delay out of range, a full queue); the chain ended there.</summary>
    public int RealStepsRefused { get; init; }

    /// <summary>ARC-DEV-132: the goal runs whose steps came due and were run by the host (<see cref="IGoalStepHost"/>), in order.</summary>
    public IReadOnlyList<GoalHandle> GoalSteps { get; init; } = [];

    /// <summary>ARC-DEV-132: the goal steps that came due with no host registered: their runs ended (a run nobody steps would never end).</summary>
    public int GoalStepsUnhandled { get; init; }

    /// <summary>ARC-DEV-132: the goal steps whose repeat the session refused (a delay out of range, a run that is not the stepped one's, a full queue); their runs ended.</summary>
    public int GoalStepsRefused { get; init; }

    public static FrameStepReport None { get; } = new(0, 0, 0, 0, 0, 0, false, false, 0, null);

    /// <summary>ARC-WORLD-045: the wait timers that came due, in firing order.</summary>
    public IReadOnlyList<SessionWaitFiring> Waits { get; init; } = [];

    /// <summary>ARC-WORLD-062: the Item Decay events that came due, in firing order.</summary>
    public IReadOnlyList<SessionDecayFiring> Decays { get; init; } = [];

    /// <summary>ARC-WORLD-096: the restock events that came due, in firing order.</summary>
    public IReadOnlyList<SessionRestockFiring> Restocks { get; init; } = [];

    /// <summary>The clocks at the end of the call.</summary>
    public FrameClocks Clocks { get; init; }

    /// <summary>The game clock's hour at the end of the call differs from its hour at the start (a jump counts too).</summary>
    public bool HourChanged { get; init; }

    /// <summary>The participants that ran after the passes (none in a jump, which is not a frame).</summary>
    public int Participants { get; init; }

    /// <summary>The diagnostic status line (FR-RUNTIME-001 trace).</summary>
    public string Status(long nowMs) =>
        $"events pending {Pending}, next {(NextDeadlineMs is { } next ? $"in {(next - nowMs) / 1000.0:0.###} s" : "none")}, last pass fired {Fired} dropped {Dropped}"
        + (ScriptCalls > 0 ? $", calls {ScriptCallsRun}/{ScriptCalls} ran" : "") + (Coalesced ? ", late (step bound)" : "") + (BudgetLeft ? ", budget left due" : "");
}

/// <summary>
/// MIG12 S4 (ARC-DEV-123, TM6, TM11): the world runtime's frame step, and with it the one place ordinary game time moves. The viewer's
/// only call is <see cref="Run"/> with the host stamp of the frame (and whether the original would not ping in it); nothing here reads
/// Godot time. One frame is, in order:
/// <list type="number">
/// <item>the change-of-place ping (TM12), the <c>placePing</c> the host registered, so a request is carried out before the frame's time
/// moves; none registered: nothing runs (the hero's own request is still pinged by the viewer in the frame of the request, CD7);</item>
/// <item>the admission (<see cref="GameSessionOwner.BeginFrame"/>: 5 ms floor, 250 ms cut, ×8) and the passes of the event queue: the
/// frame's <c>8i</c> units are divided at the next due time of any of the three domains, every sub-step moves real, game and animation
/// time together by the flags of that moment (the pause stops game and animation, a round stops game), and a pass runs real → game →
/// animation after each (decision GI5: an event fires at its own deadline, so the result does not depend on how the host splits time into
/// frames; a real-time handler's jump is seen by the later lists of the same pass, TL20);</item>
/// <item>the frame participants, in registration order, each given the clocks of the frame.</item>
/// </list>
/// The sub-steps are bounded (<see cref="FrameStepLimits"/>); beyond the bound the rest of the frame is one step and the events inside it
/// fire late, reported as <see cref="FrameStepReport.Coalesced"/>. <see cref="Jump"/> is a time jump (TL26): not a frame, ignoring pause
/// and rounds, followed by one set of passes. The step owns neither the session nor the world and is caller-serialized like the session
/// (the main thread). ARC-MAGIC-015: while the viewer has a cast host a pass goes through it (<see cref="SessionSpellCaster.ProcessEvents"/>),
/// so the spell runs' events run with the map's magic world and their payments are applied.
/// </summary>
public sealed class FrameStep
{
    private readonly GameSessionOwner _session;
    private readonly ISessionEventWorld? _world;
    private readonly FrameStepLimits _limits;
    private readonly Func<SessionSpellCaster?>? _spells;
    private readonly Action? _placePing;
    private readonly List<IFrameParticipant> _participants = [];
    private IRealStepHost? _realSteps;
    private IGoalStepHost? _goalSteps;

    /// <param name="world">The event world the passes ask, or null: the clocks move and no event passes run (a host that runs no gameplay events).</param>
    /// <param name="spells">ARC-MAGIC-015: the cast host of the shown map now, or null (no host: plain passes).</param>
    /// <param name="placePing">TM12: the change-of-place ping that opens each frame, or null.</param>
    public FrameStep(GameSessionOwner session, ISessionEventWorld? world, FrameStepLimits? limits = null, Func<SessionSpellCaster?>? spells = null,
        Action? placePing = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world;
        _spells = spells;
        _placePing = placePing;
        _limits = limits ?? new();
        _limits.Validate();
    }

    /// <summary>The session whose clocks this step moves.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The last call's report.</summary>
    public FrameStepReport Last { get; private set; } = FrameStepReport.None;

    /// <summary>Whether this step runs event passes (it was given an event world).</summary>
    public bool RunsEvents => _world is not null;

    /// <summary>The participants, in the order they run.</summary>
    public IReadOnlyList<IFrameParticipant> Participants => _participants;

    /// <summary>MIG12 S8: the host of the real-time steps (<see cref="IRealStepHost"/>), or null (a step that falls due is consumed unrun). Set by the
    /// viewer when it binds the step to its session; the host outlives the frame step's frames, not the session.</summary>
    public IRealStepHost? RealSteps { get => _realSteps; set => _realSteps = value; }

    /// <summary>ARC-DEV-132 (MIG13 S3): the host of the goal runs' steps (<see cref="IGoalStepHost"/>), or null (a step that falls due ends its run). Set by the viewer
    /// when it binds the step to its session (S4).</summary>
    public IGoalStepHost? GoalSteps { get => _goalSteps; set => _goalSteps = value; }

    /// <summary>Registers a participant after the ones already registered (TM6's fixed order).</summary>
    public void Register(IFrameParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (_participants.Contains(participant)) throw new InvalidOperationException("FRAME_PARTICIPANT_REGISTERED");
        _participants.Add(participant);
    }

    /// <summary>
    /// One frame at <paramref name="hostMs"/> (a monotone 64-bit millisecond stamp of the host). A <paramref name="frozen"/> frame (a loop
    /// of the original that does not ping: message box, movie, slide show, fade, loading; TL25) observes no stamp, moves no clock and runs
    /// one set of passes and the participants. Behind the pause real time still moves, so a paused frame is one step with a pass either side. A
    /// refusal of the clocks throws (the host's invariant, as for the lighting); a frame is never left open.
    /// </summary>
    public FrameStepReport Run(long hostMs, bool frozen = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hostMs);
        var hour = _session.QueryClockSnapshot().Hour;
        _placePing?.Invoke();
        var opened = _session.BeginFrame(hostMs, frozen);
        if (opened.Failure != FrameClockFailure.None || opened.Frame is not { } frame) throw new InvalidOperationException("FRAME_STEP_" + opened.Failure);
        var tally = new Tally();
        try
        {
            Drain(tally);
            while (frame.RemainingUnits > 0)
            {
                var step = frame.RemainingUnits;
                if (NextDeadline(frame) is { } next && next < step)
                {
                    if (tally.Steps + 1 < _limits.MaxStepsPerFrame) step = next;
                    else tally.Coalesced = true;
                }
                var moved = frame.Step(step);
                if (!moved.Ok) throw new InvalidOperationException("FRAME_STEP_" + (moved.Failure != FrameClockFailure.None ? moved.Failure : moved.ClockFailure));
                tally.Steps++;
                Drain(tally);
            }
            var clocks = frame.Clocks;
            // The frame's flags are the session's pause and round at its end; a frozen frame (TL25) is the host's word, not the session's.
            var flags = frame.Flags with { Frozen = frozen };
            foreach (var participant in _participants) participant.Step(clocks, flags);
        }
        finally { frame.End(); }
        return Last = tally.Report(this, hour, _participants.Count);
    }

    /// <summary>A time jump (TL26: the world-map step, a timed teleport, a wait; also while paused or in a round): one jump of
    /// <paramref name="gameMs"/> on game and animation time together (<see cref="GameSessionOwner.JumpClocks"/>), then the passes. Events
    /// that came due inside the jump fire once, late, as the original's time jump lets them. No frame is open and no participant runs.</summary>
    public FrameStepReport Jump(long gameMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameMs);
        var hour = _session.QueryClockSnapshot().Hour;
        var tally = new Tally();
        var result = _session.JumpClocks(_session.QueryClockSnapshot().Token, gameMs);
        if (result.Disposition == ClockMutationDisposition.Rejected) throw new InvalidOperationException("FRAME_JUMP_" + result.Failure);
        if (result.Disposition == ClockMutationDisposition.Changed) tally.Steps++;
        Drain(tally);
        return Last = tally.Report(this, hour, 0);
    }

    /// <summary>The earliest pending game-time deadline after <paramref name="nowMs"/> of a global event or one of the actor's
    /// map (events of other maps wait for their map and do not end a step), or null.</summary>
    public long? NextDeadline(long nowMs) => NextDeadline(GameEventDomain.Game, nowMs);

    private long? NextDeadline(GameEventDomain domain, long nowMs)
    {
        var map = _session.QueryActor().Map.RuntimeMap;
        long? best = null;
        foreach (var e in _session.QueryEvents())
            if (e.Domain == domain && e.DeadlineMs > nowMs && (e.ScopeMap == 0 || e.ScopeMap == map) && (best is null || e.DeadlineMs < best))
                best = e.DeadlineMs;
        return best;
    }

    // The units of the open frame to the nearest due time of any domain (events of other maps wait for their map), or null.
    private long? NextDeadline(SessionFrame frame)
    {
        var map = _session.QueryActor().Map.RuntimeMap;
        long? best = null;
        foreach (var e in _session.QueryEvents())
        {
            if (e.ScopeMap != 0 && e.ScopeMap != map) continue;
            if (frame.UnitsUntil(e.Domain, e.DeadlineMs) is { } units && units > 0 && (best is null || units < best)) best = units;
        }
        return best;
    }

    private void Drain(Tally tally)
    {
        if (_world is null) return;
        for (var pass = 0; pass < _limits.MaxPassesPerStep; pass++)
        {
            // Only a host of this step's session (a replaced session's host answers for another).
            var result = _spells?.Invoke() is { } caster && ReferenceEquals(caster.Owner, _session) ? caster.ProcessEvents() : _session.ProcessEvents(_world);
            if (result.Failure != GameSessionFailure.None || result.Pass is not { } outcome)
                throw new InvalidOperationException("FRAME_STEP_PASS_" + result.Failure);
            tally.Passes++;
            tally.Fired += outcome.Fired; tally.Dropped += outcome.Dropped;
            tally.Calls += result.ScriptCalls.Count; tally.CallsRun += result.ScriptCalls.Count(c => c.Ran);
            tally.Waits.AddRange(result.Waits);
            tally.Decays.AddRange(result.Decays);
            tally.Restocks.AddRange(result.Restocks);
            tally.BudgetLeft = outcome.BudgetExhausted;
            // MIG12 S8: the real-time steps that came due run after the pass, outside it; a step that jumped the clocks lets the next pass fire
            // the events that became due (the original's later lists see a real-time handler's jump, TL20).
            var ran = RunRealSteps(result.RealSteps, tally);
            // ARC-DEV-132: the goal steps that came due run next, in firing order; their repeats are queued from now, so the next pass fires only what a step made due.
            ran += RunGoalSteps(result.GoalSteps, tally);
            if (!outcome.BudgetExhausted && ran == 0) return;
        }
    }

    // Each due step, in firing order: the host acts, then the step is scheduled again from now when the host asks. Returns the steps run.
    private int RunRealSteps(IReadOnlyList<SessionRealStepFiring> due, Tally tally)
    {
        var ran = 0;
        foreach (var firing in due)
        {
            if (_realSteps is not { } host) { tally.RealStepsUnhandled++; continue; }
            ran++;
            tally.RealSteps.Add(firing.Step);
            var next = host.RunRealStep(firing.Step, _session.QueryFrameClocks());
            // A refused repeat (a delay out of range, a full queue) ends the chain and is reported, not thrown: the other steps of the pass
            // still run, and the host's owner re-arms from its frame when it still needs the step.
            if (next is { } delay && _session.ScheduleRealStep(firing.Step, delay) == GameSessionDisposition.Rejected) tally.RealStepsRefused++;
        }
        return ran;
    }

    // Each due goal step, in firing order: the step is opened, the host steps the run and the step is closed with its answer. With no host the run ends.
    // A step the session cannot open (its run ended in an earlier step of the pass) is skipped. Returns the steps run.
    private int RunGoalSteps(IReadOnlyList<SessionGoalStepFiring> due, Tally tally)
    {
        var ran = 0;
        foreach (var firing in due)
        {
            if (_session.BeginGoalStep(firing.Handle) is not { } run) continue;
            if (_goalSteps is not { } host)
            {
                _session.EndGoalStep(firing.Handle, SessionGoalStepAnswer.End);
                tally.GoalStepsUnhandled++;
                continue;
            }
            ran++;
            tally.GoalSteps.Add(firing.Handle);
            SessionGoalStepAnswer answer;
            try { answer = host.StepGoal(run, _session.QueryFrameClocks()); }
            catch
            {
                // A host that threw leaves no open step behind: the run ends.
                _session.EndGoalStep(firing.Handle, SessionGoalStepAnswer.End);
                throw;
            }
            if (_session.EndGoalStep(firing.Handle, answer).Outcome == SessionGoalStepOutcome.Refused) tally.GoalStepsRefused++;
        }
        return ran;
    }

    private sealed class Tally
    {
        public int Steps, Passes, Fired, Dropped, Calls, CallsRun, RealStepsUnhandled, RealStepsRefused, GoalStepsUnhandled, GoalStepsRefused;
        public List<SessionRealStep> RealSteps { get; } = [];
        public List<GoalHandle> GoalSteps { get; } = [];
        public bool Coalesced, BudgetLeft;
        public List<SessionWaitFiring> Waits { get; } = [];
        public List<SessionDecayFiring> Decays { get; } = [];
        public List<SessionRestockFiring> Restocks { get; } = [];

        public FrameStepReport Report(FrameStep step, int hourBefore, int participants)
        {
            var clock = step._session.QueryClockSnapshot();
            return new(Steps, Passes, Fired, Dropped, Calls, CallsRun, Coalesced, BudgetLeft, step._session.QueryEvents().Count,
                step.NextDeadline(clock.GameTimestampMs))
            {
                Waits = Waits.ToArray(), Decays = Decays.ToArray(), Restocks = Restocks.ToArray(),
                Clocks = step._session.QueryFrameClocks(), HourChanged = clock.Hour != hourBefore, Participants = participants,
                RealSteps = RealSteps.ToArray(), RealStepsUnhandled = RealStepsUnhandled, RealStepsRefused = RealStepsRefused,
                GoalSteps = GoalSteps.ToArray(), GoalStepsUnhandled = GoalStepsUnhandled, GoalStepsRefused = GoalStepsRefused,
            };
        }
    }
}
