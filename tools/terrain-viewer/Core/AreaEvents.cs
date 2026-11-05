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

    /// <summary>ARC-WORLD-045: the wait timers that came due, in firing order.</summary>
    public IReadOnlyList<SessionWaitFiring> Waits { get; init; } = [];

    /// <summary>ARC-WORLD-062: the Item Decay events that came due, in firing order.</summary>
    public IReadOnlyList<SessionDecayFiring> Decays { get; init; } = [];

    /// <summary>ARC-WORLD-096: the restock events that came due, in firing order.</summary>
    public IReadOnlyList<SessionRestockFiring> Restocks { get; init; } = [];

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
/// world; caller-serialized like the session. ARC-MAGIC-015: while the viewer has a cast host, a pass goes through it
/// (<see cref="SessionSpellCaster.ProcessEvents"/>), so the spell runs' events run with the map's magic world and their payments
/// are applied.
/// </summary>
public sealed class AreaEventPump
{
    private readonly GameSessionOwner _session;
    private readonly ISessionEventWorld _world;
    private readonly AreaEventPumpLimits _limits;
    private readonly Func<SessionSpellCaster?>? _spells;

    /// <param name="spells">ARC-MAGIC-015: the cast host of the shown map now, or null (no host: plain passes).</param>
    public AreaEventPump(GameSessionOwner session, ISessionEventWorld world, AreaEventPumpLimits? limits = null, Func<SessionSpellCaster?>? spells = null)
    {
        _spells = spells;
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

    /// <summary>An explicit wait (also while paused): one jump of <paramref name="gameMs"/> on game and animation time
    /// (<see cref="GameSessionOwner.JumpClocks"/>, MIG12 S3), then the passes. Events that came due inside the jump fire once,
    /// late, as the original's time jump lets them.</summary>
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
        var token = _session.QueryClockSnapshot().Token;
        var result = reason == ClockAdvanceReason.ExplicitWait ? _session.JumpClocks(token, gameMs) : _session.AdvanceGameTime(token, gameMs, reason);
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
            // Only a host of this pump's session (a replaced session's host answers for another).
            var result = _spells?.Invoke() is { } caster && ReferenceEquals(caster.Owner, _session) ? caster.ProcessEvents() : _session.ProcessEvents(_world);
            if (result.Failure != GameSessionFailure.None || result.Pass is not { } outcome)
                throw new InvalidOperationException("AREA_EVENTS_PASS_" + result.Failure);
            tally.Passes++;
            tally.Fired += outcome.Fired; tally.Dropped += outcome.Dropped;
            tally.Calls += result.ScriptCalls.Count; tally.CallsRun += result.ScriptCalls.Count(c => c.Ran);
            tally.Waits.AddRange(result.Waits);
            tally.Decays.AddRange(result.Decays);
            tally.Restocks.AddRange(result.Restocks);
            tally.BudgetLeft = outcome.BudgetExhausted;
            if (!outcome.BudgetExhausted) return;
        }
    }

    private sealed class Tally
    {
        public int Steps, Passes, Fired, Dropped, Calls, CallsRun;
        public bool Coalesced, BudgetLeft;
        public List<SessionWaitFiring> Waits { get; } = [];
        public List<SessionDecayFiring> Decays { get; } = [];
        public List<SessionRestockFiring> Restocks { get; } = [];

        public AreaEventReport Report(AreaEventPump pump) => new(Steps, Passes, Fired, Dropped, Calls, CallsRun, Coalesced, BudgetLeft,
            pump._session.QueryEvents().Count, pump.NextDeadline(pump._session.QueryClockSnapshot().GameTimestampMs)) { Waits = Waits.ToArray(), Decays = Decays.ToArray(), Restocks = Restocks.ToArray() };
    }
}

/// <summary>ARC-WORLD-039: the area objects an event world asks about (<see cref="AreaCombatHost"/> in the viewer): an NPC's
/// life facts and an object's presence in the stored objects. ARC-DEV-076 (MIG3): both by identity, wherever the object stands now
/// (the resolver finds its record); there is no home-map question any more.</summary>
public interface IAreaEventObjects
{
    LifeFacts? Facts(string entity);
    bool HasObject(string entity);
    /// <summary>ARC-WORLD-103: the NPC's combat values from its stored fields (its wearer and best-items chooser), or null.</summary>
    SessionCritterCombat? Critter(string entity) => null;
}

/// <summary>
/// ARC-WORLD-039: the AREA viewer's world for event passes. An NPC's life facts come from the area's combat fields
/// (<see cref="AreaCombatHost"/>, the same reader the hero's attack uses); an object a deferred call names exists when it is
/// the PC or an object the stored objects of a reachable map hold. A follower the session moved to another map (its timers and call
/// roles travel with it) is asked by identity (ARC-DEV-076, MIG3: the resolver's record), so its events are not dropped as missing. A call runs through the shown area's conversation host and its SCR dispatcher
/// (<see cref="AreaConversationHost.RunCall"/>). The providers are read at each question, so the world follows the
/// viewer's current hosts. ARC-WORLD-045: a wait timeout's reaction change reads the shown area's conversation state (its
/// diagnostic NPC facts, and the hero's or the diagnostic PC's facts); without a conversation host it has none. ARC-WORLD-096: a
/// restock event runs through the viewer's restock host (<see cref="AreaRestockHost"/>); without one it waits a minute.
/// </summary>
public sealed class AreaEventWorld : ISessionScriptWorld, ISessionPartyEventWorld, ISessionLootWindowWorld, ISessionAiWorld, ISessionRestockWorld
{
    private readonly Func<IAreaEventObjects?> _combat;
    private readonly Func<AreaConversationHost?> _talk;
    private readonly Func<bool>? _lootShowsContainer;
    private readonly Func<ISessionAiWorld?>? _ai;
    private readonly Func<ISessionCombatWorld?>? _round;

    /// <param name="prepareCall">ARC-DEV-081 (MIG5): runs before a deferred call does, so the host lists the call's attachee when it is a
    /// companion carried to the shown map (<see cref="AreaConversationHost.AdmitVisitor"/>); the call runs through <paramref name="talk"/>.</param>
    /// <param name="lootShowsContainer">ARC-WORLD-063: whether the viewer's loot window shows a container (a junk pile); an item's
    /// decay then waits a minute (ledger ID3, ID6). Null: never.</param>
    /// <param name="ai">ARC-WORLD-092: the shown map's AI host, which an AI tick of the pass runs through; null or a null answer: no AI
    /// runs (every NPC counts as gone and its sector as not loaded, so its tick schedules no next one).</param>
    /// <param name="round">ARC-WORLD-092: the turn-based round's world of the shown map, for the AI's subturn ends; null: none.</param>
    /// <param name="restock">ARC-WORLD-096: the restock host of the pass's session; null or a null answer: a restock event waits.</param>
    public AreaEventWorld(Func<IAreaEventObjects?> combat, Func<AreaConversationHost?> talk, Action<SessionScriptCall>? prepareCall = null,
        Func<bool>? lootShowsContainer = null, Func<ISessionAiWorld?>? ai = null, Func<ISessionCombatWorld?>? round = null,
        Func<AreaRestockHost?>? restock = null)
    {
        _restock = restock;
        _ai = ai;
        _round = round;
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _talk = talk ?? throw new ArgumentNullException(nameof(talk));
        _prepareCall = prepareCall;
        _lootShowsContainer = lootShowsContainer;
    }

    public bool LootWindowShowsContainer => _lootShowsContainer?.Invoke() == true;

    // ---- ARC-WORLD-096: the restock event's host, forwarded to the viewer's restock host ----

    private readonly Func<AreaRestockHost?>? _restock;
    private AreaRestockHost? Restock => _restock?.Invoke();
    public bool RestockReady => Restock is not null;
    public Arcanum.Simulation.Items.RestockHolder? RestockFacts(SessionMapId map, string entity) => Restock?.Facts(map, entity);
    public string? RestockMerchant(SessionMapId map, string container) => Restock?.Merchant(map, container);
    public bool Persistent(SessionMapId map, string item) => Restock?.Persistent(map, item) == true;
    public SessionRestockSet RestockSet(int source) => Restock?.Set(source) ?? new(SessionRestockSetStatus.Unsupported, null);
    public SessionRestockItem? RestockItem(int basicPrototype) => Restock?.Item(basicPrototype);
    public SessionItemKind RestockPileKind => Restock?.PileKind ?? new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0);
    // The viewer's insert check: every item fits (carry weight and room need the created character, NOT READY).
    public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => AreaInventoryHost.DiagnosticInsertCheck;
    // ARC-WORLD-101: a generated-once container's tile x and the hero's player name.
    public SessionGeneratedOnceFacts? GeneratedOnceFacts(SessionMapId map, string container) => Restock?.GeneratedOnceFacts(map, container);
    /// <summary>ARC-WORLD-103: an NPC's best-items wield after its regeneration reads its combat values from the area's combat fields.</summary>
    public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => Restock?.Wielder(map, npc, _combat()?.Critter(npc));

    private readonly Action<SessionScriptCall>? _prepareCall;

    /// <summary>The reason the last refused call did not run, or null; <see cref="CallRefusals"/> counts the refusals.</summary>
    public string? LastCallRefusal { get; private set; }
    public int CallRefusals { get; private set; }

    public LifeFacts? CritterFacts(SessionMapId map, string entity)
    {
        // ARC-DEV-076 (MIG3): by identity, wherever the object stands: a follower the session moved answers as on its home map.
        return _combat()?.Facts(entity);
    }

    public bool ObjectExists(SessionMapId map, string entity) =>
        entity == SessionEventKinds.PlayerEntity || _combat()?.HasObject(entity) == true;

    public Arcanum.Simulation.Reaction.ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => _talk()?.State.NpcFacts();

    public Arcanum.Simulation.Reaction.ReactionPcFacts? ReactionPcFacts() => _talk()?.State.PcFacts;

    public bool RunScriptCall(SessionScriptCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        _prepareCall?.Invoke(call);
        if (_talk() is not { } host) { Refuse("no script host on this map"); return false; }
        var ran = host.RunCall(call, out var detail);
        if (!ran) Refuse(detail);
        return ran;
    }

    private void Refuse(string detail)
    {
        LastCallRefusal = detail;
        CallRefusals++;
    }

    // ---- ARC-WORLD-092: the AI tick's host, forwarded to the shown map's AI host ----

    private ISessionAiWorld? Ai => _ai?.Invoke();

    public SessionLifeTarget? AiLifeTarget(string entity) => Ai?.AiLifeTarget(entity);
    public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => _round?.Invoke()?.ScanCombatants(map) ?? [];
    public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => _round?.Invoke()?.CombatantFacts(map, entity);
    public long DistanceToPc(string npc) => Ai?.DistanceToPc(npc) ?? -1;
    public bool RunScript(Arcanum.Simulation.Ai.AiScriptPoint point, string npc, string? triggerer) => Ai?.RunScript(point, npc, triggerer) ?? true;
    public bool RunWaypoints(string npc) => Ai?.RunWaypoints(npc) ?? false;
    public void RunStandpoints(string npc) => Ai?.RunStandpoints(npc);
    public bool IsGenerator(string npc) => Ai?.IsGenerator(npc) ?? false;
    public long? Spawn(string npc) => Ai?.Spawn(npc);
    public bool IsEncounterCreature(string npc) => Ai?.IsEncounterCreature(npc) ?? false;
    public void Destroy(string npc) => Ai?.Destroy(npc);
    public void InterruptAnimations(string npc) => Ai?.InterruptAnimations(npc);
    // Without a host the tick is not rescheduled (its NPC counts as gone and its sector as not loaded), as another host consumes it.
    public bool SectorLoaded(string npc) => Ai?.SectorLoaded(npc) ?? false;
    public Arcanum.Simulation.Ai.AiCatchUpFacts CatchUp(string npc) => Ai?.CatchUp(npc) ?? new();
    public void MoveToLeader(string npc) => Ai?.MoveToLeader(npc);
    public bool MapClearing => Ai?.MapClearing ?? false;
    public bool Sleeping(string npc) => Ai?.Sleeping(npc) ?? false;
    public bool Hidden(string npc) => Ai?.Hidden(npc) ?? false;
    public bool PcHidden => Ai?.PcHidden ?? false;
    public Arcanum.Simulation.Ai.AiConversation Conversation(string npc) => Ai?.Conversation(npc) ?? Arcanum.Simulation.Ai.AiConversation.None;
    public bool ConversationCallback => Ai?.ConversationCallback ?? false;
    public void EndConversation(string npc) => Ai?.EndConversation(npc);
    public int AnimationPriority(string npc) => Ai?.AnimationPriority(npc) ?? 0;
    public bool StunAnimationRunning(string npc) => Ai?.StunAnimationRunning(npc) ?? false;
    public bool AnimationRunning(string npc) => Ai?.AnimationRunning(npc) ?? false;
    public void ReWield(string npc, bool allSlots) => Ai?.ReWield(npc, allSlots);
    public string? Leader(string npc) => Ai?.Leader(npc);
    public void EndMaintainedSpells(string npc) => Ai?.EndMaintainedSpells(npc);
    public bool Heal(string npc) => Ai?.Heal(npc) ?? false;
    public bool SearchItems(string npc) => Ai?.SearchItems(npc) ?? false;
    public void UpdateDanger(string npc) => Ai?.UpdateDanger(npc);
    public void Perform(string npc) => Ai?.Perform(npc);
    public bool Loading => Ai?.Loading ?? false;
}
