using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;
using Arcanum.Simulation.Goals;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-109 (NPC AI slice I10, skill goals): the AI host's skill goals 33, 34, 86 and 67 over invented rows, no original data: a
/// human NPC (0) at world cell (10, 20) with heal 5 points and every primary stat 20, holding a healing item (1, three uses) in its
/// pack. Its heal skill on itself walks goal 33 into the attempt goal 34, turns, plays the stand-in skill animation and at the action
/// frame heals through the session, which spends a use of its item; pickpocket (86) and pick locks (67) reach their attempts; a scene that names no lock (ARC-COMBAT-1776) changes nothing, one that does
/// has the NPC pick it through the session.
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
    private static AreaObjectRecord Item(int index, int points = 10, int damage = 0) => new(Key(index), 0, 14, HealingPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, points), Field(27, 0), Field(28, damage), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x08), Field(60, 10), Field(61, Uses),
        ], true, true, true);

    // A human NPC (type 16, no monster flags unless given) with every primary stat 20, level 1, heal 5 points, 20 hit points, at world
    // cell (10, 20).
    private static AreaObjectRecord Npc(int index, uint critterFlags = 0, long repair = 0, uint spellFlags = 0) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, spellFlags), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, critterFlags), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0), Array(40, repair, 0, 0, 0), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, string Npc, string Item);

    // ARC-AI-2663: where the item (Entity 1) lies: the NPC's own list (default), another NPC's list (Entity 2), a junk pile of the map's data (Entity 3, prototype 3043), loose on the ground.
    public enum Place { Npc, OtherNpc, MapPile, Ground }

    private static AreaObjectRecord PileRow(int index, CellCoord at) => new(Key(index), 0, 2, SessionObjectFacts.JunkPilePrototypeFile, "p", "p", "r", "s", AreaScope.Core, null, at.X, at.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x20000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 2),
            Array(30), Empty(31), Field(36, 0), Field(38, 0),
        ], true, true, true);

    private static Scene Open(uint critterFlags = 0, bool hero = false, Func<AreaAiScene, AreaAiScene>? tweak = null, long repair = 0, int itemPoints = 10, int itemDamage = 0, uint spellFlags = 0, GameSessionOwner? with = null, Place place = Place.Npc)
    {
        var session = with ?? (hero ? ConversationSessionFixture.CreateWithHero() : ConversationSessionFixture.Create());
        var rows = new List<AreaObjectRecord> { Npc(0, critterFlags, repair, spellFlags), Item(1, itemPoints, itemDamage) };
        var holders = new List<SessionSeedHolder> { new(Entity(0), SessionHolderKind.Npc, false, false, [], place == Place.Npc ? [Entity(1)] : []) };
        if (place == Place.OtherNpc) { rows.Add(Npc(2)); holders.Add(new(Entity(2), SessionHolderKind.Npc, false, false, [], [Entity(1)])); }
        if (place == Place.MapPile) { rows.Add(PileRow(3, new(12, 22))); holders.Add(new(Entity(3), SessionHolderKind.Container, false, false, [], [Entity(1)])); }
        var seeded = new SessionSeedItem(Entity(1), new(Healing, 1, 0, place == Place.Ground ? -1 : 0, null, []), place == Place.Ground ? new CellCoord(12, 20) : null);
        var map = new AreaInventoryMap(1, new(holders, [seeded]), rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
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
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), Npc(0, critterFlags, repair, spellFlags), new(10, 20))], Combat = combat,
            HeroCell = () => new CellCoord(14, 20),
            NpcSheet = entity => combat.Fields(1, entity) is { } fields ? AreaAdvancement.Npc(entity, fields, _ => null) : null,
        };
        if (tweak is not null) scene = tweak(scene);
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, Entity(0), Entity(1));
    }

    // Steps the host frame by frame (50 ms, at most 20 s: the pick-locks loops take 13.2 s) until the NPC's run ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc, ref long now)
    {
        var notices = new List<AreaAiNotice>();
        for (var frame = 0; frame < 400 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Frames().AdvanceHost(50); now += 50;
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
        Assert.AreEqual(GoalType.UseSkill, host.RunOf(npc)?.Bottom.Goal, $"perform starts goal 33: {host.TraceOf(npc)?.Perform}");
        Assert.AreEqual(3, host.AnimationPriority(npc), "goal 33's priority (AK1)");
        Assert.AreEqual(started, session.Version, "the start changes no session state");

        var now = 0L;
        host.Frames().StepDue();
        Assert.AreEqual(GoalType.AttemptSkill, host.RunOf(npc)?.Top.Goal, "within range the attempt goal 34 replaces it");
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

    // ARC-SCRIPT-1732: the healer menu's facts of a speaker and its two starters (0x4AE570, 0x4B1AB0, 0x4C5EB0, 0x4350F0).
    [TestMethod]
    public void The_healer_facts_read_the_speakers_rank_skill_and_leaders_and_the_service_starts_the_skill_goal_on_the_hero()
    {
        var (session, host, npc, item) = Open(hero: true);
        var facts = host.HealerFacts(npc) ?? throw new AssertFailedException("no facts");
        Assert.IsTrue(facts.SkillAllowed, "a healing item in the pack and a critter that is neither undead nor mechanical (0x4AE570)");
        Assert.AreEqual(0, facts.Rank, "no cast host: no spell book, rank 0");
        Assert.IsTrue(facts.SkillLevel > 0 && facts.SkillTraining == 0, "the NPC's heal skill: level and training tier");
        Assert.IsTrue(facts.NoPcLeader && !facts.LedByPc, "a free NPC has no PC leader");
        Assert.IsNull(host.HealerFacts(AiSchedulerRules.Pc), "the hero is no healer here");
        Assert.IsNull(host.HealerFacts(Entity(9)), "an object that is no NPC of the host");
        Assert.IsFalse(Open(critterFlags: 0x4).Host.HealerFacts(npc)!.Value.SkillAllowed, "an undead healer may not use the skill (refusal 2)");

        Assert.IsFalse(host.HealerCast(npc, 60, free: true), "no cast host: the spell cannot start");
        Assert.IsFalse(host.HealerService(Entity(9)) || host.HealerService(AiSchedulerRules.Pc));
        var started = session.Version;
        Assert.IsTrue(host.HealerService(npc));
        var goal = host.RunOf(npc)?.Bottom ?? throw new AssertFailedException($"no run: {host.TraceOf(npc)?.Perform}");
        Assert.AreEqual((GoalType.UseSkill, AiSchedulerRules.Pc, AiPassChoice.HealSkill, item), (goal.Goal, goal.Target, goal.Skill, goal.Item), "goal 33 with the heal skill, the hero as the target and the NPC's tool");
        Assert.AreEqual(started, session.Version, "the start changes no session state");
        var now = 0L;
        var heal = Run(host, npc, ref now).Single(n => n.Kind == AreaAiNoticeKind.Skill).Heal ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no heal");
        Assert.AreEqual(GameSessionDisposition.Changed, heal.Disposition, $"{heal.Failure}");
        Assert.IsNotNull(heal.Check, "the heal check ran on the hero");
    }

    // ARC-SCRIPT-1798 (ledger DSV2, DSV5): the u:N reply's facts and service for the skill the reply names.
    [TestMethod]
    public void The_service_facts_and_starter_take_the_skill_the_reply_names()
    {
        var (_, host, npc, _) = Open(hero: true);
        var undead = Open(critterFlags: 0x4).Host.HealerFacts(npc, AiPassChoice.HealSkill)!.Value;
        Assert.IsFalse(undead.SkillAllowed, "skill 10 keeps the heal test");
        var other = Open(critterFlags: 0x4).Host.HealerFacts(npc, 3)!.Value;
        Assert.IsTrue(other.SkillAllowed, "any other skill passes 0x4AE570");
        Assert.IsNull(host.HealerFacts(Entity(9), 3));
        var outside = host.HealerFacts(npc, 99)!.Value;
        Assert.AreEqual((0, 0, true), (outside.SkillLevel, outside.SkillTraining, outside.SkillAllowed), "a number outside the 16 skills has no level or tier");

        Assert.IsTrue(host.HealerService(npc, 99), "a number the starter cannot name starts nothing and still answers");
        Assert.IsNull(host.RunOf(npc));
        Assert.IsTrue(host.HealerService(npc, 3));
        var goal = host.RunOf(npc)?.Bottom ?? throw new AssertFailedException($"no run: {host.TraceOf(npc)?.Perform}");
        Assert.AreEqual((GoalType.UseSkill, AiSchedulerRules.Pc, 3, (string?)null), (goal.Goal, goal.Target, goal.Skill, goal.Item), "goal 33 with the named skill and no tool (0x4C91F0 answers none)");
    }

    // ARC-SCRIPT-1789 (ledger HLR10): the leaderless service's flag is passed on and the goal is the one of a service with a leader.
    [TestMethod]
    public void The_leaderless_service_flag_changes_no_goal()
    {
        var (_, plain, npc, item) = Open(hero: true);
        var (_, flagged, npc2, _) = Open(hero: true);
        Assert.IsTrue(plain.HealerService(npc));
        Assert.IsTrue(flagged.HealerService(npc2, AiPassChoice.HealSkill, noPcLeader: true));
        var goal = flagged.RunOf(npc2)?.Bottom ?? throw new AssertFailedException($"no run: {flagged.TraceOf(npc2)?.Perform}");
        Assert.AreEqual((GoalType.UseSkill, AiSchedulerRules.Pc, AiPassChoice.HealSkill, item), (goal.Goal, goal.Target, goal.Skill, goal.Item));
        Assert.AreEqual(plain.RunOf(npc)!.Bottom, goal, "the flag 0x2000 is dropped by the skill goals (SK8), as in the original");
        Assert.AreEqual(plain.RunOf(npc)!.Flags, flagged.RunOf(npc2)!.Flags);
    }

    // Hurts the NPC to half its hit points (below the heal skill's 90 %, above the 30 % care line).
    private static int Hurt(GameSessionOwner session, AreaAiHost host, string npc)
    {
        var life = host.AiLifeTarget(npc) ?? throw new AssertFailedException("the NPC's life target");
        var max = session.QueryLife(life).View!.Status.MaxHitPoints;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyDamage(session.Version, life, new LifeDamage { HitPoints = max / 2 }).Disposition);
        return session.QueryLife(life).View!.Status.HitPoints;
    }

    [TestMethod]
    public void The_heal_step_finds_the_held_healing_item_and_its_skill_rung_heals_the_hurt_npc_through_goal_33()
    {
        var (session, host, npc, item) = Open();
        Assert.AreEqual(item, host.HealingItem(npc), "the first generic item with the healing flag (0x4C91F0, HI1)");
        Assert.IsNull(host.HealingItem(AiSchedulerRules.Pc), "the hero's items are not an NPC's list");
        Assert.IsTrue(host.HealSkillAccepts(npc, item, npc), "itself");
        Assert.IsTrue(host.HealSkillAccepts(npc, item, AiSchedulerRules.Pc), "the hero (type 15)");
        Assert.IsFalse(host.HealSkillAccepts(npc, npc, npc), "refusal 1: no healing item");
        Assert.IsFalse(host.HealSkillAccepts(npc, item, "oid:" + new string('f', 64)), "refusal 2: no critter");

        Assert.IsFalse(host.Heal(npc), "unhurt: nothing to heal");
        var hurt = Hurt(session, host, npc);
        Assert.IsTrue(host.Heal(npc), "the heal step chose");
        StringAssert.Contains(host.TraceOf(npc)!.Item, $"heal {npc} {AiHealStep.HealSkill} spell=-1 item={item}");
        host.Perform(npc);
        Assert.AreEqual(GoalType.UseSkill, host.RunOf(npc)?.Bottom.Goal, $"perform starts goal 33: {host.TraceOf(npc)?.Perform}");
        var now = 0L;
        var heal = Run(host, npc, ref now).Single(n => n.Kind == AreaAiNoticeKind.Skill).Heal ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no heal");
        Assert.AreEqual(GameSessionDisposition.Changed, heal.Disposition, $"{heal.Failure}");
        Assert.IsTrue(heal.ItemDestroyed || heal.UsesLeft is Uses - 1, "the heal spends a use");
        var after = session.QueryLife(host.AiLifeTarget(npc)!).View!.Status.HitPoints;
        Assert.IsTrue(heal.Check!.Success ? after > hurt : after == hurt, $"a success heals, a failure does not: {hurt} -> {after}");
    }

    [TestMethod]
    public void An_undead_healer_is_refused_the_heal_skill_and_its_heal_step_chooses_nothing()
    {
        var (session, host, npc, item) = Open(critterFlags: 0x4);
        Assert.AreEqual(item, host.HealingItem(npc), "the item search reads no healer flags");
        Assert.IsFalse(host.HealSkillAccepts(npc, item, npc), "the check reads the healer's critter flags 0x20000004 (HI2)");
        Hurt(session, host, npc);
        var before = session.Version;
        Assert.IsFalse(host.Heal(npc), "no list entry either");
        Assert.AreEqual(before, session.Version);
        var (_, mechanical, healer, tool) = Open(critterFlags: 0x20000000);
        Assert.IsFalse(mechanical.HealSkillAccepts(healer, tool, healer), "a mechanical healer is refused too");
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
    public void Pickpocket_on_itself_is_refused_and_pick_locks_reaches_its_attempt_which_is_not_ready()
    {
        var (session, host, npc, _) = Open();
        var before = session.Version;
        var now = 0L;

        host.PerformSkill(npc, npc, AiExecutorRules.PickpocketSkill, null);
        host.Frames().StepDue();
        Assert.AreEqual(GoalType.AttemptPickpocket, host.RunOf(npc)?.Top.Goal, "goal 86 replaces goal 33");
        var pickpocket = Run(host, npc, ref now).Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.AreEqual((AiExecutorRules.PickpocketSkill, (SessionHealResult?)null), (pickpocket.Skill, pickpocket.Heal));
        // ARC-DEV-246: the NPC's pick pocket is the session's; a thief on itself is refused before anything is rolled.
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidPickPocketVictim), (pickpocket.PickPocket!.Disposition, pickpocket.PickPocket.Failure));

        host.PerformSkill(npc, npc, AiExecutorRules.PickLocksSkill, null);
        host.Frames().StepDue();
        Assert.AreEqual(GoalType.AttemptPickLocks, host.RunOf(npc)?.Top.Goal, "goal 67 replaces goal 33");
        // The stand-in art's 15 frames per second and 9 frames give 15 x 10 / 7 + 1 = 22 loops of the pick-locks animation (ARC-AI-1524, 0x436220), the attempt after them: 22 x 9 / 15 = 13.2 s.
        var swing = host.BeginAction(npc, AiActionAnimation.PickLocks);
        Assert.AreEqual((AiActionStart.Started, 13200, 0), (swing.Start, swing.ActionFrameMs, swing.RestMs));
        var locks = Run(host, npc, ref now).Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.AreEqual(AiExecutorRules.PickLocksSkill, locks.Skill);
        Assert.AreEqual(before, session.Version, "neither attempt changed the session");
    }

    [TestMethod]
    public void The_use_item_with_skill_node_picks_at_once_with_the_modifier_and_the_other_skills_take_the_attempt_of_goal_34()
    {
        // ARC-SCRIPT-2752 (0x428E10, ledger SX4): pick locks through goal 40 runs the check with the goal's modifier (the plain attempt has none); a lock the scene does not name changes nothing.
        var door = new SessionPickLockTarget(new(new(1), "door:1"), new(OpenableKind.Portal, LockRules.LockedBit, 0, 0), 0);
        var (_, host, npc, _) = Open(hero: true, tweak: scene => scene with { PickLockTarget = target => target == "door:1" ? door : null });
        host.AttemptPickLocks(npc, "door:1", null);
        var plain = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PickLock!.Check!.Check.Difficulty;
        host.AttemptSkillWithItem(npc, "door:1", null, AiExecutorRules.PickLocksSkill, 30);
        var notice = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.AreEqual(AiExecutorRules.PickLocksSkill, notice.Skill);
        Assert.AreEqual(plain + 30, notice.PickLock!.Check!.Check.Difficulty, "the modifier is the check's invocation modifier");
        host.AttemptSkillWithItem(npc, "door:9", null, AiExecutorRules.PickLocksSkill, 30);
        Assert.IsNull(host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PickLock);
        Assert.AreEqual("pick locks door:9: no lock target", host.TraceOf(npc)!.Skill);
    }

    [TestMethod]
    public void A_followers_lock_pick_is_goal_forty_with_no_animation_and_its_attempt_is_the_sessions_pick()
    {
        // ARC-COMBAT-1878 (0x571CB0 → 0x4352C0, descriptor 0x5AA5D0): the use-item-with-skill goal walks near, then its attempt node is the shared pick-locks check at once: no skill animation, no timer.
        var door = new SessionPickLockTarget(new(new(1), "door:1"), new(OpenableKind.Portal, LockRules.LockedBit, 0, 0), 0);
        var doorCell = new CellCoord(15, 20);
        var (session, host, npc, _) = Open(hero: true, tweak: scene => scene with { PickLockTarget = target => target == "door:1" ? door : null, ObjectFacts = id => id == "door:1" ? new AreaAiObjectFacts(1, doorCell) : null });
        doorCell = new(host.Tile(npc)!.Value.X + 3, host.Tile(npc)!.Value.Y);
        Assert.IsTrue(host.Lockable("door:1") && !host.Lockable("door:9"), "a lock the scene names is lockable (0x441CF0)");
        Assert.IsTrue(host.StartPickLocks(npc, "door:1", null), host.TraceOf(npc)?.Perform);
        Assert.AreEqual(GoalType.UseItemWithSkill, host.RunOf(npc)!.Bottom.Goal);
        var now = 0L;
        var notices = Run(host, npc, ref now);
        var trace = host.TraceOf(npc);
        var locks = notices.SingleOrDefault(n => n.Kind == AreaAiNoticeKind.Skill) ?? throw new AssertFailedException($"no skill notice after {now} ms: notices={string.Join(",", notices.Select(n => n.Kind))} perform={trace?.Perform} step={trace?.Step} skill={trace?.Skill} item={trace?.Item}");
        Assert.AreEqual(AiExecutorRules.PickLocksSkill, locks.Skill);
        Assert.IsNotNull(locks.PickLock, host.TraceOf(npc)?.Skill);
        Assert.IsTrue(now < 5_000, $"no 13.2 s animation: the run took {now} ms");
        StringAssert.StartsWith(host.TraceOf(npc)!.Skill, "pick locks door:1");
    }

    [TestMethod]
    public void An_npc_picks_the_lock_the_scene_names_through_the_session_with_its_own_skill()
    {
        // ARC-COMBAT-1776 (0x42A4E0): the pick-locks attempt is the session's PickLockByNpc on the lock the scene names, with the NPC's sheet and no action points of its own.
        var door = new SessionPickLockTarget(new(new(1), "door:1"), new(OpenableKind.Portal, LockRules.LockedBit, 0, 0), 0);
        var (session, host, npc, _) = Open(hero: true, tweak: scene => scene with { PickLockTarget = target => target == "door:1" ? door : null });
        var hero = session.QueryHero();
        var picked = false;
        for (var attempt = 0; attempt < 400 && !picked; attempt++)
        {
            host.AttemptPickLocks(npc, "door:1", null);
            var notice = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill);
            Assert.AreEqual(AiExecutorRules.PickLocksSkill, notice.Skill);
            var pick = notice.PickLock ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no pick");
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (pick.Disposition, pick.Failure));
            Assert.IsNull(pick.Charge);
            Assert.IsFalse(pick.FateUsed, "an NPC has no fate");
            StringAssert.StartsWith(host.TraceOf(npc)!.Skill, "pick locks door:1 Changed");
            picked = pick.Check!.Success;
            if (picked) Assert.AreEqual((PickLockResult.Unlocked, false), (pick.Outcome!.Value.Result, pick.State!.Value.Locked));
            // A failure leaves the lock alone unless it jams; either way the lock is the session's.
            else Assert.AreEqual(pick.Check.Critical ? PickLockResult.Jammed : PickLockResult.Failed, pick.Outcome!.Value.Result);
            if (!picked && pick.Check.Critical) break;
        }
        Assert.IsTrue(picked || (session.QueryOpenable(door.Openable, door.Baseline).Flags & LockRules.JammedBit) != 0, "an unlock or a jam came up within 400 attempts");
        Assert.AreEqual(hero, session.QueryHero(), "the hero is untouched");
        // A target the scene does not name is a refusal that changes nothing.
        var before = session.Version;
        host.AttemptPickLocks(npc, "door:9", null);
        var none = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill);
        Assert.IsNull(none.PickLock);
        Assert.AreEqual("pick locks door:9: no lock target", host.TraceOf(npc)!.Skill);
        Assert.AreEqual(before, session.Version);
    }

    [TestMethod]
    public void An_npc_thief_takes_a_hundred_coins_from_the_hero_through_the_session_when_its_check_succeeds()
    {
        // ARC-DEV-246 (GT204-GT209): script action 115's steal ends in the pick pocket attempt, which moves min(coins, 100) from the hero's gold to the thief.
        var (session, host, npc, _) = Open(hero: true);
        Assert.IsTrue(session.QueryGold() >= 100, "the fixture hero holds gold");
        var moved = 0;
        for (var attempt = 0; attempt < 300 && moved == 0; attempt++)
        {
            var before = (session.QueryGold(), session.QueryNpcGold(npc));
            var success = host.AttemptSkill(npc, AiSchedulerRules.Pc, AiExecutorRules.PickpocketSkill, null);
            var notice = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill);
            Assert.AreEqual(GameSessionDisposition.Changed, notice.PickPocket!.Disposition, $"{notice.PickPocket.Failure}: {host.TraceOf(npc)?.Skill}");
            Assert.AreEqual(success, notice.PickPocket.Check!.Success, "the answer is the check's success (run flag 0x40000)");
            moved = notice.PickPocket.Moved;
            Assert.AreEqual((before.Item1 - moved, before.Item2 + moved), (session.QueryGold(), session.QueryNpcGold(npc)), "the coins moved exactly as the result says");
        }
        Assert.AreEqual(100, moved, $"a success moved 100 coins within 300 attempts: {host.TraceOf(npc)?.Skill}");
    }

    [TestMethod]
    public void A_noticed_failed_pick_pocket_runs_the_thiefs_point_33_before_the_victims_point_11()
    {
        // ARC-SCRIPT-2858 (TQ16, 0x4C7160): the thief's caught-thief script (the thief attachee, the victim the triggerer) runs first and its answer is ignored; then the victim's catching-thief script (the thief the triggerer), and the attack event follows
        // when that one lets it. Invented scripts; the scene's callbacks record the calls.
        var calls = new List<string>();
        var (_, host, npc, _) = Open(hero: true, tweak: scene => scene with
        {
            CaughtThief = (thief, victim) => calls.Add($"33 thief={thief} victim={victim}"),
            CatchingThief = (victim, thief) => { calls.Add($"11 victim={victim} thief={thief}"); return true; },
        });
        var noticed = 0;
        for (var attempt = 0; attempt < 600 && noticed < 3; attempt++)
        {
            calls.Clear();
            host.AttemptSkill(npc, AiSchedulerRules.Pc, AiExecutorRules.PickpocketSkill, null);
            var result = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PickPocket!;
            if (!result.Noticed) { Assert.AreEqual(0, calls.Count, "no script runs for an unnoticed attempt"); continue; }
            noticed++;
            CollectionAssert.AreEqual(new[] { $"33 thief={npc} victim={AiSchedulerRules.Pc}", $"11 victim={AiSchedulerRules.Pc} thief={npc}" }, calls, "33 before 11");
        }
        Assert.IsTrue(noticed > 0, "a noticed attempt came up within 600");
    }

    [TestMethod]
    public void A_skill_with_no_case_of_its_own_answers_the_sessions_plain_roll_and_one_with_a_handler_not_built_answers_false()
    {
        // ARC-AI-1854 (0x4C7160): bow, melee, ... are the shared check alone; repair has its own tests (ARC-AI-2622) (gambling is the next test, pick locks has its own goal 67).
        var (session, host, npc, _) = Open(hero: true);
        var before = session.Version.Revision;
        var answer = host.AttemptSkill(npc, AiSchedulerRules.Pc, (int)Skill.Melee, null);
        var notice = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill);
        var roll = notice.PlainRoll ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no roll");
        Assert.AreEqual((GameSessionDisposition.Changed, roll.Success), (roll.Disposition, answer), "the answer is the roll's success (run flag 0x40000)");
        Assert.AreEqual(before + 1, session.Version.Revision, "the roll is one revision");
        StringAssert.StartsWith(host.TraceOf(npc)!.Skill, $"skill {(int)Skill.Melee} on {AiSchedulerRules.Pc} Changed");
        foreach (var skill in new[] { Skill.PickLocks })
        {
            var revision = session.Version.Revision;
            Assert.IsFalse(host.AttemptSkill(npc, AiSchedulerRules.Pc, (int)skill, null), skill.ToString());
            Assert.IsNull(host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll);
            Assert.AreEqual(revision, session.Version.Revision, "nothing is rolled");
            StringAssert.EndsWith(host.TraceOf(npc)!.Skill, "not ready");
        }
        Assert.IsFalse(host.AttemptSkill(npc, AiSchedulerRules.Pc, 99, null), "a number outside the 16 skills");
    }

    private static int Successes(AreaAiHost host, string npc, Skill skill, int attempts)
    {
        var wins = 0;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (host.AttemptSkill(npc, AiSchedulerRules.Pc, (int)skill, null)) wins++;
            var roll = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll!;
            Assert.AreEqual(GameSessionDisposition.Changed, roll.Disposition, host.TraceOf(npc)?.Skill);
        }
        return wins;
    }

    [TestMethod]
    public void A_dodge_attempt_is_forced_to_fail_for_a_stunned_paralysed_or_stoned_actor_and_not_for_a_free_one()
    {
        // ARC-AI-2625 (0x4C7160 case 1): critter flags 0x60 and the spell flag 0x10000 of the actor force the failure; the roll is still drawn.
        var (_, free, freeNpc, _) = Open(hero: true);
        Assert.IsTrue(Successes(free, freeNpc, Skill.Dodge, 80) > 0, "a free actor dodges at least once");
        foreach (var (critter, spell) in new[] { (0x20u, 0u), (0x40u, 0u), (0u, 0x10000u) })
        {
            var (session, host, npc, _) = Open(critterFlags: critter, hero: true, spellFlags: spell);
            var before = session.Version.Revision;
            Assert.AreEqual(0, Successes(host, npc, Skill.Dodge, 80), $"critter flags {critter:X}, spell flags {spell:X}");
            Assert.AreEqual(before + 80, session.Version.Revision, "each attempt still rolls (one revision)");
        }
    }

    [TestMethod]
    public void A_spot_trap_attempt_below_expert_never_finds_a_magical_trap_and_asks_the_scene_for_the_targets_class()
    {
        // ARC-AI-2625 (0x4C7160 case 7, 0x4BBDD0): the trap class comes from the scene's answer for the named target.
        var asked = new List<string>();
        var (_, magical, magicalNpc, _) = Open(hero: true, tweak: scene => scene with { TargetTrapClass = target => { asked.Add(target); return Arcanum.Simulation.Traps.TrapClass.Magical; } });
        Assert.AreEqual(0, Successes(magical, magicalNpc, Skill.SpotTrap, 80));
        Assert.IsTrue(asked.Count == 80 && asked.All(target => target == AiSchedulerRules.Pc), "the scene is asked for each attempt's target");
        var (_, plain, plainNpc, _) = Open(hero: true, tweak: scene => scene with { TargetTrapClass = _ => Arcanum.Simulation.Traps.TrapClass.Nonmagical });
        Assert.IsTrue(Successes(plain, plainNpc, Skill.SpotTrap, 80) > 0, "a non-magical trap is found");
        var (_, none, noneNpc, _) = Open(hero: true);
        Assert.IsTrue(Successes(none, noneNpc, Skill.SpotTrap, 80) > 0, "no scene delegate: the target holds no trap and nothing is refused");
    }

    [TestMethod]
    public void A_persuasion_or_prowling_attempt_is_opposed_by_the_targets_intelligence_or_perception_and_a_target_the_host_does_not_hold_changes_nothing()
    {
        // ARC-AI-2652 (0x4C62E0 → 0x4C6410, skill flags 0x20 and 0x10): the opposed effectiveness reads the target's effective stat (0x4B0490 kind 4 or 5), 0 for a target that is no critter.
        var (session, host, npc, item) = Open(hero: true);
        foreach (var (skill, stat) in new[] { (Skill.Persuasion, 4), (Skill.Prowling, 5) })
        {
            var heroStat = session.QueryHeroScriptStat(stat) ?? throw new AssertFailedException("no hero stat");
            var opposedToHero = SkillCheckRules.Opposed(0, heroStat);
            Assert.AreNotEqual(100, opposedToHero, "the fixture hero's stat must matter");
            host.AttemptSkill(npc, AiSchedulerRules.Pc, (int)skill, null);
            var roll = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no roll");
            CollectionAssert.Contains(new[] { Math.Min(opposedToHero, SkillCheckRules.EffectivenessCap), Math.Min(opposedToHero + 10, SkillCheckRules.EffectivenessCap), opposedToHero, opposedToHero + 10 }, roll.Check!.Check.Effectiveness,
                $"{skill}: opposed by the hero's stat {stat} = {heroStat} (the NPC has no points), plus the intelligence term at most");
            // A map item is no critter: the getter reads 0.
            host.AttemptSkill(npc, item, (int)skill, null);
            var onItem = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll!;
            var opposedToNothing = SkillCheckRules.Opposed(0, 0);
            CollectionAssert.Contains(new[] { opposedToNothing, opposedToNothing + 10 }, onItem.Check!.Check.Effectiveness, $"{skill} on an item");
            // A target the host holds nothing of is refused and nothing is rolled.
            var before = (session.Version.Revision, session.QueryRandom().Draws);
            Assert.IsFalse(host.AttemptSkill(npc, Entity(9), (int)skill, null));
            Assert.IsNull(host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll);
            Assert.AreEqual($"skill {(int)skill} on {Entity(9)}: no target", host.TraceOf(npc)!.Skill);
            Assert.AreEqual(before, (session.Version.Revision, session.QueryRandom().Draws), "nothing is rolled");
        }
    }

    [TestMethod]
    public void An_npc_gambling_attempt_uses_the_stored_roll_against_the_targets_level_and_a_target_the_host_does_not_hold_changes_nothing()
    {
        // ARC-AI-2623 (0x4C7160 with skill 8 and the roll 0x4C6560): the shared check on the module's stored roll, opposed by the target's gambling level; the answer is the check's success.
        var (session, host, npc, item) = Open(hero: true);
        session.EnsureGamblingRoll(session.Version);
        var stored = session.QueryGamblingRoll()!.Value;
        var heroLevel = session.QueryHeroSkills()![Skill.Gambling].Level;
        var revision = session.Version.Revision;
        var answer = host.AttemptSkill(npc, AiSchedulerRules.Pc, (int)Skill.Gambling, null);
        var roll = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no roll");
        Assert.AreEqual((GameSessionDisposition.Changed, roll.Success), (roll.Disposition, answer), "the answer is the roll's success (run flag 0x40000)");
        Assert.AreEqual(stored, roll.Check!.Check.Roll, "the check reads the stored roll");
        Assert.AreEqual(roll.Check.NextGamblingRoll, session.QueryGamblingRoll(), "its own draw is stored for the next check");
        Assert.AreEqual(revision + 1, session.Version.Revision);
        var opposed = SkillCheckRules.Opposed(0, heroLevel);
        CollectionAssert.Contains(new[] { Math.Min(opposed, SkillCheckRules.EffectivenessCap), Math.Min(opposed + 10, SkillCheckRules.EffectivenessCap) }, roll.Check.Check.Effectiveness,
            "opposed by the hero's gambling level (the NPC has none), plus the intelligence term at most");
        StringAssert.StartsWith(host.TraceOf(npc)!.Skill, $"gambling on {AiSchedulerRules.Pc} Changed");
        // A map item is no critter: its level reads 0. A target the host holds nothing of is refused and nothing is rolled.
        host.AttemptSkill(npc, item, (int)Skill.Gambling, null);
        Assert.AreEqual(GameSessionDisposition.Changed, host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll!.Disposition);
        var before = (session.Version.Revision, session.QueryGamblingRoll());
        Assert.IsFalse(host.AttemptSkill(npc, Entity(9), (int)Skill.Gambling, null));
        Assert.IsNull(host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).PlainRoll);
        Assert.AreEqual($"gambling on {Entity(9)}: no target", host.TraceOf(npc)!.Skill);
        Assert.AreEqual(before, (session.Version.Revision, session.QueryGamblingRoll()), "nothing is rolled");
    }

    [TestMethod]
    public void An_npcs_repair_of_a_held_damaged_item_goes_through_the_session_and_writes_the_item_record()
    {
        // ARC-AI-2622 (0x4C7160, repair case 12): the NPC (repair 20 points, master) repairs the item it holds: the damage falls, hp_adj does not (master), one revision, the run's answer is the forced success.
        var (session, host, npc, item) = Open(repair: 20 | (long)SkillTraining.Master << 6, itemPoints: 100, itemDamage: 60);
        var before = session.Version.Revision;
        var answer = host.AttemptSkill(npc, item, (int)Skill.Repair, null);
        var notice = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill);
        var repair = notice.Repair ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no repair");
        Assert.IsTrue(answer, host.TraceOf(npc)?.Skill);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (repair.Disposition, repair.Success));
        Assert.AreEqual(before + 1, session.Version.Revision, "the repair is one revision");
        Assert.AreEqual(Math.Max(0, 60 - repair.Amount), repair.Condition!.Value.Damage, "the damage falls by the amount");
        Assert.AreEqual(100, repair.Condition!.Value.Points + repair.Condition.Value.Adjustment, "a master takes nothing from hp_adj");
        StringAssert.StartsWith(host.TraceOf(npc)!.Skill, $"repair {item} Changed");
    }

    // A hero session whose kit holds one weapon (kit:1), damaged by 60 of 100 points over the baseline the hero load would keep.
    private static (GameSessionOwner Session, ItemCondition Baseline) HeroWithDamagedKitItem()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Weapon, 4027, 6000, 6001))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var baseline = new ItemCondition(100, 20, 0, false);
        Assert.AreEqual(GameSessionDisposition.Changed, session.DamageItem(session.Version, "kit:1", baseline, 60).Disposition);
        return (session, baseline);
    }

    [TestMethod]
    public void An_npcs_repair_of_a_kit_item_of_the_hero_reads_the_baseline_the_viewer_supplies()
    {
        // ARC-AI-2673 (DSV5): a kit item's durability baseline lives in the hero load, so the host asks the viewer's provider; without it the item is no repair target.
        var (bare, _) = HeroWithDamagedKitItem();
        var refused = Open(with: bare, repair: 20 | (long)SkillTraining.Master << 6);
        Assert.IsFalse(refused.Host.AttemptSkill(refused.Npc, "kit:1", (int)Skill.Repair, null));
        StringAssert.EndsWith(refused.Host.TraceOf(refused.Npc)!.Skill, "no item to repair");
        var (session, baseline) = HeroWithDamagedKitItem();
        var (_, host, npc, _) = Open(with: session, repair: 20 | (long)SkillTraining.Master << 6, tweak: scene => scene with { HeroItemCondition = entity => entity == "kit:1" ? baseline : null });
        var before = session.Version.Revision;
        var damage = session.QueryItemCondition("kit:1", baseline)!.Value.Damage;
        Assert.IsTrue(host.AttemptSkill(npc, "kit:1", (int)Skill.Repair, null), host.TraceOf(npc)?.Skill);
        var repair = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).Repair!;
        Assert.AreEqual((GameSessionDisposition.Changed, true), (repair.Disposition, repair.Success));
        Assert.AreEqual(before + 1, session.Version.Revision, "one revision");
        Assert.AreEqual(Math.Max(0, damage - repair.Amount), session.QueryItemCondition("kit:1", baseline)!.Value.Damage, "the session's record of the kit item fell");
        Assert.IsFalse(host.AttemptSkill(npc, "kit:9", (int)Skill.Repair, null), "an item the provider does not know is no target");
        StringAssert.EndsWith(host.TraceOf(npc)!.Skill, "no item to repair");
    }

    [TestMethod]
    [DataRow(Place.Ground)]
    [DataRow(Place.MapPile)]
    [DataRow(Place.OtherNpc)]
    public void An_npcs_repair_reaches_an_item_loose_in_a_junk_pile_or_in_another_npcs_list(Place place)
    {
        // ARC-AI-2663 (0x4C7160 repair case 12): the case works on the target handle's own fields and asks nothing of the item's holder, so the item's place changes nothing.
        var (session, host, npc, item) = Open(repair: 20 | (long)SkillTraining.Master << 6, itemPoints: 100, itemDamage: 60, place: place);
        var before = session.Version.Revision;
        var answer = host.AttemptSkill(npc, item, (int)Skill.Repair, null);
        var repair = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Skill).Repair ?? throw new AssertFailedException(host.TraceOf(npc)?.Skill ?? "no repair");
        Assert.IsTrue(answer, host.TraceOf(npc)?.Skill);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (repair.Disposition, repair.Success));
        Assert.AreEqual(before + 1, session.Version.Revision, "the repair is one revision");
        Assert.AreEqual(Math.Max(0, 60 - repair.Amount), repair.Condition!.Value.Damage, "the damage falls by the amount");
        Assert.AreEqual(Math.Max(0, 60 - repair.Amount), session.QueryItemCondition(item, new(100, 0, 60, false))!.Value.Damage, "the session keeps the item's record");
    }

    [TestMethod]
    public void An_npcs_repair_names_no_critter_even_when_it_holds_the_item()
    {
        var (session, host, npc, _) = Open(repair: 20, itemPoints: 100, itemDamage: 60, place: Place.OtherNpc);
        var before = session.Version;
        Assert.IsFalse(host.AttemptSkill(npc, Entity(2), (int)Skill.Repair, null), "another NPC is no item");
        StringAssert.EndsWith(host.TraceOf(npc)!.Skill, "no item to repair");
        Assert.AreEqual(before, session.Version, "nothing is rolled");
    }

    [TestMethod]
    public void An_npcs_repair_with_no_item_to_repair_is_refused_and_changes_nothing()
    {
        var (session, host, npc, item) = Open(hero: true, repair: 20, itemPoints: 100, itemDamage: 60);
        var before = session.Version;
        Assert.IsFalse(host.AttemptSkill(npc, AiSchedulerRules.Pc, (int)Skill.Repair, null), "a critter is no item here (the original has no target test)");
        Assert.IsFalse(host.AttemptSkill(npc, Entity(9), (int)Skill.Repair, null), "a target the host does not know");
        StringAssert.EndsWith(host.TraceOf(npc)!.Skill, "no item to repair");
        Assert.AreEqual(before, session.Version, "nothing is rolled");
        Assert.IsTrue(host.DrainNotices().All(n => n.Kind == AreaAiNoticeKind.Skill && n.Repair is null));
        _ = item;
    }

    [TestMethod]
    public void A_speaking_idle_npc_faces_its_listener_and_a_busy_or_unable_one_does_not()
    {
        // ARC-UI-2029 (TB26): the speech display turns a living speaker that runs no animation of its own to its listener (the dialog's NPC line, the greeting).
        var (_, host, npc, _) = Open();
        Assert.IsTrue(host.FaceListener(npc, AiSchedulerRules.Pc), "an idle living NPC turns to the hero");
        Assert.AreEqual(NeighborDirection.XPlus, host.Movers.FacingOf(npc, NeighborDirection.YMinus));
        Assert.IsFalse(host.FaceListener(npc, Entity(9)), "a listener without a cell: nothing to face");
        Assert.IsFalse(host.FaceListener(AiSchedulerRules.Pc, npc), "the host walks no hero: the hero's turn is the request host's");
        var (_, stunnedHost, stunned, _) = Open(critterFlags: 0x60);
        Assert.IsFalse(stunnedHost.FaceListener(stunned, AiSchedulerRules.Pc), "a critter that cannot act is not turned");
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
