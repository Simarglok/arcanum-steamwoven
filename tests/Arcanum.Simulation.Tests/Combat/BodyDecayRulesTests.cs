using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-WORLD-1158: the body decay of a dead critter (ledger PO10). Flag values are the executable's.
[TestClass]
public sealed class BodyDecayRulesTests
{
    [TestMethod]
    public void OnlyTheNoDecayBitOfCritterFlags2KeepsTheBody()
    {
        Assert.IsTrue(BodyDecayRules.Schedules(0));
        Assert.IsTrue(BodyDecayRules.Schedules(~0x40u));
        Assert.IsFalse(BodyDecayRules.Schedules(0x40));
        Assert.IsFalse(BodyDecayRules.Schedules(uint.MaxValue));
    }

    [TestMethod]
    public void ADeadCritterIsDroppedAndDestroyedAndALivingOneIsLeft()
    {
        Assert.AreEqual(BodyDecayStep.DropAndDestroy, BodyDecayRules.Fire(true));
        Assert.AreEqual(BodyDecayStep.NotDead, BodyDecayRules.Fire(false));
    }

    [TestMethod]
    public void TheDelayIsOneGameDayForACritter()
    {
        Assert.AreEqual(86_400_000L, BloodPoolRules.DecayDelay(true));
    }
}
