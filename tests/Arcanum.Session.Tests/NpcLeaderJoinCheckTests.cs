using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-WORLD-2119 (#2119, follow-up of #1864): the hero's follow check (0x4AD950) of an NPC another NPC leads reads the NPC link's forced bit and the other leader's charisma (the direct leader,
/// 0x45DE00); the stop-attacking (0x4AA4A0) and allow-attacking (0x4AA580) orders reach the hero's whole follower tree and the NPC whose leader chain reaches the PC.
/// </summary>
[TestClass]
public sealed class NpcLeaderJoinCheckTests
{
    private const string Mage = "oid:mage", Summon = "oid:critter:1", Imp = "oid:imp";

    private static GameSessionOwner Create()
    {
        var result = GameSessionOwner.Create(Descriptor());
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void The_follow_check_of_an_npc_led_follower_needs_its_leaders_charisma_and_tests_the_links_forced_bit()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Summon, Mage).Disposition);
        // A reaction at or below the dislike bound refuses an unforced NPC; this one is a forced follower of its NPC leader (the link's bit), which the check accepts before the reaction.
        var facts = PartyStateTests.Facts(reaction: -50);
        var none = owner.JoinParty(owner.Version, Summon, facts, false);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidConsentFacts), (none.Disposition, none.Failure), "the other leader's charisma is named");
        var strong = owner.JoinParty(owner.Version, Summon, facts with { OtherLeaderCharisma = 8 }, false);
        Assert.AreEqual((GameSessionFailure.ConsentRefused, FollowConsent.AlreadyInGroup), (strong.Failure, strong.Consent!.Value), "a leader whose charisma reaches the PC's keeps it");
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
        var joined = owner.JoinParty(owner.Version, Summon, facts with { OtherLeaderCharisma = 7 }, false);
        Assert.AreEqual((GameSessionDisposition.Changed, FollowConsent.Accepted), (joined.Disposition, joined.Consent!.Value), "forced by the NPC link: accepted despite the reaction");
        Assert.AreEqual("pc", owner.QueryLeader(Summon));
        Assert.IsFalse(owner.QueryFollowers(Mage).Contains(Summon), "it left the NPC leader");
        // A free NPC is checked as before: the unforced dislike refuses it.
        Assert.AreEqual(FollowConsent.Dislike, owner.JoinParty(owner.Version, Imp, facts, false).Consent);
    }

    [TestMethod]
    public void Stopping_and_allowing_attacks_reach_the_followers_of_followers_and_every_npc_the_chain_leads_to_the_hero()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Mage).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Summon, Mage).Disposition);
        CollectionAssert.AreEqual(new[] { Mage, Summon }, owner.QueryFollowerTree("pc").ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StopAttacking(owner.Version, new() { Combatant = SessionCombatant.Hero }).Disposition);
        Assert.IsTrue(owner.QueryDanger(Summon).Flags.HasFlag(DangerFlags.NoAttack), "the follower of the hero's follower is stopped too");
        Assert.IsTrue(owner.QueryDanger(Mage).Flags.HasFlag(DangerFlags.NoAttack));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AllowAttacking(owner.Version, SessionCombatant.Critter(Summon)).Disposition, "an NPC whose chain reaches the hero");
        Assert.IsFalse(owner.QueryDanger(Summon).Flags.HasFlag(DangerFlags.NoAttack));
        Assert.IsTrue(owner.QueryDanger(Mage).Flags.HasFlag(DangerFlags.NoAttack));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AllowAttacking(owner.Version, SessionCombatant.Hero).Disposition);
        Assert.IsFalse(owner.QueryDanger(Mage).Flags.HasFlag(DangerFlags.NoAttack));
        // An NPC led by an NPC alone is no PC's: stopping it sets nothing.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, Imp, "oid:orc").Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.StopAttacking(owner.Version, new() { Combatant = SessionCombatant.Critter(Imp) }).Disposition);
        Assert.IsFalse(owner.QueryDanger(Imp).Flags.HasFlag(DangerFlags.NoAttack));
    }
}
