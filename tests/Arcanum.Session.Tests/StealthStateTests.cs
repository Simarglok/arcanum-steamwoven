using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's and its followers' concealment (ARC-SKILLS-008, ledger ST1–ST6). NPC ids are invented.
[TestClass]
public sealed class StealthStateTests
{
    private const string Pal = "oid:pal", Stranger = "oid:stranger";

    private static GameSessionOwner Owner()
    {
        var owner = CreateWith(HalfElf());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Pal).Disposition);
        return owner;
    }

    [TestMethod]
    public void TheToggleConcealsTheHeroAndEveryFollowerInOneRevisionAndClearsThemAgain()
    {
        using var owner = Owner();
        var before = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ToggleHeroConcealment(owner.Version).Disposition);
        Assert.AreEqual((true, true, before + 1), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Pal)), owner.Version.Revision));
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Stranger)));
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual((false, 0), (owner.QueryConcealment().Hero, owner.QueryConcealment().Npcs.Count));
    }

    [TestMethod]
    public void AJoiningFollowerTakesTheHerosStateAndALeavingOneLosesIt()
    {
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Stranger).Disposition);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Stranger)), "joins concealed");
        owner.LeaveParty(owner.Version, Stranger, new PartyLeave(Force: true));
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Stranger)));
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Pal)));
    }

    [TestMethod]
    public void ADialogEndsTheConcealmentOfTheHeroAndItsWholeFollowerTree()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Stranger).Disposition);
        owner.ToggleHeroConcealment(owner.Version);
        var revision = owner.Version.Revision;
        var talk = owner.BeginReactionConversation(owner.Version, Pal, new ReactionNpcFacts(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false));
        Assert.AreEqual(GameSessionDisposition.Changed, talk.Disposition);
        // 0x567460 runs 0x45EE30 on the concealed hero, which reaches its recursive follower list (0x441260): the other follower stands up too (ARC-SKILLS-2142).
        Assert.AreEqual((false, false, false), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Pal)),
            owner.IsConcealed(SessionCombatant.Critter(Stranger))), "the hero's whole follower list stands up");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void ADialogWithAStrangerStandsUpTheHerosFollowersOfFollowersToo()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Summon, Pal).Disposition);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Summon)));
        var talk = owner.BeginReactionConversation(owner.Version, Stranger, new ReactionNpcFacts(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false));
        Assert.AreEqual(GameSessionDisposition.Changed, talk.Disposition);
        Assert.AreEqual((false, false, false), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Pal)),
            owner.IsConcealed(SessionCombatant.Critter(Summon))), "the partner is no follower: the hero's own tree stands up, depth included");
    }

    [TestMethod]
    public void ADialogStartThatLeavesTheReactionSlotUnchangedStillTakesARevisionForTheConcealment()
    {
        using var owner = Owner();
        var npc = new ReactionNpcFacts(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BeginReactionConversation(owner.Version, Pal, npc).Disposition);
        var unchanged = owner.BeginReactionConversation(owner.Version, Pal, npc);
        Assert.AreEqual(GameSessionDisposition.NoChange, unchanged.Disposition, "the second start finds the slot as it is");
        owner.ToggleHeroConcealment(owner.Version);
        var revision = owner.Version.Revision;
        var again = owner.BeginReactionConversation(owner.Version, Pal, npc);
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1, revision + 1), (again.Disposition, again.Version.Revision, owner.Version.Revision));
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Hero) || owner.IsConcealed(SessionCombatant.Critter(Pal)));
    }

    [TestMethod]
    public void AnAttackOnAFollowerEndsTheWholePartysConcealmentEvenOnAMissAndOnAStrangerNothing()
    {
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        var stranger = SessionCritterCombat.From(Stranger, AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0)).Combat!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttackCritter(owner.Version, new(stranger)).Failure);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Hero) && owner.IsConcealed(SessionCombatant.Critter(Pal)), "a stranger's attack event is not the party's");
        var pal = SessionCritterCombat.From(Pal, AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0)).Combat!;
        var missed = false;
        for (var attempt = 0; attempt < 200 && !missed; attempt++)
        {
            if (!owner.IsConcealed(SessionCombatant.Hero)) owner.ToggleHeroConcealment(owner.Version);
            var revision = owner.Version.Revision;
            var result = owner.AttackCritter(owner.Version, new(pal));
            Assert.AreEqual((GameSessionFailure.None, GameSessionDisposition.Changed), (result.Failure, result.Disposition));
            Assert.AreEqual((false, 0), (owner.QueryConcealment().Hero, owner.QueryConcealment().Npcs.Count), "the attacker's party is the victim's");
            if (result.Recipient is null && result.Resolution!.Fate == FateUse.None)
            {
                missed = true;
                Assert.AreEqual(revision + 1, owner.Version.Revision, "a miss changes only the concealment, in one revision");
            }
        }
        Assert.IsTrue(missed, "a miss in 200 attacks");
    }

    [TestMethod]
    public void ConcealmentRoundTripsThroughTheSnapshotAndRestoreChecksIt()
    {
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"concealment\":{\"hero\":true,\"npcs\":[\"oid:pal\"]}");
        using var restored = Restore(Decode(text));
        Assert.IsTrue(restored.IsConcealed(SessionCombatant.Critter(Pal)) && restored.IsConcealed(SessionCombatant.Hero));
        var stranger = GameSessionOwner.Restore(Decode(text.Replace("\"npcs\":[\"oid:pal\"]", "\"npcs\":[\"oid:stranger\"]")), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.InvalidConcealment, stranger.Failure, "only a follower can be concealed");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical,
            GameSessionSnapshotCodec.TryDecode(text.Replace("\"npcs\":[\"oid:pal\"]", "\"npcs\":[\"oid:pal\",\"oid:pal\"]")).Failure);
    }

    [TestMethod]
    public void TheMarginIsZeroUntilTheHeroIsConcealedAndFollowsTheObserversPerceptionAndTheCheckKind()
    {
        // Dexterity 20 and 5 points of prowling, untrained.
        var stats = Defaults(); stats[2] = 20;
        int[] basic = new int[12]; basic[(int)Skill.Prowling] = 5;
        using var owner = CreateWith(HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } });
        var observer = new SkillDifficultyFacts { Light = 255 };
        Assert.AreEqual(new SessionProwlingMargin(0, null, null), owner.HeroProwlingMargin(8, observer, seeing: true));
        owner.ToggleHeroConcealment(owner.Version);
        var sight = owner.HeroProwlingMargin(8, observer, seeing: true);
        var request = new SkillEffectivenessFacts { OpponentValue = 8, LocalPc = true };
        var level = owner.QueryHeroSkills()![Skill.Prowling].Level;
        Assert.IsTrue(level > 0);
        Assert.AreEqual(SkillCheckRules.Effectiveness(Skill.Prowling, level, request), sight.Effectiveness, "opposed by the observer's perception");
        Assert.IsTrue(owner.HeroProwlingMargin(1, observer, seeing: true).Effectiveness > owner.HeroProwlingMargin(10, observer, seeing: true).Effectiveness);
        // An untrained prowler without cover: the sight check's prowling term is +50, the hearing check's (no armour) 0; full light
        // still costs an untrained prowler 30 against an observer without dark sight in the sight check only.
        var hearing = owner.HeroProwlingMargin(8, observer, seeing: false);
        Assert.AreEqual((50, 30, 0, 0), (sight.Difficulty!.Skill, sight.Difficulty.Light, hearing.Difficulty!.Skill, hearing.Difficulty.Light));
        Assert.AreEqual(StealthRules.Margin(sight.Effectiveness!.Value, sight.Difficulty.Total), sight.Margin);
        Assert.AreEqual(100, owner.HeroProwlingMargin(8, observer with { ProwlerHidden = true }, seeing: true).Margin, "a blocker between them: difficulty −100");
        // A sleeping observer (the check's target critter) lowers the difficulty by 50.
        var asleep = owner.HeroProwlingMargin(8, observer with { TargetConditions = SkillTargetConditions.Sleeping }, seeing: false);
        Assert.AreEqual((-50, -50), (asleep.Difficulty!.Awareness, asleep.Difficulty.Total - hearing.Difficulty.Total));
    }

    // ARC-AI-1517 (0x4A9650 → 0x45EE30, VBG6): the attack event's concealment step the host runs for any attacker.
    [TestMethod]
    public void TheAttackEventsConcealmentStepEndsTheHerosGroupForAHeroOrFollowerVictimInOneRevisionAndNoOtherVictim()
    {
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Critter(Stranger)).Disposition, "a stranger is no PC group");
        Assert.AreEqual((true, true, revision), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Pal)), owner.Version.Revision));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Critter(Pal)).Disposition);
        Assert.AreEqual((false, 0, revision + 1), (owner.QueryConcealment().Hero, owner.QueryConcealment().Npcs.Count, owner.Version.Revision), "a follower victim: the hero and every follower");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Hero).Disposition, "nothing left to end");
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Hero).Disposition);
        Assert.AreEqual((false, 0), (owner.QueryConcealment().Hero, owner.QueryConcealment().Npcs.Count), "a hero victim: the same group");
    }

    [TestMethod]
    public void TheAttackEventsConcealmentStepOnAWaitingFollowerEndsTheHerosGroupAndLeavesTheWaitingOneAlone()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Stranger).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Pal).Disposition);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Critter(Pal)).Disposition);
        Assert.AreEqual((false, false), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Stranger))), "the hero and the follower list");
    }

    [TestMethod]
    public void TheAttackEventsConcealmentStepIsRefusedForAStaleVersion()
    {
        using var owner = Owner();
        var stale = owner.Version;
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.EndConcealmentForAttackEvent(stale, SessionCombatant.Hero).Failure);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Hero));
    }

    [TestMethod]
    public void AWaitingFollowerKeepsItsOwnStateAndUnwaitingCopiesTheHerosAgain()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Pal).Disposition);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Pal)), "a waiting NPC is not in the follower list");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UnwaitParty(owner.Version, Pal, PartyStateTests.Facts(), true).Disposition);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Pal)), "following again takes the hero's state");
    }

    [TestMethod]
    public void TheUnconcealGoalsEndNodeLeavesAFollowerOfASneakingHeroConcealed()
    {
        // ARC-AI-1653 (0x42AFB0 → 0x45EE30): unless its leader is concealed.
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        var revision = owner.Version.Revision;
        var kept = owner.EndNpcConcealmentForUnconceal(owner.Version, Pal);
        Assert.AreEqual((GameSessionDisposition.NoChange, revision), (kept.Disposition, owner.Version.Revision), "the sneaking hero leads it: nothing changes");
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Pal)));
    }

    [TestMethod]
    public void TheUnconcealEndNodeEndsAnNpcConcealmentWhoseLeaderIsNotConcealedAndRefusesAStaleVersionAndABadId()
    {
        using var owner = Owner();
        // A waiting NPC keeps its leader and its own flag while the hero's toggle passes it by: concealed under a calm hero.
        owner.ToggleHeroConcealment(owner.Version);
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Pal).Disposition);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Pal)), "waiting keeps the flag it had");
        owner.ToggleHeroConcealment(owner.Version);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Pal)), "the hero's toggle leaves a waiting NPC's own state");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.EndNpcConcealmentForUnconceal(stale, Pal).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, owner.EndNpcConcealmentForUnconceal(owner.Version, "").Failure);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndNpcConcealmentForUnconceal(owner.Version, Pal).Disposition);
        Assert.AreEqual((false, false, revision + 1), (owner.IsConcealed(SessionCombatant.Critter(Pal)), owner.IsConcealed(SessionCombatant.Hero), owner.Version.Revision));
    }

    [TestMethod]
    public void TheToggleIsRefusedWithoutALivingHero()
    {
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.ConcealmentUnavailable, heroless.ToggleHeroConcealment(heroless.Version).Failure);
        using var dead = Owner();
        dead.Kill(dead.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(GameSessionFailure.ConcealmentUnavailable, dead.ToggleHeroConcealment(dead.Version).Failure);
        using var stale = Owner();
        Assert.AreEqual(GameSessionFailure.StaleVersion, stale.ToggleHeroConcealment(new(stale.Identity, 99)).Failure);
    }

    // ARC-WORLD-2118 (0x45EE30 reaches the recursive follower list; the join 0x45DE90 gives the joiner its leader's state): the followers of a companion sneak with the hero.
    private const string Summon = "oid:critter:7", Imp = "oid:imp";

    [TestMethod]
    public void TheToggleReachesTheFollowersOfAFollowerAndTheSnapshotKeepsThem()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Summon, Pal).Disposition);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Summon)));
        owner.ToggleHeroConcealment(owner.Version);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Summon)), "a companion's summon sneaks with the hero");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.IsTrue(restored.IsConcealed(SessionCombatant.Critter(Summon)));
        // A concealed critter that nobody of the hero's chain leads is refused by the restore.
        var free = GameSessionOwner.Restore(Decode(text.Replace("\"npcs\":[\"oid:critter:7\",\"oid:pal\"]", "\"npcs\":[\"oid:critter:7\",\"oid:pal\",\"oid:stranger\"]")), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.InvalidConcealment, free.Failure);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Summon)));
    }

    [TestMethod]
    public void AJoinUnderANpcLeaderTakesThatLeadersConcealmentAndANpcOutsideTheChainConcealsNoOne()
    {
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Summon, Pal).Disposition);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Summon)), "the concealed companion's summon");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Imp, Summon).Disposition);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Imp)), "the joiner takes the state of its own leader");
        // A leader outside the hero's chain conceals no one, concealed hero or not.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Imp, Stranger).Disposition);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Imp)));
        // The hero's own join gives the state to the joiner's followers as well.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Summon, Stranger).Disposition);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Summon)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Stranger).Disposition);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Stranger)) && owner.IsConcealed(SessionCombatant.Critter(Summon)) && owner.IsConcealed(SessionCombatant.Critter(Imp)),
            "0x45EE30 reaches the joiner's own followers");
    }

    [TestMethod]
    public void ALeavingCompanionTakesItsFollowersOutOfTheHerosConcealmentAndAnAttackEndsTheWholeTree()
    {
        using var owner = Owner();
        owner.ForceJoinLeader(owner.Version, Summon, Pal);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Pal, new PartyLeave(Force: true)).Disposition);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Pal)));
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Summon)), "the summon is no longer in the hero's chain");
        owner.ForceJoinParty(owner.Version, Pal);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Summon)), "the companion's rejoin gives its followers the hero's state");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Hero).Disposition);
        Assert.AreEqual(0, owner.QueryConcealment().Npcs.Count);
        // An NPC follower that leaves its NPC leader is revealed.
        owner.ToggleHeroConcealment(owner.Version);
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Summon)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: true)).Disposition);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Summon)));
    }

    [TestMethod]
    public void TheUnconcealEndReadsTheNpcLeadersConcealment()
    {
        using var owner = Owner();
        owner.ForceJoinLeader(owner.Version, Summon, Pal);
        owner.ForceJoinLeader(owner.Version, Imp, Summon);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndNpcConcealmentForUnconceal(owner.Version, Imp).Disposition, "its leader, the summon, is concealed");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndNpcConcealmentForUnconceal(owner.Version, Summon).Disposition, "its leader, the companion, is concealed");
        Assert.IsTrue(owner.IsConcealed(SessionCombatant.Critter(Imp)));
    }

    [TestMethod]
    public void AnAttackOnAFollowerOfAFollowerEndsTheHerosWholeConcealment()
    {
        using var owner = Owner();
        owner.ForceJoinLeader(owner.Version, Summon, Pal);
        owner.ToggleHeroConcealment(owner.Version);
        // 0x4A9650: the victim's PC-chain head (0x45DDA0) is the hero, so the hit on the companion's summon reveals the hero and the whole tree.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Critter(Summon)).Disposition);
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Hero) || owner.IsConcealed(SessionCombatant.Critter(Pal)) || owner.IsConcealed(SessionCombatant.Critter(Summon)));
        // A critter outside the hero's chain is no group of the session's.
        owner.ForceJoinLeader(owner.Version, Imp, Stranger);
        owner.ToggleHeroConcealment(owner.Version);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndConcealmentForAttackEvent(owner.Version, SessionCombatant.Critter(Imp)).Disposition);
    }
}
