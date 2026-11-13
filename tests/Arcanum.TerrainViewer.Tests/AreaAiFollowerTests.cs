using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;
using Arcanum.Simulation.Goals;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-115 (NPC AI slice I9, the host part): the AI host runs the hero's companion over invented rows, no original data: two human
/// NPCs with every primary stat 20 and 20 hit points, bare-handed, an enemy (0) at world cell (10, 20) and the hero's companion (1) at
/// (13, 21); the hero stands at (14, 20). The enemy's T0 record blocks its cell in the step evidence. The companion's cell is its own walk's (a fake that steps it at once). The attack events tell
/// the hero's followers (0x4A9B80): a companion joins the hero's fight, walks up through its own walk and strikes through the session.
/// ARC-WORLD-117: the viewer's attack and back-off orders go through the host (the target, the combat facts, the AI pass after each order).
/// ARC-WORLD-119: a free companion (released or cursed) is run as any leaderless NPC; a leave ends its run (FO20) and the curse's attack
/// event runs through the host before the former follower's pass.
/// </summary>
[TestClass]
public sealed class AreaAiFollowerTests
{
    private const string Pc = AiSchedulerRules.Pc;
    private static readonly CellCoord HeroCell = new(14, 20), EnemyCell = new(10, 20), CompanionCell = new(13, 21);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    private const int HealingPrototype = 8000, Uses = 3;
    private static readonly SessionItemKind Healing = new(SessionItemType.Generic, HealingPrototype, 6000, 6001);

    // A human NPC (type 16, no monster flags unless given) with every primary stat 20, level 1, 20 hit points, at the world cell; heal 5
    // points when it heals.
    private static AreaObjectRecord Npc(int index, CellCoord cell, int heal = 0, uint critterFlags = 0) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, cell.X, cell.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, critterFlags), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, heal, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    // A generic item (type 14) with the healing flag (generic flags 0x08), usage bonus 10 and three uses, in the pack (location 0).
    private static AreaObjectRecord Item(int index) => new(Key(index), 0, 14, HealingPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x08), Field(60, 10), Field(61, Uses),
        ], true, true, true);

    /// <summary>The companion's own walk as the host sees it: a step moves it at once, a turn is counted.</summary>
    private sealed class Walks(AreaAiNpc companion) : IAreaAiFollowers
    {
        public readonly Dictionary<string, CellCoord> Cells = new() { [companion.Entity] = companion.Origin };
        public int Steps, Turns, Jumps;
        public IReadOnlyList<AreaAiNpc> Npcs { get; } = [companion];

        public bool Step(string entity, CellCoord next, bool running)
        {
            Steps++;
            Cells[entity] = next;
            return true;
        }

        public void Face(string entity, CellCoord toward) => Turns++;

        public bool Jump(string entity, CellCoord cell)
        {
            Jumps++;
            Cells[entity] = cell;
            return true;
        }
    }

    private static readonly List<object> HomeSources = [];

    private sealed class HomeSource(AreaInventoryMap home) : Arcanum.World.IMapRecordSource
    {
        public int Reads;
        public Arcanum.World.MapRecordsLoad Load(int runtimeMap, CancellationToken token)
        {
            Reads++;
            return runtimeMap == home.RuntimeMap ? new(home, string.Empty) : new(null, "unreadable");
        }
    }

    private static HomeSource NewSource(AreaInventoryMap home)
    {
        var source = new HomeSource(home);
        HomeSources.Add(source);
        return source;
    }

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, Walks Walks, string Enemy, string Companion)
    {
        public AreaCombatHost Combat { get; init; } = null!;
    }

    private static Scene Open(bool companionWaits = false, bool followers = true, CellCoord? companionAt = null, CellCoord? corner = null,
        CellCoord? companionHome = null, bool healer = false, uint enemyFlags = 0, bool free = false, bool visitor = false, bool onDemand = false, bool homeless = false, string? dialogPartner = null, Action<string>? dialogEnded = null, Func<string, int?>? alertedSound = null,
        PartyConsentFacts? joinFacts = null, Func<string, PartyConsentFacts?>? followFacts = null, AiParameterTable? table = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var start = companionAt ?? CompanionCell;
        // A healer companion holds a healing item (2) and has heal 5 points.
        // ARC-DEV-075: a visitor is a companion of another home map (2): it has no holder or row on this map, its record is its home map's,
        // given to the resolver (visitor) or read from a source on demand with the session naming the home (onDemand).
        var rows = healer ? new[] { Npc(0, EnemyCell, critterFlags: enemyFlags), Npc(1, start, 5), Item(2) }
            : visitor ? new[] { Npc(0, EnemyCell, critterFlags: enemyFlags) } : new[] { Npc(0, EnemyCell, critterFlags: enemyFlags), Npc(1, start) };
        SessionSeedHolder[] holders = visitor ? [new(Entity(0), SessionHolderKind.Npc, false, false, [], [])]
            : [new(Entity(0), SessionHolderKind.Npc, false, false, [], []), new(Entity(1), SessionHolderKind.Npc, false, false, [], healer ? [Entity(2)] : [])];
        var map = new AreaInventoryMap(1,
            new(holders,
                healer ? [new SessionSeedItem(Entity(2), new(Healing, 1, 0, 0, null, []), null)] : []),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var home = new AreaInventoryMap(2, new([new SessionSeedHolder(Entity(1), SessionHolderKind.Npc, false, false, [], [])], []),
            new Dictionary<string, AreaObjectRecord> { [Entity(1)] = Npc(1, new(3, 4)) });
        var inventory = new AreaInventoryHost(visitor && !onDemand && !homeless ? [map, home] : [map], records: visitor && onDemand ? NewSource(home) : null);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        if (visitor && onDemand)
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(2), AreaCompanionPlacement.MapKey(Entity(1)), 1).Disposition);
        // ARC-AI-1515: a follower that joined by the check (not forced) can be refused by a later follow check; a forced one is accepted at once.
        Assert.AreEqual(GameSessionDisposition.Changed, (joinFacts is null ? session.ForceJoinParty(session.Version, Entity(1)) : session.JoinParty(session.Version, Entity(1), joinFacts, force: false)).Disposition);
        if (companionWaits) Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, Entity(1)).Disposition);
        // ARC-WORLD-119: a companion released before the host is built.
        if (free) Assert.AreEqual(GameSessionDisposition.Changed, session.LeaveParty(session.Version, Entity(1), new PartyLeave(Force: true)).Disposition);
        var combat = new AreaCombatHost(inventory, session: () => session);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        // The enemy's own T0 record blocks its cell in the step evidence, as a critter's does in the map data: the companion's step
        // test toward it must not read it as an object in its way (the record is the walk's occupant test, not the terrain's).
        var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>
        {
            [new(EnemyCell.X, EnemyCell.Y)] = [new(Key(0), new(EnemyCell.X, EnemyCell.Y), 16, CardinalTraversalCoverage.Supported,
                CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "TEST")],
        };
        // The companion's own T0 record on its home cell, blocking it in the step evidence as a critter's does.
        if (companionHome is { } h) objects[new(h.X, h.Y)] = [new(Key(1), new(h.X, h.Y), 16, CardinalTraversalCoverage.Supported,
            CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "TEST")];
        // A scenery object (type 5) blocking a corner cell.
        if (corner is { } c) objects[new(c.X, c.Y)] = [new(Key(9), new(c.X, c.Y), 5, CardinalTraversalCoverage.Supported,
            CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "TEST")];
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            objects, new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var walks = new Walks(new(Entity(1), Npc(1, start, healer ? 5 : 0), start));
        var scene = new AreaAiScene
        {
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), Npc(0, EnemyCell, critterFlags: enemyFlags), EnemyCell)], Combat = combat,
            HeroCell = () => HeroCell,
            Companions = () => [(Entity(1), Key(1), walks.Cells[Entity(1)])],
            Followers = followers ? walks : null,
            ConversationPartner = dialogPartner is null ? null : () => dialogPartner, ConversationEnded = dialogEnded,
            NpcSheet = entity => combat.Fields(entity) is { } fields ? AreaAdvancement.Npc(entity, fields, _ => null) : null,
            AlertedSound = alertedSound, FollowFacts = followFacts, Parameters = table,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, walks, Entity(0), Entity(1)) { Combat = combat };
    }

    // Steps the host frame by frame (50 ms) until the run of the NPC ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc)
    {
        var notices = new List<AreaAiNotice>();
        var now = 0L;
        for (var frame = 0; frame < 400 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Frames().AdvanceHost(50); now += 50;
            notices.AddRange(host.DrainNotices());
        }
        Assert.IsNull(host.RunOf(npc), $"the run of {npc} ended: {host.TraceOf(npc)?.Step}");
        return notices;
    }

    [TestMethod]
    public void Out_of_danger_a_companion_heals_the_hurt_hero_with_its_healing_item_through_the_heal_skill()
    {
        var (session, host, _, _, companion) = Open(healer: true);
        var item = Entity(2);
        Assert.AreEqual(item, host.HealingItem(companion));
        Assert.IsFalse(host.Heal(companion), "nobody is hurt");
        var max = session.QueryHeroLife()!.Status.MaxHitPoints;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = max / 2 }).Disposition);
        var hurt = session.QueryHeroLife()!.Status.HitPoints;

        // The heal step's targets: the companion itself (unhurt), then its PC leader, whom the heal skill's rung takes (0x4A8AA0, SP2).
        Assert.IsTrue(host.Heal(companion), "the heal step chose");
        StringAssert.Contains(host.TraceOf(companion)!.Item, $"heal {Pc} {AiHealStep.HealSkill} spell=-1 item={item}");
        host.Perform(companion);
        Assert.AreEqual(GoalType.UseSkill, host.RunOf(companion)?.Bottom.Goal, $"perform starts goal 33: {host.TraceOf(companion)?.Perform}");
        Assert.IsTrue(host.Holds(companion), "the skill run holds the companion's own walk");
        var heal = Run(host, companion).Single(n => n.Kind == AreaAiNoticeKind.Skill).Heal
            ?? throw new AssertFailedException(host.TraceOf(companion)?.Skill ?? "no heal");
        Assert.AreEqual(GameSessionDisposition.Changed, heal.Disposition, $"{heal.Failure}");
        Assert.IsTrue(heal.ItemDestroyed || heal.UsesLeft is Uses - 1, "the heal spends a use of the companion's item");
        var after = session.QueryHeroLife()!.Status.HitPoints;
        Assert.IsTrue(heal.Check!.Success ? after > hurt : after == hurt, $"a success heals the hero, a failure does not: {hurt} -> {after}");
        Assert.IsFalse(host.Holds(companion), "after the run its own walk follows again");
    }

    [TestMethod]
    public void The_dialog_reply_steps_hostile_check_asks_alive_and_kill_on_sight_toward_the_hero()
    {
        // ARC-DEV-193 (0x413130 → 0x45D8D0, 0x4AE120). The fixture has no AI parameter rows, so the hostile reaction is 0 and the speaker's
        // reaction of -7 turns on the hero (REACTION); the other answers are the check's own rules.
        var scene = Open();
        Assert.IsTrue(scene.Host.HostileToHero(scene.Enemy), $"{HostilityRules.KillOnSight(scene.Host, scene.Enemy, Pc, Arcanum.Simulation.Combat.CombatAnomalyProfile.Original)}");
        Assert.IsFalse(scene.Host.HostileToHero("nobody"), "an unknown NPC counts as not alive, as the original's null object");
        Assert.IsFalse(scene.Host.HostileToHero(scene.Companion), "a follower of the hero is no one's kill-on-sight");
        var target = scene.Combat.Build(scene.Session, scene.Enemy).Combat?.Target ?? throw new AssertFailedException("the enemy has no combat build");
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.Kill(scene.Session.Version, target).Disposition);
        Assert.IsFalse(scene.Host.HostileToHero(scene.Enemy), "a dead NPC is not asked");
    }

    // ARC-SCRIPT-1135 (CF4): the dialog so action's goal start: goal 69 toward the hero on a led follower, a duplicate and a not-led or foreign NPC start nothing.
    [TestMethod]
    public void The_spread_out_start_runs_goal_69_on_a_led_follower_once_and_nothing_for_others()
    {
        var scene = Open();
        Assert.IsFalse(scene.Host.StartSpreadOut(scene.Enemy), "an NPC that is not a follower of this host");
        Assert.IsFalse(scene.Host.StartSpreadOut("nobody"));
        Assert.AreEqual(0, scene.Session.QueryGoals().RunsOf(scene.Companion).Length);

        Assert.IsTrue(scene.Host.StartSpreadOut(scene.Companion), scene.Host.TraceOf(scene.Companion)?.Perform);
        var run = scene.Session.QueryGoals().RunsOf(scene.Companion).Single();
        Assert.AreEqual((GoalType.SpreadOut, Pc), (run.Bottom.Goal, run.Bottom.Target));
        Assert.IsTrue(scene.Host.Holds(scene.Companion), "the goal run holds the companion's own walk");

        Assert.IsFalse(scene.Host.StartSpreadOut(scene.Companion), "a second start is a duplicate");
        Assert.AreEqual(1, scene.Session.QueryGoals().RunsOf(scene.Companion).Length);
    }

    [TestMethod]
    public void The_spread_out_start_needs_a_leader_so_a_released_follower_gets_no_goal()
    {
        var scene = Open(free: true);
        Assert.IsFalse(scene.Host.StartSpreadOut(scene.Companion));
        Assert.AreEqual(0, scene.Session.QueryGoals().RunsOf(scene.Companion).Length);
    }

    // The NPC's tick, fired through the session's event pass with the host as its world: an immediate tick due 1 ms ahead, the clock moved 10 ms.
    private static AiTickResult Fire(Scene scene, string entity)
    {
        var session = scene.Session;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleAiTick(session.Version,
            new SessionAiTickRequest { Entity = entity, Start = SessionAiTickStart.Immediate, DistanceToPc = scene.Host.DistanceToPc(entity) }).Disposition);
        Assert.AreEqual(Arcanum.Simulation.Clock.ClockMutationDisposition.Changed,
            session.JumpClocks(session.QueryClockSnapshot().Token, 10).Disposition);
        var pass = session.ProcessEvents(scene.Host);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        return pass.AiTicks.Single(f => f.Entity == entity).Tick ?? throw new AssertFailedException("the tick did not run");
    }

    // ARC-DEV-105 (MIG9 S5, 0x4AD4D0): the far branch of a following companion's tick.
    [TestMethod]
    public void A_following_companion_more_than_thirty_tiles_off_catches_up_onto_the_hero_on_its_tick()
    {
        var far = Open(companionAt: new(60, 20));
        var facts = far.Host.CatchUp(far.Companion);
        Assert.IsTrue(facts.HasPcLeader && facts.Active && !facts.Waiting && !facts.Paralyzed, $"{facts}");
        Assert.IsTrue(facts.LeaderDistance > AiSchedulerRules.ActivityRange);
        var tick = Fire(far, far.Companion);
        Assert.IsTrue(tick.Steps.HasFlag(AiTickSteps.CaughtUp), $"{tick.Steps}");
        Assert.AreEqual(AiTickNext.Delay, tick.Next);
        Assert.AreEqual(1, far.Walks.Jumps);
        Assert.AreEqual(HeroCell, far.Walks.Cells[far.Companion], "it stands on the hero's cell");
        Assert.AreEqual(0, far.Host.DistanceToPc(far.Companion));
        // ARC-DEV-328 (B25): the catch-up's move notifies the landing tile's script (0x4AD4D0 → 0x43E770 → 0x444270).
        var landed = far.Host.DrainNotices().Where(n => n.Kind == AreaAiNoticeKind.Entered).ToArray();
        Assert.AreEqual((far.Companion, HeroCell), (landed.Single().Npc, landed.Single().Cell!.Value));

        // Near the hero nothing moves, a waiting companion stays where it waits, and a free one has no PC leader.
        var near = Open();
        Assert.IsFalse(Fire(near, near.Companion).Steps.HasFlag(AiTickSteps.CaughtUp));
        var waiting = Open(companionAt: new(60, 20), companionWaits: true);
        Assert.IsTrue(waiting.Host.CatchUp(waiting.Companion).Waiting);
        Assert.IsFalse(Fire(waiting, waiting.Companion).Steps.HasFlag(AiTickSteps.CaughtUp));
        Assert.AreEqual(0, waiting.Walks.Jumps);
        var free = Open(companionAt: new(60, 20), free: true);
        Assert.IsFalse(free.Host.CatchUp(free.Companion).HasPcLeader);
    }

    // ARC-DEV-105 (MIG9 S5, RL16, RL17): a map with a sector cache starts a heartbeat chain only in a loaded sector; a map without one keeps
    // the delayed first tick of every living NPC.
    [TestMethod]
    public void With_a_sector_cache_the_host_starts_no_chain_for_an_npc_of_an_unloaded_sector()
    {
        var map = new SessionMapId(1);
        var plain = Open();
        Assert.AreEqual(0, plain.Host.ScheduleTicks());
        Assert.IsNotNull(plain.Session.QueryAiTick(plain.Enemy), "no cache: every sector counts as loaded");

        var scene = Open();
        var opened = AreaResidency.Open(scene.Session, map, new(MapResidentRowsStatus.Ready, [new(scene.Enemy, SessionResidentKind.Npc, EnemyCell)], string.Empty),
            new(16, 16), null);
        Assert.AreEqual(SectorsOpenStatus.Open, opened.Sectors);
        Assert.AreEqual(GameSessionFailure.None, scene.Session.TouchSectors(scene.Session.Version, map, [new(200, 200)]).Failure);
        Assert.IsFalse(scene.Session.QuerySectorLoaded(map, ResidentSector.Of(EnemyCell))!.Value);
        Assert.AreEqual(0, scene.Host.ScheduleTicks());
        Assert.IsNull(scene.Session.QueryAiTick(scene.Enemy), "its sector was never loaded: no chain");
        Assert.IsNotNull(scene.Session.QueryAiTick(scene.Companion), "the companion the host runs starts its own, as a created object");

        // The sector's fold starts the chain; a later call leaves the pending tick alone.
        Assert.AreEqual(GameSessionFailure.None, scene.Session.TouchSectors(scene.Session.Version, map, [EnemyCell]).Failure);
        var folded = scene.Session.QueryAiTick(scene.Enemy);
        Assert.IsNotNull(folded);
        Assert.AreEqual(0, scene.Host.ScheduleTicks());
        Assert.AreEqual(folded, scene.Session.QueryAiTick(scene.Enemy));
    }

    // The viewer's order to every follower in reach, with the host's combat facts of the companion (AreaCompanionOrders.Facts).
    private static SessionCompanionOrderResult Order(Scene scene, CompanionOrderType type, SessionCombatant? target = null, bool withHost = true,
        Action<CompanionOrderOutcome>? goals = null)
    {
        var consent = new PartyConsentFacts(false, PersuasionTraining.None, 8, 0, 3, null, 50, new(-20, 300, 200, 5), 0, 3, 5);
        var facts = new CompanionOrderFacts(scene.Companion, "companion", 1, false, consent, Combat: scene.Host.OrderFacts(scene.Companion));
        return AreaCompanionOrders.Send(scene.Session, new(type, CompanionOrderAddressing.All, [facts]) { Target = target }, withHost ? scene.Host : null, goals);
    }

    [TestMethod]
    public void The_attack_orders_target_is_the_highlighted_objects_entity()
    {
        var scene = Open();
        Assert.AreEqual(SessionCombatant.Critter(scene.Enemy), AreaCompanionOrders.Target(scene.Combat, 1, Key(0)));
        Assert.IsNull(AreaCompanionOrders.Target(scene.Combat, 1, null), "nothing highlighted: no target (FO3)");
        Assert.IsNull(AreaCompanionOrders.Target(scene.Combat, 1, Key(7)), "an object without a session entity");
        Assert.IsNull(AreaCompanionOrders.Target(scene.Combat, 2, Key(0)), "another map's");
    }

    [TestMethod]
    public void An_attack_order_through_the_host_locks_the_companion_and_its_ai_pass_starts_the_attack()
    {
        var scene = Open();
        var (session, host, walks, enemy, companion) = scene;
        var outcome = Order(scene, CompanionOrderType.Attack, AreaCompanionOrders.Target(scene.Combat, 1, Key(0))).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.AttackLocked, true), (outcome.Effect, outcome.Locked), $"{outcome.Effect} {outcome.Failure}");
        var danger = session.QueryDanger(companion);
        Assert.AreEqual((DangerState.CombatFocus, SessionCombatant.Critter(enemy)), (danger.State, danger.Focus), "the ordered attack event's lock (FO3)");
        Assert.IsTrue((danger.Flags & DangerFlags.TargetLocked) != 0);

        // FO1: the follower's AI pass right after its order, through the host: perform starts the attack on the locked target.
        var pass = outcome.Pass ?? throw new AssertFailedException("no AI pass");
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.IsTrue(pass.Pass is { Performed: true }, $"the pass performed: gate {pass.Pass?.Gate?.Stop}");
        var run = host.RunOf(companion) ?? throw new AssertFailedException($"no run: {host.TraceOf(companion)?.Perform}");
        Assert.AreEqual((GoalType.Attack, enemy), (run.Bottom.Goal, run.Bottom.Target));
        Assert.IsTrue(host.Holds(companion));
        StringAssert.Contains(AreaCompanionOrders.Describe(outcome), "pass=performed");

        var notices = Run(host, companion);
        Assert.IsTrue(walks.Steps >= 2, $"it walked up through its own walk ({walks.Steps} steps)");
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == companion && n.Target == enemy), "it strikes the ordered target");
    }

    [TestMethod]
    public void An_attack_order_without_a_target_or_on_the_party_does_nothing_but_the_pass()
    {
        var scene = Open();
        var none = Order(scene, CompanionOrderType.Attack).Outcomes.Single();
        Assert.AreEqual(CompanionOrderEffect.NoTarget, none.Effect);
        Assert.IsNotNull(none.Pass, "the AI pass still runs (FO1)");
        Assert.IsNull(scene.Host.RunOf(scene.Companion), "out of danger the pass's follow is the companion's own walk");
        var party = Order(scene, CompanionOrderType.Attack, SessionCombatant.Critter(scene.Companion)).Outcomes.Single();
        Assert.AreEqual(CompanionOrderEffect.PartyTarget, party.Effect, "outside combat mode the hero's party is refused (FO4)");
        Assert.AreEqual(DangerState.None, scene.Session.QueryDanger(scene.Companion).State);
    }

    [TestMethod]
    public void Without_the_host_an_attack_order_lifts_the_stop_but_fails_and_runs_no_pass()
    {
        var scene = Open();
        Assert.AreEqual(CompanionOrderEffect.Stopped, Order(scene, CompanionOrderType.BackOff, withHost: false).Outcomes.Single().Effect);
        Assert.IsTrue((scene.Session.QueryDanger(scene.Companion).Flags & DangerFlags.NoAttack) != 0, "back off set NO_ATTACK");
        var outcome = Order(scene, CompanionOrderType.Attack, SessionCombatant.Critter(scene.Enemy), withHost: false).Outcomes.Single();
        Assert.AreEqual(CompanionOrderEffect.Failed, outcome.Effect);
        Assert.IsTrue((scene.Session.QueryDanger(scene.Companion).Flags & DangerFlags.NoAttack) == 0, "the attack order lifted it (FO9)");
        Assert.IsNull(outcome.Pass);
        Assert.AreEqual(DangerState.None, scene.Session.QueryDanger(scene.Companion).State);
    }

    [TestMethod]
    public void Back_off_through_the_host_stops_the_fight_and_releases_the_companion_to_its_walk()
    {
        var scene = Open();
        var (session, host, _, enemy, companion) = scene;
        Order(scene, CompanionOrderType.Attack, SessionCombatant.Critter(enemy));
        host.Frames().StepDue();
        Assert.IsNotNull(host.RunOf(companion), "the ordered attack runs");

        var outcome = Order(scene, CompanionOrderType.BackOff).Outcomes.Single();
        Assert.AreEqual(CompanionOrderEffect.Stopped, outcome.Effect, $"{outcome.Failure}");
        Assert.IsTrue(outcome.Stop is { Interrupted: true, NoAttack: true }, "peace with its focus, the interrupt and NO_ATTACK (FO5)");
        var danger = session.QueryDanger(companion);
        Assert.AreEqual(DangerState.None, danger.State);
        Assert.IsTrue((danger.Flags & DangerFlags.NoAttack) != 0);
        Assert.IsTrue(outcome.Pass?.Pass is { Gate.Passed: true }, $"the stop's interrupt came first: the pass's gate finds it idle ({outcome.Pass?.Pass?.Gate?.Stop})");
        Assert.IsNull(host.RunOf(companion), "the attack run ended");
        Assert.IsFalse(host.Holds(companion), "its own walk follows the hero again");
    }

    [TestMethod]
    public void The_stops_interrupt_ends_the_attack_run_only_when_asked()
    {
        var (_, host, _, enemy, companion) = Open();
        host.PerformAttack(companion, enemy);
        Assert.IsNotNull(host.RunOf(companion));
        var stop = new SessionPeaceStep(companion) { Interrupted = true };
        Assert.IsFalse(host.ApplyStop(stop with { Interrupted = false }), "no interrupt asked (no focus to stop)");
        Assert.IsNotNull(host.RunOf(companion));
        Assert.IsTrue(host.ApplyStop(stop), "the attack (priority 3) ends");
        Assert.IsNull(host.RunOf(companion));
        Assert.IsFalse(host.ApplyStop(stop), "nothing left to end");
    }

    [TestMethod]
    public void The_heal_skill_check_reads_the_healers_flags_not_the_targets()
    {
        // An undead and mechanical NPC (critter flags 0x20000004) as the target: the check (0x4AE570, HI2) still accepts it.
        var (_, host, _, enemy, companion) = Open(healer: true, enemyFlags: 0x20000004);
        Assert.IsTrue(host.HealSkillAccepts(companion, Entity(2), enemy), "the target's critter flags are not read");
        Assert.IsTrue(host.HealSkillAccepts(companion, Entity(2), Pc));
        Assert.IsFalse(host.HealSkillAccepts(enemy, Entity(2), companion), "the healer's are");
    }

    [TestMethod]
    public void The_heros_attack_tells_its_companion_which_joins_walks_up_and_strikes()
    {
        var (session, host, walks, enemy, companion) = Open();
        CollectionAssert.AreEqual(new[] { companion }, host.RunCompanions.ToArray());
        Assert.IsFalse(host.Holds(companion), "no fight yet: its own walk follows the hero");

        host.HeroAttacked(enemy);
        var danger = session.QueryDanger(companion);
        Assert.AreEqual(DangerState.CombatFocus, danger.State, $"the attacker's follower reacts to the victim (case 5): {host.TraceOf(companion)?.Event} {host.TraceOf(companion)?.Reaction}");
        Assert.AreEqual(enemy, danger.Source?.Entity, "it fights the hero's victim");
        StringAssert.StartsWith(host.TraceOf(companion)!.Event, "told Attacker ReactToAttack");
        Assert.IsTrue(host.Holds(companion), "a fighting companion is held: it follows nothing");

        host.Perform(companion);
        var run = host.RunOf(companion) ?? throw new AssertFailedException($"no run: {host.TraceOf(companion)?.Perform}");
        Assert.AreEqual((GoalType.Attack, enemy), (run.Bottom.Goal, run.Bottom.Target), "the attack starter's goal on the enemy");

        var notices = Run(host, companion);
        Assert.IsTrue(walks.Steps >= 2, $"it walked up through its own walk ({walks.Steps} steps)");
        Assert.IsTrue(AiExecutorRules.Distance(walks.Cells[companion], EnemyCell) <= 1, "it stands next to the enemy");
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Swing && n.Npc == companion && n.Target == enemy), "it swings at the enemy");
        var hit = notices.Single(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == companion).Result ?? throw new AssertFailedException("no hit result");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, hit.Disposition, $"the session's NPC hit step: {hit.Failure}");
        Assert.AreEqual(AttackRefusal.None, hit.Resolution!.Refusal);
        Assert.IsTrue(walks.Turns > 0, "it turned toward the enemy through its own walk");
        Assert.IsTrue(session.QueryCombatMode(SessionCombatant.Critter(companion)), "the attempt-attack goal put it into combat mode (AH2)");
    }

    [TestMethod]
    public void An_npc_striking_the_hero_ends_the_heros_dialog()
    {
        // ARC-DEV-271 (GT140): 0x4A9AD0 calls the dialog-end callback (0x5678D0(PC, 0)) when a critter attacks the PC, whoever the dialog partner is.
        var ended = new List<string>();
        // (The hero is in combat mode before the hit already: the swing's start puts a PC target there, 0x42B9C0; the dialog end and the combat mode share the attack event's response.)
        var (session, host, _, enemy, _) = Open(dialogPartner: Entity(7), dialogEnded: ended.Add);
        host.PerformAttack(enemy, Pc);
        Run(host, enemy);
        CollectionAssert.AreEqual(new[] { Entity(7) }, ended, "the strike ended the hero's dialog with its partner");
        Assert.IsTrue(session.QueryCombatMode(SessionCombatant.Hero), "and the hero entered combat mode");
    }

    [TestMethod]
    public void The_idle_restart_gives_the_hero_a_fidget_in_combat_mode_outside_a_round_and_not_otherwise()
    {
        // ARC-DEV-272 (TH8, TH9): 0x4B4320 starts a fidget for the hero too; here it is the notice an NPC's fidget is.
        var (session, host, _, _, _) = Open();
        var handle = new GoalHandle(1, 1);
        host.IdleRestart(Pc, handle);
        Assert.IsFalse(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Fidget), "not in combat mode");
        session.SetCombatMode(session.Version, SessionCombatant.Hero, true, SessionCombatModeContext.RealTime);
        host.IdleRestart(Pc, handle);
        Assert.IsTrue(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Fidget && n.Npc == Pc), "in combat mode, outside a round");
    }

    [TestMethod]
    public void An_npc_striking_the_hero_ends_no_dialog_when_none_is_open()
    {
        var ended = new List<string>();
        var (_, host, _, enemy, _) = Open(dialogEnded: ended.Add);
        host.PerformAttack(enemy, Pc);
        Run(host, enemy);
        Assert.AreEqual(0, ended.Count);
    }

    [TestMethod]
    public void The_heros_own_attack_ends_no_dialog()
    {
        var ended = new List<string>();
        var (_, host, _, enemy, _) = Open(dialogPartner: Entity(7), dialogEnded: ended.Add);
        host.HeroAttacked(enemy);
        Assert.AreEqual(0, ended.Count, "the victim is an NPC, not the PC");
    }

    [TestMethod]
    public void An_npc_striking_the_hero_tells_the_victims_follower()
    {
        var (session, host, _, enemy, companion) = Open();
        host.PerformAttack(enemy, Pc);
        var notices = Run(host, enemy);
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == enemy), "the enemy struck at the hero");
        var danger = session.QueryDanger(companion);
        Assert.AreEqual((DangerState.CombatFocus, enemy), (danger.State, danger.Source?.Entity),
            $"the victim's follower reacts to the attacker: {host.TraceOf(companion)?.Event} {host.TraceOf(companion)?.Reaction}");
        StringAssert.StartsWith(host.TraceOf(companion)!.Event, "told Victim ReactToAttack");
    }

    [TestMethod]
    public void A_visiting_companion_is_found_by_identity_and_reaches_the_rules()
    {
        // ARC-DEV-075 (MIG2): Virgil, a companion of home map 2 carried to the shown map 1, is described by his home map's record: he is found,
        // stands in scans' squares and in the flee test's party, as any NPC of the shown map.
        var (_, host, _, enemy, companion) = Open(visitor: true);
        var found = host.Find(companion);
        Assert.IsNotNull(found, "his record is his home map's, by identity");
        Assert.AreEqual(HostilityKind.Npc, found.Kind);
        Assert.AreEqual(1, host.RunCompanions.Count);
        CollectionAssert.Contains(host.Square(enemy, 20).ToList(), companion, "a scan's or noise's square holds him");
        CollectionAssert.Contains(host.Followers(Pc).ToList(), companion, "the flee test's party counts him");
        Assert.IsNotNull(host.AiLifeTarget(companion), "his combat values build");
    }

    [TestMethod]
    public void A_visiting_companion_joins_the_heros_fight_walks_up_and_strikes()
    {
        var (session, host, walks, enemy, companion) = Open(visitor: true);
        host.HeroAttacked(enemy);
        var danger = session.QueryDanger(companion);
        Assert.AreEqual((DangerState.CombatFocus, enemy), (danger.State, danger.Source?.Entity),
            $"the hero's follower is told of the attack: {host.TraceOf(companion)?.Event} {host.TraceOf(companion)?.Reaction}");
        StringAssert.StartsWith(host.TraceOf(companion)!.Event, "told Attacker ReactToAttack");
        Assert.IsTrue(host.Holds(companion), "a fighting companion is held: it follows nothing");

        host.Perform(companion);
        var run = host.RunOf(companion) ?? throw new AssertFailedException($"no run: {host.TraceOf(companion)?.Perform}");
        Assert.AreEqual((GoalType.Attack, enemy), (run.Bottom.Goal, run.Bottom.Target));
        var notices = Run(host, companion);
        Assert.IsTrue(walks.Steps >= 2, $"he walked up through his own walk ({walks.Steps} steps)");
        var hit = notices.Single(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == companion).Result ?? throw new AssertFailedException("no hit result");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, hit.Disposition, $"the session's NPC hit step: {hit.Failure}");
        Assert.AreEqual(AttackRefusal.None, hit.Resolution!.Refusal);
        Assert.IsTrue(session.QueryCombatMode(SessionCombatant.Critter(companion)), "the attempt-attack goal put him into combat mode");
    }

    [TestMethod]
    public void An_npc_striking_the_hero_tells_a_visiting_follower_and_the_npc_can_fight_him()
    {
        var (session, host, _, enemy, companion) = Open(visitor: true);
        host.PerformAttack(enemy, Pc);
        Assert.IsTrue(Run(host, enemy).Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == enemy), "the enemy struck at the hero");
        var danger = session.QueryDanger(companion);
        Assert.AreEqual((DangerState.CombatFocus, enemy), (danger.State, danger.Source?.Entity),
            $"the victim's follower reacts to the attacker: {host.TraceOf(companion)?.Event} {host.TraceOf(companion)?.Reaction}");
        StringAssert.StartsWith(host.TraceOf(companion)!.Event, "told Victim ReactToAttack");

        // The shown map's NPC attacks the visitor: the target resolves (his cell is his own walk's, his build is his home map's).
        host.PerformAttack(enemy, companion);
        var hit = Run(host, enemy).Last(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == enemy && n.Target == companion).Result
            ?? throw new AssertFailedException("no hit result");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, hit.Disposition, $"the session's NPC hit step on the visitor: {hit.Failure}");
        Assert.AreEqual(AttackRefusal.None, hit.Resolution!.Refusal);
    }

    [TestMethod]
    public void A_companion_of_a_home_map_this_run_never_loaded_is_run_through_the_resolvers_on_demand_read()
    {
        var (session, host, _, enemy, companion) = Open(visitor: true, onDemand: true);
        Assert.AreEqual(0, ((HomeSource)HomeSources[^1]).Reads, "nothing read the home map before the AI host asked");
        Assert.IsNotNull(host.Find(companion));
        Assert.AreEqual(1, ((HomeSource)HomeSources[^1]).Reads, "the AI host's own question read the home map once");
        host.HeroAttacked(enemy);
        Assert.AreEqual(DangerState.CombatFocus, session.QueryDanger(companion).State, $"{host.TraceOf(companion)?.Event}");
        CollectionAssert.Contains(host.Square(enemy, 20).ToList(), companion);
    }

    [TestMethod]
    public void A_companion_whose_home_cannot_be_read_stays_out_of_the_rules_instead_of_stopping_the_game()
    {
        // The ARC-DEV-065 fault (the follower rule met an object the world could not describe) stays closed for the one case left: no record.
        var (session, host, _, enemy, companion) = Open(visitor: true, homeless: true);
        Assert.IsNull(host.Find(companion), "no reachable map lists him");
        Assert.AreEqual(1, host.RunCompanions.Count, "his walk is still the host's follower walk");
        host.PerformAttack(enemy, Pc);
        Assert.IsTrue(Run(host, enemy).Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == enemy), "the enemy struck at the hero");
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State, "he is not told of the attack");
        Assert.IsNull(host.TraceOf(companion)?.Event);
        host.HeroAttacked(enemy);
        CollectionAssert.DoesNotContain(host.Square(enemy, 20).ToList(), companion);
        CollectionAssert.DoesNotContain(host.Followers(Pc).ToList(), companion);
    }

    [TestMethod]
    public void A_waiting_companion_is_not_told_and_a_peaceful_one_follows_by_its_own_walk()
    {
        var (session, host, walks, enemy, companion) = Open(companionWaits: true);
        host.HeroAttacked(enemy);
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State, "tell followers skips waiting NPCs (0x4411C0)");
        Assert.IsFalse(host.Holds(companion));

        var (_, peaceful, _, _, follower) = Open();
        peaceful.Perform(follower);
        Assert.IsNull(peaceful.RunOf(follower), "the follow goal is the companion's own walk's, not the host's");
        Assert.AreEqual("Follow by its own walk", peaceful.TraceOf(follower)?.Perform);
        Assert.IsFalse(peaceful.Holds(follower));
        Assert.AreEqual(0, walks.Steps);
    }

    [TestMethod]
    public void Without_followers_in_the_scene_the_companion_is_not_run()
    {
        var (session, host, _, enemy, companion) = Open(followers: false);
        Assert.AreEqual(0, host.RunCompanions.Count);
        host.HeroAttacked(enemy);
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State, "only the companions the host runs are told");
        Assert.IsFalse(host.Holds(companion));
    }

    [TestMethod]
    public void Next_to_the_enemy_past_a_corner_object_there_is_no_first_step_and_no_route()
    {
        // The companion stands diagonally next to the enemy, a scenery object on one corner of the diagonal: the first step is not
        // clear and the approach gives no route for a target next to the walker (ledger AP1, AP2), as in the original.
        var (_, host, _, enemy, companion) = Open(companionAt: new(11, 21), corner: new(11, 20));
        Assert.IsFalse(host.FirstStepClear(companion, enemy, false), "the diagonal past the corner object is not a clear first step");
        Assert.IsNull(host.PlanPath(companion, new AiPathRequest(AiPathKind.Attack, enemy, null, 1, false, false)), "no route next to the target (AP2)");
        // Without the corner object the diagonal is clear: the enemy's own T0 record on its cell does not block (AP1, the target no-block).
        var (_, open, _, _, next) = Open(companionAt: new(11, 21));
        Assert.IsTrue(open.FirstStepClear(next, enemy, false));
    }

    [TestMethod]
    public void After_its_fight_the_companion_is_released_to_its_own_walk()
    {
        var (session, host, _, enemy, companion) = Open(companionAt: new(11, 20));
        host.HeroAttacked(enemy);
        host.Perform(companion);
        Assert.IsTrue(host.Holds(companion));
        Run(host, companion);
        Assert.IsTrue(host.Holds(companion), "still fighting: its danger state is not NONE");
        // The fight ends: the setter's NONE (as the danger update's calm-down asks it).
        var calm = session.SetDangerState(session.Version, new SessionDangerRequest { Entity = companion, State = DangerState.None });
        Assert.AreNotEqual(GameSessionDisposition.Rejected, calm.Disposition, calm.Failure.ToString());
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State);
        Assert.IsFalse(host.Holds(companion), "in state NONE without a run its own walk follows the hero again");
        host.Perform(companion);
        Assert.IsNull(host.RunOf(companion));
        Assert.AreEqual("Follow by its own walk", host.TraceOf(companion)?.Perform);
    }

    [TestMethod]
    public void The_companions_step_onto_its_own_home_record_is_not_refused()
    {
        // The planner moves a companion's T0 record (it stands elsewhere); the step test must agree, or the route is planned and refused
        // step by step for ever. A map NPC's record on its cell still blocks a walker's step there (the enemy at (10, 20)).
        var (_, host, _, enemy, companion) = Open(companionAt: new(13, 21), companionHome: new(12, 21));
        Assert.AreEqual(AiStepBlock.Clear, host.StepBlocked(companion, new(12, 21), false), "its own home record does not block it");
        var (_, other, _, wolf, next) = Open(companionAt: new(11, 21));
        Assert.AreEqual(AiStepBlock.Occupied, other.StepBlocked(next, EnemyCell, false), "the living enemy on its cell (LivingCritterOn)");
        Assert.IsNotNull(enemy); Assert.IsNotNull(wolf);
    }

    [TestMethod]
    public void A_written_hold_holds_the_step_start_and_an_entangled_critter_plans_no_path()
    {
        // ARC-DEV-302 (GT148, GT149): the AI host reads the flags the spells wrote over the NPC's data.
        var (session, host, _, enemy, companion) = Open(companionAt: new(11, 21));
        var toTile = new AiPathRequest(AiPathKind.ToTile, null, new CellCoord(14, 22), 0, false, false);
        Assert.IsNotNull(host.PlanPath(companion, toTile));
        Assert.AreEqual(AiStepBlock.Clear, host.StepBlocked(companion, new(12, 21), false));
        Assert.IsTrue(host.Active(companion));
        Assert.AreEqual(GameSessionDisposition.Changed,
            session.WriteObjectFlag(session.Version, companion, SessionObjectFlagField.Critter, SessionSpellCaster.ParalysedCritterFlag, true).Disposition);
        Assert.AreEqual(AiStepBlock.Held, host.StepBlocked(companion, new(12, 21), false));
        Assert.IsTrue(host.Paralysed(companion));
        Assert.IsFalse(host.Active(companion), "paralysed (0x45D790)");
        Assert.IsNotNull(host.PlanPath(companion, toTile), "a paralysed critter still plans");
        session.WriteObjectFlag(session.Version, companion, SessionObjectFlagField.Critter, SessionSpellCaster.ParalysedCritterFlag, false);
        session.WriteObjectFlag(session.Version, companion, SessionObjectFlagField.Spell, SessionSpellCaster.EntangledSpellFlag, true);
        Assert.AreEqual(AiStepBlock.Clear, host.StepBlocked(companion, new(12, 21), false), "entangled is not tested at the step start");
        Assert.IsTrue(host.Active(companion), "entangled is not in can-act");
        Assert.IsNull(host.PlanPath(companion, toTile), "the path set-up refuses an entangled critter");
        Assert.AreEqual(0, host.InterruptForHold(companion), "no run to end");
        Assert.AreEqual(0, host.InterruptForHold("oid:none"), "not a critter this host runs");
        Assert.IsNotNull(enemy);
    }

    // ---- ARC-WORLD-119: free companions ----

    [TestMethod]
    public void A_free_companion_is_run_as_any_leaderless_npc()
    {
        var (session, host, walks, enemy, companion) = Open(free: true);
        Assert.IsFalse(session.QueryCompanion(companion).LedByPc);
        CollectionAssert.AreEqual(new[] { companion }, host.RunCompanions.ToArray(), "the host runs the free companion too");
        host.Perform(companion);
        Assert.AreEqual("Routine", host.TraceOf(companion)?.Perform, "out of danger, without a leader: the routine, no follow");
        // ARC-DEV-170 (B06): the routine is the unforced route steps now; the fixture's row stores the standpoint (0, 0) far from the companion, so the free companion walks back to it
        // (goal 7), as a former follower walks to its standpoint in the original. That walk is the host's own run, which holds the companion; interrupted, its own walk is free again.
        Assert.AreEqual(GoalType.MoveNearTile, host.RunOf(companion)?.Bottom.Goal, "route: " + host.TraceOf(companion)?.Route);
        host.InterruptAnimations(companion);
        Assert.IsNull(host.RunOf(companion));
        Assert.IsFalse(host.Holds(companion));

        host.HeroAttacked(enemy);
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State, "no longer the hero's follower: not told (0x4A9B80)");

        // The enemy strikes it: as any victim it reacts to its attacker (here a flight, the morale rule's answer for these rows). ARC-DEV-292: the strike also gives it its immediate AI pass
        // (0x4B52FC), whose danger update takes a flight on to a surrender (FLEE -> SURRENDER on the next fleeing pass, A4), so the state after the hit may be either.
        host.PerformAttack(enemy, companion);
        Run(host, enemy);
        var danger = session.QueryDanger(companion);
        Assert.IsTrue(danger.State is DangerState.Flee or DangerState.CombatFocus or DangerState.Surrender && danger.Source?.Entity == enemy,
            $"the victim reacts to the attack: {danger.State} {host.TraceOf(companion)?.Event} {host.TraceOf(companion)?.Reaction}");
        Assert.IsTrue(host.Holds(companion), "a companion in danger is held: its walk goes where the host steps it");

        // In COMBAT_FOCUS on the enemy (as a danger update's fight sets it) perform starts the attack, which it walks up and strikes
        // through its own walk.
        var focus = session.SetDangerState(session.Version, new SessionDangerRequest { Entity = companion, State = DangerState.CombatFocus, Target = SessionCombatant.Critter(enemy) });
        Assert.AreNotEqual(GameSessionDisposition.Rejected, focus.Disposition, focus.Failure.ToString());
        host.Perform(companion);
        var run = host.RunOf(companion) ?? throw new AssertFailedException($"no run: {host.TraceOf(companion)?.Perform}");
        Assert.AreEqual((GoalType.Attack, enemy), (run.Bottom.Goal, run.Bottom.Target));
        var enemyBefore = host.Tile(enemy)!.Value;
        var notices = Run(host, companion);
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Npc == companion && n.Target == enemy), "it strikes the enemy");
        // ARC-DEV-292: the struck enemy gets its immediate pass at the hit and may step at once, so the cell it stood on when the companion set out is the one the strike is read against.
        Assert.AreEqual(1, AiExecutorRules.Distance(walks.Cells[companion], enemyBefore), "from the cell next to it, where its own walk stands");
    }

    [TestMethod]
    public void The_curse_through_the_host_frees_the_follower_ends_its_run_and_runs_the_attack_event_before_its_pass()
    {
        var scene = Open();
        var (session, host, _, enemy, companion) = scene;
        host.HeroAttacked(enemy);
        host.Perform(companion);
        Assert.AreEqual(enemy, host.RunOf(companion)?.Bottom.Target, "it fights the hero's victim");

        // The order's host part sees the leave first: by its goals (right after the look) the run on the enemy has ended (FO20).
        var runAtGoals = (GoalType?)GoalType.Attack;
        var outcome = Order(scene, CompanionOrderType.Curse, goals: _ => runAtGoals = host.RunOf(companion)?.Bottom.Goal).Outcomes.Single();
        Assert.AreEqual(CompanionOrderEffect.Cursed, outcome.Effect, $"{outcome.Failure}");
        Assert.IsNull(runAtGoals, "the leave's interrupt ended the run inside the order, before the attack event and the pass");
        Assert.IsFalse(session.QueryCompanion(companion).LedByPc, "the curse's leave (FO2)");
        StringAssert.Contains(AreaCompanionOrders.Describe(outcome), "curse-attack");
        // The hero's attack event on it (loudness 1, no flags): the former follower, no longer the hero's, reacts to it (here a flight
        // from the stronger hero) and its pass runs after the event; the leave's interrupt had ended its run on the enemy (FO20).
        StringAssert.StartsWith(host.TraceOf(companion)?.Reaction, "Weigh Flee", "it reacted to the hero's attack, not as the attacker's follower");
        var danger = session.QueryDanger(companion);
        Assert.IsTrue(danger.State is DangerState.Flee or DangerState.Surrender && danger.Source?.IsHero == true,
            $"it flees the hero: {danger.State} {danger.Source?.Entity} {host.TraceOf(companion)?.Event} {host.TraceOf(companion)?.Setter}");
        Assert.IsTrue(outcome.Pass?.Pass is { Performed: true }, $"the pass after the event performed: {outcome.Pass?.Pass?.Gate?.Stop}");
        StringAssert.StartsWith(host.TraceOf(companion)?.Perform, "Flee", "the pass's perform is the flight's");
        Assert.AreNotEqual(enemy, host.RunOf(companion)?.Bottom.Target, "the run on the enemy is gone");
        Assert.IsTrue(host.RunCompanions.Contains(companion), "the host still runs it, now as a free NPC");
        Assert.AreEqual(0, host.ObserveLeaves().Count, "the leave was seen once, inside the order");

        // Without the host the curse frees the follower and runs no attack event.
        var other = Open();
        var bare = Order(other, CompanionOrderType.Curse, withHost: false).Outcomes.Single();
        Assert.AreEqual((CompanionOrderEffect.Cursed, (SessionAiResult?)null), (bare.Effect, bare.Pass));
        Assert.AreEqual(DangerState.None, other.Session.QueryDanger(other.Companion).State);
    }

    [TestMethod]
    public void A_leave_outside_an_order_ends_the_leavers_run_at_the_next_pump_and_a_join_keeps_it()
    {
        var (session, host, _, enemy, companion) = Open();
        host.HeroAttacked(enemy);
        host.Perform(companion);
        host.PerformAttack(enemy, companion);
        Assert.IsNotNull(host.RunOf(companion));
        Assert.AreEqual(GameSessionDisposition.Changed, session.LeaveParty(session.Version, companion, new PartyLeave(Force: true)).Disposition);
        host.Frames().StepDue();
        Assert.IsNull(host.RunOf(companion), "the leave's interrupt at priority 5 ended its run (FO20)");
        Assert.AreEqual("left the hero", host.TraceOf(companion)?.Event);
        Assert.IsNotNull(host.RunOf(enemy), "another NPC's run goes on");

        // A free companion's run survives a join (0x45DE90 interrupts nothing, C5).
        var (joining, freeHost, _, wolf, loner) = Open(free: true);
        freeHost.PerformAttack(loner, wolf);
        Assert.IsNotNull(freeHost.RunOf(loner));
        Assert.AreEqual(GameSessionDisposition.Changed, joining.ForceJoinParty(joining.Version, loner).Disposition);
        freeHost.Frames().StepDue();
        Assert.IsNotNull(freeHost.RunOf(loner), "the join kept its run");
        Assert.AreEqual(0, freeHost.ObserveLeaves().Count);
        Assert.AreEqual(GameSessionDisposition.Changed, joining.LeaveParty(joining.Version, loner, new PartyLeave(Force: true)).Disposition);
        CollectionAssert.AreEqual(new[] { loner }, freeHost.ObserveLeaves().ToArray(), "a leave after the join is seen again");
    }

    private sealed class OpenPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    private static GoalHandle Leftover(AreaAiHost host, string owner, GoalType goal)
    {
        var started = host.Session.StartGoal(new(owner, GoalStarters.NoInterrupt, new GoalFrame(goal)), new OpenPorts());
        Assert.IsTrue(started.Started, $"{goal}: {started.Failure} {started.Outcome}");
        return started.Handle!.Value;
    }

    private static bool Alive(AreaAiHost host, GoalHandle handle) => host.Session.QueryGoals().TryGet(handle, out var run) && !run.Done;

    /// <summary>
    /// ARC-DEV-135 (MIG13 S5): the follower's own walk interrupts what a fight left at level 3 by the table's rule (a walk ends, a spell attempt, priority 4, goes on), and
    /// a leave interrupts at level 5 (a spell attempt ends there, a dying run, priority 5, refuses).
    /// </summary>
    [TestMethod]
    public void The_follower_branch_interrupts_a_leftover_run_at_level_3_by_the_tables_rule()
    {
        // ARC-AI-1523: the leader is far (past 4 tiles), so the follow starter takes its start path, which is the one that interrupts.
        var (session, host, _, _, companion) = Open(companionAt: new(60, 20));
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State);
        var attempt = Leftover(host, companion, GoalType.AttemptSpell);
        host.Perform(companion);
        Assert.AreEqual("Follow by its own walk", host.TraceOf(companion)?.Perform);
        Assert.IsTrue(Alive(host, attempt), "level 3 does not end a spell attempt (priority 4): it goes on");
        Assert.IsTrue(host.Holds(companion), "so the host still holds the walk");

        // Free of it, a walk (priority 2) left from the fight is ended by the same interrupt.
        host.Session.KillGoals(companion);
        host.Session.DrainGoalEnds();
        var walk = Leftover(host, companion, GoalType.MoveToTile);
        host.Perform(companion);
        Assert.AreEqual("Follow by its own walk", host.TraceOf(companion)?.Perform);
        Assert.IsFalse(Alive(host, walk), "a walk is ended at level 3");
        Assert.IsNull(host.RunOf(companion));
        Assert.IsFalse(host.Holds(companion), "and its own walk follows the hero again");
    }

    /// <summary>
    /// ARC-AI-1523 (VBG12, 0x4346A0): the follow starter interrupts only on its start path; with the leader near (4 tiles, 7 spread out) or a follow run kept it ends nothing, so a companion's
    /// please-move (68) goes on instead of being ended by the follower branch (the far case, where the starter starts, is the level-3 test above).
    /// </summary>
    [TestMethod]
    public void A_follow_that_keeps_the_run_or_finds_the_leader_near_does_not_interrupt_a_please_move()
    {
        var (session, host, _, _, companion) = Open();
        Assert.AreEqual(DangerState.None, session.QueryDanger(companion).State);
        var move = Leftover(host, companion, GoalType.PleaseMove);
        host.Perform(companion);
        StringAssert.Contains(host.TraceOf(companion)!.Perform, "Follow by its own walk");
        Assert.IsTrue(Alive(host, move), "the leader is near: the follow starter ends nothing, the please-move goes on");
        Assert.IsTrue(host.Holds(companion), "so the host still holds the walk until the move is done");
    }

    [TestMethod]
    public void A_follow_that_finds_an_equal_run_queued_does_not_interrupt_the_companions_run()
    {
        // The leader is far; a follow run of the companion stands (the starter's duplicate test, AlreadyQueued): the starter keeps it and ends nothing.
        var (_, host, _, _, companion) = Open(companionAt: new(60, 20));
        var follow = Leftover(host, companion, GoalType.Follow);
        host.Perform(companion);
        StringAssert.Contains(host.TraceOf(companion)!.Perform, "Follow by its own walk");
        Assert.IsTrue(Alive(host, follow), "an equal follow run is kept, no interrupt");
    }

    [TestMethod]
    public void A_leave_interrupts_at_level_5_which_ends_a_spell_attempt_and_leaves_a_dying_run()
    {
        var (session, host, _, _, companion) = Open();
        host.ObserveLeaves();
        var attempt = Leftover(host, companion, GoalType.AttemptSpell);
        Assert.AreEqual(GameSessionDisposition.Changed, session.LeaveParty(session.Version, companion, new PartyLeave(Force: true)).Disposition);
        CollectionAssert.AreEqual(new[] { companion }, host.ObserveLeaves().ToArray(), "level 5 ends a priority 4 run (5 > 4)");
        Assert.IsFalse(Alive(host, attempt));

        var (other, otherHost, _, _, leaver) = Open();
        otherHost.ObserveLeaves();
        var dying = Leftover(otherHost, leaver, GoalType.Dying);
        Assert.AreEqual(GameSessionDisposition.Changed, other.LeaveParty(other.Version, leaver, new PartyLeave(Force: true)).Disposition);
        Assert.AreEqual(0, otherHost.ObserveLeaves().Count, "a priority 5 run refuses level 5: nothing was freed");
        Assert.IsTrue(Alive(otherHost, dying), "it goes on");
        StringAssert.Contains(otherHost.TraceOf(leaver)?.Step, "interrupt 5 refused");
    }

    /// <summary>
    /// ARC-DEV-129: the follower smoke's pick of a follower: the heal skill check refuses an undead (critter flag 0x4) or mechanical
    /// (0x20000000) healer (0x4AE570), and the danger setter's rule 1 keeps an invulnerable (object flag 0x400000) or nigh-invulnerable
    /// (critter flag 2 0x200) NPC out of a fight (0x4AABE0); a plain human fits.
    /// </summary>
    [TestMethod]
    public void TheSmokeFollowerMustHealAndFight()
    {
        Assert.IsTrue(AreaAiHost.CanHealAndFight(0, 0, 0));
        Assert.IsTrue(AreaAiHost.HealSkillUsable(0));
        Assert.IsFalse(AreaAiHost.HealSkillUsable(0x4), "undead");
        Assert.IsFalse(AreaAiHost.HealSkillUsable(0x20000000), "mechanical");
        Assert.IsFalse(AreaAiHost.CanHealAndFight(0, 0x4, 0), "an undead follower never heals");
        Assert.IsFalse(AreaAiHost.CanHealAndFight(0x400000, 0, 0), "an invulnerable NPC never fights");
        Assert.IsFalse(AreaAiHost.CanHealAndFight(0, 0, 0x200), "a nigh-invulnerable NPC never fights");
        Assert.IsTrue(AreaAiHost.CanHealAndFight(0x100, 0x8, 0x2), "flags the rules do not read leave it fit");
    }

    [TestMethod]
    public void A_killed_npc_stops_fleeing_and_focuses_on_its_killer_through_the_kill_notice()
    {
        // ARC-AI-1134 (ledger DY3, DY7; 0x45DA20 → 0x4AA1B0, 0x4AF170): the session writes the combat focus := killer and tells the host, which runs the kill notification.
        var scene = Open();
        scene.Session.KillNotice = request => scene.Host.NoticeKill(request.Victim, request.Killer);
        Assert.IsTrue(scene.Host.SpellFlee(scene.Enemy, Pc), "the enemy flees from the hero");
        Assert.AreEqual(DangerState.Flee, scene.Session.QueryDanger(scene.Enemy).State);
        var target = scene.Combat.Build(scene.Session, scene.Enemy).Combat?.Target ?? throw new AssertFailedException("the enemy has no combat build");
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.Kill(scene.Session.Version, target, SessionSpellCaster.Hero).Disposition);
        var danger = scene.Session.QueryDanger(scene.Enemy);
        Assert.AreEqual(DangerState.None, danger.State, "the kill notification's stop fleeing asked the setter for none");
        Assert.AreEqual(SessionCombatant.Hero, danger.Focus, "the dead NPC's combat focus is its killer");
        Assert.IsFalse(scene.Host.NoticeKill(Pc, null), "the hero is no NPC of the kill's branch");
        Assert.IsFalse(scene.Host.NoticeKill("oid:unknown", Pc), "an NPC this host does not run is left alone");
    }

    [TestMethod]
    public void The_reactions_setter_asks_for_no_alerted_sound_so_the_attack_goal_carries_none()
    {
        // ARC-RENDER-1126 (0x4A92D0, 0x4AABE0, 0x4296D0): only the danger update's setter call asks for the alerted sound (its pass record's bark slot, the host's UpdateDanger);
        // the attack reaction's setter call passes none (its caller gives the setter no bark slot), so even with a scene that has a sound for the NPC the attack goal carries none and the approach plays nothing.
        var (session, host, _, enemy, companion) = Open(alertedSound: entity => entity == Entity(1) ? 4242 : null);
        host.HeroAttacked(enemy);
        Assert.AreEqual(DangerState.CombatFocus, session.QueryDanger(companion).State, "the companion fights the hero's victim");
        // The reaction's setter asked for no sound: the pass's perform starts the attack with none.
        host.Perform(companion);
        Assert.IsNull(host.RunOf(companion)?.Bottom.Sound, "no alerted sound without the danger update's call");
        var plain = Run(host, companion);
        Assert.IsFalse(plain.Any(n => n.Kind == AreaAiNoticeKind.Sound), "nothing played");
    }

    // ARC-AI-1515 (0x4A9650, follower branch of the PC attacker, VBG4): the hero's hit on its own follower changes the reaction by AI parameter 9 and runs the
    // follow check (limit ignored); a refusal makes it leave with NPC flag 0x20 and its refusal line; a follower that stays says the accidental line one time in three.
    private static readonly FollowThresholds Thresholds = new(DislikeReaction: 40, MaxAlignmentAbove: 100, MaxAlignmentBelow: 100, LevelGate: 10);
    private static PartyConsentFacts Consent(int reaction) => new(false, PersuasionTraining.None, 8, 0, 1, null, reaction, Thresholds, 0, 1, 2);
    private static AiParameterTable Parameter9(int value)
    {
        var row = string.Join(' ', Enumerable.Range(0, 17).Select(i => i == 9 ? value : 0));
        return AiParameterTable.Create(MesFile.Parse(System.Text.Encoding.ASCII.GetBytes("{0}{" + row + "}")).File!, null);
    }

    private static Scene OpenFollowerHit(int reaction, int param9 = 5) =>
        Open(joinFacts: Consent(100), followFacts: entity => Consent(reaction), table: Parameter9(param9));

    private static void AssertLeft(Scene scene, bool left)
    {
        var record = scene.Session.QueryCompanion(scene.Companion);
        Assert.AreEqual(left, !record.LedByPc, $"the follower {(left ? "left" : "stayed")}: {scene.Host.TraceOf(scene.Companion)?.FollowerHit}");
        Assert.AreEqual(left, record.Jilted, "the leave sets JILTED (NPC flag 0x20)");
    }

    [TestMethod]
    public void A_melee_or_ranged_hit_on_a_follower_that_refuses_to_follow_makes_it_leave_jilted_with_the_refusal_line()
    {
        // The hero's melee swing, its ranged missile and its thrown item all raise the same attack event through HeroAttacked.
        var scene = OpenFollowerHit(reaction: 40);
        Assert.IsTrue(scene.Session.QueryCompanion(scene.Companion).LedByPc);
        scene.Host.HeroAttacked(scene.Companion);
        AssertLeft(scene, left: true);
        StringAssert.Contains(scene.Host.TraceOf(scene.Companion)!.FollowerHit!, "reaction-changed=True");
        StringAssert.Contains(scene.Host.TraceOf(scene.Companion)!.FollowerHit!, "left jilted, refusal lines 1800-1899 (Dislike)");
    }

    [TestMethod]
    public void An_area_spell_that_catches_a_follower_runs_the_same_check_through_the_spell_notice()
    {
        var scene = OpenFollowerHit(reaction: 40);
        scene.Host.SpellAttacked(Pc, scene.Companion);
        AssertLeft(scene, left: true);
        StringAssert.Contains(scene.Host.TraceOf(scene.Companion)!.FollowerHit!, "refusal lines 1800-1899");
    }

    [TestMethod]
    public void A_follower_that_passes_the_check_stays_and_says_the_accidental_line_one_time_in_three()
    {
        var spoke = 0;
        var scene = OpenFollowerHit(reaction: 100);
        for (var hit = 0; hit < 60; hit++)
        {
            scene.Host.HeroAttacked(scene.Companion);
            AssertLeft(scene, left: false);
            var trace = scene.Host.TraceOf(scene.Companion)!.FollowerHit!;
            StringAssert.Contains(trace, "reaction-changed=True");
            StringAssert.Contains(trace, "stays (Accepted)");
            if (trace.Contains("accidental-attack lines 2300-2399")) spoke++;
        }
        Assert.IsTrue(spoke is > 5 and < 40, $"one time in three of 60 hits: {spoke}");
    }

    [TestMethod]
    public void A_forced_follower_is_accepted_by_the_check_whatever_its_reaction()
    {
        // The check accepts a forced follower before the reaction step (0x4AD950); the session test holds the refused leave (extraordinary charisma).
        var forced = Open(followFacts: entity => Consent(0), table: Parameter9(5));
        forced.Host.HeroAttacked(forced.Companion);
        AssertLeft(forced, left: false);
        StringAssert.Contains(forced.Host.TraceOf(forced.Companion)!.FollowerHit!, "stays (Accepted)");
    }

    [TestMethod]
    public void Without_follow_facts_the_hit_changes_the_reaction_only()
    {
        var scene = Open(joinFacts: Consent(100), table: Parameter9(5));
        scene.Host.HeroAttacked(scene.Companion);
        AssertLeft(scene, left: false);
        StringAssert.Contains(scene.Host.TraceOf(scene.Companion)!.FollowerHit!, "follow check: no facts");
    }

    [TestMethod]
    public void A_free_former_follower_is_no_follower_so_the_hit_runs_no_check()
    {
        var scene = Open(free: true, followFacts: entity => Consent(0), table: Parameter9(5));
        scene.Host.HeroAttacked(scene.Companion);
        Assert.IsFalse(scene.Session.QueryCompanion(scene.Companion).Jilted);
        Assert.IsNull(scene.Host.TraceOf(scene.Companion)?.FollowerHit);
        Assert.IsNotNull(scene.Host.TraceOf(scene.Companion)?.Event, "the hit reached the host's attack event");
    }

    // ARC-AI-1517 (0x4A9650 → 0x45EE30, VBG6): the attack event unconceals the victim's PC group whoever attacks.
    [TestMethod]
    public void An_npcs_hit_on_the_sneaking_hero_ends_the_heros_and_its_followers_concealment()
    {
        var scene = Open();
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.ToggleHeroConcealment(scene.Session.Version).Disposition);
        Assert.IsTrue(scene.Session.IsConcealed(SessionCombatant.Hero) && scene.Session.IsConcealed(SessionCombatant.Critter(scene.Companion)));
        scene.Host.SpellAttacked(scene.Enemy, Pc);
        Assert.IsFalse(scene.Session.IsConcealed(SessionCombatant.Hero), "the hero stops prowling");
        Assert.IsFalse(scene.Session.IsConcealed(SessionCombatant.Critter(scene.Companion)), "and so does its follower");
        Assert.AreEqual(0, scene.Session.QueryConcealment().Npcs.Count);
    }

    [TestMethod]
    public void An_npcs_hit_on_a_sneaking_follower_ends_the_whole_groups_concealment()
    {
        var scene = Open();
        scene.Session.ToggleHeroConcealment(scene.Session.Version);
        scene.Host.SpellAttacked(scene.Enemy, scene.Companion);
        Assert.IsFalse(scene.Session.IsConcealed(SessionCombatant.Hero), "the victim's PC leader stops prowling");
        Assert.IsFalse(scene.Session.IsConcealed(SessionCombatant.Critter(scene.Companion)));
    }

    [TestMethod]
    public void The_script_attack_action_ends_the_concealment_too_in_one_revision()
    {
        var scene = Open();
        scene.Session.ToggleHeroConcealment(scene.Session.Version);
        var revision = scene.Session.Version.Revision;
        scene.Host.ScriptAttack(scene.Enemy, Pc);
        Assert.IsFalse(scene.Session.IsConcealed(SessionCombatant.Hero));
        Assert.IsFalse(scene.Session.IsConcealed(SessionCombatant.Critter(scene.Companion)));
        Assert.AreEqual(revision + 1, scene.Session.Version.Revision, "the concealment ended in one revision of its own (a PC victim writes nothing else)");
    }

    [TestMethod]
    public void A_hit_on_a_hero_that_is_not_sneaking_leaves_the_concealment_state_empty()
    {
        var scene = Open();
        scene.Host.SpellAttacked(scene.Enemy, Pc);
        Assert.AreEqual((false, 0), (scene.Session.QueryConcealment().Hero, scene.Session.QueryConcealment().Npcs.Count));
    }

    // ARC-AI-1516 (EX14, 0x4A9650 step 12): the hero's stray missile on a non-follower NPC is an accidental event: its reaction toward the hero drops by 10 in place of the deliberate hit's lowering
    // to AI parameter 10, and the event is not the follower branch (the free former follower stands for the NPC no one leads).
    [TestMethod]
    public void The_heros_stray_missile_on_an_npc_that_follows_nobody_lowers_its_reaction_by_ten()
    {
        var scene = Open(free: true, followFacts: entity => Consent(0), table: Parameter9(5));
        Assert.IsNull(scene.Session.QueryReactionRecord(scene.Companion)?.Remembered, "nothing remembered yet");
        scene.Host.HeroStrayAttacked(scene.Companion);
        Assert.AreEqual("None", scene.Host.TraceOf(scene.Companion)?.Event, "the event ran to the reaction step");
        Assert.AreEqual(-10, scene.Session.QueryReactionRecord(scene.Companion)?.Remembered, "the accidental hit costs 10");
        Assert.IsNull(scene.Host.TraceOf(scene.Companion)?.FollowerHit, "no follower branch");
    }

    // ARC-AI-1522 (ledger VB2-3, 0x4AA300 → 0x44E050 and 0x44E0E0): the spell's peace ends the NPC's attack run at the head's party and each follower's attack run at the NPC.
    [TestMethod]
    public void Peace_with_a_follower_ends_the_npcs_attack_run_at_the_hero_its_head()
    {
        var (_, host, _, enemy, companion) = Open();
        host.PerformAttack(enemy, Pc);
        Assert.AreEqual((GoalType.Attack, Pc), (host.RunOf(enemy)?.Bottom.Goal, host.RunOf(enemy)?.Bottom.Target), "the enemy attacks the hero");
        Assert.IsTrue(host.SpellPeace(enemy, companion), host.TraceOf(enemy)?.Setter);
        Assert.IsNull(host.RunOf(enemy), "the head of the companion is the hero: the attack at the hero ends");
    }

    [TestMethod]
    public void Peace_with_the_hero_ends_the_enemys_attack_at_a_follower_and_the_followers_attack_at_it()
    {
        var (_, host, _, enemy, companion) = Open();
        host.PerformAttack(enemy, companion);
        host.PerformAttack(companion, enemy);
        Assert.IsNotNull(host.RunOf(enemy));
        Assert.IsNotNull(host.RunOf(companion));
        Assert.IsTrue(host.SpellPeace(enemy, Pc), host.TraceOf(enemy)?.Setter);
        Assert.IsNull(host.RunOf(enemy), "the enemy's attack at a member of the hero's party ends");
        Assert.IsNull(host.RunOf(companion), "the follower's attack at the charmed NPC ends");
    }

    [TestMethod]
    public void Peace_leaves_a_followers_attack_at_another_target()
    {
        var (_, host, _, enemy, companion) = Open();
        host.PerformAttack(companion, Pc);
        var before = host.RunOf(companion);
        Assert.IsNotNull(before);
        Assert.IsTrue(host.SpellPeace(enemy, Pc), host.TraceOf(enemy)?.Setter);
        Assert.AreEqual(before!.Handle, host.RunOf(companion)?.Handle, "the follower's run does not aim at the NPC: left");
    }
}
