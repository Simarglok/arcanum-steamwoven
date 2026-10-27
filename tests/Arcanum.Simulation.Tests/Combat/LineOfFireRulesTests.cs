using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-COMBAT-012: the original's line of fire (0x4ADE00) over a synthetic map.</summary>
[TestClass]
public sealed class LineOfFireRulesTests
{
    private const uint Opaque = 0, ShootThrough = LineOfFireRules.ShootThrough, Glass = LineOfFireRules.ShootThrough | LineOfFireRules.SeeThrough,
        Cover = Glass | LineOfFireRules.ProvidesCover;

    private sealed class Map(int size = 64) : ILineOfFireMap<string>
    {
        public readonly Dictionary<(CellCoord, int), List<LineOfFirePiece<string>>> Pieces = [];
        public readonly Dictionary<CellCoord, List<LineOfFireObject<string>>> Things = [];
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < size && tile.Y < size;
        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
            return Contains(next) ? next : null;
        }
        public IEnumerable<LineOfFirePiece<string>> Edge(CellCoord tile, int edge) => Pieces.GetValueOrDefault((tile, edge)) ?? [];
        public IEnumerable<LineOfFireObject<string>> Objects(CellCoord tile) => Things.GetValueOrDefault(tile) ?? [];

        public Map With(int x, int y, string name, uint flags, bool dead = false)
        {
            var cell = new CellCoord(x, y);
            if (!Things.TryGetValue(cell, out var list)) Things[cell] = list = [];
            list.Add(new(name, dead, flags));
            return this;
        }

        public Map WithPiece(int x, int y, int edge, string name, LineOfFirePieceKind kind, uint flags = 0)
        {
            if (!Pieces.TryGetValue((new(x, y), edge), out var list)) Pieces[(new(x, y), edge)] = list = [];
            list.Add(new(name, kind, flags));
            return this;
        }
    }

    private static LineOfFireTrace<string> Trace(Map map, (int X, int Y) from, (int X, int Y) to, CombatAnomalyProfile? profile = null) =>
        LineOfFireRules.Trace(new(from.X, from.Y), 0, 0, new(to.X, to.Y), map, profile ?? CombatAnomalyProfile.Steamwoven);

    [TestMethod]
    public void ScreenAndTileRoundTripThroughTheTileCentre()
    {
        foreach (var tile in new[] { new CellCoord(0, 0), new CellCoord(5, 3), new CellCoord(63, 1), new CellCoord(17, 40) })
        {
            var (x, y) = LineOfFireRules.Screen(tile);
            Assert.AreEqual(tile, LineOfFireRules.Tile(x + LineOfFireRules.HalfWidth, y + LineOfFireRules.HalfHeight, _ => true));
        }
        Assert.AreEqual((-40L, 0L), LineOfFireRules.Screen(new(0, 0)));
        // A point off the map gives no tile; a negative axis is truncated and then lowered by 1.
        Assert.IsNull(LineOfFireRules.Tile(0, -100, new Map().Contains));
        Assert.AreEqual(new CellCoord(-1, -1), LineOfFireRules.Tile(0, -10, _ => true));
    }

    [TestMethod]
    public void RotationFollowsTheMovementOrderAndTheSameTileIsFive()
    {
        Assert.AreEqual(5, LineOfFireRules.Rotation(new(3, 3), new(3, 3)));
        Assert.AreEqual(0, LineOfFireRules.Rotation(new(3, 3), new(2, 2)));
        Assert.AreEqual(3, LineOfFireRules.Rotation(new(3, 3), new(3, 9)));
        Assert.AreEqual(4, LineOfFireRules.Rotation(new(3, 3), new(5, 4)));
        Assert.AreEqual(5, LineOfFireRules.Rotation(new(3, 3), new(6, 4)));
    }

    [TestMethod]
    public void SamplesEveryTenthPixelAndTheRestToTheEnd()
    {
        CollectionAssert.AreEqual(new[] { (9, 0), (10, 0), (6, 0) }, LineOfFireRules.Samples((0, 0), (25, 0))!.ToArray());
        CollectionAssert.AreEqual(new[] { (9, 0), (10, 0), (1, 0) }, LineOfFireRules.Samples((0, 0), (20, 0))!.ToArray());
        // A y-major line steps its minor axis by the error term.
        CollectionAssert.AreEqual(new[] { (-5, -9), (-5, -10), (0, -1) }, LineOfFireRules.Samples((0, 0), (-10, -20))!.ToArray());
        Assert.AreEqual(1, LineOfFireRules.Samples((0, 0), (0, 0))!.Count);
        Assert.AreEqual(100, LineOfFireRules.Samples((0, 0), (999, 0))!.Count);
        Assert.IsNull(LineOfFireRules.Samples((0, 0), (1000, 0)));
    }

    [TestMethod]
    public void AClearLineWalksEveryTileToTheTarget()
    {
        var trace = Trace(new Map(), (2, 2), (8, 2));
        Assert.AreEqual(0, trace.Cost);
        Assert.IsNull(trace.Blocker);
        CollectionAssert.AreEqual(Enumerable.Range(3, 6).Select(x => new CellCoord(x, 2)).ToArray(), trace.Tiles.ToArray());
        Assert.AreEqual(0, Trace(new Map(), (4, 4), (4, 4)).Cost);
        var diagonal = Trace(new Map(), (2, 2), (6, 6));
        Assert.AreEqual(new CellCoord(6, 6), diagonal.Tiles[^1]);
    }

    [TestMethod]
    public void ALineBeyondItsPixelLimitCostsAHundredWithNoBlocker()
    {
        // 26 tiles along y is 1,040 pixels on the screen's x axis.
        var trace = Trace(new Map(), (0, 0), (0, 26));
        Assert.AreEqual(LineOfFireRules.FailedLineCost, trace.Cost);
        Assert.IsTrue(trace.LineFailed);
        Assert.IsNull(trace.Blocker);
        Assert.IsFalse(Trace(new Map(), (0, 0), (0, 24)).LineFailed);
    }

    [TestMethod]
    public void TheCoverTestPricesOrStopsAnObjectAndTheTargetTileIsSkipped()
    {
        Assert.AreEqual("npc", Trace(new Map().With(5, 2, "npc", Opaque), (2, 2), (8, 2)).Blocker);
        var priced = Trace(new Map().With(4, 2, "bush", ShootThrough).With(5, 2, "glass", Glass).With(6, 2, "crate", Cover), (2, 2), (8, 2));
        Assert.IsNull(priced.Blocker);
        Assert.AreEqual(LineOfFireRules.OpaqueCover + LineOfFireRules.ProvidedCover, priced.Cost);
        // A dead critter passes; the target tile's own objects are skipped (the target itself stands there).
        Assert.IsNull(Trace(new Map().With(5, 2, "corpse", Opaque, dead: true).With(8, 2, "target", Opaque), (2, 2), (8, 2)).Blocker);
        // The attacker's own tile is never priced.
        Assert.IsNull(Trace(new Map().With(2, 2, "self", Opaque), (2, 2), (8, 2)).Blocker);
    }

    [TestMethod]
    public void WallsAndPortalsOnTheCrossedEdgesDecideByKind()
    {
        // Leaving (4,2) toward x+ crosses its edge 5; entering (5,2) crosses that tile's edge 1.
        Assert.AreEqual("wall", Trace(new Map().WithPiece(4, 2, 5, "wall", LineOfFirePieceKind.Solid), (2, 2), (8, 2)).Blocker);
        Assert.AreEqual("wall", Trace(new Map().WithPiece(5, 2, 1, "wall", LineOfFirePieceKind.Solid), (2, 2), (8, 2)).Blocker);
        // A wall on another edge of the same tile is not crossed.
        Assert.IsNull(Trace(new Map().WithPiece(5, 2, 3, "wall", LineOfFirePieceKind.Solid), (2, 2), (8, 2)).Blocker);
        Assert.IsNull(Trace(new Map().WithPiece(4, 2, 5, "window", LineOfFirePieceKind.Window), (2, 2), (8, 2)).Blocker);
        Assert.IsNull(Trace(new Map().WithPiece(4, 2, 5, "doorway", LineOfFirePieceKind.Doorway), (2, 2), (8, 2)).Blocker);
        Assert.IsNull(Trace(new Map().WithPiece(4, 2, 5, "open", LineOfFirePieceKind.OpenPortal), (2, 2), (8, 2)).Blocker);
        Assert.AreEqual("door", Trace(new Map().WithPiece(4, 2, 5, "door", LineOfFirePieceKind.ClosedPortal, Opaque), (2, 2), (8, 2)).Blocker);
        Assert.AreEqual(LineOfFireRules.OpaqueCover,
            Trace(new Map().WithPiece(4, 2, 5, "grate", LineOfFirePieceKind.ClosedPortal, ShootThrough), (2, 2), (8, 2)).Cost);
        Assert.AreEqual(0, Trace(new Map().WithPiece(4, 2, 5, "ghost", LineOfFirePieceKind.PassWalled, Glass), (2, 2), (8, 2)).Cost);
        // Walls on the target tile still count: only its other objects are skipped.
        Assert.AreEqual("wall", Trace(new Map().WithPiece(8, 2, 1, "wall", LineOfFirePieceKind.Solid), (2, 2), (8, 2)).Blocker);
        // So do doors and windows (RA8: flag 0x10 skips only objects that are neither walls nor portals).
        Assert.AreEqual("door", Trace(new Map().WithPiece(8, 2, 1, "door", LineOfFirePieceKind.ClosedPortal, Opaque), (2, 2), (8, 2)).Blocker);
    }

    [TestMethod]
    public void AWindowPieceStopsACornerStepButNotAnEdgeStep()
    {
        var map = new Map();
        // A diagonal step from (2,2) to (3,3) (rotation 4) goes around the corner through (2,3) and (3,2).
        var corner = LineOfFireRules.StepCost(new(2, 2), 4, false, map.WithPiece(2, 2, 3, "window", LineOfFirePieceKind.Window), CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual("window", corner.Blocker);
        var edge = LineOfFireRules.StepCost(new(2, 2), 3, false, map, CombatAnomalyProfile.Steamwoven);
        Assert.IsNull(edge.Blocker);
    }

    [TestMethod]
    public void ACornerStepCountsTheCornerTilesCoverOnceUnlessTheOriginalSettingDoublesIt()
    {
        var map = new Map().With(3, 3, "crate", Cover);
        Assert.AreEqual(LineOfFireRules.ProvidedCover, LineOfFireRules.StepCost(new(2, 2), 4, false, map, CombatAnomalyProfile.Steamwoven).Cost);
        // AW13: both routes enter the corner tile, so one see-through cover object costs 40 in the original.
        Assert.AreEqual(2 * LineOfFireRules.ProvidedCover, LineOfFireRules.StepCost(new(2, 2), 4, false, map, CombatAnomalyProfile.Original).Cost);
        // The side tiles count once in both settings, and a blocker on either stops the line.
        var sides = new Map().With(2, 3, "bush", ShootThrough).With(3, 2, "bush", ShootThrough);
        Assert.AreEqual(2 * LineOfFireRules.OpaqueCover, LineOfFireRules.StepCost(new(2, 2), 4, false, sides, CombatAnomalyProfile.Steamwoven).Cost);
        Assert.AreEqual(2 * LineOfFireRules.OpaqueCover, LineOfFireRules.StepCost(new(2, 2), 4, false, sides, CombatAnomalyProfile.Original).Cost);
        Assert.AreEqual("npc", LineOfFireRules.StepCost(new(2, 2), 4, false, new Map().With(3, 2, "npc", Opaque), CombatAnomalyProfile.Steamwoven).Blocker);
    }

    [TestMethod]
    public void TheLastCornerStepSkipsOnlyTheTargetTileUnlessTheOriginalSettingSkipsTheSidesToo()
    {
        var map = new Map().With(3, 2, "npc", Opaque).With(3, 3, "target", Opaque);
        Assert.AreEqual("npc", LineOfFireRules.StepCost(new(2, 2), 4, true, map, CombatAnomalyProfile.Steamwoven).Blocker);
        // AW14: the original's last step also skips the side tiles' objects.
        Assert.IsNull(LineOfFireRules.StepCost(new(2, 2), 4, true, map, CombatAnomalyProfile.Original).Blocker);
    }

    [TestMethod]
    public void AStepOffTheMapCostsOneAndDropsItsSum()
    {
        var map = new Map(4).WithPiece(3, 1, 5, "grate", LineOfFirePieceKind.ClosedPortal, ShootThrough);
        Assert.AreEqual(new LineOfFireCost<string>(1, null), LineOfFireRules.StepCost(new(3, 1), 5, false, map, CombatAnomalyProfile.Steamwoven));
        // A corner whose side tile is off the map costs 1 as well.
        Assert.AreEqual(new LineOfFireCost<string>(1, null), LineOfFireRules.StepCost(new(0, 1), 0, false, new Map(4), CombatAnomalyProfile.Steamwoven));
    }

    [TestMethod]
    public void TheAttackersPixelOffsetMovesTheWalk()
    {
        // From (2,2) toward (2,8) the line runs along the screen's x axis; an offset half a tile down puts the walk on other tiles.
        var straight = Trace(new Map(), (2, 2), (2, 8));
        var shifted = LineOfFireRules.Trace(new CellCoord(2, 2), 0, 25, new CellCoord(2, 8), new Map(), CombatAnomalyProfile.Steamwoven);
        CollectionAssert.AreNotEqual(straight.Tiles.ToArray(), shifted.Tiles.ToArray());
    }

    [TestMethod]
    public void TheGoalsReachTestNeedsTheRangeAndAClearShot()
    {
        // ARC-COMBAT-015 (ledger RH1): distance at most max(range, 1), cost below 26, no blocker but the target.
        var open = new LineOfFireTrace<string>(0, null, false, []);
        Assert.IsTrue(LineOfFireRules.InReach(10, 10, open, "target"));
        Assert.IsFalse(LineOfFireRules.InReach(11, 10, open, "target"), "beyond the range");
        Assert.IsTrue(LineOfFireRules.InReach(1, 0, open, "target"), "a range below 1 reaches 1");
        Assert.IsFalse(LineOfFireRules.InReach(2, 0, open, "target"));
        Assert.IsTrue(LineOfFireRules.InReach(3, 10, new LineOfFireTrace<string>(25, null, false, []), "target"));
        Assert.IsFalse(LineOfFireRules.InReach(3, 10, new LineOfFireTrace<string>(26, null, false, []), "target"), "cost 26 is no clear shot");
        Assert.IsFalse(LineOfFireRules.InReach(3, 10, new LineOfFireTrace<string>(0, "crate", false, []), "target"), "another blocker");
        var target = "target";
        Assert.IsTrue(LineOfFireRules.InReach(3, 10, new LineOfFireTrace<string>(0, target, false, []), target), "the target itself");
        // Two see-through cover objects (20 each) on the way deny the shot; one does not.
        var map = new Map().With(2, 4, "screen", Cover);
        Assert.IsTrue(LineOfFireRules.InReach(6, 10, Trace(map, (2, 2), (2, 8)), "target"));
        Assert.IsFalse(LineOfFireRules.InReach(6, 10, Trace(map.With(2, 5, "screen2", Cover), (2, 2), (2, 8)), "target"));
    }
}
