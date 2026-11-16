using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-WORLD-1840 (#1322): any critter leads followers, as in the original (leader handle 0x119 read by 0x45DE00, follower array 0xF1, the chain query 0x45DDA0, the join 0x45DE90 and the leave
/// 0x45DFC0 / 0x45E040 under any leader). The hero's party stays as it was; the followers of NPC leaders sit beside it, a critter has one leader, and they are saved (schema 102).
/// </summary>
[TestClass]
public sealed class NpcLeaderStateTests
{
    private const string Hero = "pc", Mage = "oid:mage", Summon = "oid:critter:1", Imp = "oid:imp", Virgil = "oid:virgil", Orc = "oid:orc";
    private const int TempusFugit = 0x4000000;

    private static GameSessionOwner Create()
    {
        var result = GameSessionOwner.Create(Descriptor());
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Changed(PartyMutationResult result) => Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));

    [TestMethod]
    public void An_npc_leads_its_forced_followers_in_join_order_and_the_chain_reaches_the_hero_through_a_companion()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryLeader(Summon));
        var before = owner.Version.Revision;
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreEqual(before + 1, owner.Version.Revision, "one revision");
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Mage));
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
        CollectionAssert.AreEqual(new[] { Summon, Imp }, owner.QueryFollowers(Mage).ToArray());
        Assert.IsNull(owner.QueryPcLeader(Summon), "no player character at the top of the mage's chain");
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion(Summon).Standing, "the hero does not lead it");
        Assert.AreEqual(0, owner.QueryParty().Followers.Count);
        // The same join again is NoChange; a join under another leader moves it to the end of that array.
        var same = owner.Version;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ForceJoinLeader(owner.Version, Imp, Mage).Disposition);
        Assert.AreEqual(same, owner.Version);
        Changed(owner.ForceJoinLeader(owner.Version, Orc, Virgil));
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Virgil));
        CollectionAssert.AreEqual(new[] { Imp }, owner.QueryFollowers(Mage).ToArray());
        CollectionAssert.AreEqual(new[] { Orc, Summon }, owner.QueryFollowers(Virgil).ToArray());
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        // The mage joins the hero: the chain of its summons now reaches the hero.
        Changed(owner.ForceJoinParty(owner.Version, Mage));
        Assert.AreEqual(Hero, owner.QueryLeader(Mage));
        Assert.AreEqual(Hero, owner.QueryPcLeader(Summon));
        Assert.AreEqual(Hero, owner.QueryPcLeader(Mage));
        Assert.IsNull(owner.QueryPcLeader(Hero), "the chain starts at the leader: the hero itself answers none");
        CollectionAssert.AreEqual(new[] { Mage }, owner.QueryFollowers(Hero).ToArray());
        Assert.AreEqual(0, owner.QueryFollowers(Summon).Count);
    }

    [TestMethod]
    public void A_join_under_an_npc_first_leaves_the_hero_and_a_join_of_the_hero_leaves_the_npc()
    {
        using var owner = Create();
        Changed(owner.ForceJoinParty(owner.Version, Virgil));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Hero, SessionObjectFlagField.Spell, TempusFugit, true).Disposition);
        Changed(owner.ForceJoinParty(owner.Version, Orc));
        Assert.AreNotEqual(0, owner.QueryObjectFlags(Orc).Spell(0) & TempusFugit, "the hero's join copied the flag");
        // Dominated by an NPC: the companion leaves the hero (its copied flag goes) and follows the mage.
        Changed(owner.ForceJoinLeader(owner.Version, Orc, Mage));
        Assert.AreEqual(Mage, owner.QueryLeader(Orc));
        Assert.AreEqual(0, owner.QueryObjectFlags(Orc).Spell(0) & TempusFugit, "the leave half cleared it, the mage holds none");
        CollectionAssert.AreEqual(new[] { Virgil }, owner.QueryParty().Followers.Select(f => f.Entity).ToArray());
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion(Orc).Standing);
        // The hero takes it back: it leaves the mage first.
        Changed(owner.ForceJoinParty(owner.Version, Orc));
        Assert.AreEqual(Hero, owner.QueryLeader(Orc));
        Assert.AreEqual(0, owner.QueryFollowers(Mage).Count);
        Assert.AreEqual(0, owner.QueryParty().NpcLed.Count);
    }

    [TestMethod]
    public void A_join_copies_the_npc_leaders_tempus_fugit_flag_and_the_leave_clears_it()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Mage, SessionObjectFlagField.Spell, TempusFugit, true).Disposition);
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreNotEqual(0, owner.QueryObjectFlags(Summon).Spell(0) & TempusFugit);
        var before = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: true)).Disposition);
        Assert.AreEqual(before + 1, owner.Version.Revision);
        Assert.IsNull(owner.QueryLeader(Summon));
        Assert.AreEqual(0, owner.QueryObjectFlags(Summon).Spell(0) & TempusFugit);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: true)).Disposition, "no leader to leave");
    }

    [TestMethod]
    public void An_unforced_leave_of_an_npc_leader_is_refused_by_the_callers_facts_as_the_heros_is()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreEqual(GameSessionFailure.LeaveRefused, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: false, MindControlled: true)).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: false)).Disposition);
    }

    [TestMethod]
    public void The_hero_follows_no_one_a_critter_does_not_lead_itself_and_no_cycle_is_closed()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, owner.ForceJoinLeader(owner.Version, Hero, Mage).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, owner.ForceJoinLeader(owner.Version, Mage, Mage).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, owner.ForceJoinLeader(owner.Version, "a b", Mage).Failure);
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Summon));
        var before = owner.Version;
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, owner.ForceJoinLeader(before, Mage, Imp).Failure, "the mage under its summon's summon closes a cycle");
        Assert.AreEqual(before, owner.Version);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ForceJoinLeader(new(before.Owner, before.Revision - 1), Orc, Mage).Failure);
    }

    [TestMethod]
    public void A_destroyed_leader_leaves_its_followers_without_one_and_a_destroyed_follower_leaves_its_leader()
    {
        using var owner = Create();
        var made = owner.CreateSummonedCritter(owner.Version, 28488, Exterior, new(5, 5), null, null);
        Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition, made.Failure.ToString());
        var leader = made.Object!.Entity;
        Assert.AreNotEqual(Imp, leader);
        Changed(owner.ForceJoinLeader(owner.Version, Imp, leader));
        Changed(owner.ForceJoinLeader(owner.Version, leader, Mage));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, leader).Disposition);
        Assert.IsNull(owner.QueryLeader(Imp));
        Assert.AreEqual(0, owner.QueryFollowers(Mage).Count);
        Assert.AreEqual(0, owner.QueryParty().NpcLed.Count);
    }

    [TestMethod]
    public void Npc_led_followers_are_saved_in_leader_order_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Orc));
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Changed(owner.ForceJoinLeader(owner.Version, Virgil, Mage));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"npcLed\":[{\"entity\":\"oid:critter:1\",\"leader\":\"oid:mage\",\"forced\":true},{\"entity\":\"oid:virgil\",\"leader\":\"oid:mage\",\"forced\":true}," +
            "{\"entity\":\"oid:imp\",\"leader\":\"oid:orc\",\"forced\":true}]}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        CollectionAssert.AreEqual(new[] { Summon, Virgil }, restored.QueryFollowers(Mage).ToArray());
        // A follower listed twice is not canonical; one the hero leads, the hero, an unforced one, leaders out of order and a cycle are refused by restore.
        var twice = GameSessionSnapshotCodec.TryDecode(text.Replace("\"entity\":\"oid:virgil\",\"leader\"", "\"entity\":\"oid:critter:1\",\"leader\""));
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.party.npcLed[1].entity"), (twice.Failure, twice.Path));
        var saved = owner.Capture();
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, WaitFlagStateTests.RestoreFailure(saved, Party(new SessionNpcFollower(Hero, Mage, true))));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, WaitFlagStateTests.RestoreFailure(saved, Party(new SessionNpcFollower(Summon, Mage, false))));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, WaitFlagStateTests.RestoreFailure(saved, Party(new(Summon, Orc, true), new(Imp, Mage, true))));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, WaitFlagStateTests.RestoreFailure(saved, Party(new(Summon, Imp, true), new(Imp, Summon, true))));
        var heroLed = new SessionPartyState([new(Summon, CompanionStanding.Following, true, false, false)], [], [new(Summon, Mage, true)]);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, WaitFlagStateTests.RestoreFailure(saved, heroLed));
    }

    [TestMethod]
    public void A_schema_101_save_upgrades_with_no_npc_leader_and_the_older_schema_cannot_carry_the_list()
    {
        using var owner = Create();
        Changed(owner.ForceJoinParty(owner.Version, Virgil));
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema101(current);
        StringAssert.Contains(older, "\"schemaVersion\":101,");
        Assert.IsFalse(older.Contains("npcLed", StringComparison.Ordinal));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot!));
        var damaged = GameSessionSnapshotCodec.TryDecode(current.Replace("\"schemaVersion\":104,", "\"schemaVersion\":101,"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.party.npcLed"), (damaged.Failure, damaged.Path));
    }

    // Review finding: a dominated companion keeps its free record (spread out, loyalty marks) next to its NPC link, and the save holds both.
    [TestMethod]
    public void A_spread_out_companion_taken_by_an_npc_keeps_its_free_record_and_the_save_round_trips()
    {
        using var owner = Create();
        Changed(owner.ForceJoinParty(owner.Version, Virgil));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetSpreadOut(owner.Version, Virgil, true).Disposition);
        Changed(owner.ForceJoinLeader(owner.Version, Virgil, Mage));
        Assert.IsTrue(owner.QueryCompanion(Virgil) is { Standing: CompanionStanding.Free, SpreadOut: true });
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        using var restored = Restore(decoded.Snapshot!);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(Mage, restored.QueryLeader(Virgil));
        // A follower of the hero listed again among the NPC-led ones is not canonical.
        Changed(owner.ForceJoinParty(owner.Version, Orc));
        var withOrc = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("\"npcLed\":[{\"entity\":\"oid:virgil\"", "\"npcLed\":[{\"entity\":\"oid:orc\"");
        var twice = GameSessionSnapshotCodec.TryDecode(withOrc);
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.party.npcLed[0].entity"), (twice.Failure, twice.Path));
    }

    [TestMethod]
    public void A_follower_of_a_leader_outside_the_hero_chain_is_not_concealed_and_its_leave_clears_the_wait_bit()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.IsFalse(owner.IsConcealed(SessionCombatant.Critter(Summon)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon).Disposition);
        Assert.IsTrue(owner.QueryCompanion(Summon).WaitFlag);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: true)).Disposition);
        Assert.IsFalse(owner.QueryCompanion(Summon).WaitFlag, "the forced unwait inside the leave");
        Assert.IsNull(owner.QueryWaitTimer(Summon));
        // An unforced leave under an extraordinary-charisma leader is refused.
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreEqual(GameSessionFailure.LeaveRefused, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: false, LeaderCharisma: 20)).Failure);
    }

    // ARC-WORLD-2116 (0x4AA7A0): the wait keeps the leader. The follower leaves the NPC leader's array, keeps the leader handle (so the chain still reaches the hero), carries the wait flag and, with the wait facts,
    // holds the timer of the leader's stats; a forced unwait (the death's) joins it again at the end of the array; the timer's expiry frees it, jilted, with no reaction change.
    [TestMethod]
    public void A_follower_of_an_npc_waits_outside_its_leaders_array_and_keeps_the_leader()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Mage));
        Changed(owner.ForceJoinParty(owner.Version, Mage));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon, new PartyWaitFacts(2, PersuasionTraining.None, 10, false)).Disposition);
        Assert.IsTrue(owner.QueryCompanion(Summon).WaitFlag);
        Assert.IsTrue(owner.QueryCompanion(Summon).WaitBit);
        Assert.AreEqual(Mage, owner.QueryLeader(Summon), "the wait keeps the leader");
        Assert.AreEqual(Hero, owner.QueryPcLeader(Summon), "and the chain still reaches the hero");
        CollectionAssert.AreEqual(new[] { Imp }, owner.QueryFollowers(Mage).ToArray(), "it left the leader's array");
        Assert.IsNotNull(owner.QueryWaitTimer(Summon), "the timer of the wait index 2 (2 h)");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WaitParty(owner.Version, Summon).Disposition, "already waiting");
        // The save keeps it waiting under the leader, with its timer.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        using var restored = Restore(decoded.Snapshot!);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(Mage, restored.QueryLeader(Summon));
        CollectionAssert.AreEqual(new[] { Imp }, restored.QueryFollowers(Mage).ToArray());
        Assert.IsNotNull(restored.QueryWaitTimer(Summon));
        // An unforced unwait with the hero-side facts changes nothing (the leader's own facts: UnwaitNpcLed); the forced one joins the leader again at the end of the array, forced, the flag and the timer gone.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.UnwaitParty(owner.Version, Summon, PartyStateTests.Facts(), force: false).Disposition);
        Assert.IsTrue(owner.QueryCompanion(Summon).WaitFlag);
        Changed(owner.UnwaitParty(owner.Version, Summon, PartyStateTests.Facts(), force: true));
        Assert.IsFalse(owner.QueryCompanion(Summon).WaitFlag);
        Assert.IsNull(owner.QueryWaitTimer(Summon));
        CollectionAssert.AreEqual(new[] { Imp, Summon }, owner.QueryFollowers(Mage).ToArray());
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
    }

    // ARC-WORLD-2127 (0x4AA7A0): the wait timer reads the DIRECT leader's Persuasion and charisma as given, never the hero's side: an extraordinary-charisma NPC leader schedules none, Persuasion training moves the index.
    [TestMethod]
    public void The_wait_timer_of_a_follower_of_an_npc_reads_the_leaders_facts_as_given()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Mage));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon, new PartyWaitFacts(2, PersuasionTraining.None, 20, false)).Disposition);
        Assert.IsNull(owner.QueryWaitTimer(Summon), "charisma 20 is extraordinary: no timer");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Imp, new PartyWaitFacts(5, PersuasionTraining.Apprentice, 10, false)).Disposition);
        Assert.IsNull(owner.QueryWaitTimer(Imp), "wait index 5 plus one for any Persuasion training is 6: no timer");
    }

    private static NpcLeaderUnwaitFacts Leader(int level = 5, int alignment = 0, int maxFollowers = 2, PersuasionTraining persuasion = PersuasionTraining.None, bool mind = false, bool mindAfter = false,
        int followerAlignment = 0, int followerLevel = 1, FollowThresholds? thresholds = null, int charisma = 5) =>
        new(persuasion, alignment, level, maxFollowers, thresholds ?? new(-1000, 2001, 2001, 30), followerAlignment, followerLevel, mind, mindAfter, charisma);

    private static GameSessionOwner WaitingUnder(string leader, params string[] others)
    {
        var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, leader));
        foreach (var other in others) Changed(owner.ForceJoinLeader(owner.Version, other, leader));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon, new PartyWaitFacts(2, PersuasionTraining.None, 10, false)).Disposition);
        return owner;
    }

    // ARC-WORLD-2127 (0x4AA8C0, 0x4AD950): the unforced unwait joins the leader again at the end of its array, not forced, jilted cleared, the flag and the timer gone, one revision.
    [TestMethod]
    public void An_unforced_unwait_under_an_npc_leader_passes_the_follow_check_and_joins_again_not_forced()
    {
        using var owner = WaitingUnder(Mage, Imp);
        var before = owner.Version.Revision;
        var result = owner.UnwaitNpcLed(owner.Version, Summon, Leader());
        Changed(result);
        Assert.AreEqual(FollowConsent.Accepted, result.Consent);
        Assert.AreEqual(before + 1, owner.Version.Revision);
        Assert.IsTrue(owner.QueryCompanion(Summon) is { WaitFlag: false, Jilted: false });
        Assert.IsNull(owner.QueryWaitTimer(Summon));
        CollectionAssert.AreEqual(new[] { Imp, Summon }, owner.QueryFollowers(Mage).ToArray());
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
        // Not forced now: it counts against the leader's limit, as the forced Imp does not.
        Assert.AreEqual(GameSessionFailure.NotWaiting, owner.UnwaitNpcLed(owner.Version, Summon, Leader()).Failure, "no longer waiting");
    }

    [TestMethod]
    public void A_refused_follow_check_of_the_unforced_unwait_changes_nothing()
    {
        // Each refusal reads the leader's side: level (the follower's level at or above the leader's plus the gate), the alignment gap, the limit.
        foreach (var (facts, expected) in new[]
        {
            (Leader(level: 1, followerLevel: 5, thresholds: new(-1000, 2001, 2001, 0)), FollowConsent.LowLevel),
            (Leader(alignment: 500, followerAlignment: 0, thresholds: new(-1000, 100, 2001, 30)), FollowConsent.TooGood),
            (Leader(alignment: 0, followerAlignment: 500, thresholds: new(-1000, 2001, 100, 30)), FollowConsent.TooBad),
            (Leader(maxFollowers: 0), FollowConsent.NotAllowed),
        })
        {
            using var owner = WaitingUnder(Mage);
            var version = owner.Version;
            var result = owner.UnwaitNpcLed(version, Summon, facts);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ConsentRefused, expected), (result.Disposition, result.Failure, result.Consent));
            Assert.AreEqual(version, owner.Version, "nothing changed");
            Assert.IsTrue(owner.QueryCompanion(Summon).WaitFlag);
            Assert.IsNotNull(owner.QueryWaitTimer(Summon));
            Assert.AreEqual(Mage, owner.QueryLeader(Summon));
        }
    }

    [TestMethod]
    public void The_unforced_unwait_counts_the_leaders_non_forced_followers_and_reads_a_master_persuaders_limit_only()
    {
        using var owner = WaitingUnder(Mage, Imp);
        Changed(owner.UnwaitNpcLed(owner.Version, Summon, Leader(maxFollowers: 1)));
        // Summon is now a non-forced follower; a second waiting follower finds the limit of 1 reached.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Imp, new PartyWaitFacts(2, PersuasionTraining.None, 10, false)).Disposition);
        var refused = owner.UnwaitNpcLed(owner.Version, Imp, Leader(maxFollowers: 1));
        Assert.AreEqual((GameSessionFailure.ConsentRefused, FollowConsent.LimitReached), (refused.Failure, refused.Consent));
        var none = owner.UnwaitNpcLed(owner.Version, Imp, Leader(maxFollowers: 0));
        Assert.AreEqual((GameSessionFailure.ConsentRefused, FollowConsent.NotAllowed), (none.Failure, none.Consent));
        // A Master persuader skips the middle steps (level here) but never the limits.
        Assert.AreEqual(FollowConsent.LowLevel, owner.UnwaitNpcLed(owner.Version, Imp, Leader(level: 1, followerLevel: 5, maxFollowers: 3, thresholds: new(-1000, 2001, 2001, 0))).Consent);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UnwaitNpcLed(owner.Version, Imp, Leader(level: 1, followerLevel: 5, maxFollowers: 3, persuasion: PersuasionTraining.Master, thresholds: new(-1000, 2001, 2001, 0))).Disposition);
    }

    // ARC-WORLD-2148 (0x415BA0 case 0x15): the dialog UW's own check 0x4AD950(NPC, hero, argument) runs first for a speaker an NPC leads, with the hero as the player and L as the other leader: L's charisma
    // not below the hero's refuses it (already in a group) and nothing changes; then the reaction, alignment gap, level and the hero's follower limit are the hero's; Persuasion Master skips the middle steps.
    [TestMethod]
    public void The_dialog_outer_check_of_a_speaker_an_npc_leads_compares_the_leaders_charisma_with_the_heros()
    {
        using var owner = WaitingUnder(Mage);
        var version = owner.Version;
        var heroCharisma = owner.QueryHeroPartySide()?.Charisma ?? PartyStateTests.Facts().PcCharisma;
        var refused = owner.UnwaitNpcLed(version, Summon, Leader(charisma: heroCharisma), PartyStateTests.Facts());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ConsentRefused, FollowConsent.AlreadyInGroup), (refused.Disposition, refused.Failure, refused.Consent));
        Assert.AreEqual(version, owner.Version, "a refused outer check changes nothing");
        Assert.IsTrue(owner.QueryCompanion(Summon).WaitFlag);
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
        // A leader of lower charisma lets the outer check go on; then the leader's own check unwaits it as before.
        var accepted = owner.UnwaitNpcLed(owner.Version, Summon, Leader(charisma: heroCharisma - 1), PartyStateTests.Facts());
        Changed(accepted);
        Assert.IsFalse(owner.QueryCompanion(Summon).WaitFlag);
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
    }

    [TestMethod]
    public void The_dialog_outer_check_reads_the_heros_side_after_the_leader_step()
    {
        // The hero-side thresholds refuse before the leader's own check would: reaction at or below the dislike value.
        using var owner = WaitingUnder(Mage);
        var version = owner.Version;
        var dislike = PartyStateTests.Facts() with { Reaction = -20 };
        var refused = owner.UnwaitNpcLed(version, Summon, Leader(charisma: 1), dislike);
        Assert.AreEqual((GameSessionFailure.ConsentRefused, FollowConsent.Dislike), (refused.Failure, refused.Consent));
        Assert.AreEqual(version, owner.Version);
        // Facts that name the other leader's charisma (the dialog host does, ARC-WORLD-2135) are overridden by the leader's own facts: the named 100 would refuse (the hero's is 8), the leader's own 1 does not.
        Changed(owner.UnwaitNpcLed(owner.Version, Summon, Leader(charisma: 1), PartyStateTests.Facts() with { OtherLeaderCharisma = 100 }));
        Assert.IsFalse(owner.QueryCompanion(Summon).WaitFlag);
    }

    // The join's second check runs with the handle cleared: a mind-controlled follower accepted through the chain of the first check is refused there when nothing else lets it pass, and ends with no
    // leader, not waiting, not jilted, its timer gone.
    [TestMethod]
    public void A_refusal_of_the_join_after_the_unwait_leaves_the_follower_free_and_not_waiting()
    {
        using var owner = WaitingUnder(Mage);
        var result = owner.UnwaitNpcLed(owner.Version, Summon, Leader(level: 1, followerLevel: 5, mind: true, mindAfter: false, thresholds: new(-1000, 2001, 2001, 0)));
        Assert.AreEqual((GameSessionDisposition.Changed, FollowConsent.LowLevel), (result.Disposition, result.Consent));
        Assert.IsNull(owner.QueryLeader(Summon));
        Assert.IsTrue(owner.QueryCompanion(Summon) is { WaitFlag: false, Jilted: false });
        Assert.IsNull(owner.QueryWaitTimer(Summon));
        Assert.AreEqual(0, owner.QueryFollowers(Mage).Count);
    }

    [TestMethod]
    public void The_unforced_unwait_refuses_a_follower_not_waiting_under_an_npc_and_bad_input()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.NotWaiting, owner.UnwaitNpcLed(owner.Version, Summon, Leader()).Failure);
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreEqual(GameSessionFailure.NotWaiting, owner.UnwaitNpcLed(owner.Version, Summon, Leader()).Failure, "following, not waiting");
        Assert.AreEqual(GameSessionFailure.InvalidConsentFacts, owner.UnwaitNpcLed(owner.Version, Summon, Leader(persuasion: (PersuasionTraining)9)).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.UnwaitNpcLed(new(owner.Version.Owner, owner.Version.Revision + 5), Summon, Leader()).Failure);
    }

    [TestMethod]
    public void The_timer_of_a_follower_waiting_under_an_npc_frees_it_jilted_with_no_reaction_change()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon, new PartyWaitFacts(0, PersuasionTraining.None, 10, false)).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 2, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var pass = owner.ProcessEvents(new PartyWorld());
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(1, pass.Waits.Count);
        Assert.IsNull(pass.Waits[0].Reaction, "the reaction change is written only toward a player character");
        Assert.IsNull(owner.QueryLeader(Summon), "the NPC lost its leader");
        Assert.IsTrue(owner.QueryCompanion(Summon) is { WaitFlag: false, Jilted: true, Standing: CompanionStanding.Free });
        Assert.AreEqual(0, owner.QueryFollowers(Mage).Count);
    }

    // Review finding: the hero's join of a follower waiting under an NPC leaves that leader first (the forced unwait inside the leave), so no flag and no timer are left on the new follower; the death's forced unwait joins the leader again.
    [TestMethod]
    public void The_hero_taking_a_waiting_npc_follower_clears_its_wait_and_the_death_rejoins_its_leader()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Mage));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon, new PartyWaitFacts(2, PersuasionTraining.None, 10, false)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Imp, new PartyWaitFacts(2, PersuasionTraining.None, 10, false)).Disposition);
        Changed(owner.ForceJoinParty(owner.Version, Summon));
        Assert.IsTrue(owner.QueryCompanion(Summon) is { Standing: CompanionStanding.Following, WaitFlag: false });
        Assert.IsNull(owner.QueryWaitTimer(Summon));
        Assert.IsNull(owner.QueryLeader(Mage), "the mage leads no one else's chain");
        Assert.AreEqual(Hero, owner.QueryLeader(Summon));
        // The death of the other waiter (the kill notification's forced unwait) puts it back in its NPC leader's array.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Critter(Imp, new LifeFacts(), LifeState.Unharmed)).Disposition);
        Assert.IsTrue(owner.QueryCompanion(Imp) is { WaitFlag: false });
        Assert.IsNull(owner.QueryWaitTimer(Imp));
        Assert.AreEqual(Mage, owner.QueryLeader(Imp));
        CollectionAssert.AreEqual(new[] { Imp }, owner.QueryFollowers(Mage).ToArray());
    }

    [TestMethod]
    public void A_join_under_an_npc_leader_clears_the_wait_flag_a_free_npc_carried()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Summon).Disposition);
        Assert.IsTrue(owner.QueryCompanion(Summon).WaitFlag, "a leaderless wait keeps the flag");
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.IsFalse(owner.QueryCompanion(Summon).WaitFlag, "under an NPC leader the flag means a wait outside the array");
        CollectionAssert.AreEqual(new[] { Summon }, owner.QueryFollowers(Mage).ToArray());
    }

    // A host that answers life facts only and no reaction facts: a wait timeout under an NPC reads none.
    private sealed class PartyWorld : ISessionPartyEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => new();
        public ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => null;
        public ReactionPcFacts? ReactionPcFacts() => null;
    }

    private static SessionPartyState Party(params SessionNpcFollower[] led) => new([], [], led);

    // ARC-WORLD-2130 (0x4AC380 after 0x45DFC0): an NPC-led follower that leaves through the loyalty step is free and jilted; a leave without the jilt flag leaves no mark, as before.
    [TestMethod]
    public void A_jilting_leave_of_an_npc_led_follower_marks_it_jilted_and_a_refused_one_changes_nothing()
    {
        using var owner = Create();
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        var refused = owner.Version;
        Assert.AreEqual(GameSessionFailure.LeaveRefused, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: false, LeaderCharisma: 20, Jilt: true)).Failure);
        Assert.AreEqual(refused, owner.Version);
        Assert.AreEqual(Mage, owner.QueryLeader(Summon));
        Assert.IsFalse(owner.QueryCompanion(Summon).Jilted);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Summon, new PartyLeave(Force: false, LeaderCharisma: 19, Jilt: true)).Disposition);
        Assert.IsNull(owner.QueryLeader(Summon));
        Assert.IsTrue(owner.QueryCompanion(Summon).Jilted);
        Changed(owner.ForceJoinLeader(owner.Version, Imp, Mage));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Imp, new PartyLeave(Force: true)).Disposition);
        Assert.IsFalse(owner.QueryCompanion(Imp).Jilted, "no jilt without the flag");
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.IsFalse(owner.QueryCompanion(Summon).Jilted, "a join clears the mark");
    }

    // ARC-WORLD-2130: the link query answers the leader and the forced bit of an NPC-led follower, and null for a critter no NPC leads (the hero's follower included).
    [TestMethod]
    public void The_npc_link_query_answers_the_leader_and_the_forced_bit()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryNpcLink(Summon));
        Changed(owner.ForceJoinLeader(owner.Version, Summon, Mage));
        Assert.AreEqual(new SessionNpcFollower(Summon, Mage, true), owner.QueryNpcLink(Summon));
        Changed(owner.ForceJoinParty(owner.Version, Virgil));
        Assert.IsNull(owner.QueryNpcLink(Virgil), "the hero's follower has no NPC link");
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryNpcLink(""));
    }
}
