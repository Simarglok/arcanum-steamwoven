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
    public void ADialogEndsTheConcealmentOfTheHeroAndOfThatNpcOnly()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Stranger).Disposition);
        owner.ToggleHeroConcealment(owner.Version);
        var revision = owner.Version.Revision;
        var talk = owner.BeginReactionConversation(owner.Version, Pal, new ReactionNpcFacts(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false));
        Assert.AreEqual(GameSessionDisposition.Changed, talk.Disposition);
        Assert.AreEqual((false, false, true), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Pal)),
            owner.IsConcealed(SessionCombatant.Critter(Stranger))), "the other follower stays concealed");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
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
}
