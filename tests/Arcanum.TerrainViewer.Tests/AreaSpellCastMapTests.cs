using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    private static AreaSpellCastMap Map(GameSessionOwner session, IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> placed, int runtimeMap = 1)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 16; x++) for (var y = 0; y < 16; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        return new(session, runtimeMap, steps, [Row(1, 3, 3), Row(2, 3, 3), Row(3, 5, 5)], new AreaCombatHost(new AreaInventoryHost([])),
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

    // Every step is open: a path of one step a tile (a created item lies next to its marker).
    private sealed class OpenDrop : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    // ARC-DEV-259 (B14): an item the session lists as lying on this map is a cast object on its cell (the shot's end names a thrown grenade as
    // its own parent and source), whatever row flags its map row carries; an item on another map is none, and an unknown id stays none.
    [TestMethod]
    public void An_item_lying_on_this_map_is_a_cast_object_on_its_cell()
    {
        using var session = ConversationSessionFixture.Create();
        var grenade = new SessionItemKind(SessionItemType.Generic, 15172, 7001, 15172);
        var created = session.CreateGroundItem(session.Version, grenade, new SessionMapId(1), new CellCoord(8, 9), new OpenDrop());
        Assert.AreEqual(GameSessionDisposition.Changed, created.Disposition);
        var id = created.Item!;
        var cell = session.QueryPlacedItems().Single().Cell;
        var map = Map(session, new Dictionary<AreaObjectKey, (long X, long Y)?>());

        var found = map.Object(id)!;
        Assert.AreEqual((id, (int)SessionItemType.Generic, new SpellLocation((uint)cell.X, (uint)cell.Y), 0, true), (found.Id, found.Type, found.Location, found.ObjectFlags, found.IsItem));
        Assert.IsNull(map.Object("made:99"), "an unknown id is no object");
        Assert.IsNull(Map(session, new Dictionary<AreaObjectKey, (long X, long Y)?>(), runtimeMap: 2).Object(id),
            "an item lying on another map is no object of this one");
    }

    // ARC-DEV-262: an NPC's seed item that lies loose where it was dropped or thrown (its row still carries the carried flag) is a cast object on
    // that cell too, but not while it is held or destroyed (and, in the session, in flight), and not on another map.
    [TestMethod]
    public void An_npc_item_lying_where_it_was_dropped_is_a_cast_object_on_its_cell()
    {
        using var session = ConversationSessionFixture.Create();
        var map1 = new SessionMapId(1);
        var facts = new SessionItemFacts(new(SessionItemType.Generic, 15172, 7001, 15172), 5, 0, -1, null, []);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(map1,
            new SessionMapInventorySeed([new("npc:1", SessionHolderKind.Npc, false, true, [], ["grenade"])], [new("grenade", facts, null)])).Failure);
        var map = Map(session, new Dictionary<AreaObjectKey, (long X, long Y)?>());
        Assert.IsNull(map.Object("grenade"), "held by its NPC: no map object");

        var cell = new CellCoord(8, 9);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, map1,
            MapChangeKey.ForEntity("grenade", GameSessionOwner.ItemDroppedProperty), GameSessionOwner.ItemDroppedValue(cell)).Disposition);
        var found = map.Object("grenade")!;
        Assert.AreEqual(("grenade", (int)SessionItemType.Generic, new SpellLocation(8, 9), true), (found.Id, found.Type, found.Location, found.IsItem));
        Assert.IsNull(Map(session, new Dictionary<AreaObjectKey, (long X, long Y)?>(), runtimeMap: 2).Object("grenade"), "lying on another map");

        Assert.AreEqual(GameSessionDisposition.Changed, session.DestroyLyingItem(session.Version, "grenade").Disposition);
        Assert.IsNull(map.Object("grenade"), "a destroyed item is no object");
    }
}
