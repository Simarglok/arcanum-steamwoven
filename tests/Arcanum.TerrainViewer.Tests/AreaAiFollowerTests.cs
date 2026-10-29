using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-115 (NPC AI slice I9, the host part): the AI host runs the hero's companion over invented rows, no original data: two human
/// NPCs with every primary stat 20 and 20 hit points, bare-handed, an enemy (0) at world cell (10, 20) and the hero's companion (1) at
/// (13, 21); the hero stands at (14, 20). The enemy's T0 record blocks its cell in the step evidence. The companion's cell is its own walk's (a fake that steps it at once). The attack events tell
/// the hero's followers (0x4A9B80): a companion joins the hero's fight, walks up through its own walk and strikes through the session.
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

    // A human NPC (type 16, no monster flags) with every primary stat 20, level 1, 20 hit points, at the world cell.
    private static AreaObjectRecord Npc(int index, CellCoord cell) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, cell.X, cell.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    /// <summary>The companion's own walk as the host sees it: a step moves it at once, a turn is counted.</summary>
    private sealed class Walks(AreaAiNpc companion) : IAreaAiFollowers
    {
        public readonly Dictionary<string, CellCoord> Cells = new() { [companion.Entity] = companion.Origin };
        public int Steps, Turns;
        public IReadOnlyList<AreaAiNpc> Npcs { get; } = [companion];

        public bool Step(string entity, CellCoord next, bool running)
        {
            Steps++;
            Cells[entity] = next;
            return true;
        }

        public void Face(string entity, CellCoord toward) => Turns++;
    }

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, Walks Walks, string Enemy, string Companion);

    private static Scene Open(bool companionWaits = false, bool followers = true, CellCoord? companionAt = null, CellCoord? corner = null,
        CellCoord? companionHome = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var start = companionAt ?? CompanionCell;
        var rows = new[] { Npc(0, EnemyCell), Npc(1, start) };
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], []), new(Entity(1), SessionHolderKind.Npc, false, false, [], [])], []),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, Entity(1)).Disposition);
        if (companionWaits) Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, Entity(1)).Disposition);
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
        var walks = new Walks(new(Entity(1), Npc(1, start), start));
        var scene = new AreaAiScene
        {
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), Npc(0, EnemyCell), EnemyCell)], Combat = combat,
            HeroCell = () => HeroCell,
            Companions = () => [(Key(1), walks.Cells[Entity(1)])],
            Followers = followers ? walks : null,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, walks, Entity(0), Entity(1));
    }

    // Steps the host frame by frame (50 ms) until the run of the NPC ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc)
    {
        var notices = new List<AreaAiNotice>();
        var now = 0L;
        for (var frame = 0; frame < 400 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Pump(now += 50);
            notices.AddRange(host.DrainNotices());
        }
        Assert.IsNull(host.RunOf(npc), $"the run of {npc} ended: {host.TraceOf(npc)?.Step}");
        return notices;
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
        Assert.AreEqual((AiGoalType.Attack, enemy), (run.Bottom.Goal, run.Bottom.Target), "the attack starter's goal on the enemy");

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
}
