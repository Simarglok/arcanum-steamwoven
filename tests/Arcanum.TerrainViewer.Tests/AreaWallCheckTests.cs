using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// Synthetic walls around the block whose corner is (10,10): the player's block spans (10..13, 10..13); the lattice
// corners are every four tiles. Expectations are worked out by hand from the restated rule, not produced by the code.
[TestClass]
public sealed class AreaWallCheckTests
{
    private static int _next;
    private static AreaWallPiece Wall(long x, long y, int rotation, bool disallowed = false)
        => new(new AreaObjectKey("wall-fixture", 1, AreaSourceKind.Mobile, null, Interlocked.Increment(ref _next), 0, "wall-fixture", 0), x, y, rotation, disallowed);
    private static AreaWallPiece[] Run(long fromX, long fromY, int count, int dx, int dy, int rotation)
        => Enumerable.Range(1, count).Select(step => Wall(fromX + step * dx, fromY + step * dy, rotation)).ToArray();

    [TestMethod]
    public void BlockRegionsSplitTheBlockAlongItsDiagonals()
    {
        // Rows are local y 0..3, columns local x 0..3. Letters: a LowY, b LowX, c HighY, d HighX.
        string[][] expected =
        [
            ["ab", "a", "a", "ad"],
            ["b", "ab", "ad", "d"],
            ["b", "bc", "cd", "d"],
            ["bc", "c", "c", "cd"],
        ];
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                var regions = AreaWallCheck.RegionsOf(x, y);
                var letters = (regions.HasFlag(AreaWallRegions.LowY) ? "a" : "") + (regions.HasFlag(AreaWallRegions.LowX) ? "b" : "")
                    + (regions.HasFlag(AreaWallRegions.HighY) ? "c" : "") + (regions.HasFlag(AreaWallRegions.HighX) ? "d" : "");
                Assert.AreEqual(expected[y][x], letters, $"local {x},{y}");
            }
    }

    [TestMethod]
    public void TheFrontEdgeOfThePlayersBlockFadesAndItsCornersFadeTowardsIt()
    {
        // The +X line of the block: three tiles between corners (14,10) and (14,14), all facing across X (rotation 1).
        var edge = Run(14, 10, 3, 0, 1, 1);
        var start = Wall(14, 10, 1); var end = Wall(14, 14, 1);
        var other = Wall(30, 30, 1);
        var check = new AreaWallCheck([.. edge, start, end, other]);
        var state = check.Recalculate(11, 11);
        Assert.AreEqual((10L, 10L, AreaWallRegions.LowY | AreaWallRegions.LowX, true), (state.BlockX, state.BlockY, state.Regions, state.Recalculated));
        foreach (var wall in edge) Assert.AreEqual(AreaWallFade.Both, state.FadeOf(wall.Key));
        Assert.AreEqual(AreaWallFade.Right, state.FadeOf(start.Key), "the start corner fades its far (right) side");
        Assert.AreEqual(AreaWallFade.Left, state.FadeOf(end.Key), "the end corner fades its near (left) side");
        Assert.AreEqual(AreaWallFade.None, state.FadeOf(other.Key));
        Assert.AreEqual(5, state.Fades.Count);
    }

    [TestMethod]
    public void AnEdgeAlongXMarksCornersOnlyForWallsFacingAcrossY()
    {
        // The +Y line of the block, (14,14) -> (10,14) along -X; corner walls of both orientations.
        var edge = Run(14, 14, 3, -1, 0, 3);
        var startAcrossY = Wall(14, 14, 3); var startAcrossX = Wall(14, 14, 5);
        var endAcrossY = Wall(10, 14, 7);
        var state = new AreaWallCheck([.. edge, startAcrossY, startAcrossX, endAcrossY]).Recalculate(11, 11);
        foreach (var wall in edge) Assert.AreEqual(AreaWallFade.Both, state.FadeOf(wall.Key));
        Assert.AreEqual(AreaWallFade.Right, state.FadeOf(startAcrossY.Key));
        Assert.AreEqual(AreaWallFade.None, state.FadeOf(startAcrossX.Key), "rotation 5 takes only the marks of edges along Y");
        Assert.AreEqual(AreaWallFade.Left, state.FadeOf(endAcrossY.Key));
    }

    [TestMethod]
    public void ACompleteBackEdgeHidesTheEdgeBehindIt()
    {
        // Y(1,-1): the +X line of the block behind, tiles (14,7..9); a fade edge on its own.
        var behind = Run(14, 6, 3, 0, 1, 1);
        var alone = new AreaWallCheck(behind).Recalculate(11, 11);
        foreach (var wall in behind) Assert.AreEqual(AreaWallFade.Both, alone.FadeOf(wall.Key));
        // X(1,0): the block's own low-Y line, tiles (13..11,10), only tests; complete, it hides Y(1,-1).
        var back = Run(14, 10, 3, -1, 0, 3);
        var hidden = new AreaWallCheck([.. behind, .. back]).Recalculate(11, 11);
        Assert.AreEqual(0, hidden.Fades.Count, "the back edge neither fades nor lets the edge behind it fade");
        // An incomplete back edge hides nothing.
        var broken = new AreaWallCheck([.. behind, back[0], back[2]]).Recalculate(11, 11);
        foreach (var wall in behind) Assert.AreEqual(AreaWallFade.Both, broken.FadeOf(wall.Key));
    }

    [TestMethod]
    public void SomeEdgesAreHiddenOnlyWhileThePlayerIsInAGivenRegion()
    {
        // Y(1,0) complete hides X(2,2) (tiles (17..15,18)) from LowY and HighX, not from LowX.
        var front = Run(14, 10, 3, 0, 1, 1);
        var far = Run(18, 18, 3, -1, 0, 3);
        AreaWallCheckState At(long x, long y) => new AreaWallCheck([.. front, .. far]).Recalculate(x, y);
        var lowY = At(11, 10);
        Assert.AreEqual(AreaWallRegions.LowY, lowY.Regions);
        foreach (var wall in far) Assert.AreEqual(AreaWallFade.None, lowY.FadeOf(wall.Key));
        var lowX = At(10, 12);
        Assert.AreEqual(AreaWallRegions.LowX, lowX.Regions);
        foreach (var wall in far) Assert.AreEqual(AreaWallFade.Both, lowX.FadeOf(wall.Key));
        foreach (var wall in front) Assert.AreEqual(AreaWallFade.Both, lowX.FadeOf(wall.Key));
        var highX = At(13, 12);
        Assert.AreEqual(AreaWallRegions.HighX, highX.Regions);
        foreach (var wall in far) Assert.AreEqual(AreaWallFade.None, highX.FadeOf(wall.Key));
    }

    [TestMethod]
    public void TheSetIsKeptWhileThePlayerStaysInTheBlockAndSharesARegion()
    {
        var edge = Run(14, 10, 3, 0, 1, 1);
        var check = new AreaWallCheck(edge);
        var first = check.Recalculate(11, 11); // LowY | LowX
        var kept = check.Recalculate(12, 11); // LowY | HighX: shares LowY
        Assert.IsFalse(kept.Recalculated);
        Assert.AreSame(first.Fades, kept.Fades);
        Assert.AreEqual(first.Regions, kept.Regions, "a kept set keeps the regions it was computed for");
        var moved = check.Recalculate(13, 13); // HighY | HighX: no region shared with LowY | LowX
        Assert.IsTrue(moved.Recalculated);
        var otherBlock = check.Recalculate(15, 11);
        Assert.AreEqual((14L, 10L, true), (otherBlock.BlockX, otherBlock.BlockY, otherBlock.Recalculated));
        check.Reset();
        Assert.AreSame(AreaWallCheckState.Empty, check.State);
        Assert.IsTrue(check.Recalculate(12, 11).Recalculated, "a reset forgets the previous block");
    }

    [TestMethod]
    public void AWallOnThePlayersTileCanMoveThePlayerToThePreviousBlock()
    {
        // Player on the block's low-X line (local x 0). A wall facing across X there (rotation 5) moves it one tile back.
        Assert.AreEqual(10L, new AreaWallCheck([Wall(30, 30, 1)]).Recalculate(10, 11).BlockX);
        var acrossX = new AreaWallCheck([Wall(10, 11, 5)]).Recalculate(10, 11);
        Assert.AreEqual((6L, 10L, AreaWallRegions.HighX), (acrossX.BlockX, acrossX.BlockY, acrossX.Regions));
        // On the low-X line a wall facing across Y (rotation 3) moves it along Y instead.
        var acrossY = new AreaWallCheck([Wall(10, 12, 3)]).Recalculate(10, 12);
        Assert.AreEqual((10L, 8L), (acrossY.BlockX, acrossY.BlockY), "the source moves y by one and takes local y 3, off the lattice");
        // On the low-Y line only a rotation 2/3 wall counts.
        Assert.AreEqual((10L, 6L), (new AreaWallCheck([Wall(11, 10, 2)]).Recalculate(11, 10) is var s ? (s.BlockX, s.BlockY) : default));
        Assert.AreEqual((10L, 10L), (new AreaWallCheck([Wall(11, 10, 4)]).Recalculate(11, 10) is var t ? (t.BlockX, t.BlockY) : default));
    }

    [TestMethod]
    public void DisallowedWallsStayOpaqueAndInvalidInputIsRefused()
    {
        var edge = Run(14, 10, 3, 0, 1, 1);
        var locked = Wall(14, 12, 1, disallowed: true);
        var state = new AreaWallCheck([edge[0], locked, edge[2]]).Recalculate(11, 11);
        Assert.AreEqual(AreaWallFade.Both, state.FadeOf(edge[0].Key));
        Assert.AreEqual(AreaWallFade.None, state.FadeOf(locked.Key));
        // ARC-RENDER-022: the check still holds it, so its roof is counted (ledger RF9).
        CollectionAssert.AreEquivalent(new[] { edge[0].Key, locked.Key, edge[2].Key }, state.HeldWalls.ToArray());
        var duplicate = Wall(1, 1, 0);
        Assert.ThrowsExactly<ArgumentException>(() => new AreaWallCheck([duplicate, duplicate]));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaWallCheck([Wall(1, 1, 8)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AreaWallCheck([]).Recalculate(1, 5));
    }

    [TestMethod]
    public void ACornerBetweenTwoFadingEdgesFadesWhole()
    {
        // (14,14) ends Y(1,0) and starts Y(1,1); a wall facing across X there takes both marks.
        var corner = Wall(14, 14, 1);
        var state = new AreaWallCheck([.. Run(14, 10, 3, 0, 1, 1), .. Run(14, 14, 3, 0, 1, 1), corner]).Recalculate(11, 11);
        Assert.AreEqual(AreaWallFade.Both, state.FadeOf(corner.Key));
        var locked = Wall(14, 10, 1, disallowed: true);
        Assert.AreEqual(AreaWallFade.None, new AreaWallCheck([.. Run(14, 10, 3, 0, 1, 1), locked]).Recalculate(11, 11).FadeOf(locked.Key),
            "a disallowed corner wall stays opaque");
    }

    [TestMethod]
    public void ATestOnlyEdgeHidesSomeEdgesOnlyFromSomeRegions()
    {
        // X(2,0) (tiles (17..15,10)) only tests; complete, it hides Y(2,-2) (tiles (18,3..5)) from HighY and HighX only.
        var tester = Run(18, 10, 3, -1, 0, 3);
        var far = Run(18, 2, 3, 0, 1, 1);
        var highY = new AreaWallCheck([.. tester, .. far]).Recalculate(11, 13);
        Assert.AreEqual(AreaWallRegions.HighY, highY.Regions);
        Assert.AreEqual(0, highY.Fades.Count);
        var lowY = new AreaWallCheck([.. tester, .. far]).Recalculate(11, 10);
        foreach (var wall in far) Assert.AreEqual(AreaWallFade.Both, lowY.FadeOf(wall.Key));
        foreach (var wall in tester) Assert.AreEqual(AreaWallFade.None, lowY.FadeOf(wall.Key), "a test-only edge never fades");
    }

    [TestMethod]
    public void TheEdgeListHasTheSourcesShape()
    {
        // 8 edges only test, 14 test and fade, 12 only fade; every hide points at a later edge (so hiding before fading
        // equals the source's single pass); the edges use the source's 27 corner offsets.
        Assert.AreEqual((8, 14, 12), AreaWallCheck.EdgeKinds);
        Assert.IsTrue(AreaWallCheck.HidesPointForward);
        Assert.AreEqual(27, AreaWallCheck.CornerCount);
    }

    [TestMethod]
    public void OnlyTheFirstWallOfASharedEdgeTileFadesAndItIsCounted()
    {
        var edge = Run(14, 10, 3, 0, 1, 1);
        var second = Wall(14, 12, 1);
        var state = new AreaWallCheck([.. edge, second]).Recalculate(11, 11);
        Assert.AreEqual(AreaWallFade.Both, state.FadeOf(edge[1].Key));
        Assert.AreEqual(AreaWallFade.None, state.FadeOf(second.Key));
        Assert.AreEqual(1, state.SharedEdgeTiles);
    }
}
