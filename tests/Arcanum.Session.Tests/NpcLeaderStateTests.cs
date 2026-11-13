using Arcanum.Simulation.Party;
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
        var damaged = GameSessionSnapshotCodec.TryDecode(current.Replace("\"schemaVersion\":102,", "\"schemaVersion\":101,"));
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
    public void A_follower_of_an_npc_is_never_concealed_and_its_leave_clears_the_wait_bit()
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

    private static SessionPartyState Party(params SessionNpcFollower[] led) => new([], [], led);
}
