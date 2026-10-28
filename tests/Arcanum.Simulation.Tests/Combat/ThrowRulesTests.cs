using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-COMBAT-017: the thrown range (0x4622A0, ledger RH4). Strengths and weights are invented.
[TestClass]
public sealed class ThrowRulesTests
{
    [TestMethod]
    public void TheRangeIsFiftyTimesStrengthOverATenthOfTheWeightOverFive()
    {
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.None, 15), "400 / 1 / 5");
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.Apprentice, 15));
        Assert.AreEqual(120, ThrowRules.Range(8, SkillTraining.Expert, 15), "expert adds half: 600 / 1 / 5");
        Assert.AreEqual(120, ThrowRules.Range(8, SkillTraining.Master, 15));
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.None, 0), "a tenth of the weight is at least 1");
        Assert.AreEqual(5, ThrowRules.Range(8, SkillTraining.None, 150), "400 / 15 = 26, / 5 = 5: divisions toward zero");
        Assert.AreEqual(9, ThrowRules.Range(9, SkillTraining.Expert, 150), "450 + 225 = 675, / 15 = 45, / 5 = 9");
    }

    [TestMethod]
    public void TheRangeIsAtLeastOne()
    {
        Assert.AreEqual(1, ThrowRules.Range(8, SkillTraining.None, ThrowRules.MaxWeight));
        Assert.AreEqual(1, ThrowRules.Range(-3, SkillTraining.None, 15), "a negative arm counts as 1");
        Assert.AreEqual(1, ThrowRules.Range(0, SkillTraining.Expert, 15));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, SkillTraining.None, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, SkillTraining.None, ThrowRules.MaxWeight + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, (SkillTraining)9, 15));
    }
}
