using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// The heal skill's handler (ARC-SKILLS-005, ledger HL1–HL4), as the retail 1.0.7.4 executable runs it.
[TestClass]
public sealed class HealRulesTests
{
    private sealed class Rolls(params int[] values) : IHealRolls
    {
        private int _next;
        public List<int> Asked { get; } = [];

        public int Amount(int maximum)
        {
            Asked.Add(maximum);
            return values[_next++];
        }
    }

    [TestMethod]
    public void TheMaximumIsTheLevelOrOneAndAHalfTimesItWhenTrained()
    {
        Assert.AreEqual(1, HealRules.MaximumAmount(0, SkillTraining.None));
        Assert.AreEqual(1, HealRules.MaximumAmount(0, SkillTraining.Master));
        Assert.AreEqual(7, HealRules.MaximumAmount(7, SkillTraining.None));
        Assert.AreEqual(10, HealRules.MaximumAmount(7, SkillTraining.Apprentice), "3 × 7 / 2 toward zero");
        Assert.AreEqual(30, HealRules.MaximumAmount(20, SkillTraining.Expert));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealRules.MaximumAmount(21, SkillTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealRules.MaximumAmount(5, (SkillTraining)4));
    }

    [TestMethod]
    public void ASuccessHealsARollAndAMasterOrACriticalTheMaximumWithACure()
    {
        var rolls = new Rolls(4);
        Assert.AreEqual(new HealOutcome(4, false, 1, false), HealRules.Resolve(true, false, 8, SkillTraining.None, rolls));
        CollectionAssert.AreEqual(new[] { 8 }, rolls.Asked);
        var none = new Rolls();
        Assert.AreEqual(new HealOutcome(12, true, 1, false), HealRules.Resolve(true, false, 8, SkillTraining.Master, none));
        Assert.AreEqual(new HealOutcome(8, true, 1, true), HealRules.Resolve(true, true, 8, SkillTraining.None, none));
        Assert.AreEqual(0, none.Asked.Count, "the full amount draws nothing");
        Assert.ThrowsExactly<InvalidOperationException>(() => HealRules.Resolve(true, false, 8, SkillTraining.None, new Rolls(9)));
    }

    [TestMethod]
    public void AFailureHealsNothingAndACriticalOneCostsFiveUsesBelowExpert()
    {
        var none = new Rolls();
        Assert.AreEqual(new HealOutcome(0, false, 1, false), HealRules.Resolve(false, false, 8, SkillTraining.Master, none));
        Assert.AreEqual(new HealOutcome(0, false, 5, true), HealRules.Resolve(false, true, 8, SkillTraining.Apprentice, none));
        Assert.AreEqual(new HealOutcome(0, false, 1, false), HealRules.Resolve(false, true, 8, SkillTraining.Expert, none), "an expert's critical failure is plain");
        Assert.AreEqual(0, none.Asked.Count);
    }

    [TestMethod]
    public void TheCureRemovesOneScarAndTheFirstInjuryThenTheHitPointsHeal()
    {
        var facts = new LifeFacts();
        var hurt = new LifeState(9, 3, 50, LifeConditions.CrippledArmOne | LifeConditions.CrippledLegsBoth | LifeConditions.Blinded, 2);
        var cured = LifeRules.SkillHeal(hurt, facts, 4, cure: true);
        Assert.AreEqual(new LifeState(5, 3, 50, LifeConditions.CrippledArmOne | LifeConditions.CrippledLegsBoth, 1), cured.After);
        var arms = LifeRules.SkillHeal(hurt with { Conditions = LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth }, facts, 0, cure: true);
        Assert.AreEqual(LifeConditions.CrippledArmOne, arms.After.Conditions, "both arms come before one arm");
        Assert.AreEqual(new LifeState(0, 3, 50, hurt.Conditions, 2), LifeRules.SkillHeal(hurt, facts, 40, cure: false).After, "never below 0");
        // A dead critter: the cure still applies, the hit points do not.
        var dead = LifeRules.Kill(hurt, facts).After;
        var corpse = LifeRules.SkillHeal(dead, facts, 40, cure: true);
        Assert.AreEqual(dead.HitPointDamage, corpse.After.HitPointDamage);
        Assert.AreEqual((1, LifeConditions.CrippledArmOne | LifeConditions.CrippledLegsBoth), (corpse.After.Scars, corpse.After.Conditions & ~LifeConditions.Stunned));
        Assert.IsTrue(corpse.Events.HasFlag(LifeEvents.AlreadyDead));
    }
}
