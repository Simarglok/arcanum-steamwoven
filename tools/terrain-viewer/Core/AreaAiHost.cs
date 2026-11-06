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
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.World;

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
    /// <summary>ARC-DEV-118 (MIG9 S5b, K1 §3.5): the host's chart over the loaded sectors (<c>LoadedSectorChart.Steps</c>), read at each question: while it
    /// answers a snapshot the planners and the steps read it instead of <see cref="Steps"/>, so a cell of a sector the session's cache has not loaded is
    /// outside the evidence; null (the provider, or its answer before a first sector is read): <see cref="Steps"/>. A whole map leaves it null.</summary>
    public Func<AreaStepSnapshot?>? LoadedSteps { get; init; }
    /// <summary>ARC-DEV-118: told after the host touched sectors in the session's cache (a planner reading a goal in an unloaded sector, RL11), so the
    /// chart reads what entered the cache.</summary>
    public Action? SectorsTouched { get; init; }
    /// <summary>The NPCs the host runs (companions are left out: ARC-WORLD-115 runs the hero's through <see cref="Followers"/>).</summary>
    public required IReadOnlyList<AreaAiNpc> Npcs { get; init; }
    public required AreaCombatHost Combat { get; init; }
    /// <summary>ARC-DEV-137 (MIG13 S6): the ART of an NPC's animation (frames and rate) for the critter-state goals' timing; null (the provider, or its answer) when the header does not read or
    /// the viewer cannot read that animation (the stun's 23 is not read yet: the stun then ends at once, GT26).</summary>
    public Func<string, int, AiAnimationClip?>? AnimationClip { get; init; }
    /// <summary>The hero's world cell now, or null without a hero on this map.</summary>
    public required Func<CellCoord?> HeroCell { get; init; }
    /// <summary>ARC-DEV-116 (MIG11 S5): whether the map list holds a map (the destination of an NPC's change of place, the original's map list lookup);
    /// null: only the shown map is listed.</summary>
    public Func<int, bool>? MapListed { get; init; }
    /// <summary>ARC-DEV-116: the requests of NPCs waiting for the session's slot (Steamwoven). The viewer owns the list, so a rebuilt host (a companion
    /// joined or went home, a reload of the area) goes on with the requests the earlier host held; null: the host keeps its own.</summary>
    public List<(string Npc, int Map, CellCoord Cell)>? Homecomings { get; init; }
    /// <summary>The companions standing here: their session entity, the T0 record they were made from (a visitor's is a row of its home map) and
    /// the world cells they hold now (their live overlay). ARC-DEV-075: the entity is given, never read back from the key on the shown map.</summary>
    public Func<IReadOnlyList<(string Entity, AreaObjectKey Key, CellCoord Cell)>>? Companions { get; init; }
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
    /// <summary>ARC-RENDER-038: the attack ART an NPC's body has for a request (its animation, weapon part and shield), or null when it
    /// has none the viewer can read: the attack then plays with the fixed human attack ART's timing and is not drawn. Null: always so.</summary>
    public Func<AreaAiNpc, AreaAiAttackArtRequest, AreaAiAttackArt?>? AttackArt { get; init; }
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
    /// <summary>ARC-WORLD-114: runs an NPC's first-heartbeat or heartbeat script (the NPC as attachee and triggerer) and answers whether
    /// the tick goes on ("run default", or no script that runs); null: no script runs at these points.</summary>
    public Func<AiScriptPoint, string, bool>? Heartbeat { get; init; }
    /// <summary>ARC-WORLD-109: an NPC's map data's advancement sheet and its effects' modifiers (its skills for a skill attempt), or null
    /// when they cannot be read; null: no NPC has skills, so its heal attempt is refused.</summary>
    public Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?>? NpcSheet { get; init; }
    /// <summary>ARC-WORLD-115 (NPC AI slice I9, host part): the companions of the shown map the host runs too, and their moves; null:
    /// the companions run no AI (their follow goal moves them). ARC-WORLD-119: the free ones too, as any leaderless NPC.</summary>
    public IAreaAiFollowers? Followers { get; init; }
}

/// <summary>
/// ARC-WORLD-115: the companions of the shown map as the AI host runs them (ARC-WORLD-119: whether the hero leads them or they are
/// free, released or cursed: the removal leaves no mark but the null leader, which the AI's follower branches read, FO20). A companion stands where its own walk put it (the
/// scene's <see cref="AreaAiScene.Companions"/> answers its cell); the host's step and turn of one go through the viewer's walk of
/// that companion, which follows nothing else while the host holds it (<see cref="AreaAiHost.Holds"/>).
/// </summary>
public interface IAreaAiFollowers
{
    /// <summary>The companions standing on the map: their session entity, T0 row and home map data's world cell.</summary>
    IReadOnlyList<AreaAiNpc> Npcs { get; }
    /// <summary>Walks the companion one step onto the world cell <paramref name="next"/> (a neighbour of its cell); false when its walk
    /// refuses it.</summary>
    bool Step(string entity, CellCoord next, bool running);
    /// <summary>Turns the companion toward the world cell <paramref name="toward"/>.</summary>
    void Face(string entity, CellCoord toward);
    /// <summary>ARC-DEV-105 (MIG9 S5): moves the companion onto the world cell <paramref name="cell"/> at once (the follower catch-up, 0x4AD4D0);
    /// false when it is not here or its walk refuses the cell. A follower source without the move refuses it.</summary>
    bool Jump(string entity, CellCoord cell) => false;
}

/// <summary>What the AI host tells the viewer: an NPC's attack swing (its pose), the hit's result, a step, a sound, (ARC-WORLD-099)
/// the fidget an NPC starts after a done run (0x4B4320; its animation is not drawn: NPC ART is not read yet), (ARC-WORLD-102) a
/// cast's play, its release and its goal's end, (ARC-WORLD-105) a missile launched at the play's action frame, (ARC-WORLD-109) a
/// skill attempt at its action frame, (ARC-WORLD-113) a throw at its action frame and (ARC-WORLD-108) an item use's magic branch.</summary>
public enum AreaAiNoticeKind { Swing, Hit, Step, Sound, Fidget, Cast, Release, CastEnd, Missile, Skill, Throw, ItemUse }

/// <summary>ARC-WORLD-092: one notice for the viewer (the attack pose, the hit's sounds and status line, the redraw).</summary>
public sealed record AreaAiNotice(AreaAiNoticeKind Kind, string Npc, string? Target)
{
    public SessionAttackResult? Result { get; init; }
    public int Sound { get; init; }
    public AiAttackSwing Swing { get; init; }
    /// <summary>ARC-RENDER-038: the attack animation the swing started (<see cref="AreaAiNoticeKind.Swing"/>).</summary>
    public AreaAiAttackPlay? Play { get; init; }
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
    /// <summary>ARC-WORLD-109: the attempted skill (<see cref="AreaAiNoticeKind.Skill"/>) and, for a heal that reached the session, its
    /// result.</summary>
    public int Skill { get; init; } = -1;
    public SessionHealResult? Heal { get; init; }
    /// <summary>ARC-WORLD-113: the thrown item and the session's throw (<see cref="AreaAiNoticeKind.Throw"/>; the struck critter in
    /// <see cref="Target"/>, its attack in <see cref="Result"/>); ARC-WORLD-108: the used item (<see cref="AreaAiNoticeKind.ItemUse"/>).</summary>
    public string? Item { get; init; }
    public SessionNpcThrowResult? Throw { get; init; }
    /// <summary>ARC-WORLD-108: what the item use's magic branch did (<see cref="AreaAiNoticeKind.ItemUse"/>).</summary>
    public SessionItemUseResult? ItemUse { get; init; }
}

/// <summary>
/// ARC-WORLD-092 (NPC AI slice I10, first fight): the AREA viewer's AI host. It is the scheduler's host (<see cref="ISessionAiWorld"/>,
/// forwarding the event pass's script and party questions to the viewer's event world), the executor's world (<see cref="IAiPerformWorld"/>)
/// and the hostility and morale rules' world (<see cref="IMoraleWorld"/>) over the shown map. ARC-DEV-134 (MIG13 S4): an NPC's goal run is a run of the session's goal
/// table (<see cref="GoalRun"/>, not saved), started by the host's starts and stepped by the frame step at its deadline (<see cref="StepGoal"/>, the world runtime's
/// <see cref="GoalStepper"/>); <see cref="RunOf"/> and <see cref="AnimationRunning"/> read the table. The pass's perform stage
/// runs <see cref="AiExecutorRules.Perform"/>; the danger stage runs <see cref="HostilityRules.DangerUpdate"/> and applies its plan through
/// the session's setter; the hit step is the session's <see cref="GameSessionOwner.AttackByNpc"/>, followed by the attack event; the
/// hero's hits run the attack event here too (<see cref="HeroAttacked"/>). The walker moves NPCs tile by tile (<see cref="Movers"/>).
/// The runs go by the session's animation clock (<c>goal.step</c> events), the ticks by the session's game clock. In a turn-based round
/// (ARC-WORLD-095) the host follows the round's subturns (<see cref="ObserveRound"/>): a round's start and each new subturn interrupt
/// runs as the original's do, the pending cost reads 0 after each subturn start, and the round host
/// (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/>) runs the NPCs' subturn hooks.
/// ARC-WORLD-099: an NPC's subturn end asks its end-combat script (<see cref="SubturnsEnded"/>), a turn set's interrupt that frees the
/// last run runs the completion check, and a done run's end may start the NPC's fidget (<see cref="AiExecutorRules.FidgetAfterRun"/>).
/// ARC-WORLD-102: the support rules' spell half (<see cref="IAiSupportWorld"/>, the spell answers through the map's cast host) chooses
/// a spell at the danger update's target (<see cref="AiSupportRules.ChooseSpell"/>), perform casts it (<see cref="Cast"/>,
/// <see cref="SessionSpellCaster.CastByAi"/>) and the casting goals play through the cast host (start, turn, play, release, end).
/// ARC-WORLD-104: a cast while the NPC's casting goal 26 or 27 plays is queued beneath it (SU13, AC5), its goal played once the goals
/// above it are done; the AI pass itself never meets one, as the gate holds a casting NPC busy (AC6). ARC-WORLD-105: a spell with a
/// missile launches it at the play's action frame (AC8), the viewer flying it from the NPC. ARC-WORLD-113: the attack starter's throw of
/// a "default throws" weapon plays the throw goal 37 (<see cref="ThrowItem"/>, the session's <see cref="GameSessionOwner.ThrowByNpc"/>).
/// ARC-WORLD-108: the NPCs' items are listed (<see cref="Inventory"/>, <see cref="Item"/>), the pass's heal step chooses among them
/// (<see cref="Heal"/>), perform casts from an item (<see cref="Cast"/>) and goal 39 uses one through the cast host's item use.
/// ARC-WORLD-115 (I9's host part): the companions of the shown map are run too (<see cref="AreaAiScene.Followers"/>): their ticks, passes
/// and goal runs, their steps and turns through their own walk, which the host holds while they fight (<see cref="Holds"/>); every
/// attack event tells the attacker's and the victim's followers (the follower rule, <see cref="TellFollowers"/>).
/// ARC-WORLD-119: free companions are run as any leaderless NPC; a leave ends the leaver's run (<see cref="ObserveLeaves"/>), and an
/// order's attack event (the curse's) runs through <see cref="OrderAttack"/>.
/// ARC-DEV-075 (MIG2, TM3): a companion carried here from another home map is run like any other: the host asks its combat values, worn
/// and held items, AI parameter row and sheet by identity (<see cref="AreaCombatHost.Fields(string)"/> and the other identity overloads, the
/// record resolver of MIG1 reading its home map's rows), so it is told of attacks, stands in scans' and noises' squares, counts in a flee
/// test's party, attacks and is attacked (the ARC-DEV-065 skip of every visitor is gone). A follower left on
/// another map is still not here (<see cref="Followers"/>). A companion whose home map cannot be read (the main map's window is not read
/// on demand: the resolver's NOT READY, MIG9) has no record, so it is not found and stays out of the rules as before (<see cref="Unresolved"/>).
/// NOT READY (later slices): the visitor's faction change of a script (the shown map's change), the main-map window as a home;
/// the heal skill's rung and goal 40, pickups, a thrown item's flight, grenades, the back-off's away path, the attack order's target and pass, the comment lines, NPC
/// ranged attacks, scripts at the AI points, routines and waypoints, saved NPC positions.
/// </summary>
public sealed partial class AreaAiHost : ISessionAiWorld, ISessionPartyEventWorld, ISessionLootWindowWorld, IAiPerformWorld, IMoraleWorld, IAiSupportWorld, IGoalStepHost
{
    private const string Pc = AiSchedulerRules.Pc;
    // Object flags: destroyed, off, don't draw, invisible, invulnerable; critter flags; spell flags; NPC flags (HostilityCritter's bits).
    private const uint DontDraw = 0x100, ObjectInvisible = 0x200, Invulnerable = 0x400000, OffObject = 0x2;
    private const uint Undead = 0x4, Animal = 0x8, SleepingFlag = 0x1000, NoFleeFlag = 0x8000000, NonLethal = 0x10000000, AnimalEnshrouded = 0x40000000;
    private const uint NighInvulnerable2 = 0x200, AutoAnimates2 = 0x2;
    private const uint InvisibleSpell = 0x1, DetectsInvisibleSpell = 0x200, StonedSpell = 0x10000, MindControlledSpell = 0x8000000, EnshroudedSpell = 0x20000000;
    private const uint WaitingHereNpc = 0x8, KillOnSightNpc = 0x100, KosOverrideNpc = 0x800, GeneratorNpc = 0x80000, AloofNpc = 0x20000;
    // ARC-WORLD-102: critter flag MECHANICAL and NPC flag CAST_HIGHEST (field 0x118, read in 0x4AE720).
    private const uint MechanicalFlag = 0x20000000, CastHighestNpc = 0x40000;
    // ARC-WORLD-108: item flags identified, hexed and valid AI action (field 0x57, read by 0x4CCA30, 0x4CC810, 0x4CC930).
    private const uint IdentifiedItem = 0x1, HexedItem = 0x40, ValidAiActionItem = 0x200000;
    // The lists of a host without spell records: every spell without AI values.
    private static readonly AiGlobalLists NoLists = AiGlobalLists.Build([.. Enumerable.Repeat(AiSpellValues.None, AiGlobalLists.Spells)]);
    private const int GuardClass = 5;
    // The fixed human attack ART (hmfuwxbu: 9 frames, action frame 6, 15 frames per second) stands in for an NPC's own when the viewer
    // reads none (ARC-RENDER-038: <see cref="AreaAiScene.AttackArt"/>).
    private static readonly (int Frames, int ActionFrame, int RawFramesPerSecond) FixedAttackArt = (9, 6, 15);
    // ARC-WORLD-109: the skill animation (12), and since ARC-WORLD-113 the throw animation (14), stand in with the same frames, played
    // at their raw rate: NPC ART is not read yet.
    private static readonly (int Frames, int ActionFrame, int RawFramesPerSecond) FixedSkillArt = FixedAttackArt;
    // ARC-WORLD-109: object flag 0x10000 and critter flags stunned and paralysed refuse an action's turn and start (0x42C0F0, 0x42CB10).
    private const uint ActionRefusedObject = 0x10000, StunnedCritter = 0x20, ParalysedCritter = 0x40;
    // ARC-DEV-135 (MIG13 S5): the levels of the host's own interrupts (GS20): the AI tick's far path, the follower catch-up and the waypoint and standpoint moves interrupt at 4
    // (TL42), a follower's removal from the party at 5 (0x45E040).
    private const int FarPathLevel = 4, LeaveLevel = 5;

    private readonly GameSessionOwner _session;
    // ARC-DEV-119 (MIG9 S5c): replaced by Rebind when the map's window moves; the host itself is built once per map.
    private AreaAiScene _scene;
    private readonly ISessionScriptWorld _events;
    private readonly ISessionCombatWorld _round;
    private readonly Dictionary<string, AreaAiNpc> _npcs;
    // ARC-WORLD-115: the companions the host runs (their cells are their walk's, never the movers'); ARC-WORLD-119: free ones too.
    private readonly Dictionary<string, AreaAiNpc> _followers;
    // ARC-WORLD-119: the companions the hero led at the host's last look (ObserveLeaves).
    private readonly HashSet<string> _led;
    // ARC-DEV-134 (MIG13 S4): the runs are the session's goal table (GameSessionOwner.QueryGoals); the host keeps none. The steps taken since TakeSteps.
    private int _steps;
    private readonly Dictionary<string, AreaAiTrace> _traces = new(StringComparer.Ordinal);
    private readonly List<AreaAiNotice> _notices = [];
    // The round's events of the host's own session calls (the executor's consumes and subturn ends), for the viewer's round log.
    private readonly List<SessionCombatEvent> _roundEvents = [];
    // The world milliseconds of the last frame or step (the frame step's clock, ARC-DEV-124).
    private long _nowMs;
    // ARC-DEV-134: the run the stepper is ending (its end hooks run before the session frees it, so the fidget's "no run" test leaves it out).
    private GoalHandle? _ending;
    // ARC-DEV-134: the runs stepped since the last frame (a run steps at most once a frame, as the former pump stepped it: a delay shorter than the frame, 0 among
    // them, is the next frame), the world milliseconds of the last frame and its length (the wait of a run that came due again within its frame).
    private readonly HashSet<GoalHandle> _steppedThisFrame = [];
    private long _lastFrameMs = -1;
    private long _frameLengthMs = 16;
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
    // ARC-WORLD-108: the items each NPC used or cast from, until they are gone (the list cache's drops, List).
    private readonly Dictionary<string, HashSet<string>> _usedItems = new(StringComparer.Ordinal);
    private readonly Dictionary<int, SpellFacing?> _castFacing = [];
    private readonly HashSet<int> _noRelease = [];
    // ARC-WORLD-104: the magic runs of queued casting goals that have not started yet (SU13); an end step leaves them unplayed.
    private readonly HashSet<int> _queuedCasts = [];
    // ARC-WORLD-105: the runs launched by a casting goal still playing, with the cast host that launched them (its handled frame and
    // missile live there): a host rebuilt before the goal's end would see neither and free the run charged.
    private readonly Dictionary<int, SessionSpellCaster> _launched = [];
    // ARC-RENDER-038: each NPC's last attack animation (the viewer draws it while it plays).
    private readonly Dictionary<string, AreaAiAttackPlay> _attackPlays = new(StringComparer.Ordinal);
    // ARC-RENDER-038: the weapon part and shield each NPC shows, for one session revision.
    private readonly Dictionary<string, (int Weapon, int Shield)> _shown = new(StringComparer.Ordinal);
    private long _shownRevision = -1;

    /// <param name="events">The viewer's event world: the pass's script calls, life facts, party and loot questions.</param>
    /// <param name="round">The turn-based round's world (the scan of the map's critters).</param>
    public AreaAiHost(GameSessionOwner session, AreaAiScene scene, ISessionScriptWorld events, ISessionCombatWorld round)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _round = round ?? throw new ArgumentNullException(nameof(round));
        _npcs = scene.Npcs.ToDictionary(n => n.Entity, StringComparer.Ordinal);
        _followers = (scene.Followers?.Npcs ?? []).Where(n => !_npcs.ContainsKey(n.Entity)).ToDictionary(n => n.Entity, StringComparer.Ordinal);
        _led = _followers.Keys.Where(f => _session.QueryCompanion(f).LedByPc).ToHashSet(StringComparer.Ordinal);
        RestoreMovers();
    }

    // ARC-DEV-094 (MIG7c, GR10): where an NPC stands is session state, the place the host last walked or turned it to. A host built for the map
    // (after a load, a map change or a window move) stands each of its NPCs there again; an NPC with no record is on its map data's cell.
    private void RestoreMovers() => RestorePlaces(null);

    // ARC-DEV-119: only = the NPCs a window move added to a standing host (their places are restored as a built host would); null: every NPC.
    private void RestorePlaces(ISet<string>? only)
    {
        foreach (var place in _session.QueryNpcPlaces(new SessionMapId(_scene.RuntimeMap)))
        {
            if ((only is not null && !only.Contains(place.Entity)) || !_npcs.TryGetValue(place.Entity, out var npc) || Gone(place.Entity)) continue;
            // A place the shown map cannot hold (outside the chart, not admitted: a window moved since, or a damaged value) or the hero's own cell is
            // not restored: the NPC stands on its map data's cell, as before the place was state.
            if (!PlaceHeld(place.World))
            {
                Trace(npc.Entity).Step = $"place {place.World.X},{place.World.Y} not restored";
                continue;
            }
            Movers.Restore(npc.Entity, npc.Row.Key, npc.Origin, place.World, place.Facing);
        }
    }

    // The mover's cell and facing into the session (one revision, none when it is already recorded). A refusal only leaves the NPC's place unsaved.
    private void RecordPlace(string id)
    {
        if (Movers.Of(id) is not { } mover) return;
        var result = _session.RecordNpcPlace(_session.Version, new SessionMapId(_scene.RuntimeMap), id, mover.Cell, mover.Facing);
        if (result.Failure != GameSessionFailure.None) Trace(id).Step = $"place not recorded: {result.Failure}";
    }

    public AreaAiScene Scene => _scene;
    public AreaAiMovers Movers { get; } = new();
    public GameSessionOwner Session => _session;

    /// <summary>The NPC's run now, or null: ARC-DEV-134 (MIG13 S4), the first of its runs in the session's goal table in slot order (the host starts one run per NPC;
    /// the table could hold overlays beside it, which no starter of the host makes yet).</summary>
    public GoalRun? RunOf(string npc) => _session.QueryGoals().FirstRunOf(npc);

    /// <summary>ARC-DEV-136 (MIG13 S5b, GT3): every run of the NPC in the session's table, in slot order: the runs the executor's equal-goal and alias tests read (the original's 0x44E710 walks every run;
    /// the host starts no overlay yet, so this is the one run it makes).</summary>
    public IReadOnlyList<GoalRun> RunsOf(string npc) => _session.QueryGoals().RunsOf(npc);

    // The runs of the table that are not done (a run marked done during its own step is ending).
    private IEnumerable<GoalRun> LiveRuns() => _session.QueryGoals().Runs.Where(r => !r.Done);

    // The owners of the live runs, in slot order, each once.
    private string[] RunOwners() => [.. LiveRuns().Select(r => r.Owner).Distinct(StringComparer.Ordinal)];

    /// <summary>What the overlay shows of an NPC: its last pass, perform, run step and reason.</summary>
    public AreaAiTrace? TraceOf(string npc) => _traces.GetValueOrDefault(npc);

    /// <summary>The subturn start whose subturn hook the round host ran last (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/>),
    /// -1 before any; a host built during a round asks the NPC whose subturn it is again.</summary>
    public long HookedSubturn { get; set; } = -1;

    /// <summary>The round's events of the host's own session calls since the last call, in order.</summary>
    public IReadOnlyList<SessionCombatEvent> DrainRoundEvents()
    {
        var drained = _roundEvents.ToArray();
        _roundEvents.Clear();
        return drained;
    }

    /// <summary>
    /// ARC-WORLD-095: follows the session's turn-based round. A round that began since the last call interrupts every run (the round's
    /// start 0x4B71E0: the session's sweep of the table, 0x424310, then each participant's runs, 0x424070, both at level 3); a subturn that started since then interrupts the runs
    /// of the critter of the previous turn set when that is an NPC (the turn set 0x4B6E70, 0x424070 at level 3). ARC-DEV-135 (MIG13 S5): the table applies the rule
    /// (0x44E2C0: level 3 ends the runs whose current goal has priority 3 or less, walks, attacks and flights; an attempted cast's 4 goes on, a no-interrupt overlay is left, a
    /// priority-5 goal refuses) and the end nodes of every run it ended run here. ARC-DEV-136 (MIG13 S5b, TL39, TH10): the round start's interrupts come before the animation system's callback
    /// is registered and the round flag set, so they call nothing (<see cref="GameSessionOwner.SuppressRoundCallback"/>); a turn set's interrupt that frees the last significant goal calls the
    /// callback itself (the table does, <see cref="GameSessionOwner.AnimationsDone"/>), with the new participant and its action points, before its hook. Called by the pump before each step and
    /// after the last, and by the round host.
    /// </summary>
    public void ObserveRound()
    {
        if (_session.QueryCombatRound() is not { } round) { _roundSeen = false; return; }
        if (!_roundSeen)
        {
            _roundSeen = true;
            using (_session.SuppressRoundCallback())
            {
                EndNodes(_session.SweepGoalsForRoundStart().Steps);
                foreach (var participant in round.Participants)
                    if (Id(participant) is { } who && who != Pc) InterruptRuns(who, GoalInterruptRules.OrdinaryActions);
            }
        }
        var starts = _session.QueryCombatSubturnStarts();
        if (starts == _subturnsSeen) return;
        _subturnsSeen = starts;
        var previous = _turnSetActor;
        _turnSetActor = Id(round.Actor);
        // The previous critter's runs end; their frees call the callback (the stall detector sees the new participant before its hook, TH10, and the event is asked for).
        // A check that passes the turn on (an actor seen again with unchanged action points) is the next observation's turn set.
        if (previous is not null && previous != Pc) InterruptRuns(previous, GoalInterruptRules.OrdinaryActions);
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
    /// the session; refused schedules are counted. Called when the map's AI starts and after a load. ARC-DEV-105 (MIG9 S5, RL16, RL17): a map
    /// that has a sector cache starts no heartbeat chain for a map NPC whose sector is not loaded (a chain starts only at creation, resurrection,
    /// the fold and its own reschedule; opening a map starts none, and the fold's first ticks came before this call); the companions here are
    /// created with the map's host, so they get theirs whatever their sector.
    /// </summary>
    public int ScheduleTicks()
    {
        var refused = 0;
        foreach (var npc in _scene.Npcs.Concat(_followers.Values))
        {
            if (_session.QueryAiTick(npc.Entity) is not null || Life(npc.Entity) is null) continue;
            if (!_followers.ContainsKey(npc.Entity) && !SectorLoaded(npc.Entity)) continue;
            var result = _session.ScheduleAiTick(_session.Version, new SessionAiTickRequest
            {
                Entity = npc.Entity, Start = SessionAiTickStart.Delayed, DistanceToPc = DistanceToPc(npc.Entity),
            });
            if (result.Disposition == GameSessionDisposition.Rejected) refused++;
        }
        return refused;
    }

    /// <summary>ARC-DEV-124: the world milliseconds of the last frame or goal step (<see cref="Frame"/>, <see cref="StepGoal"/>), the clock the movers' steps and the attack
    /// plays are timed on; the viewer reads it to draw them, so a pause that stops the frames stops their drawing too.</summary>
    public long NowMs => _nowMs;

    /// <summary>The goal steps the host took since the last call (the frame participant's count), and clears it.</summary>
    public int TakeSteps() { var steps = _steps; _steps = 0; return steps; }

    /// <summary>
    /// ARC-DEV-134 (MIG13 S4): the host's part of a frame, once per frame after the frame step's passes and goal steps (<see cref="AreaAiFrameParticipant"/>, not while
    /// paused): the movers' moves that ended by <paramref name="nowMs"/> settle, the companions' leaves are seen and the round is observed after the last step (a step that
    /// began a round or passed the subturn on interrupts runs as the original's round start and turn set do). The runs no longer step here: each is a session run
    /// whose <c>goal.step</c> fires at its deadline on the animation clock and is stepped by <see cref="StepGoal"/>.
    /// </summary>
    public void Frame(long nowMs)
    {
        if (_lastFrameMs >= 0 && nowMs > _lastFrameMs) _frameLengthMs = Math.Clamp(nowMs - _lastFrameMs, 1, 250);
        _lastFrameMs = Math.Max(_lastFrameMs, nowMs);
        _steppedThisFrame.Clear();
        _nowMs = Math.Max(_nowMs, nowMs);
        ClaimSessionEnds();
        Movers.Settle(_nowMs);
        ObserveLeaves();
        ObserveRound();
    }

    // ARC-DEV-134: the runs the session ended by itself (a map's close, a departure, a lost step, a refused step close: GameSessionOwner.DrainGoalEnds) are run
    // through the host's end nodes, as the former host's own drop of them did (ARC-WORLD-102: no NPC's magic run outlives its goal). The hero's runs are not the host's.
    private void ClaimSessionEnds()
    {
        foreach (var end in _session.DrainGoalEnds()) EndNodes(end);
    }

    // ARC-DEV-135 (MIG13 S5, GS24): the end nodes of every run the table ended from outside the stepper (a start's interrupts, an interrupt, a sweep, a kill, the session's own ends),
    // top down. No subturn hook, no idle restart and no AP charge follow them: those are the stepper's own end (StepHooks). A run only marked done, the one being stepped, runs
    // them at once as well (its step's close frees it); a reset's end runs none (GS16). The hero's runs are not the host's.
    private void EndNodes(IEnumerable<GoalInterruptStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Ended is { } end) EndNodes(end);
            else if (step.Outcome == GoalInterruptOutcome.MarkedDone && _session.QueryGoals().TryGet(step.Handle, out var marked) && marked.Owner != Pc) EndDroppedRun(marked);
        }
    }

    private void EndNodes(GoalRunEnd end)
    {
        if (end.Kind != GoalEndKind.Reset && end.Run.Owner != Pc) EndDroppedRun(end.Run);
    }

    /// <summary>
    /// ARC-DEV-134 (MIG13 S4, goal-table.md §3.2): one step of a session run whose <c>goal.step</c> came due (<see cref="IGoalStepHost"/>), at the clocks of its deadline.
    /// Before it the movers settle, the leaves are seen and the round is observed (a step that began a round or passed the subturn interrupts runs, this one too: then it
    /// ends without stepping, its end nodes run by the interrupt). A run of a map NPC whose sector the session's cache has evicted waits a frame (the NPC is not alive there,
    /// RL16) and goes on when the sector loads; a map without a cache holds none back (ARC-DEV-119). A run steps at most once a frame (the former pump's cadence: a
    /// delay shorter than the frame, 0 among them, is the next frame), so one that comes due again within its frame waits for the next. Otherwise the world runtime's stepper steps it
    /// (<see cref="GoalStepper.Step"/>) over this host's node ports and end hooks, and its answer goes back to the session: the next frame delay in real milliseconds
    /// (the session queues it ×8 on the animation clock from now, TL40), or the end.
    /// </summary>
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        _nowMs = Math.Max(_nowMs, clocks.WorldMs);
        ClaimSessionEnds();
        Movers.Settle(_nowMs);
        ObserveLeaves();
        ObserveRound();
        if (Ended(run.Handle)) return SessionGoalStepAnswer.End;
        if (!_followers.ContainsKey(run.Owner) && !SectorLoaded(run.Owner)) return SessionGoalStepAnswer.After(NextFrameDelay(clocks));
        // Stepped in this frame already: it waits for the next one. The guard holds only while the host's frame runs (its participant is bound, as in the viewer).
        if (_lastFrameMs >= 0 && _steppedThisFrame.Contains(run.Handle)) return SessionGoalStepAnswer.After(NextFrameDelay(clocks));
        _steppedThisFrame.Add(run.Handle);
        var npc = run.Owner;
        GoalStepperResult stepped;
        _ending = run.Handle;
        try { stepped = GoalStepper.Step(run, this, _session.AiRolls, _session.Rules.Combat, new StepHooks(this), TraceOf(npc)?.Reason ?? AiExecutorReason.None); }
        finally { _ending = null; }
        _steps++;
        var step = stepped.Step;
        Trace(npc).Step = step is null ? $"{stepped.End}" : $"{step.End} {step.Reason} nodes={step.Nodes} delay={step.DelayMs}";
        if (step is not null) Trace(npc).Reason = step.Reason;
        // A node that ended the run from outside (an interrupt or a drop of its owner) ran its end nodes: the session's close ends the marked run.
        return Ended(run.Handle) ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    // A wait of one frame: to just past this frame's expected end (the last frame's end plus its length), or a whole frame's length when this frame already ran
    // longer, so a waiting run does not wake within its frame (each wake is a sub-step of the frame step's budget).
    private long NextFrameDelay(FrameClocks clocks)
    {
        var remaining = _lastFrameMs + _frameLengthMs + 1 - clocks.WorldMs;
        return _lastFrameMs >= 0 && remaining >= 1 ? remaining : _frameLengthMs;
    }

    // Whether the run is gone or marked done in the table (an interrupt during its step).
    private bool Ended(GoalHandle handle) => !_session.QueryGoals().TryGet(handle, out var now) || now.Done;

    // The stepper's end hooks (IGoalStepHooks), this host's: the end nodes of a dropped run, the queued casts an end step leaves, the fidget after a done run.
    private sealed class StepHooks(AreaAiHost host) : IGoalStepHooks
    {
        public void EndDropped(GoalRun run) => host.EndDroppedRun(run);

        // ARC-WORLD-104: the end step runs the end node of every goal (QC5); a queued goal 26's (0x42B090) frees nothing (MG43).
        public void EndStepped(GoalRun run) => host.FreeQueuedCasts(run);

        // ARC-WORLD-099: the stepper's end of a done run (TH4): the fidget's call (0x4B4320), after the owner's subturn hook. The round actor's hook is the round
        // host's next frame (FidgetAfterHook); anyone else's hook does nothing.
        public void Restart(GoalRun run)
        {
            if (host.InRound && host.RoundActor == run.Owner) host._fidgetAfterHook = run.Owner;
            else host.Fidget(run.Owner);
        }
    }

    /// <summary>
    /// The attack event of the hero's attack on the NPC <paramref name="victim"/> (0x4A9650, <see cref="HostilityRules.AttackEvent"/>):
    /// the session's who-hit-me-last and team writes, the reaction change toward the hero, the victim's turn and its reaction to the
    /// attack (fight or flee, <see cref="MoraleRules.ReactToAttack"/>) through the setter, and the witnesses of the noise
    /// (<see cref="HostilityRules.NoiseWitnesses{T}"/>, <see cref="HostilityRules.Witness"/>). ARC-WORLD-115: the attacker's and the
    /// victim's followers are told (<see cref="TellFollowers"/>).
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
    /// ran for <paramref name="npc"/> since (<see cref="AreaTurnBased.Step(GameSessionOwner, ISessionCombatWorld, bool, AreaAiHost?, int, bool)"/>);
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
        // ARC-DEV-134: the run being ended is still in the table until its step closes; it is not the NPC's animation any more.
        var animating = LiveRuns().Any(r => r.Owner == npc && r.Handle != _ending);
        if (!AiExecutorRules.FidgetAfterRun(this, npc, combatMode, (critter.Flags2 & AutoAnimates2) != 0, animating, artAllows: true)) return;
        _notices.Add(new(AreaAiNoticeKind.Fidget, npc, null));
    }

    /// <summary>
    /// ARC-WORLD-102: ends every run as a dropped one (its end nodes, <see cref="EndDroppedRun"/>: a casting goal's magic run is freed,
    /// so no NPC's spell run outlives its goal), then <see cref="Reset"/>. The viewer calls it before it drops or replaces the host of
    /// the same session (a follower joining or leaving, a map change, the AI turned off).
    /// </summary>
    public void Retire()
    {
        ClaimSessionEnds();
        foreach (var npc in RunOwners()) DropRun(npc);
        Reset();
    }

    /// <summary>Forgets the host's own state and movers (a load: the session's goal table belongs to the restored session, which starts with no run; ARC-DEV-134:
    /// the runs are the session's, so the host holds none to clear).</summary>
    public void Reset()
    {
        _steps = 0; _ending = null; _steppedThisFrame.Clear(); _lastFrameMs = -1; _frameLengthMs = 16;
        _traces.Clear();
        _notices.Clear();
        _roundEvents.Clear();
        _roundSeen = false; _subturnsSeen = -1; _turnSetActor = null; _pendingCost = 0; _pendingAt = -1; HookedSubturn = -1; _fidgetAfterHook = null;
        _passChoices.Clear(); _listCache = AiListCache.Empty; _castFacing.Clear(); _noRelease.Clear(); _queuedCasts.Clear(); _launched.Clear();
        _attackPlays.Clear(); _shown.Clear(); _shownRevision = -1;
        Movers.Clear();
        RestoreMovers();
    }

    /// <summary>
    /// ARC-DEV-119 (MIG9 S5c, K1 §3.5): the map's window moved, so the viewer built the scene again (the chart, the evidence, the rows the new window holds, the
    /// companion group and every provider) while the host goes on: its goal runs, traces, movers, round state and pending ticks are the map's, not the
    /// window's. The NPCs the host runs become those of every window it has seen: a row the new window holds replaces the earlier one (its record key is the new
    /// window's, and so is its mover's), an NPC the new window no longer holds keeps its row (it still walks, fights and ticks while its sector is loaded). The companions
    /// are the new group's. Casting runs end first (<see cref="ReleaseCasts"/>), since the window's cast host and eye candy are gone. Another map is a new host.
    /// </summary>
    public void Rebind(AreaAiScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.RuntimeMap != _scene.RuntimeMap) throw new ArgumentException("A host serves one map; another map is a new host.", nameof(scene));
        ReleaseCasts();
        var followers = (scene.Followers?.Npcs ?? []).ToDictionary(n => n.Entity, StringComparer.Ordinal);
        // The NPCs the earlier windows gave that stand on their map data's cell and run nothing are forgotten (a window that holds them again gives their rows back): the
        // host keeps the NPCs that act, so the set stays that of the windows near the hero and the ones walking.
        foreach (var entity in _npcs.Keys.ToArray())
            if (!AnimationRunning(entity) && Movers.Of(entity) is null && scene.Npcs.All(n => n.Entity != entity)) _npcs.Remove(entity);
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in scene.Npcs) { if (!_npcs.ContainsKey(npc.Entity)) added.Add(npc.Entity); _npcs[npc.Entity] = npc; }
        foreach (var companion in followers.Keys) _npcs.Remove(companion);
        _followers.Clear();
        foreach (var (entity, npc) in followers) _followers[entity] = npc;
        foreach (var npc in scene.Npcs) if (_npcs.ContainsKey(npc.Entity)) Movers.Rekey(npc.Entity, npc.Row.Key, npc.Origin);
        _led.RemoveWhere(entity => !_followers.ContainsKey(entity));
        _scene = scene with { Npcs = [.. _npcs.Values] };
        // A row the host did not hold stands where the session recorded the NPC, as in a host built for it (the first step would overwrite the place).
        if (added.Count > 0) RestorePlaces(added);
        _listCache = AiListCache.Empty; _shown.Clear(); _shownRevision = -1;
    }

    /// <summary>Ends the casting runs as dropped ones (their end nodes free the magic run) and forgets the cast bookkeeping: what a window move takes from under them.</summary>
    public void ReleaseCasts()
    {
        foreach (var npc in LiveRuns().Where(r => r.Frames.Any(f => f.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation)).Select(r => r.Owner)
                     .Distinct(StringComparer.Ordinal).ToArray())
            DropRun(npc);
        _castFacing.Clear(); _noRelease.Clear(); _queuedCasts.Clear(); _launched.Clear();
    }

    private void AttackEvent(string attacker, string victim, AttackEventFlags flags = AttackEventFlags.None, Loudness loudness = Loudness.Normal)
    {
        var plan = HostilityRules.AttackEvent(this, attacker, victim, flags);
        Trace(victim).Event = $"{plan.Stop}";
        if (plan.Stop is AttackEventStop.Missing or AttackEventStop.VictimProtected or AttackEventStop.VictimDead or AttackEventStop.VictimGone
            or AttackEventStop.VictimDecoy or AttackEventStop.VictimWall or AttackEventStop.Self) return;
        // ARC-WORLD-115: the attacker's followers first, right after the early exits (0x4A9B80, side attacker, the victim the other).
        if (plan.TellsAttackerFollowers) TellFollowers(attacker, victim, FollowerSide.Attacker);
        // A PC victim responds (0x4A9AD0): it enters combat mode (its call of 0x4B3F50).
        if (plan.PcVictimResponds) HeroIntoCombatMode();
        // ARC-WORLD-115: then the victim's followers (side victim, the attacker the other), before the noise.
        if (plan.TellsVictimFollowers) TellFollowers(victim, attacker, FollowerSide.Victim);
        if (plan.RaisesNoise) Witnesses(attacker, victim, loudness);
        if (plan.Stop != AttackEventStop.None) return;
        _session.ApplyAttackEvent(_session.Version, new SessionAttackEventRequest { Attacker = Combatant(attacker)!, Victim = victim, Plan = plan });
        if (plan.TurnsVictim && Tile(attacker) is { } there && TryNpc(victim, out var npc)) FaceNpc(npc, there);
        if (plan.Reaction == AttackReactionChange.Deliberate && attacker == Pc && ReactionFacts(victim) is { } facts && Row(victim) is { } row)
            _session.ReactToPcHit(_session.Version, victim, facts.Npc, facts.Pc, new(row.FollowerHitReaction, row.HostileReaction), deliberate: true);
        if (plan.ReactsToAttack) React(victim, attacker);
    }

    /// <summary>
    /// ARC-WORLD-115 (I9 host part): tell followers (0x4A9B80, <see cref="FollowerRules.TellFollowers"/>) of <paramref name="leader"/>
    /// about <paramref name="other"/>: the leader's direct followers in its follower array's order (only the hero leads here: the
    /// party's followers that follow it, not the waiting ones, that stand on this map), each through the follower rule (never ordered,
    /// never accidental here: the host's attacks are deliberate). A lock or focus and a complaint's −5 toward the hero go through the
    /// session (<see cref="GameSessionOwner.ApplyFollowerRule"/>); the reaction to an attack by the other (case 5) is
    /// <see cref="MoraleRules.ReactToAttack"/> through the setter, as a victim's. The complaint's upset line is not floated (the line
    /// texts are the host's, not read yet).
    /// </summary>
    private void TellFollowers(string leader, string other, FollowerSide side)
    {
        if (leader != Pc || Find(other) is null) return;
        var followers = _session.QueryParty().Followers.Where(f => f.Standing == CompanionStanding.Following && _followers.ContainsKey(f.Entity))
            .Select(f => f.Entity).Where(f => !Unresolved(f)).ToArray();
        if (followers.Length == 0) return;
        foreach (var outcome in FollowerRules.TellFollowers(this, leader, other, followers, side, accidental: false, _session.Rules.Combat))
        {
            var follower = outcome.Follower;
            Trace(follower).Event = $"told {side} {outcome.Step}{(outcome.Upset == UpsetReason.None ? "" : " " + outcome.Upset)} {other}";
            switch (outcome.Step)
            {
                case FollowerRuleStep.ReactToAttack:
                    React(follower, other);
                    break;
                case FollowerRuleStep.Lock or FollowerRuleStep.Focus or FollowerRuleStep.Complain:
                    var facts = ReactionFacts(follower);
                    if (outcome.Step == FollowerRuleStep.Complain && facts is null) break;
                    var result = _session.ApplyFollowerRule(_session.Version, new SessionFollowerRuleRequest
                    {
                        // The leader is the hero, so the other side never is (the rule says nothing then): no reaction facts.
                        Outcome = outcome, Leader = SessionCombatant.Hero, Target = Combatant(other)!, Setter = SetterFacts(follower),
                        Complaint = facts is { } f ? new SessionPeaceReaction(f.Npc, f.Pc) : null,
                    });
                    Trace(follower).Setter = $"follower rule {outcome.Step} {other} {result.Disposition} {result.Failure}";
                    break;
            }
        }
    }

    /// <summary>
    /// ARC-WORLD-117: a follower's combat facts for an order (<see cref="CompanionOrderFacts.Combat"/>): the setter's facts of an attack
    /// order's lock, its reaction facts toward the hero (a complaint's change, a stop's peace with the hero's side) and its combat-mode
    /// facts (a stop's exit). Missing reaction or combat-mode facts stay null, as the session's defaults.
    /// </summary>
    public SessionPeaceFacts OrderFacts(string follower)
    {
        ArgumentNullException.ThrowIfNull(follower);
        return new()
        {
            Setter = SetterFacts(follower),
            Reaction = ReactionFacts(follower) is { } facts ? new SessionPeaceReaction(facts.Npc, facts.Pc) : null,
            CombatMode = NpcCombatMode(follower),
        };
    }

    /// <summary>
    /// ARC-WORLD-117: the host's part of an order's stop attacking (back off, FO5), run right after the follower's order and before
    /// its AI pass (<see cref="CompanionOrderRequest.Ordered"/>): the stop's animation interrupt at priority 3
    /// (<see cref="SessionPeaceStep.Interrupted"/>, 0x44E2C0 as at a round's start) ends the follower's walk, attack or flight run with
    /// its end nodes, so the pass finds it idle. True: it freed a run.
    /// </summary>
    public bool ApplyStop(SessionPeaceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Interrupted && InterruptRuns(step.Entity, GoalInterruptRules.OrdinaryActions);
    }

    /// <summary>
    /// ARC-WORLD-119: the leaves since the last look. A companion the host runs that the hero led and leads no more (any leave: an
    /// order's or the curse's, a dialog's, the loyalty step's, a script's disband) has its run dropped with its end nodes, as the
    /// original's removal (0x45E040, ledger FO20) nulls the leader and then interrupts the NPC's animations at priority 5 (0x424070),
    /// which ends every run of this host (its goals are of priority 4 or less); from then on it is run as any leaderless NPC. A join
    /// interrupts nothing (0x45DE90, C5): the joined companion keeps its run. The pump calls it before its steps and perform before it
    /// reads the run; an order's host part calls it right after the order (<see cref="AreaCompanionOrders.Send"/>), so the curse's
    /// attack event and the pass after it meet the leave already done. Returns the companions whose run was dropped.
    /// </summary>
    public IReadOnlyList<string> ObserveLeaves()
    {
        List<string>? dropped = null;
        foreach (var companion in _followers.Keys)
        {
            var led = _session.QueryCompanion(companion).LedByPc;
            if (led) { _led.Add(companion); continue; }
            if (!_led.Remove(companion)) continue;
            Trace(companion).Event = "left the hero";
            if (RunOf(companion) is null) continue;
            if (InterruptRuns(companion, LeaveLevel)) (dropped ??= []).Add(companion);
        }
        return dropped ?? (IReadOnlyList<string>)[];
    }

    /// <summary>
    /// ARC-WORLD-119: the attack event an order leaves to the host (<see cref="CompanionOrderOutcome.CurseAttack"/>: the curse's, the
    /// hero on the former follower at loudness 1 with no flags, FO2), run in full as the hero's attacks are (<see cref="HeroAttacked"/>):
    /// the hero's remaining followers are told, the noise's witnesses react, and the victim, now free, reacts to the attack (fight or
    /// flee through the setter). False: an attacker or victim this host does not know (nothing runs).
    /// </summary>
    public bool OrderAttack(CompanionOrderAttack attack)
    {
        ArgumentNullException.ThrowIfNull(attack);
        if (Id(attack.Attacker) is not { } attacker || Id(attack.Victim) is not { } victim || Find(attacker) is null || Find(victim) is null) return false;
        AttackEvent(attacker, victim, attack.Flags, attack.Loudness);
        return true;
    }

    // The witnesses of an attack's noise: the attacker's square when under 30 tiles apart, then the victim's (AW16 under the switch).
    private void Witnesses(string attacker, string victim, Loudness loudness)
    {
        var distance = Distance(attacker, victim);
        var radius = AwarenessRules.NoiseRadius(loudness);
        var witnesses = HostilityRules.NoiseWitnesses(distance, Square(attacker, radius), Square(victim, radius), _session.Rules.Combat);
        foreach (var witness in witnesses)
        {
            if (witness == Pc || witness == attacker || witness == victim || !Walks(witness)) continue;
            var outcome = HostilityRules.Witness(this, witness, attacker, victim, loudness);
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

    /// <summary>ARC-WORLD-114: the first-heartbeat and heartbeat points run the NPC's script through the scene's
    /// <see cref="AreaAiScene.Heartbeat"/> (true: continue); the other points run no script yet and continue.</summary>
    public bool RunScript(AiScriptPoint point, string npc, string? triggerer) =>
        point is not (AiScriptPoint.FirstHeartbeat or AiScriptPoint.Heartbeat) || _scene.Heartbeat?.Invoke(point, npc) != false;
    // ARC-DEV-117 (MIG9 S4b): the fold's waypoint and standpoint steps (0x4ACBB0, 0x4ACDB0), in AreaAiHostRoutes.cs.
    public bool RunWaypoints(string npc) => RunRouteStep(npc, NpcRouteRules.ForcedWaypoints, waypoints: true);
    public void RunStandpoints(string npc) => RunRouteStep(npc, NpcRouteRules.ForcedStandpoint, waypoints: false);
    public bool IsGenerator(string npc) => Fields(npc)?.Npc is { } fields && (fields.Flags & GeneratorNpc) != 0;
    public long? Spawn(string npc) => null;
    public bool IsEncounterCreature(string npc) => false;
    // The object destroy (0x43CCA0) kills every run of the object (0x423FF0, GS21): no rule, no level; the end nodes of the runs that ended run (they do nothing for a killed run
    // in the original; this host's also free a cast's unbegun magic run, ARC-WORLD-102).
    public void Destroy(string npc) => EndNodes(_session.KillGoals(npc).Steps);
    // The AI tick's far path (0x424070 at level 4, TL42, GS20).
    public void InterruptAnimations(string npc) => InterruptRuns(npc, FarPathLevel);
    /// <summary>ARC-DEV-104 (MIG9 S4, RL16's loaded test): the session answers for the open map's sector cache (a whole map whose residency the
    /// viewer opened); a map without one (a window of the main map) counts every sector loaded, as before.</summary>
    public bool SectorLoaded(string npc) =>
        Tile(npc) is not { } at || _session.QuerySectorLoaded(new(_scene.RuntimeMap), ResidentSector.Of(at)) != false;
    /// <summary>ARC-DEV-105 (MIG9 S5, 0x4AD4D0): the follower catch-up's tests as the original reads them: a PC leader, its distance, the NPC flag
    /// WAITING_HERE (field 0x118, 0x8; ours also a waiting standing), the critter flag paralysed (field 0xDA, 0x40) and active (0x45D790).</summary>
    public AiCatchUpFacts CatchUp(string npc) => new(Leader(npc) == Pc, DistanceToPc(npc),
        (Fields(npc)?.Npc is { } fields && (fields.Flags & WaitingHereNpc) != 0) || Waiting(npc),
        Fields(npc)?.Critter is { } critter && (critter.Flags & ParalysedCritter) != 0, Active(npc));
    /// <summary>ARC-DEV-105 (MIG9 S5, 0x4AD4D0 then 0x43E770): the follower's goal run ends (the original's interrupt at priority 4), then it
    /// stands on the PC's cell, through the follower source (the companion group's jump); a follower the source does not hold stays put.</summary>
    public void MoveToLeader(string npc)
    {
        InterruptRuns(npc, FarPathLevel);
        if (_scene.Followers is { } followers && _followers.ContainsKey(npc) && Tile(Pc) is { } pc) followers.Jump(npc, pc);
    }
    public bool MapClearing => false;
    public bool Sleeping(string npc) => Fields(npc)?.Critter is { } critter && (critter.Flags & SleepingFlag) != 0;
    // Object flags 0x102: not drawn or switched off in its map data, or (ARC-WORLD-114) switched off by a script (gone from the scene).
    public bool Hidden(string npc) => Fields(npc) is { } fields && (fields.Object.Flags & (DontDraw | OffObject)) != 0 || Gone(npc);
    public bool PcHidden => false;
    public AiConversation Conversation(string npc) => _scene.ConversationPartner?.Invoke() == npc ? AiConversation.Pc : AiConversation.None;
    public bool ConversationCallback => false;
    public void EndConversation(string npc) { }

    /// <summary>The gate's animation priority (0x4233D0, AC6): that of the bottom goal (stack slot 0) of the NPC's run, 0 without a
    /// run. ARC-WORLD-105: the top goal was read before; for a run whose goals differ, such as an attack under its approach, the gate
    /// then let the pass run mid-approach (a technical slip of the host, fixed in both modes).</summary>
    // The AI pre-check (0x4A8570, GS20) reads the priority of the main run's bottom goal (0x4233D0: the first run that is not an overlay; 0 with none).
    public int AnimationPriority(string npc) => _session.QueryGoals().MainRunPriority(npc);
    public bool StunAnimationRunning(string npc) => false;
    public bool AnimationRunning(string npc) => _session.QueryGoals().RunsOf(npc).Length > 0;
    public void ReWield(string npc, bool allSlots) { }
    public void EndMaintainedSpells(string npc) { }
    /// <summary>
    /// ARC-WORLD-108: heal (0x4A8940, <see cref="AiSupportRules.Heal"/>), the pass's first step: the healer's lists chosen for the NPC,
    /// its leader and the party; a choice goes to this pass's perform as the pass record's, an AI-action item is used at once
    /// (<see cref="UseItemAtOnce"/>). True when it chose: the pass skips the item search and the danger update. The heal skill's rung
    /// finds the NPC's healing item (ARC-WORLD-116, <see cref="HealingItem"/>) and its choice starts the skill goal 33 at perform.
    /// </summary>
    public bool Heal(string npc)
    {
        _passChoices.Remove(npc);
        var heal = AiSupportRules.Heal(this, npc, _session.AiRolls, _session.Rules.Combat);
        if (!heal.Chose) return false;
        Trace(npc).Item = $"heal {heal.Attempts[^1].Target} {heal.Attempts[^1].Step} spell={heal.Choice?.Spell.ToString() ?? "-"} item={heal.Choice?.Item ?? heal.ItemUse?.Entry?.Item ?? "-"}";
        if (heal.ItemUse is { } atOnce) UseItemAtOnce(npc, atOnce);
        else if (heal.Choice is { } choice) _passChoices[npc] = choice;
        return true;
    }

    /// <summary>
    /// ARC-WORLD-108: the chooser's use of an AI-action item at once (0x4ABF10 → 0x462CC0, <see cref="AiChooserOutcome.ItemUsedAtOnce"/>):
    /// the item use with the NPC as user and the entry's target, unchecked, before the pass goes on; the pass record keeps no choice.
    /// </summary>
    private void UseItemAtOnce(string npc, AiChooserResult chosen)
    {
        if (chosen.Entry?.Item is not { } item) return;
        var used = AiExecutorRules.UseItemNow(this, npc, item, chosen.Target);
        Trace(npc).Item += $" used at once {used.Step} spent={used.Spent}";
    }
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
            choice = chosen.Choice;
            Trace(npc).Spell = $"{chosen.Stop} chance={chosen.Chance} spell={choice?.Spell.ToString() ?? "-"} item={choice?.Item ?? chosen.ItemUse?.Entry?.Item ?? "-"} target={choice?.Target ?? "-"}";
            // ARC-WORLD-108: an AI-action item entry is used at once (0x462CC0); the setter then runs without a choice.
            if (chosen.ItemUse is { } atOnce) UseItemAtOnce(npc, atOnce);
        }
        var target = state == DangerState.CombatFocus ? HostilityRules.FocusTarget(plan, choice?.Target ?? plan.Target, _session.Rules.Combat) : null;
        var result = SetDanger(npc, state, target);
        if (choice is not null && (result.Transition is not { } transition || HostilityRules.KeepsChosenAction(transition, _session.Rules.Combat)))
            _passChoices[npc] = choice;
    }

    /// <summary>The perform stage: <see cref="AiExecutorRules.Perform"/> with the danger source as the pass's target; a started run
    /// replaces the NPC's (the old one's end nodes run, <see cref="AiExecutorRules.EndRun"/>) and steps at the next pump.
    /// ARC-WORLD-115: a companion's follow goal (the non-combat perform's, 0x4346A0) is its own walk's, which follows the hero while
    /// the host does not hold it (<see cref="Holds"/>), so a follow the perform starts for one is not run here.</summary>
    public void Perform(string npc)
    {
        ObserveLeaves();
        var danger = _session.QueryDanger(npc);
        var target = danger.Source is { } source ? Id(source) : null;
        var current = RunOf(npc);
        // ARC-WORLD-102: the pass record's choice from this pass's danger update (none: the action type 0).
        _passChoices.Remove(npc, out var choice);
        var result = AiExecutorRules.Perform(this, npc, target, choice, null, current);
        if (_followers.ContainsKey(npc) && result.Kind == AiPerformKind.Follow)
        {
            // A run left from the fight (a flight, a back-off) is interrupted as the follow starter's own interrupt would (level 3, the table's rule: a spell attempt, priority 4,
            // goes on), so the walk is no longer held by it. ARC-DEV-135: it was a drop at the highest level.
            Trace(npc).Perform = $"{result.Kind} by its own walk";
            if (current is not null) InterruptRuns(npc, GoalInterruptRules.OrdinaryActions);
            return;
        }
        Started(npc, current, result);
    }

    /// <summary>
    /// ARC-WORLD-115: whether the host holds the companion <paramref name="entity"/>, so its own walk follows nothing: while it has a
    /// goal run here, or its danger state is not NONE (a follower follows only in state NONE, the non-combat perform, A6). Else
    /// false (a map NPC or an unknown one too).
    /// </summary>
    public bool Holds(string entity) =>
        _followers.ContainsKey(entity) && (AnimationRunning(entity) || _session.QueryDanger(entity).State != DangerState.None);

    /// <summary>ARC-WORLD-115: the companions the host runs (ARC-WORLD-119: free ones too).</summary>
    public IReadOnlyCollection<string> RunCompanions => _followers.Keys;

    /// <summary>ARC-WORLD-113 test seam: the attack starter (0x434B00, <see cref="AiExecutorRules.StartAttack"/>) on
    /// <paramref name="target"/>, as combat perform's attack starts it: a "default throws" weapon starts the throw goal 37.</summary>
    public void PerformAttack(string npc, string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = RunOf(npc);
        Started(npc, current, AiExecutorRules.StartAttack(this, npc, target, null, current));
    }

    private void Started(string npc, GoalRun? current, AiPerformResult result)
    {
        Trace(npc).Perform = $"{result.Kind}{(result.Refusal == AiStartRefusal.None ? "" : " " + result.Refusal)}{(result.Started ? " started" : "")}";
        if (!result.Started || result.Run is not { } run || ReferenceEquals(current, run)) return;
        // ARC-DEV-135 (MIG13 S5): the proposed run starts in the session's table with its starter's own plan (GS12, GS13): the table interrupts the NPC's runs at the starter's
        // level by the rule (the executor's starters that interrupt in their own body, the attack, throw and follow, did so already, so what is left is the runs the rule kept, a
        // no-interrupt overlay among them, and the attack retarget's, which asks no port), refuses a duplicate and a refused interrupt, and the end nodes of every run the start
        // ended run here (the replaced run's among them). The first step comes 5 ms of animation time after the start (GS7), at the next pass (a start inside the AI tick's pass waits for it).
        var started = _session.StartGoal(new GoalStartRequest(npc, StarterPlan(run.Bottom.Goal), run.Bottom, run.Flags), _startPorts ??= new StartPorts(this));
        foreach (var end in started.Ended) EndNodes(end);
        if (started.Started) { Trace(npc).Reason = AiExecutorReason.None; return; }
        // A start the table refused (an interrupt a priority-4 or -5 goal refuses, a duplicate, no free slot, a full queue) leaves the NPC with the runs the rule kept: the proposed
        // run's end nodes free what its starter took (a cast's magic run).
        // Only the proposed run's end nodes run: the kept run's attack animation (a priority-4 goal's run can have one) is not the proposed run's.
        Trace(npc).Perform += $" not in the table: {started.Failure} {started.Outcome}";
        AiExecutorRules.EndRun(run, this);
        FreeUnbegunCasts(run, queuedOnly: false);
    }

    // The interrupt plan of the starter that builds a run with this bottom goal (GS12, GS13): level 3 for the walks, runs, follow, attack, throw and spread out (with the
    // duplicate refusal for the last four); every other start interrupts at its goal's own priority (the cast, skill, item, pickup and flee goals are 3).
    internal static GoalStartPlan StarterPlan(GoalType goal) => goal switch
    {
        GoalType.MoveToTile => GoalStarters.Walk,
        GoalType.RunToTile => GoalStarters.Run,
        GoalType.Follow => GoalStarters.Follow,
        GoalType.Attack => GoalStarters.Attack,
        GoalType.ThrowItem => GoalStarters.Throw,
        GoalType.SpreadOut => GoalStarters.SpreadOut,
        _ => GoalStarters.Generic,
    };

    // The table's start ports for the host's starts. ARC-DEV-136 (MIG13 S5b, TL41, GS15): the gated starters (the walks and runs through the movement gate 0x4339A0; follow, attack, throw and
    // spread out through the shared start gate 0x4348E0) refuse a critter that is not the current participant of a round, whoever asks (the executor's starters run the same test with the
    // action-point rule before they get here, so the table's gate is the second line); nothing loads and a destroyed NPC is not performed for. The other starts (the hit reactions, dying,
    // the casts, the goals scripts or the combat code push) pass the gate: the table gives them the 1 ms first frame instead (`GameSessionOwner.RoundFirstGoalStepDelayMs`).
    private StartPorts? _startPorts;

    private sealed class StartPorts(AreaAiHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => host.StartGatePasses(request);
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    // The table's start gate (0x4339A0, 0x4348E0 for the gated starters, TL41): outside a round everything passes; in one only the current participant.
    // The fidget start (0x435BD0), the get-up starter (0x42E590) and the wander starters (0x435E60, 0x436040) go through the same gates (TL41) though their plan is the generic one.
    internal bool StartGatePasses(GoalStartRequest request) =>
        !(Gated(request.Plan.Kind) || request.Frame.Goal is GoalType.Fidget or GoalType.GetUp or GoalType.Wander or GoalType.WanderSeekDarkness) || !InRound || RoundActor == request.Owner;

    internal static bool Gated(GoalStarterKind kind) =>
        kind is GoalStarterKind.Walk or GoalStarterKind.Run or GoalStarterKind.Follow or GoalStarterKind.Attack or GoalStarterKind.Throw or GoalStarterKind.SpreadOut;

    /// <summary>ARC-WORLD-105 smoke seam: perform with <paramref name="choice"/> as the pass record's choice, as a danger update that
    /// chose it would (the native ai-cast smoke's missile spell).</summary>
    public void PerformChoice(string npc, AiPassChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        _passChoices[npc] = choice;
        Perform(npc);
    }

    /// <summary>ARC-WORLD-109 test and smoke seam: perform with a skill use of <paramref name="skill"/> on <paramref name="target"/> with
    /// the tool <paramref name="item"/> as the pass record's choice (the action type 3), as the heal ladder's heal skill would choose it
    /// (the skill starter 0x4350F0, goal 33).</summary>
    public void PerformSkill(string npc, string target, int skill, string? item)
    {
        ArgumentNullException.ThrowIfNull(target);
        PerformChoice(npc, new AiPassChoice(AiPassAction.UseSkill, -1, skill, item, target));
    }

    // An NPC that joined the hero since the host was built is the companions' (it counts as gone here); ARC-WORLD-115: a companion the
    // host runs is its own.
    public SessionLifeTarget? AiLifeTarget(string entity) =>
        !_followers.ContainsKey(entity) && _session.QueryCompanion(entity).LedByPc ? null : Build(entity)?.Target;

    // ARC-DEV-135 (MIG13 S5): an interrupt of every run of the NPC at a level (0x424070, GS17; the first refusal stops the walk) with the end nodes of each run it ended. True: it
    // freed a run (or marked the stepped one done); Succeeded is the original's answer (no run, a no-interrupt goal left running and a run that ended all succeed; a refusal, or the
    // spell-run rules holding the table, do not).
    private (bool Freed, bool Succeeded) InterruptOwner(string npc, int level)
    {
        var change = _session.InterruptGoals(npc, level);
        EndNodes(change.Steps);
        var succeeded = change.Failure == SessionGoalFailure.None && change.Succeeded;
        if (!succeeded) Trace(npc).Step = $"interrupt {level} refused: {change.Failure} {string.Join(",", change.Steps.Select(s => s.Outcome))}";
        return (change.Steps.Any(s => s.Outcome is GoalInterruptOutcome.Ended or GoalInterruptOutcome.MarkedDone), succeeded);
    }

    private bool InterruptRuns(string npc, int level) => InterruptOwner(npc, level).Freed;

    // A drop of the NPC's runs at the highest level, which every goal and overlay yields to (GS18): the host's own end of a run (a map reset's level, GS19).
    private void DropRun(string npc) => InterruptRuns(npc, GoalInterruptRules.MaxLevel);

    // A run the host drops without stepping it to its end: its end nodes (AiExecutorRules.EndRun). ARC-WORLD-102: a casting goal 26 or
    // 82 dropped before its attempt goal has only the animation reset as its end node (0x42B090), which frees nothing, so its magic run
    // would keep its slot, never begun, as MG43's queued cast does; as there (owner decision 2026-10-28, both modes) the NPC's run that
    // still waits for its BEGIN is freed, unpaid.
    private void EndDroppedRun(GoalRun run)
    {
        // ARC-RENDER-038: a dropped run's attack animation is not drawn on.
        _attackPlays.Remove(run.Owner);
        AiExecutorRules.EndRun(run, this);
        FreeUnbegunCasts(run, queuedOnly: false);
    }

    // ARC-WORLD-104: a run that ended through the end step leaves the casts queued beneath its goals unplayed: their goals' end node,
    // the animation reset (0x42B090), frees nothing, so in the original the runs keep their slots (MG43, fixed in both modes: the
    // host frees them unpaid, as the hero's dropped queue). Only the goals that never started are theirs: one that started ended its
    // own run.
    private void FreeQueuedCasts(GoalRun run) => FreeUnbegunCasts(run, queuedOnly: true);

    private void FreeUnbegunCasts(GoalRun run, bool queuedOnly)
    {
        var caster = _scene.Spells?.Invoke();
        foreach (var frame in run.Frames)
        {
            if (frame is not { Goal: GoalType.CastSpell or GoalType.CastSpellWithAnimation, SpellRun: { } id }) continue;
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

    /// <summary>ARC-DEV-118: the evidence the planners and steps read: the loaded sectors' chart when the scene has one that holds a sector, else the scene's.</summary>
    private AreaStepSnapshot Steps => _scene.LoadedSteps?.Invoke() ?? _scene.Steps;

    /// <summary>ARC-DEV-118: whether a cell is a place the shown map holds: admitted by the scene's evidence or by a loaded sector's (a relocation is no walk,
    /// so it does not wait for its sector to load).</summary>
    private bool Admitted(AreaStepCell cell) => _scene.Steps.IsAdmitted(cell) || _scene.LoadedSteps?.Invoke()?.IsAdmitted(cell) == true;

    /// <summary>The most unloaded sectors one planned path touches (the cache holds 16: the arrival's 3 × 3 pre-cache and the view square already take most of them).</summary>
    public const int PathTouchLimit = 4;

    /// <summary>
    /// ARC-DEV-118 (MIG9 S5b, K1 §3.5, RL11), ARC-DEV-119 (S5c): a planner reads the tiles it plans over and the original's tile reads lock the sector of a tile
    /// whose sector is not loaded; the host touches the sectors a path goes through when the scene has a loaded-sector chart and the session holds the map's
    /// sector cache: the sectors on the straight line from the NPC to the goal, the corridor the planner's search reads first, the goal's last. At most
    /// <see cref="PathTouchLimit"/> unloaded ones, the nearest to the NPC first (a longer walk loads the next ones when its next plan asks); a loaded sector is left alone. The
    /// chart reads what entered the cache when its sync runs (<see cref="AreaAiScene.SectorsTouched"/>), so the next question sees the evidence. A deliberate
    /// difference (K1 §4): the cells the search explores beside the line are not touched one by one.
    /// </summary>
    private void TouchPath(CellCoord from, CellCoord goal)
    {
        if (_scene.LoadedSteps is null || goal.X < 0 || goal.Y < 0 || from.X < 0 || from.Y < 0) return;
        var map = new SessionMapId(_scene.RuntimeMap);
        if (_session.QuerySectors(map)?.Map is not { } extent) return;
        var cells = new List<CellCoord>();
        // The NPC's own sector is not part of the corridor: an NPC whose sector was evicted does not load it by planning (its fold is the cache's to run).
        var seen = new HashSet<ResidentSector> { ResidentSector.Of(from) };
        foreach (var cell in Corridor(from, goal))
        {
            var sector = ResidentSector.Of(cell);
            if (sector.X >= extent.SectorsX || sector.Y >= extent.SectorsY || !seen.Add(sector) || _session.QuerySectorLoaded(map, sector) != false) continue;
            cells.Add(cell);
            if (cells.Count == PathTouchLimit) break;
        }
        if (cells.Count == 0) return;
        _session.TouchSectors(_session.Version, map, cells);
        _scene.SectorsTouched?.Invoke();
    }

    // The cells of the straight line from one cell to another every 4 cells (a sector is 64), the end last.
    private static IEnumerable<CellCoord> Corridor(CellCoord from, CellCoord to)
    {
        long dx = to.X - from.X, dy = to.Y - from.Y;
        var n = Math.Max(Math.Abs(dx), Math.Abs(dy));
        for (long i = 0; i < n; i += 4) yield return new(checked((int)(from.X + Math.Round(dx * (double)i / n))), checked((int)(from.Y + Math.Round(dy * (double)i / n))));
        yield return to;
    }

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
        return new(Pc: id == Pc, KnockedDown: id != Pc && status is { IsDead: false, IsUnconscious: false } && ShownAnimation(id) == DyingAnimationRules.FallDown,
            Concealed: false, Fatigue: status?.Fatigue ?? 0, MaxFatigue: status?.MaxFatigue ?? 0, CannotTurn: !Active(id),
            SpreadOut: id != Pc && _session.QueryCompanion(id).SpreadOut);
    }

    /// <summary>The terrain step from the object's tile toward the other's: walls, doors and objects other than critters decide
    /// (inference: the step test 0x425130 measures the way, the occupants are the walk's own test). ARC-WORLD-115 (ledger AP1: the
    /// target is made no-block for the test, other critters still block): the walker's overlay toward the target
    /// (<see cref="Occupancy"/>: the target's T0 record and every moved record away, the live cells of the hero, the companions and the
    /// other movers held). Before, an attacker next to an NPC standing on its own record's cell read that record as an object in its
    /// way, so its approach found no first step and no path.</summary>
    public bool FirstStepClear(string id, string toward, bool ignoreTarget)
    {
        if (Tile(id) is not { } from || Tile(toward) is not { } to) return false;
        if (from == to) return true;
        var chart = ToChart(from);
        var direction = RoutePlanner.Toward(chart, ToChart(to));
        return new AreaRouteSource(Steps, _scene.Chart, () => Occupancy(id, toward)).Assess(chart, direction) == RouteStepVerdict.Open;
    }

    public AiAttackFacts AttackFacts(string id, string target)
    {
        var line = Line(id, target, out var targetRow);
        var foreign = line?.Blocker is { } blocker && !ReferenceEquals(blocker, targetRow);
        return new(AttackerInactive: !Active(id), TargetGone: Gone(target), TargetDead: DeadCritter(target), ForeignBlocker: foreign,
            NoPathToTarget: foreign && PlanPath(id, new AiPathRequest(AiPathKind.Attack, target, null, 1, false, false), touch: false) is null);
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

    public AiPath? PlanPath(string id, AiPathRequest request) => PlanPath(id, request, touch: true);

    // A facts question (AttackFacts) plans without touching: a touch can load a sector and run its fold, which a question must not do.
    private AiPath? PlanPath(string id, AiPathRequest request, bool touch)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Tile(id) is not { } from) return null;
        var goal = request.Tile ?? (request.Target is { } target ? Tile(target) : null);
        if (goal is not { } to) return null;
        if (touch) TouchPath(from, to);
        var source = new AreaRouteSource(Steps, _scene.Chart, () => Occupancy(id, request.Target));
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
        var open = new AreaRouteSource(Steps, _scene.Chart, MovedRecords).Assess(chart, direction) == RouteStepVerdict.Open;
        return open ? AiStepBlock.Clear : flight ? AiStepBlock.Occupied : AiStepBlock.CannotStart;
    }

    /// <summary>The step onto <paramref name="next"/>: a map NPC's mover takes the cell at once and slides its body there over the step's
    /// time; ARC-WORLD-115: a companion's own walk is told to step there (<see cref="IAreaAiFollowers.Step"/>), its cell changing as
    /// that walk goes.</summary>
    // ARC-WORLD-115: the step's terrain test with the T0 records the planner moves too (the moved NPCs' and the companions'; the ones
    // the session removed), no live cells (the destination's critter is LivingCritterOn's): a route through a companion's home cell or
    // a walked NPC's origin is then not refused step by step while its planner keeps planning it.
    private AreaOccupancy MovedRecords()
    {
        var occupancy = new AreaOccupancy(Movers.Moved.Concat(_followers.Values.Select(n => n.Row.Key))
            .Concat((_scene.Companions?.Invoke() ?? []).Select(c => c.Key)), []);
        return _scene.Removed?.Invoke() is { Count: > 0 } removed ? occupancy.WithRemoved(removed) : occupancy;
    }

    /// <summary>ARC-DEV-124: the steps a run began while a round ran and the round's actor was another participant (a diagnostic: the executor pays a step with the
    /// round's action points, so a step off its turn is a defect; the ai-turn-based smoke requires none).</summary>
    public int OffTurnSteps { get; private set; }

    public int BeginStep(string id, CellCoord next, bool running)
    {
        if (!TryNpc(id, out var npc) || Tile(id) is not { } from) return 1;
        if (InRound && RoundActor != id) OffTurnSteps++;
        var gait = running ? MovementGait.Run : MovementGait.Walk;
        var direction = AreaCombatHost.Facing(from, next) ?? NeighborDirection.XPlus;
        var duration = _scene.Timing?.For(gait, direction) ?? DiagnosticGridSession.LiveDuration;
        var ms = (int)Int128.Clamp(duration.Value.Numerator * 1000 / duration.Value.Denominator, 1, 10_000);
        if (_followers.ContainsKey(id)) { if (!_scene.Followers!.Step(id, next, running)) Trace(id).Step = $"walk refused the step to {next.X},{next.Y}"; }
        else { Movers.Step(id, npc.Row.Key, npc.Origin, next, _nowMs, ms, running); RecordPlace(id); }
        _notices.Add(new(AreaAiNoticeKind.Step, id, null));
        return ms;
    }

    public int GetUp(string id) => 1;
    public int Unconceal(string id) => 1;

    // ---- the critter states (ARC-DEV-137, MIG13 S6: goals 57, 62, 63 and 31) ----

    // The art each NPC shows now, as the critter-state goals set it (the original's object art: animation and frame). An NPC with no entry shows what its life says (a dead or unconscious one
    // lies in its fall, others stand), as a body first seen after a load or a map change does.
    private readonly Dictionary<string, (int Animation, int Frame)> _arts = [];

    /// <summary>Counts the art writes of the critter-state goals, so the viewer redraws the lying bodies when a frame changes.</summary>
    public long ArtRevision { get; private set; }

    /// <summary>The art the critter-state goals set for the NPC (null: none set, or standing again).</summary>
    public (int Animation, int Frame)? ArtOf(string id)
    {
        if (!_arts.TryGetValue(id, out var art)) return null;
        // A dying animation of an NPC that is no longer dead (a resurrection) is a stale write, not a pose.
        if (art.Animation is >= DyingAnimationRules.Decapitation and <= DyingAnimationRules.SeveredLeg && Status(id) is { IsDead: false }) { _arts.Remove(id); return null; }
        return art;
    }

    /// <summary>Whether a dying run (goal 31) of the NPC is on the table: its pool of blood waits for the run to end (B13, 0x42FED0).</summary>
    public bool DyingRunning(string id) => _session.QueryGoals().RunsOf(id).Any(run => run.Frames.Any(f => f.Goal == GoalType.Dying));

    public AiAnimationClip? Clip(string id, int animation) => _scene.AnimationClip?.Invoke(id, animation);

    // An NPC with no art written lies in its fall when it is unconscious or dead (a body first seen), except a dead one whose dying goal is on the table: it was killed standing and the goal sets the art.
    public int ShownAnimation(string id) => _arts.TryGetValue(id, out var art) ? art.Animation
        : Status(id) switch { { IsDead: true } => DyingRunning(id) ? 0 : DyingAnimationRules.FallDown, { IsUnconscious: true } => DyingAnimationRules.FallDown, _ => 0 };

    public int ShownFrame(string id) => _arts.TryGetValue(id, out var art) ? art.Frame : ShownAnimation(id) == 0 ? 0 : Math.Max((Clip(id, ShownAnimation(id))?.Frames ?? 1) - 1, 0);

    public void ShowArt(string id, int animation, int frame)
    {
        if (animation == 0) _arts.Remove(id);
        else _arts[id] = (animation, frame);
        ArtRevision++;
    }

    public bool Unconscious(string id) => Status(id)?.IsUnconscious ?? false;

    public bool Stoned(string id) => Fields(id) is { } fields && (fields.Object.SpellFlags & StonedSpell) != 0;

    public bool Paralysed(string id) => Fields(id) is { Critter: { } critter } && (critter.Flags & ParalysedCritter) != 0;

    public void ClearStunned(string id)
    {
        if (id != Pc && Build(id) is { } build && LifeState(id) is { } state && state.Conditions.HasFlag(LifeConditions.Stunned)) _session.ClearStun(_session.Version, build.Target);
    }

    public void DyingFinished(string id) { }

    public void Face(string id, string toward)
    {
        if (TryNpc(id, out var npc) && Tile(toward) is { } there) FaceNpc(npc, there);
    }

    public void PlaySound(string id, int sound) => _notices.Add(new(AreaAiNoticeKind.Sound, id, null) { Sound = sound });

    /// <summary>
    /// The attempt-attack goal's start: its node 5 (0x42B9C0, read by this task) puts the attacker into combat mode, and a PC target,
    /// or an NPC target's living PC leader, too (0x4B3F50); then the turn and the attack animation (0x432700): the NPC's attack ART
    /// played at its Speed and weapon speed (<see cref="RealTimeCombatRules"/>), whose action frame is the hit.
    /// </summary>
    public AiAttackSwing BeginAttack(string id, string target)
    {
        if (!TryNpc(id, out var npc) || Values(id) is not { } values) return new(1, 1);
        if (NpcCombatMode(id) is { } context) _session.SetCombatMode(_session.Version, SessionCombatant.Critter(id), true, context);
        if (target == Pc || _session.QueryCompanion(target).LedByPc && !(Status(Pc)?.IsDead ?? true)) HeroIntoCombatMode();
        Face(id, target);
        // ARC-RENDER-038: the attack ART of the animation and the weapon the attacker's body shows (WA1, WA4), its frames, action frame
        // and the rate of its weapon slot; an ART the viewer cannot read plays with the fixed human attack ART's timing.
        var wanted = AttackRequest(id, target);
        var art = _scene.AttackArt?.Invoke(npc, wanted) is { Frames: >= 1 and <= RealTimeCombatRules.MaxFrames, ActionFrame: >= 0 and <= RealTimeCombatRules.MaxFrames } read
            ? read : null;
        var (frames, actionFrame, rate, weapon) = art is null
            ? (FixedAttackArt.Frames, FixedAttackArt.ActionFrame, RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Unarmed,
                Math.Clamp(values.Speed, 1, RealTimeCombatRules.MaxStat), FixedAttackArt.RawFramesPerSecond), AttackArtWeapon.Unarmed)
            : (art.Frames, art.ActionFrame, AreaAiAttackArtRules.FramesPerSecond(art, wanted.Animation, values.Speed), AreaAiAttackArtRules.ClipWeapon(art));
        var clip = new AttackClip(frames, actionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, Math.Clamp(values.WeaponSpeed, -RealTimeCombatRules.MaxStat, RealTimeCombatRules.MaxStat)),
            weapon);
        var moments = RealTimeCombatRules.Schedule(clip);
        var hit = moments.FirstOrDefault(m => m.Kind == AttackUpdateKind.Hit);
        var end = moments[^1];
        var action = (int)Math.Max(1, hit.Kind == AttackUpdateKind.Hit ? hit.AtMs : end.AtMs);
        var swing = new AiAttackSwing(action, (int)Math.Max(1, end.AtMs - action));
        var play = new AreaAiAttackPlay(id, _nowMs, wanted, art, moments);
        _attackPlays[id] = play;
        _notices.Add(new(AreaAiNoticeKind.Swing, id, target) { Swing = swing, Play = play });
        return swing;
    }

    /// <summary>ARC-RENDER-038: the NPC's attack animation playing at <paramref name="nowMs"/> and its frame, or null.</summary>
    public (AreaAiAttackPlay Play, int Frame)? AttackPose(string id, long nowMs) =>
        _attackPlays.TryGetValue(id, out var play) && play.Frame(nowMs) is { } frame ? (play, frame) : null;

    /// <summary>
    /// ARC-RENDER-038: the weapon part and shield bit the NPC's body shows now (0x465020, ledger WA1): none out of combat mode; in it the
    /// use-art subtype of the item it wields (bare hands without one) and the shield bit for an armour item in its shield slot, its worn
    /// items as the session shows them (<see cref="AreaCombatHost.Worn"/>; none read: bare hands).
    /// </summary>
    public (int Weapon, int Shield) ShownWeapon(string id)
    {
        if (id == Pc) return (0, 0);
        // Asked per mover and frame: kept per session revision (combat mode and worn items change only with it).
        var revision = _session.Version.Revision;
        if (_shownRevision != revision) { _shown.Clear(); _shownRevision = revision; }
        if (_shown.TryGetValue(id, out var known)) return known;
        var shown = (0, 0);
        if (_session.QueryCombatMode(SessionCombatant.Critter(id)))
        {
            var (use, _, shield) = Wielded(id);
            shown = (WeaponArtRules.WeaponPart(true, use), WeaponArtRules.ShieldPart(true, shield));
        }
        _shown[id] = shown;
        return shown;
    }

    /// <summary>
    /// ARC-RENDER-038: the attack ART an NPC's attack on <paramref name="target"/> wants (WA1, WA4): its wielded weapon's part and flags,
    /// its shield, and the target's stored object flags (the shrunk flag), its art (its height; the hero's is a critter's) and its current
    /// animation, read as lying (7) while it is unconscious or dead, else its stored art's (<see cref="WeaponArtRules.AnimationOf"/>; a
    /// target mid-fall, getting up or knocked down is NOT READY).
    /// </summary>
    public AreaAiAttackArtRequest AttackRequest(string id, string target)
    {
        var (use, flags, shield) = Wielded(id);
        var targetFields = Fields(target);
        var lying = Status(target) is { IsUnconscious: true } or { IsDead: true };
        var targetArt = targetFields?.Object.CurrentArt ?? BodyArmorArtRules.CritterArt << 28;
        return AreaAiAttackArtRules.Request(use, flags, shield, targetFields?.Object.Flags ?? 0,
            lying ? AreaAiAttackArtRules.LyingAnimation : WeaponArtRules.AnimationOf(targetArt), targetArt);
    }

    // The use art and weapon flags of the item in the NPC's weapon slot and the type of the item in its shield slot.
    private (uint? UseArt, uint Flags, int? ShieldType) Wielded(string id)
    {
        var worn = _scene.Combat.Worn(_session, id);
        var weapon = worn?.FirstOrDefault(item => item.Slot == EquipmentSlot.Weapon);
        var shield = worn?.FirstOrDefault(item => item.Slot == EquipmentSlot.Shield);
        return (weapon is { Entity: not null } w ? w.Fields.Item?.UseArtFragment : null,
            weapon is { Entity: not null } f ? (uint)(f.Fields.Weapon?.Flags ?? 0) : 0,
            shield is { Entity: not null } s ? s.Fields.Object.Type : null);
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
            AttackerWorn = _scene.Combat.WornItems(_session, id),
            TargetWorn = target == Pc ? _scene.HeroWorn?.Invoke() : _scene.Combat.WornItems(_session, target),
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
    /// re-check refused it (the run freed unpaid in both settings, MG41) or the frame was released before.</summary>
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
    // ---- the skill goals (ARC-WORLD-109) ----

    /// <summary>The turn to an action's target object or tile (0x42C0F0, 0x42C390): refused for an object the host does not walk (the
    /// hero, a companion the host does not run), a target without a cell, object flag 0x10000 or a stunned or paralysed critter
    /// (critter flags 0x60, or the session's stun).</summary>
    public bool FaceForAction(string id, string? target, CellCoord? tile)
    {
        if (!TryNpc(id, out var npc) || !CanAct(id)) return false;
        if ((target is null ? tile : Tile(target)) is not { } there) return false;
        FaceNpc(npc, there);
        return true;
    }

    /// <summary>
    /// An action animation's start (0x42BD40, 0x42CB10, 0x42D300): the skill animation (12) of goals 34 and 86 to its action frame and
    /// rest; goal 67's pick-locks loops, as many as the pick-locks starter (0x436220, AG12) counts from the art's frames (frames ÷
    /// max(frames − 2, 1) + 1, halved when the NPC's pick-locks training is apprentice or more), the attempt after the last;
    /// ARC-WORLD-113: goal 37's throw animation (14) to its action frame, the throw, and rest. The art is the fixed stand-in's (NPC ART
    /// is not read yet), played at its raw rate. An object flag 0x10000 or a stunned or paralysed critter cannot start.
    /// </summary>
    public AiActionSwing BeginAction(string id, AiActionAnimation animation)
    {
        if (!Walks(id) || !CanAct(id)) return new(AiActionStart.CannotStart);
        var art = FixedSkillArt;
        var frameMs = 1000.0 / art.RawFramesPerSecond;
        if (animation == AiActionAnimation.PickLocks)
        {
            var loops = art.Frames / Math.Max(art.Frames - 2, 1) + 1;
            if (NpcSkill(id, Skill.PickLocks)?.Training is { } training && training >= SkillTraining.Apprentice) loops /= 2;
            return new(AiActionStart.Started, (int)Math.Max(1, Math.Round(loops * art.Frames * frameMs)), 0);
        }
        var action = (int)Math.Max(1, Math.Round(art.ActionFrame * frameMs));
        return new(AiActionStart.Started, action, (int)Math.Max(1, Math.Round(art.Frames * frameMs)) - action);
    }

    /// <summary>
    /// The skill invocation at the attempt's action frame (0x42A2A0 → 0x4C7090, 0x4C7160; the 4 action points are already paid): the
    /// heal skill (10) is the session's <see cref="GameSessionOwner.HealByNpc"/> with the NPC's tool item (it must be a healing item the
    /// NPC holds; without one the heal is impossible and nothing changes); its answer is the check's success, which sets the run's flag
    /// 0x40000 (ledger AK2). NOT READY: every other skill (pickpocket, the other skills a script might start), which does nothing and
    /// answers false.
    /// </summary>
    public bool AttemptSkill(string id, string target, int skill, string? item)
    {
        if (skill != AiPassChoice.HealSkill)
        {
            Trace(id).Skill = $"skill {skill} on {target}: not ready";
            _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = skill });
            return false;
        }
        var heal = HealByNpc(id, target, item, out var refusal);
        Trace(id).Skill = heal is null ? $"heal {target}: {refusal}"
            : $"heal {target} {heal.Disposition} {heal.Failure} success={heal.Check?.Success} hp={heal.Heal?.HitPoints} uses={heal.UsesLeft?.ToString() ?? "-"}";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = skill, Heal = heal });
        return heal is { Disposition: GameSessionDisposition.Changed, Check.Success: true };
    }

    /// <summary>The pick-locks attempt (0x42A4E0). NOT READY: an NPC's lock picking (the session's is the hero's) does nothing.</summary>
    public void AttemptPickLocks(string id, string target, string? item)
    {
        Trace(id).Skill = $"pick locks {target}: not ready";
        _notices.Add(new(AreaAiNoticeKind.Skill, id, target) { Skill = AiExecutorRules.PickLocksSkill });
    }

    // The heal: the healer's sheet, the target's life and the held healing item; null with the refusal when the session is not asked.
    private SessionHealResult? HealByNpc(string id, string target, string? item, out string refusal)
    {
        refusal = "";
        if (Build(id) is not { } healer || _scene.NpcSheet?.Invoke(id) is not { } sheet) { refusal = "no sheet"; return null; }
        var life = target == Pc ? SessionLifeTarget.Hero : Build(target)?.Target;
        if (life is null) { refusal = "no target"; return null; }
        if (item is null || _scene.Combat.HeldItemFields(_session, id, item) is not { } fields
            || AreaHealingItems.From(item, fields) is not { } healing)
        {
            refusal = "no healing item (impossible)";
            return null;
        }
        return _session.HealByNpc(_session.Version, new SessionNpcHeal(healer.Target, sheet.Loaded, sheet.Modifiers, life, healing));
    }

    // Object flag 0x10000 and a stunned or paralysed critter refuse an action's turn and start.
    private bool CanAct(string id)
    {
        if (Fields(id) is not { } fields) return false;
        if ((fields.Object.Flags & ActionRefusedObject) != 0 || fields.Critter is { } critter && (critter.Flags & (StunnedCritter | ParalysedCritter)) != 0) return false;
        return !(LifeState(id)?.Conditions.HasFlag(LifeConditions.Stunned) ?? false);
    }

    private SessionSkill? NpcSkill(string id, Skill skill) =>
        _scene.NpcSheet?.Invoke(id) is { } sheet ? _session.QueryNpcSkills(sheet.Loaded, sheet.Modifiers)[skill] : null;

    // ---- the throw (ARC-WORLD-113) ----

    /// <summary>
    /// The throw at goal 37's action frame (0x42AB90 → 0x4B3C00, ledger TW rows, TA rows): the session's <see cref="GameSessionOwner.ThrowByNpc"/>
    /// with the NPC's live cell, the item's weight as the thrower holds it (0x461370 with the thrower, TW5: its stored weight and its
    /// magic adjustment under the thrower's aptitude) and the critter the line of fire to the tile names (TW4,
    /// <see cref="StruckAt"/>), then the attack event on it for a resolved attack (the projectile's end alerts the struck object, PF8).
    /// The item must be one the NPC holds (an item gone or taken fails, AG10). A <see cref="AreaAiNoticeKind.Throw"/> notice tells the
    /// viewer. NOT READY: the projectile's flight (the item lands at once), and a first blocker on the line that is a scenery, a wall,
    /// a dead or unknown critter, and the hero on the line to another target (the line's map holds no hero): the item then lands on the
    /// tile, striking nothing.
    /// </summary>
    public bool ThrowItem(string id, string item, CellCoord tile)
    {
        if (!Walks(id) || Build(id) is not { } thrower || Tile(id) is not { } from || LifeState(id) is not { } state) return false;
        if (_scene.Combat.HeldItemFields(_session, id, item) is not { Item: { } stored } fields)
        {
            Trace(id).Throw = $"throw {item}: not held";
            return false;
        }
        var aptitude = Math.Clamp(thrower.EffectiveStat(CharacterStat.MagickTechAptitude, state), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        int weight;
        try
        {
            weight = Math.Clamp(Arcanum.Simulation.Items.CarryRules.ItemWeight(new(fields.Object.Type, stored.Weight, stored.MagicWeight,
                stored.MagickTechComplexity), Arcanum.Simulation.Items.CarryHolder.Critter, aptitude), 0, ThrowRules.MaxWeight);
        }
        catch (ArgumentException)
        {
            Trace(id).Throw = $"throw {item}: weight facts out of range";
            return false;
        }
        var struck = StruckAt(id, from, tile);
        var hero = struck?.Who == Pc;
        var request = new SessionNpcThrow(thrower, item, weight, from, tile)
        {
            Struck = struck is { Who: { } who } && who != Pc ? Build(who) : null,
            StruckHero = hero,
            StruckCell = struck?.Cell,
            LineOfFire = struck is { } s && _scene.LineOfFire?.Invoke() is { } map ? LineTo(from, s.Cell, s.Key, map) : null,
            ThrowerWorn = _scene.Combat.WornItems(_session, id),
            StruckWorn = struck is not { Who: { } target } ? null : hero ? _scene.HeroWorn?.Invoke() : _scene.Combat.WornItems(_session, target),
            DropMap = _scene.DropMap?.Invoke(),
        };
        var result = _session.ThrowByNpc(_session.Version, request);
        var attack = result.Attack;
        Trace(id).Throw = $"throw {item} at {tile.X},{tile.Y} struck={struck?.Who ?? "-"} {result.Disposition} {result.Failure} {attack?.Resolution?.Outcome}"
            + $" landed={(result.Landed is { } at ? $"{at.X},{at.Y}" : "-")}";
        _notices.Add(new(AreaAiNoticeKind.Throw, id, struck?.Who) { Item = item, Throw = result, Result = attack });
        if (struck?.Who is { } victim && attack is { Disposition: not GameSessionDisposition.Rejected, Resolution.Refusal: AttackRefusal.None })
            AttackEvent(id, victim);
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>The throw's struck critter (TW4): the first object the line of fire from <paramref name="from"/> to the tile stops at
    /// when it is a living critter the host knows (a walked NPC or a companion), else the living critter on the tile (the hero, a walked
    /// NPC, a companion), with its cell and map key (none for the hero); null when it strikes no critter (a scenery or wall blocker
    /// among them: not ready).</summary>
    private (string Who, CellCoord Cell, AreaObjectKey? Key)? StruckAt(string id, CellCoord from, CellCoord tile)
    {
        if (_scene.LineOfFire?.Invoke() is { } map && LineOfFireRules.Trace(from, 0, 0, tile, map, _session.Rules.Combat).Blocker is { } row)
            return EntityOf(row.Key) is { } blocker && blocker != id && Tile(blocker) is { } at && !DeadCritter(blocker)
                && (Walks(blocker) || CompanionCell(blocker) is not null) ? (blocker, at, row.Key) : null;
        if (Tile(Pc) == tile && !DeadCritter(Pc)) return (Pc, tile, null);
        foreach (var npc in _scene.Npcs)
            if (npc.Entity != id && Tile(npc.Entity) == tile && !DeadCritter(npc.Entity)) return (npc.Entity, tile, npc.Row.Key);
        foreach (var (companion, key, cell) in _scene.Companions?.Invoke() ?? [])
            if (cell == tile && companion != id && !DeadCritter(companion))
                return (companion, tile, key);
        return null;
    }

    // The line of fire to the struck critter's cell as the check reads it (the hero's attack's SessionLineOfFire): its cost and whether
    // an object other than the struck one stops it.
    private SessionLineOfFire LineTo(CellCoord from, CellCoord to, AreaObjectKey? struck, ILineOfFireMap<AreaObjectRecord> map)
    {
        var trace = LineOfFireRules.Trace(from, 0, 0, to, map, _session.Rules.Combat);
        return new(Math.Min(trace.Cost, AttackResolver.MaxMagnitude), trace.Blocker is { } b && b.Key != struck);
    }

    public void PickUp(string id, string item) { }
    /// <summary>ARC-WORLD-108: the NPC that holds the item now (0x4612A0; ARC-DEV-075: of any map the host holds, a visiting companion's holder entry is its home map's), or null (the hero's items are not the AI's).</summary>
    public string? ItemHolder(string item) => _scene.Combat.NpcHolder(_session, item);

    // ---- ARC-WORLD-108: an NPC's item use (0x462CC0: goal 39's last node and the chooser's use at once) ----

    /// <summary>The item use's facts of an NPC's item: its fields' type and flags (<see cref="AreaItemUseFacts"/>), its spell charges and
    /// magical flag as the cast host's item lookup gives them, and the scroll test (a scroll and the user's intelligence below 5,
    /// 0x4B1750). An item the NPCs do not hold reads as an empty use.</summary>
    public AiUsedItem UsedItem(string user, string item)
    {
        if (UseFacts(item) is not { } facts) return new();
        var tooHard = facts.Type == AreaItemUseFacts.ScrollType && (Find(user)?.Intelligence ?? 0) < SpellBookRules.MinimumIntelligence;
        return AreaItemUse.Used(facts, _scene.Spells?.Invoke()?.Item(item), tooHard);
    }

    /// <summary>The use script (0x441980): NOT READY for an NPC (no SCR runs at the AI points); an item with one goes on as if the
    /// script let it, which the trace notes.</summary>
    public bool RunUseScript(string user, string item, string? target)
    {
        if (UseFacts(item) is { UseScript: true }) Trace(user).Item += $" use-script {item} not run";
        return true;
    }

    // The targeting cursor (0x4EE060) is the player's: an NPC's use always has a target (the starter's and the chooser's), so it never opens.
    public void OpenTargeting(string user, string item) => Trace(user).Item += $" targeting {item} not ready";
    // NOT READY for an NPC: a trap device's use, reading a written item, picking a lock and the heal skill with an item (goal 40, 0x4352C0).
    public void UseTrapDevice(string user, string item, string? target) => Trace(user).Item += $" trap-device {item} not ready";
    public void ReadWritten(string user, string item) => Trace(user).Item += $" read {item} not ready";
    public bool Lockable(string target) => false;
    public void StartSkillWithItem(string user, string item, string target, int skill) => Trace(user).Item += $" skill {skill} with {item} not ready";

    /// <summary>The magic branch (0x4F2810 … 0x4CBF70) through the cast host's item use (<see cref="SessionItemTriggers.UseItem"/>):
    /// for an NPC the slot-0 activation casts nothing (NU2), the item used executor's spells start at the target and play at once,
    /// a food or scroll is spent. Without the map's cast host or its item executor nothing happens (the trace says so).</summary>
    public void UseItemMagic(string user, string item, string? target)
    {
        if (_scene.Spells?.Invoke()?.ItemTriggers is not SessionItemTriggers triggers) { Trace(user).Item += $" magic {item} needs the cast host"; return; }
        var used = triggers.UseItem(item, target);
        NoteUsedItem(user, item);
        Trace(user).Item += $" magic {item} {used.Outcome} {used.Cast} fired={used.ItemUsed?.Spells.Count ?? 0} spent={used.Spent}";
        _notices.Add(new(AreaAiNoticeKind.ItemUse, user, target) { Item = item, ItemUse = used });
    }

    /// <summary>Spends a used food or scroll the magic branch did not already spend (<see cref="SessionSpellCaster.SpendUsedItem"/>: the
    /// deletion out of the NPC's list, then the runs' sources cleared).</summary>
    public void SpendItem(string item)
    {
        if (_session.IsItemDestroyed(item) || _scene.Spells?.Invoke() is not { } caster) return;
        if (ItemHolder(item) is { } holder) NoteUsedItem(holder, item);
        caster.SpendUsedItem(item);
    }

    // An item the NPC used or cast from, whose cached list slot the next list request checks.
    private void NoteUsedItem(string npc, string item)
    {
        if (!_usedItems.TryGetValue(npc, out var items)) _usedItems[npc] = items = new HashSet<string>(StringComparer.Ordinal);
        items.Add(item);
    }

    // The item use's facts of an item an NPC holds, or null.
    private AreaItemUseFacts? UseFacts(string item) => ItemFields(item) is { } fields ? AreaItemUseFacts.From(item, fields) : null;

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
    /// ARC-WORLD-108: a choice with an item casts the item's spell with the item as source (a scroll or food item spent, MG47 for the
    /// others' run sources). NOT READY: a cast while goal 82 or 83 is at the run's bottom, which the original starts as a second run
    /// beside it (AC5; the host holds one run per NPC): it starts no run.
    /// </summary>
    public AiCastStart Cast(string npc, AiPassChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (_scene.Spells?.Invoke() is not { } caster) return new(null);
        if (RunOf(npc)?.Bottom.Goal is GoalType.CastSpellWithAnimation or GoalType.AttemptSpellWithAnimation)
        {
            Trace(npc).Cast = $"cast {choice.Spell} not started (goal 82 or 83 plays: a second run)";
            return new(null);
        }
        var attempt = caster.CastByAi(npc, choice.Spell, choice.Target, choice.Item);
        if (choice.Item is { } fromItem) NoteUsedItem(npc, fromItem);
        Trace(npc).Cast = $"cast {choice.Spell}{(choice.Item is null ? "" : " from " + choice.Item)} at {choice.Target ?? "-"} {attempt.Outcome} {attempt.Run?.Failure} {attempt.Run?.Creation?.Check?.Refusal}";
        if (attempt.RunId is not { } run) return new(null);
        var castAnimation = (caster.SpellRecord(choice.Spell)?.Run.CastAnimation ?? -1) != -1;
        if (RunOf(npc) is not { } held || held.Bottom.Goal is not (GoalType.CastSpell or GoalType.AttemptSpell))
            return new(run, CastAnimation: castAnimation);
        var depth = held.Frames.Length - 1;
        var queued = caster.Queue(run, depth);
        Trace(npc).Cast += $" queue depth={depth} {queued}";
        if (queued == SpellCastQueueOutcome.Queued)
        {
            // The perform's target, as a started goal's; a queued goal whose run was freed meanwhile (QC6) lost its slot to this one.
            var target = choice.Target ?? (_session.QueryDanger(npc).Source is { } source ? Id(source) : null);
            var goal = new GoalFrame(castAnimation ? GoalType.CastSpellWithAnimation : GoalType.CastSpell, target) { SpellRun = run };
            var frames = held.Frames.RemoveAll(f => !ReferenceEquals(f, held.Top) && f.SpellRun == run
                && f.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation).Insert(0, goal);
            // ARC-DEV-134: the queued goal goes beneath the stack in the session's table (GS14). A stack the table refuses (eight goals, the run being stepped) has no
            // goal to play the queued run, which is freed unpaid as a dropped queue's.
            var refused = _session.ReplaceGoalFrames(held.Handle, frames);
            if (refused == SessionGoalFailure.None) _queuedCasts.Add(run);
            else
            {
                if (OwnRun(npc, run) && caster.DropQueued(run) is { } freed) _notices.Add(new(AreaAiNoticeKind.CastEnd, npc, null) { Run = run, SpellRun = freed });
                Trace(npc).Cast += $" not queued in the table: {refused}";
                return new(null, castAnimation);
            }
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

    /// <summary>ARC-WORLD-113: the attack starter's throw (0x434B00, TW1, AG13): the item the NPC wears in its weapon slot (the session's
    /// view) when it is a weapon with the "default throws" flag (weapon flag 0x200) that the remove check (0x466DA0: no no-drop flag)
    /// lets go. Not ready: a weapon the session's throw refuses (one with scripts, or one the session added to the NPC, which no seed
    /// holds) is answered as not throwable, so the NPC attacks with it instead of starting a throw that always fails.</summary>
    public string? ThrowableWeapon(string npc)
    {
        if (!Walks(npc) || _scene.Combat.Worn(_session, npc) is not { } worn) return null;
        foreach (var item in worn)
            if (item.Slot == EquipmentSlot.Weapon && item.Fields.Weapon is { } weapon && weapon.Flags.HasFlag(CombatWeaponFlags.DefaultThrows) && !item.Wear.NoDrop
                && item.Fields.Object.ScriptPoints.Count == 0 && !_session.QueryNpcInventory(npc).Any(made => made.Entity == item.Entity))
                return item.Entity;
        return null;
    }

    /// <summary>ARC-WORLD-113: the attack starter's removal (0x466E00, read by this task: 0x4612A0 then 0x467860, the item leaves its
    /// holder's inventory entirely, ledger TA1). The original then loses the weapon whenever the throw goal never reaches its throw (a
    /// refused start, an interrupted or replaced run); a lost object, so both rule modes keep it in the NPC's hand until the throw,
    /// which removes it (<see cref="GameSessionOwner.ThrowByNpc"/>). Nothing changes here.</summary>
    public void Unwield(string npc, string item) => Trace(npc).Throw = $"unwield {item} (kept until the throw)";
    /// <summary>ARC-WORLD-109: the skill animation's art (0x436220's test) is the fixed stand-in's, so every NPC the host walks has it.</summary>
    public bool HasSkillArt(string npc) => Walks(npc);
    public bool NpcPickupsOn => false;
    public bool BoomerangInFlight(string npc) => false;
    // The starters' interrupt (0x424070 at level 3, GS13): the end nodes of the runs it ended run now, and a refusal (a run whose current goal has priority 4 or 5) refuses the start.
    public bool InterruptLowerAnimations(string id) => InterruptOwner(id, GoalInterruptRules.OrdinaryActions).Succeeded;
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
        // ARC-WORLD-108: an item the NPC used or cast from that is gone drops the NPC's slot (the force-removal 0x467860 → 0x4CCC00), one
        // out of charges drops it when a list holds the item (0x450420 → 0x4CCC40), once each, so the Original setting's frozen list
        // lists it no more.
        if (_usedItems.TryGetValue(critter, out var used))
            foreach (var item in used.ToArray())
            {
                if (_session.IsItemDestroyed(item)) _listCache = _listCache.Clear(critter);
                else if (_scene.Spells?.Invoke()?.Item(item) is { Charges: 0 }) _listCache = _listCache.ItemExhausted(critter, item);
                else continue;
                used.Remove(item);
            }
        var request = _listCache.Request(this, critter, list, _session.Rules.Combat);
        _listCache = request.Cache;
        return request.Entries;
    }

    /// <summary>ARC-WORLD-108: the NPC's wielded weapon (slot 1004, as the session shows what it wears) when it has item flag 0x40
    /// (hexed); null for the hero (the lists are an NPC's own) and without one.</summary>
    public string? HexedWeapon(string critter) =>
        critter != Pc && _scene.Combat.Worn(_session, critter)?.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is { Entity: { } weapon } worn
            && worn.Fields.Item is { } item && (item.Flags & HexedItem) != 0 ? weapon : null;

    /// <summary>ARC-WORLD-108: the NPC's items, listed or worn, in its list's order as the session shows them (the combat host's
    /// <see cref="AreaCombatHost.NpcItems"/>); none for the hero.</summary>
    public IReadOnlyList<string> Inventory(string critter) =>
        critter == Pc ? [] : [.. _scene.Combat.NpcItems(_session, critter).Select(item => item.Entity)];

    /// <summary>
    /// ARC-WORLD-108: an item as the list build and the chooser read it: the spell charges (field 0x69, as the cast host's item lookup
    /// gives them with the session's spent charges), identified (item flag 0x1, or identified in the session), exempt as food or a
    /// scroll, the five slots (10000 none → −1), the valid-AI-action flag (0x200000) and the AI action (field 0x6A). An item the NPCs
    /// do not hold reads as empty.
    /// </summary>
    public AiListItem Item(string item)
    {
        if (ItemFields(item) is not { Item: { } fields } all) return new();
        var spells = _scene.Spells?.Invoke()?.Item(item);
        return new AiListItem
        {
            ManaStore = spells?.Charges ?? fields.SpellManaStore,
            Identified = (fields.Flags & IdentifiedItem) != 0 || _session.QueryItemIdentified(item),
            IdentificationExempt = all.Object.Type is SessionItemSpells.FoodType or SessionItemSpells.ScrollType,
            Spells = [.. Enumerable.Range(0, ItemSpellTriggerRules.Slots).Select(slot => slot < fields.Spells.Count && fields.Spells[slot] != ItemSpellTriggerRules.NoSpell ? fields.Spells[slot] : -1)],
            ValidAiAction = (fields.Flags & ValidAiActionItem) != 0,
            AiAction = fields.AiAction,
        };
    }

    public bool CanUseItem(string critter, string item) => _scene.Spells?.Invoke()?.AiCanUseItem(critter, item) ?? false;
    public int ItemFailChance(string critter, string item, string target, int spell) =>
        _scene.Spells?.Invoke()?.AiItemFailChance(critter, item, target, spell) ?? 0;

    /// <summary>
    /// ARC-WORLD-116: the heal skill's tool (0x4C91F0 with skill 10 → 0x462760 with flag 0x8, ledger HI1): the first item of the NPC's
    /// list (fields 0xED/0xEE, listed or worn, <see cref="AreaCombatHost.NpcItems"/>) that is a generic item (type 14) with the healing
    /// flag (generic flags 0x8); none for the hero. NOT READY: the original then searches the substitute container of an NPC without a
    /// leader (0x45F650), which the host does not read; it applies only without a leader, so never to a companion.
    /// </summary>
    public string? HealingItem(string npc) =>
        npc == Pc ? null : _scene.Combat.NpcItems(_session, npc).Where(item => HealingTool(item.Fields)).Select(item => item.Entity).FirstOrDefault();

    /// <summary>
    /// ARC-WORLD-116: the heal skill check (0x4AE570, skill 10, ledger SP3 as corrected by HI2): the item must be a generic item with the
    /// healing flag (refusal 1), the target the hero or an NPC (refusal 2), and the healer neither undead (critter flag 0x4) nor
    /// mechanical (0x20000000; refusal 2). The target's own flags are not read here (the heal helper's mechanical test, P4, comes first).
    /// </summary>
    public bool HealSkillAccepts(string npc, string item, string target)
    {
        if (ItemFields(item) is not { } fields || !HealingTool(fields)) return false;
        if (target != Pc && Fields(target)?.Critter is null) return false;
        return Fields(npc)?.Critter is { } healer && HealSkillUsable(healer.Flags);
    }

    /// <summary>ARC-DEV-129: the heal skill check's healer test (0x4AE570, refusal 2): a critter that is neither undead (0x4) nor
    /// mechanical (0x20000000) can use the skill.</summary>
    public static bool HealSkillUsable(uint critterFlags) => (critterFlags & (Undead | MechanicalFlag)) == 0;

    /// <summary>ARC-DEV-129: a follower the follower smoke can use: it heals with the skill (<see cref="HealSkillUsable"/>) and takes the hero's
    /// victim as its own (the danger setter's rule 1, 0x4AABE0, keeps an invulnerable or nigh-invulnerable NPC out of every fight).</summary>
    public static bool CanHealAndFight(uint objectFlags, uint critterFlags, uint critterFlags2) =>
        HealSkillUsable(critterFlags) && (objectFlags & Invulnerable) == 0 && (critterFlags2 & NighInvulnerable2) == 0;

    private static bool HealingTool(CombatFields fields) =>
        fields.Object.Type == AreaHealingItems.GenericType && fields.Generic is { } generic && (generic.Flags & SessionHealingItem.HealingFlag) != 0;

    // The fields of an item an NPC (of the shown map, or a companion carried here) holds now, or null.
    private CombatFields? ItemFields(string item) => _scene.Combat.NpcItemFields(_session, item);

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
            : EntityOf(row.Key) ?? row.Key.ToString();
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
            AttemptingAttack = RunOf(id)?.Top.Goal is GoalType.AttemptAttack or GoalType.AttemptSpell or GoalType.AttemptSpellWithAnimation,
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
        return Faction(a) is { } fa && fa != 0 && fa == Faction(b);
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

    // The party's followers this world knows (the interface's contract): a follower left on another map, or a companion here with no record
    // (ARC-DEV-075: only a home the resolver cannot read), is not in a flee test's party count.
    public IReadOnlyList<string> Followers(string id) =>
        id == Pc ? _session.QueryParty().Followers.Select(f => f.Entity).Where(f => Find(f) is not null).ToArray() : [];

    public AwarenessResult See(string observer, string target) => SeeFacing(observer, target, null);

    /// <summary>ARC-SKILLS-010: sight (0x4AF260) with the observer turned toward the target (the hero's facing is not tracked: the viewer
    /// turns it toward the critter it picks the pocket of).</summary>
    public AwarenessResult SeeToward(string observer, string target) =>
        Tile(observer) is { } from && Tile(target) is { } to ? SeeFacing(observer, target, AreaCombatHost.Facing(from, to)) : See(observer, target);

    private AwarenessResult SeeFacing(string observer, string target, NeighborDirection? facing)
    {
        if (Tile(observer) is not { } at || Tile(target) is not { } there || _scene.LineOfFire?.Invoke() is not { } map) return new(AwarenessRules.NotPerceived, AwarenessReason.State);
        var facts = Find(observer);
        var sight = new SightFacts
        {
            Perception = Math.Clamp(facts?.Perception ?? 0, 0, AwarenessRules.MaxPerception),
            Rotation = DiagnosticGridActor.Octant(facing ?? Movers.FacingOf(observer, NeighborDirection.XPlus)),
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
        foreach (var other in _scene.Npcs.Concat(_followers.Values))
            if (Tile(other.Entity) is { } cell && Inside(cell) && !Unresolved(other.Entity)) critters.Add(new(SessionCombatant.Critter(other.Entity), cell, 0, other.Row.Key));
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

    // The session entity of a map row: a row of the shown map, or the T0 row of a companion standing here (ARC-DEV-075: a visitor's is a
    // row of its home map, which the shown map's entity index does not hold).
    private string? EntityOf(AreaObjectKey key) =>
        _scene.Combat.Entity(_scene.RuntimeMap, key) ?? _scene.Companions?.Invoke().FirstOrDefault(c => c.Key == key).Entity;

    private AreaObjectKey? Key(string id) => TryNpc(id, out var npc) ? npc.Row.Key : null;

    // ARC-WORLD-115: an NPC the host walks: one of the map's or a companion the host runs.
    // ARC-DEV-075: the one case left of ARC-DEV-065's skip: a companion run here whose record no reachable map gives (Find is null: its home
    // is the main map's window, which the resolver does not read on demand, MIG1's NOT READY, MIG9). The rules' Get refuses an object the
    // world cannot describe, so such a follower stays out of them; any companion with a record is run like an NPC of the shown map.
    private bool Unresolved(string id) => _followers.ContainsKey(id) && Find(id) is null;
    private bool TryNpc(string id, out AreaAiNpc npc) => _npcs.TryGetValue(id, out npc!) || _followers.TryGetValue(id, out npc!);
    private bool Walks(string id) => _npcs.ContainsKey(id) || _followers.ContainsKey(id);

    // The turn of a walked NPC toward a world cell: a map NPC's mover, a companion's own walk.
    private void FaceNpc(AreaAiNpc npc, CellCoord there)
    {
        if (_followers.ContainsKey(npc.Entity)) _scene.Followers!.Face(npc.Entity, there);
        else { Movers.Face(npc.Entity, npc.Row.Key, npc.Origin, there); RecordPlace(npc.Entity); }
    }

    private CombatFields? Fields(string id) => id == Pc ? null : _scene.Combat.Fields(id);

    // ARC-SCRIPT-049: an NPC's faction now: a script's set faction (AreaUseScriptHost.FactionProperty), else its fields'.
    private int? Faction(string id) => Fields(id)?.Npc?.Faction is not { } loaded ? null
        : _session.QueryMapChange(new SessionMapId(_scene.RuntimeMap), MapChangeKey.ForEntity(id, AreaUseScriptHost.FactionProperty)) is { } set ? unchecked((int)set) : loaded;

    private SessionCritterCombat? Build(string id) => id == Pc ? null : _scene.Combat.Build(_session, id).Combat;

    private LifeState? LifeState(string id) => Build(id) is { } build ? _session.QueryLife(build.Target).View?.Record.State : null;

    private LifeStatus? Status(string id) => id == Pc ? _session.QueryHeroLife()?.Status
        : Build(id) is { } build ? _session.QueryLife(build.Target).View?.Status : null;

    private (SessionCombatant Who, SessionLifeTarget Life)? Life(string id) =>
        Build(id) is { } build && Status(id) is { IsDead: false } ? (SessionCombatant.Critter(id), build.Target) : null;

    private SessionNpcAttackValues? Values(string id)
    {
        if (Build(id) is not { } build || Status(id) is not { } status || LifeState(id) is not { } state) return null;
        try { return build.AttackValues(status, state, _session.Rules.Combat, _scene.Combat.WornItems(_session, id)?.Keys); }
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
        if (_session.QueryHeroReactionFacts(0, Faction(npc) ?? fields.Faction) is not { } pc) return null;
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
        foreach (var (entity, _, cell) in _scene.Companions?.Invoke() ?? [])
            if (entity == id) return cell;
        return null;
    }

    // A living critter (the hero, a companion, an NPC of the map) other than the mover on the cell.
    private bool LivingCritterOn(CellCoord cell, string mover)
    {
        if (mover != Pc && Tile(Pc) == cell && !(Status(Pc)?.IsDead ?? true)) return true;
        foreach (var npc in _scene.Npcs)
            if (npc.Entity != mover && Tile(npc.Entity) == cell && !(Status(npc.Entity)?.IsDead ?? true)) return true;
        return _scene.Companions?.Invoke().Any(c => c.Cell == cell && c.Entity != mover) == true;
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
        foreach (var (entity, key, cell) in _scene.Companions?.Invoke() ?? [])
        {
            moved.Add(key);
            if (entity != mover && entity != target) held.Add(ToChart(cell));
        }
        if (target is not null && Key(target) is { } targetKey) moved.Add(targetKey);
        var occupancy = new AreaOccupancy(moved, held);
        return _scene.Removed?.Invoke() is { Count: > 0 } removed ? occupancy.WithRemoved(removed) : occupancy;
    }

    // The line of fire from the object's tile to the target's, and the target's row (null for the hero).
    private LineOfFireTrace<AreaObjectRecord>? Line(string id, string target, out AreaObjectRecord? targetRow)
    {
        targetRow = TryNpc(target, out var npc) ? npc.Row : null;
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
    /// <summary>ARC-WORLD-108: its last heal choice and item use.</summary>
    public string? Item { get; set; }
    /// <summary>ARC-WORLD-109: the last skill attempt.</summary>
    public string? Skill { get; set; }
    /// <summary>ARC-WORLD-113: the last throw (or the attack starter's unwield).</summary>
    public string? Throw { get; set; }
    /// <summary>ARC-DEV-117: its last fold step, waypoint or standpoint: what the rules decided and what the host did.</summary>
    public string? Route { get; set; }
}
