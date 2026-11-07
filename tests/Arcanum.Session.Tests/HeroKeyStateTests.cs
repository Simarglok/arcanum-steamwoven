using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-081 (ledger LB20, schema 77): the PC's key numbers, kept in the order they were added. Key numbers are invented.</summary>
[TestClass]
public sealed class HeroKeyStateTests
{
    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HeroStateTests.HalfElf() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void Keys_are_appended_in_order_and_a_number_held_already_is_added_again()
    {
        using var owner = Owner();
        Assert.AreEqual(0, owner.QueryHeroKeys()!.Count);
        foreach (var invalid in new[] { -1, int.MinValue })
            Assert.AreEqual(GameSessionFailure.InvalidKey, owner.AddHeroKey(owner.Version, invalid).Failure, $"key {invalid}");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.AddHeroKey(owner.Version with { Revision = 5 }, 3).Failure);
        var first = owner.AddHeroKey(owner.Version, 30);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L), (first.Disposition, owner.Version.Revision), "one revision");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, 4).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, 30).Disposition, "the original's add does not look for a held key");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, 0).Disposition);
        CollectionAssert.AreEqual(new[] { 30, 4, 30, 0 }, owner.QueryHeroKeys()!.ToArray(), "stored order, not sorted, repeats kept");
    }

    [TestMethod]
    public void A_session_without_a_hero_has_no_key_ring_and_the_list_is_bounded()
    {
        using var noHero = Owner(hero: false);
        Assert.IsNull(noHero.QueryHeroKeys());
        Assert.AreEqual(GameSessionFailure.KeysUnavailable, noHero.AddHeroKey(noHero.Version, 3).Failure);
        using var owner = Owner();
        for (var i = 0; i < SessionStoryLimits.MaxKeys; i++) Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroKey(owner.Version, i).Disposition);
        Assert.AreEqual(GameSessionFailure.KeyRingFull, owner.AddHeroKey(owner.Version, 1).Failure);
        Assert.AreEqual(SessionStoryLimits.MaxKeys, owner.QueryHeroKeys()!.Count);
    }

    [TestMethod]
    public void The_keys_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        // The plain fixture owner has no hero; the list is the story section's, written for any session.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"townMapCells\":[],\"heroKeys\":[]");
        using var withHero = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, withHero.AddHeroKey(withHero.Version, 12).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, withHero.AddHeroKey(withHero.Version, 5).Disposition);
        var saved = GameSessionSnapshotCodec.Encode(withHero.Capture());
        StringAssert.Contains(saved, ",\"heroKeys\":[12,5],\"killLog\"");
        var snapshot = Decode(saved);
        CollectionAssert.AreEqual(new[] { 12, 5 }, snapshot.Story.HeroKeys.ToArray());
        using var restored = Restore(snapshot);
        CollectionAssert.AreEqual(new[] { 12, 5 }, restored.QueryHeroKeys()!.ToArray());
        Assert.AreEqual(saved, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        // Keys need a PC: a text with keys and no hero is refused on restore.
        var heroless = Decode(text.Replace(",\"heroKeys\":[]", ",\"heroKeys\":[4]"));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(heroless, RestoreOptions()).Failure);
        Fails(saved.Replace("[12,5]", "[12,-5]"), SnapshotDecodeFailure.OutOfRange, "$.story.heroKeys[1]");
        Fails(saved.Replace("[12,5]", "[\"12\"]"), SnapshotDecodeFailure.WrongType, "$.story.heroKeys[0]");
        Fails(saved.Replace(",\"heroKeys\":[12,5]", ""), SnapshotDecodeFailure.MissingField, "$.story.heroKeys");
        Fails(saved.Replace("[12,5]", "[" + string.Join(",", Enumerable.Range(0, SessionStoryLimits.MaxKeys + 1)) + "]"), SnapshotDecodeFailure.BudgetExceeded, "$.story.heroKeys");
    }

    [TestMethod]
    public void The_seventy_six_to_seventy_seven_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema76(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":76,\"world\"");
        Assert.IsFalse(older.Contains("heroKeys", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Story.HeroKeys.Count, "no older build kept a key");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        // A schema 76 text that already carries the list is damage: the step adds it, never merges.
        Fails(older.Replace(",\"townMapCells\":[]}", ",\"townMapCells\":[],\"heroKeys\":[4]}"), SnapshotDecodeFailure.UnknownField, "$.story.heroKeys");
        var story = older.IndexOf(",\"story\":{", StringComparison.Ordinal);
        var party = older.IndexOf(",\"party\":", StringComparison.Ordinal);
        Assert.IsTrue(story > 0 && party > story);
        Fails(older.Remove(story, party - story), SnapshotDecodeFailure.MissingField, "$.story");
    }
}
