using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-205 (bug B22): the town maps revealed whole (unfog town map, ledger UF1–UF3) and schema 71. Town-map numbers are invented.</summary>
[TestClass]
public sealed class TownMapStateTests
{
    [TestMethod]
    public void A_town_map_is_revealed_once_and_a_number_outside_the_range_is_refused()
    {
        using var owner = Create();
        Assert.IsFalse(owner.IsTownMapRevealed(12));
        Assert.AreEqual(0, owner.QueryRevealedTownMaps().Count);
        foreach (var invalid in new[] { 0, -1, SessionStoryLimits.LastTownMap + 1 })
            Assert.AreEqual(GameSessionFailure.InvalidTownMap, owner.RevealTownMap(owner.Version, invalid).Failure, $"town map {invalid}");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RevealTownMap(owner.Version with { Revision = 7 }, 12).Failure);

        var revealed = owner.RevealTownMap(owner.Version, 12);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L), (revealed.Disposition, owner.Version.Revision), "one revision");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RevealTownMap(owner.Version, 12).Disposition, "the original fills the same bits again");
        Assert.AreEqual(1L, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMap(owner.Version, 3).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMap(owner.Version, SessionStoryLimits.LastTownMap).Disposition);
        Assert.IsTrue(owner.IsTownMapRevealed(12));
        Assert.IsFalse(owner.IsTownMapRevealed(0));
        CollectionAssert.AreEqual(new[] { 3, 12, SessionStoryLimits.LastTownMap }, owner.QueryRevealedTownMaps().ToArray(), "ascending");
    }

    [TestMethod]
    public void The_revealed_town_maps_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMap(owner.Version, 12).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMap(owner.Version, 3).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"storyState\":0,\"revealedTownMaps\":[3,12],\"townMapCells\":[]},");
        var snapshot = Decode(text);
        CollectionAssert.AreEqual(new[] { 3, 12 }, snapshot.Story.RevealedTownMaps.ToArray());
        using var restored = Restore(snapshot);
        CollectionAssert.AreEqual(new[] { 3, 12 }, restored.QueryRevealedTownMaps().ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails(text.Replace("[3,12]", "[12,3]"), SnapshotDecodeFailure.NotCanonical, "$.story.revealedTownMaps[1]");
        Fails(text.Replace("[3,12]", "[3,3]"), SnapshotDecodeFailure.NotCanonical, "$.story.revealedTownMaps[1]");
        Fails(text.Replace("[3,12]", "[0,12]"), SnapshotDecodeFailure.OutOfRange, "$.story.revealedTownMaps[0]");
        Fails(text.Replace("[3,12]", $"[3,{SessionStoryLimits.LastTownMap + 1}]"), SnapshotDecodeFailure.OutOfRange, "$.story.revealedTownMaps[1]");
        Fails(text.Replace("[3,12]", "[\"3\"]"), SnapshotDecodeFailure.WrongType, "$.story.revealedTownMaps[0]");
        Fails(text.Replace(",\"revealedTownMaps\":[3,12]", ""), SnapshotDecodeFailure.MissingField, "$.story.revealedTownMaps");
    }

    [TestMethod]
    public void The_seventy_to_seventy_one_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema70(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":70,\"world\"");
        Assert.IsFalse(older.Contains("revealedTownMaps", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Story.RevealedTownMaps.Count, "no older build revealed a town map");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        // A schema 70 text that already carries the list is damage: the step adds it, never merges.
        Fails(older.Replace(",\"storyState\":0}", ",\"storyState\":0,\"revealedTownMaps\":[4]}"), SnapshotDecodeFailure.UnknownField, "$.story.revealedTownMaps");
        // The step needs the story section it extends.
        var story = older.IndexOf(",\"story\":{", StringComparison.Ordinal);
        var party = older.IndexOf(",\"party\":", StringComparison.Ordinal);
        Assert.IsTrue(story > 0 && party > story);
        Fails(older.Remove(story, party - story), SnapshotDecodeFailure.MissingField, "$.story");
        Fails(older.Remove(story, party - story).Insert(story, ",\"story\":[]"), SnapshotDecodeFailure.WrongType, "$.story");
    }
}
