using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-230 (schema 74, ledger NQ1–NQ5): the newspaper issue queue of the original (25 words, the count; the head is word 4) as session state: the queue rule of the dialog action
/// <c>np</c> (0x4BF140), the save and load of the table, the upgrade from schema 73 and the refusal of tables a build cannot write. Issue numbers are invented.
/// </summary>
[TestClass]
public sealed class NewspaperStateTests
{
    private static int[] Words(GameSessionOwner owner) => [.. owner.QueryNewspaper().Entries];

    private static int[] Table()
    {
        var table = new int[SessionNewspaperState.Capacity];
        for (var i = 0; i < SessionNewspaperState.Head; i++) table[i] = -1;
        table[SessionNewspaperState.Head] = SessionNewspaperState.NewGameHead;
        return table;
    }

    [TestMethod]
    public void A_new_game_has_the_head_5000_behind_four_reserved_words_and_a_count_of_5()
    {
        using var owner = Create();
        var queue = owner.QueryNewspaper();
        Assert.AreEqual(5, queue.Count);
        CollectionAssert.AreEqual(Table(), Words(owner));
        Assert.IsTrue(queue.IsCanonical);
    }

    [TestMethod]
    public void An_issue_without_the_front_flag_takes_the_word_at_the_count_and_the_last_word_once_the_table_is_full()
    {
        using var owner = Create();
        for (var issue = 1; issue <= 20; issue++) Assert.AreEqual(GameSessionDisposition.Changed, owner.QueueNewspaper(owner.Version, 1000 + issue, false).Disposition);
        Assert.AreEqual(SessionNewspaperState.Capacity, owner.QueryNewspaper().Count);
        Assert.AreEqual(1001, Words(owner)[5]);
        Assert.AreEqual(1020, Words(owner)[24]);
        // The table is full: the next issue overwrites the last word and the count stays.
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.QueueNewspaper(owner.Version, 7777, false).Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual(7777, Words(owner)[24]);
        Assert.AreEqual(1019, Words(owner)[23]);
        Assert.AreEqual(SessionNewspaperState.Capacity, owner.QueryNewspaper().Count);
    }

    [TestMethod]
    public void An_issue_with_the_front_flag_goes_behind_the_head_and_pushes_the_rest_up_losing_the_last()
    {
        using var owner = Create();
        foreach (var issue in new[] { 1001, 1002, 1003 }) owner.QueueNewspaper(owner.Version, issue, false);
        Assert.AreEqual(8, owner.QueryNewspaper().Count);
        owner.QueueNewspaper(owner.Version, 1500, true);
        // The head (word 4) stays; the new issue is word 5; the old words 5..7 are 6..8.
        CollectionAssert.AreEqual(new[] { 5000, 1500, 1001, 1002, 1003 }, Words(owner)[4..9]);
        Assert.AreEqual(9, owner.QueryNewspaper().Count);

        // A full table: the front issue pushes every word up and the last one is lost, the count stays at 25.
        using var full = Create();
        for (var issue = 1; issue <= 20; issue++) full.QueueNewspaper(full.Version, 1000 + issue, false);
        full.QueueNewspaper(full.Version, 2000, true);
        Assert.AreEqual(2000, Words(full)[5]);
        Assert.AreEqual(1001, Words(full)[6]);
        Assert.AreEqual(1019, Words(full)[24], "the word 1020 that was last is lost");
        Assert.AreEqual(SessionNewspaperState.Capacity, full.QueryNewspaper().Count);
        Assert.AreEqual(5000, Words(full)[4]);
    }

    [TestMethod]
    public void An_issue_below_5000_that_is_already_queued_changes_nothing_whatever_the_flag()
    {
        using var owner = Create();
        owner.QueueNewspaper(owner.Version, 1001, false);
        owner.QueueNewspaper(owner.Version, 1002, false);
        var before = owner.Version;
        foreach (var front in new[] { false, true })
        {
            var again = owner.QueueNewspaper(owner.Version, 1002, front);
            Assert.AreEqual(GameSessionDisposition.NoChange, again.Disposition, "front " + front);
            Assert.AreEqual(before, owner.Version);
        }
        CollectionAssert.AreEqual(new[] { 5000, 1001, 1002, 0 }, Words(owner)[4..8]);
    }

    [TestMethod]
    public void The_look_up_covers_the_zero_words_past_the_count_and_skips_issues_from_5000_on()
    {
        using var owner = Create();
        // The words from the count on are 0 and the look-up reads them too, so np 0 finds an "issue 0" while the table has room.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.QueueNewspaper(owner.Version, 0, false).Disposition);
        // An issue number from 5000 on is not looked up: the head's own number queues again and again.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.QueueNewspaper(owner.Version, 5000, false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.QueueNewspaper(owner.Version, 5000, false).Disposition);
        CollectionAssert.AreEqual(new[] { 5000, 5000, 5000, 0 }, Words(owner)[4..8]);
        // A negative operand is an issue number below 5000 like any other: queued once.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.QueueNewspaper(owner.Version, -3, false).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.QueueNewspaper(owner.Version, -3, false).Disposition);
        // A full table has no zero word left, so issue 0 queues (into the last word).
        using var full = Create();
        for (var issue = 1; issue <= 20; issue++) full.QueueNewspaper(full.Version, 1000 + issue, false);
        Assert.AreEqual(GameSessionDisposition.Changed, full.QueueNewspaper(full.Version, 0, false).Disposition);
        Assert.AreEqual(0, Words(full)[24]);
    }

    [TestMethod]
    public void A_stale_version_rejects_and_changes_nothing()
    {
        using var owner = Create();
        var stale = owner.Version;
        owner.QueueNewspaper(owner.Version, 1001, false);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.QueueNewspaper(stale, 1002, false).Failure);
        Assert.AreEqual(1, owner.QueryNewspaper().Count - 5);
    }

    [TestMethod]
    public void The_queue_survives_a_save_and_a_load_and_a_new_game_writes_the_golden_section()
    {
        using var owner = Create();
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(owner.Capture()), NoNewspaper);
        foreach (var issue in new[] { 1001, 1002, 1003 }) owner.QueueNewspaper(owner.Version, issue, false);
        owner.QueueNewspaper(owner.Version, 1500, true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QueryNewspaper().Count, restored.QueryNewspaper().Count);
        CollectionAssert.AreEqual(Words(owner), Words(restored));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void A_schema_73_save_is_upgraded_with_a_new_games_queue_and_refuses_one_already_there()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema73(current);
        StringAssert.DoesNotMatch(older, new System.Text.RegularExpressions.Regex("\"newspaper\""));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot!));
        var twice = GameSessionSnapshotCodec.TryDecode(current.Replace(NoHeroKeys, "").Replace(NoTownMapCells, "").Replace("\"schemaVersion\":80,\"world\"", "\"schemaVersion\":73,\"world\""));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.newspaper"), (twice.Failure, twice.Path));
        // A schema 73 save of the oldest readable chain reaches the same state.
        var chain = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema72(current));
        Assert.AreEqual(SnapshotDecodeFailure.None, chain.Failure, chain.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(chain.Snapshot!));
    }

    [TestMethod]
    public void A_damaged_newspaper_section_is_refused_with_a_typed_failure_or_a_refused_restore()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        string With(string replacement) => current.Replace(NoNewspaper, replacement);
        const string Zeros = ",0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0";
        // Shape errors come from the decode.
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(With(",\"newspaper\":{\"entries\":[-1,-1,-1,-1,5000],\"count\":5}")).Failure, "24 or fewer words");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(With(",\"newspaper\":{\"entries\":[-1,-1,-1,-1,5000" + Zeros + "],\"count\":4}")).Failure, "a count below the head's place");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(With(",\"newspaper\":{\"entries\":[-1,-1,-1,-1,5000" + Zeros + "],\"count\":26}")).Failure, "a count above the table");
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(With(",\"newspaper\":{\"entries\":[]}")).Failure);
        // A table a build that cannot pop never writes is a refused restore: a reserved word that is not -1, and a word past the count that is not 0.
        foreach (var text in new[]
        {
            With(",\"newspaper\":{\"entries\":[0,-1,-1,-1,5000" + Zeros + "],\"count\":5}"),
            With(",\"newspaper\":{\"entries\":[-1,-1,-1,-1,5000,9" + Zeros[2..] + "],\"count\":5}"),
        })
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text);
            Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure);
        }
    }

    [TestMethod]
    public void The_npc_origin_is_a_mobile_object_field_that_travels_with_the_save()
    {
        using var owner = Create();
        var map = new SessionMapId(79);
        var key = SessionNpcOrigin.Key("oid:virgil");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, null, [new(map, key, 7)]).Disposition);
        Assert.AreEqual(7, owner.QueryMapChange(map, key));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(7, restored.QueryMapChange(map, key));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.ApplyChanges(restored.Version, null, [new(map, key, null)]).Disposition);
        Assert.IsNull(restored.QueryMapChange(map, key));
    }
}
