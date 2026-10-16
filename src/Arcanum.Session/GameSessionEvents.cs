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

/// <summary>One timer that ran in a pass and the life step it made (a refused step is reported, the event is consumed).</summary>
public sealed record SessionLifeFiring(GameEvent Event, SessionLifeResult Life);

/// <summary>
/// A session event pass: the queue's outcomes (<see cref="GameEventPassResult"/>), the life steps of the timers that
/// ran, and the session version after the pass. <see cref="Pass"/> is null for a refused pass.
/// </summary>
public sealed record SessionEventPassResult(GameSessionFailure Failure, GameEventPassResult? Pass,
    IReadOnlyList<SessionLifeFiring> Life, GameSessionVersion Version);

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
/// sub-kind (the source keeps one too) with the game time it was scheduled at as its only value. Hero kinds name no
/// entity and run on any map; NPC kinds name the NPC on its map and run while that map is active.
/// </summary>
public static class SessionEventKinds
{
    public const int RegistryVersion = 1;

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

    internal static GameEventRegistry Build(SessionEventSink owner)
    {
        GameEventKindDefinition Hero(string name, LifeTimer timer) => new(name, GameEventDomain.Game, GameEventPersistence.Saved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.Global, 0, 1, 0, 0, new LifeTimerHandler(owner, timer),
            Validate: ScheduledAt);
        GameEventKindDefinition Npc(string name, LifeTimer timer) => new(name, GameEventDomain.Game, GameEventPersistence.Saved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.EntityMap, 1, 1, 1, 0, new LifeTimerHandler(owner, timer),
            Validate: ScheduledAt);
        return new(RegistryVersion,
        [
            Hero(HeroFatigue, LifeTimer.Fatigue), Hero(HeroPoisonDamage, LifeTimer.PoisonDamage),
            Hero(HeroPoisonRecovery, LifeTimer.PoisonRecovery),
            Npc(NpcFatigue, LifeTimer.Fatigue), Npc(NpcPoisonDamage, LifeTimer.PoisonDamage),
            Npc(NpcPoisonRecovery, LifeTimer.PoisonRecovery), Npc(NpcResting, LifeTimer.Resting),
        ]);
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

    // The scheduling time is a game time no later than the deadline; the queue checks the deadline itself.
    private static bool ScheduledAt(GameEventPayload payload) => payload.Values[0] >= 0;
}

internal enum LifeTimer { Fatigue, PoisonDamage, PoisonRecovery, Resting }

// The registry is built before its owner (a restore checks the saved events first); the owner fills this in when built.
internal sealed class SessionEventSink
{
    internal GameSessionOwner? Owner { get; set; }
}

internal sealed class LifeTimerHandler(SessionEventSink sink, LifeTimer timer) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireLifeTimer(timer, firing) ?? GameEventHandlerOutcome.Refused;
}
