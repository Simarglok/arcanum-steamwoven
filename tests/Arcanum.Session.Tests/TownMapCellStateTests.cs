using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-122 (ledger WM78, schema 75): the cells of a town map the PC revealed by walking, a bit per tile of the town's picture. Numbers are invented.</summary>
[TestClass]
public sealed class TownMapCellStateTests
{
    [TestMethod]
    public void A_cell_is_set_once_and_a_number_outside_the_ranges_is_refused()
    {
        using var owner = Create();
        Assert.IsFalse(owner.IsTownMapCellRevealed(7, 100));
        foreach (var (townMap, cell) in new[] { (0, 0), (-1, 0), (SessionStoryLimits.LastTownMap + 1, 0), (7, -1), (7, SessionStoryLimits.LastTownMapCell + 1) })
            Assert.AreEqual(GameSessionFailure.InvalidTownMap, owner.RevealTownMapCell(owner.Version, townMap, cell).Failure, $"{townMap}/{cell}");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RevealTownMapCell(owner.Version with { Revision = 9 }, 7, 100).Failure);

        var set = owner.RevealTownMapCell(owner.Version, 7, 100);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L), (set.Disposition, owner.Version.Revision), "one revision");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RevealTownMapCell(owner.Version, 7, 100).Disposition, "the same bit again");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMapCell(owner.Version, 7, 3).Disposition, "a lower cell of the same town");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMapCell(owner.Version, 7, SessionStoryLimits.LastTownMapCell).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMapCell(owner.Version, 8, 100).Disposition, "another town keeps its own bits");
        Assert.IsTrue(owner.IsTownMapCellRevealed(7, 100) && owner.IsTownMapCellRevealed(7, 3) && owner.IsTownMapCellRevealed(8, 100));
        Assert.IsFalse(owner.IsTownMapCellRevealed(7, 101));
        Assert.IsFalse(owner.IsTownMapCellRevealed(9, 100));
        Assert.AreEqual(3, owner.QueryTownMapCellCount(7));
        Assert.AreEqual(1, owner.QueryTownMapCellCount(8));
        Assert.IsFalse(owner.IsTownMapRevealed(7), "walked cells are not a whole reveal");
    }

    [TestMethod]
    public void A_town_revealed_whole_shows_every_cell_and_takes_no_walked_ones()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMap(owner.Version, 5).Disposition);
        Assert.IsTrue(owner.IsTownMapCellRevealed(5, 0) && owner.IsTownMapCellRevealed(5, 19599));
        Assert.IsFalse(owner.IsTownMapCellRevealed(5, SessionStoryLimits.LastTownMapCell + 1), "outside the cell range nothing shows");
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RevealTownMapCell(owner.Version, 5, 42).Disposition);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(0, owner.QueryTownMapCellCount(5));
    }

    [TestMethod]
    public void The_walked_cells_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        foreach (var (townMap, cell) in new[] { (12, 9), (12, 0), (3, 17) }) owner.RevealTownMapCell(owner.Version, townMap, cell);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        // Town 3: cell 17 = byte 2, bit 1 -> 000002; town 12: cells 0 and 9 = bytes 01 02.
        StringAssert.Contains(text, ",\"revealedTownMaps\":[],\"townMapCells\":[{\"townMap\":3,\"bits\":\"000002\"},{\"townMap\":12,\"bits\":\"0102\"}],\"heroKeys\":[],\"killLog\":{\"total\":0,\"records\":[[0,0],[0,0],[0,0],[0,0],[0,0],[0,0]],\"injuries\":[]},\"townNotes\":[]}");
        var snapshot = Decode(text);
        Assert.AreEqual(new[] { 3, 12 }.Length, snapshot.Story.TownMapCells.Count);
        Assert.IsTrue(snapshot.Story.TownMapCells[0].Has(17) && !snapshot.Story.TownMapCells[0].Has(16));
        using var restored = Restore(snapshot);
        Assert.IsTrue(restored.IsTownMapCellRevealed(12, 9) && restored.IsTownMapCellRevealed(12, 0) && restored.IsTownMapCellRevealed(3, 17));
        Assert.AreEqual(2, restored.QueryTownMapCellCount(12));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails(text.Replace("\"bits\":\"000002\"", "\"bits\":\"00000200\""), SnapshotDecodeFailure.NotCanonical, "$.story.townMapCells[0].bits");
        Fails(text.Replace("\"bits\":\"000002\"", "\"bits\":\"\""), SnapshotDecodeFailure.NotCanonical, "$.story.townMapCells[0].bits");
        Fails(text.Replace("\"bits\":\"000002\"", "\"bits\":\"00000A\""), SnapshotDecodeFailure.NotCanonical, "$.story.townMapCells[0].bits");
        Fails(text.Replace("\"bits\":\"000002\"", "\"bits\":\"0002f\""), SnapshotDecodeFailure.NotCanonical, "$.story.townMapCells[0].bits");
        Fails(text.Replace("\"bits\":\"000002\"", "\"bits\":\"" + new string('0', 16384) + "01\""), SnapshotDecodeFailure.OutOfRange, "$.story.townMapCells[0].bits");
        Fails(text.Replace("\"townMap\":3,", "\"townMap\":12,"), SnapshotDecodeFailure.NotCanonical, "$.story.townMapCells[1].townMap");
        Fails(text.Replace("\"townMap\":3,", "\"townMap\":0,"), SnapshotDecodeFailure.OutOfRange, "$.story.townMapCells[0].townMap");
        Fails(text.Replace("\"bits\":\"000002\"", "\"bits\":5"), SnapshotDecodeFailure.WrongType, "$.story.townMapCells[0].bits");
        Fails(text.Replace(",\"bits\":\"000002\"", ""), SnapshotDecodeFailure.MissingField, "$.story.townMapCells[0].bits");
        Fails(text.Replace(",\"townMapCells\":[{\"townMap\":3,\"bits\":\"000002\"},{\"townMap\":12,\"bits\":\"0102\"}]", ""), SnapshotDecodeFailure.MissingField, "$.story.townMapCells");
    }

    [TestMethod]
    public void The_seventy_four_to_seventy_five_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema74(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":74,\"world\"");
        Assert.IsFalse(older.Contains("townMapCells", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Story.TownMapCells.Count, "no older build kept a walked cell");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));
        // A schema 74 text that already carries the list is damage: the step adds it, never merges.
        var damaged = older.Replace(NoTownMaps + "}", NoTownMaps + ",\"townMapCells\":[]}");
        var result = GameSessionSnapshotCodec.TryDecode(damaged);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.story.townMapCells"), (result.Failure, result.Path));
    }

    [TestMethod]
    public void At_most_the_limit_of_towns_hold_cells()
    {
        using var owner = Create();
        for (var town = 1; town <= SessionStoryLimits.MaxTownMapsWithCells; town++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMapCell(owner.Version, town, 0).Disposition, $"town {town}");
        Assert.AreEqual(GameSessionFailure.InvalidTownMap, owner.RevealTownMapCell(owner.Version, SessionStoryLimits.MaxTownMapsWithCells + 1, 0).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RevealTownMapCell(owner.Version, 1, 1).Disposition, "a town that holds cells takes more");
    }
}
