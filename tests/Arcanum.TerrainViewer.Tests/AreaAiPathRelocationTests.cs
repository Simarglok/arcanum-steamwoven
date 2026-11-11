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
}
