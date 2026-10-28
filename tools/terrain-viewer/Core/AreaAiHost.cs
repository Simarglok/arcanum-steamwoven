using System.Collections.Immutable;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Reaction;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-WORLD-092: one NPC of the shown map the AI host runs: its session entity, its map row and its map data's world cell.</summary>
public sealed record AreaAiNpc(string Entity, AreaObjectRecord Row, CellCoord Origin);

/// <summary>
/// ARC-WORLD-092: the shown map as the AI host sees it, supplied by the viewer. Cells are world cells (the line of fire's and the
/// map rows'); the step evidence and the planners take chart cells through <see cref="Chart"/>. Every provider is read at each
/// question, so the host follows the viewer's live state (the hero's cell, the companions, the removed objects).
/// </summary>
public sealed record AreaAiScene
{
    public required int RuntimeMap { get; init; }
    public required AreaChart Chart { get; init; }
    public required AreaStepSnapshot Steps { get; init; }
    /// <summary>The NPCs the host runs (companions are left out: their follow goal moves them).</summary>
    public required IReadOnlyList<AreaAiNpc> Npcs { get; init; }
    public required AreaCombatHost Combat { get; init; }
    /// <summary>The hero's world cell now, or null without a hero on this map.</summary>
    public required Func<CellCoord?> HeroCell { get; init; }
    /// <summary>The companions' T0 records and the world cells they hold now (their live overlay).</summary>
    public Func<IReadOnlyList<(AreaObjectKey Key, CellCoord Cell)>>? Companions { get; init; }
    /// <summary>The T0 records the session destroyed or switched off.</summary>
    public Func<IReadOnlySet<AreaObjectKey>>? Removed { get; init; }
    /// <summary>The shown map's line of fire now (walls, doors, objects, the companions on their cells), or null.</summary>
    public Func<ILineOfFireMap<AreaObjectRecord>?>? LineOfFire { get; init; }
    /// <summary>The shown map as the drop's placement reads it, or null.</summary>
    public Func<Arcanum.Simulation.Items.IDropMap?>? DropMap { get; init; }
    /// <summary>The hero's worn items with their durability values, or null.</summary>
    public Func<IReadOnlyDictionary<EquipmentSlot, SessionWearItem>?>? HeroWorn { get; init; }
    /// <summary>The NPC the hero talks with, or null.</summary>
    public Func<string?>? ConversationPartner { get; init; }
    /// <summary>The context the hero enters combat mode with when attacked (the viewer's turn-based setting and round world).</summary>
    public Func<SessionCombatModeContext>? HeroCombatMode { get; init; }
    /// <summary>The AI parameter table; null: every NPC reads row values 0 (the original's out-of-table read is refused as data).</summary>
    public AiParameterTable? Parameters { get; init; }
    /// <summary>The per-gait step durations; null: the live step of <see cref="DiagnosticGridSession.LiveDuration"/>.</summary>
    public GaitTiming? Timing { get; init; }
    /// <summary>The attack ART's frames, action frame and raw frame rate of an NPC's body, or null for the fixed human attack ART.</summary>
    public Func<AreaAiNpc, (int Frames, int ActionFrame, int RawFramesPerSecond)?>? AttackArt { get; init; }
    /// <summary>ARC-WORLD-099: whether the hero runs no animation (its walk or attack, which count in the original's animations-complete
    /// test); null: idle.</summary>
    public Func<bool>? HeroIdle { get; init; }
    /// <summary>ARC-WORLD-102: the shown map's cast host (the hero's casts go through it too), or null while the spell data or the
    /// map's magic world is missing: the NPCs then know no spell.</summary>
    public Func<SessionSpellCaster?>? Spells { get; init; }
    /// <summary>ARC-WORLD-102: the cast entry's eye candy of a spell on the NPC (goal 27's play, 0x429F00), started at 0 ms, or null
    /// when none plays (no table, art, part or free slot, an ART that does not read).</summary>
    public Func<string, int, CastEyeCandyAnimation?>? CastEyeCandy { get; init; }
    /// <summary>ARC-WORLD-102: whether a spell's projectile eye candy has art (0x458B70), so its release launches a missile
    /// (ARC-WORLD-105: <see cref="AreaAiNoticeKind.Missile"/>, flown by the viewer).</summary>
    public Func<int, bool>? MissileArt { get; init; }
}

/// <summary>What the AI host tells the viewer: an NPC's attack swing (its pose), the hit's result, a step, a sound, (ARC-WORLD-099)
/// the fidget an NPC starts after a done run (0x4B4320; its animation is not drawn: NPC ART is not read yet), (ARC-WORLD-102) a
/// cast's play, its release and its goal's end, and (ARC-WORLD-105) a missile launched at the play's action frame.</summary>
public enum AreaAiNoticeKind { Swing, Hit, Step, Sound, Fidget, Cast, Release, CastEnd, Missile }

/// <summary>ARC-WORLD-092: one notice for the viewer (the attack pose, the hit's sounds and status line, the redraw).</summary>
public sealed record AreaAiNotice(AreaAiNoticeKind Kind, string Npc, string? Target)
{
    public SessionAttackResult? Result { get; init; }
    public int Sound { get; init; }
    public AiAttackSwing Swing { get; init; }
    /// <summary>ARC-WORLD-102: the cast's spell and magic run, its play's timing (<see cref="AreaAiNoticeKind.Cast"/>; none plays: the
    /// release came at once), whether the release began the run (<see cref="AreaAiNoticeKind.Release"/>) and the goal end's free
    /// (<see cref="AreaAiNoticeKind.CastEnd"/>); ARC-WORLD-105: the launched missile's spell and run, its target object in
    /// <see cref="Target"/> (<see cref="AreaAiNoticeKind.Missile"/>).</summary>
    public int Spell { get; init; } = -1;
    public int Run { get; init; } = -1;
    public AiCastSwing CastSwing { get; init; }
    public bool Released { get; init; }
    public SessionSpellRunResult? SpellRun { get; init; }
    /// <summary>ARC-WORLD-105: the cast host that launched the missile (its arrival and loss go through it).</summary>
    public SessionSpellCaster? Caster { get; init; }
}

/// <summary>
/// ARC-WORLD-092 (NPC AI slice I10, first fight): the AREA viewer's AI host. It is the scheduler's host (<see cref="ISessionAiWorld"/>,
/// forwarding the event pass's script and party questions to the viewer's event world), the executor's world (<see cref="IAiPerformWorld"/>)
/// and the hostility and morale rules' world (<see cref="IMoraleWorld"/>) over the shown map. It holds one goal run per NPC
/// (<see cref="AiGoalRun"/>, not saved) and steps it after its delay (<see cref="Pump"/>); the pass's perform stage
/// runs <see cref="AiExecutorRules.Perform"/>; the danger stage runs <see cref="HostilityRules.DangerUpdate"/> and applies its plan through
/// the session's setter; the hit step is the session's <see cref="GameSessionOwner.AttackByNpc"/>, followed by the attack event; the
/// hero's hits run the attack event here too (<see cref="HeroAttacked"/>). The walker moves NPCs tile by tile (<see cref="Movers"/>).
/// The runs go by the viewer's world clock (<see cref="Pump"/>), the ticks by the session's game clock. In a turn-based round
/// (ARC-WORLD-095) the host follows the round's subturns (<see cref="ObserveRound"/>): a round's start and each new subturn interrupt
/// runs as the original's do, the pending cost reads 0 after each subturn start, and the round host
/// (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int)"/>) runs the NPCs' subturn hooks.
/// ARC-WORLD-099: an NPC's subturn end asks its end-combat script (<see cref="SubturnsEnded"/>), a turn set's interrupt that frees the
/// last run runs the completion check, and a done run's end may start the NPC's fidget (<see cref="AiExecutorRules.FidgetAfterRun"/>).
/// ARC-WORLD-102: the support rules' spell half (<see cref="IAiSupportWorld"/>, the spell answers through the map's cast host) chooses
/// a spell at the danger update's target (<see cref="AiSupportRules.ChooseSpell"/>), perform casts it (<see cref="Cast"/>,
/// <see cref="SessionSpellCaster.CastByAi"/>) and the casting goals play through the cast host (start, turn, play, release, end).
/// ARC-WORLD-104: a cast while the NPC's casting goal 26 or 27 plays is queued beneath it (SU13, AC5), its goal played once the goals
/// above it are done; the AI pass itself never meets one, as the gate holds a casting NPC busy (AC6). ARC-WORLD-105: a spell with a
/// missile launches it at the play's action frame (AC8), the viewer flying it from the NPC.
/// NOT READY (later slices): item use and items in the lists, skills, throws and pickups,
/// grenades, the back-off's away path, followers in combat (the companions keep their follow goal), the follower rule and the comment lines, NPC
/// ranged attacks, scripts at the AI points, routines and waypoints, saved NPC positions.
/// </summary>
public sealed class AreaAiHost : ISessionAiWorld, ISessionPartyEventWorld, ISessionLootWindowWorld, IAiPerformWorld, IMoraleWorld, IAiSupportWorld
{
    private const string Pc = AiSchedulerRules.Pc;
    // Object flags: destroyed, off, don't draw, invisible, invulnerable; critter flags; spell flags; NPC flags (HostilityCritter's bits).
    private const uint DontDraw = 0x100, ObjectInvisible = 0x200, Invulnerable = 0x400000;
    private const uint Undead = 0x4, Animal = 0x8, SleepingFlag = 0x1000, NoFleeFlag = 0x8000000, NonLethal = 0x10000000, AnimalEnshrouded = 0x40000000;
    private const uint NighInvulnerable2 = 0x200, AutoAnimates2 = 0x2;
    private const uint InvisibleSpell = 0x1, DetectsInvisibleSpell = 0x200, StonedSpell = 0x10000, MindControlledSpell = 0x8000000, EnshroudedSpell = 0x20000000;
    private const uint WaitingHereNpc = 0x8, KillOnSightNpc = 0x100, KosOverrideNpc = 0x800, GeneratorNpc = 0x80000, AloofNpc = 0x20000;
    // ARC-WORLD-102: critter flag MECHANICAL and NPC flag CAST_HIGHEST (field 0x118, read in 0x4AE720).
    private const uint MechanicalFlag = 0x20000000, CastHighestNpc = 0x40000;
    // The lists of a host without spell records: every spell without AI values.
    private static readonly AiGlobalLists NoLists = AiGlobalLists.Build([.. Enumerable.Repeat(AiSpellValues.None, AiGlobalLists.Spells)]);
    private const int GuardClass = 5;
    // The fixed human attack ART (hmfuwxbu: 9 frames, action frame 6, 15 frames per second) stands in for an NPC's own.
    private static readonly (int Frames, int ActionFrame, int RawFramesPerSecond) FixedAttackArt = (9, 6, 15);
    // The goal priorities (the goal table's header word 1, read by this task; 0x423F3B counts the goals of priority 2 and above).
    private static readonly IReadOnlyDictionary<AiGoalType, int> Priorities = new Dictionary<AiGoalType, int>
    {
        [AiGoalType.RunToTile] = 2, [AiGoalType.AttemptMove] = 2, [AiGoalType.MoveToPause] = 2, [AiGoalType.MoveNear] = 2,
        [AiGoalType.PickupItem] = 3, [AiGoalType.AttemptPickup] = 3, [AiGoalType.Attack] = 3, [AiGoalType.AttemptAttack] = 3,
        [AiGoalType.Follow] = 2, [AiGoalType.Flee] = 3, [AiGoalType.CastSpell] = 3, [AiGoalType.AttemptSpell] = 4,
        [AiGoalType.ThrowItem] = 3, [AiGoalType.UseItem] = 3, [AiGoalType.MoveAway] = 2, [AiGoalType.RunNear] = 2,
        [AiGoalType.MeleeApproach] = 2, [AiGoalType.SpreadOut] = 2, [AiGoalType.CombatApproach] = 2, [AiGoalType.RangedApproach] = 2,
        [AiGoalType.CastSpellWithAnimation] = 3, [AiGoalType.AttemptSpellWithAnimation] = 4,
    };

    private readonly GameSessionOwner _session;
    private readonly AreaAiScene _scene;
    private readonly ISessionScriptWorld _events;
    private readonly ISessionCombatWorld _round;
    private readonly Dictionary<string, AreaAiNpc> _npcs;
    private readonly Dictionary<string, (AiGoalRun Run, long DueMs)> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AreaAiTrace> _traces = new(StringComparer.Ordinal);
    private readonly List<AreaAiNotice> _notices = [];
    // The round's events of the host's own session calls (the executor's consumes and subturn ends), for the viewer's round log.
    private readonly List<SessionCombatEvent> _roundEvents = [];
    // The animation clock of the last pump.
    private long _nowMs;
    // ARC-WORLD-095: the round as last observed (whether one ran, the session's subturn starts then, the critter of the last turn set:
    // 0x5FC248), the pending cost (0x5FC244) and the subturn start it was written in.
    private bool _roundSeen;
    private long _subturnsSeen = -1;
    private string? _turnSetActor;
    private int _pendingCost;
    private long _pendingAt = -1;
    // ARC-WORLD-099: the round actor whose done run's fidget call waits for its subturn hook.
    private string? _fidgetAfterHook;
    // ARC-WORLD-102: the pass record's choice the danger update wrote for perform (0x4ABC20 → 0x4AC180), the list cache (SP11, not
    // saved), the facing a casting goal's start computed for its turn, and the magic runs whose play reaches no action frame.
    private readonly Dictionary<string, AiPassChoice> _passChoices = new(StringComparer.Ordinal);
    private AiListCache _listCache = AiListCache.Empty;
    private readonly Dictionary<int, SpellFacing?> _castFacing = [];
    private readonly HashSet<int> _noRelease = [];
    // ARC-WORLD-104: the magic runs of queued casting goals that have not started yet (SU13); an end step leaves them unplayed.
    private readonly HashSet<int> _queuedCasts = [];
    // ARC-WORLD-105: the runs launched by a casting goal still playing, with the cast host that launched them (its handled frame and
    // missile live there): a host rebuilt before the goal's end would see neither and free the run charged.
    private readonly Dictionary<int, SessionSpellCaster> _launched = [];

    /// <param name="events">The viewer's event world: the pass's script calls, life facts, party and loot questions.</param>
    /// <param name="round">The turn-based round's world (the scan of the map's critters).</param>
    public AreaAiHost(GameSessionOwner session, AreaAiScene scene, ISessionScriptWorld events, ISessionCombatWorld round)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _round = round ?? throw new ArgumentNullException(nameof(round));
        _npcs = scene.Npcs.ToDictionary(n => n.Entity, StringComparer.Ordinal);
    }

    public AreaAiScene Scene => _scene;
    public AreaAiMovers Movers { get; } = new();
    public GameSessionOwner Session => _session;

    /// <summary>The NPC's run now, or null.</summary>
    public AiGoalRun? RunOf(string npc) => _runs.TryGetValue(npc, out var held) ? held.Run : null;

    /// <summary>What the overlay shows of an NPC: its last pass, perform, run step and reason.</summary>
    public AreaAiTrace? TraceOf(string npc) => _traces.GetValueOrDefault(npc);

    /// <summary>The subturn start whose subturn hook the round host ran last (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int)"/>),
    /// -1 before any; a host built during a round asks the NPC whose subturn it is again.</summary>
    public long HookedSubturn { get; set; } = -1;

    /// <summary>ARC-WORLD-099: the completion checks the host's own round observations ran (a turn set's interrupt that freed the last
    /// run), for the overlay and tests, and the failure of the last one the session refused (none: <see cref="GameSessionFailure.None"/>).</summary>
    public int CompletionChecks { get; private set; }
    public GameSessionFailure CheckFailure { get; private set; }

    /// <summary>The round's events of the host's own session calls since the last call, in order.</summary>
    public IReadOnlyList<SessionCombatEvent> DrainRoundEvents()
    {
        var drained = _roundEvents.ToArray();
        _roundEvents.Clear();
        return drained;
    }

    /// <summary>Whether a goal run of a critter other than <paramref name="npc"/> runs (each counts in the original's animations-complete
    /// test: every goal of the host has priority 2 or more).</summary>
    public bool RunsBesides(string npc) => _runs.Keys.Any(other => other != npc);

    /// <summary>
    /// ARC-WORLD-095: follows the session's turn-based round. A round that began since the last call interrupts every run (the round's
    /// start 0x4B71E0: 0x424310 and the scanned critters' 0x424070 at priority 3); a subturn that started since then interrupts the runs
    /// of the critter of the previous turn set when that is an NPC (the turn set 0x4B6E70, 0x424070 at priority 3). Priority 3 ends a run
    /// whose current goal has priority 3 or less (0x44E2C0: walks, attacks and flights; an attempted cast's 4 goes on) and its end nodes
    /// run. Called by the pump before each step and after the last, and by the round host.
    /// </summary>
    public void ObserveRound()
    {
        if (_session.QueryCombatRound() is not { } round) { _roundSeen = false; return; }
        if (!_roundSeen)
        {
            _roundSeen = true;
            foreach (var npc in _runs.Keys.ToArray()) Interrupt(npc);
        }
        var starts = _session.QueryCombatSubturnStarts();
        if (starts == _subturnsSeen) return;
        _subturnsSeen = starts;
        // ARC-WORLD-099: a turn set's interrupt that frees the last run (and the hero runs none) calls the completion check (0x4B7080 as
        // the animation system's callback, TH5), before the new participant's hook. A round's start runs none: its interrupts come
        // before the callback is registered and the round flag set (0x4B71E0).
        var previous = _turnSetActor;
        _turnSetActor = Id(round.Actor);
        // A check that passes the turn on (an actor seen again with unchanged action points) is the next observation's turn set.
        if (previous is not null && previous != Pc && Interrupt(previous) && _runs.Count == 0 && (_scene.HeroIdle?.Invoke() ?? true))
        {
            var check = _session.CompleteCombatActions(_session.Version, _round);
            if (check.Disposition != GameSessionDisposition.Rejected) CompletionChecks++;
            else CheckFailure = check.Failure;
            RoundCall(check);
        }
    }

    // An interrupt at priority 3 (0x44E2C0 with 3): a run whose current goal has priority 3 or less ends with its end nodes. True: it
    // freed a run.
    private bool Interrupt(string npc)
    {
        if (RunOf(npc) is not { } run || Priorities.GetValueOrDefault(run.Top.Goal, 2) > 3) return false;
        DropRun(npc);
        return true;
    }

    /// <summary>The notices since the last call, in order.</summary>
    public IReadOnlyList<AreaAiNotice> DrainNotices()
    {
        var drained = _notices.ToArray();
        _notices.Clear();
        return drained;
    }

    // ---- the viewer's calls ----

    /// <summary>
    /// Schedules a delayed AI tick (0x4AD6E0, as for a loaded NPC) for every NPC of the map that has none pending and is not dead in
    /// the session; refused schedules are counted. Called when the map's AI starts and after a load.
    /// </summary>
    public int ScheduleTicks()
    {
        var refused = 0;
        foreach (var npc in _scene.Npcs)
        {
            if (_session.QueryAiTick(npc.Entity) is not null || Life(npc.Entity) is null) continue;
            var result = _session.ScheduleAiTick(_session.Version, new SessionAiTickRequest
            {
                Entity = npc.Entity, Start = SessionAiTickStart.Delayed, DistanceToPc = DistanceToPc(npc.Entity),
            });
            if (result.Disposition == GameSessionDisposition.Rejected) refused++;
        }
        return refused;
    }

    /// <summary>
    /// Steps every run that is due at <paramref name="nowMs"/> (<paramref name="maxSteps"/> steps at most), each after its delay; a run
    /// that ended is dropped (a killed one, or one stopped by the node limit or an overflow, through <see cref="AiExecutorRules.EndRun"/>).
    /// Returns the steps taken. The runs go by the caller's animation clock in milliseconds (the viewer's world clock, which the hero's
    /// walk and attack play on; the session's game clock runs 8 times faster), the AI ticks by the session's game clock.
    /// </summary>
    public int Pump(long nowMs, int maxSteps = 64)
    {
        var steps = 0;
        _nowMs = Math.Max(_nowMs, nowMs);
        Movers.Settle(_nowMs);
        // Before each step: a step that began a round (an attack putting the hero into combat mode) or passed the subturn on
        // interrupts runs as the original's round start and turn set do, its own run too (inside its step the original marks it done).
        for (; ; )
        {
            ObserveRound();
            if (steps >= maxSteps || _runs.Where(r => r.Value.DueMs <= _nowMs).OrderBy(r => r.Value.DueMs).ThenBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => r.Key).FirstOrDefault() is not { } npc) break;
            var (run, due) = _runs[npc];
            var step = AiExecutorRules.Step(run, this, _session.AiRolls, _session.Rules.Combat);
            steps++;
            Trace(npc) .Step = $"{step.End} {step.Reason} nodes={step.Nodes} delay={step.DelayMs}";
            Trace(npc).Reason = step.Reason;
            if (step.Ended || step.Run is null)
            {
                _runs.Remove(npc);
                // The end step ran the end nodes itself; a killed run, the node limit and an overflow drop the run without them.
                if (step.End is AiRunEnd.Killed or AiRunEnd.NodeLimit or AiRunEnd.Overflow) EndDroppedRun(run);
                // ARC-WORLD-104: the end step runs the end node of every goal (QC5); a queued goal 26's (0x42B090) frees nothing (MG43).
                else if (step.End == AiRunEnd.EndStep) FreeQueuedCasts(run);
                // ARC-WORLD-099: the stepper's end of a done run (TH4): the fidget's call (0x4B4320), after the owner's subturn hook. The
                // round actor's hook is the round host's next frame (<see cref="FidgetAfterHook"/>); anyone else's hook does nothing. The
                // node limit frees its run without it; an overflow, which the reading did not follow, is taken as the node limit.
                if (step.End is not (AiRunEnd.NodeLimit or AiRunEnd.Overflow))
                {
                    if (InRound && RoundActor == npc) _fidgetAfterHook = npc;
                    else Fidget(npc);
                }
                continue;
            }
            // A delay of 0 is the next frame.
            _runs[npc] = (step.Run, Math.Max(due, _nowMs) + Math.Max(step.DelayMs, 1));
        }
        return steps;
    }

    /// <summary>
    /// The attack event of the hero's attack on the NPC <paramref name="victim"/> (0x4A9650, <see cref="HostilityRules.AttackEvent"/>):
    /// the session's who-hit-me-last and team writes, the reaction change toward the hero, the victim's turn and its reaction to the
    /// attack (fight or flee, <see cref="MoraleRules.ReactToAttack"/>) through the setter, and the witnesses of the noise
    /// (<see cref="HostilityRules.NoiseWitnesses{T}"/>, <see cref="HostilityRules.Witness"/>). The attacker's and the victim's followers
    /// (the follower rule) are not told yet.
    /// </summary>
    public void HeroAttacked(string victim) => AttackEvent(Pc, victim);

    /// <summary>
    /// ARC-WORLD-099: the subturn ends among <paramref name="events"/> (a round call's): an NPC's end runs its end-combat script (point 16,
    /// 0x4B7630: the NPC as attachee and triggerer; the PC's end calls the interface's callback instead). This host runs no SCR at the AI
    /// points (<see cref="RunScript"/> continues), so only the trace counts it. The host passes the events of its own round calls (the
    /// executor's consumes and subturn ends, a turn set's completion check); the round host those of its calls.
    /// </summary>
    public void SubturnsEnded(IEnumerable<SessionCombatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var ended in events)
            if (ended is { Kind: SessionCombatEventKind.SubturnEnded, Combatant: { IsHero: false } critter } && Id(critter) is var npc)
            {
                RunScript(AiScriptPoint.EndCombat, npc, npc);
                Trace(npc).EndCombatScripts++;
            }
    }

    /// <summary>ARC-WORLD-099: the fidget call of the round actor's done run, which the stepper makes after the subturn hook the round host
    /// ran for <paramref name="npc"/> since (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int)"/>);
    /// nothing when its run's end left none pending.</summary>
    public void FidgetAfterHook(string npc)
    {
        if (_fidgetAfterHook != npc) return;
        _fidgetAfterHook = null;
        Fidget(npc);
    }

    // The fidget after a done run (0x4B4320): an NPC in combat mode outside a round, or auto-animating, without a run. Its art test
    // (0x503E20) allows it: NPC ART is not read yet. The fidget's run (goal 2, priority 1) is not held: the round's completion test does
    // not count it, every start replaces it, and the subturn hook, the only reader of "a run runs" (0x423300), never meets it, since a
    // round starts none.
    private void Fidget(string npc)
    {
        if (npc == Pc || Fields(npc)?.Critter is not { } critter) return;
        var combatMode = _session.QueryCombatMode(SessionCombatant.Critter(npc));
        if (!AiExecutorRules.FidgetAfterRun(this, npc, combatMode, (critter.Flags2 & AutoAnimates2) != 0, _runs.ContainsKey(npc), artAllows: true)) return;
        _notices.Add(new(AreaAiNoticeKind.Fidget, npc, null));
    }

    /// <summary>
    /// ARC-WORLD-102: ends every run as a dropped one (its end nodes, <see cref="EndDroppedRun"/>: a casting goal's magic run is freed,
    /// so no NPC's spell run outlives its goal), then <see cref="Reset"/>. The viewer calls it before it drops or replaces the host of
    /// the same session (a follower joining or leaving, a map change, the AI turned off).
    /// </summary>
    public void Retire()
    {
        foreach (var npc in _runs.Keys.ToArray()) DropRun(npc);
        Reset();
    }

    /// <summary>Drops every run and mover without their end nodes (a load: the runs belong to the replaced session).</summary>
    public void Reset()
    {
        _runs.Clear();
        _traces.Clear();
        _notices.Clear();
        _roundEvents.Clear();
        _roundSeen = false; _subturnsSeen = -1; _turnSetActor = null; _pendingCost = 0; _pendingAt = -1; HookedSubturn = -1; _fidgetAfterHook = null;
        _passChoices.Clear(); _listCache = AiListCache.Empty; _castFacing.Clear(); _noRelease.Clear(); _queuedCasts.Clear(); _launched.Clear();
        Movers.Clear();
    }

    private void AttackEvent(string attacker, string victim)
    {
        var plan = HostilityRules.AttackEvent(this, attacker, victim, AttackEventFlags.None);
        Trace(victim).Event = $"{plan.Stop}";
        if (plan.Stop is AttackEventStop.Missing or AttackEventStop.VictimProtected or AttackEventStop.VictimDead or AttackEventStop.VictimGone
            or AttackEventStop.VictimDecoy or AttackEventStop.VictimWall or AttackEventStop.Self) return;
        // A PC victim responds (0x4A9AD0): it enters combat mode (its call of 0x4B3F50).
        if (plan.PcVictimResponds) HeroIntoCombatMode();
        if (plan.RaisesNoise) Witnesses(attacker, victim);
        if (plan.Stop != AttackEventStop.None) return;
        _session.ApplyAttackEvent(_session.Version, new SessionAttackEventRequest { Attacker = Combatant(attacker)!, Victim = victim, Plan = plan });
        if (plan.TurnsVictim && Tile(attacker) is { } there && _npcs.TryGetValue(victim, out var npc)) Movers.Face(victim, npc.Row.Key, npc.Origin, there);
        if (plan.Reaction == AttackReactionChange.Deliberate && attacker == Pc && ReactionFacts(victim) is { } facts && Row(victim) is { } row)
            _session.ReactToPcHit(_session.Version, victim, facts.Npc, facts.Pc, new(row.FollowerHitReaction, row.HostileReaction), deliberate: true);
        if (plan.ReactsToAttack) React(victim, attacker);
    }

    // The witnesses of an attack's noise: the attacker's square when under 30 tiles apart, then the victim's (AW16 under the switch).
    private void Witnesses(string attacker, string victim)
    {
        var distance = Distance(attacker, victim);
        var radius = AwarenessRules.NoiseRadius(Loudness.Normal);
        var witnesses = HostilityRules.NoiseWitnesses(distance, Square(attacker, radius), Square(victim, radius), _session.Rules.Combat);
        foreach (var witness in witnesses)
        {
            if (witness == Pc || witness == attacker || witness == victim || !_npcs.ContainsKey(witness)) continue;
            var outcome = HostilityRules.Witness(this, witness, attacker, victim, Loudness.Normal);
            switch (outcome.Response)
            {
                case WitnessResponse.ReactToAttacker: React(witness, attacker); break;
                case WitnessResponse.ReactToVictim: React(witness, victim); break;
            }
        }
    }

    // The reaction to an attack (0x4AA620 with the should-flee of 0x4AB2A0): a weighed reaction asks the setter.
    private void React(string npc, string attacker)
    {
        var reaction = MoraleRules.ReactToAttack(this, npc, attacker, _session.AiRolls, _session.Rules.Combat);
        Trace(npc).Reaction = $"{reaction.Reaction.Step} {reaction.Reaction.Request}";
        if (reaction.Reaction is { Step: AttackReactionStep.Weigh, Request: { } state, Target: { } target }) SetDanger(npc, state, target);
    }

    // ---- the pipeline's stages (ISessionAiWorld, IAiHost) ----

    public long DistanceToPc(string npc) => Tile(npc) is { } at && Tile(Pc) is { } pc ? AiExecutorRules.Distance(at, pc) : -1;

    // No SCR runs at the AI points yet: every point continues.
    public bool RunScript(AiScriptPoint point, string npc, string? triggerer) => true;
    public bool RunWaypoints(string npc) => false;
    public void RunStandpoints(string npc) { }
    public bool IsGenerator(string npc) => Fields(npc)?.Npc is { } fields && (fields.Flags & GeneratorNpc) != 0;
    public long? Spawn(string npc) => null;
    public bool IsEncounterCreature(string npc) => false;
    public void Destroy(string npc) { }
    public void InterruptAnimations(string npc) => DropRun(npc);
    public bool SectorLoaded(string npc) => true;
    public AiCatchUpFacts CatchUp(string npc) => new();
    public void MoveToLeader(string npc) { }
    public bool MapClearing => false;
    public bool Sleeping(string npc) => Fields(npc)?.Critter is { } critter && (critter.Flags & SleepingFlag) != 0;
    public bool Hidden(string npc) => Fields(npc) is { } fields && (fields.Object.Flags & DontDraw) != 0;
    public bool PcHidden => false;
    public AiConversation Conversation(string npc) => _scene.ConversationPartner?.Invoke() == npc ? AiConversation.Pc : AiConversation.None;
    public bool ConversationCallback => false;
    public void EndConversation(string npc) { }

    /// <summary>The gate's animation priority (0x4233D0, AC6): that of the bottom goal (stack slot 0) of the NPC's run, 0 without a
    /// run. ARC-WORLD-105: the top goal was read before; for a run whose goals differ, such as an attack under its approach, the gate
    /// then let the pass run mid-approach (a technical slip of the host, fixed in both modes).</summary>
    public int AnimationPriority(string npc) => RunOf(npc) is { } run ? Priorities.GetValueOrDefault(run.Bottom.Goal, 2) : 0;
    public bool StunAnimationRunning(string npc) => false;
    public bool AnimationRunning(string npc) => _runs.ContainsKey(npc);
    public void ReWield(string npc, bool allSlots) { }
    public void EndMaintainedSpells(string npc) { }
    public bool Heal(string npc) => false;
    public bool SearchItems(string npc) => false;
    public bool Loading => false;

    /// <summary>
    /// The danger stage: the update's plan applied through the setter. ARC-WORLD-102: for a fight the spell and item choice runs for
    /// the found target before the setter (0x4ABC20, HS1, SP4), the setter's target is then <see cref="HostilityRules.FocusTarget"/>
    /// of the pass record's (SP16), and the choice stays in the pass record for perform unless the setter's NO_ATTACK or "NPC
    /// fighting" rule refused the fight in the Steamwoven setting (FO8, <see cref="HostilityRules.KeepsChosenAction"/>).
    /// </summary>
    public void UpdateDanger(string npc)
    {
        _passChoices.Remove(npc);
        var plan = HostilityRules.DangerUpdate(this, npc, _session.AiRolls, _session.Rules.Combat);
        Trace(npc).Danger = $"{plan.Path} {plan.Request?.ToString() ?? "-"} {plan.Target ?? "-"}";
        if (plan.Path == DangerUpdatePath.Flee) { _session.RunFleeSpellStep(_session.Version, npc, () => false); return; }
        if (plan.Unlocks) _session.UnlockDangerTarget(_session.Version, npc);
        if (plan.Request is not { } state) return;
        AiPassChoice? choice = null;
        if (plan.ChoosesAction && plan.Target is { } found)
        {
            var chosen = AiSupportRules.ChooseSpell(this, npc, found, _session.AiRolls);
            // An item AI-action entry used at once (0x462CC0) needs the NPC's items, which the lists do not hold yet.
            choice = chosen.Choice;
            Trace(npc).Spell = $"{chosen.Stop} chance={chosen.Chance} spell={choice?.Spell.ToString() ?? "-"} target={choice?.Target ?? "-"}";
        }
        var target = state == DangerState.CombatFocus ? HostilityRules.FocusTarget(plan, choice?.Target ?? plan.Target, _session.Rules.Combat) : null;
        var result = SetDanger(npc, state, target);
        if (choice is not null && (result.Transition is not { } transition || HostilityRules.KeepsChosenAction(transition, _session.Rules.Combat)))
            _passChoices[npc] = choice;
    }

    /// <summary>The perform stage: <see cref="AiExecutorRules.Perform"/> with the danger source as the pass's target; a started run
    /// replaces the NPC's (the old one's end nodes run, <see cref="AiExecutorRules.EndRun"/>) and steps at the next pump.</summary>
    public void Perform(string npc)
    {
        var danger = _session.QueryDanger(npc);
        var target = danger.Source is { } source ? Id(source) : null;
        var current = RunOf(npc);
        // ARC-WORLD-102: the pass record's choice from this pass's danger update (none: the action type 0).
        _passChoices.Remove(npc, out var choice);
        var result = AiExecutorRules.Perform(this, npc, target, choice, null, current);
        Trace(npc).Perform = $"{result.Kind}{(result.Refusal == AiStartRefusal.None ? "" : " " + result.Refusal)}{(result.Started ? " started" : "")}";
        if (!result.Started || result.Run is not { } run) return;
        if (current is not null && !ReferenceEquals(current, run)) EndDroppedRun(current);
        // Started inside the event pass: it steps at the next pump.
        _runs[npc] = (run, _nowMs);
    }

    /// <summary>ARC-WORLD-105 smoke seam: perform with <paramref name="choice"/> as the pass record's choice, as a danger update that
    /// chose it would (the native ai-cast smoke's missile spell).</summary>
    public void PerformChoice(string npc, AiPassChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        _passChoices[npc] = choice;
        Perform(npc);
    }

    // An NPC that joined the hero since the host was built is the companions' (it counts as gone here).
    public SessionLifeTarget? AiLifeTarget(string entity) => _session.QueryCompanion(entity).LedByPc ? null : Build(entity)?.Target;

    private void DropRun(string npc)
    {
        if (_runs.Remove(npc, out var held)) EndDroppedRun(held.Run);
    }

    // A run the host drops without stepping it to its end: its end nodes (AiExecutorRules.EndRun). ARC-WORLD-102: a casting goal 26 or
    // 82 dropped before its attempt goal has only the animation reset as its end node (0x42B090), which frees nothing, so its magic run
    // would keep its slot, never begun, as MG43's queued cast does; as there (owner decision 2026-10-28, both modes) the NPC's run that
    // still waits for its BEGIN is freed, unpaid.
    private void EndDroppedRun(AiGoalRun run)
    {
        AiExecutorRules.EndRun(run, this);
        FreeUnbegunCasts(run, queuedOnly: false);
    }

    // ARC-WORLD-104: a run that ended through the end step leaves the casts queued beneath its goals unplayed: their goals' end node,
    // the animation reset (0x42B090), frees nothing, so in the original the runs keep their slots (MG43, fixed in both modes: the
    // host frees them unpaid, as the hero's dropped queue). Only the goals that never started are theirs: one that started ended its
    // own run.
    private void FreeQueuedCasts(AiGoalRun run) => FreeUnbegunCasts(run, queuedOnly: true);

    private void FreeUnbegunCasts(AiGoalRun run, bool queuedOnly)
    {
        var caster = _scene.Spells?.Invoke();
        foreach (var frame in run.Frames)
        {
            if (frame is not { Goal: AiGoalType.CastSpell or AiGoalType.CastSpellWithAnimation, SpellRun: { } id }) continue;
            var queued = _queuedCasts.Remove(id);
            if ((queuedOnly && !queued) || caster is null) continue;
            if (OwnRun(run.Owner, id) && caster.DropQueued(id) is { } freed)
                _notices.Add(new(AreaAiNoticeKind.CastEnd, run.Owner, null) { Run = id, SpellRun = freed });
        }
    }

    // A goal's magic run is the NPC's own: a queued run cancelled meanwhile (QC6) may have lent its slot to another caster's run.
    private bool OwnRun(string id, int run) => _session.QuerySpellRuns()[run]?.Parent?.Id == id;

    // The setter call with the facts its rules read; a fight on the hero carries the reaction facts (rule 8).
    private SessionDangerResult SetDanger(string npc, DangerState state, string? target)
    {
        var request = new SessionDangerRequest
        {
            Entity = npc, State = state, Target = target is null ? null : Combatant(target), Facts = SetterFacts(npc),
            Reaction = target == Pc && state == DangerState.CombatFocus && ReactionFacts(npc) is { } facts
                ? new SessionDangerReaction(facts.Npc, facts.Pc, Row(npc)?.HostileReaction ?? 0) : null,
            CombatMode = NpcCombatMode(npc),
        };
        var result = _session.SetDangerState(_session.Version, request);
        Trace(npc).Setter = $"{state} {target ?? "-"} {result.Disposition} {result.Failure} {result.Transition?.Rule}";
        return result;
    }

    // ---- the executor's world (IAiGoalWorld) ----

    public CellCoord? Tile(string id)
    {
        if (id == Pc) return _scene.HeroCell();
        return _npcs.TryGetValue(id, out var npc) ? Movers.CellOf(id, npc.Origin) : CompanionCell(id);
    }

    public bool Gone(string id) => id != Pc && Key(id) is { } key && _scene.Removed?.Invoke().Contains(key) == true;

    public bool DeadCritter(string id) => Status(id)?.IsDead ?? true;

    public AiMoverFacts Mover(string id)
    {
        var status = Status(id);
        return new(Pc: id == Pc, Fatigue: status?.Fatigue ?? 0, MaxFatigue: status?.MaxFatigue ?? 0, CannotTurn: !Active(id),
            SpreadOut: id != Pc && _session.QueryCompanion(id).SpreadOut);
    }

    /// <summary>The terrain step from the object's tile toward the other's: walls, doors and objects other than critters decide
    /// (inference: the step test 0x425130 measures the way, the occupants are the walk's own test).</summary>
    public bool FirstStepClear(string id, string toward, bool ignoreTarget)
    {
        if (Tile(id) is not { } from || Tile(toward) is not { } to) return false;
        if (from == to) return true;
        var chart = ToChart(from);
        var direction = RoutePlanner.Toward(chart, ToChart(to));
        return new AreaRouteSource(_scene.Steps, _scene.Chart).Assess(chart, direction) == RouteStepVerdict.Open;
    }

    public AiAttackFacts AttackFacts(string id, string target)
    {
        var line = Line(id, target, out var targetRow);
        var foreign = line?.Blocker is { } blocker && !ReferenceEquals(blocker, targetRow);
        return new(AttackerInactive: !Active(id), TargetGone: Gone(target), TargetDead: DeadCritter(target), ForeignBlocker: foreign,
            NoPathToTarget: foreign && PlanPath(id, new AiPathRequest(AiPathKind.Attack, target, null, 1, false, false)) is null);
    }

    public AiReachFacts Reach(string id, string target)
    {
        var distance = Tile(id) is { } a && Tile(target) is { } b ? AiExecutorRules.Distance(a, b) : -1;
        var values = Values(id);
        var line = Line(id, target, out var targetRow);
        var clear = line is null || LineOfFireRules.ClearShot(line, targetRow!);
        return new(distance, values?.Weapon.Kind == WeaponKind.Weapon ? values.Range : 0, values?.Weapon.Kind == WeaponKind.Weapon, clear);
    }

    public int AttackCost(string id) => id == Pc
        ? TurnBasedRules.AttackCost(_session.QueryHeroAttack()?.WeaponSpeed ?? TurnBasedRules.UnarmedWeaponSpeed)
        : Values(id)?.AttackCost ?? TurnBasedRules.AttackCost(TurnBasedRules.UnarmedWeaponSpeed);

    public AiPath? PlanPath(string id, AiPathRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Tile(id) is not { } from) return null;
        var goal = request.Tile ?? (request.Target is { } target ? Tile(target) : null);
        if (goal is not { } to) return null;
        var source = new AreaRouteSource(_scene.Steps, _scene.Chart, () => Occupancy(id, request.Target));
        RoutePlan plan;
        switch (request.Kind)
        {
            case AiPathKind.Attack: plan = RoutePlanner.PlanApproach(source, ToChart(from), ToChart(to)); break;
            case AiPathKind.Near:
                plan = RoutePlanner.PlanApproach(source, ToChart(from), ToChart(to));
                if (plan.Outcome != RouteOutcome.Found) return null;
                // Within the range of the object: the route up to its first cell there.
                var cells = new List<CellCoord>();
                foreach (var cell in plan.Cells)
                {
                    cells.Add(ToWorld(cell));
                    if (AiExecutorRules.Distance(ToWorld(cell), to) <= request.Range) break;
                }
                return new([.. cells]);
            case AiPathKind.Escape when request.Straight: return Straight(from, to);
            default: plan = RoutePlanner.Plan(source, ToChart(from), ToChart(to)); break;
        }
        return plan.Outcome == RouteOutcome.Found ? new([.. plan.Cells.Select(ToWorld)]) : null;
    }

    // A flight's escape line: the straight line of neighbour steps toward the tile, every step accepted, no search.
    private static AiPath? Straight(CellCoord from, CellCoord to)
    {
        var cells = new List<CellCoord>();
        var at = from;
        for (var i = 0; i < 100 && at != to; i++)
        {
            at = RoutePlanner.Neighbor(at, RoutePlanner.Toward(at, to));
            cells.Add(at);
        }
        return cells.Count == 0 ? null : new([.. cells]);
    }

    public AiStepBlock StepBlocked(string id, CellCoord next, bool flight)
    {
        if (Tile(id) is not { } from) return AiStepBlock.CannotStart;
        if (LivingCritterOn(next, id)) return AiStepBlock.Occupied;
        var chart = ToChart(from);
        if (AiExecutorRules.Distance(from, next) != 1) return AiStepBlock.CannotStart;
        var direction = RoutePlanner.Toward(chart, ToChart(next));
        var open = new AreaRouteSource(_scene.Steps, _scene.Chart).Assess(chart, direction) == RouteStepVerdict.Open;
        return open ? AiStepBlock.Clear : flight ? AiStepBlock.Occupied : AiStepBlock.CannotStart;
    }

    public int BeginStep(string id, CellCoord next, bool running)
    {
        if (!_npcs.TryGetValue(id, out var npc)) return 1;
        var from = Movers.CellOf(id, npc.Origin);
        var gait = running ? MovementGait.Run : MovementGait.Walk;
        var direction = AreaCombatHost.Facing(from, next) ?? NeighborDirection.XPlus;
        var duration = _scene.Timing?.For(gait, direction) ?? DiagnosticGridSession.LiveDuration;
        var ms = (int)Int128.Clamp(duration.Value.Numerator * 1000 / duration.Value.Denominator, 1, 10_000);
        Movers.Step(id, npc.Row.Key, npc.Origin, next, _nowMs, ms);
        _notices.Add(new(AreaAiNoticeKind.Step, id, null));
        return ms;
    }

    public int GetUp(string id) => 1;
    public int Unconceal(string id) => 1;

    public void Face(string id, string toward)
    {
        if (_npcs.TryGetValue(id, out var npc) && Tile(toward) is { } there) Movers.Face(id, npc.Row.Key, npc.Origin, there);
    }

    public void PlaySound(string id, int sound) => _notices.Add(new(AreaAiNoticeKind.Sound, id, null) { Sound = sound });

    /// <summary>
    /// The attempt-attack goal's start: its node 5 (0x42B9C0, read by this task) puts the attacker into combat mode, and a PC target,
    /// or an NPC target's living PC leader, too (0x4B3F50); then the turn and the attack animation (0x432700): the NPC's attack ART
    /// played at its Speed and weapon speed (<see cref="RealTimeCombatRules"/>), whose action frame is the hit.
    /// </summary>
    public AiAttackSwing BeginAttack(string id, string target)
    {
        if (!_npcs.TryGetValue(id, out var npc) || Values(id) is not { } values) return new(1, 1);
        if (NpcCombatMode(id) is { } context) _session.SetCombatMode(_session.Version, SessionCombatant.Critter(id), true, context);
        if (target == Pc || _session.QueryCompanion(target).LedByPc && !(Status(Pc)?.IsDead ?? true)) HeroIntoCombatMode();
        Face(id, target);
        var art = _scene.AttackArt?.Invoke(npc) ?? FixedAttackArt;
        var rate = RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Unarmed, Math.Clamp(values.Speed, 1, RealTimeCombatRules.MaxStat),
            art.RawFramesPerSecond);
        var clip = new AttackClip(art.Frames, art.ActionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, Math.Clamp(values.WeaponSpeed, -RealTimeCombatRules.MaxStat, RealTimeCombatRules.MaxStat)),
            AttackArtWeapon.Unarmed);
        var moments = RealTimeCombatRules.Schedule(clip);
        var hit = moments.FirstOrDefault(m => m.Kind == AttackUpdateKind.Hit);
        var end = moments[^1];
        var action = (int)Math.Max(1, hit.Kind == AttackUpdateKind.Hit ? hit.AtMs : end.AtMs);
        var swing = new AiAttackSwing(action, (int)Math.Max(1, end.AtMs - action));
        _notices.Add(new(AreaAiNoticeKind.Swing, id, target) { Swing = swing });
        return swing;
    }

    /// <summary>The hit step: the session's NPC attack (<see cref="GameSessionOwner.AttackByNpc"/>), then the attack event.</summary>
    public void Hit(string id, string target)
    {
        if (Build(id) is not { } attacker) return;
        var targetBuild = target == Pc ? null : Build(target);
        if (target != Pc && targetBuild is null) return;
        var attack = new SessionNpcAttack(attacker)
        {
            Target = targetBuild,
            Distance = Tile(id) is { } a && Tile(target) is { } b ? AiExecutorRules.Distance(a, b) : 1,
            AttackerWorn = _scene.Combat.WornItems(_session, _scene.RuntimeMap, id),
            TargetWorn = target == Pc ? _scene.HeroWorn?.Invoke() : _scene.Combat.WornItems(_session, _scene.RuntimeMap, target),
            AttackerCell = Tile(id),
            TargetCell = target == Pc ? null : Tile(target),
            DropMap = _scene.DropMap?.Invoke(),
        };
        var result = _session.AttackByNpc(_session.Version, attack);
        Trace(id).Hit = $"{result.Disposition} {result.Failure} {result.Resolution?.Outcome} {result.Life?.HitPointDamage}";
        _notices.Add(new(AreaAiNoticeKind.Hit, id, target) { Result = result });
        if (result.Disposition != GameSessionDisposition.Rejected && result.Resolution?.Refusal == AttackRefusal.None) AttackEvent(id, target);
    }

    public void SetNoFlee(string id)
    {
        if (id != Pc) _session.SetNoFlee(_session.Version, id);
    }

    // ---- the casting goals (ARC-WORLD-102) ----

    /// <summary>The casting goal's start (0x429B50 → 0x429BC0 → 0x42C850) through the cast host (<see cref="SessionSpellCaster.StartGoal"/>):
    /// the 4 action points of a round (their events are the round's), the pre-animation check, the target, then the facing turn, which
    /// <see cref="FaceCast"/> applies.</summary>
    public AiCastGoalStart StartCast(string id, int run)
    {
        _queuedCasts.Remove(run);
        _launched.Remove(run);
        // A queued goal whose run was cancelled before it (QC6) starts without one and ends (QC4).
        if (_scene.Spells?.Invoke() is not { } caster || !OwnRun(id, run)) return AiCastGoalStart.Refused;
        var start = caster.StartGoal(run, _round);
        if (start.ActionPoints is { } points) RoundCall(points);
        Trace(id).Cast = $"start run={run} {start.Outcome}";
        if (start.Outcome is SpellCastGoalOutcome.Started or SpellCastGoalOutcome.CannotTurn) _castFacing[run] = start.Facing;
        return start.Outcome switch
        {
            SpellCastGoalOutcome.Started or SpellCastGoalOutcome.CannotTurn => AiCastGoalStart.Started,
            SpellCastGoalOutcome.TargetGone => AiCastGoalStart.TargetGone,
            _ => AiCastGoalStart.Refused,
        };
    }

    /// <summary>The facing turn (0x42C850, <see cref="SpellFacingRules.Turn"/>) the start computed: a stoned, stunned or paralysed caster
    /// cannot turn; a turn faces the NPC toward its run's target object (its body's facing; NPC ART is not read).</summary>
    public bool FaceCast(string id, int run)
    {
        _castFacing.Remove(run, out var facing);
        if (facing?.Kind == SpellFacingKind.Refused) return false;
        if (facing?.Kind == SpellFacingKind.Turned && _session.QuerySpellRuns()[run]?.Target?.Id is { } target) Face(id, target);
        return true;
    }

    /// <summary>
    /// The play's start (0x429F00, AG4): the target re-check (0x456A90, <see cref="SessionSpellCaster.TargetStillValid"/>), then the
    /// caster's cast eye candy (6 · spell, <see cref="AreaAiScene.CastEyeCandy"/>), whose release frame and last frame time the action
    /// frame and the rest (<see cref="CastEyeCandyAnimation"/>, played through with a passing release). A failed re-check, goal 83's
    /// casting animation (NPC ART is not read) or no eye candy: no play, so the release runs at once. A play whose action frame is never
    /// reached (EC7) answers its whole length, and its action frame then releases nothing.
    /// </summary>
    public AiCastSwing BeginCast(string id, int run, bool castAnimation)
    {
        if (_scene.Spells?.Invoke() is not { } caster || _session.QuerySpellRuns()[run] is not { } r) return new(false);
        if (!caster.TargetStillValid(r) || castAnimation || _scene.CastEyeCandy?.Invoke(id, r.Spell) is not { Immediate: false } play)
        {
            _notices.Add(new(AreaAiNoticeKind.Cast, id, r.Target?.Id) { Spell = r.Spell, Run = run, CastSwing = new(false) });
            return new(false);
        }
        // ARC-WORLD-105: a release that launches a missile restarts the goal one pace later (0x4246E0, SM1), as the hero's does.
        play.PaceAfterRelease = _scene.MissileArt?.Invoke(r.Spell) == true;
        long? release = null;
        var end = play.StartMs;
        foreach (var step in play.Advance(long.MaxValue, () => true))
        {
            if (step.Kind == EyeCandyStepKind.Release) release ??= step.AtMs;
            end = Math.Max(end, step.AtMs);
        }
        var swing = release is { } at
            ? new AiCastSwing(true, (int)Math.Clamp(at - play.StartMs, 1, int.MaxValue), (int)Math.Clamp(end - at, 0, int.MaxValue))
            : new AiCastSwing(true, (int)Math.Clamp(end - play.StartMs, 1, int.MaxValue));
        if (release is null) _noRelease.Add(run);
        _notices.Add(new(AreaAiNoticeKind.Cast, id, r.Target?.Id) { Spell = r.Spell, Run = run, CastSwing = swing });
        return swing;
    }

    /// <summary>The play's action frame (0x429C40): a play that never reaches it releases nothing; a spell whose projectile eye candy has
    /// art launches its missile (<see cref="LaunchCast"/>); any other is released (<see cref="ReleaseCast"/>).</summary>
    public bool CastActionFrame(string id, int run)
    {
        if (_noRelease.Remove(run)) return false;
        return _session.QuerySpellRuns()[run] is { } r && _scene.MissileArt?.Invoke(r.Spell) == true ? LaunchCast(id, run, r) : ReleaseCast(id, run);
    }

    /// <summary>ARC-WORLD-105: the launch (0x429C40, AC8, <see cref="SessionSpellCaster.LaunchMissile"/>): the release gate marks the
    /// frame handled, so the goal's end frees nothing, and no re-check or BEGIN runs now; the viewer flies the missile from the NPC's
    /// tile (<see cref="AreaAiNoticeKind.Missile"/>, goal 28), whose arrival begins the run and whose loss frees it. False when the run
    /// was released or begun before: the goal then ends.</summary>
    private bool LaunchCast(string id, int run, SpellRun r)
    {
        if (_scene.Spells?.Invoke() is not { } caster) return false;
        var launched = caster.LaunchMissile(run);
        Trace(id).Cast = $"missile run={run} {(launched ? "launched" : "not launched")}";
        if (!launched) return false;
        _launched[run] = caster;
        _notices.Add(new(AreaAiNoticeKind.Missile, id, r.Target?.Id) { Spell = r.Spell, Run = run, Caster = caster });
        return true;
    }

    /// <summary>The release (0x429C80, <see cref="SessionSpellCaster.Release"/>): the target re-check and the run's BEGIN; false when the
    /// re-check refused it (the original keeps the run, MG41; Steamwoven frees it) or the frame was released before.</summary>
    public bool ReleaseCast(string id, int run)
    {
        _noRelease.Remove(run);
        if (_scene.Spells?.Invoke() is not { } caster) return false;
        var spell = _session.QuerySpellRuns()[run]?.Spell ?? -1;
        var begun = caster.Release(run);
        var released = begun is not null && begun.Disposition != GameSessionDisposition.Rejected;
        Trace(id).Cast = $"release run={run} {(begun is null ? "re-check failed" : $"{begun.Disposition} {begun.Failure}")}";
        _notices.Add(new(AreaAiNoticeKind.Release, id, null) { Spell = spell, Run = run, Released = released, SpellRun = begun });
        return released;
    }

    /// <summary>The casting goal's end (0x429CD0, <see cref="SessionSpellCaster.EndGoal"/>): before the release frame the run is freed
    /// with its BEGIN charged (MG39: refused at the start, Steamwoven frees it unpaid); after it nothing. ARC-WORLD-105: a launched
    /// run's goal ends at the host that launched it, which knows the handled frame (nothing is freed: the run is its missile's).</summary>
    public void EndCast(string id, int run, bool refused)
    {
        _noRelease.Remove(run);
        _castFacing.Remove(run);
        var launcher = _launched.Remove(run, out var launching) ? launching : null;
        if ((launcher ?? _scene.Spells?.Invoke()) is not { } caster) return;
        var freed = OwnRun(id, run) ? caster.EndGoal(run, refused) : null;
        _notices.Add(new(AreaAiNoticeKind.CastEnd, id, null) { Run = run, SpellRun = freed });
    }
    public bool FaceForAction(string id, string? target, CellCoord? tile) => false;
    public AiActionSwing BeginAction(string id, AiActionAnimation animation) => new(AiActionStart.CannotStart);
    public bool AttemptSkill(string id, string target, int skill, string? item) => false;
    public void AttemptPickLocks(string id, string target, string? item) { }
    public bool ThrowItem(string id, string item, CellCoord tile) => false;
    public void PickUp(string id, string item) { }
    public string? ItemHolder(string item) => null;
    public AiUsedItem UsedItem(string user, string item) => new();
    public bool RunUseScript(string user, string item, string? target) => true;
    public void OpenTargeting(string user, string item) { }
    public void UseTrapDevice(string user, string item, string? target) { }
    public void ReadWritten(string user, string item) { }
    public bool Lockable(string target) => false;
    public void StartSkillWithItem(string user, string item, string target, int skill) { }
    public void UseItemMagic(string user, string item, string? target) { }
    public void SpendItem(string item) { }

    public bool InRound => _session.QueryCombatRound() is not null;
    public string? RoundActor => _session.QueryCombatRound() is { } round ? Id(round.Actor) : null;
    public int ActionPoints(string id) => _session.QueryCombatRound() is { } round && Id(round.Actor) == id ? round.ActionPoints : 0;
    /// <summary>The round's pending cost (0x5FC244): what the last action-point check of this subturn wrote, 0 once a subturn started
    /// since (the subturn start's check of cost 0, 0x4B75D0, ARC-WORLD-095), whichever call began it.</summary>
    public int PendingCost
    {
        get => _pendingAt == _session.QueryCombatSubturnStarts() ? _pendingCost : 0;
        set { _pendingCost = value; _pendingAt = _session.QueryCombatSubturnStarts(); }
    }
    public void Consume(string id, int cost)
    {
        if (Combatant(id) is { } combatant) RoundCall(_session.ConsumeActionPoints(_session.Version, combatant, cost, _round));
    }
    public void EndSubturn(string id)
    {
        if (Combatant(id) is { } combatant) RoundCall(_session.EndCombatTurn(_session.Version, combatant, _round));
    }

    private void RoundCall(SessionCombatResult result)
    {
        _roundEvents.AddRange(result.Events);
        SubturnsEnded(result.Events);
    }

    // ---- perform's world (IAiPerformWorld) ----

    /// <summary>Active (0x45D790): not dead, unconscious, stunned, paralysed or stoned.</summary>
    public bool Active(string id)
    {
        if (Status(id) is not { } status || status.IsDead || status.IsUnconscious) return false;
        if (id == Pc) return status.IsActive;
        return Build(id) is { } build && !build.Facts.Traits.HasFlag(LifeTraits.Stoned) && !build.Facts.Traits.HasFlag(LifeTraits.Paralyzed)
            && !(LifeState(id)?.Conditions.HasFlag(LifeConditions.Stunned) ?? false);
    }

    public DangerState ReadDanger(string npc) => _session.QueryDanger(npc).State;
    public DangerFlags DangerBits(string npc) => _session.QueryDanger(npc).Flags;
    public BackOffRun BackOff(string npc, string? target) => MoraleRules.BackOff(this, npc, target ?? Pc, DangerBits(npc));
    // The back-off's away path (0x41F3C0 along the offsets) is not walked yet: no tile, so the back-off ends.
    public CellCoord? AwayTile(string npc, string? target, int range) => null;
    public void EndBackOff(string npc) => _session.EndBackOff(_session.Version, npc);
    /// <summary>
    /// Perform's cast (0x4AC250 → 0x455AC0 → 0x455C30): the cast host's <see cref="SessionSpellCaster.CastByAi"/> with the choice's
    /// spell and target; a started run's casting goal is 82 when the record's casting animation is not −1, else 26. ARC-WORLD-104
    /// (SU13, AC5): when the NPC's run has goal 26 or 27 at its bottom (0x44E830; 82 and 83 are not found) the new goal is queued
    /// beneath it (<see cref="SessionSpellCaster.Queue"/>, the run's current index as its depth): inserted at the bottom of the run,
    /// so the queued goals play in the order they were cast, each when the goals above it have popped. A depth of 4 or more or a
    /// turn-based round refuses, freeing the run. Either way no run starts and perform keeps the current one.
    /// NOT READY: a choice with an item (the item's spells and AI actions are not listed yet), and a cast while goal 82 or 83 is at
    /// the run's bottom, which the original starts as a second run beside it (AC5; the host holds one run per NPC): neither starts a run.
    /// </summary>
    public AiCastStart Cast(string npc, AiPassChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Item is not null || _scene.Spells?.Invoke() is not { } caster) return new(null);
        if (RunOf(npc)?.Bottom.Goal is AiGoalType.CastSpellWithAnimation or AiGoalType.AttemptSpellWithAnimation)
        {
            Trace(npc).Cast = $"cast {choice.Spell} not started (goal 82 or 83 plays: a second run)";
            return new(null);
        }
        var attempt = caster.CastByAi(npc, choice.Spell, choice.Target);
        Trace(npc).Cast = $"cast {choice.Spell} at {choice.Target ?? "-"} {attempt.Outcome} {attempt.Run?.Failure} {attempt.Run?.Creation?.Check?.Refusal}";
        if (attempt.RunId is not { } run) return new(null);
        var castAnimation = (caster.SpellRecord(choice.Spell)?.Run.CastAnimation ?? -1) != -1;
        if (!_runs.TryGetValue(npc, out var held) || held.Run.Bottom.Goal is not (AiGoalType.CastSpell or AiGoalType.AttemptSpell))
            return new(run, CastAnimation: castAnimation);
        var depth = held.Run.Frames.Length - 1;
        var queued = caster.Queue(run, depth);
        Trace(npc).Cast += $" queue depth={depth} {queued}";
        if (queued == SpellCastQueueOutcome.Queued)
        {
            // The perform's target, as a started goal's; a queued goal whose run was freed meanwhile (QC6) lost its slot to this one.
            var target = choice.Target ?? (_session.QueryDanger(npc).Source is { } source ? Id(source) : null);
            var goal = new AiGoalFrame(castAnimation ? AiGoalType.CastSpellWithAnimation : AiGoalType.CastSpell, target) { SpellRun = run };
            var frames = held.Run.Frames.RemoveAll(f => !ReferenceEquals(f, held.Run.Top) && f.SpellRun == run
                && f.Goal is AiGoalType.CastSpell or AiGoalType.CastSpellWithAnimation).Insert(0, goal);
            _runs[npc] = (held.Run with { Frames = frames }, held.DueMs);
            _queuedCasts.Add(run);
        }
        return new(null, castAnimation, Queued: queued == SpellCastQueueOutcome.Queued);
    }

    public void LeaveCombatMode(string npc)
    {
        if (NpcCombatMode(npc) is { } context) _session.SetCombatMode(_session.Version, SessionCombatant.Critter(npc), false, context);
    }

    public string? Leader(string npc) => npc != Pc && _session.QueryCompanion(npc).LedByPc ? Pc : null;
    public bool Waiting(string npc) => _session.QueryCompanion(npc).Standing == CompanionStanding.Waiting;
    public void LoyaltyCheck(string npc) { }
    public void RunRoutine(string npc) { }

    public bool StartBackOff(string npc, string? target) =>
        target is not null && MoraleRules.StartsBackOff(this, npc, target, _session.AiRolls)
        && _session.StartBackOff(_session.Version, npc).Disposition == GameSessionDisposition.Changed;

    public AiGrenadeResult Grenade(string npc, string target) => new(AiGrenadeStop.NoGrenade);
    public string? ThrowableWeapon(string npc) => null;
    public void Unwield(string npc, string item) { }
    public bool HasSkillArt(string npc) => false;
    public bool NpcPickupsOn => false;
    public bool BoomerangInFlight(string npc) => false;
    public bool InterruptLowerAnimations(string id) => true;
    public void StepApart(string id) { }

    public AiFleeing PerformFleeing(string npc)
    {
        var result = _session.PerformFleeing(_session.Version, new SessionPerformFleeingRequest
        {
            Entity = npc, Facts = SetterFacts(npc), CombatMode = NpcCombatMode(npc),
        });
        var source = _session.QueryDanger(npc).FleeingFrom is { } from ? Id(from) : null;
        return new(result.StartsFleeGoal, source);
    }

    public void PerformSurrender(string npc)
    {
        if (MoraleRules.SurrenderEnds(this, npc)) SetDanger(npc, DangerState.None, null);
    }

    // ---- the support rules' world, the spell half (IAiSupportWorld, ARC-WORLD-102) ----

    /// <summary>The support facts: MECHANICAL (critter flag 0x20000000), CAST_HIGHEST (NPC flag 0x40000), the fatigue percent (0x4AB430:
    /// 100 × current / maximum, toward zero; 0 for a maximum of 0, where the original divides by zero), the current fatigue, the
    /// poison level, the effective willpower and the magick points (an NPC's session record when it has one, else its effective stat).</summary>
    public AiSupportCritter Support(string id)
    {
        var status = Status(id);
        var fatigue = status is { MaxFatigue: not 0 } s ? (int)(100L * s.Fatigue / s.MaxFatigue) : 0;
        if (id == Pc)
        {
            // The hero is a healer only in the heal step (not run yet): its stats without the poison's effect, as the setter's facts read them.
            var stats = _session.QueryHeroStats(new(0))?.Snapshot;
            return new AiSupportCritter
            {
                FatiguePercent = fatigue, CurrentFatigue = status?.Fatigue ?? 0, PoisonLevel = _session.QueryHeroLife()?.Record.State.PoisonLevel ?? 0,
                Willpower = stats?.GetEffective(CharacterStat.Willpower) ?? 0, MagickPoints = stats?.GetEffective(CharacterStat.MagickPoints) ?? 0,
            };
        }
        if (Fields(id) is not { Critter: { } critter, Npc: { } npc } || Build(id) is not { } build) return new AiSupportCritter();
        var state = LifeState(id) ?? build.Loaded;
        return new AiSupportCritter
        {
            Mechanical = (critter.Flags & MechanicalFlag) != 0, CastHighest = (npc.Flags & CastHighestNpc) != 0,
            FatiguePercent = fatigue, CurrentFatigue = status?.Fatigue ?? 0, PoisonLevel = state.PoisonLevel,
            Willpower = build.EffectiveStat(CharacterStat.Willpower, state),
            MagickPoints = _session.QueryNpcAdvancement(id)?.Sheet.MagickPoints ?? build.EffectiveStat(CharacterStat.MagickPoints, state),
        };
    }

    /// <summary>The AI parameters 13 (the defensive list's skip), 14 (the combat heal chance) and 15 (the combat distance); out of the
    /// table 0.</summary>
    public AiSupportParameters SupportParameters(string npc) => Row(npc) is { } row
        ? new(row.DefensiveSpellRoll, row.CombatHealChance, row.CombatDistance) : default;

    public bool NpcFighting => true;
    public AiGlobalLists GlobalLists => _scene.Spells?.Invoke()?.AiLists ?? NoLists;

    /// <summary>The list request (0x4CCA90) through the host's cache (<see cref="AiListCache.Request"/>: the Original setting's frozen
    /// first list, SP11; built fresh in Steamwoven). The cache is not saved: a load or a map change builds the host again.</summary>
    public ImmutableArray<AiListEntry> List(string critter, AiActionList list)
    {
        var request = _listCache.Request(this, critter, list, _session.Rules.Combat);
        _listCache = request.Cache;
        return request.Entries;
    }

    // The NPCs' items are not listed yet (item use and goal 40 are their own slice): no hexed weapon, no inventory, no item entry.
    public string? HexedWeapon(string critter) => null;
    public IReadOnlyList<string> Inventory(string critter) => [];
    public AiListItem Item(string item) => new();
    public bool CanUseItem(string critter, string item) => false;
    public int ItemFailChance(string critter, string item, string target, int spell) => 0;
    public string? HealingItem(string npc) => null;
    public bool HealSkillAccepts(string npc, string item, string target) => false;

    public bool Knows(string critter, int spell) => _scene.Spells?.Invoke()?.AiKnows(critter, spell) ?? false;
    public AiSpellFacts Spell(int spell) => _scene.Spells?.Invoke()?.AiSpell(spell) ?? default;
    public int CollegeLevel(string critter, int college) => _scene.Spells?.Invoke()?.AiCollegeLevel(critter, college) ?? 0;
    public int CastCost(string critter, int spell) => _scene.Spells?.Invoke()?.AiCastCost(critter, spell) ?? 0;
    public int FreeMaintainSlots(string critter) => _scene.Spells?.Invoke()?.AiFreeMaintainSlots(critter) ?? 0;
    public bool UnderInfluence(string target, int spell) => _scene.Spells?.Invoke()?.AiUnderInfluence(target, spell) ?? false;
    public int FailChance(string critter, string target, int spell) => _scene.Spells?.Invoke()?.AiFailChance(critter, target, spell) ?? 0;

    /// <summary>The line of fire (0x4ADE00) from the critter's tile to the target's: its cost and the first object that stops it (the
    /// target's own id for its own row), as the attack's reach reads it; no map: cost 0, nothing in the way.</summary>
    public AiLineOfFire LineOfFire(string critter, string target)
    {
        if (Line(critter, target, out var targetRow) is not { } line) return new(0, null);
        var blocker = line.Blocker is not { } row ? null
            : ReferenceEquals(row, targetRow) ? target
            : _scene.Combat.Entity(_scene.RuntimeMap, row.Key) ?? row.Key.ToString();
        return new(line.Cost, blocker);
    }

    // ---- the hostility and morale world (IMoraleWorld) ----

    public HostilityCritter? Find(string id)
    {
        if (id == Pc)
        {
            if (_session.QueryHeroLife() is not { } hero) return null;
            return new HostilityCritter
            {
                Id = Pc, Kind = HostilityKind.Pc, Dead = hero.Status.IsDead, Unconscious = hero.Status.IsUnconscious,
                Stunned = !hero.Status.IsActive && !hero.Status.IsDead && !hero.Status.IsUnconscious,
                InCombatMode = _session.QueryCombatMode(SessionCombatant.Hero), Level = _session.QueryHero()?.Level ?? 0,
                HealthPercent = Percent(hero.Status),
                Perception = _session.QueryHeroAttack()?.Combatant.Perception ?? 0,
            };
        }
        if (!_npcs.ContainsKey(id) && CompanionCell(id) is null) return null;
        if (Fields(id) is not { Critter: { } critter, Npc: { } npc } fields || Build(id) is not { } build) return null;
        var status = Status(id);
        var state = LifeState(id) ?? build.Loaded;
        var danger = _session.QueryDanger(id);
        var leader = Leader(id);
        return new HostilityCritter
        {
            Id = id, Kind = HostilityKind.Npc,
            Destroyed = Gone(id), DontDraw = (fields.Object.Flags & DontDraw) != 0, ObjectInvisible = (fields.Object.Flags & ObjectInvisible) != 0,
            Invulnerable = (fields.Object.Flags & Invulnerable) != 0, NighInvulnerable = (critter.Flags2 & NighInvulnerable2) != 0,
            Dead = status?.IsDead ?? true, Unconscious = status?.IsUnconscious ?? false, Asleep = (critter.Flags & SleepingFlag) != 0,
            NonLethal = (critter.Flags & NonLethal) != 0, Undead = (critter.Flags & Undead) != 0, Animal = (critter.Flags & Animal) != 0,
            AnimalEnshrouded = (critter.Flags & AnimalEnshrouded) != 0, Monstrous = build.Monstrous,
            // The NO_FLEE the AI set (session bit, ARC-WORLD-086) is ORed into the critter's stored flag.
            NoFlee = (critter.Flags & NoFleeFlag) != 0 || danger.Flags.HasFlag(DangerFlags.NoFlee),
            InvisibleSpell = (fields.Object.SpellFlags & InvisibleSpell) != 0, DetectsInvisible = (fields.Object.SpellFlags & DetectsInvisibleSpell) != 0,
            Stoned = (fields.Object.SpellFlags & StonedSpell) != 0, Stunned = state.Conditions.HasFlag(LifeConditions.Stunned) || build.Facts.Traits.HasFlag(LifeTraits.Paralyzed),
            MindControlled = (fields.Object.SpellFlags & MindControlledSpell) != 0, Enshrouded = (fields.Object.SpellFlags & EnshroudedSpell) != 0,
            AttemptingAttack = RunOf(id)?.Top.Goal is AiGoalType.AttemptAttack or AiGoalType.AttemptSpell or AiGoalType.AttemptSpellWithAnimation,
            InCombatMode = _session.QueryCombatMode(SessionCombatant.Critter(id)),
            Guard = npc.SocialClass == GuardClass, WaitingHere = (npc.Flags & WaitingHereNpc) != 0 || Waiting(id),
            KillOnSight = (npc.Flags & KillOnSightNpc) != 0, KosOverride = (npc.Flags & KosOverrideNpc) != 0,
            Perception = build.EffectiveStat(CharacterStat.Perception, state), Intelligence = build.EffectiveStat(CharacterStat.Intelligence, state),
            Level = build.Level, Alignment = critter.BaseStats.Count > AreaCompanionData.AlignmentStat ? critter.BaseStats[AreaCompanionData.AlignmentStat] : 0,
            HealthPercent = status is null ? 0 : Percent(status), Leader = leader, PcLeader = leader,
        };
    }

    private static int Percent(LifeStatus status) => status.MaxHitPoints <= 0 ? 0 : (int)(100L * status.HitPoints / status.MaxHitPoints);

    public int Distance(string from, string to) => Tile(from) is { } a && Tile(to) is { } b ? AiExecutorRules.Distance(a, b) : int.MaxValue;

    /// <summary>One faction (0x45D110) as far as this host knows: the hero and its followers, followers of one leader, or one non-zero
    /// faction; the PC's reputations granting a faction are not read here.</summary>
    public bool FactionSame(string a, string b)
    {
        if (a == b) return a != Pc;
        if (a == Pc || b == Pc) return Leader(a == Pc ? b : a) == Pc;
        if (Leader(a) is { } la && la == Leader(b)) return true;
        return Fields(a)?.Npc?.Faction is { } fa && fa != 0 && fa == Fields(b)?.Npc?.Faction;
    }

    public int HostilityReaction(string npc, string pc)
    {
        if (ReactionFacts(npc) is not { } facts) return 0;
        var query = _session.QueryReaction(npc, facts.Npc, facts.Pc, ReactionQuery.Hostility);
        return query.View?.Effective ?? 0;
    }

    public bool AllowsKillOnSight(string npc, string candidate) => true;

    public HostilityParameters Parameters(string npc) => Row(npc) is { } row
        ? new(row.FleeDistance, row.HostileReaction, row.HostileAlignmentGap, row.GoodFollowerStop) : default;

    public MoraleParameters MoraleParameters(string npc) => Row(npc) is { } row
        ? new(row.FleeHpPercent, row.FleeOutnumbered, row.FleeOutlevelled, row.StandWhileTargetWeakPercent, row.CombatDistance) : default;

    public IReadOnlyList<string> Followers(string id) =>
        id == Pc ? _session.QueryParty().Followers.Select(f => f.Entity).ToArray() : [];

    public AwarenessResult See(string observer, string target)
    {
        if (Tile(observer) is not { } at || Tile(target) is not { } there || _scene.LineOfFire?.Invoke() is not { } map) return new(AwarenessRules.NotPerceived, AwarenessReason.State);
        var facts = Find(observer);
        var sight = new SightFacts
        {
            Perception = Math.Clamp(facts?.Perception ?? 0, 0, AwarenessRules.MaxPerception),
            Rotation = DiagnosticGridActor.Octant(Movers.FacingOf(observer, NeighborDirection.XPlus)),
            Stunned = facts?.Stunned ?? false, Sleeping = facts?.Asleep ?? false, Unconscious = facts?.Unconscious ?? false,
            TargetInvisible = Find(target) is { } seen && (seen.ObjectInvisible || seen.InvisibleSpell), DetectsInvisible = facts?.DetectsInvisible ?? false,
        };
        return AwarenessRules.See(sight, at, 0, 0, there, map, _session.Rules.Combat);
    }

    public AwarenessResult Hear(string observer, string target, Loudness loudness)
    {
        if (Tile(observer) is not { } at || Tile(target) is not { } there || _scene.LineOfFire?.Invoke() is not { } map) return new(AwarenessRules.NotPerceived, AwarenessReason.State);
        var facts = Find(observer);
        var hearing = new HearingFacts
        {
            Perception = Math.Clamp(facts?.Perception ?? 0, 0, AwarenessRules.MaxPerception),
            Stunned = facts?.Stunned ?? false, Sleeping = facts?.Asleep ?? false, Unconscious = facts?.Unconscious ?? false,
        };
        return AwarenessRules.Hear(hearing, at, 0, 0, there, loudness, new SoundMap(map), _session.Rules.Combat);
    }

    public HostilityDanger Danger(string npc)
    {
        var record = _session.QueryDanger(npc);
        return new(record.State, record.Source is { } source ? Id(source) : null, record.Focus is { } focus ? Id(focus) : null,
            record.HitBy is { } hit ? Id(hit) : null, record.Enemies.Select(Id).ToArray());
    }

    public string? MindController(string npc) => null;

    /// <summary>The critters of the square of half-size <paramref name="halfSize"/> around the object, in the map's scan order.</summary>
    public IReadOnlyList<string> Square(string npc, int halfSize)
    {
        if (Tile(npc) is not { } center) return [];
        bool Inside(CellCoord cell) => Math.Abs((long)cell.X - center.X) <= halfSize && Math.Abs((long)cell.Y - center.Y) <= halfSize;
        var critters = new List<AreaScanCritter>();
        if (Tile(Pc) is { } hero && Inside(hero)) critters.Add(new(SessionCombatant.Hero, hero, 0, null));
        foreach (var other in _scene.Npcs)
            if (Tile(other.Entity) is { } cell && Inside(cell)) critters.Add(new(SessionCombatant.Critter(other.Entity), cell, 0, other.Row.Key));
        return AreaCombatScan.Order(critters).Select(c => Id(c.Combatant)).ToArray();
    }

    // ---- the event world's questions (forwarded) ----

    public LifeFacts? CritterFacts(SessionMapId map, string entity) => _events.CritterFacts(map, entity);
    public bool ObjectExists(SessionMapId map, string entity) => _events.ObjectExists(map, entity);
    public bool RunScriptCall(SessionScriptCall call) => _events.RunScriptCall(call);
    public ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => (_events as ISessionPartyEventWorld)?.ReactionNpcFacts(map, entity);
    public ReactionPcFacts? ReactionPcFacts() => (_events as ISessionPartyEventWorld)?.ReactionPcFacts();
    public bool LootWindowShowsContainer => (_events as ISessionLootWindowWorld)?.LootWindowShowsContainer == true;
    public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => _round.ScanCombatants(map);
    public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => _round.CombatantFacts(map, entity);

    // ---- helpers ----

    private AreaAiTrace Trace(string npc)
    {
        if (!_traces.TryGetValue(npc, out var trace)) _traces[npc] = trace = new();
        return trace;
    }

    private static string Id(SessionCombatant combatant) => combatant.Entity ?? Pc;

    private SessionCombatant? Combatant(string id) => id == Pc ? SessionCombatant.Hero : SessionCombatant.Critter(id);

    private AreaObjectKey? Key(string id) => _npcs.TryGetValue(id, out var npc) ? npc.Row.Key : null;

    private CombatFields? Fields(string id) => id == Pc ? null : _scene.Combat.Fields(_scene.RuntimeMap, id);

    private SessionCritterCombat? Build(string id) => id == Pc ? null : _scene.Combat.Build(_scene.RuntimeMap, id).Combat;

    private LifeState? LifeState(string id) => Build(id) is { } build ? _session.QueryLife(build.Target).View?.Record.State : null;

    private LifeStatus? Status(string id) => id == Pc ? _session.QueryHeroLife()?.Status
        : Build(id) is { } build ? _session.QueryLife(build.Target).View?.Status : null;

    private (SessionCombatant Who, SessionLifeTarget Life)? Life(string id) =>
        Build(id) is { } build && Status(id) is { IsDead: false } ? (SessionCombatant.Critter(id), build.Target) : null;

    private SessionNpcAttackValues? Values(string id)
    {
        if (Build(id) is not { } build || Status(id) is not { } status || LifeState(id) is not { } state) return null;
        try { return build.AttackValues(status, state, _session.Rules.Combat, _scene.Combat.WornItems(_session, _scene.RuntimeMap, id)?.Keys); }
        catch (ArgumentException) { return null; }
    }

    private AiParameterRow? Row(string npc) =>
        _scene.Parameters is { } table && Fields(npc)?.Npc is { } fields && table.Lookup(fields) is { Status: AiParameterLookupStatus.Found, Row: { } row } ? row : null;

    // The setter's facts of the NPC: invulnerable (or nigh-invulnerable), NO_FLEE (the stored flag ORed with the session bit), the
    // leader's extraordinary charisma.
    private DangerSetterFacts SetterFacts(string npc)
    {
        var critter = Find(npc);
        var charisma = Leader(npc) == Pc && _session.QueryHeroStats(new(0))?.Snapshot?.IsExtraordinary(CharacterStat.Charisma) == true;
        return new(Invulnerable: critter is { Invulnerable: true } or { NighInvulnerable: true }, NoFlee: critter?.NoFlee ?? false, LeaderExtraordinaryCharisma: charisma);
    }

    private (ReactionNpcFacts Npc, ReactionPcFacts Pc)? ReactionFacts(string npc)
    {
        if (Fields(npc) is not { Critter: { } critter, Npc: { } fields } || Build(npc) is not { } build) return null;
        if (_session.QueryHeroReactionFacts(0, fields.Faction) is not { } pc) return null;
        return (new ReactionNpcFacts(fields.ReactionBase, build.Race, (fields.Flags & AloofNpc) != 0, build.Monstrous), pc);
    }

    // The NPC's combat-mode facts (the toggle's refusals read them), or null when it has none.
    private SessionCombatModeContext? NpcCombatMode(string npc)
    {
        if (Build(npc) is not { } build || Fields(npc) is not { } fields || LifeState(npc) is not { } state) return null;
        return new SessionCombatModeContext { Facts = AreaCombatWorld.Facts(build, fields, state) };
    }

    private void HeroIntoCombatMode()
    {
        if (_session.QueryHero() is null || _session.QueryCombatMode(SessionCombatant.Hero)) return;
        _session.SetCombatMode(_session.Version, SessionCombatant.Hero, true, _scene.HeroCombatMode?.Invoke() ?? SessionCombatModeContext.RealTime);
    }

    private CellCoord? CompanionCell(string id)
    {
        foreach (var (key, cell) in _scene.Companions?.Invoke() ?? [])
            if (_scene.Combat.Entity(_scene.RuntimeMap, key) == id) return cell;
        return null;
    }

    // A living critter (the hero, a companion, an NPC of the map) other than the mover on the cell.
    private bool LivingCritterOn(CellCoord cell, string mover)
    {
        if (mover != Pc && Tile(Pc) == cell && !(Status(Pc)?.IsDead ?? true)) return true;
        foreach (var npc in _scene.Npcs)
            if (npc.Entity != mover && Tile(npc.Entity) == cell && !(Status(npc.Entity)?.IsDead ?? true)) return true;
        return _scene.Companions?.Invoke().Any(c => c.Cell == cell && _scene.Combat.Entity(_scene.RuntimeMap, c.Key) != mover) == true;
    }

    // The planner's overlay for the mover: every moved T0 record, and the live cells of the hero, the companions and the other movers
    // (chart cells); the target's own cell does not block a path into it.
    private AreaOccupancy Occupancy(string mover, string? target)
    {
        var moved = new List<AreaObjectKey>(Movers.Moved);
        var held = new List<CellCoord>();
        if (mover != Pc && target != Pc && Tile(Pc) is { } hero) held.Add(ToChart(hero));
        foreach (var other in Movers.All)
            if (other.Entity != mover && other.Entity != target) held.AddRange(new[] { other.Cell }.Concat(other.From is { } f ? [f] : []).Select(ToChart));
        foreach (var (key, cell) in _scene.Companions?.Invoke() ?? [])
        {
            moved.Add(key);
            if (_scene.Combat.Entity(_scene.RuntimeMap, key) is not { } entity || (entity != mover && entity != target)) held.Add(ToChart(cell));
        }
        if (target is not null && Key(target) is { } targetKey) moved.Add(targetKey);
        var occupancy = new AreaOccupancy(moved, held);
        return _scene.Removed?.Invoke() is { Count: > 0 } removed ? occupancy.WithRemoved(removed) : occupancy;
    }

    // The line of fire from the object's tile to the target's, and the target's row (null for the hero).
    private LineOfFireTrace<AreaObjectRecord>? Line(string id, string target, out AreaObjectRecord? targetRow)
    {
        targetRow = _npcs.TryGetValue(target, out var npc) ? npc.Row : null;
        if (Tile(id) is not { } from || Tile(target) is not { } to || _scene.LineOfFire?.Invoke() is not { } map) return null;
        return LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat);
    }

    private CellCoord ToChart(CellCoord world) => _scene.Chart.ToChart(new AreaStepCell(world.X, world.Y));

    private CellCoord ToWorld(CellCoord chart)
    {
        var world = _scene.Chart.ToWorld(chart);
        return new(checked((int)world.X), checked((int)world.Y));
    }

    // The line of fire as a hearing map: no tile is soundproof here (not read yet).
    private sealed class SoundMap(ILineOfFireMap<AreaObjectRecord> map) : IAwarenessMap<AreaObjectRecord>
    {
        public CellCoord? Step(CellCoord from, int rotation) => map.Step(from, rotation);
        public bool Contains(CellCoord tile) => map.Contains(tile);
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => map.Edge(tile, edge);
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => map.Objects(tile);
        public bool Soundproof(CellCoord tile) => false;
    }
}

/// <summary>ARC-WORLD-092: what the AI overlay shows of one NPC: its last danger update, setter call, perform, run step and reason,
/// hit and attack event.</summary>
public sealed class AreaAiTrace
{
    public string? Danger { get; set; }
    public string? Setter { get; set; }
    public string? Perform { get; set; }
    public string? Step { get; set; }
    public AiExecutorReason Reason { get; set; }
    public string? Hit { get; set; }
    public string? Event { get; set; }
    public string? Reaction { get; set; }
    /// <summary>ARC-WORLD-099: the end-combat scripts (point 16) its subturn ends asked.</summary>
    public int EndCombatScripts { get; set; }
    /// <summary>ARC-WORLD-102: its last spell choice and its last cast call (perform's cast, a casting goal's start or release).</summary>
    public string? Spell { get; set; }
    public string? Cast { get; set; }
}
