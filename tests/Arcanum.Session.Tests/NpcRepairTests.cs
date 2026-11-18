using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-2622 (exe 0x4C7160, repair case 12 of an NPC actor): the item side follows the exe read. The NPC "virgil" and the item "item:1" are invented.
[TestClass]
public sealed class NpcRepairTests
{
    private const string Npc = "virgil";
    private const string Item = "item:1";

    private static SessionNpcSkillUser User(int points, SkillTraining training, string entity = Npc)
    {
        var tech = new int[4];
        tech[SkillRules.FamilyIndex(Skill.Repair)] = points | (int)training << 6;
        var sheet = new SessionNpcAdvancement(entity, new(CharacterRace.Human, false, [20, 20, 20, 20, 20, 20, 20, 20], 0, 0, 1, 0,
            [.. new int[12]], [.. tech], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0);
        return new(SessionLifeTarget.Critter(entity, new LifeFacts { HitPointPoints = 10 }, new LifeState(0, 0, 0, LifeConditions.None, 0)), sheet, AdvancementModifiers.None, false);
    }

    private static GameSessionOwner Owner(ulong seed = 0)
    {
        var created = GameSessionOwner.Create(Descriptor() with { RandomSeed = seed, Hero = HalfElf(Defaults()) });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    private static readonly ItemCondition Worn = new(100, 20, 60, false);

    [TestMethod]
    public void A_damaged_item_is_repaired_in_one_revision_and_the_hero_is_untouched()
    {
        using var owner = Owner(3);
        var (before, draws, hero) = (owner.Version.Revision, owner.QueryRandom().Draws, owner.QueryHero());
        var result = owner.RepairByNpc(owner.Version, User(20, SkillTraining.Expert), Item, Worn);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, ItemRepairStatus.Repaired), (result.Disposition, result.Failure, result.Repair));
        Assert.IsTrue(result.Success, "the case forces the success");
        var effectiveness = result.Check!.Check.Effectiveness;
        var amount = (int)(120L * effectiveness / 100);
        Assert.AreEqual(amount > 0 ? amount : effectiveness > 0 ? 1 : 0, result.Amount);
        // Expert: 1 % of the maximum 120 (truncated) leaves hp_adj; the damage falls by the amount.
        Assert.AreEqual(new ItemCondition(100, 20 - 120 / 100, Math.Max(0, 60 - result.Amount), false), result.Condition);
        Assert.AreEqual(result.Condition, owner.QueryItemCondition(Item, Worn));
        Assert.AreEqual((before + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws));
        Assert.AreEqual(hero, owner.QueryHero(), "no fate and no hero write");
    }

    [TestMethod]
    public void The_training_decides_the_share_taken_from_hp_adj()
    {
        var expected = new Dictionary<SkillTraining, int> { [SkillTraining.None] = 10, [SkillTraining.Apprentice] = 5, [SkillTraining.Expert] = 1, [SkillTraining.Master] = 0 };
        foreach (var (training, percent) in expected)
        {
            using var owner = Owner(1);
            var result = owner.RepairByNpc(owner.Version, User(20, training), Item, new(100, 100, 10, false));
            Assert.AreEqual(100 - 200 * percent / 100, result.Condition!.Value.Adjustment, training.ToString());
        }
    }

    [TestMethod]
    public void An_undamaged_item_is_not_touched_and_the_attempt_still_succeeds()
    {
        using var owner = Owner(2);
        var result = owner.RepairByNpc(owner.Version, User(20, SkillTraining.None), Item, new(100, 20, 0, false));
        Assert.AreEqual(ItemRepairStatus.NothingToRepair, result.Repair);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Amount);
        Assert.AreEqual(new ItemCondition(100, 20, 0, false), result.Condition);
        Assert.AreEqual(0, owner.QueryDurability().Items.Count, "nothing is recorded");
    }

    [TestMethod]
    public void A_master_clears_the_broken_art_of_an_item_even_without_damage_and_takes_5_percent()
    {
        using var owner = Owner(2);
        var result = owner.RepairByNpc(owner.Version, User(20, SkillTraining.Master), Item, new(100, 20, 0, true, true));
        Assert.AreEqual(ItemRepairStatus.Repaired, result.Repair);
        Assert.IsTrue(result.Success, "the master's clear forces the success");
        Assert.AreEqual(new ItemCondition(100, 14, 0, false, true), result.Condition, "the current art loses its bit, the inventory art keeps it; 5 % of 120");
    }

    [TestMethod]
    public void A_broken_item_stays_broken_for_an_apprentice_and_an_undamaged_one_is_not_touched()
    {
        using var owner = Owner(2);
        Assert.AreEqual(ItemRepairStatus.NothingToRepair, owner.RepairByNpc(owner.Version, User(20, SkillTraining.Apprentice), Item, new(100, 20, 0, true, true)).Repair);
        var damaged = owner.RepairByNpc(owner.Version, User(20, SkillTraining.Apprentice), "item:2", new(100, 20, 30, true, true));
        Assert.IsTrue(damaged.Condition!.Value.Broken, "only a master clears the destroyed bit");
    }

    [TestMethod]
    public void No_skill_at_all_restores_nothing_and_fails_the_attempt()
    {
        using var owner = Owner(4);
        // Effectiveness 0 (level 0, no training, ordinary intelligence): the amount is 0, the success is cleared, the share is still taken.
        var user = User(0, SkillTraining.None);
        var sheet = user.Loaded with { Sheet = user.Loaded.Sheet with { Stats = [10, 10, 10, 10, 10, 10, 10, 10] } };
        var result = owner.RepairByNpc(owner.Version, user with { Loaded = sheet }, Item, Worn);
        Assert.AreEqual(0, result.Check!.Check.Effectiveness);
        Assert.AreEqual(0, result.Amount);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(60, result.Condition!.Value.Damage);
        Assert.AreEqual(20 - 120 * 10 / 100, result.Condition!.Value.Adjustment);
    }

    [TestMethod]
    public void Refusals_change_nothing()
    {
        using var owner = Owner(1);
        var (revision, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RepairByNpc(owner.Version with { Revision = revision + 5 }, User(20, SkillTraining.None), Item, Worn).Failure);
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, owner.RepairByNpc(owner.Version, User(20, SkillTraining.None) with { User = SessionLifeTarget.Hero }, Item, Worn).Failure, "the hero is not an NPC");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.RepairByNpc(owner.Version, User(20, SkillTraining.None), Item, new(1_000_000, 0, 0, false)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.RepairByNpc(owner.Version, User(20, SkillTraining.None), "", Worn).Failure);
        Assert.AreEqual((revision, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
    }
}
