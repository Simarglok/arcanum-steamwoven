using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;

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
    /// <summary>The wait timers that came due (ARC-WORLD-045), in firing order.</summary>
    public IReadOnlyList<SessionWaitFiring> Waits { get; init; } = Array.Empty<SessionWaitFiring>();
    /// <summary>The respawning scenery switched on again (ARC-COMBAT-008), in firing order.</summary>
    public IReadOnlyList<SessionRespawnFiring> Respawns { get; init; } = Array.Empty<SessionRespawnFiring>();
    /// <summary>The magic-tech events that came due (ARC-MAGIC-010), in firing order.</summary>
    public IReadOnlyList<SessionSpellRunFiring> SpellRuns { get; init; } = Array.Empty<SessionSpellRunFiring>();
    /// <summary>The Item Decay events that came due (ARC-WORLD-062), in firing order.</summary>
    public IReadOnlyList<SessionDecayFiring> Decays { get; init; } = Array.Empty<SessionDecayFiring>();
    /// <summary>The Decay Dead Bodies events that came due (ARC-WORLD-1158), in firing order.</summary>
    public IReadOnlyList<SessionBodyDecayFiring> BodyDecays { get; init; } = Array.Empty<SessionBodyDecayFiring>();
    /// <summary>MIG12 S8: the real-time steps that came due (the world-map journey's travel step, the turn-based event), in firing order; the
    /// frame step runs their hosts after the pass.</summary>
    public IReadOnlyList<SessionRealStepFiring> RealSteps { get; init; } = Array.Empty<SessionRealStepFiring>();
    /// <summary>ARC-DEV-132 (MIG13 S3): the goal steps that came due on the animation list, in firing order; the frame step hands them to its goal-step host after the pass.</summary>
    public IReadOnlyList<SessionGoalStepFiring> GoalSteps { get; init; } = Array.Empty<SessionGoalStepFiring>();
    /// <summary>ARC-DEV-132: the runs the check before the pass killed because their step went missing or left the active map (their end nodes are S5's).</summary>
    public IReadOnlyList<GoalRunEnd> GoalsReconciled { get; init; } = Array.Empty<GoalRunEnd>();
    /// <summary>The AI ticks that came due (ARC-WORLD-084), in firing order.</summary>
    public IReadOnlyList<SessionAiTickFiring> AiTicks { get; init; } = Array.Empty<SessionAiTickFiring>();
    /// <summary>The restock events that came due (ARC-WORLD-094), in firing order.</summary>
    public IReadOnlyList<SessionRestockFiring> Restocks { get; init; } = Array.Empty<SessionRestockFiring>();
    /// <summary>ARC-WORLD-124: the trap device fuses that came due (kind 28, ledger TP3), in firing order; the cast host runs their spells after the pass.</summary>
    public IReadOnlyList<SessionTrapDeviceFiring> TrapDevices { get; init; } = Array.Empty<SessionTrapDeviceFiring>();
    /// <summary>ARC-DEV-104: the first ticks of sector folds that a touch inside the pass queued (<see cref="GameSessionOwner.TouchSectors"/>),
    /// scheduled when the pass ended, and those the queue could not take.</summary>
    public int DeferredFoldTicks { get; init; }
    public int DeferredFoldTicksRefused { get; init; }
}

/// <summary>An Item Decay event that came due (ARC-WORLD-062, ledger ID3): the item, its map and what the rules say the event does.
/// <see cref="Postponed"/>: a deletion the session could not make yet (the item's map seed not attached, or the deleted-item budget
/// full) was scheduled again a minute on instead.</summary>
public sealed record SessionDecayFiring(GameEvent Event, SessionMapId Map, string Entity, Arcanum.Simulation.Items.ItemDecayStep Step)
{
    public bool Postponed { get; init; }
}

/// <summary>
/// ARC-WORLD-062: a host that knows whether the loot window shows a container (the original's counter 0x5E8800, lowered when the
/// inventory window opens on a container in its loot mode or its second container mode, raised when it closes). An Item Decay event that fires meanwhile
/// waits a minute (ledger ID3, ID6). A pass whose host does not implement it runs as if no container were shown.
/// </summary>
public interface ISessionLootWindowWorld : ISessionEventWorld
{
    bool LootWindowShowsContainer { get; }
}

/// <summary>ARC-WORLD-096: a pass world that wraps a host's event world (a spell host's, <see cref="SessionSpellCaster.ProcessEvents"/>):
/// the questions it does not answer itself (the loot window, the restock host) go to the wrapped world.</summary>
internal interface ISessionWrappingEventWorld : ISessionEventWorld
{
    ISessionEventWorld Wrapped { get; }
}

/// <summary>A respawn event that came due (ARC-COMBAT-008, ledger OD10): the scenery switched on again.</summary>
public sealed record SessionRespawnFiring(GameEvent Event, SessionMapId Map, string Entity);

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
    /// 1. ARC-SCRIPT-010 added <see cref="ScriptCall"/>, ARC-WORLD-029 <see cref="HeroEffectEnd"/>, ARC-WORLD-050
    /// <see cref="LockReset"/>, ARC-WORLD-045 <see cref="PartyWait"/>, ARC-COMBAT-008 <see cref="ObjectRespawn"/>, ARC-MAGIC-010
    /// <see cref="SpellRun"/> (saved with the settled runs since ARC-DEV-210, schema 73), ARC-WORLD-062 <see cref="ItemDecay"/>, ARC-WORLD-084 <see cref="AiTick"/>, ARC-WORLD-087
    /// <see cref="HeroEncumbrance"/>, ARC-WORLD-094 <see cref="Restock"/> and MIG12 S8 the real-time <see cref="WorldMapStep"/> and
    /// <see cref="TurnBasedStep"/> (not saved) and ARC-DEV-132 (MIG13 S3) the animation-domain <see cref="GoalStep"/> (not saved) without a new version:
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
    /// The original's wait timer (kind 17; ARC-WORLD-045, OL23–OL26): a follower told to wait stops waiting, jilted, when it
    /// comes due. Scoped to the NPC's map, one per NPC. Entity: the NPC; values: the game time it was scheduled at, its duration
    /// (0 up to 30 days; the queue stores 0 as 1 ms) and the leader's charisma then (read again from a hero when it fires).
    /// </summary>
    public const string PartyWait = "party.wait";
    /// <summary>Pending wait timers a session holds: one per recorded companion.</summary>
    public const int MaxWaitTimers = GameSessionLimits.PolicyMaxCompanions;

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
    /// <summary>
    /// The hero's encumbrance fatigue timer (ARC-WORLD-087; mode 1 of the original's fatigue timer, ledger WE5, WE8): global, one at a time
    /// (a new one is not added while one is pending, WEA2). No entities; value: the game time it was scheduled at. Its delay is the drain
    /// interval of the level (<see cref="Arcanum.Simulation.Items.CarryRules.DrainIntervalMs"/>).
    /// </summary>
    public const string HeroEncumbrance = "life.hero.encumbrance";

    /// <summary>Fatigue recovery: 80 game seconds (10 real seconds) after fatigue damage is set.</summary>
    public const long FatigueRecoveryDelayMs = 80_000;
    /// <summary>Poison damage: every 15 game seconds while poisoned.</summary>
    public const long PoisonDamageDelayMs = 15_000;
    /// <summary>Poison recovery: every 120 game seconds while poisoned.</summary>
    public const long PoisonRecoveryDelayMs = 120_000;
    /// <summary>Resting: one game hour after hit-point damage is set.</summary>
    public const long RestingDelayMs = 3_600_000;

    /// <summary>Pending events a session holds: four timers for each NPC life record and four for the hero (ARC-WORLD-087: its encumbrance timer), so a life step
    /// can always set its timers, the deferred calls, the timed effects' ENDs, the Lock events, the wait timers, the respawns, the
    /// magic-tech events, the Item Decay events, the AI ticks, (ARC-WORLD-094) the restock events, the real-time steps and (ARC-DEV-132) one goal step per slot.</summary>
    public const int MaxPending = 4 * GameSessionLimits.PolicyMaxLifeRecords + 4 + MaxScriptCalls + SessionEffectLimits.MaxTimedEffects
        + MaxLockResets + MaxWaitTimers + MaxObjectRespawns + MaxSpellRunEvents + MaxItemDecays + MaxBodyDecays + MaxRandomEncounters + MaxAiTicks + MaxRestocks + MaxTrapDevices + MaxRealSteps + MaxGoalSteps;

    /// <summary>
    /// The original's AI event (kind 10, game time, never saved by the original; ARC-WORLD-084, ledger CL7, CL8): one NPC's AI tick
    /// (<see cref="Arcanum.Simulation.Ai.AiSchedulerRules.Tick"/>). Active map only, one per NPC (a new one replaces it). Saved
    /// unless the Original setting rebuilds it on load (OQ2: each becomes a wake-up tick 1 ms after the restored clock); a map
    /// change removes every tick, and the host's sector fold schedules the new map's. Entity: the NPC; values: the game time it was
    /// scheduled at, its delay (1 ms up to <see cref="MaxAiTickDelayMs"/>) and the wake-up marker (0 or 1).
    /// </summary>
    public const string AiTick = "ai.tick";
    /// <summary>Pending AI ticks a session holds (project policy; one per NPC of the active map).</summary>
    public const int MaxAiTicks = 8192;
    /// <summary>The longest AI tick delay: a spawner's, bounded as a deferred call's.</summary>
    public const long MaxAiTickDelayMs = MaxScriptCallDelayMs;
    /// <summary>Pending Lock events a session holds (project policy; the original has no bound).</summary>
    public const int MaxLockResets = 1024;

    /// <summary>
    /// The original's respawn event (kind 33; ARC-COMBAT-008, ledger OD8): a respawning scenery an attack broke is switched on again.
    /// Scoped to the object's map, one per break. Entity: the scenery; values: the game time it was scheduled at and its delay
    /// (<see cref="Arcanum.Simulation.Combat.ObjectDamageRules.RespawnDelayMs"/>).
    /// </summary>
    public const string ObjectRespawn = "object.respawn";
    /// <summary>Pending respawn events a session holds (project policy; the original has no bound).</summary>
    public const int MaxObjectRespawns = 1024;

    /// <summary>
    /// The original's Item Decay event (kind 25, saved, game time; ARC-WORLD-062, ledger ID1–ID9): an item dropped on the ground is
    /// destroyed two game days later unless it was picked up (<see cref="Arcanum.Simulation.Items.ItemDecayRules"/>). Scoped to the
    /// item's map, one per item (a new one replaces the item's earlier one). Entity: the item; values: the game time it was
    /// scheduled at and its delay (<see cref="Arcanum.Simulation.Items.ItemDecayRules.DelayMs"/>, or the retry's
    /// <see cref="Arcanum.Simulation.Items.ItemDecayRules.RetryDelayMs"/>).
    /// </summary>
    public const string ItemDecay = "item.decay";
    /// <summary>Pending Item Decay events a session holds (project policy; the original has no bound): a drop beyond it does not decay.</summary>
    public const int MaxItemDecays = 8192;
    /// <summary>
    /// The original's Decay Dead Bodies event (kind 24, saved, game time; ARC-WORLD-1158, ledger PO10): a dead NPC's body is removed one game day after its death (its inventory dropped, the critter destroyed;
    /// <see cref="Arcanum.Simulation.Combat.BodyDecayRules"/>). Scoped to the NPC's map, one per NPC (a new one replaces the earlier). Entity: the NPC; values: the game time it was scheduled at and its delay
    /// (<see cref="Arcanum.Simulation.Combat.BloodPoolRules.CritterDecayDelayMs"/>, or the retry's <see cref="Arcanum.Simulation.Items.ItemDecayRules.RetryDelayMs"/> of a body the session could not remove yet).
    /// Added without a new registry version, as the other kinds before it: a build without the kind refuses a save holding one.
    /// </summary>
    public const string BodyDecay = "body.decay";

    /// <summary>
    /// The original's Random Encounters event (kind 34, game time, saved; ARC-WORLD-1159, ledger PO17, TL29): the world journey's roll. It re-arms itself first, 300 to 700 game minutes on, then acts only on the outdoor map with the hero in
    /// no named area. Global, one in a running chain (the pending one stays when another is added). No entities; value: the game time it was scheduled at. Armed by the first event pass of a host that has the tables.
    /// </summary>
    public const string RandomEncounter = "encounter.random";
    /// <summary>Pending Random Encounters events a session holds: one (a chain).</summary>
    public const int MaxRandomEncounters = 1;
    /// <summary>Pending Decay Dead Bodies events a session holds: one per NPC life record.</summary>
    public const int MaxBodyDecays = GameSessionLimits.PolicyMaxLifeRecords;
    /// <summary>
    /// The original's restock event (type 0x16, saved, game time; ARC-WORLD-094, ledger RK1–RK8): 12 to 24 game hours after an inventory
    /// window showed an object with an inventory source, the object's inventory is cleared and generated again
    /// (<see cref="Arcanum.Simulation.Items.RestockRules"/>). Scoped to the object's map, one per object (a new one replaces the pending
    /// one). Entity: the NPC or container; values: the game time it was scheduled at and its delay (the rules' 43,200,000..86,400,000 ms,
    /// or the session's <see cref="RestockRetryDelayMs"/>).
    /// </summary>
    public const string Restock = "inventory.restock";

    /// <summary>
    /// The original's Traps event (kind 28, saved, game time; ARC-WORLD-124, ledger TP2, TP3): a placed trap device's fuse. 88,000 ms after the device was used its spell runs at the tile
    /// with no source and no caster and the marker made at the tile is destroyed. Scoped to the map of its entity, any number. Entity: the marker made on a tile, or the destroyed
    /// device for a placement on an object (it only names the map; the handler checks its own entities). Values: the game time it was scheduled at, its delay
    /// (<see cref="Arcanum.Simulation.Traps.TrapDeviceRules.FuseMs"/>, or the retry's <see cref="TrapDeviceRetryDelayMs"/>), the spell record, the tile's cell x and y, and 1 when the entity
    /// is a marker to destroy (0 otherwise).
    /// </summary>
    public const string TrapDevice = "trap.device";
    /// <summary>Pending trap fuses a session holds (project policy; the original has no bound).</summary>
    public const int MaxTrapDevices = 256;
    /// <summary>The delay a fuse that came due without a cast host waits before it is tried again: a minute.</summary>
    public const long TrapDeviceRetryDelayMs = 60_000;
    /// <summary>Pending restock events a session holds (project policy; the original has no bound; the installed maps have 374 objects
    /// with an inventory source).</summary>
    public const int MaxRestocks = 2048;
    /// <summary>The delay of a restock event the session could not run yet (no restock host, the map's seed not attached, a full
    /// deleted-item budget): one game minute.</summary>
    public const long RestockRetryDelayMs = 60_000;
    /// <summary>
    /// The original's magic-tech event (kind 5; ARC-MAGIC-010, ledger RP): a run's tick, interrupt or process
    /// (<see cref="Arcanum.Simulation.Magic.SpellRunEventCode"/>). Global, any number. No entities; values: the game time it was
    /// scheduled at, the run (0..511) and the code. Not saved: the session's runs are not saved either until MT9b, so a restored
    /// session has neither.
    /// </summary>
    public const string SpellRun = "magic.run";

    /// <summary>
    /// MIG12 S8 (ARC-DEV-127): the world-map journey's travel step, the original's real-time WorldMap event (kind 14, TL28): 50 ms after the
    /// journey starts, then 62 ms after each step while the route continues. Real domain, global, one at a time (a second is not queued), not
    /// saved (a journey is not saved, §4 of the clocks concept). No entities; value: the real time it was scheduled at.
    /// </summary>
    public const string WorldMapStep = "travel.step";

    /// <summary>
    /// MIG12 S8 (ARC-DEV-127): the turn-based event (kind 12, TL39): the host's pass over the round's subturns, 2 ms after it is asked for, on the
    /// real list so the Esc menu does not stop it. Real domain, global, one at a time (the original queues none while one is pending), not saved
    /// (a loaded round is rebuilt or resumed by the host's load rule). No entities; value: the real time it was scheduled at.
    /// </summary>
    public const string TurnBasedStep = "combat.step";
    /// <summary>Pending real-time steps a session holds: one of each kind (<see cref="WorldMapStep"/>, <see cref="TurnBasedStep"/>).</summary>
    public const int MaxRealSteps = 2;

    /// <summary>
    /// ARC-DEV-132 (MIG13 S3, goal-table.md §3.2): a goal run's next step, the original's Anim event (kind 1, the animation list, TL32, TL40, GS7): the first
    /// 5 ms of animation time after the start, then the run's frame delay ×8 after each step, counted from the clock at the step. Animation domain, scoped to the
    /// owner's map (the active map: a map change ends the runs and removes their steps), one per run (a new one replaces it), not saved (MIG14 saves the table).
    /// Entity: the run's owner; values: the slot, the run id and the animation time it was scheduled at.
    /// </summary>
    public const string GoalStep = "goal.step";
    /// <summary>Pending goal steps a session holds: one per slot of the goal table.</summary>
    public const int MaxGoalSteps = GoalRunTable.SlotCount;
    /// <summary>Pending magic-tech events a session holds (a budget: about a tick and an interrupt for each of the 512 runs; the
    /// interrupts and process events are not deduplicated).</summary>
    public const int MaxSpellRunEvents = 2 * Arcanum.Simulation.Magic.SpellRunTable.Slots;
    /// <summary>The longest respawn delay: the most negative respawn field in game minutes.</summary>
    public const long MaxRespawnDelayMs = 2_147_483_648L * 60_000L;

    internal static GameEventQueueLimits Limits { get; } = new(MaxPending: MaxPending);

    internal static GameEventRegistry Build(SessionEventSink owner)
    {
        GameEventKindDefinition Hero(string name, LifeTimer timer) => new(name, GameEventDomain.Game, GameEventPersistence.Saved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.Global, 0, Values(timer), 0, 0, new LifeTimerHandler(owner, timer),
            Validate: ScheduledAt);
        GameEventKindDefinition Npc(string name, LifeTimer timer) => new(name, GameEventDomain.Game, GameEventPersistence.Saved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.EntityMap, 1, Values(timer), 1, 0, new LifeTimerHandler(owner, timer),
            Validate: ScheduledAt);
        GameEventKindDefinition Real(string name, SessionRealStep step) => new(name, GameEventDomain.Real, GameEventPersistence.NotSaved,
            GameEventUniqueness.KeepExisting, GameEventScopeRule.Global, 0, 1, 0, 0, new RealStepHandler(owner, step), Validate: payload => payload.Values[0] >= 0);
        return new(RegistryVersion,
        [
            Hero(HeroFatigue, LifeTimer.Fatigue), Hero(HeroPoisonDamage, LifeTimer.PoisonDamage),
            Hero(HeroPoisonRecovery, LifeTimer.PoisonRecovery),
            new(HeroEncumbrance, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.KeepExisting, GameEventScopeRule.Global, 0, 1, 0, 0,
                new EncumbranceDrainHandler(owner), Validate: payload => payload.Values[0] >= 0),
            Npc(NpcFatigue, LifeTimer.Fatigue), Npc(NpcPoisonDamage, LifeTimer.PoisonDamage),
            Npc(NpcPoisonRecovery, LifeTimer.PoisonRecovery), Npc(NpcResting, LifeTimer.Resting),
            new(ScriptCall, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.Global, 2, 4, 0, 0,
                new ScriptCallHandler(owner), Validate: ScriptCallPayload),
            new(HeroEffectEnd, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.Global, 0, 4, 0, 0,
                new EffectEndHandler(owner), Validate: EffectEndPayload),
            new(LockReset, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.EntityMap, 1, 1, 0, 0,
                new LockResetHandler(owner), Validate: payload => payload.Values[0] >= 0),
            new(PartyWait, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.KeepExisting, GameEventScopeRule.EntityMap, 1, 3, 1, 0,
                new WaitTimerHandler(owner), Validate: WaitPayload),
            new(ObjectRespawn, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.EntityMap, 1, 2, 0, 0,
                new ObjectRespawnHandler(owner),
                Validate: payload => payload.Values[0] >= 0 && payload.Values[1] is >= 1 and <= MaxRespawnDelayMs && payload.Entities[0].Code != AbsentRole),
            new(ItemDecay, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.EntityMap, 1, 2, 0, 0,
                new ItemDecayHandler(owner), Validate: DecayPayload),
            new(BodyDecay, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Replace, GameEventScopeRule.EntityMap, 1, 2, 1, 0,
                new BodyDecayHandler(owner), Validate: BodyDecayPayload),
            new(RandomEncounter, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.KeepExisting, GameEventScopeRule.Global, 0, 1, 0, 0,
                new RandomEncounterHandler(owner), Validate: payload => payload.Values[0] >= 0),
            new(Restock, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Replace, GameEventScopeRule.EntityMap, 1, 2, 1, 0,
                new RestockHandler(owner), Validate: RestockPayload),
            new(TrapDevice, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.EntityMap, 1, 6, 0, 0,
                new TrapDeviceHandler(owner), Validate: TrapDevicePayload, ChecksOwnEntities: true),
            new(AiTick, GameEventDomain.Game, GameEventPersistence.SavedUnlessRebuilt, GameEventUniqueness.Replace, GameEventScopeRule.ActiveMapOnly,
                1, 3, 1, 0, new AiTickHandler(owner), Validate: AiTickPayload),
            new(SpellRun, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many, GameEventScopeRule.Global, 0, 3, 0, 0,
                new SpellRunHandler(owner),
                Validate: payload => payload.Values[0] >= 0 && payload.Values[1] is >= 0 and < Arcanum.Simulation.Magic.SpellRunTable.Slots
                    && payload.Values[2] is >= 1 and <= 3),
            Real(WorldMapStep, SessionRealStep.WorldMapStep), Real(TurnBasedStep, SessionRealStep.TurnBasedStep),
            new(GoalStep, GameEventDomain.Animation, GameEventPersistence.NotSaved, GameEventUniqueness.Replace, GameEventScopeRule.EntityMap, 1, 3, 1, 2,
                new GoalStepHandler(owner),
                Validate: payload => payload.Values[0] is >= 0 and < GoalRunTable.SlotCount && payload.Values[1] is >= 1 and <= int.MaxValue && payload.Values[2] >= 0,
                ChecksOwnEntities: true),
        ]);
    }

    // ARC-WORLD-124: [scheduled at, delay (the fuse or the retry), spell record, cell x, cell y, 1 for a marker entity]; the entity names the map.
    internal static bool TrapDevicePayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is Arcanum.Simulation.Traps.TrapDeviceRules.FuseMs or TrapDeviceRetryDelayMs &&
        payload.Values[2] is >= 0 and < Arcanum.Simulation.Magic.SpellPaymentRules.Spells && payload.Values[3] is >= 0 and < 1 << 24 &&
        payload.Values[4] is >= 0 and < 1 << 24 && payload.Values[5] is 0 or 1 && payload.Entities[0].Code != AbsentRole;

    // Scheduled at a game time, after the decay delay or the retry's, naming an item.
    internal static bool DecayPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is Arcanum.Simulation.Items.ItemDecayRules.DelayMs or Arcanum.Simulation.Items.ItemDecayRules.RetryDelayMs
        && payload.Entities[0].Code != AbsentRole;

    // Scheduled at a game time, after the critter decay delay or the retry's, naming an NPC (not the player: its death ends the game).
    internal static bool BodyDecayPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is Arcanum.Simulation.Combat.BloodPoolRules.CritterDecayDelayMs or Arcanum.Simulation.Items.ItemDecayRules.RetryDelayMs
        && AiTickNpc(payload.Entities[0].Code);

    // Scheduled at a game time, after a restock delay or the retry's, naming an NPC or a container (not the player, not a made item).
    internal static bool RestockPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is >= Arcanum.Simulation.Items.RestockRules.MinDelayMs and <= Arcanum.Simulation.Items.RestockRules.MaxDelayMs
            or RestockRetryDelayMs && RestockHolder(payload.Entities[0].Code);

    // A restock event names a session entity id that is neither the absent role, the player nor a made item.
    internal static bool RestockHolder(string? entity) =>
        entity is not null && SessionValidation.Entity(entity) && entity != AbsentRole && entity != PlayerEntity && !entity.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal);

    // Scheduled at a game time, a delay of 1 ms up to the longest, a wake-up marker, naming an NPC.
    internal static bool AiTickPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && payload.Values[1] is >= 1 and <= MaxAiTickDelayMs && payload.Values[2] is 0 or 1 && AiTickNpc(payload.Entities[0].Code);

    // An AI tick names an NPC: a session entity id that is neither the absent role nor the player.
    internal static bool AiTickNpc(string? entity) =>
        SessionValidation.Entity(entity) && entity != AbsentRole && entity != PlayerEntity;

    /// <summary>The original's wait-event predicate (0x4AAA30): whether the event names the NPC.</summary>
    [Arcanum.Simulation.OriginalFunction(0x4AAA30, Arcanum.Simulation.OriginalFunctionScope.Full)]
    internal static bool NamesNpc(GameEvent e, string entity) => e.Payload.Entities[0].Code == entity;

    // A goal step of the run `handle` names: slot and run id both (GS7 compares the slot only; the session compares the id too).
    internal static bool NamesRun(GameEvent e, GoalHandle handle) =>
        e.Payload.Values[0] == handle.Slot && e.Payload.Values[1] == handle.RunId;

    // Scheduled at a game time, a duration of the wait table, any charisma.
    private static bool WaitPayload(GameEventPayload payload) =>
        payload.Values[0] >= 0 && Arcanum.Simulation.Party.CompanionLoyaltyRules.WaitDurationsMs.Contains(payload.Values[1]) &&
        payload.Values[2] is >= int.MinValue and <= int.MaxValue && payload.Entities[0].Code != AbsentRole;

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

internal sealed class WaitTimerHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireWaitTimer(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class ObjectRespawnHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireObjectRespawn(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class ItemDecayHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireItemDecay(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class BodyDecayHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireBodyDecay(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class RandomEncounterHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireRandomEncounter(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class RestockHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireRestock(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class TrapDeviceHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireTrapDevice(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class SpellRunHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireSpellRun(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class EncumbranceDrainHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireEncumbranceDrain(firing) ?? GameEventHandlerOutcome.Refused;
}

internal sealed class LockResetHandler(SessionEventSink sink) : IGameEventHandler
{
    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue) =>
        sink.Owner?.FireLockReset(firing) ?? GameEventHandlerOutcome.Refused;
}
