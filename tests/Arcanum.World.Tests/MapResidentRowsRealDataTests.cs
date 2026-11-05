using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-103 (MIG9 S3) opt-in (<c>DAT_ROOT</c>, the game folder): every map a whole-map plan loads gives residency rows the session opens.
/// Each interior is loaded strictly as the viewer loads it (snapshot, whole-map steps, inventory seed), its rows are built by
/// <see cref="MapResidentRows"/> and opened in a session standing on it, and every row is a resident. A map whose inventory seed the reader refuses
/// has no rows (counted). Only counts are printed.
/// </summary>
[TestClass]
public sealed class MapResidentRowsRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_whole_map_builds_rows_the_session_opens()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        int maps = 0, windows = 0, noInventory = 0, rows = 0, largest = 0, statics = 0, held = 0, detached = 0;
        var failures = new List<string>();
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) { windows++; continue; }
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            if (created.Status != AreaMapPlanStatus.Ready) { failures.Add($"{map.Name}: plan {created.Status}"); continue; }
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, created.Plan.Map.AggregatePath, 1);
            var inventory = AreaInventoryMap.Build(snapshot, steps);
            // The inventory reader's own refusals (ARC-DATA-012's bounds) leave the map without a seed, so without residency rows: counted, not failed.
            if (inventory.Map is null) { noInventory++; continue; }
            var build = MapResidentRows.Build(snapshot, inventory.Map);
            if (build.Status != MapResidentRowsStatus.Ready) { failures.Add($"{map.Name}: rows {build.Status} {build.Detail}"); continue; }
            using var session = MapResidentRowsTests.Session(snapshot.RuntimeMap);
            var opened = MapResidentRows.Open(session, new SessionMapId(snapshot.RuntimeMap), build);
            if (opened.Status != ResidencyStatus.Open) { failures.Add($"{map.Name}: open {opened.Status} {opened.Detail}"); continue; }
            Assert.AreEqual(build.Rows!.Count + 1, session.QueryResidents(new SessionMapId(snapshot.RuntimeMap)).Residents.Count, map.Name + ": every row and the hero");
            maps++; rows += build.Rows.Count; largest = Math.Max(largest, build.Rows.Count); statics += build.StaticRows;
            held += build.Rows.Count(row => row.Holder is not null); detached += build.Rows.Count(row => row.Cell is null && row.Holder is null);
        }
        Console.WriteLine($"ARC-DEV-103 whole maps opened={maps} windows={windows} inventoryRefused={noInventory} rows={rows} largest={largest} held={held} detached={detached} staticRowsLeftOut={statics} failures={failures.Count}");
        foreach (var failure in failures) Console.WriteLine("ARC-DEV-103 failure " + failure);
        Assert.AreEqual(0, failures.Count, string.Join("; ", failures));
    }
}
