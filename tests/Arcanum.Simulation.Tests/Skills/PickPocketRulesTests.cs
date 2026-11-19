using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// The pick-pocket rules (ARC-SKILLS-010, ledger PP1–PP11). Values are invented.
[TestClass]
public sealed class PickPocketRulesTests
{
    private static readonly PickPocketOpenFacts Living = new(false, false, false, false, false, false, true);

    [TestMethod]
    public void TheStartRefusesSilentlyThenFloatsTheRefusalThenLootsTheDead()
    {
        Assert.AreEqual(PickPocketOpen.Steal, PickPocketRules.Open(Living));
        Assert.AreEqual(PickPocketOpen.NotSeen, PickPocketRules.Open(Living with { TargetSeen = false }));
        Assert.AreEqual(PickPocketOpen.Loot, PickPocketRules.Open(Living with { TargetDead = true, TargetSeen = false }));
        // The flags are tested before the life test: a dead mechanical critter floats the refusal.
        Assert.AreEqual(PickPocketOpen.NotAllowed, PickPocketRules.Open(Living with { TargetDead = true, TargetMechanical = true }));
        Assert.AreEqual(PickPocketOpen.NotAllowed, PickPocketRules.Open(Living with { TargetNoPickpocket = true }));
        // The silent refusals come first.
        Assert.AreEqual(PickPocketOpen.Refused, PickPocketRules.Open(Living with { ThiefDead = true, TargetNoPickpocket = true }));
        Assert.AreEqual(PickPocketOpen.Refused, PickPocketRules.Open(Living with { TargetStoned = true }));
        Assert.AreEqual(PickPocketOpen.Refused, PickPocketRules.Open(Living with { TargetIsThief = true }));
        Assert.IsTrue(PickPocketRules.NotAllowed(0, PickPocketRules.NoPickpocketFlag2));
        Assert.IsTrue(PickPocketRules.NotAllowed(PickPocketRules.MechanicalFlag, 0));
        Assert.IsFalse(PickPocketRules.NotAllowed(~PickPocketRules.MechanicalFlag, ~PickPocketRules.NoPickpocketFlag2));
    }

    [TestMethod]
    public void GoldAndAmmunitionMoveInPartAnyOtherItemWhole()
    {
        Assert.AreEqual((100, 37, 20, 7, 1), (PickPocketRules.Amount(PickPocketItemKind.Gold, 250), PickPocketRules.Amount(PickPocketItemKind.Gold, 37),
            PickPocketRules.Amount(PickPocketItemKind.Ammunition, 50), PickPocketRules.Amount(PickPocketItemKind.Ammunition, 7), PickPocketRules.Amount(PickPocketItemKind.Item, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PickPocketRules.Amount(PickPocketItemKind.Gold, -1));
    }

    [TestMethod]
    public void OnlyFailuresAreNoticedAndAnUntrainedThiefFacesThePerceptionDie()
    {
        foreach (var training in Enum.GetValues<SkillTraining>())
            foreach (var direction in Enum.GetValues<PickPocketDirection>())
            {
                Assert.AreEqual(PickPocketDetection.None, PickPocketRules.Detection(direction, training, true, true), "a success, fated or not");
                var masterPlant = direction == PickPocketDirection.Plant && training == SkillTraining.Master;
                Assert.AreEqual(masterPlant ? PickPocketDetection.None : PickPocketDetection.Noticed, PickPocketRules.Detection(direction, training, false, true));
                Assert.AreEqual(training == SkillTraining.None ? PickPocketDetection.PerceptionRoll : PickPocketDetection.None,
                    PickPocketRules.Detection(direction, training, false, false));
            }
        Assert.IsTrue(PickPocketRules.NoticedByRoll(8, 8));
        Assert.IsFalse(PickPocketRules.NoticedByRoll(9, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PickPocketRules.NoticedByRoll(21, 8));
    }

    [TestMethod]
    public void AKillMovesTheLeadersAlignmentWithoutTheSecondHalving()
    {
        // ARC-COMBAT-2873 (0x45DC90 with flag 1): −trunc(a / 20) for a ≥ 0, trunc(a / 20) for a < 0 and b ≤ a, the same bounds.
        Assert.AreEqual(-10, PickPocketRules.AlignmentAfterKill(200, 0, 0));
        Assert.AreEqual(-5, PickPocketRules.AlignmentAfterKill(119, 0, 0), "119 / 20 = 5");
        Assert.AreEqual(-1, PickPocketRules.AlignmentAfterKill(39, 0, 0), "39 / 20 = 1: a theft's second halving would make it 0");
        Assert.IsNull(PickPocketRules.AlignmentAfterKill(19, 0, 0));
        Assert.AreEqual(-300, PickPocketRules.AlignmentAfterKill(200, -295, -295), "the bound −100 − a");
        Assert.IsNull(PickPocketRules.AlignmentAfterKill(200, -300, -300));
        Assert.AreEqual(-260, PickPocketRules.AlignmentAfterKill(-200, -250, -250));
        Assert.AreEqual(-300, PickPocketRules.AlignmentAfterKill(-200, -295, -295), "the bound a − 100");
        Assert.IsNull(PickPocketRules.AlignmentAfterKill(-200, -199, -199), "a better killer is not changed");
        Assert.IsNull(PickPocketRules.AlignmentAfterKill(-19, -500, -500));
        Assert.AreEqual(PickPocketRules.AlignmentAfterTheft(200, 0, 0), PickPocketRules.AlignmentStep(200, 0, 0, halved: true), "the theft is the halved step");
    }

    [TestMethod]
    public void ATheftLowersTheThiefsBaseAlignmentDownToTheBound()
    {
        // A good victim (a ≥ 0): −trunc(trunc(a / 20) / 2).
        Assert.AreEqual(-5, PickPocketRules.AlignmentAfterTheft(200, 0, 0));
        Assert.AreEqual(-2, PickPocketRules.AlignmentAfterTheft(119, 0, 0), "119 / 20 = 5, / 2 = 2");
        Assert.IsNull(PickPocketRules.AlignmentAfterTheft(39, 0, 0), "a change of 0 is not applied");
        // The bound −100 − a stops it; a base already below the bound stays.
        Assert.AreEqual(-300, PickPocketRules.AlignmentAfterTheft(200, -298, -298));
        Assert.IsNull(PickPocketRules.AlignmentAfterTheft(200, -300, -300));
        Assert.IsNull(PickPocketRules.AlignmentAfterTheft(200, -500, -500));
        // An evil victim (a < 0) changes a thief that is no better than it, with the bound a − 100.
        Assert.AreEqual(-255, PickPocketRules.AlignmentAfterTheft(-200, -250, -250));
        Assert.IsNull(PickPocketRules.AlignmentAfterTheft(-200, -400, -400), "a base below the bound a − 100 stays");
        Assert.AreEqual(-300, PickPocketRules.AlignmentAfterTheft(-200, -298, -298), "b ≤ a and the bound a − 100");
        Assert.IsNull(PickPocketRules.AlignmentAfterTheft(-200, -199, -199), "a better thief is not changed");
        Assert.IsNull(PickPocketRules.AlignmentAfterTheft(-39, -500, -500));
    }

    [TestMethod]
    public void ACaughtThiefsConsequencesStopAtTheVictimsScriptThenAtAFollower()
    {
        Assert.AreEqual(PickPocketCaughtStop.VictimScript, PickPocketRules.Caught(false, true));
        Assert.AreEqual(PickPocketCaughtStop.VictimFollowsThief, PickPocketRules.Caught(true, true));
        Assert.AreEqual(PickPocketCaughtStop.None, PickPocketRules.Caught(true, false));
    }
}
