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
        owner.ToggleHeroConcealment(owner.Version);
        var revision = owner.Version.Revision;
        var talk = owner.BeginReactionConversation(owner.Version, Pal, new ReactionNpcFacts(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false));
        Assert.AreNotEqual(GameSessionDisposition.Rejected, talk.Disposition);
        Assert.AreEqual((false, false), (owner.IsConcealed(SessionCombatant.Hero), owner.IsConcealed(SessionCombatant.Critter(Pal))));
        Assert.IsTrue(owner.Version.Revision > revision);
    }

    [TestMethod]
    public void AnAttackOnAFollowerEndsTheWholePartysConcealment()
    {
        using var owner = Owner();
        owner.ToggleHeroConcealment(owner.Version);
        var pal = SessionCritterCombat.From(Pal, AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0)).Combat!;
        var result = owner.AttackCritter(owner.Version, new(pal));
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.AreEqual((false, 0), (owner.QueryConcealment().Hero, owner.QueryConcealment().Npcs.Count), "the attacker's party is the victim's");
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
    public void TheMarginIsZeroUntilTheHeroIsConcealedThenTheChecksEffectivenessLessDifficulty()
    {
        using var owner = Owner();
        var observer = new SkillDifficultyFacts { Light = 255 };
        Assert.AreEqual(new SessionProwlingMargin(0, null, null), owner.HeroProwlingMargin(8, observer, seeing: true));
        owner.ToggleHeroConcealment(owner.Version);
        var sight = owner.HeroProwlingMargin(8, observer, seeing: true);
        Assert.AreEqual(StealthRules.Margin(sight.Effectiveness!.Value, sight.Difficulty!.Total), sight.Margin);
        var hidden = owner.HeroProwlingMargin(8, observer with { ProwlerHidden = true }, seeing: true);
        Assert.AreEqual(Math.Clamp(hidden.Effectiveness!.Value + 100, 0, 100), hidden.Margin, "a blocker between them: difficulty −100");
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
