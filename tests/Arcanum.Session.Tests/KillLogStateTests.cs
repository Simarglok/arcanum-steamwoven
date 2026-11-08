using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-082 (ledger LB21, schema 78): the PC's kill statistics and injuries. Description numbers, levels and values are invented.</summary>
[TestClass]
public sealed class KillLogStateTests
{
    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HeroStateTests.HalfElf() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Kill(GameSessionOwner owner, int name, int level, int alignment, int aptitude) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordHeroKill(owner.Version, name, level, alignment, aptitude).Disposition);

    [TestMethod]
    public void A_new_log_is_empty_and_a_session_without_a_hero_has_none()
    {
        using var owner = Owner();
        Assert.IsTrue(owner.QueryHeroKillLog()!.IsEmpty);
        using var noHero = Owner(hero: false);
        Assert.IsNull(noHero.QueryHeroKillLog());
        Assert.AreEqual(GameSessionFailure.KillLogUnavailable, noHero.RecordHeroKill(noHero.Version, 5, 1, 0, 0).Failure);
        Assert.AreEqual(GameSessionFailure.KillLogUnavailable, noHero.RecordHeroInjury(noHero.Version, 5, 0).Failure);
    }

    [TestMethod]
    public void Each_kill_replaces_the_slots_it_beats_and_a_tie_goes_to_the_newest_kill()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidKillFacts, owner.RecordHeroKill(owner.Version, -1, 1, 0, 0).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RecordHeroKill(owner.Version with { Revision = 4 }, 5, 1, 0, 0).Failure);
        Kill(owner, 100, 5, 20, -3);
        var first = owner.QueryHeroKillLog()!;
        Assert.AreEqual(1, owner.Version.Revision, "one revision");
        Assert.AreEqual((1, new SessionKillRecord(100, 5), new SessionKillRecord(100, 5)), (first.Total, first.HighestLevel, first.LowestLevel));
        Assert.AreEqual(new SessionKillRecord(100, 20), first.MostPositiveAlignment);
        Assert.AreEqual(default, first.MostNegativeAlignment, "no negative alignment yet");
        Assert.AreEqual(default, first.HighestAptitude, "no positive aptitude yet");
        Assert.AreEqual(new SessionKillRecord(100, -3), first.LowestAptitude);
        Kill(owner, 200, 9, -7, 4);   // higher level, a negative alignment, a positive aptitude
        Kill(owner, 300, 2, 20, 4);   // lower level; alignment ties 20 (newest wins); aptitude ties 4 (newest wins)
        Kill(owner, 400, 5, 0, 0);    // zero alignment and aptitude count for neither; level 5 is neither the highest nor the lowest
        var log = owner.QueryHeroKillLog()!;
        Assert.AreEqual(4, log.Total);
        Assert.AreEqual(new SessionKillRecord(200, 9), log.HighestLevel);
        Assert.AreEqual(new SessionKillRecord(300, 2), log.LowestLevel);
        Assert.AreEqual(new SessionKillRecord(300, 20), log.MostPositiveAlignment);
        Assert.AreEqual(new SessionKillRecord(200, -7), log.MostNegativeAlignment);
        Assert.AreEqual(new SessionKillRecord(300, 4), log.HighestAptitude);
        Assert.AreEqual(new SessionKillRecord(100, -3), log.LowestAptitude);
    }

    [TestMethod]
    public void Injuries_are_appended_in_order_within_their_limits()
    {
        using var owner = Owner();
        foreach (var (name, kind) in new[] { (0, 1), (-2, 1), (5, -1), (5, SessionStoryLimits.InjuryKinds) })
            Assert.AreEqual(GameSessionFailure.InvalidKillFacts, owner.RecordHeroInjury(owner.Version, name, kind).Failure, $"{name}/{kind}");
        foreach (var (name, kind) in new[] { (30286, 3), (77, 0), (30287, 1), (77, 0) })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordHeroInjury(owner.Version, name, kind).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionInjury(30286, 3), new SessionInjury(77, 0), new SessionInjury(30287, 1), new SessionInjury(77, 0) },
            owner.QueryHeroKillLog()!.Injuries.ToArray(), "oldest first, repeats kept");
        for (var i = owner.QueryHeroKillLog()!.Injuries.Count; i < SessionStoryLimits.MaxInjuries; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordHeroInjury(owner.Version, 9, 2).Disposition);
        Assert.AreEqual(GameSessionFailure.InjuryLogFull, owner.RecordHeroInjury(owner.Version, 9, 2).Failure);
    }

    [TestMethod]
    public void The_log_survives_the_snapshot_and_a_damaged_one_is_refused()
    {
        using var owner = Owner();
        Kill(owner, 100, 5, -20, 6);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordHeroInjury(owner.Version, 77, 2).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordHeroInjury(owner.Version, 30287, 3).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"killLog\":{\"total\":1,\"records\":[[100,5],[100,5],[0,0],[100,-20],[100,6],[0,0]],\"injuries\":[[77,2],[30287,3]]},\"townNotes\":[]}");
        var snapshot = Decode(text);
        Assert.AreEqual(1, snapshot.Story.KillLog.Total);
        using var restored = Restore(snapshot);
        Assert.AreEqual(owner.QueryHeroKillLog()!.Total, restored.QueryHeroKillLog()!.Total);
        CollectionAssert.AreEqual(owner.QueryHeroKillLog()!.Injuries.ToArray(), restored.QueryHeroKillLog()!.Injuries.ToArray());
        Assert.AreEqual(new SessionKillRecord(100, -20), restored.QueryHeroKillLog()!.MostNegativeAlignment);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails(text.Replace("[77,2]", "[77,4]"), SnapshotDecodeFailure.OutOfRange, "$.story.killLog.injuries[0][1]");
        Fails(text.Replace("[77,2]", "[-1,2]"), SnapshotDecodeFailure.OutOfRange, "$.story.killLog.injuries[0][0]");
        Fails(text.Replace("[77,2]", "[0,2]"), SnapshotDecodeFailure.OutOfRange, "$.story.killLog.injuries[0][0]");
        Fails(text.Replace("[77,2]", "[77]"), SnapshotDecodeFailure.OutOfRange, "$.story.killLog.injuries[0]");
        Fails(text.Replace("[[100,5],[100,5],", "[[100,5],"), SnapshotDecodeFailure.OutOfRange, "$.story.killLog.records");
        Fails(text.Replace("\"total\":1", "\"total\":-1"), SnapshotDecodeFailure.OutOfRange, "$.story.killLog.total");
        Fails(text.Replace("\"total\":1,", ""), SnapshotDecodeFailure.MissingField, "$.story.killLog.total");
        // A log needs a PC: a text with kills and no hero is refused on restore.
        var heroless = Decode(GameSessionSnapshotCodec.Encode(Owner(hero: false).Capture()).Replace("\"total\":0", "\"total\":2"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(heroless, RestoreOptions()).Failure);
    }

    [TestMethod]
    public void The_seventy_seven_to_seventy_eight_step_adds_a_new_games_log_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema77(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":77,\"world\"");
        Assert.IsFalse(older.Contains("killLog", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.IsTrue(upgraded.Story.KillLog.IsEmpty, "no older build kept a kill or an injury");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails(older.Replace(NoHeroKeys + "}", NoHeroKeys + NoKillLog + "}"), SnapshotDecodeFailure.UnknownField, "$.story.killLog");
        var story = older.IndexOf(",\"story\":{", StringComparison.Ordinal);
        var party = older.IndexOf(",\"party\":", StringComparison.Ordinal);
        Assert.IsTrue(story > 0 && party > story);
        Fails(older.Remove(story, party - story), SnapshotDecodeFailure.MissingField, "$.story");
    }
}
