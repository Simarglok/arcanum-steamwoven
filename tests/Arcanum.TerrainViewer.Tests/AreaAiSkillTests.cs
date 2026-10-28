using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-109 (NPC AI slice I10, skill goals): the AI host's skill goals 33, 34, 86 and 67 over invented rows, no original data: a
/// human NPC (0) at world cell (10, 20) with heal 5 points and every primary stat 20, holding a healing item (1, three uses) in its
/// pack. Its heal skill on itself walks goal 33 into the attempt goal 34, turns, plays the stand-in skill animation and at the action
/// frame heals through the session, which spends a use of its item; pickpocket (86) and pick locks (67) reach their attempts, which are
/// not ready and change nothing.
/// </summary>
[TestClass]
public sealed class AreaAiSkillTests
{
    private const int HealingPrototype = 8000, Uses = 3;
    private static readonly SessionItemKind Healing = new(SessionItemType.Generic, HealingPrototype, 6000, 6001);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

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

    // A human NPC (type 16, no monster flags) with every primary stat 20, level 1, heal 5 points, 20 hit points, at world cell (10, 20).
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, string Npc, string Item);

    private static Scene Open()
    {
        var session = ConversationSessionFixture.Create();
        var rows = new[] { Npc(0), Item(1) };
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)])], [new SessionSeedItem(Entity(1), new(Healing, 1, 0, 0, null, []), null)]),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var scene = new AreaAiScene
        {
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), Npc(0), new(10, 20))], Combat = combat,
            HeroCell = () => new CellCoord(14, 20),
            NpcSheet = entity => combat.Fields(1, entity) is { } fields ? AreaAdvancement.Npc(entity, fields, _ => null) : null,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, Entity(0), Entity(1));
    }

    // Steps the host frame by frame (50 ms) until the NPC's run ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc, ref long now)
    {
        var notices = new List<AreaAiNotice>();
        for (var frame = 0; frame < 200 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Pump(now += 50);
            notices.AddRange(host.DrainNotices());
        }
        Assert.IsNull(host.RunOf(npc), "the skill run ended");
        return notices;
    }

    [TestMethod]
    public void An_npcs_heal_skill_on_itself_heals_through_the_session_and_spends_a_use_of_its_item()
    {
        var (session, host, npc, item) = Open();
        Assert.IsTrue(host.HasSkillArt(npc), "the stand-in skill art");
        var started = session.Version;
        host.PerformSkill(npc, npc, AiPassChoice.HealSkill, item);
        Assert.AreEqual(AiGoalType.UseSkill, host.RunOf(npc)?.Bottom.Goal, $"perform starts goal 33: {host.TraceOf(npc)?.Perform}");
        Assert.AreEqual(3, host.AnimationPriority(npc), "goal 33's priority (AK1)");
        Assert.AreEqual(started, session.Version, "the start changes no session state");

        var now = 0L;
        host.Pump(now);
        Assert.AreEqual(AiGoalType.AttemptSkill, host.RunOf(npc)?.Top.Goal, "within range the attempt goal 34 replaces it");
        var notices = Run(host, npc, ref now);
        var skill = notices.Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.AreEqual((AiPassChoice.HealSkill, npc), (skill.Skill, skill.Target));
        var heal = skill.Heal ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no heal");
        Assert.AreEqual(GameSessionDisposition.Changed, heal.Disposition, $"{heal.Failure}");
        Assert.IsNotNull(heal.Check);
        Assert.IsNull(heal.Charge, "the attempt goal paid its own action points");
        Assert.IsFalse(heal.FateUsed, "an NPC has no fate");
        Assert.IsTrue(heal.ItemDestroyed || heal.UsesLeft is Uses - 1, "the heal spends a use (a critical failure's five use up the three)");
        Assert.AreNotEqual(started, session.Version);
        StringAssert.StartsWith(host.TraceOf(npc)!.Skill, "heal ");
    }

    [TestMethod]
    public void A_heal_without_a_held_healing_item_is_impossible_and_changes_nothing()
    {
        var (session, host, npc, _) = Open();
        var before = session.Version;
        Assert.IsFalse(host.AttemptSkill(npc, npc, AiPassChoice.HealSkill, null));
        Assert.IsFalse(host.AttemptSkill(npc, npc, AiPassChoice.HealSkill, Entity(9)), "an item the NPC does not hold");
        Assert.IsFalse(host.AttemptSkill(npc, npc, AiPassChoice.HealSkill, npc), "a held object that is no healing item");
        Assert.IsFalse(host.AttemptSkill(npc, Entity(9), AiPassChoice.HealSkill, Entity(1)), "a target that is neither the hero nor an NPC");
        Assert.AreEqual(before, session.Version);
        Assert.IsTrue(host.DrainNotices().All(n => n.Kind == AreaAiNoticeKind.Skill && n.Heal is null));
    }

    [TestMethod]
    public void Pickpocket_and_pick_locks_reach_their_attempts_which_are_not_ready()
    {
        var (session, host, npc, _) = Open();
        var before = session.Version;
        var now = 0L;

        host.PerformSkill(npc, npc, AiExecutorRules.PickpocketSkill, null);
        host.Pump(now);
        Assert.AreEqual(AiGoalType.AttemptPickpocket, host.RunOf(npc)?.Top.Goal, "goal 86 replaces goal 33");
        var pickpocket = Run(host, npc, ref now).Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.AreEqual((AiExecutorRules.PickpocketSkill, (SessionHealResult?)null), (pickpocket.Skill, pickpocket.Heal));
        StringAssert.EndsWith(host.TraceOf(npc)!.Skill, "not ready");

        host.PerformSkill(npc, npc, AiExecutorRules.PickLocksSkill, null);
        host.Pump(now);
        Assert.AreEqual(AiGoalType.AttemptPickLocks, host.RunOf(npc)?.Top.Goal, "goal 67 replaces goal 33");
        // The stand-in art's 9 frames give 9 / 7 + 1 = 2 loops of the pick-locks animation, the attempt after them.
        var swing = host.BeginAction(npc, AiActionAnimation.PickLocks);
        Assert.AreEqual((AiActionStart.Started, 1200, 0), (swing.Start, swing.ActionFrameMs, swing.RestMs));
        var locks = Run(host, npc, ref now).Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.AreEqual(AiExecutorRules.PickLocksSkill, locks.Skill);
        Assert.AreEqual(before, session.Version, "neither attempt changed the session");
    }

    [TestMethod]
    public void The_skill_and_throw_animations_stand_in_with_the_fixed_art()
    {
        var (_, host, npc, _) = Open();
        // 9 frames at 15 per second: the action frame 6 at 400 ms, the rest 200 ms; ARC-WORLD-113: the throw's too.
        Assert.AreEqual(new AiActionSwing(AiActionStart.Started, 400, 200), host.BeginAction(npc, AiActionAnimation.Skill));
        Assert.AreEqual(new AiActionSwing(AiActionStart.Started, 400, 200), host.BeginAction(npc, AiActionAnimation.Throw));
        Assert.AreEqual(AiActionStart.CannotStart, host.BeginAction(AiSchedulerRules.Pc, AiActionAnimation.Skill).Start, "the host walks no hero");
        Assert.IsTrue(host.FaceForAction(npc, AiSchedulerRules.Pc, null), "it turns to the hero's cell");
        Assert.AreEqual(NeighborDirection.XPlus, host.Movers.FacingOf(npc, NeighborDirection.YMinus));
        Assert.IsTrue(host.FaceForAction(npc, null, new(10, 21)), "and to a tile");
        Assert.IsFalse(host.FaceForAction(npc, Entity(9), null), "an object without a cell refuses the turn");
    }
}
