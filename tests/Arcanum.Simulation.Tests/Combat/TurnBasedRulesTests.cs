using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// The turn-based round rules (ARC-COMBAT-003): numbers checked in the original 1.0.7.4 executable.
[TestClass]
public sealed class TurnBasedRulesTests
{
    [TestMethod]
    public void ScanRadiusIsHalfThePerceptionPlusFiveAtLeastTen()
    {
        Assert.AreEqual(10, TurnBasedRules.ScanRadius(1));
        Assert.AreEqual(10, TurnBasedRules.ScanRadius(10));
        Assert.AreEqual(10, TurnBasedRules.ScanRadius(11), "11 / 2 + 5 = 10");
        Assert.AreEqual(11, TurnBasedRules.ScanRadius(12));
        Assert.AreEqual(15, TurnBasedRules.ScanRadius(20));
        Assert.AreEqual(10, TurnBasedRules.ScanRadius(-3), "the division truncates toward zero");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnBasedRules.ScanRadius(TurnBasedRules.MaxStat + 1));
    }

    [TestMethod]
    public void TheScanSquareIncludesItsBounds()
    {
        var center = new CellCoord(100, 200);
        Assert.IsTrue(TurnBasedRules.InScanSquare(center, new(110, 190), 10));
        Assert.IsTrue(TurnBasedRules.InScanSquare(center, new(90, 210), 10));
        Assert.IsFalse(TurnBasedRules.InScanSquare(center, new(111, 200), 10));
        Assert.IsFalse(TurnBasedRules.InScanSquare(center, new(100, 189), 10));
        Assert.IsTrue(TurnBasedRules.InScanSquare(center, center, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnBasedRules.InScanSquare(center, center, -1));
    }

    [TestMethod]
    public void ThePcPartyGoesLastAndEachGroupKeepsTheScanOrder()
    {
        string[] scan = ["pc", "wolf", "virgil", "bandit", "dog"];
        var party = new HashSet<string> { "pc", "virgil", "dog" };
        CollectionAssert.AreEqual(new[] { "wolf", "bandit", "pc", "virgil", "dog" }, TurnBasedRules.Order(scan, party.Contains).ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), TurnBasedRules.Order(Array.Empty<string>(), party.Contains).ToArray());
    }

    [TestMethod]
    public void InactiveOrHiddenAwakeParticipantsAreSkipped()
    {
        Assert.IsFalse(TurnBasedRules.IsSkipped(active: true, hidden: false, sleeping: false));
        Assert.IsTrue(TurnBasedRules.IsSkipped(active: false, hidden: false, sleeping: false));
        Assert.IsTrue(TurnBasedRules.IsSkipped(active: true, hidden: true, sleeping: false));
        Assert.IsFalse(TurnBasedRules.IsSkipped(active: true, hidden: true, sleeping: true), "a hidden sleeper keeps its subturn");
        Assert.IsFalse(TurnBasedRules.IsSkipped(active: true, hidden: false, sleeping: true));
    }

    [TestMethod]
    public void ASubturnGrantsTheSpeedAtLeastFive()
    {
        Assert.AreEqual(5, TurnBasedRules.ActionPointGrant(-2));
        Assert.AreEqual(5, TurnBasedRules.ActionPointGrant(4));
        Assert.AreEqual(5, TurnBasedRules.ActionPointGrant(5));
        Assert.AreEqual(9, TurnBasedRules.ActionPointGrant(9));
        Assert.AreEqual(25, TurnBasedRules.ActionPointGrant(25));
    }

    [TestMethod]
    public void AttackCostFollowsTheWeaponSpeed()
    {
        Assert.AreEqual(5, TurnBasedRules.AttackCost(TurnBasedRules.UnarmedWeaponSpeed), "unarmed: 8 − 10 / 3");
        Assert.AreEqual(8, TurnBasedRules.AttackCost(0));
        Assert.AreEqual(9, TurnBasedRules.AttackCost(-3), "8 − (−3) / 3");
        Assert.AreEqual(2, TurnBasedRules.AttackCost(18));
        Assert.AreEqual(2, TurnBasedRules.AttackCost(20), "8 − 20 / 3 = 2");
        Assert.AreEqual(2, TurnBasedRules.AttackCost(21));
        Assert.AreEqual(2, TurnBasedRules.AttackCost(24));
        Assert.AreEqual(1, TurnBasedRules.AttackCost(25));
        Assert.AreEqual(1, TurnBasedRules.AttackCost(400));
        Assert.AreEqual(15, TurnBasedRules.WeaponSpeed(10, 0, SkillTraining.Apprentice));
        Assert.AreEqual(10, TurnBasedRules.WeaponSpeed(12, -2, SkillTraining.None));
        Assert.AreEqual(23, TurnBasedRules.WeaponSpeed(10, 8, SkillTraining.Master));
    }

    [TestMethod]
    public void MoveAndActionCosts()
    {
        Assert.AreEqual(8, TurnBasedRules.MoveCost(4, alwaysRunningPc: false));
        Assert.AreEqual(4, TurnBasedRules.MoveCost(4, alwaysRunningPc: true));
        Assert.AreEqual(1, TurnBasedRules.StepCost(running: true, currentFatigue: 1));
        Assert.AreEqual(2, TurnBasedRules.StepCost(running: true, currentFatigue: 0), "a runner without fatigue walks");
        Assert.AreEqual(2, TurnBasedRules.StepCost(running: false, currentFatigue: 30));
        Assert.AreEqual(5, TurnBasedRules.AttackActionCost(5, reachOne: true, distance: 1, adjacentPathSteps: 0, alwaysRunningPc: false));
        Assert.AreEqual(11, TurnBasedRules.AttackActionCost(5, reachOne: true, distance: 4, adjacentPathSteps: 3, alwaysRunningPc: false));
        Assert.AreEqual(5, TurnBasedRules.AttackActionCost(5, reachOne: false, distance: 4, adjacentPathSteps: 3, alwaysRunningPc: false));
        Assert.AreEqual(2, TurnBasedRules.UseObjectActionCost(sameCell: true, adjacentPathSteps: 9, alwaysRunningPc: false));
        Assert.AreEqual(5, TurnBasedRules.UseObjectActionCost(sameCell: false, adjacentPathSteps: 3, alwaysRunningPc: true));
        Assert.AreEqual(7, TurnBasedRules.PickUpActionCost(sameCell: false, pathSteps: 3, alwaysRunningPc: false));
        Assert.AreEqual(1, TurnBasedRules.PickUpActionCost(sameCell: true, pathSteps: 3, alwaysRunningPc: false));
        Assert.AreEqual((4, 4, 4, 5), (TurnBasedRules.CastSpellCost, TurnBasedRules.UseSkillCost, TurnBasedRules.UseItemCost, TurnBasedRules.GetUpCost));
    }

    [TestMethod]
    public void OnlyTheLocalPcOverexerts()
    {
        Assert.AreEqual(new ActionPointCharge(ActionPointChargeKind.Paid, 3), TurnBasedRules.Charge(8, 5, false, 0));
        Assert.AreEqual(new ActionPointCharge(ActionPointChargeKind.Paid, 0), TurnBasedRules.Charge(5, 5, true, 30));
        Assert.AreEqual(new ActionPointCharge(ActionPointChargeKind.Overexerted, 0), TurnBasedRules.Charge(1, 5, true, 2));
        Assert.AreEqual(new ActionPointCharge(ActionPointChargeKind.Refused, 0), TurnBasedRules.Charge(1, 5, true, 1), "fatigue must be above 1");
        Assert.AreEqual(new ActionPointCharge(ActionPointChargeKind.Refused, 0), TurnBasedRules.Charge(0, 5, true, 30), "no action points left");
        Assert.AreEqual(new ActionPointCharge(ActionPointChargeKind.Refused, 0), TurnBasedRules.Charge(4, 5, false, 30), "an NPC never overexerts");
        Assert.IsTrue(TurnBasedRules.CanAfford(5, 5, false, 0));
        Assert.IsTrue(TurnBasedRules.CanAfford(1, 5, true, 2));
        Assert.IsFalse(TurnBasedRules.CanAfford(1, 5, true, 1));
        Assert.IsFalse(TurnBasedRules.CanAfford(4, 5, false, 30));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnBasedRules.Charge(-1, 5, true, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TurnBasedRules.Charge(5, -1, true, 2));
    }

    [TestMethod]
    public void OverexertionDamageMeetsThreeQuartersOfTheNormalResistance()
    {
        Assert.AreEqual(2, TurnBasedRules.OverexertionFatigueDamage(0));
        Assert.AreEqual(2, TurnBasedRules.OverexertionFatigueDamage(-40), "only a positive resistance counts");
        Assert.AreEqual(2, TurnBasedRules.OverexertionFatigueDamage(40), "¾ × 40 = 30 % of 2 truncates to 0");
        Assert.AreEqual(2, TurnBasedRules.OverexertionFatigueDamage(60), "¾ × 60 = 45 % of 2 truncates to 0 (60 % would take 1)");
        Assert.AreEqual(1, TurnBasedRules.OverexertionFatigueDamage(68), "¾ × 68 = 51 % of 2 is 1");
        Assert.AreEqual(1, TurnBasedRules.OverexertionFatigueDamage(95));
    }

    [TestMethod]
    public void AnNpcAboveThirtyFivePercentFatigueMayStartWhatItCannotPay()
    {
        Assert.IsTrue(TurnBasedRules.NpcMayStartAction(5, 5, 0, 30), "the action points cover it");
        Assert.IsTrue(TurnBasedRules.NpcMayStartAction(1, 5, 11, 30), "100 × 11 / 30 = 36");
        Assert.IsFalse(TurnBasedRules.NpcMayStartAction(1, 5, 10, 30), "100 × 10 / 30 = 33");
        Assert.IsFalse(TurnBasedRules.NpcMayStartAction(1, 5, 7, 20), "exactly 35 is not above");
        Assert.IsTrue(TurnBasedRules.NpcMayStartAction(1, 5, 36, 100));
        Assert.IsFalse(TurnBasedRules.NpcMayStartAction(1, 5, 5, 0), "a maximum of 0 never divides");
        Assert.IsFalse(TurnBasedRules.NpcMayStartAction(1, 5, 5, -4));
    }

    [TestMethod]
    public void TheStallCheckForcesAnActorSeenTwiceWithTheSameActionPoints()
    {
        foreach (var keep in new[] { false, true })
        {
            var stall = new TurnBasedStall<string>().Observe("wolf", 6, keep);
            Assert.IsFalse(stall.Forced);
            stall = stall.Observe("wolf", 4, keep);
            Assert.IsFalse(stall.Forced, "the action points changed");
            stall = stall.Observe("wolf", 4, keep);
            Assert.IsTrue(stall.Forced);
            Assert.IsTrue(stall.Observe("bandit", 3, keep).Forced, "only the turn event clears the flag");
        }
    }

    [TestMethod]
    public void A11TheOriginalComparesANewActorWithThePreviousActorsActionPoints()
    {
        // The wolf ends with 3 left; the bandit has spent from 8 to 3 between its first two checks.
        var afterWolf = new TurnBasedStall<string>().Observe("wolf", 5, false).Observe("wolf", 3, false);
        var fixedCheck = afterWolf.Observe("bandit", 8, keepPreviousActionPoints: false).Observe("bandit", 3, keepPreviousActionPoints: false);
        Assert.IsFalse(fixedCheck.Forced);
        var original = afterWolf.Observe("bandit", 8, keepPreviousActionPoints: true);
        Assert.AreEqual(("bandit", 3), (original.LastActor, original.LastActionPoints), "the wolf's 3 stays the reference");
        Assert.IsTrue(original.Observe("bandit", 3, keepPreviousActionPoints: true).Forced);
        Assert.AreEqual(8, afterWolf.Observe("bandit", 8, keepPreviousActionPoints: false).LastActionPoints);
    }

    [TestMethod]
    public void TheOriginalProfileKeepsTheRoundAnomalies()
    {
        var original = CombatAnomalyProfile.Original;
        Assert.IsTrue(original.FirstSubturnStartedTwice && original.StallCheckKeepsPreviousActionPoints &&
            original.OverexertionKnockoutClearsNextActionPoints);
        var steamwoven = CombatAnomalyProfile.Steamwoven;
        Assert.IsFalse(steamwoven.FirstSubturnStartedTwice || steamwoven.StallCheckKeepsPreviousActionPoints ||
            steamwoven.OverexertionKnockoutClearsNextActionPoints);
    }
}
