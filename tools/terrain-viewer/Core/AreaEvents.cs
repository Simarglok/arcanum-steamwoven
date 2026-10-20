using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Bounds of one <see cref="AreaEventPump"/> call: game-time steps per ordinary advance (beyond them the rest of
/// the interval is one step and its events fire late, <see cref="AreaEventReport.Coalesced"/>), and passes per step while a
/// pass leaves due events behind its budget (the rest wait for the next step or frame).</summary>
public sealed record AreaEventPumpLimits
{
    public int MaxStepsPerAdvance { get; init; } = 64;
    public int MaxPassesPerStep { get; init; } = 8;

    internal void Validate()
    {
        if (MaxStepsPerAdvance is < 1 or > 4096 || MaxPassesPerStep is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(AreaEventPumpLimits), "Pump limits must be 1..4096 steps and 1..64 passes.");
    }
}

/// <summary>
/// What one pump call did: clock steps and passes, the events taken (fired or dropped), the deferred script calls that came
/// due and how many ran, whether the step bound folded the rest of the interval into one step, whether due events were
/// left behind the pass budget, and the queue after the call (pending events and the next game-time deadline of the
/// active map, or null).
/// </summary>
public sealed record AreaEventReport(int Steps, int Passes, int Fired, int Dropped, int ScriptCalls, int ScriptCallsRun, bool Coalesced,
    bool BudgetLeft, int Pending, long? NextDeadlineMs)
{
    public static AreaEventReport None { get; } = new(0, 0, 0, 0, 0, 0, false, false, 0, null);

    /// <summary>The diagnostic status line (FR-RUNTIME-001 trace).</summary>
    public string Status(long nowMs) =>
        $"events pending {Pending}, next {(NextDeadlineMs is { } next ? $"in {(next - nowMs) / 1000.0:0.###} s" : "none")}, last pass fired {Fired} dropped {Dropped}"
        + (ScriptCalls > 0 ? $", calls {ScriptCallsRun}/{ScriptCalls} ran" : "") + (Coalesced ? ", late (step bound)" : "") + (BudgetLeft ? ", budget left due" : "");
}

/// <summary>
/// ARC-WORLD-039, event-queue slice E3 (docs/world/game-event-queue-runtime.md#host-pump-arc-world-039): the AREA viewer's
/// pump of the session's event queue. Ordinary time advances in steps that end at each pending game-time deadline of the
/// active map, and a pass runs after every step, so every event fires at its own deadline and the result does not depend
/// on how the host splits time into frames (FR-RUNTIME-001). An explicit wait is one jump followed by a pass: its events
/// fire late and see the final time, as the original's time jump does. Due events left behind a pass's budget get more
/// passes, at most <see cref="AreaEventPumpLimits.MaxPassesPerStep"/> per step. The pump owns neither the session nor the
/// world; caller-serialized like the session.
/// </summary>
public sealed class AreaEventPump
{
    private readonly GameSessionOwner _session;
    private readonly ISessionEventWorld _world;
    private readonly AreaEventPumpLimits _limits;

    public AreaEventPump(GameSessionOwner session, ISessionEventWorld world, AreaEventPumpLimits? limits = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _limits = limits ?? new();
        _limits.Validate();
    }

    /// <summary>The last call's report.</summary>
    public AreaEventReport Last { get; private set; } = AreaEventReport.None;

    /// <summary>
    /// Ordinary flow of <paramref name="gameMs"/> game milliseconds, stepped at each pending deadline, with a pass after every
    /// step (and one before the first, for events made due by a step outside the pump). A paused clock admits nothing, and
    /// then only that first pass runs. Returns the report; a clock refusal throws (the host's invariant, as for the lighting).
    /// </summary>
    public AreaEventReport Advance(long gameMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gameMs);
        var tally = new Tally();
        Drain(tally);
        var remaining = gameMs;
        while (remaining > 0)
        {
            var now = _session.QueryClockSnapshot().GameTimestampMs;
            var step = remaining;
            if (NextDeadline(now) is { } next && next - now < remaining)
            {
                if (tally.Steps + 1 < _limits.MaxStepsPerAdvance) step = next - now;
                else tally.Coalesced = true;
            }
            if (!Move(step, ClockAdvanceReason.OrdinaryAdvance)) break;
            remaining -= step;
            tally.Steps++;
            Drain(tally);
        }
        return Last = tally.Report(this);
    }

    /// <summary>An explicit wait (also while paused): one jump of <paramref name="gameMs"/>, then the passes. Events that
    /// came due inside the jump fire once, late, as the original's time jump lets them.</summary>
    public AreaEventReport Jump(long gameMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameMs);
        var tally = new Tally();
        if (Move(gameMs, ClockAdvanceReason.ExplicitWait)) tally.Steps++;
        Drain(tally);
        return Last = tally.Report(this);
    }

    /// <summary>The earliest pending game-time deadline after <paramref name="nowMs"/> of a global event or one of the actor's
    /// map (events of other maps wait for their map and do not end a step), or null.</summary>
    public long? NextDeadline(long nowMs)
    {
        var map = _session.QueryActor().Map.RuntimeMap;
        long? best = null;
        foreach (var e in _session.QueryEvents())
            if (e.Domain == GameEventDomain.Game && e.DeadlineMs > nowMs && (e.ScopeMap == 0 || e.ScopeMap == map) && (best is null || e.DeadlineMs < best))
                best = e.DeadlineMs;
        return best;
    }

    private bool Move(long gameMs, ClockAdvanceReason reason)
    {
        var result = _session.AdvanceGameTime(_session.QueryClockSnapshot().Token, gameMs, reason);
        return result.Disposition switch
        {
            ClockMutationDisposition.Changed => true,
            ClockMutationDisposition.NoChange => false,
            _ => throw new InvalidOperationException("AREA_EVENTS_CLOCK_" + result.Failure),
        };
    }

    private void Drain(Tally tally)
    {
        for (var pass = 0; pass < _limits.MaxPassesPerStep; pass++)
        {
            var result = _session.ProcessEvents(_world);
            if (result.Failure != GameSessionFailure.None || result.Pass is not { } outcome)
                throw new InvalidOperationException("AREA_EVENTS_PASS_" + result.Failure);
            tally.Passes++;
            tally.Fired += outcome.Fired; tally.Dropped += outcome.Dropped;
            tally.Calls += result.ScriptCalls.Count; tally.CallsRun += result.ScriptCalls.Count(c => c.Ran);
            tally.BudgetLeft = outcome.BudgetExhausted;
            if (!outcome.BudgetExhausted) return;
        }
    }

    private sealed class Tally
    {
        public int Steps, Passes, Fired, Dropped, Calls, CallsRun;
        public bool Coalesced, BudgetLeft;

        public AreaEventReport Report(AreaEventPump pump) => new(Steps, Passes, Fired, Dropped, Calls, CallsRun, Coalesced, BudgetLeft,
            pump._session.QueryEvents().Count, pump.NextDeadline(pump._session.QueryClockSnapshot().GameTimestampMs));
    }
}

/// <summary>
/// ARC-WORLD-039: the AREA viewer's world for event passes. An NPC's life facts come from the area's combat fields
/// (<see cref="AreaCombatHost"/>, the same reader the hero's attack uses); an object a deferred call names exists when it is
/// the PC or a row of its map; a call runs through the shown area's conversation host and its SCR dispatcher
/// (<see cref="AreaConversationHost.RunCall"/>). The providers are read at each question, so the world follows the
/// viewer's current hosts.
/// </summary>
public sealed class AreaEventWorld : ISessionScriptWorld
{
    private readonly Func<AreaCombatHost?> _combat;
    private readonly Func<AreaConversationHost?> _talk;

    public AreaEventWorld(Func<AreaCombatHost?> combat, Func<AreaConversationHost?> talk)
    {
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _talk = talk ?? throw new ArgumentNullException(nameof(talk));
    }

    /// <summary>The reason the last call did not run, or null.</summary>
    public string? LastCallRefusal { get; private set; }

    public LifeFacts? CritterFacts(SessionMapId map, string entity) => _combat()?.Build(map.RuntimeMap, entity).Combat?.Facts;

    public bool ObjectExists(SessionMapId map, string entity) =>
        entity == SessionEventKinds.PlayerEntity || (_combat()?.HasObject(map.RuntimeMap, entity) ?? false);

    public bool RunScriptCall(SessionScriptCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_talk() is not { } host) { LastCallRefusal = "no script host on this map"; return false; }
        var ran = host.RunCall(call, out var detail);
        LastCallRefusal = ran ? null : detail;
        return ran;
    }
}
