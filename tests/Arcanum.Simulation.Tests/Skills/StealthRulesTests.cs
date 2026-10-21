using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// Prowling's gait and margin (ARC-SKILLS-008, ledger ST3 and AW4), as the retail 1.0.7.4 executable runs them.
[TestClass]
public sealed class StealthRulesTests
{
    [TestMethod]
    public void BelowExpertTheStealthWalkAnExpertWalksAndOnlyAMasterWithFatigueRuns()
    {
        Assert.AreEqual(new StealthGait(StealthWalk.StealthWalk, false), StealthRules.Gait(SkillTraining.None, true));
        Assert.AreEqual(new StealthGait(StealthWalk.StealthWalk, false), StealthRules.Gait(SkillTraining.Apprentice, true));
        Assert.AreEqual(new StealthGait(StealthWalk.Walk, false), StealthRules.Gait(SkillTraining.Expert, true));
        Assert.AreEqual(new StealthGait(StealthWalk.Walk, true), StealthRules.Gait(SkillTraining.Master, true));
        Assert.AreEqual(new StealthGait(StealthWalk.Walk, false), StealthRules.Gait(SkillTraining.Master, false), "no fatigue left: no run");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StealthRules.Gait((SkillTraining)4, true));
    }

    [TestMethod]
    public void TheMarginIsEffectivenessLessDifficultyWithinZeroAndAHundredAndLowersPerception()
    {
        Assert.AreEqual((40, 0, 100, 0), (StealthRules.Margin(60, 20), StealthRules.Margin(10, 30), StealthRules.Margin(150, -100), StealthRules.Margin(0, 0)));
        Assert.AreEqual(100, StealthRules.Margin(int.MaxValue, int.MinValue), "no overflow");
        // P' = P − P × M / 100, truncated: 8 − 8 × 40 / 100 = 8 − 3 = 5.
        Assert.AreEqual((5, 8, 0), (StealthRules.PerceptionAfterMargin(8, 40), StealthRules.PerceptionAfterMargin(8, 0), StealthRules.PerceptionAfterMargin(8, 100)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StealthRules.PerceptionAfterMargin(8, 101));
    }
}
