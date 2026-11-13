using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-UI-1739 (ledger NQ4, NQ10): the Newspapers event (kind 27) in the session: the chain a host with the newspaper texts arms to the next midnight, the daily pop of the queue's head, the default issue drawn among the numbered
/// headlines of the file, the chain's re-arming a day on, and the save and load of the state a pop leaves. Texts and issue numbers are invented.
/// </summary>
[TestClass]
public sealed class NewspaperEventTests
{
    private const long Day = SessionEventKinds.NewspaperDayMs;

    private sealed class PlainWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class Host(WrittenTexts? texts) : ISessionNewspaperWorld
    {
        public WrittenTexts? NewspaperTexts { get; } = texts;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class WrappingWorld(ISessionEventWorld wrapped) : ISessionWrappingEventWorld
    {
        public ISessionEventWorld Wrapped => wrapped;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => wrapped.CritterFacts(map, entity);
    }

    // The file holds the default issues 5000, 5010 and 5020 (so the draw is among 0..2) and some numbered issues.
    private static WrittenTexts Texts(params int[] numbers) => WrittenTexts.Create(null, null,
        MesFile.Parse(Encoding.Latin1.GetBytes(string.Concat((numbers.Length == 0 ? [5000, 5010, 5020, 1001, 1002] : numbers).Select(n => $"{{{n}}}{{1 Headline {n}}}")))).File!, null, null);

    private static GameSessionOwner CreateOwner() => Create();

    private static SessionEventPassResult Pass(GameSessionOwner owner, ISessionEventWorld world)
    {
        var result = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.ExplicitWait).Disposition);

    private static int[] Words(GameSessionOwner owner) => [.. owner.QueryNewspaper().Entries];

    // The clock starts at 15:00 (the fixture's profile): the next midnight is nine hours on.
    private const long ToMidnight = 9 * 3_600_000;

    [TestMethod]
    public void A_host_with_the_newspaper_texts_arms_one_event_to_the_next_midnight_and_a_plain_world_arms_none()
    {
        using var owner = CreateOwner();
        Pass(owner, new PlainWorld());
        Pass(owner, new Host(null));
        Assert.AreEqual(0, owner.QueryNewspaperDeadlines().Count, "no texts: no chain");
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        Pass(owner, new Host(Texts()));
        Assert.AreEqual(now + ToMidnight, owner.QueryNewspaperDeadlines().Single(), "a day less the time of day on (0x4BF270 as the new game's start adds it)");
        var revision = owner.Version.Revision;
        Pass(owner, new Host(Texts()));
        Assert.AreEqual(1, owner.QueryNewspaperDeadlines().Count, "a second pass keeps the one event");
        Assert.AreEqual(revision, owner.Version.Revision, "and changes nothing");
    }

    [TestMethod]
    public void The_chain_arms_through_a_wrapped_pass_world()
    {
        using var owner = CreateOwner();
        Pass(owner, new WrappingWorld(new Host(Texts())));
        Assert.AreEqual(1, owner.QueryNewspaperDeadlines().Count);
    }

    [TestMethod]
    public void At_midnight_the_head_goes_a_default_issue_takes_its_place_and_the_event_sets_itself_up_a_day_on()
    {
        using var owner = CreateOwner();
        var host = new Host(Texts());
        Pass(owner, host);
        Advance(owner, ToMidnight - 1);
        Pass(owner, host);
        CollectionAssert.AreEqual(new[] { -1, -1, -1, -1, 5000 }, Words(owner)[..5], "not yet");
        Advance(owner, 1);
        var drawsBefore = owner.QueryRandom().Draws;
        var revision = owner.Version.Revision;
        Pass(owner, host);
        var queue = owner.QueryNewspaper();
        Assert.AreEqual(5, queue.Count, "the pop took the only head and the default issue is the new one");
        CollectionAssert.AreEqual(new[] { -1, -1, -1, -1 }, Words(owner)[..4]);
        Assert.IsTrue(queue.HeadIssue is 5000 or 5010 or 5020, "5000 + 10 × a draw of 0..2");
        Assert.AreEqual(drawsBefore + 1, owner.QueryRandom().Draws, "one draw for the default issue");
        Assert.IsTrue(owner.Version.Revision > revision);
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        Assert.AreEqual(now + Day, owner.QueryNewspaperDeadlines().Single(), "a whole day on, from the time it fired");
        // The next day: the default head is a head from 5000 on, removed from word 4, and a default issue again.
        Advance(owner, Day);
        Pass(owner, host);
        Assert.AreEqual(5, owner.QueryNewspaper().Count);
        Assert.IsTrue(owner.QueryNewspaper().HeadIssue is 5000 or 5010 or 5020);
        Assert.AreEqual(1, owner.QueryNewspaperDeadlines().Count);
    }

    [TestMethod]
    public void Queued_issues_come_up_one_a_day_and_a_head_below_5000_moves_the_whole_table_down_from_word_0()
    {
        using var owner = CreateOwner();
        var host = new Host(Texts());
        foreach (var issue in new[] { 1001, 1002 }) owner.QueueNewspaper(owner.Version, issue, false);
        Pass(owner, host);
        Advance(owner, ToMidnight);
        var drawsBefore = owner.QueryRandom().Draws;
        Pass(owner, host);
        // The head 5000 (from 5000 on) goes by moving the words from word 4 down; 1001 is the head and 1002 behind it.
        Assert.AreEqual(6, owner.QueryNewspaper().Count);
        CollectionAssert.AreEqual(new[] { -1, -1, -1, -1, 1001, 1002, 1002 }, Words(owner)[..7], "the last word stays where it was: a stale word past the count");
        Assert.AreEqual(drawsBefore, owner.QueryRandom().Draws, "no default issue, no draw");
        Advance(owner, Day);
        Pass(owner, host);
        // The head 1001 is below 5000: every word moves down from word 0, so it lands in word 3 and 1002 is the head.
        Assert.AreEqual(5, owner.QueryNewspaper().Count);
        CollectionAssert.AreEqual(new[] { -1, -1, -1, 1001, 1002, 1002 }, Words(owner)[..6]);
        Assert.AreEqual(1002, owner.QueryNewspaper().HeadIssue);
        Advance(owner, Day);
        Pass(owner, host);
        // 1002 goes the same way (0 .. count − 1), nothing is left behind it and a default issue takes the head.
        Assert.AreEqual(5, owner.QueryNewspaper().Count);
        CollectionAssert.AreEqual(new[] { -1, -1, 1001, 1002 }, Words(owner)[..4]);
        Assert.IsTrue(owner.QueryNewspaper().HeadIssue is 5000 or 5010 or 5020);
    }

    [TestMethod]
    public void A_file_with_no_numbered_default_issue_gives_5000_and_draws_nothing()
    {
        using var owner = CreateOwner();
        var host = new Host(Texts(5000));
        Pass(owner, host);
        Advance(owner, ToMidnight);
        var draws = owner.QueryRandom().Draws;
        Pass(owner, host);
        Assert.AreEqual(5000, owner.QueryNewspaper().HeadIssue);
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "equal bounds return without a draw (0x4CEF00)");
    }

    [TestMethod]
    public void A_pass_whose_host_lost_its_texts_still_pops_with_the_default_issue_5000()
    {
        using var owner = CreateOwner();
        Pass(owner, new Host(Texts()));
        Advance(owner, ToMidnight);
        Pass(owner, new PlainWorld());
        Assert.AreEqual(5000, owner.QueryNewspaper().HeadIssue);
        Assert.AreEqual(1, owner.QueryNewspaperDeadlines().Count, "the chain goes on");
    }

    [TestMethod]
    public void The_state_a_pop_leaves_and_its_event_survive_a_save_and_a_load()
    {
        using var owner = CreateOwner();
        var host = new Host(Texts());
        owner.QueueNewspaper(owner.Version, 1001, false);
        Pass(owner, host);
        Advance(owner, ToMidnight + Day);
        Pass(owner, host);
        Assert.AreEqual(-1, Words(owner)[0]);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(Words(owner), Words(restored));
        Assert.AreEqual(owner.QueryNewspaper().Count, restored.QueryNewspaper().Count);
        CollectionAssert.AreEqual(owner.QueryNewspaperDeadlines().ToArray(), restored.QueryNewspaperDeadlines().ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // The restored chain fires on: a day later the head goes again.
        Advance(restored, Day);
        Pass(restored, host);
        Assert.AreEqual(1, restored.QueryNewspaperDeadlines().Count);
    }

    [TestMethod]
    public void A_saved_newspaper_event_that_is_not_one_chain_event_is_refused()
    {
        using var owner = CreateOwner();
        Pass(owner, new Host(Texts()));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var deadline = owner.QueryNewspaperDeadlines().Single();
        StringAssert.Contains(text, "newspaper.daily");
        // A deadline more than a day after the time it was set at is not what the chain writes.
        var late = Regex.Replace(text, "(\"kind\":\"newspaper.daily\"[^}]*?\"deadlineMs\":)" + deadline, "${1}" + (deadline + Day), RegexOptions.Singleline);
        if (late == text) late = text.Replace(deadline.ToString(), (deadline + Day).ToString());
        var decoded = GameSessionSnapshotCodec.TryDecode(late);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure);
    }
}
