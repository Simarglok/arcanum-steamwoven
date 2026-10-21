using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero repairs its items with the Repair skill (ARC-SKILLS-007, ledger RP1–RP5). The item and its values are invented: a
// maximum of 50 (40 points + 10 adjustment).
[TestClass]
public sealed class RepairStateTests
{
    private const string Sword = "kit:4";
    private static readonly ItemCondition Damaged = new(40, 10, 30, false);

    // Intelligence 20 (repair's key stat), repair points and training as asked; the kit's fourth item is the damaged sword.
    private static GameSessionOwner Repairer(int points, SkillTraining training)
    {
        var stats = Defaults(); stats[4] = 20;
        int[] tech = new int[4];
        tech[0] = points | (int)training << 6;
        var hero = HalfElf(stats) with
        {
            Kit = [.. Kit, new(new(SessionItemType.Weapon, 4027, 6000, 6001))],
            Advancement = SessionHeroAdvancement.None with { TechSkills = [.. tech] },
        };
        return CreateWith(hero);
    }

    [TestMethod]
    public void TheRollChangesNothingAndTheRepairRestoresItsShareInOneRevision()
    {
        using var owner = Repairer(3, SkillTraining.Apprentice);
        var (before, draws) = (owner.Version.Revision, owner.QueryRandom().Draws);
        var result = owner.RepairWithSkill(owner.Version, Sword, Damaged);
        Assert.AreEqual((GameSessionDisposition.Changed, ItemRepairStatus.Repaired), (result.Disposition, result.Repair));
        var effectiveness = result.Check!.Check.Effectiveness;
        var amount = Math.Max(effectiveness > 0 ? 1 : 0, 50 * effectiveness / 100);
        Assert.AreEqual(amount, result.Amount, "maximum × effectiveness / 100, whatever the roll");
        // An apprentice takes 5 % of the maximum (50) from the adjustment: 10 − 2 = 8.
        Assert.AreEqual(new ItemCondition(40, 8, Math.Max(0, 30 - amount), false), result.Condition);
        Assert.AreEqual(result.Condition, owner.QueryItemCondition(Sword, Damaged));
        Assert.AreEqual((before + 1, draws + 2), (owner.Version.Revision, owner.QueryRandom().Draws));
    }

    [TestMethod]
    public void AnUndamagedItemIsNotTouchedButTheDrawsAndAnArmedFateAreSpent()
    {
        using var owner = Repairer(3, SkillTraining.Apprentice);
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.Repair);
        var sound = Damaged with { Damage = 0 };
        var result = owner.RepairWithSkill(owner.Version, Sword, sound);
        Assert.AreEqual((GameSessionDisposition.Changed, ItemRepairStatus.NothingToRepair, 0, true), (result.Disposition, result.Repair, result.Amount, result.FateUsed));
        Assert.AreEqual(0, owner.QueryDurability().Items.Count, "no record");
        Assert.IsFalse(owner.HeroFateArmed(FateKind.Repair), "the fate is used even with nothing to repair");
    }

    [TestMethod]
    public void AFatedRepairRestoresTheWholeMaximumAndAMasterMendsABrokenItem()
    {
        using var owner = Repairer(0, SkillTraining.None);
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.Repair);
        var full = owner.RepairWithSkill(owner.Version, Sword, Damaged);
        Assert.AreEqual((50, true), (full.Amount, full.FateUsed));
        // Untrained: 10 % of 50 from the adjustment.
        Assert.AreEqual(new ItemCondition(40, 5, 0, false), full.Condition);

        using var master = Repairer(5, SkillTraining.Master);
        var broken = new ItemCondition(40, 10, 0, true, true);
        var mended = master.RepairWithSkill(master.Version, Sword, broken);
        Assert.AreEqual(ItemRepairStatus.Repaired, mended.Repair);
        Assert.AreEqual(new ItemCondition(40, 8, 0, false, true), mended.Condition, "a master's 5 % for a broken item; the current art only");
    }

    [TestMethod]
    public void RefusalsComeBeforeAnyRoll()
    {
        using var owner = Repairer(3, SkillTraining.Apprentice);
        var draws = owner.QueryRandom().Draws;
        void Refused(SessionRepairResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
        }
        Refused(owner.RepairWithSkill(owner.Version, "kit:9", Damaged), GameSessionFailure.ItemNotAvailable);
        Refused(owner.RepairWithSkill(owner.Version, Sword, Damaged with { Damage = -1 }), GameSessionFailure.InvalidItem);
        Refused(owner.RepairWithSkill(new(owner.Identity, 5), Sword, Damaged), GameSessionFailure.StaleVersion);
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.RepairUnavailable, heroless.RepairWithSkill(heroless.Version, Sword, Damaged).Failure);
        using var dead = Repairer(3, SkillTraining.Apprentice);
        dead.Kill(dead.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(GameSessionFailure.RepairUnavailable, dead.RepairWithSkill(dead.Version, Sword, Damaged).Failure);
    }
}
