using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Party;

[TestClass]
public sealed class FollowConsentRulesTests
{
    // Made-up AI row (dislike ≤ -20, alignment gaps 300/200, level gate 5), so every refusal is reachable.
    private static readonly FollowThresholds Row = new(-20, 300, 200, 5);

    // A PC (charisma 8, alignment 0, level 3) asking an NPC (alignment 0, level 3, reaction 50) with no followers.
    private static FollowConsentInput Base() => new(
        MindControlledByPc: false, PcPersuasion: PersuasionTraining.None, PcCharisma: 8, PcAlignment: 0, PcLevel: 3,
        OtherLeaderCharisma: null, ForcedFollower: false, Reaction: 50, Thresholds: Row, NpcAlignment: 0, NpcLevel: 3,
        MaxFollowers: 2, NonForcedFollowers: 0, IgnoreCharismaLimit: false);

    [TestMethod]
    public void EachRefusalIsReachableAtItsBoundary()
    {
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base()));
        Assert.AreEqual(FollowConsent.AlreadyInGroup, FollowConsentRules.Evaluate(Base() with { OtherLeaderCharisma = 8 }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { OtherLeaderCharisma = 7 }));
        Assert.AreEqual(FollowConsent.Dislike, FollowConsentRules.Evaluate(Base() with { Reaction = -20 }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { Reaction = -19 }));
        Assert.AreEqual(FollowConsent.TooGood, FollowConsentRules.Evaluate(Base() with { PcAlignment = 301 }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { PcAlignment = 300 }));
        Assert.AreEqual(FollowConsent.TooBad, FollowConsentRules.Evaluate(Base() with { PcAlignment = -201 }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { PcAlignment = -200 }));
        Assert.AreEqual(FollowConsent.LowLevel, FollowConsentRules.Evaluate(Base() with { NpcLevel = 8 }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { NpcLevel = 7 }));
        Assert.AreEqual(FollowConsent.NotAllowed, FollowConsentRules.Evaluate(Base() with { MaxFollowers = 0 }));
        Assert.AreEqual(FollowConsent.LimitReached, FollowConsentRules.Evaluate(Base() with { NonForcedFollowers = 2 }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { NonForcedFollowers = 1 }));
    }

    [TestMethod]
    public void FirstMatchingStepWins()
    {
        var everything = Base() with
        {
            OtherLeaderCharisma = 9, Reaction = -100, PcAlignment = 900, NpcLevel = 50, NonForcedFollowers = 5,
        };
        Assert.AreEqual(FollowConsent.AlreadyInGroup, FollowConsentRules.Evaluate(everything));
        // Mind control and Persuasion Master accept before the other leader is looked at.
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(everything with { MindControlledByPc = true }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(everything with { PcPersuasion = PersuasionTraining.Master }));
        // A forced follower is accepted after the other-leader step, before reaction, alignment, level and limit.
        Assert.AreEqual(FollowConsent.AlreadyInGroup, FollowConsentRules.Evaluate(everything with { ForcedFollower = true }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(everything with { ForcedFollower = true, OtherLeaderCharisma = null }));
        var noLeader = everything with { OtherLeaderCharisma = null };
        Assert.AreEqual(FollowConsent.Dislike, FollowConsentRules.Evaluate(noLeader));
        Assert.AreEqual(FollowConsent.TooGood, FollowConsentRules.Evaluate(noLeader with { Reaction = 0 }));
        Assert.AreEqual(FollowConsent.LowLevel, FollowConsentRules.Evaluate(noLeader with { Reaction = 0, PcAlignment = 0 }));
        Assert.AreEqual(FollowConsent.LimitReached, FollowConsentRules.Evaluate(noLeader with { Reaction = 0, PcAlignment = 0, NpcLevel = 3 }));
    }

    [TestMethod]
    public void IgnoringTheCharismaLimitSkipsOnlyTheLimitSteps()
    {
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { NonForcedFollowers = 9, IgnoreCharismaLimit = true }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { MaxFollowers = 0, IgnoreCharismaLimit = true }));
        Assert.AreEqual(FollowConsent.Dislike, FollowConsentRules.Evaluate(Base() with { Reaction = -50, IgnoreCharismaLimit = true }));
    }

    [TestMethod]
    public void ExtremeInputsDoNotOverflow()
    {
        Assert.AreEqual(FollowConsent.TooGood, FollowConsentRules.Evaluate(Base() with { PcAlignment = int.MaxValue, NpcAlignment = int.MinValue }));
        Assert.AreEqual(FollowConsent.TooBad, FollowConsentRules.Evaluate(Base() with { PcAlignment = int.MinValue, NpcAlignment = int.MaxValue }));
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(Base() with { PcLevel = int.MaxValue, Thresholds = Row with { LevelGate = int.MaxValue } }));
        Assert.AreEqual(FollowConsent.LowLevel, FollowConsentRules.Evaluate(Base() with { PcLevel = int.MinValue, NpcLevel = int.MaxValue }));
    }

    [TestMethod]
    public void VirgilsRowRefusesAFreshPcOnlyAtTheLimit()
    {
        // ARC-RES-026: AI row 5 (dislike ≤ -1000, gaps 2001, level gate 30), level 1, alignment 1, reaction base 60.
        var virgil = Base() with
        {
            Thresholds = new(-1000, 2001, 2001, 30), NpcAlignment = 1, NpcLevel = 1, Reaction = 60, PcLevel = 1,
            MaxFollowers = FollowConsentRules.MaxFollowers(8, PersuasionTraining.None),
        };
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(virgil));
        foreach (var alignment in new[] { -1000, 1000 })
            Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(virgil with { PcAlignment = alignment }), alignment.ToString());
        Assert.AreEqual(FollowConsent.Accepted, FollowConsentRules.Evaluate(virgil with { Reaction = -10 }));
        Assert.AreEqual(FollowConsent.LimitReached, FollowConsentRules.Evaluate(virgil with { NonForcedFollowers = 2 }));
    }

    [TestMethod]
    public void MaxFollowersIsCharismaQuarterPlusExpertClampedToOneThroughSeven()
    {
        Assert.AreEqual(1, FollowConsentRules.MaxFollowers(0, PersuasionTraining.None));
        Assert.AreEqual(1, FollowConsentRules.MaxFollowers(7, PersuasionTraining.None));
        Assert.AreEqual(2, FollowConsentRules.MaxFollowers(8, PersuasionTraining.None));
        Assert.AreEqual(2, FollowConsentRules.MaxFollowers(8, PersuasionTraining.Apprentice));
        Assert.AreEqual(3, FollowConsentRules.MaxFollowers(8, PersuasionTraining.Expert));
        Assert.AreEqual(3, FollowConsentRules.MaxFollowers(8, PersuasionTraining.Master));
        Assert.AreEqual(6, FollowConsentRules.MaxFollowers(20, PersuasionTraining.Expert));
        Assert.AreEqual(7, FollowConsentRules.MaxFollowers(28, PersuasionTraining.Expert));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FollowConsentRules.MaxFollowers(-1, PersuasionTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FollowConsentRules.MaxFollowers(8, (PersuasionTraining)9));
    }

    [TestMethod]
    public void UnforcedLeaveIsRefusedUnderExtraordinaryCharismaOrMindControl()
    {
        Assert.IsTrue(FollowConsentRules.MayLeaveUnforced(19, false));
        Assert.IsFalse(FollowConsentRules.MayLeaveUnforced(20, false));
        Assert.IsFalse(FollowConsentRules.MayLeaveUnforced(8, true));
    }

    [TestMethod]
    public void InvalidInputsThrow()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => FollowConsentRules.Evaluate(null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FollowConsentRules.Evaluate(Base() with { PcPersuasion = (PersuasionTraining)7 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FollowConsentRules.Evaluate(Base() with { NonForcedFollowers = -1 }));
    }
}
