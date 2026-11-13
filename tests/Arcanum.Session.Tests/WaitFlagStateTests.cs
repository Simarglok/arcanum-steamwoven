using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-1136 (ledger CF8 to CF10): the WAIT bit of an NPC the PC does not lead. The dialog wa on a free NPC sets it (the source never tests the leader), the wa condition reads that bit alone, the timer does
// nothing when it runs out, a join keeps the bit and only an unwait clears it. NPCs and numbers are invented; the session clock starts at 15:00 (T0).
[TestClass]
public sealed class WaitFlagStateTests
{
    private const string Npc = "oid:sogg";
    private const long T0 = 54_000_000, Minute = 60_000, Hour = 60 * Minute, Day = 24 * Hour;
    private static readonly DialogParticipantId Pc = new(800), Virgil = new(900);

    private static PartyWaitFacts Wait(int index = 3, int charisma = 8, PersuasionTraining persuasion = PersuasionTraining.None, bool mindControlled = false) =>
        new(index, persuasion, charisma, mindControlled);

    private static GameSessionOwner Flagged(PartyWaitFacts? timer = null)
    {
        var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, Npc, timer).Disposition);
        return owner;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(Arcanum.Simulation.Clock.ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, Arcanum.Simulation.Clock.ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private sealed class World : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => new();
    }

    [TestMethod]
    public void A_wait_on_a_free_npc_sets_the_flag_keeps_it_free_and_outside_the_array()
    {
        using var owner = Flagged();
        var record = owner.QueryCompanion(Npc);
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Free, false, false, false, CompanionLoyalty.None, true), record);
        Assert.IsTrue(record.WaitBit);
        Assert.IsFalse(record.LedByPc);
        Assert.AreEqual(0, owner.QueryParty().Followers.Count);
        CollectionAssert.AreEqual(new[] { record }, owner.QueryParty().Others.ToArray(), "a free NPC with the flag is listed, as one with any flag");
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WaitParty(owner.Version, Npc).Disposition, "the bit is already set: the source returns before anything else");
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void The_timer_is_the_one_of_the_wait_index_with_no_leader_read()
    {
        (int Index, long? Delay)[] cases = [(0, 1), (1, 15 * Minute), (2, 2 * Hour), (3, Day), (4, 7 * Day), (5, 30 * Day), (6, null)];
        foreach (var (index, delay) in cases)
        {
            using var owner = Flagged(Wait(index));
            Assert.AreEqual(delay is { } d ? T0 + d : null, owner.QueryWaitTimer(Npc), $"index {index}");
        }
        // The leader's charisma and Persuasion training are not read: an extraordinary charisma queues the timer, and training adds no step.
        using (var charismatic = Flagged(Wait(3, charisma: 25))) Assert.AreEqual(T0 + Day, charismatic.QueryWaitTimer(Npc));
        using (var trained = Flagged(Wait(5, persuasion: PersuasionTraining.Master))) Assert.AreEqual(T0 + 30 * Day, trained.QueryWaitTimer(Npc));
        // The mind-control test stands: no timer.
        using (var controlled = Flagged(Wait(3, mindControlled: true))) Assert.IsNull(controlled.QueryWaitTimer(Npc));
        using var none = Flagged();
        Assert.IsNull(none.QueryWaitTimer(Npc), "no facts, no timer");
        Assert.IsTrue(none.QueryCompanion(Npc).WaitFlag);
    }

    [TestMethod]
    public void When_the_timer_runs_out_nothing_happens_without_a_leader()
    {
        using var owner = Flagged(Wait(2));
        var before = owner.QueryCompanion(Npc);
        Advance(owner, 2 * Hour);
        var pass = owner.ProcessEvents(new World());
        Assert.AreEqual(0, pass.Waits.Count, "no jilting, no reaction change");
        Assert.AreEqual(before, owner.QueryCompanion(Npc), "the flag stays and the NPC is not jilted");
        Assert.IsNull(owner.QueryWaitTimer(Npc), "the event is consumed");
    }

    [TestMethod]
    public void The_wa_condition_reads_the_bit_and_a_following_reply_flows_through_the_conversation()
    {
        using var owner = Create();
        var talk = Talk(owner, Virgil);
        Assert.AreEqual("Wait here.", talk.Options.Single().Text, "wa 0 while the bit is clear");
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options[0].Token).Status);
        Assert.IsTrue(owner.QueryCompanion("virgil").WaitFlag, "the free NPC now has the bit");
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion("virgil").Standing);

        talk = Talk(owner, Virgil);
        Assert.AreEqual("Come along.", talk.Options.Single().Text, "wa 1 now passes and wa 0 fails");
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogConversationSelectStatus.Continued, talk.Select(talk.Options[0].Token).Status, "the unwait of an NPC with no leader does nothing and the talk goes on");
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsTrue(owner.QueryCompanion("virgil").WaitFlag, "uw does not clear the bit of a leaderless NPC");
    }

    [TestMethod]
    public void A_join_keeps_the_flag_and_only_an_unwait_clears_it()
    {
        using var owner = Flagged(Wait(2));
        Assert.IsNotNull(owner.QueryWaitTimer(Npc));
        var joined = owner.JoinParty(owner.Version, Npc, PartyStateTests.Facts(), false);
        Assert.AreEqual(GameSessionDisposition.Changed, joined.Disposition, joined.Failure.ToString());
        var follower = owner.QueryCompanion(Npc);
        Assert.AreEqual((CompanionStanding.Following, true, true), (follower.Standing, follower.WaitFlag, follower.WaitBit), "in the array with the bit still set (the join never touches it)");
        CollectionAssert.AreEqual(new[] { follower }, owner.QueryParty().Followers.ToArray());
        Assert.IsNull(owner.QueryWaitTimer(Npc), "the pending timer goes with the join (the original's keeps it: not modelled)");
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WaitParty(owner.Version, Npc).Disposition, "the bit is set: a wait does nothing");
        Assert.AreEqual(revision, owner.Version.Revision);

        // The loyalty step returns early for a set bit.
        var step = owner.RunLoyaltyStep(owner.Version, Npc, PartyStateTests.Facts(reaction: -100) with { }, false);
        Assert.AreEqual((GameSessionDisposition.NoChange, (LoyaltyStepResult?)null), (step.Disposition, step.Step));
        Assert.IsTrue(owner.QueryCompanion(Npc).LedByPc);

        // An unwait runs the follow check again, clears the bit and joins again at the end of the array.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:other").Disposition);
        var refused = owner.UnwaitParty(owner.Version, Npc, PartyStateTests.Facts(maxFollowers: 0), false);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ConsentRefused), (refused.Disposition, refused.Failure));
        Assert.IsTrue(owner.QueryCompanion(Npc).WaitFlag, "a refusal keeps the bit");
        var unwaited = owner.UnwaitParty(owner.Version, Npc, PartyStateTests.Facts(), false);
        Assert.AreEqual(GameSessionDisposition.Changed, unwaited.Disposition, unwaited.Failure.ToString());
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Following, false, false, false), owner.QueryCompanion(Npc));
        CollectionAssert.AreEqual(new[] { "oid:other", Npc }, owner.QueryParty().Followers.Select(f => f.Entity).ToArray());
    }

    [TestMethod]
    public void A_follower_that_carries_the_flag_loses_it_when_it_joins_again_because_the_join_leaves_its_leader_first()
    {
        using var owner = Flagged();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, PartyStateTests.Facts(), false).Disposition);
        Assert.IsTrue(owner.QueryCompanion(Npc).WaitFlag);
        // The PC leads it now: 0x45DE90 calls the leave 0x45DFC0, which runs the forced unwait that clears the bit.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Npc).Disposition);
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Following, true, false, false), owner.QueryCompanion(Npc));
    }

    [TestMethod]
    public void The_unwait_of_a_free_npc_with_the_flag_and_its_leave_do_nothing_without_a_leader()
    {
        using var owner = Flagged();
        var revision = owner.Version.Revision;
        var unwait = owner.UnwaitParty(owner.Version, Npc, PartyStateTests.Facts(), false);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (unwait.Disposition, unwait.Failure));
        var leave = owner.LeaveParty(owner.Version, Npc, new PartyLeave(Force: true));
        Assert.AreEqual(GameSessionDisposition.NoChange, leave.Disposition);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsTrue(owner.QueryCompanion(Npc).WaitFlag, "neither clears the bit");
        // A free NPC without the flag still refuses an unwait: it is not waiting.
        var plain = owner.UnwaitParty(owner.Version, "oid:plain", PartyStateTests.Facts(), false);
        Assert.AreEqual(GameSessionFailure.NotWaiting, plain.Failure);
    }

    [TestMethod]
    public void A_follower_that_carries_the_flag_loses_it_with_a_leave_and_with_a_wait_into_the_standing()
    {
        using var owner = Flagged(Wait(2));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Npc, PartyStateTests.Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, Npc, new PartyLeave(Force: true)).Disposition);
        Assert.AreEqual(new SessionCompanion(Npc, CompanionStanding.Free, false, false, false), owner.QueryCompanion(Npc), "the leave clears the bit, as it clears a waiting standing");
        Assert.AreEqual(0, owner.QueryParty().Others.Count + owner.QueryParty().Followers.Count);
    }

    [TestMethod]
    public void A_free_npc_without_a_record_needs_room_for_one()
    {
        using var owner = Create(new GameSessionLimits(maxCompanions: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetSpreadOut(owner.Version, "oid:a", true).Disposition);
        var revision = owner.Version.Revision;
        var refused = owner.WaitParty(owner.Version, Npc);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsFalse(owner.QueryCompanion(Npc).WaitFlag);
    }

    [TestMethod]
    public void The_flag_and_its_timer_survive_a_save_and_load()
    {
        using var owner = Flagged(Wait(2));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:follower", PartyStateTests.Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "oid:follower").Disposition, "a plain waiting standing next to it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:both", PartyStateTests.Facts(), false).Disposition);
        // A follower with the flag: the free flag first, then the join.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "oid:late").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:late", PartyStateTests.Facts(), false).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "{\"entity\":\"oid:sogg\",\"standing\":\"Free\",\"spreadOut\":false,\"jilted\":false,\"loyalty\":0,\"waitFlag\":true}");
        StringAssert.Contains(text, "{\"entity\":\"oid:late\",\"forced\":false,\"spreadOut\":false,\"loyalty\":0,\"waitFlag\":true}");
        StringAssert.Contains(text, "\"kind\":\"party.wait\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(owner.QueryCompanion(Npc), restored.QueryCompanion(Npc));
        Assert.AreEqual(T0 + 2 * Hour, restored.QueryWaitTimer(Npc), "the timer of the free NPC with the flag is admitted again");
        Assert.IsTrue(restored.QueryCompanion("oid:late").WaitFlag);
    }

    [TestMethod]
    public void A_waiting_standing_never_carries_the_flag_and_a_timer_needs_a_waiting_npc_or_the_flag()
    {
        using var owner = Flagged(Wait(2));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var waitingWithFlag = text.Replace("\"standing\":\"Free\",\"spreadOut\":false,\"jilted\":false,\"loyalty\":0,\"waitFlag\":true", "\"standing\":\"Waiting\",\"spreadOut\":false,\"jilted\":false,\"loyalty\":0,\"waitFlag\":true");
        Assert.AreNotEqual(text, waitingWithFlag);
        var decoded = GameSessionSnapshotCodec.TryDecode(waitingWithFlag);
        Assert.AreEqual((SnapshotDecodeFailure.OutOfRange, "$.party.others[0].waitFlag"), (decoded.Failure, decoded.Path));
        // The restore refuses the same record built by hand.
        var saved = owner.Capture();
        var party = new SessionPartyState([], [new(Npc, CompanionStanding.Waiting, false, false, false, CompanionLoyalty.None, true)]);
        Assert.AreEqual(GameSessionFailure.InvalidCompanion, RestoreFailure(saved, party));
        // A timer for an NPC that neither waits nor carries the flag is refused; with the flag it is the one the save holds.
        var withoutFlag = text.Replace(",\"waitFlag\":true}", ",\"waitFlag\":false}").Replace("\"others\":[{\"entity\":\"oid:sogg\",\"standing\":\"Free\",\"spreadOut\":false,\"jilted\":false,\"loyalty\":0,\"waitFlag\":false}]", "\"others\":[]");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, RestoreFailureOfText(withoutFlag));
        // A follower with a timer is not a state the transitions make (a join removes it).
        var followerWithTimer = new SessionPartyState([new(Npc, CompanionStanding.Following, false, false, false, CompanionLoyalty.None, true)], []);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, RestoreFailure(saved, followerWithTimer));
    }

    [TestMethod]
    public void A_schema_95_save_upgrades_with_the_flag_clear_and_a_damaged_one_is_refused()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:a", PartyStateTests.Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, "oid:b", PartyStateTests.Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "oid:b").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetSpreadOut(owner.Version, "oid:c", true).Disposition);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema95(current);
        StringAssert.Contains(older, "\"schemaVersion\":95,");
        Assert.IsFalse(older.Contains("waitFlag", StringComparison.Ordinal));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot!.SchemaVersion);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot));
        // The older schema cannot carry the field: a flag in a 95 text is an unknown field; a party that is not an object or a list that is not an array is the typed failure.
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace("\"loyalty\":0}", "\"loyalty\":0,\"waitFlag\":false}"));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, damaged.Failure);
        StringAssert.EndsWith(damaged.Path, ".waitFlag");
        var notList = GameSessionSnapshotCodec.TryDecode(older.Replace("\"party\":{\"followers\":[", "\"party\":{\"followers\":{},\"x\":["));
        Assert.AreNotEqual(SnapshotDecodeFailure.None, notList.Failure);
    }

    // The dialog over the real conversation runtime with the session's party and companion ports: a talk with an NPC no one leads.
    private static readonly DlgDocument Document = DlgReader.Read(Encoding.Latin1.GetBytes(
        Record("10", "?")
        + Record("11", "Wait here.", iq: "1", condition: "WA 0", response: "0", action: "WA")
        + Record("12", "Come along.", iq: "1", condition: "WA 1", response: "0", action: "UW 0 30")
        + Record("30", "Together.")));

    private static DialogConversation Talk(GameSessionOwner owner, DialogParticipantId npc)
    {
        var party = new SessionDialogPartyPort(owner, Pc, p => p == Virgil ? "virgil" : null, p => p == Virgil ? PartyStateTests.Facts() : null);
        var companion = new SessionDialogCompanionPort(owner, Pc, p => p == Virgil ? "virgil" : null, p => p == Virgil ? Wait(3) : null);
        var none = new NoCommands();
        return DialogConversation.Start(Document,
            new DialogConversationRequest(new DialogNodeId(10), new DialogParticipants(new DialogId(700), Pc, npc),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Virgil")),
            new DialogConversationPorts(none, none, null, Party: party, Companion: companion), new DialogConversationOptions());
    }

    private static string Record(string id, string text = "", string alternate = "", string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{{text}}}{{{alternate}}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}";

    private sealed class NoCommands : IDialogStateQueryPort, IDialogCommandPort
    {
        public LocalFlagReadResult Read(LocalFlagQuery query) => throw new InvalidOperationException();
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(QuestStateCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(RumorLogCommand command) => throw new InvalidOperationException();
    }

    internal static GameSessionFailure RestoreFailure(GameSessionSnapshot saved, SessionPartyState party)
    {
        var result = GameSessionOwner.Restore(WithParty(saved, party), RestoreOptions());
        result.Owner?.Dispose();
        return result.Failure;
    }

    private static GameSessionFailure RestoreFailureOfText(string text)
    {
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        var result = GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions());
        result.Owner?.Dispose();
        return result.Failure;
    }

    private static GameSessionSnapshot WithParty(GameSessionSnapshot saved, SessionPartyState party) => new(
        saved.SchemaVersion, saved.Identity, saved.Revision, saved.Clock, saved.Flags, saved.Actor, saved.Maps.ToArray(), saved.Story, party,
        saved.Hero, saved.Inventory, saved.Equipment, saved.Durability, saved.Schematics, saved.Effects, saved.Experience, saved.Advancement,
        saved.Random, saved.Life, saved.Reactions, saved.Combat, saved.CombatMode, saved.Concealment, saved.Danger, saved.Events, saved.Lighting, saved.Sound);
}
