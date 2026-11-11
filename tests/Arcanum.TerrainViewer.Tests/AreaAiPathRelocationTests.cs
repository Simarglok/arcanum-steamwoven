using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaAiRouteTests;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-036 (MA-5, ledger MV1–MV3): the AI host's immediate path relocation of an NPC or companion (the spell Movement component's type 0, 0x455350) over the invented 32 × 32 map of the route tests and an
/// invented path request: the NPC stands on the last tile walked, its walk ends, and the tile script is told for every tile walked and once more for the last one.
/// </summary>
[TestClass]
public sealed class AreaAiPathRelocationTests
{
    private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

    private sealed class Route(int result, params int[] rotations) : IDropMap
    {
        public List<DropOwner> Owners { get; } = [];
        public CellCoord? Step(CellCoord from, int rotation) => rotation is >= 0 and <= 7 ? new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) : from;
        public DropPath Path(CellCoord from, CellCoord to, DropOwner owner) { Owners.Add(owner); return new(result, 0, rotations); }
        public bool HasCritter(CellCoord cell) => false;
    }

    // A map where the NPC's own tile holds a critter and the path depends on the target: two tiles to the placement's, one to the relocation's.
    private sealed class CritterRoute : IDropMap
    {
        public CellCoord? Step(CellCoord from, int rotation) => rotation is >= 0 and <= 7 ? new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) : from;
        public DropPath Path(CellCoord from, CellCoord to, DropOwner owner) => to.X == NpcCell.X + 2 ? new(2, 0, [5, 5]) : new(1, 0, [5]);
        public bool HasCritter(CellCoord cell) => cell == NpcCell;
    }

    private static AreaAiRouteTests.Scene Scene(bool companion = false) => Open(Npc(0, [], 0, (1, 1), (1, 1)), companion: companion);

    private static CellCoord[] Entered(AreaAiRouteTests.Scene scene) =>
        [.. scene.Host.DrainNotices().Where(n => n.Kind == AreaAiNoticeKind.Entered).Select(n => n.Cell!.Value)];

    [TestMethod]
    public void An_npc_is_placed_on_the_last_tile_walked_and_every_tile_walked_is_told_with_the_last_one_again()
    {
        var scene = Scene();
        var map = new Route(3, 5, 5, 5);
        var (tiles, text) = scene.Host.RelocateAlongPath(scene.Npc, new(20, 20), map);
        Assert.AreEqual(3, tiles, text);
        Assert.AreEqual(new CellCoord(13, 20), scene.Host.Tile(scene.Npc), text);
        CollectionAssert.AreEqual(new[] { new CellCoord(11, 20), new(12, 20), new(13, 20), new(13, 20) }, Entered(scene), "the tile script of each tile walked, then the relocation's own notification");
        Assert.AreEqual(new CellCoord(13, 20), scene.Session.QueryNpcPlaces(new Arcanum.Session.SessionMapId(1)).Single().World, "the session keeps the place");
        Assert.AreEqual(DropOwner.Npc, map.Owners.Single(), "an NPC with no PC leader avoids every trap");
    }

    [TestMethod]
    public void A_companion_is_placed_by_its_walks_jump_and_asks_the_path_as_the_pcs_side()
    {
        var scene = Scene(companion: true);
        var map = new Route(2, 5, 5);
        var (tiles, text) = scene.Host.RelocateAlongPath(scene.Npc, new(20, 20), map);
        Assert.AreEqual(2, tiles, text);
        Assert.AreEqual(new CellCoord(12, 20), scene.Walks.Cells[scene.Npc], text);
        Assert.AreEqual(3, Entered(scene).Length);
        Assert.AreEqual(DropOwner.Npc, map.Owners.Single(), "a free companion has no PC leader");
    }

    [TestMethod]
    public void No_route_a_tile_the_map_does_not_hold_and_a_critter_the_host_does_not_run_move_nothing()
    {
        var scene = Scene();
        Assert.AreEqual((0, "no route"), scene.Host.RelocateAlongPath(scene.Npc, new(20, 20), new Route(0)));
        // The last tile is the hero's own (14, 20): the shown map holds no resident there.
        var (tiles, text) = scene.Host.RelocateAlongPath(scene.Npc, new(20, 20), new Route(4, 5, 5, 5, 5));
        Assert.AreEqual(4, tiles);
        StringAssert.Contains(text, "not held by the shown map");
        Assert.AreEqual(NpcCell, scene.Host.Tile(scene.Npc));
        Assert.AreEqual(0, Entered(scene).Length, "nothing moved, no tile script told");
        Assert.AreEqual((0, "not run by the host"), scene.Host.RelocateAlongPath("oid:nobody", new(20, 20), new Route(1, 5)));
    }

    // ARC-MAGIC-038 (ledger TP5): with the viewer's hook the tile scripts of the tiles walked run before the placement, with the NPC still on its old tile; only the last tile's notification follows it.
    [TestMethod]
    public void With_the_hook_the_tile_scripts_run_before_the_npc_is_placed_and_only_the_last_notice_follows()
    {
        var scene = Scene();
        var seen = new List<(CellCoord[] Tiles, CellCoord? NpcAt)>();
        scene.Host.BeforeRelocationScripts = (npc, tiles) => seen.Add(([.. tiles], scene.Host.Tile(npc)));
        var (tiles, text) = scene.Host.RelocateAlongPath(scene.Npc, new(20, 20), new Route(3, 5, 5, 5));
        Assert.AreEqual(3, tiles, text);
        var call = seen.Single();
        CollectionAssert.AreEqual(new[] { new CellCoord(11, 20), new(12, 20), new(13, 20) }, call.Tiles);
        Assert.AreEqual(NpcCell, call.NpcAt, "the scripts see the NPC where it stood");
        CollectionAssert.AreEqual(new[] { new CellCoord(13, 20) }, Entered(scene), "the placement's own notification only");
        Assert.AreEqual(new CellCoord(13, 20), scene.Host.Tile(scene.Npc));
    }

    // 0x4524C0 case 1: the placement 0x4F4E40 once (the drawn direction), then the same relocation.
    [TestMethod]
    public void A_random_tile_is_the_placement_of_the_drawn_direction_relocated_to()
    {
        var scene = Scene();
        // Direction 5 (x + 1), radius 2: the placement's target is (NpcCell.X + 2, NpcCell.Y); its path of 2 steps stops on the first tile with no critter (the NPC itself stands on its own), which the
        // relocation then walks to (a path of one step).
        var map = new CritterRoute();
        var (tiles, text) = scene.Host.RelocateToRandomTile(scene.Npc, 2, 5, map);
        Assert.AreEqual(1, tiles, text);
        Assert.AreEqual(new CellCoord(NpcCell.X + 1, NpcCell.Y), scene.Host.Tile(scene.Npc), text);
    }

    [TestMethod]
    public void A_random_tile_that_leaves_the_map_or_keeps_the_tile_moves_nothing()
    {
        var scene = Scene();
        Assert.AreEqual((0, "the placement kept the tile"), scene.Host.RelocateToRandomTile(scene.Npc, 2, 8, new Route(0)));
        Assert.AreEqual((0, "not run by the host"), scene.Host.RelocateToRandomTile("oid:nobody", 2, 5, new Route(1, 5)));
        StringAssert.Contains(scene.Host.RelocateToRandomTile(scene.Npc, 2, 9, new Route(1, 5)).Result, "out of range");
        Assert.AreEqual(NpcCell, scene.Host.Tile(scene.Npc));
    }
}
