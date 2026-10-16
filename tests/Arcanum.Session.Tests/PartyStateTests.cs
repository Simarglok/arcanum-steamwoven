using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class PartyStateTests
{
    // A PC with max followers 2 asking an NPC on a made-up AI row; ConsentFacts below varies one input per refusal.
    internal static PartyConsentFacts Facts(int maxFollowers = 2, int reaction = 50) => new(
        MindControlledByPc: false, PcPersuasion: PersuasionTraining.None, PcCharisma: 8, PcAlignment: 0, PcLevel: 3,
        OtherLeaderCharisma: null, Reaction: reaction, Thresholds: new(-20, 300, 200, 5), NpcAlignment: 0, NpcLevel: 3,
        MaxFollowers: maxFollowers);

    private static SessionCompanion Following(string entity, bool forced = false, bool spreadOut = false) =>
        new(entity, CompanionStanding.Following, forced, spreadOut, false);

    private static void Joined(PartyMutationResult result, GameSessionOwner owner, long revision, FollowConsent? consent = FollowConsent.Accepted)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.AreEqual(consent, result.Consent);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
    }

    private static void Refused(PartyMutationResult result, GameSessionOwner owner, GameSessionFailure failure, long revision, FollowConsent? consent = null)
    {
        Assert.AreEqual(GameSessionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(failure, result.Failure);
        Assert.AreEqual(consent, result.Consent);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    [TestMethod]
    public void JoinAppendsInOrderAndARejoinMovesToTheEndWithoutADuplicate()
    {
        using var owner = Create();
        Assert.AreEqual(new SessionCompanion("virgil", CompanionStanding.Free, false, false, false), owner.QueryCompanion("virgil"));
        Assert.IsFalse(owner.QueryCompanion("virgil").LedByPc);

        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 1);
        Joined(owner.JoinParty(owner.Version, "sogg", Facts(maxFollowers: 3), false), owner, 2);
        CollectionAssert.AreEqual(new[] { Following("virgil"), Following("sogg") }, owner.QueryParty().Followers.ToArray());
        Assert.IsTrue(owner.QueryCompanion("virgil").LedByPc);

        // The last follower joining again changes nothing; an earlier one leaves and is re-appended.
        var again = owner.JoinParty(owner.Version, "sogg", Facts(maxFollowers: 3), false);
        Assert.AreEqual(GameSessionDisposition.NoChange, again.Disposition);
        Assert.AreEqual(FollowConsent.Accepted, again.Consent);
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(maxFollowers: 3), false), owner, 3);
        CollectionAssert.AreEqual(new[] { Following("sogg"), Following("virgil") }, owner.QueryParty().Followers.ToArray());
        Assert.AreEqual(2, owner.QueryParty().NonForcedFollowers);
    }

    [TestMethod]
    public void RefusalCarriesTheReasonAndChangesNothing()
    {
        using var owner = Create();
        Refused(owner.JoinParty(owner.Version, "virgil", Facts(reaction: -20), false), owner, GameSessionFailure.ConsentRefused, 0, FollowConsent.Dislike);
        Assert.AreEqual(0, owner.QueryParty().Followers.Count);

        Joined(owner.JoinParty(owner.Version, "a", Facts(maxFollowers: 1), false), owner, 1);
        Refused(owner.JoinParty(owner.Version, "b", Facts(maxFollowers: 1), false), owner, GameSessionFailure.ConsentRefused, 1, FollowConsent.LimitReached);
        // JO with a nonzero first operand ignores the limit and joins forced; forced followers do not count.
        Joined(owner.JoinParty(owner.Version, "b", Facts(maxFollowers: 1), true), owner, 2);
        Assert.AreEqual(Following("b", forced: true), owner.QueryCompanion("b"));
        Assert.AreEqual(1, owner.QueryParty().NonForcedFollowers);
        // As read in the source, the NPC already in the array counts toward the limit itself.
        Refused(owner.JoinParty(owner.Version, "a", Facts(maxFollowers: 1), false), owner, GameSessionFailure.ConsentRefused, 2, FollowConsent.LimitReached);
        // A forced follower passes the check; an unforced join then makes it unforced (the source leaves first).
        Joined(owner.JoinParty(owner.Version, "b", Facts(maxFollowers: 1, reaction: -50), false), owner, 3);
        Assert.AreEqual(Following("b"), owner.QueryCompanion("b"));
    }

    [TestMethod]
    public void ScriptFollowJoinsForcedWithoutACheck()
    {
        using var owner = Create();
        Joined(owner.ForceJoinParty(owner.Version, "golem"), owner, 1, consent: null);
        Assert.AreEqual(Following("golem", forced: true), owner.QueryCompanion("golem"));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ForceJoinParty(owner.Version, "golem").Disposition);
        Changed(owner.WaitParty(owner.Version, "golem"), owner, 2);
        Joined(owner.ForceJoinParty(owner.Version, "golem"), owner, 3, consent: null);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion("golem").Standing);
    }

    [TestMethod]
    public void WaitingKeepsTheLeaderOutsideTheArrayAndUnwaitRechecks()
    {
        using var owner = Create();
        Rejected(owner.WaitParty(owner.Version, "virgil"), owner, GameSessionFailure.NotInParty, 0);
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), true), owner, 1);
        Joined(owner.JoinParty(owner.Version, "sogg", Facts(), false), owner, 2);
        Changed(owner.WaitParty(owner.Version, "virgil"), owner, 3);
        NoChangeAt(owner.WaitParty(owner.Version, "virgil"), owner, 3);

        var waiting = owner.QueryCompanion("virgil");
        Assert.AreEqual(new SessionCompanion("virgil", CompanionStanding.Waiting, false, false, false), waiting, "wait is a forced leave: forced cleared");
        Assert.IsTrue(waiting.LedByPc, "FO / FOLLOWING stay true while waiting");
        CollectionAssert.AreEqual(new[] { Following("sogg") }, owner.QueryParty().Followers.ToArray());
        CollectionAssert.AreEqual(new[] { waiting }, owner.QueryParty().Others.ToArray());

        Refused(owner.UnwaitParty(owner.Version, "sogg", Facts(), false), owner, GameSessionFailure.NotWaiting, 3);
        Refused(owner.UnwaitParty(owner.Version, "virgil", Facts(maxFollowers: 1), false), owner, GameSessionFailure.ConsentRefused, 3, FollowConsent.LimitReached);
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("virgil").Standing, "a refusal keeps it waiting");
        Joined(owner.UnwaitParty(owner.Version, "virgil", Facts(), false), owner, 4);
        CollectionAssert.AreEqual(new[] { Following("sogg"), Following("virgil") }, owner.QueryParty().Followers.ToArray());
        Assert.AreEqual(0, owner.QueryParty().Others.Count);
    }

    [TestMethod]
    public void LeaveFreesAndOnlyAForcedLeaveOvercomesExtraordinaryCharismaOrMindControl()
    {
        using var owner = Create();
        NoChangeAt(owner.LeaveParty(owner.Version, "virgil", new(Force: true)), owner, 0);
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), true), owner, 1);
        Rejected(owner.LeaveParty(owner.Version, "virgil", new(Force: false, LeaderCharisma: 20)), owner, GameSessionFailure.LeaveRefused, 1);
        Rejected(owner.LeaveParty(owner.Version, "virgil", new(Force: false, LeaderCharisma: 8, MindControlled: true)), owner, GameSessionFailure.LeaveRefused, 1);
        Changed(owner.LeaveParty(owner.Version, "virgil", new(Force: false, LeaderCharisma: 19)), owner, 2);
        Assert.AreEqual(new SessionCompanion("virgil", CompanionStanding.Free, false, false, false), owner.QueryCompanion("virgil"));
        Assert.AreEqual(0, owner.QueryParty().Followers.Count + owner.QueryParty().Others.Count, "a free NPC without flags has no record");

        // A waiting NPC can leave too; a jilting leave keeps the mark until the next join clears it.
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 3);
        Changed(owner.WaitParty(owner.Version, "virgil"), owner, 4);
        Changed(owner.LeaveParty(owner.Version, "virgil", new(Force: true, LeaderCharisma: 25, Jilt: true)), owner, 5);
        Assert.AreEqual(new SessionCompanion("virgil", CompanionStanding.Free, false, false, true), owner.QueryCompanion("virgil"));
        Assert.IsFalse(owner.QueryCompanion("virgil").LedByPc);
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 6);
        Assert.IsFalse(owner.QueryCompanion("virgil").Jilted);
    }

    [TestMethod]
    public void SpreadOutIsKeptAcrossStandings()
    {
        using var owner = Create();
        Changed(owner.SetSpreadOut(owner.Version, "virgil", true), owner, 1);
        NoChangeAt(owner.SetSpreadOut(owner.Version, "virgil", true), owner, 1);
        Assert.AreEqual(new SessionCompanion("virgil", CompanionStanding.Free, false, true, false), owner.QueryParty().Others.Single());
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 2);
        Joined(owner.JoinParty(owner.Version, "sogg", Facts(), false), owner, 3);
        Changed(owner.SetSpreadOut(owner.Version, "virgil", false), owner, 4);
        CollectionAssert.AreEqual(new[] { Following("virgil"), Following("sogg") }, owner.QueryParty().Followers.ToArray(), "a flag change keeps the order");
        Changed(owner.SetSpreadOut(owner.Version, "virgil", true), owner, 5);
        Changed(owner.WaitParty(owner.Version, "virgil"), owner, 6);
        Assert.IsTrue(owner.QueryCompanion("virgil").SpreadOut);
    }

    [TestMethod]
    public void InvalidStaleAndOverBudgetRequestsChangeNothing()
    {
        using var owner = Create(new GameSessionLimits(maxCompanions: 2));
        var stale = owner.Version;
        Refused(owner.JoinParty(owner.Version, "bad id", Facts(), false), owner, GameSessionFailure.InvalidCompanion, 0);
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.JoinParty(owner.Version, "virgil", null!, false));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.UnwaitParty(owner.Version, "virgil", null!, false));
        Refused(owner.JoinParty(owner.Version, "virgil", Facts() with { PcPersuasion = (PersuasionTraining)9 }, false), owner, GameSessionFailure.InvalidConsentFacts, 0);
        Refused(owner.UnwaitParty(owner.Version, "", Facts(), false), owner, GameSessionFailure.InvalidCompanion, 0);
        Refused(owner.ForceJoinParty(owner.Version, new string('x', 129)), owner, GameSessionFailure.InvalidCompanion, 0);
        Rejected(owner.WaitParty(owner.Version, "a b"), owner, GameSessionFailure.InvalidCompanion, 0);
        Rejected(owner.LeaveParty(owner.Version, "a b", new(true)), owner, GameSessionFailure.InvalidCompanion, 0);
        Rejected(owner.SetSpreadOut(owner.Version, "a b", true), owner, GameSessionFailure.InvalidCompanion, 0);
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.LeaveParty(owner.Version, "virgil", null!));
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryCompanion("a b"));

        Joined(owner.JoinParty(owner.Version, "a", Facts(maxFollowers: 7), false), owner, 1);
        Refused(owner.JoinParty(stale, "b", Facts(maxFollowers: 7), false), owner, GameSessionFailure.StaleVersion, 1);
        Rejected(owner.WaitParty(stale, "a"), owner, GameSessionFailure.StaleVersion, 1);
        Rejected(owner.SetSpreadOut(stale, "a", true), owner, GameSessionFailure.StaleVersion, 1);
        Joined(owner.JoinParty(owner.Version, "b", Facts(maxFollowers: 7), false), owner, 2);
        Refused(owner.JoinParty(owner.Version, "c", Facts(maxFollowers: 7), false), owner, GameSessionFailure.BudgetExceeded, 2, FollowConsent.Accepted);
        Refused(owner.ForceJoinParty(owner.Version, "c"), owner, GameSessionFailure.BudgetExceeded, 2);
        Rejected(owner.SetSpreadOut(owner.Version, "c", true), owner, GameSessionFailure.BudgetExceeded, 2);
        // Existing records may still change at capacity.
        Changed(owner.SetSpreadOut(owner.Version, "a", true), owner, 3);
        Changed(owner.WaitParty(owner.Version, "b"), owner, 4);
    }

    [TestMethod]
    public void PartySurvivesCaptureEncodeAndRestore()
    {
        using var owner = Create();
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 1);
        Joined(owner.ForceJoinParty(owner.Version, "golem"), owner, 2, consent: null);
        Joined(owner.JoinParty(owner.Version, "sogg", Facts(maxFollowers: 3), false), owner, 3);
        Changed(owner.SetSpreadOut(owner.Version, "golem", true), owner, 4);
        Changed(owner.WaitParty(owner.Version, "sogg"), owner, 5);
        Changed(owner.LeaveParty(owner.Version, "virgil", new(true, Jilt: true)), owner, 6);
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 7);
        Changed(owner.LeaveParty(owner.Version, "virgil", new(true, Jilt: true)), owner, 8);
        Joined(owner.JoinParty(owner.Version, "ari", Facts(), false), owner, 9);

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"party\":{\"followers\":[{\"entity\":\"golem\",\"forced\":true,\"spreadOut\":true}," +
            "{\"entity\":\"ari\",\"forced\":false,\"spreadOut\":false}]," +
            "\"others\":[{\"entity\":\"sogg\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false}," +
            "{\"entity\":\"virgil\",\"standing\":\"Free\",\"spreadOut\":false,\"jilted\":true}]}" + NoHero + EmptyInventory + FreshRandom + NoLife + NoEvents + ",\"lighting\":null}");

        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryParty().Followers.ToArray(), restored.QueryParty().Followers.ToArray());
        CollectionAssert.AreEqual(owner.QueryParty().Others.ToArray(), restored.QueryParty().Others.ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // The restored array keeps working: unwaiting appends after the saved followers.
        Joined(restored.UnwaitParty(restored.Version, "sogg", Facts(maxFollowers: 3), false), restored, 10);
        Assert.AreEqual("sogg", restored.QueryParty().Followers[^1].Entity);
    }

    [TestMethod]
    public void RestoreRefusesPartyRecordsOverTheCallersLimit()
    {
        using var owner = Create();
        Joined(owner.JoinParty(owner.Version, "a", Facts(), false), owner, 1);
        Joined(owner.JoinParty(owner.Version, "b", Facts(), false), owner, 2);
        var result = GameSessionOwner.Restore(owner.Capture(), RestoreOptions(new GameSessionLimits(maxCompanions: 1)));
        Assert.IsNull(result.Owner);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, result.Failure);
    }

    [TestMethod]
    public void DecodeRefusesPartyRecordsTheTransitionsCannotProduce()
    {
        void Fails(string party, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(EmptyParty, party));
            Assert.IsNull(result.Snapshot, party);
            Assert.AreEqual(failure, result.Failure, party);
            Assert.AreEqual(path, result.Path, party);
        }

        const string F = "{\"entity\":\"a\",\"forced\":false,\"spreadOut\":false}";
        Fails(",\"party\":{\"followers\":[" + F + "," + F + "],\"others\":[]}", SnapshotDecodeFailure.NotCanonical, "$.party.followers[1].entity");
        Fails(",\"party\":{\"followers\":[" + F + "],\"others\":[{\"entity\":\"a\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false}]}",
            SnapshotDecodeFailure.NotCanonical, "$.party.others[0].entity");
        Fails(",\"party\":{\"followers\":[],\"others\":[{\"entity\":\"b\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false}," +
            "{\"entity\":\"a\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false}]}", SnapshotDecodeFailure.NotCanonical, "$.party.others[1].entity");
        Fails(",\"party\":{\"followers\":[],\"others\":[{\"entity\":\"a\",\"standing\":\"Following\",\"spreadOut\":false,\"jilted\":false}]}",
            SnapshotDecodeFailure.OutOfRange, "$.party.others[0].standing");
        Fails(",\"party\":{\"followers\":[],\"others\":[{\"entity\":\"a\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":true}]}",
            SnapshotDecodeFailure.OutOfRange, "$.party.others[0].jilted");
        Fails(",\"party\":{\"followers\":[],\"others\":[{\"entity\":\"a\",\"standing\":\"Free\",\"spreadOut\":false,\"jilted\":false}]}",
            SnapshotDecodeFailure.OutOfRange, "$.party.others[0].jilted");
        Fails(",\"party\":{\"followers\":[{\"entity\":\"a b\",\"forced\":false,\"spreadOut\":false}],\"others\":[]}",
            SnapshotDecodeFailure.OutOfRange, "$.party.followers[0].entity");
        Fails(",\"party\":{\"followers\":[{\"entity\":\"a\",\"forced\":false,\"spreadOut\":false,\"jilted\":false}],\"others\":[]}",
            SnapshotDecodeFailure.UnknownField, "$.party.followers[0].jilted");
        Fails(",\"party\":{\"followers\":[{\"entity\":\"a\",\"forced\":0,\"spreadOut\":false}],\"others\":[]}",
            SnapshotDecodeFailure.WrongType, "$.party.followers[0].forced");
        Fails(",\"party\":{\"followers\":[]}", SnapshotDecodeFailure.MissingField, "$.party.others");
        Fails(",\"party\":null", SnapshotDecodeFailure.WrongType, "$.party");
        var many = string.Join(",", Enumerable.Range(0, GameSessionLimits.PolicyMaxCompanions + 1)
            .Select(i => $"{{\"entity\":\"n{i:D3}\",\"forced\":false,\"spreadOut\":false}}"));
        Fails(",\"party\":{\"followers\":[" + many + "],\"others\":[]}", SnapshotDecodeFailure.BudgetExceeded, "$.party.followers");
        var half = string.Join(",", Enumerable.Range(0, 40).Select(i => $"{{\"entity\":\"f{i:D3}\",\"forced\":false,\"spreadOut\":false}}"));
        var rest = string.Join(",", Enumerable.Range(0, 40).Select(i => $"{{\"entity\":\"o{i:D3}\",\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false}}"));
        Fails(",\"party\":{\"followers\":[" + half + "],\"others\":[" + rest + "]}", SnapshotDecodeFailure.BudgetExceeded, "$.party.others");
    }

    [TestMethod]
    public void AnotherLeaderIsAReasonForAFreeNpcButContradictsOneThePcLeads()
    {
        using var owner = Create();
        var otherLeader = Facts() with { OtherLeaderCharisma = 8 };
        Refused(owner.JoinParty(owner.Version, "virgil", otherLeader, false), owner, GameSessionFailure.ConsentRefused, 0, FollowConsent.AlreadyInGroup);
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 1);
        Refused(owner.JoinParty(owner.Version, "virgil", otherLeader, false), owner, GameSessionFailure.InvalidConsentFacts, 1);
        Changed(owner.WaitParty(owner.Version, "virgil"), owner, 2);
        Refused(owner.UnwaitParty(owner.Version, "virgil", otherLeader, false), owner, GameSessionFailure.InvalidConsentFacts, 2);
        Refused(owner.JoinParty(owner.Version, "virgil", otherLeader, false), owner, GameSessionFailure.InvalidConsentFacts, 2);
    }

    [TestMethod]
    public void ForcedUnwaitIgnoresTheLimitAndJoiningAWaitingNpcAppendsIt()
    {
        using var owner = Create();
        Joined(owner.JoinParty(owner.Version, "a", Facts(maxFollowers: 1), false), owner, 1);
        Joined(owner.ForceJoinParty(owner.Version, "b"), owner, 2, consent: null);
        Changed(owner.WaitParty(owner.Version, "b"), owner, 3);
        Refused(owner.UnwaitParty(owner.Version, "b", Facts(maxFollowers: 1), false), owner, GameSessionFailure.ConsentRefused, 3, FollowConsent.LimitReached);
        Joined(owner.UnwaitParty(owner.Version, "b", Facts(maxFollowers: 1), true), owner, 4);
        Assert.AreEqual(Following("b", forced: true), owner.QueryCompanion("b"));

        // JO while waiting (C5 leaves the old standing first): following again at the end of the array.
        Joined(owner.JoinParty(owner.Version, "c", Facts(maxFollowers: 7), false), owner, 5);
        Changed(owner.WaitParty(owner.Version, "a"), owner, 6);
        Joined(owner.JoinParty(owner.Version, "a", Facts(maxFollowers: 7), false), owner, 7);
        CollectionAssert.AreEqual(new[] { "b", "c", "a" }, owner.QueryParty().Followers.Select(f => f.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryParty().Others.Count);
    }

    [TestMethod]
    public void ASpreadOutFollowerLeavesAFlaggedFreeRecord()
    {
        using var owner = Create();
        Joined(owner.JoinParty(owner.Version, "virgil", Facts(), false), owner, 1);
        Changed(owner.SetSpreadOut(owner.Version, "virgil", true), owner, 2);
        Changed(owner.LeaveParty(owner.Version, "virgil", new(true)), owner, 3);
        CollectionAssert.AreEqual(new[] { new SessionCompanion("virgil", CompanionStanding.Free, false, true, false) }, owner.QueryParty().Others.ToArray());
        Changed(owner.SetSpreadOut(owner.Version, "virgil", false), owner, 4);
        Assert.AreEqual(0, owner.QueryParty().Others.Count, "back at the baseline, the record is dropped");
    }

    [TestMethod]
    public void PartyChangesStopAtTheRevisionLimit()
    {
        using var owner = Restore(Decode(GoldenJson.Replace("\"revision\":2,", "\"revision\":9223372036854775807,")));
        var max = long.MaxValue;
        Refused(owner.JoinParty(owner.Version, "a", Facts(), false), owner, GameSessionFailure.RevisionExhausted, max, FollowConsent.Accepted);
        Refused(owner.ForceJoinParty(owner.Version, "a"), owner, GameSessionFailure.RevisionExhausted, max);
        Rejected(owner.SetSpreadOut(owner.Version, "a", true), owner, GameSessionFailure.RevisionExhausted, max);
        Assert.AreEqual(0, owner.QueryParty().Followers.Count + owner.QueryParty().Others.Count);
    }

    [TestMethod]
    public void RestoreRefusesHandBuiltRecordsTheTransitionsCannotProduce()
    {
        using var owner = Create();
        var saved = owner.Capture();
        GameSessionFailure Restored(SessionCompanion[] followers, SessionCompanion[] others)
        {
            var snapshot = new GameSessionSnapshot(saved.SchemaVersion, saved.Identity, saved.Revision, saved.Clock, saved.Flags,
                saved.Actor, saved.Maps.ToArray(), saved.Story, new SessionPartyState(followers, others), saved.Hero, saved.Inventory, saved.Random, saved.Life, saved.Events, saved.Lighting);
            var result = GameSessionOwner.Restore(snapshot, RestoreOptions());
            result.Owner?.Dispose();
            return result.Failure;
        }

        var waiting = new SessionCompanion("w", CompanionStanding.Waiting, false, false, false);
        Assert.AreEqual(GameSessionFailure.None, Restored([Following("a", forced: true)], [waiting]));
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([Following("a") with { Jilted = true }], []), "jilted follower");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([waiting], []), "follower list holds a waiting record");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([Following("a b")], []), "invalid id");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([], [waiting with { Forced = true }]), "forced while waiting");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([], [waiting with { Jilted = true }]), "jilted while waiting");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([], [waiting with { Standing = CompanionStanding.Free }]), "baseline record");
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, Restored([], [Following("a")]), "others hold a follower");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored([Following("w")], [waiting]), "entity twice");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored([Following("a"), Following("a")], []), "follower twice");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored([], [waiting with { Entity = "z" }, waiting]), "unsorted others");
    }

    private static void NoChangeAt(GameSessionMutationResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }
}
