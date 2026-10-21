using Arcanum.Simulation.Character;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-067: the session hero as the PC side of the follow check, the orders, the loyalty step, the wait timer and the leave guard.
// Heroes and thresholds are invented; the follow check is the ledger's AP rows (FollowConsentRules).
[TestClass]
public sealed class HeroPartySideTests
{
    private const int Persuasion = 11;
    // The fixtures' session clock starts at 15:00.
    private const long T0 = 54_000_000;

    private static GameSessionOwner Hero(int charisma = 8, int persuasionTraining = 0, int alignment = 0)
    {
        var stats = Defaults();
        stats[(int)CharacterStat.Charisma] = charisma;
        var skills = new int[12];
        skills[Persuasion] = persuasionTraining << 6;
        var hero = HalfElf(stats) with
        {
            Alignment = alignment,
            Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. skills] },
        };
        return CreateWith(hero);
    }

    // The caller's facts as the diagnostic viewer gives them: charisma 8, level 1, no Persuasion, alignment 0, max followers 2.
    private static PartyConsentFacts Diagnostic(int npcAlignment = 0, int npcLevel = 1, FollowThresholds? thresholds = null) =>
        PartyStateTests.Facts() with { NpcAlignment = npcAlignment, NpcLevel = npcLevel, Thresholds = thresholds ?? PartyStateTests.Facts().Thresholds };

    [TestMethod]
    public void The_hero_is_the_party_side_and_none_without_a_hero()
    {
        using (var owner = Hero(charisma: 13, persuasionTraining: 2, alignment: -30))
        {
            var side = owner.QueryHeroPartySide()!;
            // Charisma 13 / 4 = 3, +1 from expert Persuasion: max followers 4.
            Assert.AreEqual(new SessionPartySide(PersuasionTraining.Expert, 13, 1, -30, 4), side);
        }
        using (var plain = Hero(charisma: 12))
            Assert.AreEqual(3, plain.QueryHeroPartySide()!.MaxFollowers, "12 / 4, no Persuasion bonus below expert");
        using var none = Create();
        Assert.IsNull(none.QueryHeroPartySide());
    }

    [TestMethod]
    public void The_follow_check_reads_the_heros_charisma_limit_not_the_callers()
    {
        // The caller says charisma 8 (max followers 2); the hero has charisma 12 (max 3), so a third follower is admitted.
        using var owner = Hero(charisma: 12);
        foreach (var entity in new[] { "a", "b", "c" })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, entity, Diagnostic(), false).Disposition, entity);
        var fourth = owner.JoinParty(owner.Version, "d", Diagnostic(), false);
        Assert.AreEqual(FollowConsent.LimitReached, fourth.Consent);
        // Expert Persuasion adds one: charisma 12 → 4 followers.
        using var expert = Hero(charisma: 12, persuasionTraining: 2);
        foreach (var entity in new[] { "a", "b", "c", "d" })
            Assert.AreEqual(GameSessionDisposition.Changed, expert.JoinParty(expert.Version, entity, Diagnostic(), false).Disposition, entity);
    }

    [TestMethod]
    public void The_follow_check_reads_the_heros_level_and_alignment()
    {
        // An NPC whose low-level parameter refuses a PC 5 levels below it: the hero's level 1 decides, not a caller's level 20.
        var lowLevel = Diagnostic(npcLevel: 6, thresholds: PartyStateTests.Facts().Thresholds with { LevelGate = 5 }) with { PcLevel = 20 };
        using (var owner = Hero())
            Assert.AreEqual(FollowConsent.LowLevel, owner.JoinParty(owner.Version, "npc", lowLevel, false).Consent);
        // "Too good": the hero's alignment 300 against an NPC at 0 with a gap of 200 refuses, whatever alignment the caller gives.
        var good = Diagnostic(thresholds: PartyStateTests.Facts().Thresholds with { MaxAlignmentAbove = 200 }) with { PcAlignment = 0 };
        using (var saint = Hero(alignment: 300))
            Assert.AreEqual(FollowConsent.TooGood, saint.JoinParty(saint.Version, "npc", good, false).Consent);
        using (var plain = Hero(alignment: 0))
            Assert.AreEqual(GameSessionDisposition.Changed, plain.JoinParty(plain.Version, "npc", good, false).Disposition);
    }

    [TestMethod]
    public void An_unforced_leave_is_refused_under_a_hero_of_charisma_twenty()
    {
        // The leave guard (extraordinary charisma, 20 and above) reads the hero's charisma, whatever the caller passes.
        foreach (var (charisma, refused) in new[] { (19, false), (20, true) })
        {
            using var owner = Hero(charisma: charisma);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "npc", Diagnostic(), false).Disposition);
            var leave = owner.LeaveParty(owner.Version, "npc", new PartyLeave(Force: false, LeaderCharisma: 0));
            Assert.AreEqual(refused ? GameSessionFailure.LeaveRefused : GameSessionFailure.None, leave.Failure, $"charisma {charisma}");
        }
    }

    [TestMethod]
    public void The_wait_timer_takes_the_persuasion_step_from_the_heros_training()
    {
        // Wait index 1 is 15 minutes; any Persuasion training of the leader adds one step (2 hours), read from the hero, not the caller.
        long Deadline(int training)
        {
            using var owner = Hero(persuasionTraining: training);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "npc", Diagnostic(), false).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "npc", new PartyWaitFacts(1, PersuasionTraining.None, 8, false)).Disposition);
            return owner.QueryWaitTimer("npc")!.Value - T0;
        }
        Assert.AreEqual(CompanionLoyaltyRules.WaitDurationsMs[1], Deadline(0));
        Assert.AreEqual(CompanionLoyaltyRules.WaitDurationsMs[2], Deadline(1));
        // A hero of charisma 20 or more leaves no timer at all.
        using var grand = Hero(charisma: 20);
        grand.JoinParty(grand.Version, "npc", Diagnostic(), false);
        grand.WaitParty(grand.Version, "npc", new PartyWaitFacts(1, PersuasionTraining.None, 8, false));
        Assert.IsNull(grand.QueryWaitTimer("npc"));
    }

    [TestMethod]
    public void An_alignment_change_writes_the_hero_and_arms_its_followers()
    {
        using var owner = Hero(alignment: 100);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "npc", Diagnostic(), false).Disposition);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ChangeHeroAlignment(owner.Version, 150).Disposition);
        Assert.AreEqual((150, revision + 1), (owner.QueryHero()!.Alignment, owner.Version.Revision));
        Assert.AreEqual(CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood, owner.QueryCompanion("npc").Loyalty);
        // The stat's range bounds it; the same value changes nothing.
        owner.ChangeHeroAlignment(owner.Version, -5000);
        Assert.AreEqual(SessionHeroLimits.MinAlignment, owner.QueryHero()!.Alignment);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ChangeHeroAlignment(owner.Version, SessionHeroLimits.MinAlignment).Disposition);
        // It survives save and load (the hero's alignment is saved).
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(SessionHeroLimits.MinAlignment, restored.QueryHero()!.Alignment);
        using var none = Create();
        Assert.AreEqual(GameSessionFailure.InvalidHero, none.ChangeHeroAlignment(none.Version, 10).Failure);
    }
}
