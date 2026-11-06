using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-DEV-144 (MIG13 S9c, goal-table.md GT94–GT97): the burn's amounts, victim types and round gate.</summary>
[TestClass]
public sealed class FireDamageRulesTests
{
    [TestMethod]
    public void TheBurnHurtsTypesFiveToSixteenOnly()
    {
        foreach (var type in new[] { 5, 8, 14, 15, 16 }) Assert.IsTrue(FireDamageRules.HurtType(type), $"type {type}");
        foreach (var type in new[] { 0, 1, 2, 3, 4, 17, 18 }) Assert.IsFalse(FireDamageRules.HurtType(type), $"type {type}");
    }

    [TestMethod]
    public void TheDamageIsThreeFireOrOneNormalDoubledInARound()
    {
        Assert.AreEqual(DamageByType.Zero with { Fire = 3 }, FireDamageRules.Damage(normal: false, doubled: false));
        Assert.AreEqual(DamageByType.Zero with { Fire = 6 }, FireDamageRules.Damage(normal: false, doubled: true));
        Assert.AreEqual(DamageByType.Zero with { Normal = 1 }, FireDamageRules.Damage(normal: true, doubled: false));
        Assert.AreEqual(DamageByType.Zero with { Normal = 2 }, FireDamageRules.Damage(normal: true, doubled: true));
    }

    [TestMethod]
    public void TheGateBelongsToTheRunAndOnlyAppliesInARound()
    {
        Assert.AreEqual(new FireRoundGate(true, false), FireDamageRules.Gate(null, 0), "outside a round: every call, undoubled");
        Assert.AreEqual(new FireRoundGate(true, false), FireDamageRules.Gate(null, 5), "a stored id means nothing outside a round");
        Assert.AreEqual(new FireRoundGate(true, true), FireDamageRules.Gate(1, 0), "a fresh run's slot 0 never matches round 1");
        Assert.AreEqual(new FireRoundGate(false, true), FireDamageRules.Gate(4, 4), "the same round again: nobody is hurt");
        Assert.AreEqual(new FireRoundGate(true, true), FireDamageRules.Gate(5, 4));
    }
}
