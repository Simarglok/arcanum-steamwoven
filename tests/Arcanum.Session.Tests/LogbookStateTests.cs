using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-008: quelled rumors in the story domain and the logbook lists of quests and rumors (ledger LB6, LB9,
/// LB11–LB12).</summary>
[TestClass]
public sealed class LogbookStateTests
{
    private const long StartMs = 54_000_000;

    [TestMethod]
    public void QuellingIsGameWideFinalAndIndependentOfKnowingTheRumor()
    {
        using var owner = Create();
        Assert.IsFalse(owner.QueryRumorQuelled(1500));
        // The PC need not know the rumor; the mark is not a learning.
        Changed(owner.QuellRumor(owner.Version, 1500), owner, 1);
        Assert.IsTrue(owner.QueryRumorQuelled(1500));
        Assert.IsNull(owner.QueryRumor(1500));
        NoChange(owner.QuellRumor(owner.Version, 1500), owner, 1);
        Changed(owner.QuellRumor(owner.Version, 2999), owner, 2);
        Changed(owner.QuellRumor(owner.Version, 1000), owner, 3);
        CollectionAssert.AreEqual(new[] { 1000, 1500, 2999 }, owner.QueryStory().QuelledRumors.ToArray());

        Rejected(owner.QuellRumor(owner.Version, 999), owner, GameSessionFailure.InvalidRumor, 3);
        Rejected(owner.QuellRumor(owner.Version, 3000), owner, GameSessionFailure.InvalidRumor, 3);
        var stale = owner.Version;
        Changed(owner.LearnRumor(owner.Version, 1001), owner, 4);
        Rejected(owner.QuellRumor(stale, 1001), owner, GameSessionFailure.StaleVersion, 4);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryRumorQuelled(3000));
    }

    [TestMethod]
    public void TheQuestLogListsKnownQuestsInTimeOrderWithTheBotchedMark()
    {
        using var owner = Create();
        Assert.AreEqual(0, owner.QueryQuestLog().Count);
        Changed(owner.SetQuestState(owner.Version, 1500, SessionQuestState.Accepted), owner, 1);
        Changed(owner.SetQuestState(owner.Version, 1200, SessionQuestState.Mentioned), owner, 2);
        // Botched while still Unknown: a record, but not in the logbook.
        Changed(owner.SetQuestState(owner.Version, 1300, SessionQuestState.Botched), owner, 3);
        Advance(owner, 60_000);
        Changed(owner.SetQuestState(owner.Version, 1100, SessionQuestState.Completed), owner, 4);
        Changed(owner.SetQuestState(owner.Version, 1600, SessionQuestState.Achieved), owner, 5);
        Advance(owner, 60_000);
        // Botched over Accepted: shown Botched, with the time of the botching.
        Changed(owner.SetQuestState(owner.Version, 1500, SessionQuestState.Botched), owner, 6);
        Assert.IsNotNull(owner.QueryQuest(1300));

        CollectionAssert.AreEqual(new[]
        {
            new SessionQuestLogEntry(1200, SessionQuestState.Mentioned, StartMs),
            new SessionQuestLogEntry(1100, SessionQuestState.Completed, StartMs + 60_000),
            new SessionQuestLogEntry(1600, SessionQuestState.Achieved, StartMs + 60_000),
            new SessionQuestLogEntry(1500, SessionQuestState.Botched, StartMs + 120_000),
        }, owner.QueryQuestLog().ToArray());
        Assert.AreEqual(6, owner.Version.Revision, "reading the logbook changes nothing");
    }

    [TestMethod]
    public void TheRumorLogListsKnownRumorsInTimeOrderWithTheQuelledMark()
    {
        using var owner = Create();
        Changed(owner.LearnRumor(owner.Version, 2000), owner, 1);
        Changed(owner.LearnRumor(owner.Version, 1001), owner, 2);
        Advance(owner, 1_000);
        Changed(owner.LearnRumor(owner.Version, 1000), owner, 3);
        Changed(owner.QuellRumor(owner.Version, 2000), owner, 4);
        // A quelled rumor the PC does not know is not listed.
        Changed(owner.QuellRumor(owner.Version, 2500), owner, 5);
        CollectionAssert.AreEqual(new[]
        {
            new SessionRumorLogEntry(1001, StartMs, false),
            new SessionRumorLogEntry(2000, StartMs, true),
            new SessionRumorLogEntry(1000, StartMs + 1_000, false),
        }, owner.QueryRumorLog().ToArray());
    }

    [TestMethod]
    public void QuelledRumorsSurviveTheSnapshotAndAMapRoundTrip()
    {
        using var owner = Create();
        Changed(owner.LearnRumor(owner.Version, 1200), owner, 1);
        Changed(owner.QuellRumor(owner.Version, 1200), owner, 2);
        Changed(owner.QuellRumor(owner.Version, 1100), owner, 3);
        Changed(owner.PlaceActor(owner.Version, Start with { Map = Cave }), owner, 4);
        Assert.IsTrue(owner.QueryRumorQuelled(1100));

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"rumors\":[{\"rumor\":1200,\"timestampMs\":54000000}],\"quelledRumors\":[1100,1200],\"knownAreas\":[],\"lastArea\":0,\"reputations\":[],\"storyState\":0,\"revealedTownMaps\":[],\"townMapCells\":[],\"heroKeys\":[],\"killLog\":{\"total\":0,\"records\":[[0,0],[0,0],[0,0],[0,0],[0,0],[0,0]],\"injuries\":[]}}");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryRumorLog().ToArray(), restored.QueryRumorLog().ToArray());
        Assert.IsTrue(restored.QueryRumorQuelled(1100));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        NoChange(restored.QuellRumor(restored.Version, 1100), restored, 4);
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
