using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1854 (exe 0x4C7160, the goal-33 attempt of a skill with no case of its own): an NPC's plain skill roll. The NPC "virgil" is invented.
[TestClass]
public sealed class NpcSkillRollTests
{
    private const string Npc = "virgil";

    // The NPC's sheet: every primary stat 20, the named basic skill with its points and training.
    private static SessionNpcSkillUser User(Skill skill, int points, SkillTraining training, bool led = false, string entity = Npc)
    {
        var basic = new int[12];
        var tech = new int[4];
        if (SkillRules.IsTech(skill)) tech[SkillRules.FamilyIndex(skill)] = points | (int)training << 6;
        else basic[(int)skill] = points | (int)training << 6;
        var sheet = new SessionNpcAdvancement(entity, new(CharacterRace.Human, false, [20, 20, 20, 20, 20, 20, 20, 20], 0, 0, 1, 0,
            [.. basic], [.. tech], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0);
        return new(SessionLifeTarget.Critter(entity, new LifeFacts { HitPointPoints = 10 }, new LifeState(0, 0, 0, LifeConditions.None, 0)), sheet, AdvancementModifiers.None, led);
    }

    private static GameSessionOwner Owner(ulong seed = 0)
    {
        var created = GameSessionOwner.Create(Descriptor() with { RandomSeed = seed, Hero = HalfElf(Defaults()) });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    [TestMethod]
    public void A_plain_skill_rolls_the_shared_check_in_one_revision_and_answers_its_success()
    {
        using var owner = Owner(3);
        var (before, draws, hero) = (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryHero());
        var result = owner.RollNpcSkill(owner.Version, User(Skill.Melee, 20, SkillTraining.Master), Skill.Melee);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.IsNotNull(result.Check);
        Assert.AreEqual(result.Check.Success, result.Success, "a skill with no case of its own answers the check's success");
        Assert.IsFalse(result.SecondRoll);
        Assert.AreEqual((before + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws));
        Assert.AreEqual(hero, owner.QueryHero(), "the hero is untouched (no fate, no condition)");
    }

    [TestMethod]
    public void Persuasion_and_prowling_are_opposed_by_the_targets_stat_and_no_target_reads_a_hundred()
    {
        // ARC-AI-2652 (0x4C6410): the effectiveness is the opposed table over the level difference; a skill with no opposed stat ignores the value.
        foreach (var skill in new[] { Skill.Persuasion, Skill.Prowling })
        {
            using var owner = Owner(5);
            var user = User(skill, 10, SkillTraining.Master);
            var level = owner.QueryNpcSkills(user.Loaded, user.Modifiers)[skill].Level;
            var none = owner.RollNpcSkill(owner.Version, user, skill);
            var weak = owner.RollNpcSkill(owner.Version, user, skill, opponentStat: 0);
            var strong = owner.RollNpcSkill(owner.Version, user, skill, opponentStat: level + 9);
            Assert.IsTrue(none.Check!.Check.Effectiveness >= SkillCheckRules.EffectivenessCap, $"{skill}: no target reads 100 (plus the intelligence term, capped at 95 for persuasion)");
            Assert.IsTrue(none.Check.Check.Effectiveness > strong.Check!.Check.Effectiveness, $"{skill}: a strong opponent lowers the chance");
            Assert.IsTrue(weak.Check!.Check.Effectiveness >= strong.Check.Check.Effectiveness);
            var expected = SkillCheckRules.Opposed(level, level + 9) + 10;
            Assert.AreEqual(expected, strong.Check.Check.Effectiveness, $"{skill}: the table over the difference of {level} and {level + 9}, plus the +10 of the fixture sheet's extraordinary intelligence (every stat 20)");
        }
        using var plain = Owner(5);
        var melee = User(Skill.Melee, 20, SkillTraining.Master);
        Assert.AreEqual(plain.RollNpcSkill(plain.Version, melee, Skill.Melee).Check!.Check.Effectiveness, plain.RollNpcSkill(plain.Version, melee, Skill.Melee, opponentStat: 20).Check!.Check.Effectiveness,
            "melee is no opposed skill");
    }

    [TestMethod]
    public void An_opponent_stat_outside_the_limit_is_refused_and_rolls_nothing()
    {
        using var owner = Owner(5);
        var edge = owner.RollNpcSkill(owner.Version, User(Skill.Persuasion, 10, SkillTraining.Master), Skill.Persuasion, opponentStat: GameSessionOwner.MaxPickPocketStat);
        Assert.AreEqual(GameSessionDisposition.Changed, edge.Disposition, "the limit itself is accepted");
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        foreach (var bad in new[] { GameSessionOwner.MaxPickPocketStat + 1, -GameSessionOwner.MaxPickPocketStat - 1 })
        {
            var result = owner.RollNpcSkill(owner.Version, User(Skill.Persuasion, 10, SkillTraining.Master), Skill.Persuasion, opponentStat: bad);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidSkill), (result.Disposition, result.Failure));
        }
        Assert.AreEqual((revision, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
    }

    [TestMethod]
    public void A_highly_skilled_npc_always_succeeds_and_an_unskilled_one_mostly_fails()
    {
        var strong = 0;
        var weak = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            using var owner = Owner(seed);
            if (owner.RollNpcSkill(owner.Version, User(Skill.Bow, 20, SkillTraining.Master), Skill.Bow).Success) strong++;
            if (owner.RollNpcSkill(owner.Version, User(Skill.Bow, 0, SkillTraining.None), Skill.Bow).Success) weak++;
        }
        Assert.IsTrue(strong > weak, $"strong {strong}, weak {weak}");
        Assert.IsTrue(strong >= 30, $"strong {strong}");
    }

    [TestMethod]
    public void A_dodge_of_an_incapable_actor_never_succeeds_where_a_capable_one_does_with_the_same_draws()
    {
        var found = false;
        for (ulong seed = 0; seed < 40 && !found; seed++)
        {
            using var capable = Owner(seed);
            var ok = capable.RollNpcSkill(capable.Version, User(Skill.Dodge, 20, SkillTraining.Master), Skill.Dodge);
            if (!ok.Success) continue;
            using var incapable = Owner(seed);
            var refused = incapable.RollNpcSkill(incapable.Version, User(Skill.Dodge, 20, SkillTraining.Master), Skill.Dodge, incapable: true);
            Assert.IsTrue(refused.Check!.Success, "the check itself still rolled the same");
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(ok.Check!.Check.Roll, refused.Check.Check.Roll);
            found = true;
        }
        Assert.IsTrue(found);
    }

    [TestMethod]
    public void A_forced_failure_of_the_dodge_or_of_a_magical_spot_clears_the_critical_bit_the_check_kept()
    {
        // ARC-AI-2625 (exe 0x4C73FB): the forced failure clears the critical bit; the check's own roll is kept in Check.
        var dodge = false;
        var spot = false;
        for (ulong seed = 0; seed < 600 && !(dodge && spot); seed++)
        {
            using var a = Owner(seed);
            var gated = a.RollNpcSkill(a.Version, User(Skill.Dodge, 20, SkillTraining.Master), Skill.Dodge, incapable: true);
            if (gated.Check!.Critical)
            {
                Assert.IsFalse(gated.Critical);
                Assert.IsFalse(gated.Success);
                using var free = Owner(seed);
                Assert.IsTrue(free.RollNpcSkill(free.Version, User(Skill.Dodge, 20, SkillTraining.Master), Skill.Dodge).Critical, "a free actor keeps the critical");
                dodge = true;
            }
            using var b = Owner(seed);
            var magical = b.RollNpcSkill(b.Version, User(Skill.SpotTrap, 20, SkillTraining.Apprentice), Skill.SpotTrap, trap: TrapClass.Magical);
            if (magical.Check!.Success && magical.Check.Critical)
            {
                Assert.IsFalse(magical.Critical);
                Assert.IsFalse(magical.Success);
                using var plain = Owner(seed);
                Assert.IsTrue(plain.RollNpcSkill(plain.Version, User(Skill.SpotTrap, 20, SkillTraining.Apprentice), Skill.SpotTrap, trap: TrapClass.None).Critical);
                spot = true;
            }
        }
        Assert.IsTrue(dodge && spot, $"dodge {dodge}, spot {spot}");
    }

    [TestMethod]
    public void The_incapacity_gate_is_the_dodges_alone()
    {
        using var owner = Owner(1);
        var result = owner.RollNpcSkill(owner.Version, User(Skill.Melee, 20, SkillTraining.Master), Skill.Melee, incapable: true);
        Assert.AreEqual(result.Check!.Success, result.Success);
    }

    [TestMethod]
    public void The_masters_second_spot_trap_roll_is_drawn_only_after_a_failure_that_is_no_critical()
    {
        var second = false;
        for (ulong seed = 0; seed < 200; seed++)
        {
            using var owner = Owner(seed);
            var draws = owner.QueryRandom().Draws;
            var master = owner.RollNpcSkill(owner.Version, User(Skill.SpotTrap, 5, SkillTraining.Master), Skill.SpotTrap);
            var expected = master.SecondRoll ? 3 : 2;
            Assert.AreEqual(draws + expected, owner.QueryRandom().Draws);
            if (master.SecondRoll) { second = true; Assert.IsFalse(master.Check!.Success); Assert.IsFalse(master.Check.Critical); }
            using var apprentice = Owner(seed);
            Assert.IsFalse(apprentice.RollNpcSkill(apprentice.Version, User(Skill.SpotTrap, 5, SkillTraining.Apprentice), Skill.SpotTrap).SecondRoll);
        }
        Assert.IsTrue(second, "some failed first roll of a master draws the second");
    }

    [TestMethod]
    public void A_magical_trap_is_never_spotted_below_expert_whatever_the_roll()
    {
        for (ulong seed = 0; seed < 30; seed++)
        {
            using var owner = Owner(seed);
            var result = owner.RollNpcSkill(owner.Version, User(Skill.SpotTrap, 20, SkillTraining.Apprentice), Skill.SpotTrap, trap: TrapClass.Magical);
            Assert.IsFalse(result.Success);
        }
    }

    [TestMethod]
    public void Skills_with_their_own_handler_and_gambling_are_refused_before_anything_is_rolled()
    {
        using var owner = Owner(2);
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        foreach (var skill in new[] { Skill.Gambling, Skill.Heal, Skill.PickPocket, Skill.Repair, Skill.PickLocks })
        {
            Assert.IsFalse(GameSessionOwner.IsPlainNpcSkill(skill));
            var result = owner.RollNpcSkill(owner.Version, User(skill, 20, SkillTraining.Master), skill);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.SkillsUnavailable), (result.Disposition, result.Failure), skill.ToString());
        }
        Assert.AreEqual((revision, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
    }

    [TestMethod]
    public void A_hero_user_a_foreign_sheet_and_a_stale_version_are_refused()
    {
        using var owner = Owner(2);
        var user = User(Skill.Melee, 20, SkillTraining.Master);
        var hero = user with { User = SessionLifeTarget.Hero };
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, owner.RollNpcSkill(owner.Version, hero, Skill.Melee).Failure);
        var foreign = user with { Loaded = User(Skill.Melee, 20, SkillTraining.Master, entity: "other").Loaded };
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, owner.RollNpcSkill(owner.Version, foreign, Skill.Melee).Failure);
        var stale = owner.Version;
        owner.RollNpcSkill(owner.Version, user, Skill.Melee);
        var late = owner.RollNpcSkill(stale, user, Skill.Melee);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (late.Disposition, late.Failure));
    }

    // ARC-AI-2623 (0x4C7160 with skill 8, 0x4C6560): the gambling attempt uses the stored roll of the skill module and stores its own draw.
    [TestMethod]
    public void A_gambling_attempt_uses_the_stored_roll_and_stores_the_checks_own_draw_in_one_revision()
    {
        using var owner = Owner(7);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EnsureGamblingRoll(owner.Version).Disposition);
        var stored = owner.QueryGamblingRoll()!.Value;
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var result = owner.RollNpcGambling(owner.Version, User(Skill.Gambling, 20, SkillTraining.Master), 0);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(stored, result.Check!.Check.Roll, "the check reads the roll stored before");
        Assert.AreEqual(result.Check.NextGamblingRoll, owner.QueryGamblingRoll(), "its own draw is stored for the next check");
        Assert.AreEqual(result.Check.Success, result.Success);
        Assert.AreEqual((revision + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws), "the check roll and the critical roll");
    }

    [TestMethod]
    public void A_gambling_attempt_chains_on_the_roll_the_last_check_stored()
    {
        using var owner = Owner(11);
        owner.EnsureGamblingRoll(owner.Version);
        var first = owner.RollNpcGambling(owner.Version, User(Skill.Gambling, 10, SkillTraining.Expert), 5);
        var second = owner.RollNpcGambling(owner.Version, User(Skill.Gambling, 10, SkillTraining.Expert), 5);
        Assert.AreEqual(first.Check!.NextGamblingRoll, second.Check!.Check.Roll, "the module keeps one stored roll, whoever checked");
        Assert.AreEqual(second.Check.NextGamblingRoll, owner.QueryGamblingRoll());
    }

    [TestMethod]
    public void A_gambling_attempt_of_a_session_with_no_stored_roll_throws_it_first_in_the_same_revision()
    {
        using var owner = Owner(5);
        Assert.IsNull(owner.QueryGamblingRoll());
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var result = owner.RollNpcGambling(owner.Version, User(Skill.Gambling, 10, SkillTraining.Expert), 5);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual((revision + 1, draws + 3), (owner.Version.Revision, owner.QueryRandom().Draws), "the first roll, the check's draw and the critical roll");
        Assert.AreEqual(result.Check!.NextGamblingRoll, owner.QueryGamblingRoll());
    }

    [TestMethod]
    public void The_gambling_effectiveness_is_opposed_by_the_targets_level_and_a_missing_target_is_a_hundred()
    {
        var user = User(Skill.Gambling, 10, SkillTraining.Expert);
        int Effectiveness(int? opponent)
        {
            using var owner = Owner(3);
            return owner.RollNpcGambling(owner.Version, user, opponent).Check!.Check.Effectiveness;
        }
        using var reader = Owner(3);
        var level = reader.QueryNpcSkills(user.Loaded, user.Modifiers)[Skill.Gambling].Level;
        var bonus = Effectiveness(level) - SkillCheckRules.Opposed(level, level);
        Assert.IsTrue(bonus is 0 or 10, "the extraordinary-intelligence term is 0 or 10");
        foreach (var opponent in new[] { 0, level, 20 })
            Assert.AreEqual(Math.Min(SkillCheckRules.Opposed(level, opponent) + bonus, SkillCheckRules.EffectivenessCap), Effectiveness(opponent), $"opponent {opponent}");
        Assert.AreEqual(Math.Min(100 + bonus, SkillCheckRules.EffectivenessCap), Effectiveness(null), "no target: 100");
        Assert.IsTrue(Effectiveness(0) >= Effectiveness(level) && Effectiveness(level) >= Effectiveness(20));
    }

    [TestMethod]
    public void A_gambling_attempt_refuses_a_bad_opponent_a_hero_a_foreign_sheet_and_a_stale_version_before_anything_is_rolled()
    {
        using var owner = Owner(2);
        owner.EnsureGamblingRoll(owner.Version);
        var (revision, draws, roll) = (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryGamblingRoll());
        var user = User(Skill.Gambling, 10, SkillTraining.Expert);
        Assert.AreEqual(GameSessionFailure.InvalidSkill, owner.RollNpcGambling(owner.Version, user, -1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSkill, owner.RollNpcGambling(owner.Version, user, SkillRules.MaxLevel + 1).Failure);
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, owner.RollNpcGambling(owner.Version, user with { User = SessionLifeTarget.Hero }, 0).Failure);
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, owner.RollNpcGambling(owner.Version, user with { Loaded = User(Skill.Gambling, 10, SkillTraining.Expert, entity: "other").Loaded }, 0).Failure);
        Assert.AreEqual((revision, draws, roll), (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryGamblingRoll()), "a refusal writes nothing");
        var stale = owner.Version;
        Assert.AreEqual(GameSessionFailure.None, owner.RollNpcGambling(owner.Version, user, 0).Failure);
        var (afterRevision, afterDraws, afterRoll) = (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryGamblingRoll());
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RollNpcGambling(stale, user, 0).Failure);
        Assert.AreEqual((afterRevision, afterDraws, afterRoll), (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryGamblingRoll()), "a stale attempt writes nothing");
        Assert.AreEqual((revision + 1, draws + 2), (afterRevision, afterDraws), "only the one accepted attempt rolled");
    }
}
