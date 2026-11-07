using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-008: overlay classification against hand-written literal verdict tables
/// (an independent fake authority), not the overlay builder as its own oracle.</summary>
[TestClass]
public sealed class PassabilityOverlayTests
{
    private const OverlayStepVerdict O = OverlayStepVerdict.Open, B = OverlayStepVerdict.Blocked,
        U = OverlayStepVerdict.Unknown, X = OverlayStepVerdict.Unsupported;

    /// <summary>Literal directed verdicts keyed (from, to); anything unlisted is Unsupported (outside coverage).</summary>
    private sealed class Table : IPassabilityAuthority
    {
        public readonly Dictionary<(CellCoord From, CellCoord To), OverlayStepVerdict> Steps = new();
        public readonly List<(CellCoord From, CellCoord To, NeighborDirection Direction)> Requests = new();
        public Exception? Throw;
        public string Identity { get; init; } = "fake|digest|1|context";
        public string ProfileId => "FAKE";
        public string ActorId => "FAKE_ACTOR";
        public string CoverageDescription => "literal table";
        public object Owner => this;
        public (int MinX, int MinY, int MaxX, int MaxY) Chart { get; init; } = (0, 0, 9, 9);
        public (int MinX, int MinY, int MaxX, int MaxY) Envelope { get; init; } = (-2, -2, 11, 11);
        public OverlayStep Assess(CellCoord from, CellCoord to, NeighborDirection direction)
        {
            Requests.Add((from, to, direction));
            if (Throw is not null) throw Throw;
            return Steps.TryGetValue((from, to), out var verdict) ? new(verdict, verdict.ToString().ToUpperInvariant()) : new(X, "CONTEXT_UNSUPPORTED");
        }
        // Sets all eight incoming verdicts of a cell (order: NeighborDirection, i.e. delta FROM source TO cell).
        public void Into(int x, int y, params OverlayStepVerdict[] verdicts)
        {
            (int Dx, int Dy)[] deltas = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
            for (var d = 0; d < 8; d++) Steps[(new(x - deltas[d].Dx, y - deltas[d].Dy), new(x, y))] = verdicts[d];
        }
    }

    private static OverlayCell Cell(Table table, int x, int y)
    {
        var overlay = new PassabilityOverlay(table);
        Assert.AreEqual(1, overlay.Fill([new(x, y)], 10));
        Assert.IsTrue(overlay.TryGet(new(x, y), out var cell));
        return cell;
    }

    [TestMethod]
    public void IncomingSourcesFollowTheNeighbourDirectionDeltas()
    {
        // Literal: the step INTO (5,5) in direction XPlus (+1,0) comes from (4,5); XMinusYMinus (-1,-1) from (6,6).
        Assert.AreEqual(new CellCoord(4, 5), PassabilityOverlay.Source(new(5, 5), NeighborDirection.XPlus));
        Assert.AreEqual(new CellCoord(6, 6), PassabilityOverlay.Source(new(5, 5), NeighborDirection.XMinusYMinus));
        Assert.AreEqual(new CellCoord(5, 4), PassabilityOverlay.Source(new(5, 5), NeighborDirection.YPlus));
        var table = new Table(); table.Into(5, 5, O, O, O, O, O, O, O, O);
        Cell(table, 5, 5);
        Assert.IsTrue(table.Requests.All(r => r.To == new CellCoord(5, 5)));
        Assert.IsTrue(table.Requests.Contains((new CellCoord(4, 5), new CellCoord(5, 5), NeighborDirection.XPlus)));
    }

    [TestMethod]
    public void OpenCellHasNoFillAndNoMarks()
    {
        var table = new Table(); table.Into(5, 5, O, O, O, O, O, O, O, O);
        var cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Open, cell.Kind);
        Assert.AreEqual(0, cell.BlockedEdges.Count() + cell.UnknownEdges.Count());
    }

    [TestMethod]
    public void FillRequiresEveryAdmittedIncomingStepBlocked()
    {
        var table = new Table(); table.Into(5, 5, B, B, X, B, B, X, B, B);
        var cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Blocked, cell.Kind);
        Assert.AreEqual(0, cell.BlockedEdges.Count(), "a filled cell needs no separate edge marks");
        // One Open incoming step forbids the fill; the blocked ones stay directed edges.
        table = new Table(); table.Into(5, 5, B, B, B, B, B, B, B, O);
        cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Open, cell.Kind);
        CollectionAssert.AreEqual(new[] { NeighborDirection.XMinusYMinus, NeighborDirection.XMinus, NeighborDirection.XMinusYPlus, NeighborDirection.YPlus,
            NeighborDirection.XPlusYPlus, NeighborDirection.XPlus, NeighborDirection.XPlusYMinus }, cell.BlockedEdges.Select(e => e.Direction).ToArray());
    }

    [TestMethod]
    public void DiagonalOnlyBlocksAreNotAProvenBlockedCell()
    {
        // Only the four map diagonals are admitted and all are Blocked: shoulders could explain them,
        // so the cell is Unknown (gray) with four red directed marks, never a red fill.
        var table = new Table(); table.Into(5, 5, B, X, B, X, B, X, B, X);
        var cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Unknown, cell.Kind);
        Assert.AreEqual(4, cell.BlockedEdges.Count());
        // One admitted map-cardinal Blocked step proves the destination blocker.
        table = new Table(); table.Into(5, 5, B, X, B, B, B, X, B, X);
        Assert.AreEqual(OverlayCellKind.Blocked, Cell(table, 5, 5).Kind);
    }

    [TestMethod]
    public void EdgeOnlyBlockerStaysDirectedAndAsymmetric()
    {
        // (4,5)->(5,5) Blocked, (5,5)->(4,5) Open: only (5,5) carries the mark, from the west neighbour.
        var table = new Table();
        table.Into(5, 5, O, O, O, O, O, B, O, O);
        table.Into(4, 5, O, O, O, O, O, O, O, O);
        var overlay = new PassabilityOverlay(table);
        overlay.Fill([new(5, 5), new(4, 5)], 10);
        overlay.TryGet(new(5, 5), out var east); overlay.TryGet(new(4, 5), out var west);
        Assert.AreEqual(OverlayCellKind.Open, east.Kind);
        Assert.AreEqual(new CellCoord(4, 5), east.BlockedEdges.Single().From);
        Assert.AreEqual(NeighborDirection.XPlus, east.BlockedEdges.Single().Direction);
        Assert.AreEqual(OverlayCellKind.Open, west.Kind);
        Assert.AreEqual(0, west.BlockedEdges.Count());
    }

    [TestMethod]
    public void UnknownNeverBecomesOpenOrBlocked()
    {
        var table = new Table(); table.Into(5, 5, U, B, X, B, U, B, X, B);
        var cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Unknown, cell.Kind, "Unknown + Blocked without Open is not a proven blocked cell");
        Assert.AreEqual(4, cell.BlockedEdges.Count());
        // An Unknown diagonal into an Open cell is a directed gray mark, never Open.
        table = new Table(); table.Into(5, 5, U, O, O, O, O, O, O, O);
        cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Open, cell.Kind);
        Assert.AreEqual(NeighborDirection.XMinusYMinus, cell.UnknownEdges.Single().Direction);
    }

    [TestMethod]
    public void OutsideCoverageAndContextMismatchAreNeverOpen()
    {
        var table = new Table(); // nothing listed: every request answers Unsupported CONTEXT_UNSUPPORTED
        var cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayCellKind.Outside, cell.Kind);
        Assert.IsTrue(cell.Incoming.All(i => i.Step.Reason == "CONTEXT_UNSUPPORTED"));
    }

    [TestMethod]
    public void ChartBoundaryAndOutsideChartMakeNoRequests()
    {
        var table = new Table(); table.Into(0, 0, O, O, O, O, O, O, O, O);
        var overlay = new PassabilityOverlay(table);
        overlay.Fill([new(0, 0)], 10);
        // At the chart corner only the three in-chart sources (1,0),(1,1),(0,1) are requested.
        CollectionAssert.AreEquivalent(new[] { new CellCoord(1, 1), new CellCoord(1, 0), new CellCoord(0, 1) }, table.Requests.Select(r => r.From).ToArray());
        overlay.TryGet(new(0, 0), out var corner);
        Assert.AreEqual(5, corner.Incoming.Count(i => i.Step.Reason == "OVERLAY_OUTSIDE_CHART"));
        Assert.AreEqual(OverlayCellKind.Open, corner.Kind);
        var before = table.Requests.Count;
        overlay.Fill([new(-1, 0), new(11, 11)], 10);
        Assert.AreEqual(before, table.Requests.Count, "cells outside the chart are Outside without a request");
        overlay.TryGet(new(-1, 0), out var outside);
        Assert.AreEqual(OverlayCellKind.Outside, outside.Kind);
    }

    [TestMethod]
    public void NeighbourMissingFromEvidenceIsUnsupportedNotOpen()
    {
        var table = new Table(); table.Into(5, 5, O, O, O, O, O, O, O, O);
        table.Steps.Remove((new(4, 5), new(5, 5)));
        var cell = Cell(table, 5, 5);
        Assert.AreEqual(OverlayStepVerdict.Unsupported, cell.Incoming[(int)NeighborDirection.XPlus].Step.Verdict);
        Assert.AreEqual(OverlayCellKind.Open, cell.Kind);
    }

    [TestMethod]
    public void BudgetIsDeterministicAndCachedCellsAreNotReassessed()
    {
        var table = new Table();
        var overlay = new PassabilityOverlay(table);
        CellCoord[] cells = [new(1, 1), new(2, 1), new(3, 1)];
        Assert.AreEqual(2, overlay.Fill(cells, 2));
        Assert.AreEqual(16, overlay.AssessCalls);
        Assert.IsTrue(overlay.TryGet(new(2, 1), out _)); Assert.IsFalse(overlay.TryGet(new(3, 1), out _));
        Assert.AreEqual(1, overlay.Fill(cells, 2));
        Assert.AreEqual(24, overlay.AssessCalls);
        Assert.AreEqual(0, overlay.Fill(cells, 2));
        Assert.AreEqual(24, overlay.AssessCalls, "a cached cell is never reassessed");
    }

    [TestMethod]
    public void AuthorityFailureIsNotUnknownEvidence()
    {
        var table = new Table(); table.Into(5, 5, O, O, O, O, O, O, O, O);
        var overlay = new PassabilityOverlay(table);
        overlay.Fill([new(5, 5)], 10);
        table.Throw = new InvalidOperationException("GRID_FAILURE");
        overlay.Fill([new(6, 6)], 10);
        Assert.AreEqual("OVERLAY_AUTHORITY_FAILED", overlay.Failure);
        Assert.AreEqual(0, overlay.CachedCells, "no stale or partial cells survive a failed authority");
        Assert.AreEqual(0, overlay.Fill([new(7, 7)], 10));
    }

    [TestMethod]
    public void ReplacementAuthorityStartsEmpty()
    {
        var first = new Table(); first.Into(5, 5, B, B, B, B, B, B, B, B);
        var overlay = new PassabilityOverlay(first); overlay.Fill([new(5, 5)], 10);
        var second = new Table { Identity = "fake|digest|2|context" }; second.Into(5, 5, O, O, O, O, O, O, O, O);
        var replacement = new PassabilityOverlay(second);
        Assert.AreNotEqual(overlay.Identity, replacement.Identity);
        Assert.AreEqual(0, replacement.CachedCells);
        replacement.Fill([new(5, 5)], 10);
        replacement.TryGet(new(5, 5), out var cell);
        Assert.AreEqual(OverlayCellKind.Open, cell.Kind, "no red cell from the previous snapshot");
    }

    [TestMethod]
    public void CountsAndVisibleCellsAreLiteral()
    {
        var table = new Table();
        table.Into(5, 5, B, B, B, B, B, B, B, B);
        table.Into(6, 5, O, O, O, O, O, B, O, O);
        table.Into(7, 5, U, X, X, X, X, X, X, X);
        var overlay = new PassabilityOverlay(table);
        CellCoord[] cells = [new(5, 5), new(6, 5), new(7, 5), new(8, 5), new(-1, 0)];
        overlay.Fill(cells, 10);
        Assert.AreEqual(new OverlayCounts(Open: 1, Blocked: 1, Unknown: 1, Outside: 2, BlockedEdges: 1, UnknownEdges: 0), overlay.Count(cells));
        // Cell (X,Y) top vertex is (40*(Y-X), 20*(Y+X)); a rectangle around (5,5)'s diamond [(0,200)..(40,240)] holds it.
        var visible = overlay.Visible(-1, 201, 1, 239, 100);
        Assert.IsTrue(visible.Contains(new CellCoord(5, 5)));
        Assert.IsFalse(visible.Contains(new CellCoord(9, 9)));
        Assert.AreEqual(3, overlay.Visible(-1000, -1000, 1000, 1000, 3).Count, "the cap bounds the visible set");
        // ARC-RENDER-011: the AREA chart draws diamonds from the terrain tile origin, 40 px to the right.
        var area = new PassabilityOverlay(table, originX: 40);
        Assert.AreEqual(40, area.OriginX);
        Assert.IsTrue(area.Visible(39, 201, 41, 239, 100).Contains(new CellCoord(5, 5)));
        Assert.IsFalse(area.Visible(-81, 201, -79, 239, 100).Contains(new CellCoord(5, 5)), "the unshifted diamond's left vertex is outside");
    }

    // The production AREA assessor (public strict owner fixture: blocking scenery at (5,1), OF_NO_BLOCK canopy at (6,1),
    // core sectors 1452..1453 x 1290..1291 only), checked against literal expectations stated in AreaGridSessionTests.
    [TestMethod]
    public void ProductionAreaAssessorDrivesTheOverlayReadOnly()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var steps = AreaOwnerFixture.Steps(source.Owner.LoadStrict(), source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        var session = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8));
        var state = session.State;
        var overlay = new PassabilityOverlay(new AreaPassabilityAuthority(session));
        CellCoord blocker = new(5, 1), canopy = new(6, 1), open = new(7, 1), corner = new(5, 0), west = new(-1, 1);
        overlay.Fill([blocker, canopy, open, corner, west], 100);
        overlay.TryGet(blocker, out var b); overlay.TryGet(canopy, out var c); overlay.TryGet(open, out var o);
        overlay.TryGet(corner, out var k); overlay.TryGet(west, out var w);
        Assert.AreEqual(OverlayCellKind.Blocked, b.Kind);
        Assert.IsTrue(b.Incoming.All(i => i.Step == new OverlayStep(OverlayStepVerdict.Blocked, "DESTINATION_OBJECT_BLOCKER")));
        // Asymmetric: (6,1)->(5,1) Blocked, (5,1)->(6,1) Open (the source cell's scenery is not consulted).
        Assert.AreEqual(OverlayCellKind.Open, c.Kind);
        Assert.AreEqual(new OverlayStep(OverlayStepVerdict.Open, "COMPLETE_NONBLOCKING_EVIDENCE"), c.Incoming[(int)NeighborDirection.XPlus].Step);
        Assert.AreEqual(OverlayCellKind.Open, o.Kind);
        // Edge-only: the map diagonal (6,1)->(5,0) passes the (5,1) blocker as a shoulder.
        Assert.AreEqual(OverlayCellKind.Open, k.Kind);
        var shoulder = k.BlockedEdges.Single(e => e.From == canopy);
        Assert.AreEqual((NeighborDirection.XMinusYMinus, "SHOULDER_OBJECT_BLOCKER"), (shoulder.Direction, shoulder.Step.Reason));
        // Sector 1451 is not admitted by this fixture: outside coverage, never Open.
        Assert.AreEqual(OverlayCellKind.Outside, w.Kind);
        Assert.IsTrue(w.Incoming.All(i => i.Step.Verdict == OverlayStepVerdict.Unsupported));
        Assert.AreSame(state, session.State, "the overlay never advances or replaces movement state");
        Assert.IsNull(session.LastAdvance);
        // Unresolved tile names: Unknown evidence draws as Unknown, never Open.
        using var unknownSource = new AreaOwnerFixture(motionBodies: true);
        var unknownSteps = AreaOwnerFixture.Steps(unknownSource.Owner.LoadStrict(), unknownSource.Streams[new(AreaSourceRole.Module)].Bytes, 7, tileNamesResolved: false);
        var unknown = new PassabilityOverlay(new AreaPassabilityAuthority(new AreaGridSession(unknownSteps, SimulationTime.At(0), StepDuration.Of(8))));
        unknown.Fill([open], 10); unknown.TryGet(open, out var u);
        Assert.AreEqual(OverlayCellKind.Unknown, u.Kind);
    }

    [TestMethod]
    public void AreaAuthorityFollowsTheSessionChart()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var steps = AreaOwnerFixture.Steps(source.Owner.LoadStrict(), source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        // Chart origin at the second core sector (1453,1290); the fixture admits sectors 1452..1453 x 1290..1291.
        var chart = new AreaChart(1453 * 64, 1290 * 64, new(-58, 1), "TEST_SECOND_SECTOR_CHART");
        var session = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(8), chart: chart);
        var authority = new AreaPassabilityAuthority(session);
        Assert.AreEqual((-64, 0, 63, 127), authority.Chart);
        Assert.AreEqual((-128, -64, 127, 191), authority.Envelope);
        Assert.IsTrue(authority.Identity.EndsWith("|TEST_SECOND_SECTOR_CHART", StringComparison.Ordinal));
        // The blocker at exterior (5,1) is chart (-59,1): the same assessment through the chart's world cells.
        var direct = steps.Assess(steps.Request(chart.ToWorld(new(-58, 1)), chart.ToWorld(new(-59, 1)), Arcanum.Data.Traversal.CardinalTraversalDirection.XMinus));
        Assert.AreEqual(new OverlayStep(OverlayStepVerdict.Blocked, direct.ReasonCode), authority.Assess(new(-58, 1), new(-59, 1), NeighborDirection.XMinus));
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", direct.ReasonCode);
    }
}
