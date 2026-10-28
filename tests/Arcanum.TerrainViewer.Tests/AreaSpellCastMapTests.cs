using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-014: the cast map's direction of a tile offset, as the original numbers it (0x4B8D50). ARC-MAGIC-021: its
/// placed rows (a companion where it stands now, none while on another map), which the cursor's pick asks for the objects on a
/// tile. Invented rows and cells, no original data.</summary>
[TestClass]
public sealed class AreaSpellCastMapTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, long x, long y) => new(Key(index), 0, 2, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static AreaSpellCastMap Map(GameSessionOwner session, IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> placed)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 16; x++) for (var y = 0; y < 16; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        return new(session, 1, steps, [Row(1, 3, 3), Row(2, 3, 3), Row(3, 5, 5)], new AreaCombatHost(new AreaInventoryHost([])),
            () => null, () => null, placed: () => placed);
    }

    [TestMethod]
    public void Directions_follow_the_original_table_with_a_two_to_one_slope_rule()
    {
        var cases = new (long Dx, long Dy, int Direction)[]
        {
            (-1, -1, 0), (-1, 0, 1), (-1, 1, 2), (0, 1, 3), (1, 1, 4), (1, 0, 5), (1, -1, 6), (0, -1, 7), (0, 0, 5),
            (5, 2, 5), (3, 2, 4), (5, -1, 5), (-1, -5, 7), (-4, 2, 2), (-5, 2, 1),
        };
        foreach (var (dx, dy, direction) in cases) Assert.AreEqual(direction, AreaSpellCastMap.Direction(dx, dy), $"({dx},{dy})");
    }

    [TestMethod]
    public void A_placed_row_stands_where_it_is_now_and_on_no_cell_while_away()
    {
        using var session = ConversationSessionFixture.Create();
        var placed = new Dictionary<AreaObjectKey, (long X, long Y)?> { [Key(2)] = (7, 7), [Key(3)] = null };
        var map = Map(session, placed);
        string Id(int index) => map.Id(Row(index, 0, 0));

        CollectionAssert.AreEqual(new[] { Id(1) }, map.ObjectsAt(new(3, 3), SpellUiRules.PickTileTypes).Select(o => o.Id).ToArray(),
            "the placed row is no longer on its map cell");
        var moved = map.ObjectsAt(new(7, 7), SpellUiRules.PickTileTypes);
        Assert.AreEqual((Id(2), new SpellLocation(7, 7)), (moved.Single().Id, moved.Single().Location));
        Assert.AreEqual(0, map.ObjectsAt(new(5, 5), SpellUiRules.PickTileTypes).Count, "an absent row stands on no cell");
        Assert.IsNull(map.Object(Id(3)));
        Assert.AreEqual(new SpellLocation(7, 7), map.Object(Id(2))!.Location);
        Assert.AreEqual((Id(1), (string?)null), (map.IdOf(Key(1)), map.IdOf(Key(9))), "only rows of this map have an id by key");
        CollectionAssert.AreEquivalent(new[] { Id(1), Id(2) }, map.ObjectsInSquare(new(5, 5), 2, SpellUiRules.PickTileTypes).Select(o => o.Id).ToArray());
    }
}
