using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// What the host knows during an event pass (ARC-WORLD-024): the life facts of an NPC that is present on its map, or
/// null when the entity no longer exists there (an event that names it is then dropped without running).
/// </summary>
public interface ISessionEventWorld
{
    LifeFacts? CritterFacts(SessionMapId map, string entity);
}

/// <summary>An object a deferred script call names (ARC-SCRIPT-010): the runtime map and the session entity id.</summary>
public readonly record struct SessionScriptRole(SessionMapId Map, string Entity);

/// <summary>A deferred SCR call (call script in): the script, the line it starts at and the stored roles, either absent.</summary>
public sealed record SessionScriptCall(int Script, int Line, SessionScriptRole? Triggerer, SessionScriptRole? Attachee);

/// <summary>
/// A host that can run deferred SCR calls during an event pass (ARC-SCRIPT-010). A pass given only an
/// <see cref="ISessionEventWorld"/> consumes due calls without running them and reports them as not run.
/// </summary>
public interface ISessionScriptWorld : ISessionEventWorld
{
    /// <summary>
    /// Whether an object a call names still exists (an NPC with a life record is asked through
    /// <see cref="ISessionEventWorld.CritterFacts"/> instead); a call naming a missing object is dropped. The map is the
    /// object's map: when the actor changes map, a call's <see cref="SessionEventKinds.PlayerEntity"/> and follower
    /// roles move with it (the original keeps their handles across maps) and other roles keep the map they had.
    /// </summary>
    bool ObjectExists(SessionMapId map, string entity);
    /// <summary>Runs the call through the host's SCR dispatcher and applies its effects; false when it could not.</summary>
    bool RunScriptCall(SessionScriptCall call);
}

/// <summary>A deferred call that came due in a pass, and whether the host ran it.</summary>
public sealed record SessionScriptCallFiring(GameEvent Event, SessionScriptCall Call, bool Ran);

/// <summary>A pending deferred call: its queue sequence, deadline and call.</summary>
public sealed record SessionPendingScriptCall(long Sequence, long DeadlineMs, SessionScriptCall Call);

/// <summary>One timer that ran in a pass and the life step it made (a refused step is reported, the event is consumed).</summary>
public sealed record SessionLifeFiring(GameEvent Event, SessionLifeResult Life);

/// <summary>
/// A session event pass: the queue's outcomes (<see cref="GameEventPassResult"/>), the life steps of the timers that
/// ran, and the session version after the pass. <see cref="Pass"/> is null for a refused pass.
/// </summary>
public sealed record SessionEventPassResult(GameSessionFailure Failure, GameEventPassResult? Pass,
    IReadOnlyList<SessionLifeFiring> Life, GameSessionVersion Version)
{
    /// <summary>The deferred SCR calls that came due (ARC-SCRIPT-010), in firing order.</summary>
    public IReadOnlyList<SessionScriptCallFiring> ScriptCalls { get; init; } = Array.Empty<SessionScriptCallFiring>();
    /// <summary>The timed effects' ENDs that came due (ARC-WORLD-029), in firing order.</summary>
    public IReadOnlyList<SessionEffectFiring> Effects { get; init; } = Array.Empty<SessionEffectFiring>();
    /// <summary>The Lock events that came due (ARC-WORLD-050), in firing order.</summary>
    public IReadOnlyList<SessionLockFiring> Locks { get; init; } = Array.Empty<SessionLockFiring>();
}

/// <summary>One saved pending event: the queue record without its derived domain and scope map.</summary>
public sealed record SessionPendingEvent(long Sequence, string Kind, long DeadlineMs, ImmutableArray<GameEventEntity> Entities,
    ImmutableArray<long> Values);

/// <summary>Saved event queue (snapshot schema 9): the kind registry version, the next sequence number and the pending
/// events in queue order.</summary>
public sealed class SessionEventState
{
    internal SessionEventState(int registry, long nextSequence, SessionPendingEvent[] pending)
    {
        Registry = registry;
        NextSequence = nextSequence;
        Pending = Array.AsReadOnly(pending);
    }

    public int Registry { get; }
    public long NextSequence { get; }
    public IReadOnlyList<SessionPendingEvent> Pending { get; }
}

/// <summary>
/// The session's event kinds (registry version 1): the life-state timers of ARC-COMBAT-002, each one per critter and
/// sub-kind (the source keeps one too) with the game time it was scheduled at as its first value. The poison recovery
/// kinds carry a second value, the number of source recovery events the timer stands for: 1 when a poison write set it,
/// 2 when the recovery tick itself did (anomaly TE10; the Original rule mode applies that many ticks). Hero kinds name no
/// entity and run on any map; NPC kinds name the NPC on its map and run while that map is active.
/// </summary>
public static class SessionEventKinds
{
    /// <summary>
    /// 1. ARC-SCRIPT-010 added <see cref="ScriptCall"/>, ARC-WORLD-029 <see cref="HeroEffectEnd"/> and ARC-WORLD-050
    /// <see cref="LockReset"/> without a new version:
    /// the life kinds are unchanged, and a build without a kind refuses a save holding one as an unknown kind.
    /// </summary>
    public const int RegistryVersion = 1;

    /// <summary>
    /// The END of a hero's timed effect (ARC-WORLD-029): global, any number. No entities; values: the game time it was set at,
    /// its duration, the effect number, the count of entries it removes.
    /// </summary>
    public const string HeroEffectEnd = "effect.hero.end";

    /// <summary>
    /// The original's Lock event (kind 21; ARC-WORLD-050, ledger LK3–LK5): an hour after an unlock of a locked object or a
    /// day after a jam it locks, unjams and closes the portal or container it names. Scoped to the object's map, any number
    /// per object (each unlock or jam adds one). Entity: the object; value: the game time it was scheduled at.
    /// </summary>
    public const string LockReset = "lock.reset";

    /// <summary>
    /// A deferred SCR call (ARC-SCRIPT-010): global, any number per key. Entities: the triggerer and the attachee (the
    /// player as <see cref="PlayerEntity"/>; player and follower roles follow the actor across maps), an absent role
    /// written as <see cref="AbsentRole"/> on the map that was active when it was scheduled; values: the game time it was
    /// scheduled at, its delay, the script, the line.
    /// </summary>
    public const string ScriptCall = "scr.call";
    /// <summary>The entity id standing for an absent role of a <see cref="ScriptCall"/>.</summary>
    public const string AbsentRole = "-";
    /// <summary>The entity id of the player character in a <see cref="ScriptCall"/>; it travels with the actor.</summary>
    public const string PlayerEntity = "pc";
    /// <summary>Pending deferred calls a session holds.</summary>
    public const int MaxScriptCalls = 1024;
    /// <summary>The longest delay: a call-in of int.MaxValue seconds at the game rate (8 game ms per real ms).</summary>
    public const long MaxScriptCallDelayMs = int.MaxValue * 8000L;

    public const string HeroFatigue = "life.hero.fatigue";
    public const string HeroPoisonDamage = "life.hero.poison-damage";
    public const string HeroPoisonRecovery = "life.hero.poison-recovery";
    public const string NpcFatigue = "life.npc.fatigue";
    public const string NpcPoisonDamage = "life.npc.poison-damage";
    public const string NpcPoisonRecovery = "life.npc.poison-recovery";
    public const string NpcResting = "life.npc.resting";

    /// <summary>Fatigue recovery: 80 game seconds (10 real seconds) after fatigue damage is set.</summary>
    public const long FatigueRecoveryDelayMs = 80_000;
    /// <summary>Poison damage: every 15 game seconds while poisoned.</summary>
    public const long PoisonDamageDelayMs = 15_000;
    /// <summary>Poison recovery: every 120 game seconds while poisoned.</summary>
    public const long PoisonRecoveryDelayMs = 120_000;
    /// <summary>Resting: one game hour after hit-point damage is set.</summary>
    public const long RestingDelayMs = 3_600_000;

    /// <summary>Pending events a session holds: four timers for each NPC life record and three for the hero, so a life step
    /// can always set its timers, the deferred calls, the timed effects' ENDs and the Lock events.</summary>
    public const int MaxPending = 4 * GameSessionLimits.PolicyMaxLifeRecords + 3 + MaxScriptCalls + SessionEffectLimits.MaxTimedEffects
        + MaxLockResets;
    /// <summary>Pending Lock events a session holds (project policy; the original has no bound).</summary>
    public const int MaxLockResets = 1024;

    internal static GameEventQueueLimits Limits { get; } = new(MaxPending: MaxPending);

    internal static GameEventRegistry Build(SessionEventSink owner)
    {
        GameEventKindDefinition Hero(string name, LifeTimer timer) => new(name, GameEventDomain.Game, GameEventPersistence.Saved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.Global, 0, Values(timer), 0, 0, new LifeTimerHandler(owner, timer),
            Validate: ScheduledAt);
        GameEventKindDefinition Npc(string name, LifeTimer timer) => new(name, GameEventDomain.Game, GameEventPersistence.Saved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.EntityMap, 1, Values(timer), 1, 0, new LifeTimerHandler(owner, timer),
            Validate: ScheduledAt);
        return new(RegistryVersion,
        [
            Hero(HeroFatigue, LifeTimer.Fatigue), Hero(HeroPoisonDamage, LifeTimer.PoisonDamage),
            Hero(HeroPoisonRecovery, LifeTimer.PoisonRecovery),
            Npc(NpcFatigue, LifeTimer.Fatigue), Npc(NpcPoisonDamage, LifeTimer.PoisonDamage),
            Npc(NpcPoisonRecovery, LifeTimer.PoisonRecovery), Npc(NpcResting, LifeTimer.Resting),
            new(ScriptCall, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.Global, 2, 4, 0, 0,
                new ScriptCallHandler(owner), Validate: ScriptCallPayload),
            new(HeroEffectEnd, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.Global, 0, 4, 0, 0,
                new EffectEndHandler(owner), Validate: EffectEndPayload),
            new(LockReset, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.EntityMap, 1, 1, 0, 0,
                new LockResetHandler(owner), Validate: payload => payload.Values[0] >= 0),
        ]);
    }

    // Set at a game time, a duration of 1 ms up to the longest, a number of the effect tables and a count the hero's list holds.
    private static bool EffectEndPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is >= 1 and <= SessionEffectLimits.MaxDurationMs &&
        payload.Values[2] is >= SessionEffectLimits.FirstNumber and <= SessionEffectLimits.LastNumber &&
        payload.Values[3] is >= 1 and <= SessionEffectLimits.MaxHeroEffects;

    // Scheduled at a game time, a delay of 1 ms up to the longest, any script and line number (a missing script or line
    // ends the run when it fires, as in the original).
    private static bool ScriptCallPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is >= 1 and <= MaxScriptCallDelayMs &&
        payload.Values[2] is >= int.MinValue and <= int.MaxValue && payload.Values[3] is >= int.MinValue and <= int.MaxValue;

    internal static GameEventPayload Payload(SessionScriptCall call, long scheduledAt, long delayMs, int currentMap)
    {
        GameEventEntity Role(SessionScriptRole? role) => role is { } r ? new(r.Map.RuntimeMap, r.Entity) : new(currentMap, AbsentRole);
        return GameEventPayload.Of([Role(call.Triggerer), Role(call.Attachee)], scheduledAt, delayMs, call.Script, call.Line);
    }

    internal static SessionScriptCall Call(GameEventPayload payload)
    {
        SessionScriptRole? Role(GameEventEntity e) => e.Code == AbsentRole ? null : new(new(e.Map), e.Code);
        return new((int)payload.Values[2], (int)payload.Values[3], Role(payload.Entities[0]), Role(payload.Entities[1]));
    }

    internal static string Kind(LifeTimer timer, bool hero) => (timer, hero) switch
    {
        (LifeTimer.Fatigue, true) => HeroFatigue,
        (LifeTimer.PoisonDamage, true) => HeroPoisonDamage,
        (LifeTimer.PoisonRecovery, true) => HeroPoisonRecovery,
        (LifeTimer.Fatigue, false) => NpcFatigue,
        (LifeTimer.PoisonDamage, false) => NpcPoisonDamage,
        (LifeTimer.PoisonRecovery, false) => NpcPoisonRecovery,
        (LifeTimer.Resting, false) => NpcResting,
        _ => throw new ArgumentOutOfRangeException(nameof(timer)),
    };

    internal static long Delay(LifeTimer timer) => timer switch
    {
        LifeTimer.Fatigue => FatigueRecoveryDelayMs,
        LifeTimer.PoisonDamage => PoisonDamageDelayMs,
        LifeTimer.PoisonRecovery => PoisonRecoveryDelayMs,
        LifeTimer.Resting => RestingDelayMs,
        _ => throw new ArgumentOutOfRangeException(nameof(timer)),
    };

    internal static int Values(LifeTimer timer) => timer == LifeTimer.PoisonRecovery ? 2 : 1;

    // The scheduling time is a game time; a recovery timer stands for one or two source events.
    private static bool ScheduledAt(GameEventPayload payload) => payload.Values[0] >= 0 && (payload.Values.Length == 1 || payload.Values[1] is 1 or 2);
}

internal enum LifeTimer { Fatigue, PoisonDamage, PoisonRecovery, Resting }

// The registry is built before its owner (a restore checks the saved events first); the owner fills this in when built.
internal sealed class SessionEventSink
{
    internal GameSessionOwner? Owner { get; set; }
}

internal sealed class ScriptCallHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireScriptCall(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class EffectEndHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireEffectEnd(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class LifeTimerHandler(SessionEventSink sink, LifeTimer timer) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireLifeTimer(timer, firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class LockResetHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireLockReset(firing) ?? GameEventHandlerOutcome.Refused;
}
