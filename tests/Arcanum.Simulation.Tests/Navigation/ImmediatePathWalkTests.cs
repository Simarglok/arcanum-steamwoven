using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-MAGIC-036 (MA-5, ledger MV1–MV3): the tiles of the immediate path relocation (0x455350) over an invented map whose path request answers from a table.</summary>
[TestClass]
public sealed class ImmediatePathWalkTests
{
    private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

    private sealed class Map(DropPath? path, int width = 20) : IDropMap
    {
        public List<(CellCoord From, CellCoord To, DropOwner Who)> Asked { get; } = [];

        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = rotation is >= 0 and <= 7 ? new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) : from;
            return next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= width ? null : next;
        }

        public DropPath Path(CellCoord from, CellCoord to, DropOwner owner)
        {
            Asked.Add((from, to, owner));
            return path!;
        }

        public bool HasCritter(CellCoord cell) => false;
    }

    // Rotation 5 is +x in the original's table (0x4B8FF0), 3 is +y.
    private static int[] East(int count) => Enumerable.Repeat(5, count).ToArray();

    [TestMethod]
    public void The_walk_steps_the_returned_count_of_rotations_from_the_start_and_asks_the_path_once()
    {
        var map = new Map(new(Result: 3, Partial: 0, East(8)));
        var tiles = ImmediatePathWalk.Tiles(map, new(2, 4), new(12, 4), DropOwner.PcSide);
        CollectionAssert.AreEqual(new[] { new CellCoord(3, 4), new(4, 4), new(5, 4) }, tiles.ToArray(), "only the count the request returned (not the buffer's length)");
        Assert.AreEqual((new CellCoord(2, 4), new CellCoord(12, 4), DropOwner.PcSide), map.Asked.Single());
    }

    [TestMethod]
    public void No_route_leaves_nothing_to_walk_even_when_the_direct_attempt_completed_steps()
    {
        // The relocation reads the returned count alone: the partial steps (the drop's own fallback) are ignored.
        var tiles = ImmediatePathWalk.Tiles(new Map(new(Result: 0, Partial: 4, East(8))), new(2, 4), new(12, 4), DropOwner.Npc);
        Assert.AreEqual(0, tiles.Count);
        Assert.AreEqual(0, ImmediatePathWalk.Tiles(new Map(DropPath.None), new(2, 4), new(2, 4), DropOwner.Npc).Count);
    }

    [TestMethod]
    public void A_step_off_the_map_ends_the_walk_on_the_last_tile_reached()
    {
        var map = new Map(new(Result: 5, Partial: 0, East(8)), width: 6);
        var tiles = ImmediatePathWalk.Tiles(map, new(3, 0), new(9, 0), DropOwner.PcSide);
        CollectionAssert.AreEqual(new[] { new CellCoord(4, 0), new(5, 0) }, tiles.ToArray());
        Assert.AreEqual(0, ImmediatePathWalk.Tiles(new Map(new(Result: 2, Partial: 0, East(8)), width: 6), new(5, 0), new(9, 0), DropOwner.PcSide).Count, "the first step already leaves the map: nothing to place");
    }

    [TestMethod]
    public void A_path_that_contradicts_itself_is_refused_loudly()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => ImmediatePathWalk.Tiles(new Map(new(Result: 4, Partial: 0, East(2))), new(2, 4), new(12, 4), DropOwner.PcSide));
        Assert.ThrowsExactly<InvalidOperationException>(() => ImmediatePathWalk.Tiles(new Map(new(Result: 1, Partial: 0, [9])), new(2, 4), new(12, 4), DropOwner.PcSide));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ImmediatePathWalk.Tiles(new Map(DropPath.None), new(2, 4), new(12, 4), (DropOwner)7));
    }
}
