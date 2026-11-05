using System.Collections.Immutable;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Residency;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b): the loaded-sector chart over invented sectors (64 × 64 open cells each, one blocked cell where a test wants it), no original
/// data: the evidence of several sectors joins into one that assesses a step across their border, a window's evidence restricts and joins again, and the
/// chart follows the cache's slots (read what entered, drop what left, ask a failed sector once until it leaves).
/// </summary>
[TestClass]
public sealed class LoadedSectorChartTests
{
    private const string Profile = "arc-data-019-whole-map-original-rules-v1";

    private static AreaStepSnapshot Sector(long sx, long sy, long epoch = 1, params (long X, long Y)[] blocked)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = sx * 64; x < sx * 64 + 64; x++)
            for (var y = sy * 64; y < sy * 64 + 64; y++)
            {
                var wall = blocked.Contains((x, y));
                terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, wall ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open,
                    AreaStepTerrainSource.TileName, "TEST");
            }
        return new(new(AreaStepInputKind.Structural, Profile, [], $"sector-{sx}-{sy}"), epoch, [(sx, sy)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    private static AreaStepAssessment Step(AreaStepSnapshot steps, AreaStepCell from, AreaStepCell to, CardinalTraversalDirection direction) => steps.Assess(steps.Request(from, to, direction));

    [TestMethod]
    public void A_union_assesses_a_step_across_the_border_of_its_sectors()
    {
        var left = Sector(2, 3); var right = Sector(3, 3, 1, (64 * 3 + 1, 64 * 3 + 5));
        var from = new AreaStepCell(64 * 3 - 1, 64 * 3 + 5);
        var across = Step(AreaStepSnapshot.Union([left, right]), from, new(64 * 3, 64 * 3 + 5), CardinalTraversalDirection.XPlus);
        Assert.AreEqual(CardinalTraversalDisposition.Open, across.Disposition, across.ReasonCode);
        var blocked = Step(AreaStepSnapshot.Union([right, left]), new(64 * 3, 64 * 3 + 5), new(64 * 3 + 1, 64 * 3 + 5), CardinalTraversalDirection.XPlus);
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, blocked.Disposition, "a cell's own verdict is kept");
        Assert.AreEqual("DESTINATION_OUTSIDE_ADMITTED_AREA", Step(left, from, new(64 * 3, 64 * 3 + 5), CardinalTraversalDirection.XPlus).ReasonCode,
            "one sector alone refuses the same step");
        var union = AreaStepSnapshot.Union([left, right]);
        CollectionAssert.AreEquivalent(new[] { (2L, 3L), (3L, 3L) }, union.AdmittedSectors.ToArray());
        Assert.AreEqual(8192, union.AdmittedCells);
        Assert.AreEqual(union.EvidenceSha256, AreaStepSnapshot.Union([left, right]).EvidenceSha256, "the same parts give the same digest");
        Assert.AreNotEqual(union.EvidenceSha256, AreaStepSnapshot.Union([left]).EvidenceSha256);
    }

    [TestMethod]
    public void A_union_refuses_what_it_cannot_join()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AreaStepSnapshot.Union([]));
        Assert.ThrowsExactly<ArgumentException>(() => AreaStepSnapshot.Union([Sector(1, 1), Sector(1, 1)]), "a sector twice");
        Assert.ThrowsExactly<ArgumentException>(() => AreaStepSnapshot.Union([Sector(1, 1, 1), Sector(2, 1, 2)]), "different epochs");
    }

    [TestMethod]
    public void A_union_can_restamp_parts_of_different_epochs_and_the_chart_holds_a_window_and_reads_together()
    {
        // A window's evidence carries its load's generation and a sector read another epoch; the chart joins them under its own.
        var joined = AreaStepSnapshot.Union([Sector(1, 1, 7), Sector(2, 1, 1)], 3);
        Assert.AreEqual(3, joined.EvidenceEpoch);
        Assert.AreEqual(8192, joined.AdmittedCells);
        var port = new EpochPort { Epoch = { [new(5, 5)] = 41, [new(6, 5)] = 1 } };
        var chart = new LoadedSectorChart(port);
        var done = chart.Sync(Slots((5, 5), (6, 5)));
        Assert.AreEqual(2, done.Read.Count);
        Assert.AreEqual(LoadedSectorChart.ChartEpoch, chart.Steps!.EvidenceEpoch);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(chart.Steps, new(64 * 6 - 1, 64 * 5), new(64 * 6, 64 * 5), CardinalTraversalDirection.XPlus).Disposition);
        chart.Sync(Slots((6, 5), (7, 5)));
        Assert.AreEqual(LoadedSectorChart.ChartEpoch, chart.Steps!.EvidenceEpoch, "a later sync keeps the chart's epoch");
    }

    private sealed class EpochPort : ISectorTerrain
    {
        public Dictionary<ResidentSector, long> Epoch { get; } = [];
        public SectorTerrainRead Read(ResidentSector sector) =>
            new(SectorTerrainStatus.Ready, new(sector, Sector(sector.X, sector.Y, Epoch.GetValueOrDefault(sector, 1)), []), "ok");
    }

    [TestMethod]
    public void Forgetting_the_failures_asks_the_port_again_and_a_stop_ends_a_sync_between_reads()
    {
        var port = new Port();
        port.Missing.Add(new(9, 9));
        var chart = new LoadedSectorChart(port);
        chart.Sync(Slots((9, 9)));
        port.Reads.Clear();
        chart.Sync(Slots((9, 9)));
        Assert.AreEqual(0, port.Reads.Count);
        port.Missing.Clear();
        chart.ForgetFailures();
        var again = chart.Sync(Slots((9, 9)));
        Assert.AreEqual(1, again.Read.Count, "the sector the port can read now is read");
        port.Reads.Clear();
        var stopped = chart.Sync(Slots((9, 9), (10, 9), (11, 9)), () => true);
        Assert.AreEqual(0, port.Reads.Count, "a stopped sync reads nothing more");
        Assert.AreEqual(0, stopped.Read.Count);
    }

    [TestMethod]
    public void A_restriction_keeps_the_wanted_sectors_and_joins_again()
    {
        var window = AreaStepSnapshot.Union([Sector(0, 0), Sector(1, 0, 1, (64 + 3, 7)), Sector(2, 0)]);
        var one = window.Restrict([(1L, 0L), (9L, 9L)]);
        CollectionAssert.AreEqual(new[] { (1L, 0L) }, one.AdmittedSectors.ToArray());
        Assert.AreEqual(4096, one.AdmittedCells);
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, Step(one, new(64 + 2, 7), new(64 + 3, 7), CardinalTraversalDirection.XPlus).Disposition);
        Assert.ThrowsExactly<ArgumentException>(() => window.Restrict([(5L, 5L)]), "no admitted sector left");
        var again = AreaStepSnapshot.Union([window.Restrict([(0L, 0L)]), window.Restrict([(1L, 0L)])]);
        Assert.AreEqual(8192, again.AdmittedCells);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(again, new(63, 7), new(64, 7), CardinalTraversalDirection.XPlus).Disposition, "across the joined border");
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, Step(again, new(64 + 2, 7), new(64 + 3, 7), CardinalTraversalDirection.XPlus).Disposition);
    }

    private sealed class Port : ISectorTerrain
    {
        public List<ResidentSector> Reads { get; } = [];
        public HashSet<ResidentSector> Missing { get; } = [];
        public SectorTerrainRead Read(ResidentSector sector)
        {
            Reads.Add(sector);
            return Missing.Contains(sector) ? new(SectorTerrainStatus.Unsupported, null, "no source")
                : new(SectorTerrainStatus.Ready, new(sector, Sector(sector.X, sector.Y), []), "ok");
        }
    }

    private static ImmutableArray<SectorCacheSlot> Slots(params (int X, int Y)[] sectors) =>
        [.. sectors.Select((s, i) => new SectorCacheSlot(SectorCacheRules.SectorId(s.X, s.Y), i + 1))];

    [TestMethod]
    public void The_chart_reads_what_entered_the_cache_and_drops_what_left()
    {
        var port = new Port();
        var chart = new LoadedSectorChart(port);
        Assert.IsNull(chart.Steps);
        var first = chart.Sync(Slots((5, 5), (6, 5)));
        CollectionAssert.AreEqual(new[] { new ResidentSector(5, 5), new ResidentSector(6, 5) }, first.Read.ToArray());
        Assert.IsTrue(first.Changed);
        var steps = chart.Steps!;
        Assert.AreEqual(8192, steps.AdmittedCells);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(steps, new(64 * 6 - 1, 64 * 5), new(64 * 6, 64 * 5), CardinalTraversalDirection.XPlus).Disposition);

        port.Reads.Clear();
        var same = chart.Sync(Slots((6, 5), (5, 5)));
        Assert.IsFalse(same.Changed);
        Assert.AreEqual(0, port.Reads.Count, "nothing is read for sectors the chart holds");
        Assert.AreSame(steps, chart.Steps, "an unchanged set keeps the evidence object");

        var moved = chart.Sync(Slots((6, 5), (7, 5)));
        CollectionAssert.AreEqual(new[] { new ResidentSector(5, 5) }, moved.Dropped.ToArray());
        CollectionAssert.AreEqual(new[] { new ResidentSector(7, 5) }, moved.Read.ToArray());
        CollectionAssert.AreEqual(new[] { new ResidentSector(6, 5), new ResidentSector(7, 5) }, chart.Sectors.ToArray());
        Assert.IsNull(chart.Terrain(new(5, 5)));
        Assert.IsNotNull(chart.Terrain(new(7, 5)));
        Assert.AreEqual(8192, steps.AdmittedCells, "evidence handed out before is never mutated");
        CollectionAssert.AreEquivalent(new[] { (6L, 5L), (7L, 5L) }, chart.Steps!.AdmittedSectors.ToArray());

        var empty = chart.Sync([]);
        Assert.AreEqual(2, empty.Dropped.Count);
        Assert.IsNull(chart.Steps);
    }

    [TestMethod]
    public void A_sector_that_cannot_be_read_is_asked_once_until_it_leaves_the_cache()
    {
        var port = new Port();
        port.Missing.Add(new(9, 9));
        var chart = new LoadedSectorChart(port);
        var first = chart.Sync(Slots((8, 9), (9, 9)));
        Assert.AreEqual(1, first.Failed.Count);
        StringAssert.Contains(first.Failed[0].Detail, "Unsupported");
        Assert.AreEqual(new ResidentSector(9, 9), first.Failed[0].Sector);
        CollectionAssert.AreEqual(new[] { new ResidentSector(8, 9) }, chart.Sectors.ToArray(), "the readable sector is held");
        port.Reads.Clear();
        chart.Sync(Slots((8, 9), (9, 9)));
        Assert.AreEqual(0, port.Reads.Count, "the failed sector is not asked again every frame");
        chart.Sync(Slots((8, 9)));
        port.Missing.Clear();
        var back = chart.Sync(Slots((8, 9), (9, 9)));
        CollectionAssert.AreEqual(new[] { new ResidentSector(9, 9) }, back.Read.ToArray(), "asked again after it left and entered the cache");
    }
}
