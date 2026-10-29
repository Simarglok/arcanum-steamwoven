using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-RENDER-041: the pool of blood a dying critter leaves (ledger BP1–BP9). Flag values are the executable's.
[TestClass]
public sealed class BloodPoolRulesTests
{
    [TestMethod]
    public void AnOrdinaryCritterOnAnOpenTileLeavesAPool()
    {
        Assert.AreEqual(BloodPoolRefusal.None, BloodPoolRules.Leaves(default));
        // Every other critter flag, flag 2 and spell flag leaves it, "no blood splotches" (flags 2, 0x100) among them: that flag only
        // stops the hit's eye candy (0x433020).
        Assert.AreEqual(BloodPoolRefusal.None, BloodPoolRules.Leaves(new(~0x20000004u, ~0x400u, ~0x10000u, false, false)));
        Assert.AreEqual(BloodPoolRefusal.None, BloodPoolRules.Leaves(new(0, 0x100, 0, false, false)));
    }

    [TestMethod]
    public void TheRefusalsComeInTheOriginalsOrder()
    {
        var all = new BloodPoolFacts(0x20000004, 0x400, 0x10000, true, true);
        Assert.AreEqual(BloodPoolRefusal.TileBlocking, BloodPoolRules.Leaves(all));
        Assert.AreEqual(BloodPoolRefusal.ViolenceFilter, BloodPoolRules.Leaves(all with { TileBlocking = false }));
        Assert.AreEqual(BloodPoolRefusal.UndeadOrMechanical, BloodPoolRules.Leaves(all with { TileBlocking = false, ViolenceFilter = false }));
        Assert.AreEqual(BloodPoolRefusal.UndeadOrMechanical, BloodPoolRules.Leaves(new(BloodPoolRules.UndeadFlag, 0, 0, false, false)));
        Assert.AreEqual(BloodPoolRefusal.UndeadOrMechanical, BloodPoolRules.Leaves(new(BloodPoolRules.MechanicalFlag, 0, 0, false, false)));
        Assert.AreEqual(BloodPoolRefusal.Elemental, BloodPoolRules.Leaves(new(0, 0x400, 0x10000, false, false)));
        Assert.AreEqual(BloodPoolRefusal.Stoned, BloodPoolRules.Leaves(new(0, 0, 0x10000, false, false)));
        // The creation's own refusals do not test the tile (the dying goal's node does).
        Assert.AreEqual(BloodPoolRefusal.None, BloodPoolRules.Refusal(new(0, 0, 0, true, false)));
    }

    [TestMethod]
    public void ThePoolPlaysItsFramesOnceAndKeepsTheLast()
    {
        // blood.ART: 4 frames at 8 frames a second: the first step at once, then one every 125 ms.
        Assert.AreEqual(1, BloodPoolRules.Frame(0, 4, 8));
        Assert.AreEqual(1, BloodPoolRules.Frame(124, 4, 8));
        Assert.AreEqual(2, BloodPoolRules.Frame(125, 4, 8));
        Assert.AreEqual(3, BloodPoolRules.Frame(250, 4, 8));
        Assert.AreEqual(3, BloodPoolRules.Frame(long.MaxValue, 4, 8));
        // The delay is 1000 / rate in integer division (7 frames a second: 142 ms).
        Assert.AreEqual(1, BloodPoolRules.Frame(141, 4, 7));
        Assert.AreEqual(2, BloodPoolRules.Frame(142, 4, 7));
        Assert.AreEqual(0, BloodPoolRules.Frame(-1, 4, 8));
        Assert.AreEqual(0, BloodPoolRules.Frame(5000, 1, 8));
        Assert.AreEqual(3, BloodPoolRules.Frame(0, 4, 0));
        Assert.AreEqual(3, BloodPoolRules.Frame(2, 4, 2000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BloodPoolRules.Frame(0, 0, 8));
    }

    [TestMethod]
    public void APoolDecaysTwoGameDaysAfterItsCreation()
    {
        Assert.AreEqual(86_400_000, BloodPoolRules.DecayDelay(true));
        Assert.AreEqual(172_800_000, BloodPoolRules.DecayDelay(false));
        Assert.IsFalse(BloodPoolRules.Decayed(1000, 1000 + 172_800_000 - 1));
        Assert.IsTrue(BloodPoolRules.Decayed(1000, 1000 + 172_800_000));
    }
}
