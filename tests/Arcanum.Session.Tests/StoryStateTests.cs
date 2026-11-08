using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class StoryStateTests
{
    // Fixture clock: start hour 15, so the session starts at 54,000,000 ms.
    private const long StartMs = 54_000_000;

    [TestMethod]
    public void QuestStatesOnlyRiseAndCompletionIsSharedAndFinal()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryQuest(1001));
        Assert.AreEqual(SessionQuestState.Accepted, owner.QueryGlobalQuest(1001), "source default shared state");

        Changed(owner.SetQuestState(owner.Version, 1001, SessionQuestState.Mentioned), owner, 1);
        Assert.AreEqual(new SessionQuest(1001, SessionQuestState.Mentioned, false, StartMs), owner.QueryQuest(1001));
        Advance(owner, 60_000);
        Changed(owner.SetQuestState(owner.Version, 1001, SessionQuestState.Accepted), owner, 2);
        Assert.AreEqual(new SessionQuest(1001, SessionQuestState.Accepted, false, StartMs + 60_000), owner.QueryQuest(1001));

        // A request not above the current state changes nothing, the time included.
        foreach (var lower in new[] { SessionQuestState.Unknown, SessionQuestState.Mentioned, SessionQuestState.Accepted })
            NoChange(owner.SetQuestState(owner.Version, 1001, lower), owner, 2);
        Assert.AreEqual(SessionQuestState.Accepted, owner.QueryGlobalQuest(1001));

        Changed(owner.SetQuestState(owner.Version, 1001, SessionQuestState.Completed), owner, 3);
        Assert.AreEqual(SessionQuestState.Completed, owner.QueryQuest(1001)!.Value.Effective);
        Assert.AreEqual(SessionQuestState.Completed, owner.QueryGlobalQuest(1001));
        foreach (var any in new[] { SessionQuestState.OtherCompleted, SessionQuestState.Botched })
            NoChange(owner.SetQuestState(owner.Version, 1001, any), owner, 3);

        // OtherCompleted requested directly also completes the shared state.
        Changed(owner.SetQuestState(owner.Version, 1002, SessionQuestState.OtherCompleted), owner, 4);
        Assert.AreEqual(SessionQuestState.Completed, owner.QueryGlobalQuest(1002));
    }

    [TestMethod]
    public void BotchedKeepsTheStoredStateAndEndsTheQuest()
    {
        using var owner = Create();
        Changed(owner.SetQuestState(owner.Version, 1500, SessionQuestState.Accepted), owner, 1);
        Changed(owner.SetQuestState(owner.Version, 1500, SessionQuestState.Botched), owner, 2);
        var quest = owner.QueryQuest(1500)!.Value;
        Assert.AreEqual(new SessionQuest(1500, SessionQuestState.Accepted, true, StartMs), quest);
        Assert.AreEqual(SessionQuestState.Botched, quest.Effective);
        Assert.AreEqual(SessionQuestState.Botched, owner.QueryGlobalQuest(1500));
        NoChange(owner.SetQuestState(owner.Version, 1500, SessionQuestState.Completed), owner, 2);

        // Botched from an untouched quest marks the Unknown state.
        Changed(owner.SetQuestState(owner.Version, 1999, SessionQuestState.Botched), owner, 3);
        Assert.AreEqual(new SessionQuest(1999, SessionQuestState.Unknown, true, StartMs), owner.QueryQuest(1999));
    }

    [TestMethod]
    public void AQuestAlreadyEndedForEveryoneEndsTheSameWayForThePc()
    {
        var text = GoldenJson.Replace(EmptyStory,
            ",\"story\":{\"quests\":[],\"globalQuests\":[{\"quest\":1003,\"state\":\"Completed\"},{\"quest\":1004,\"state\":\"Botched\"}],\"rumors\":[],\"quelledRumors\":[],\"knownAreas\":[],\"lastArea\":0" + NoReputations + NoStoryState + NoTownMaps + NoTownMapCells + NoHeroKeys + NoKillLog + NoTownNotes + "}");
        using var owner = Restore(Decode(text));
        Changed(owner.SetQuestState(owner.Version, 1003, SessionQuestState.Mentioned), owner, 3);
        Assert.AreEqual(new SessionQuest(1003, SessionQuestState.OtherCompleted, false, StartMs), owner.QueryQuest(1003));
        Changed(owner.SetQuestState(owner.Version, 1004, SessionQuestState.Accepted), owner, 4);
        Assert.AreEqual(new SessionQuest(1004, SessionQuestState.Unknown, true, StartMs), owner.QueryQuest(1004));
        Assert.AreEqual(SessionQuestState.Botched, owner.QueryGlobalQuest(1004));
    }

    [TestMethod]
    public void RestoreRefusesStoryRecordsTheRulesCannotProduce()
    {
        void Refused(string story, GameSessionFailure failure)
        {
            var result = GameSessionOwner.Restore(Decode(GoldenJson.Replace(EmptyStory, story)), RestoreOptions());
            Assert.IsNull(result.Owner, story);
            Assert.AreEqual(failure, result.Failure, story);
        }
        string Story(string quests, string globals = "", string rumors = "", string quelled = "") =>
            $",\"story\":{{\"quests\":[{quests}],\"globalQuests\":[{globals}],\"rumors\":[{rumors}],\"quelledRumors\":[{quelled}],\"knownAreas\":[],\"lastArea\":0" + NoReputations + NoStoryState + NoTownMaps + NoTownMapCells + NoHeroKeys + NoKillLog + NoTownNotes + "}";

        Refused(Story("{\"quest\":1001,\"state\":\"Completed\",\"botched\":true,\"timestampMs\":1}"), GameSessionFailure.InvalidQuestState);
        Refused(Story("{\"quest\":1001,\"state\":\"Completed\",\"botched\":false,\"timestampMs\":1}"), GameSessionFailure.InvalidQuestState);
        Refused(Story("{\"quest\":1001,\"state\":\"Accepted\",\"botched\":true,\"timestampMs\":1}",
            "{\"quest\":1001,\"state\":\"Completed\"}"), GameSessionFailure.InvalidQuestState);
        Refused(Story("{\"quest\":1001,\"state\":\"Accepted\",\"botched\":false,\"timestampMs\":1}",
            "{\"quest\":1001,\"state\":\"Botched\"}"), GameSessionFailure.InvalidQuestState);
        Refused(Story("{\"quest\":1001,\"state\":\"Accepted\",\"botched\":false,\"timestampMs\":54000001}"), GameSessionFailure.InvalidSnapshot);
        Refused(Story("", rumors: "{\"rumor\":1001,\"timestampMs\":54000001}"), GameSessionFailure.InvalidSnapshot);

        // Consistent records restore, at the saved clock time included.
        using var owner = Restore(Decode(GoldenJson.Replace(EmptyStory, Story(
            "{\"quest\":1001,\"state\":\"OtherCompleted\",\"botched\":false,\"timestampMs\":54000000}",
            "{\"quest\":1001,\"state\":\"Completed\"}", "{\"rumor\":1001,\"timestampMs\":54000000}"))));
        Assert.AreEqual(SessionQuestState.OtherCompleted, owner.QueryQuest(1001)!.Value.Effective);
    }

    [TestMethod]
    public void RumorsAreLearnedOnceWithTheirTime()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryRumor(1000));
        Changed(owner.LearnRumor(owner.Version, 1000), owner, 1);
        Advance(owner, 1_000);
        NoChange(owner.LearnRumor(owner.Version, 1000), owner, 1);
        Changed(owner.LearnRumor(owner.Version, 2999), owner, 2);
        Assert.AreEqual(StartMs, owner.QueryRumor(1000));
        Assert.AreEqual(StartMs + 1_000, owner.QueryRumor(2999));
        CollectionAssert.AreEqual(new[] { new SessionRumor(1000, StartMs), new SessionRumor(2999, StartMs + 1_000) },
            owner.QueryStory().Rumors.ToArray());
    }

    [TestMethod]
    public void InvalidOrStaleStoryRequestsChangeNothing()
    {
        using var owner = Create();
        var stale = owner.Version;
        Changed(owner.LearnRumor(owner.Version, 1500), owner, 1);
        foreach (var quest in new[] { 0, 999, 2000, int.MinValue })
            Rejected(owner.SetQuestState(owner.Version, quest, SessionQuestState.Mentioned), owner, GameSessionFailure.InvalidQuest, 1);
        Rejected(owner.SetQuestState(owner.Version, 1000, (SessionQuestState)7), owner, GameSessionFailure.InvalidQuestState, 1);
        Rejected(owner.SetQuestState(owner.Version, 1000, (SessionQuestState)(-1)), owner, GameSessionFailure.InvalidQuestState, 1);
        Rejected(owner.SetQuestState(stale, 1000, SessionQuestState.Mentioned), owner, GameSessionFailure.StaleVersion, 1);
        foreach (var rumor in new[] { 999, 3000 })
            Rejected(owner.LearnRumor(owner.Version, rumor), owner, GameSessionFailure.InvalidRumor, 1);
        Rejected(owner.LearnRumor(stale, 1001), owner, GameSessionFailure.StaleVersion, 1);
        Assert.AreEqual(0, owner.QueryStory().Quests.Count);
        Assert.AreEqual(1, owner.QueryStory().Rumors.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryQuest(999));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryGlobalQuest(2000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryRumor(3000));
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.LearnRumor(stale, 1001));
    }

    // ARC-SCRIPT-019: the story-state number only rises (SS1) and survives the snapshot.
    [TestMethod]
    public void TheStoryStateOnlyRisesAndSurvivesTheSnapshot()
    {
        using var owner = Create();
        Assert.AreEqual(0, owner.QueryStoryState());
        Changed(owner.RaiseStoryState(owner.Version, 15), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RaiseStoryState(owner.Version, 15).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RaiseStoryState(owner.Version, 3).Disposition, "never lowered");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RaiseStoryState(owner.Version with { Revision = 0 }, 20).Failure);
        Assert.AreEqual(15, owner.QueryStoryState());
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"storyState\":15,\"revealedTownMaps\":[],\"townMapCells\":[],\"heroKeys\":[],\"killLog\":{\"total\":0,\"records\":[[0,0],[0,0],[0,0],[0,0],[0,0],[0,0]],\"injuries\":[]},\"townNotes\":[]}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(15, restored.QueryStoryState());
        Assert.AreNotEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(text.Replace("\"storyState\":15", "\"storyState\":-1")).Failure);
    }

    [TestMethod]
    public void StorySurvivesAMapRoundTripAndTheSnapshot()
    {
        using var owner = Create();
        Changed(owner.SetQuestState(owner.Version, 1001, SessionQuestState.Accepted), owner, 1);
        Changed(owner.LearnRumor(owner.Version, 1200), owner, 2);
        Changed(owner.PlaceActor(owner.Version, Start with { Map = Cave }), owner, 3);
        Changed(owner.PlaceActor(owner.Version, Start), owner, 4);
        Assert.AreEqual(SessionQuestState.Accepted, owner.QueryQuest(1001)!.Value.State);
        Assert.AreEqual(StartMs, owner.QueryRumor(1200));

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"story\":{\"quests\":[{\"quest\":1001,\"state\":\"Accepted\",\"botched\":false,\"timestampMs\":54000000}]," +
            "\"globalQuests\":[],\"rumors\":[{\"rumor\":1200,\"timestampMs\":54000000}],\"quelledRumors\":[],\"knownAreas\":[],\"lastArea\":0" + NoReputations + NoStoryState + NoTownMaps + NoTownMapCells + NoHeroKeys + NoKillLog + NoTownNotes + "}" + EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoCombatMode + NoConcealment + NoDanger + NoSound + NoEvents + NoHotkeys + NoScripts + NoResidency + NoGoals + NoSpells + NoNewspaper + NoFlights + ",\"lighting\":null}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QueryQuest(1001), restored.QueryQuest(1001));
        Assert.AreEqual(StartMs, restored.QueryRumor(1200));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // The restored owner keeps applying the rules.
        Changed(restored.SetQuestState(restored.Version, 1001, SessionQuestState.Completed), restored, 5);
        Assert.AreEqual(SessionQuestState.Completed, restored.QueryGlobalQuest(1001));
    }

    [TestMethod]
    public void DecodeRejectsDamagedStoryText()
    {
        void Fails(string story, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(EmptyStory, story));
            Assert.IsNull(result.Snapshot, story);
            Assert.AreEqual(failure, result.Failure, story);
            Assert.AreEqual(path, result.Path, story);
        }

        string Story(string quests = "", string globals = "", string rumors = "", string quelled = "") =>
            $",\"story\":{{\"quests\":[{quests}],\"globalQuests\":[{globals}],\"rumors\":[{rumors}],\"quelledRumors\":[{quelled}],\"knownAreas\":[],\"lastArea\":0" + NoReputations + NoStoryState + NoTownMaps + NoTownMapCells + NoHeroKeys + NoKillLog + NoTownNotes + "}";
        string Quest(int quest, string state = "Accepted", string botched = "false", string time = "1") =>
            $"{{\"quest\":{quest},\"state\":\"{state}\",\"botched\":{botched},\"timestampMs\":{time}}}";

        Fails("", SnapshotDecodeFailure.MissingField, "$.story");
        Fails(",\"story\":{\"quests\":[],\"rumors\":[],\"quelledRumors\":[],\"knownAreas\":[],\"lastArea\":0}", SnapshotDecodeFailure.MissingField, "$.story.globalQuests");
        Fails(",\"story\":{\"quests\":[],\"globalQuests\":[],\"rumors\":[]}", SnapshotDecodeFailure.MissingField, "$.story.quelledRumors");
        Fails(Story(quelled: "1001,1000"), SnapshotDecodeFailure.NotCanonical, "$.story.quelledRumors[1]");
        Fails(Story(quelled: "1001,1001"), SnapshotDecodeFailure.NotCanonical, "$.story.quelledRumors[1]");
        Fails(Story(quelled: "999"), SnapshotDecodeFailure.OutOfRange, "$.story.quelledRumors[0]");
        Fails(Story(quelled: "3000"), SnapshotDecodeFailure.OutOfRange, "$.story.quelledRumors[0]");
        Fails(Story(quelled: "\"1001\""), SnapshotDecodeFailure.WrongType, "$.story.quelledRumors[0]");
        Fails(Story(Quest(1002) + "," + Quest(1001)), SnapshotDecodeFailure.NotCanonical, "$.story.quests[1].quest");
        Fails(Story(Quest(1001) + "," + Quest(1001)), SnapshotDecodeFailure.NotCanonical, "$.story.quests[1].quest");
        Fails(Story(Quest(999)), SnapshotDecodeFailure.OutOfRange, "$.story.quests[0].quest");
        Fails(Story(Quest(1001, "Botched")), SnapshotDecodeFailure.OutOfRange, "$.story.quests[0].state");
        Fails(Story(Quest(1001, "Unknown")), SnapshotDecodeFailure.OutOfRange, "$.story.quests[0].state");
        Fails(Story(Quest(1001, "accepted")), SnapshotDecodeFailure.OutOfRange, "$.story.quests[0].state");
        Fails(Story(Quest(1001, botched: "0")), SnapshotDecodeFailure.WrongType, "$.story.quests[0].botched");
        Fails(Story(Quest(1001, time: "-1")), SnapshotDecodeFailure.OutOfRange, "$.story.quests[0].timestampMs");
        Fails(Story(globals: "{\"quest\":1001,\"state\":\"Accepted\"}"), SnapshotDecodeFailure.OutOfRange, "$.story.globalQuests[0].state");
        Fails(Story(globals: "{\"quest\":2000,\"state\":\"Completed\"}"), SnapshotDecodeFailure.OutOfRange, "$.story.globalQuests[0].quest");
        Fails(Story(rumors: "{\"rumor\":3000,\"timestampMs\":1}"), SnapshotDecodeFailure.OutOfRange, "$.story.rumors[0].rumor");
        Fails(Story(rumors: "{\"rumor\":1001,\"timestampMs\":1},{\"rumor\":1000,\"timestampMs\":1}"), SnapshotDecodeFailure.NotCanonical, "$.story.rumors[1].rumor");
        Fails(Story(rumors: "{\"rumor\":1001}"), SnapshotDecodeFailure.MissingField, "$.story.rumors[0].timestampMs");
        var tooMany = string.Join(",", Enumerable.Range(1000, SessionStoryLimits.MaxRumors + 1).Select(r => $"{{\"rumor\":{r},\"timestampMs\":1}}"));
        Fails(Story(rumors: tooMany), SnapshotDecodeFailure.BudgetExceeded, "$.story.rumors");

        // An Unknown state marked botched is a real record (Botched from an untouched quest).
        var botched = GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(EmptyStory, Story(Quest(1001, "Unknown", "true"))));
        Assert.AreEqual(SnapshotDecodeFailure.None, botched.Failure);
    }

    [TestMethod]
    public void OriginalNewGameFlagsAreKnownFalseAndBounded()
    {
        var created = GameSessionOwner.Create(Descriptor() with { Flags = OriginalGlobalFlags.NewGame(Identity, Epoch) });
        using var owner = created.Owner!;
        Assert.AreEqual(OriginalGlobalFlags.Count, owner.Flags.Binding.Domain.Count);
        Assert.AreEqual(false, ReadFlag(owner, 0));
        Assert.AreEqual(false, ReadFlag(owner, 3199));
        var outside = owner.Flags.Read(new(new(Guid.Parse("55555555-5555-5555-5555-555555555555")), owner.FlagVersion, new FlagId(3200)));
        Assert.AreEqual(GlobalFlagReason.FlagNotAdmitted, outside.Reason);
        Assert.AreEqual(owner.FlagVersion, owner.Flags.Version);
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static void NoChange(GameSessionMutationResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
    }
}
