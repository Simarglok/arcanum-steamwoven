using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

// The fate rules (ARC-WORLD-054, ledger FT1–FT8): arm, undo, use and grant as the retail 1.0.7.4 executable runs them.
[TestClass]
public sealed class FateRulesTests
{
    [TestMethod]
    public void TwelveKindsAndTheirBits()
    {
        Assert.AreEqual(FateRules.KindCount, Enum.GetValues<FateKind>().Length);
        Assert.AreEqual(0, FateRules.Mask(FateKind.FullHeal));
        Assert.AreEqual(1, FateRules.Mask(FateKind.ForceGoodReaction));
        Assert.AreEqual(2, FateRules.Mask(FateKind.CriticalHit));
        Assert.AreEqual(1024, FateRules.Mask(FateKind.DisarmTraps));
        Assert.IsFalse(FateRules.IsArmed(new(0, FateRules.AllArmed), FateKind.FullHeal));
        Assert.IsTrue(FateRules.IsArmed(new(0, 4), FateKind.CriticalMiss));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FateRules.Mask((FateKind)12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FateRules.IsArmed(new(101, 0), FateKind.Heal));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FateRules.IsArmed(new(1, 0x800), FateKind.Heal));
    }

    [TestMethod]
    public void ArmSpendsOnePointAndRefusesAnArmedKindFirst()
    {
        var armed = FateRules.Arm(new(2, 0), FateKind.CriticalHit);
        Assert.AreEqual(new FateChange(FateStatus.Armed, new(2, 0), new(1, 2)), armed);
        Assert.AreEqual(FateStatus.AlreadyArmed, FateRules.Arm(new(1, 2), FateKind.CriticalHit).Status);
        // The armed test comes first: an armed kind without points is AlreadyArmed, nothing changes either way.
        Assert.AreEqual(new FateChange(FateStatus.AlreadyArmed, new(0, 2), new(0, 2)), FateRules.Arm(new(0, 2), FateKind.CriticalHit));
        Assert.AreEqual(new FateChange(FateStatus.NoPoints, new(0, 0), new(0, 0)), FateRules.Arm(new(0, 0), FateKind.Heal));
        // The full heal is never armed: each use spends a point.
        var heal = FateRules.Arm(new(2, 1), FateKind.FullHeal);
        Assert.AreEqual(new FateChange(FateStatus.UsedAtOnce, new(2, 1), new(1, 1)), heal);
        Assert.AreEqual(FateStatus.UsedAtOnce, FateRules.Arm(heal.After, FateKind.FullHeal).Status);
    }

    [TestMethod]
    public void UndoRefundsUpToAHundredAndUseRefundsNothing()
    {
        Assert.AreEqual(new FateChange(FateStatus.Undone, new(4, 0x401), new(5, 0x400)), FateRules.Undo(new(4, 0x401), FateKind.ForceGoodReaction));
        // The stat setter clamps the refund: at 100 the point is lost.
        Assert.AreEqual(new FateState(100, 0), FateRules.Undo(new(100, 1), FateKind.ForceGoodReaction).After);
        Assert.AreEqual(FateStatus.NotArmed, FateRules.Undo(new(4, 0), FateKind.ForceGoodReaction).Status);
        Assert.AreEqual(FateStatus.NotArmed, FateRules.Undo(new(4, 0x7FF), FateKind.FullHeal).Status);
        Assert.AreEqual(new FateChange(FateStatus.Used, new(4, 0x6), new(4, 0x4)), FateRules.Use(new(4, 0x6), FateKind.CriticalHit));
        Assert.AreEqual(new FateChange(FateStatus.NotArmed, new(4, 0x4), new(4, 0x4)), FateRules.Use(new(4, 0x4), FateKind.CriticalHit));
    }

    [TestMethod]
    public void GrantAddsOnePointUpToAHundred()
    {
        Assert.AreEqual(new FateState(1, 3), FateRules.Grant(new(0, 3)).After);
        var full = FateRules.Grant(new(100, 0));
        Assert.AreEqual(FateStatus.Granted, full.Status);
        Assert.IsFalse(full.Changed);
    }

    [TestMethod]
    public void SkillFatesMapToTheRunsKinds()
    {
        CollectionAssert.AreEqual(
            new FateKind?[] { null, FateKind.Gambling, FateKind.Heal, FateKind.PickPocket, FateKind.Repair, FateKind.PickLocks, FateKind.DisarmTraps },
            Enum.GetValues<SkillFate>().Select(FateRules.Of).ToArray());
        Assert.AreEqual(FateKind.Heal, FateRules.Of(SkillCheckRules.Fate(Skill.Heal)));
        Assert.IsNull(FateRules.Of(SkillCheckRules.Fate(Skill.Melee)));
    }

    [TestMethod]
    public void TheFullHealClearsHitPointDamagePoisonAndFatigueOnly()
    {
        var facts = new LifeFacts();
        var hurt = new LifeState(5, 7, 250, LifeConditions.Blinded, 2);
        var outcome = LifeRules.FateFullHeal(hurt, facts);
        Assert.AreEqual(new LifeState(0, 0, 0, LifeConditions.Blinded, 2), outcome.After);
        var dead = LifeRules.Kill(LifeState.Unharmed, facts).After;
        var refused = LifeRules.FateFullHeal(dead, facts);
        Assert.IsTrue(refused.Events.HasFlag(LifeEvents.AlreadyDead));
        Assert.AreEqual(dead, refused.After);
    }
}
