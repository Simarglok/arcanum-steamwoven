using System.Diagnostics;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b) opt-in (<c>DAT_ROOT</c>, the game folder): the sector port over the main world map. One sector reads with its objects and
/// admits only itself; the evidence of the nine sectors around the Crash Site, joined, assesses every step of their 192 × 192 cells (all eight
/// directions) exactly as the radius-1 window's evidence does (ARC-DATA-034), the borders between sectors included; sectors outside the map
/// and a sector the map does not give are typed. Only counts and times are printed.
/// </summary>
[TestClass]
public sealed class SectorTerrainRealDataTests
{
    private const string MainMap = "arcanum1-024-fixed";

    private static string Root()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        return root;
    }

    [TestMethod, TestCategory("RealData")]
    public void One_sector_reads_with_its_objects_and_admits_only_itself()
    {
        using var port = ContentSectorTerrain.Open(Root(), MainMap, out var detail) ?? throw new AssertFailedException(detail);
        var clock = Stopwatch.StartNew();
        var read = port.Read(new(1452, 1291));
        var first = clock.ElapsedMilliseconds;
        clock.Restart();
        var again = port.Read(new(1452, 1291));
        Console.WriteLine($"ARC-DEV-118 sector 1452,1291 status={read.Status} {read.Detail} firstMs={first} secondMs={clock.ElapsedMilliseconds}");
        Assert.AreEqual(SectorTerrainStatus.Ready, read.Status, read.Detail);
        Assert.AreEqual(SectorTerrainStatus.Ready, again.Status);
        var terrain = read.Terrain!;
        CollectionAssert.AreEqual(new[] { (1452L, 1291L) }, terrain.Steps.AdmittedSectors.ToArray());
        Assert.AreEqual(4096, terrain.Steps.AdmittedCells);
        Assert.IsTrue(terrain.Objects.Count > 0);
        Assert.IsTrue(terrain.Objects.All(o => o.WorldX >> 6 == 1452 && o.WorldY >> 6 == 1291));
        Assert.AreEqual(terrain.Steps.EvidenceSha256, again.Terrain!.Steps.EvidenceSha256, "the same read twice gives the same evidence");
    }

    [TestMethod, TestCategory("RealData")]
    public void Sectors_outside_the_map_are_typed_not_thrown()
    {
        using var port = ContentSectorTerrain.Open(Root(), MainMap, out var detail) ?? throw new AssertFailedException(detail);
        Assert.AreEqual(SectorTerrainStatus.OutsideMap, port.Read(new(-1, 5)).Status);
        Assert.AreEqual(SectorTerrainStatus.OutsideMap, port.Read(new(1_000_000, 5)).Status);
        Assert.IsNull(ContentSectorTerrain.Open(Root(), "no such map", out var why));
        StringAssert.Contains(why, "unknown map");
    }

    [TestMethod, TestCategory("RealData")]
    public void Joined_sector_evidence_assesses_every_step_as_the_window_does()
    {
        var root = Root();
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        using var port = new ContentSectorTerrain(stack, catalog, MainMap);
        var parts = new List<AreaStepSnapshot>();
        var clock = Stopwatch.StartNew();
        for (var y = 1290; y <= 1292; y++)
            for (var x = 1451; x <= 1453; x++)
            {
                var read = port.Read(new(x, y));
                Assert.AreEqual(SectorTerrainStatus.Ready, read.Status, $"{x},{y} {read.Detail}");
                parts.Add(read.Terrain!.Steps);
            }
        var readMs = clock.ElapsedMilliseconds;
        var joined = AreaStepSnapshot.Union(parts);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, new(1452, 1291));
        Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, planned.Detail);
        using var owner = planned.Plan!.Open();
        var windowSnapshot = owner.Inspect().Snapshot!;
        var window = AreaStepInspector.InspectWholeMap(windowSnapshot, stack, planned.Plan.Map.AggregatePath, 1);
        Assert.AreEqual(window.AdmittedCells, joined.AdmittedCells, "the same 3 x 3 core sectors");
        // The window's own evidence, restricted sector by sector and joined, is the same evidence again (the seeded path of the chart).
        var seeded = new WindowSectorTerrain(windowSnapshot, window, port);
        var seededJoined = AreaStepSnapshot.Union(Enumerable.Range(0, 9).Select(i => seeded.Read(new(1451 + i % 3, 1290 + i / 3))).Select(r => { Assert.AreEqual(SectorTerrainStatus.Ready, r.Status); return r.Terrain!.Steps; }).ToArray());
        Assert.AreEqual(window.AdmittedCells, seededJoined.AdmittedCells);
        var objectsFromWindow = seeded.Read(new(1452, 1291)).Terrain!.Objects.Count;
        Assert.AreEqual(port.Read(new(1452, 1291)).Terrain!.Objects.Count, objectsFromWindow, "the window's rows of a sector are the port's");
        long steps = 0, different = 0, blocked = 0, open = 0, unknown = 0;
        string? first = null;
        var directions = Enum.GetValues<CardinalTraversalDirection>();
        for (var wx = 1451L * 64; wx < 1454L * 64; wx++)
            for (var wy = 1290L * 64; wy < 1293L * 64; wy++)
                foreach (var direction in directions)
                {
                    var from = new AreaStepCell(wx, wy);
                    var to = AreaWindowRouteStep(from, direction);
                    if (!window.IsAdmitted(to) || !joined.IsAdmitted(to)) continue;
                    var a = window.Assess(window.Request(from, to, direction));
                    var b = joined.Assess(joined.Request(from, to, direction));
                    var c = seededJoined.Assess(seededJoined.Request(from, to, direction));
                    steps++;
                    if (a.Disposition == CardinalTraversalDisposition.Open) open++; else if (a.Disposition == CardinalTraversalDisposition.Blocked) blocked++; else unknown++;
                    if (a.Coverage != b.Coverage || a.Disposition != b.Disposition || a.ReasonCode != b.ReasonCode || a.Gaps.Count != b.Gaps.Count
                        || a.Coverage != c.Coverage || a.Disposition != c.Disposition || a.ReasonCode != c.ReasonCode || a.Gaps.Count != c.Gaps.Count)
                    { different++; first ??= $"{from}->{to} {a.ReasonCode} vs {b.ReasonCode} vs {c.ReasonCode}"; }
                }
        Console.WriteLine($"ARC-DEV-118 joined nine sectors readMs={readMs} steps={steps} open={open} blocked={blocked} unknown={unknown} different={different} {first}");
        Assert.IsTrue(steps > 250_000);
        Assert.AreEqual(0, different, first);
    }

    private static AreaStepCell AreaWindowRouteStep(AreaStepCell cell, CardinalTraversalDirection direction) => direction switch
    {
        CardinalTraversalDirection.XMinusYMinus => new(cell.X - 1, cell.Y - 1),
        CardinalTraversalDirection.XMinus => new(cell.X - 1, cell.Y),
        CardinalTraversalDirection.XMinusYPlus => new(cell.X - 1, cell.Y + 1),
        CardinalTraversalDirection.YPlus => new(cell.X, cell.Y + 1),
        CardinalTraversalDirection.XPlusYPlus => new(cell.X + 1, cell.Y + 1),
        CardinalTraversalDirection.XPlus => new(cell.X + 1, cell.Y),
        CardinalTraversalDirection.XPlusYMinus => new(cell.X + 1, cell.Y - 1),
        _ => new(cell.X, cell.Y - 1),
    };
}
