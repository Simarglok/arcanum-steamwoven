using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-AI-1512 (VBG1): the "away" path of the request with flag 0x800 (0x41F6C0), which the back-off 0x4AC7B0 asks for its destination.</summary>
[TestClass]
public sealed class AwayPathTests
{
    private sealed class Map(int size, params (int X, int Y)[] blocked) : IOriginalPathMap
    {
        private readonly HashSet<CellCoord> _blocked = [.. blocked.Select(b => new CellCoord(b.X, b.Y))];
        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = OriginalPathRequest.Neighbor(from, rotation);
            return next.X >= 0 && next.Y >= 0 && next.X < size && next.Y < size ? next : null;
        }
        public bool TileBlocked(CellCoord cell) => _blocked.Contains(cell);
        public OriginalPathTraversal Traverse(CellCoord from, int rotation) => OriginalPathTraversal.Open;
        public int EnterCost(CellCoord to) => OriginalPathRequest.BaseCost;
        public bool DestinationOpen(CellCoord to) => !_blocked.Contains(to);
    }

    [TestMethod]
    public void TheAwayLineRunsStraightFromTheTargetRangeMinusDistanceTilesOut()
    {
        // Target two tiles east: toward is rotation 5 (east), the first offset 4 turns it half a circle (west); 5 - 2 = 3 tiles out.
        Assert.AreEqual(new CellCoord(7, 10), OriginalPathRequest.Away(new Map(30), new(10, 10), new(12, 10), 5));
        // Target to the north-west: straight away is south-east.
        Assert.AreEqual(new CellCoord(13, 13), OriginalPathRequest.Away(new Map(30), new(10, 10), new(9, 9), 4));
    }

    [TestMethod]
    public void ABlockedLineTriesTheNextDirectionInTheOriginalsOrder()
    {
        var from = new CellCoord(10, 10);
        var target = new CellCoord(12, 10);
        // Straight away (west) is blocked on its first tile: 45 degrees to one side (offset 5: rotation 2, south-west), then the other (offset 3: rotation 0, north-west).
        Assert.AreEqual(new CellCoord(7, 13), OriginalPathRequest.Away(new Map(30, (9, 10)), from, target, 5));
        Assert.AreEqual(new CellCoord(7, 7), OriginalPathRequest.Away(new Map(30, (9, 10), (9, 11)), from, target, 5));
        // With both 45-degree lines blocked as well, offset 6 (rotation 3, south) comes next, and then offset 2 (rotation 7, north).
        Assert.AreEqual(new CellCoord(10, 13), OriginalPathRequest.Away(new Map(30, (9, 10), (9, 11), (9, 9)), from, target, 5));
        Assert.AreEqual(new CellCoord(10, 7), OriginalPathRequest.Away(new Map(30, (9, 10), (9, 11), (9, 9), (10, 11)), from, target, 5));
    }

    [TestMethod]
    public void NoClearLineGivesNone()
    {
        var from = new CellCoord(10, 10);
        var ring = Enumerable.Range(0, 8).Select(r => OriginalPathRequest.Neighbor(from, r)).ToArray();
        Assert.IsNull(OriginalPathRequest.Away(new Map(30, ring.Select(c => (c.X, c.Y)).ToArray()), from, new(12, 10), 5), "every first tile blocked: the retry of MR15 fails as well");
    }

    [TestMethod]
    public void RangeNotAboveTheDistanceGivesNone()
    {
        var map = new Map(30);
        Assert.IsNull(OriginalPathRequest.Away(map, new(10, 10), new(15, 10), 5));
        Assert.IsNull(OriginalPathRequest.Away(map, new(10, 10), new(15, 10), 4));
        Assert.IsNotNull(OriginalPathRequest.Away(map, new(10, 10), new(14, 10), 5));
    }

    [TestMethod]
    public void ALineThatLeavesTheMapEndsAtItsEdgeAndALongOneFails()
    {
        // West of (1,10) there is one tile: the 4-tile line stops at the edge, as 0x4B90D0 does.
        Assert.AreEqual(new CellCoord(0, 10), OriginalPathRequest.Away(new Map(30), new(1, 10), new(3, 10), 6));
        // More than 32 tiles out is beyond the direct attempt: no line, whatever the map.
        Assert.IsNull(OriginalPathRequest.Away(new Map(200), new(100, 100), new(102, 100), 60));
    }

    [TestMethod]
    public void TheRoutePlannersAwayTileAsksTheStepEvidence()
    {
        var source = GridSource.Open(20, 20);
        Assert.AreEqual(new CellCoord(7, 10), RoutePlanner.AwayTile(source, new(10, 10), new(12, 10), 5));
        // A wall on the first west tile also blocks both diagonals beside it (their shoulder), so the evidence sends the NPC south, the next line open.
        source = new GridSource(Enumerable.Range(0, 20).Select(y => y == 10 ? new string('.', 9) + "#" + new string('.', 10) : new string('.', 20)).ToArray());
        Assert.AreEqual(new CellCoord(10, 13), RoutePlanner.AwayTile(source, new(10, 10), new(12, 10), 5));
        Assert.IsNull(RoutePlanner.AwayTile(GridSource.Open(5, 5), new(10, 10), new(12, 10), 5), "the mover outside the area");
    }
}
