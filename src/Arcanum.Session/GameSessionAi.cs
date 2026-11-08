using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-084 (NPC AI slice I6): the host of the AI scheduler. Besides the event pass's script and combat ports it answers
/// <see cref="IAiHost"/> about the critters of the actor's map (ids are entity ids, <see cref="AiSchedulerRules.Pc"/> the hero)
/// and gives an NPC's life facts and loaded state. The session answers death, unconsciousness, stoning, the round, combat mode,
/// the danger record, the critter-flags word's parts it holds and a companion's waiting itself.
/// </summary>
public interface ISessionAiWorld : ISessionScriptWorld, ISessionCombatWorld, IAiHost
{
    /// <summary>The NPC's life facts and loaded state on the actor's map, or null when it is gone (it then counts as dead).</summary>
    SessionLifeTarget? AiLifeTarget(string entity);
}

/// <summary>How an AI tick starts (0x4AD790 immediate, 0x4AD6E0 delayed).</summary>
public enum SessionAiTickStart
{
    /// <summary>A sector fold with the fold flag (no eviction record, or away 3 s or more): the tick is due 1 ms from now and carries the
    /// wake-up marker.</summary>
    WakeUp,
    /// <summary>A placed, created, loaded or resurrected NPC: the tick comes after the distance delay, without the marker.</summary>
    Delayed,
    /// <summary>ARC-DEV-104 (RL8, RL17): a sector fold whose plan sets no fold flag (the sector was away less than 3 s): the tick is due 1 ms
    /// from now, without the marker.</summary>
    Immediate,
}

/// <summary>An AI tick to schedule for the NPC <see cref="Entity"/> on the actor's map; <see cref="DistanceToPc"/> (negative: none)
/// sets a delayed tick's delay.</summary>
public sealed record SessionAiTickRequest
{
    public required string Entity { get; init; }
    public SessionAiTickStart Start { get; init; }
    public long DistanceToPc { get; init; }
}

/// <summary>An AI tick that came due: the NPC, whether it carried the wake-up marker and what it did (null: the pass's host is not an
/// <see cref="ISessionAiWorld"/>, so it did not run and no tick follows).</summary>
public sealed record SessionAiTickFiring(GameEvent Event, string Entity, bool WakeUp, AiTickResult? Tick)
{
    /// <summary>The write the scheduler could not make (a session budget), or None.</summary>
    public GameSessionFailure WriteFailure { get; init; }
}

/// <summary>An AI pass or subturn hook: the failure of the call (none ran), else the rules' answer, the first write the session
/// could not make (a budget) and the version after it.</summary>
public sealed record SessionAiResult(GameSessionFailure Failure, GameSessionVersion Version)
{
    public AiPassResult? Pass { get; init; }
    public AiSubturnHookResult? Hook { get; init; }
    public GameSessionFailure WriteFailure { get; init; }
    /// <summary>The subturn's end (<see cref="GameSessionOwner.EndCombatTurn"/>) when the hook ended it: the next participant's
    /// subturn start or the round's end are among its events, for the host to run their hooks.</summary>
    public SessionCombatResult? Round { get; init; }
}

public sealed partial class GameSessionOwner
{
    // The gate's check bits, which a map change drops from records that hold nothing else.
    private const DangerFlags CheckBits = DangerFlags.CheckWield | DangerFlags.CheckWeapon;

    /// <summary>
    /// Schedules the NPC's AI tick on the actor's map (0x4AD790, 0x4AD6E0), replacing its pending one: a wake-up 1 ms from now with
    /// the marker, an immediate tick 1 ms from now without it, a delayed tick after <see cref="AiSchedulerRules.TickDelayMs"/>. One revision. Refused during an event pass
    /// (a tick reschedules itself) and when the queue is full.
    /// </summary>
    [OriginalFunction(0x4AD6E0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4AD790, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult ScheduleAiTick(GameSessionVersion expected, SessionAiTickRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (!SessionEventKinds.AiTickNpc(request.Entity) || !Enum.IsDefined(request.Start))
            return Reject(GameSessionFailure.InvalidEvents);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_passWorld is not null) return Reject(GameSessionFailure.InvalidEventPass);
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var wakeUp = request.Start == SessionAiTickStart.WakeUp;
        var delay = request.Start == SessionAiTickStart.Delayed ? AiSchedulerRules.TickDelayMs(request.DistanceToPc) : 1;
        if (!AiTickFits(request.Entity, now, delay)) return Reject(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        ScheduleTick(_actor.Map.RuntimeMap, request.Entity, now, delay, wakeUp);
        return Changed();
    }

    /// <summary>The NPC's pending AI tick: its deadline and wake-up marker, or null.</summary>
    public (long DeadlineMs, bool WakeUp)? QueryAiTick(string entity)
    {
        ThrowIfDisposed();
        var pending = _events.QueryPending(SessionEventKinds.AiTick).FirstOrDefault(e => SessionEventKinds.NamesNpc(e, entity));
        return pending is null ? null : (pending.DeadlineMs, pending.Payload.Values[2] == 1);
    }

    /// <summary>
    /// One AI pass of the NPC on the actor's map (<see cref="AiSchedulerRules.Pass"/>), as an attack, a follower order or the host
    /// asks for it. The host's stages may call the owner; each write the gate makes is its own revision.
    /// </summary>
    public SessionAiResult RunAiPass(GameSessionVersion expected, string entity, ISessionAiWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (!SessionValidation.Entity(entity) || entity == AiSchedulerRules.Pc) return new(GameSessionFailure.InvalidDangerTarget, Version);
        if (!IsCurrent(expected)) return new(GameSessionFailure.StaleVersion, Version);
        var adapter = new SessionAiAdapter(this, world);
        var pass = AiSchedulerRules.Pass(adapter, entity, AiRolls, _rules.Combat);
        return new(GameSessionFailure.None, Version) { Pass = pass, WriteFailure = adapter.WriteFailure };
    }

    /// <summary>
    /// The subturn hook of a round (<see cref="AiSchedulerRules.SubturnHook"/>): the host runs it for the participant at the start of
    /// its subturn (<see cref="SessionCombatEventKind.SubturnStarted"/>) and after each animation it finishes; a non-PC actor's
    /// heartbeat and pass run, and with no animation left its subturn ends (<see cref="EndCombatTurn"/>).
    /// </summary>
    public SessionAiResult RunAiSubturnHook(GameSessionVersion expected, SessionCombatant critter, ISessionAiWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(critter);
        ArgumentNullException.ThrowIfNull(world);
        if (!ValidCombatant(critter)) return new(GameSessionFailure.InvalidCombatant, Version);
        if (!IsCurrent(expected)) return new(GameSessionFailure.StaleVersion, Version);
        var adapter = new SessionAiAdapter(this, world);
        var hook = AiSchedulerRules.SubturnHook(adapter, critter.Entity ?? AiSchedulerRules.Pc, AiRolls, _rules.Combat);
        return new(GameSessionFailure.None, Version) { Pass = hook.Pass, Hook = hook, WriteFailure = adapter.WriteFailure, Round = adapter.Round };
    }

    // The tick handler: the rules run through the pass's host when it is an AI host, and the tick they name is scheduled. Another
    // host consumes the tick without running it, as deferred calls are.
    internal GameEventHandlerOutcome FireAiTick(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0];
        var wakeUp = firing.Event.Payload.Values[2] == 1;
        if (_passWorld is not ISessionAiWorld world || entity.Map != _actor.Map.RuntimeMap)
        {
            _passAiTicks?.Add(new(firing.Event, entity.Code, wakeUp, null));
            return GameEventHandlerOutcome.Refused;
        }
        var adapter = new SessionAiAdapter(this, world);
        var tick = AiSchedulerRules.Tick(adapter, entity.Code, wakeUp, AiRolls, _rules.Combat);
        var failure = adapter.WriteFailure;
        if (tick.Next != AiTickNext.None)
        {
            // The NPC may have left the map in its pass (a teleport): its tick stays with the map it fired on, which a map change
            // clears. A delay beyond the bound or a full queue loses the tick; the next fold wakes the NPC again.
            var delay = Math.Max(tick.DelayMs, 1);
            // A spawner delay beyond the bound counts as a budget failure too.
            var refused = delay > SessionEventKinds.MaxAiTickDelayMs || !AiTickFits(entity.Code, firing.NowMs, delay) ? GameSessionFailure.EventBudgetExceeded
                : _revision == long.MaxValue ? GameSessionFailure.RevisionExhausted : GameSessionFailure.None;
            if (refused == GameSessionFailure.None)
            {
                ScheduleTick(entity.Map, entity.Code, firing.NowMs, delay, false);
                Revise();
            }
            else if (failure == GameSessionFailure.None) failure = refused;
        }
        _passAiTicks?.Add(new(firing.Event, entity.Code, wakeUp, tick) { WriteFailure = failure });
        return GameEventHandlerOutcome.Completed;
    }

    private bool AiTickFits(string entity, long now, long delay)
    {
        var replaces = _events.QueryPending(SessionEventKinds.AiTick).Any(e => SessionEventKinds.NamesNpc(e, entity));
        return replaces || (_events.Count < _events.Limits.MaxPending && _events.QueryPending(SessionEventKinds.AiTick).Length < SessionEventKinds.MaxAiTicks
            && _events.NextSequence < long.MaxValue && now <= long.MaxValue - delay);
    }

    // The replacing schedule (0x4AD730, its removal 0x4AD7D0 and the immediate insert of 0x4AD790).
    [OriginalFunction(0x4AD730, OriginalFunctionScope.Partial)]
    private void ScheduleTick(int map, string entity, long now, long delay, bool wakeUp)
    {
        // Replace: a tick of the same NPC on another map (none while ticks keep to the actor's map) is removed first.
        _events.RemoveAll(SessionEventKinds.AiTick, e => SessionEventKinds.NamesNpc(e, entity) && e.ScopeMap != map);
        var scheduled = _events.ScheduleAfter(SessionEventKinds.AiTick,
            GameEventPayload.Of([new(map, entity)], now, delay, wakeUp ? 1 : 0), delay, EventClocks(now));
        if (scheduled.Disposition is not (GameEventScheduleDisposition.Added or GameEventScheduleDisposition.Replaced))
            throw new InvalidOperationException("AI tick scheduling failed after admission.");
    }

    // ---- the scheduler's world over the session ----

    private sealed class SessionAiAdapter(GameSessionOwner owner, ISessionAiWorld host) : IAiSchedulerWorld
    {
        /// <summary>The first write the session refused.</summary>
        internal GameSessionFailure WriteFailure { get; private set; }

        private void Fail(GameSessionFailure failure)
        {
            if (failure != GameSessionFailure.None && WriteFailure == GameSessionFailure.None) WriteFailure = failure;
        }

        private static string Id(SessionCombatant combatant) => combatant.Entity ?? AiSchedulerRules.Pc;

        // The NPC's life state and facts: the session's record over the host's loaded state; null when it is gone.
        private (LifeState State, LifeFacts Facts, SessionLifeTarget Target)? Life(string id)
        {
            if (id == AiSchedulerRules.Pc)
            {
                if (owner._hero is null || owner._life.Hero is not { } hero) return null;
                return (hero.State, owner.HeroLifeFacts(hero.State), SessionLifeTarget.Hero);
            }
            if (host.AiLifeTarget(id) is not { Facts: { } facts } target || target.IsHero || target.Entity != id) return null;
            // ARC-DEV-302: with the hold flags the spells wrote (stone gates the NPC's whole AI turn).
            return (owner._life.Critters.TryGetValue(id, out var record) ? record.State : target.Loaded, owner.WithWrittenHolds(id, facts), target);
        }

        public bool IsDead(string id) => Life(id) is not { } life || LifeRules.IsDead(life.State, life.Facts);
        public bool IsUnconscious(string npc) => Life(npc) is { } life && LifeRules.IsUnconscious(life.State, life.Facts);
        public bool IsStoned(string npc) => Life(npc) is { } life && life.Facts.Traits.HasFlag(LifeTraits.Stoned);
        public bool InRound => owner._combat.Round is not null;
        public string? RoundActor => owner._combat.Round is { } round ? Id(round.Actor) : null;

        public bool InCombatMode(string id) =>
            id == AiSchedulerRules.Pc ? owner._hero is not null && owner._combat.HeroCombatMode : owner._combat.Critters.Contains(id);

        public (DangerState State, string? Source) Danger(string npc)
        {
            var record = owner._danger.Get(npc);
            return (record.State, record.Source is { } source ? Id(source) : null);
        }

        public DangerFlags DangerBits(string npc) => owner._danger.Get(npc).Flags;

        public void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear)
        {
            var plan = new DangerPlan(owner);
            plan.Update(npc, record => record with { Flags = (record.Flags | set) & ~clear });
            Fail(plan.Commit(new GameSessionMutationResult(GameSessionDisposition.NoChange, GameSessionFailure.None, owner.Version),
                (r, disposition, version) => r with { Disposition = disposition, Version = version }, owner.Reject).Failure);
        }

        public AiCritterFlags CritterFlags(string npc) => new(Life(npc)?.State.Conditions ?? LifeConditions.None,
            owner._danger.Get(npc).Flags & AiCritterFlags.CritterBits, owner._combat.Critters.Contains(npc), owner._combat.Concealed.Contains(npc));

        public void WriteCritterFlags(string npc, AiCritterFlags flags) => Fail(owner.WriteAiCritterFlags(npc, flags, Life(npc)));

        /// <summary>The subturn's end the hook made.</summary>
        internal SessionCombatResult? Round { get; private set; }

        public void EndSubturn(string npc)
        {
            Round = owner.EndCombatTurn(owner.Version, npc == AiSchedulerRules.Pc ? SessionCombatant.Hero : SessionCombatant.Critter(npc), host);
            Fail(Round.Failure);
        }

        // The host answers the NPC flag waiting here; a waiting standing of the session's party also counts (OR, never replaced).
        public AiCatchUpFacts CatchUp(string npc)
        {
            var facts = host.CatchUp(npc);
            return owner._party.Records.TryGetValue(npc, out var companion)
                ? facts with { Waiting = facts.Waiting || companion.Standing == CompanionStanding.Waiting }
                : facts;
        }

        // The host's part.
        public long DistanceToPc(string npc) => host.DistanceToPc(npc);
        public bool RunScript(AiScriptPoint point, string npc, string? triggerer) => host.RunScript(point, npc, triggerer);
        public bool RunWaypoints(string npc) => host.RunWaypoints(npc);
        public void RunStandpoints(string npc) => host.RunStandpoints(npc);
        public bool IsGenerator(string npc) => host.IsGenerator(npc);
        public long? Spawn(string npc) => host.Spawn(npc);
        public bool IsEncounterCreature(string npc) => host.IsEncounterCreature(npc);
        public void Destroy(string npc) => host.Destroy(npc);
        public void InterruptAnimations(string npc) => host.InterruptAnimations(npc);
        public bool SectorLoaded(string npc) => host.SectorLoaded(npc);
        public void MoveToLeader(string npc) => host.MoveToLeader(npc);
        public bool MapClearing => host.MapClearing;
        public bool Sleeping(string npc) => host.Sleeping(npc);
        public bool Hidden(string npc) => host.Hidden(npc);
        public bool PcHidden => host.PcHidden;
        public AiConversation Conversation(string npc) => host.Conversation(npc);
        public bool ConversationCallback => host.ConversationCallback;
        public void EndConversation(string npc) => host.EndConversation(npc);
        public int AnimationPriority(string npc) => host.AnimationPriority(npc);
        public bool StunAnimationRunning(string npc) => host.StunAnimationRunning(npc);
        public bool AnimationRunning(string npc) => host.AnimationRunning(npc);
        public void ReWield(string npc, bool allSlots) => host.ReWield(npc, allSlots);
        public string? Leader(string npc) => host.Leader(npc);
        public void EndMaintainedSpells(string npc) => host.EndMaintainedSpells(npc);
        public bool Heal(string npc) => host.Heal(npc);
        public bool SearchItems(string npc) => host.SearchItems(npc);
        public void UpdateDanger(string npc) => host.UpdateDanger(npc);
        public void Perform(string npc) => host.Perform(npc);
        public bool Loading => host.Loading;
    }

    // The gate's critter-flags write (CL5) in one revision: the life conditions, the danger bits of the word, combat mode and
    // concealment. Concealment is held only for the hero's followers, so a set bit for another NPC is not stored.
    private GameSessionFailure WriteAiCritterFlags(string npc, AiCritterFlags flags, (LifeState State, LifeFacts Facts, SessionLifeTarget Target)? life)
    {
        var recorded = _life.Critters.TryGetValue(npc, out var record);
        var lifeChanges = life is { } l && l.State.Conditions != flags.Conditions;
        var danger = _danger.Get(npc);
        var bits = (danger.Flags & ~AiCritterFlags.CritterBits) | (flags.Danger & AiCritterFlags.CritterBits);
        var combat = _combat.Critters.Contains(npc) != flags.CombatMode;
        var conceal = _combat.Concealed.Contains(npc) != flags.Concealed && (!flags.Concealed || _party.Order.Contains(npc));
        if (!lifeChanges && bits == danger.Flags && !combat && !conceal) return GameSessionFailure.None;
        if (lifeChanges && !recorded && _life.Critters.Count >= _limits.MaxLifeRecords) return GameSessionFailure.BudgetExceeded;
        var next = danger with { Flags = bits };
        if (bits != danger.Flags && next.IsBaseline != danger.IsBaseline && !next.IsBaseline && _danger.Records.Count >= _limits.MaxDangerRecords)
            return GameSessionFailure.BudgetExceeded;
        if (combat && flags.CombatMode && _combat.Critters.Count >= SessionCombatModeLimits.MaxCritters) return GameSessionFailure.BudgetExceeded;
        if (_revision == long.MaxValue) return GameSessionFailure.RevisionExhausted;
        if (lifeChanges)
            _life.Critters[npc] = new(life!.Value.State with { Conditions = flags.Conditions }, recorded ? record.DeathTimeMs : null);
        if (bits != danger.Flags) _danger.Put(next);
        if (combat && flags.CombatMode) _combat.Critters.Add(npc);
        else if (combat) _combat.Critters.Remove(npc);
        if (conceal && flags.Concealed) _combat.Concealed.Add(npc);
        else if (conceal) _combat.Concealed.Remove(npc);
        Revise();
        return GameSessionFailure.None;
    }
}

internal sealed class AiTickHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireAiTick(firing) ?? GameEventHandlerOutcome.Refused;
}
