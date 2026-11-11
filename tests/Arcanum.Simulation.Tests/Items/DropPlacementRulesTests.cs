using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>ARC-COMBAT-010: the drop's distance and tries (0x461A90, 0x4F4E40) over an invented map and scripted draws.</summary>
[TestClass]
public sealed class DropPlacementRulesTests
{
    private sealed class Rolls(params int[] values) : IDropRolls
    {
        private readonly Queue<int> _values = new(values);
        public List<(DropRoll Purpose, int Min, int Max)> Made { get; } = [];
        public int Roll(DropRoll purpose, int min, int max)
        {
            Made.Add((purpose, min, max));
            return _values.Dequeue();
        }
    }

    // A 10×10 map; Path answers from a table keyed by the target, critters stand on the listed cells.
    private sealed class Map(Dictionary<CellCoord, DropPath> paths, params CellCoord[] critters) : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public List<(CellCoord From, CellCoord To, DropOwner Who)> Asked { get; } = [];
        public CellCoord? Step(CellCoord from, int rotation)
        {
            if (rotation == 8) return from;
            var next = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
            return next.X is >= 0 and < 10 && next.Y is >= 0 and < 10 ? next : null;
        }
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            Asked.Add((from, to, who));
            return paths.TryGetValue(to, out var path) ? path : DropPath.None;
        }
        public bool HasCritter(CellCoord cell) => critters.Contains(cell);
    }

    private static readonly CellCoord Owner = new(5, 5);

    [TestMethod]
    public void TheDistanceIsDrawnTwoToFour()
    {
        var rolls = new Rolls(3);
        Assert.AreEqual(3, DropPlacementRules.Distance(rolls));
        Assert.AreEqual((DropRoll.Distance, 2, 4), rolls.Made.Single());
        Assert.ThrowsExactly<InvalidOperationException>(() => DropPlacementRules.Distance(new Rolls(5)));
    }

    [TestMethod]
    public void ACritterOwnersItemLandsOnTheFirstTileWithoutACritterAlongThePath()
    {
        // Direction 5 (x + 1) for 2 tiles: the path returns 2 straight steps; the owner stands on its own tile.
        var map = new Map(new() { [new(7, 5)] = new(2, 0, [5, 5]) }, Owner);
        var rolls = new Rolls(5);
        Assert.AreEqual(new CellCoord(6, 5), DropPlacementRules.Place(Owner, 2, DropOwner.Npc, map, rolls));
        Assert.AreEqual((DropRoll.Direction, 0, 8), rolls.Made.Single());
        // Another critter on the first tile: the next one.
        Assert.AreEqual(new CellCoord(7, 5), DropPlacementRules.Place(Owner, 2, DropOwner.Npc, new Map(new() { [new(7, 5)] = new(2, 0, [5, 5]) }, Owner, new(6, 5)), new Rolls(5)),
            "the walk ends where the path ends without a test of its last tile");
        // A container owner walks the whole path.
        Assert.AreEqual(new CellCoord(7, 5), DropPlacementRules.Place(Owner, 2, DropOwner.Object, map, new Rolls(5)));
    }

    [TestMethod]
    public void NoPathOrALongOneKeepsTheOwnersTileAndAPartialDirectAttemptIsWalked()
    {
        Assert.AreEqual(Owner, DropPlacementRules.Place(Owner, 2, DropOwner.Npc, new Map([]), new Rolls(5)), "no path, no partial steps");
        Assert.AreEqual(Owner, DropPlacementRules.Place(Owner, 2, DropOwner.Npc, new Map(new() { [new(7, 5)] = new(4, 0, [5, 5, 5, 5]) }), new Rolls(5)),
            "a path of distance + 2 or more");
        // The direct attempt completed one step before it failed: the walk reads that count, here over the fallback's rotations.
        var partial = new Map(new() { [new(7, 5)] = new(3, 1, [3, 5, 1]) }, Owner);
        Assert.AreEqual(new CellCoord(5, 6), DropPlacementRules.Place(Owner, 2, DropOwner.Npc, partial, new Rolls(5)));
        Assert.AreEqual(new CellCoord(6, 5), DropPlacementRules.Place(Owner, 2, DropOwner.Npc, new Map(new() { [new(7, 5)] = new(0, 1, [5]) }, Owner), new Rolls(5)),
            "no path but one partial step");
    }

    [TestMethod]
    public void ATryThatLeavesTheMapDrawsAgainAndTenFailuresKeepTheOwnersTile()
    {
        var edge = new CellCoord(9, 5);
        var map = new Map(new() { [new(9, 7)] = new(2, 0, [3, 3]) }, edge);
        var rolls = new Rolls(5, 4, 3);
        Assert.AreEqual(new CellCoord(9, 6), DropPlacementRules.Place(edge, 2, DropOwner.PcSide, map, rolls), "x + 1 and x + 1, y + 1 leave the map; y + 1 stays");
        Assert.AreEqual(3, rolls.Made.Count);
        var stuck = new Rolls(Enumerable.Repeat(5, 10).ToArray());
        Assert.AreEqual(edge, DropPlacementRules.Place(edge, 2, DropOwner.PcSide, map, stuck));
        Assert.AreEqual(10, stuck.Made.Count);
        // Direction 8 is a step in place: the target is the owner's tile, which the path does not reach.
        var still = new Map([], edge);
        Assert.AreEqual(edge, DropPlacementRules.Place(edge, 3, DropOwner.PcSide, still, new Rolls(8)));
        Assert.AreEqual((edge, edge, DropOwner.PcSide), still.Asked.Single());
    }
    [TestMethod]
    public void ACreatedObjectDrawsADistanceOneToThreeInEachTryThenTheDirection()
    {
        // ARC-WORLD-081, PG1: try 1 draws distance 3 and direction 1 (x − 1), which leaves the 10×10 map from x 1; try 2 draws distance 1
        // and direction 5 (x + 1), whose one-step path is walked.
        var near = new CellCoord(1, 5);
        var map = new Map(new() { [new(2, 5)] = new(1, 0, [5]) });
        var rolls = new Rolls(3, 1, 1, 5);
        Assert.AreEqual(new CellCoord(2, 5), DropPlacementRules.PlaceCreated(near, map, rolls));
        CollectionAssert.AreEqual(new[] { (DropRoll.Distance, 1, 3), (DropRoll.Direction, 0, 8), (DropRoll.Distance, 1, 3), (DropRoll.Direction, 0, 8) }, rolls.Made);
        Assert.AreEqual((near, new CellCoord(2, 5), DropOwner.Object), map.Asked.Single(), "the object it is created next to is no critter owner");
        // Ten failed tries give the object's own tile; a distance outside 1..3 is a broken roll source.
        var edge = new CellCoord(0, 0);
        Assert.AreEqual(edge, DropPlacementRules.PlaceCreated(edge, new Map([]), new Rolls(Enumerable.Repeat(new[] { 1, 0 }, 10).SelectMany(p => p).ToArray())));
        Assert.ThrowsExactly<InvalidOperationException>(() => DropPlacementRules.PlaceCreated(near, map, new Rolls(4)));
    }

    [TestMethod]
    public void CreatedItemsAreCappedNearTheirObjectOnlyUnderSteamwoven()
    {
        var anchor = new CellCoord(100, 100);
        var lying = new[] { new CellCoord(97, 103), new CellCoord(103, 97), new CellCoord(104, 100), new CellCoord(100, 96) };
        Assert.AreEqual(CreatedItemRules.Limit - 2, CreatedItemRules.Room(anchor, lying, unbounded: false), "two lie within 3 tiles on both axes");
        Assert.AreEqual(0, CreatedItemRules.Room(anchor, Enumerable.Repeat(anchor, CreatedItemRules.Limit + 1), unbounded: false));
        Assert.IsNull(CreatedItemRules.Room(anchor, Enumerable.Repeat(anchor, 100), unbounded: true));
    }

    // ARC-MAGIC-038 (ledger TP3): the one call of 0x4F4E40 the spell Movement component's type 1 makes, with the drawn direction given.
    [TestMethod]
    public void PlaceOnce_walks_the_radius_in_the_drawn_direction_and_stops_on_the_first_tile_without_a_critter()
    {
        var map = new Map(new() { [new(8, 5)] = new(3, 0, [5, 5, 5]) }, Owner);
        Assert.AreEqual(new CellCoord(6, 5), DropPlacementRules.PlaceOnce(Owner, 3, DropOwner.Npc, map, 5));
        Assert.AreEqual((Owner, new CellCoord(8, 5), DropOwner.Npc), map.Asked.Single(), "the path from the owner to the tile `radius` steps away");
        // An object owner has no critter test: the walk goes to its end.
        Assert.AreEqual(new CellCoord(8, 5), DropPlacementRules.PlaceOnce(Owner, 3, DropOwner.Object, new Map(new() { [new(8, 5)] = new(3, 0, [5, 5, 5]) }, Owner), 5));
    }

    [TestMethod]
    public void PlaceOnce_keeps_the_owners_tile_without_a_path_or_with_a_route_of_radius_plus_two_and_answers_null_off_the_map()
    {
        Assert.AreEqual(Owner, DropPlacementRules.PlaceOnce(Owner, 3, DropOwner.Npc, new Map([], Owner), 5), "no path, no completed step");
        Assert.AreEqual(Owner, DropPlacementRules.PlaceOnce(Owner, 3, DropOwner.Npc, new Map(new() { [new(8, 5)] = new(5, 0, [5, 5, 5, 5, 5]) }, Owner), 5), "a route of radius + 2");
        Assert.AreEqual(Owner, DropPlacementRules.PlaceOnce(Owner, 3, DropOwner.Npc, new Map([], Owner), 8), "direction 8 steps in place: the path is empty");
        Assert.IsNull(DropPlacementRules.PlaceOnce(new(8, 5), 3, DropOwner.Npc, new Map([], new CellCoord(8, 5)), 5), "a step leaves the 10 x 10 map: the component relocates nothing");
        Assert.AreEqual(Owner, DropPlacementRules.PlaceOnce(Owner, 0, DropOwner.Npc, new Map([], Owner), 3), "radius 0 targets the owner's own tile");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DropPlacementRules.PlaceOnce(Owner, -1, DropOwner.Npc, new Map([], Owner), 3));
    }
}
