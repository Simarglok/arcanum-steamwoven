using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Residency;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b) opt-in (<c>DAT_ROOT</c>, the game folder): the whole mobile read against the original data. Every whole map that the
/// snapshot path opens gives the same rows through the aggregate read (entity, kind, cell, holder, flags), the main world map gives its 7,399
/// rows and the session opens them, and the rows of a window around the Crash Site agree with the whole read. Only counts are printed.
/// </summary>
[TestClass]
public sealed class WholeMapMobileRowsRealDataTests
{
    private static (ContentStack Stack, WorldCatalog Catalog) Open()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var stack = ContentStack.Open(discovery.Plan!).Stack!;
        return (stack, WorldCatalog.Open(stack));
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_whole_map_the_snapshot_path_opens_gives_the_same_rows_through_the_aggregate()
    {
        var (stack, catalog) = Open();
        using var _ = stack;
        var patch = AreaMapPlans.ReadMapPatch(stack);
        int extra = 0, compared = 0, refusedBySnapshot = 0, rowsTotal = 0, wholeOnly = 0;
        var failures = new List<string>();
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) continue;
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            if (created.Status != AreaMapPlanStatus.Ready) { failures.Add($"{map.Name}: plan {created.Status}"); continue; }
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, created.Plan.Map.AggregatePath, 1);
            var inventory = AreaInventoryMap.Build(snapshot, steps);
            var whole = WholeMapMobileRows.Rows(catalog, created.Plan.Map, patch);
            if (inventory.Map is null)
            {
                // The inventory reader refuses this map; the aggregate read has no seed to be refused by.
                refusedBySnapshot++;
                if (whole.Status == MapResidentRowsStatus.Ready) wholeOnly++;
                continue;
            }
            var reference = MapResidentRows.Build(snapshot, inventory.Map);
            if (reference.Status != MapResidentRowsStatus.Ready) { failures.Add($"{map.Name}: reference {reference.Status} {reference.Detail}"); continue; }
            if (whole.Status != MapResidentRowsStatus.Ready) { failures.Add($"{map.Name}: aggregate {whole.Status} {whole.Detail}"); continue; }
            var expected = reference.Rows!.ToDictionary(row => row.Entity);
            var actual = whole.Rows!.ToDictionary(row => row.Entity);
            // The snapshot path leaves out an object whose stale location is outside the map's sectors; the aggregate read keeps it (RL1), and only a
            // detached or held item (no cell) can be one.
            var onlyAggregate = actual.Where(pair => !expected.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray();
            if (expected.Keys.Except(actual.Keys).Any() || onlyAggregate.Any(row => row.Cell is not null))
            { failures.Add($"{map.Name}: entities {expected.Count} vs {actual.Count}, only aggregate {string.Join(",", onlyAggregate.Select(r => r.ToString()))}, only snapshot {string.Join(",", expected.Keys.Except(actual.Keys))}"); continue; }
            extra += onlyAggregate.Length;
            foreach (var key in onlyAggregate.Select(row => row.Entity)) actual.Remove(key);
            var differing = expected.Where(pair => !pair.Value.Equals(actual[pair.Key])).ToArray();
            if (differing.Length > 0) { failures.Add($"{map.Name}: {differing.Length} rows differ, first {differing[0].Value} vs {actual[differing[0].Key]}"); continue; }
            if (reference.StaticRows != whole.StaticRows) { failures.Add($"{map.Name}: statics {reference.StaticRows} vs {whole.StaticRows}"); continue; }
            compared++; rowsTotal += expected.Count;
        }
        Console.WriteLine($"ARC-DEV-118 whole maps compared={compared} rows={rowsTotal} aggregateOnlyRows={extra} snapshotRefused={refusedBySnapshot} aggregateReadsThoseToo={wholeOnly} failures={failures.Count}");
        foreach (var failure in failures) Console.WriteLine("ARC-DEV-118 failure " + failure);
        Assert.AreEqual(0, failures.Count, string.Join("; ", failures));
    }

    [TestMethod, TestCategory("RealData")]
    public void The_main_map_gives_its_rows_the_session_opens_and_a_window_agrees_with_them()
    {
        var (stack, catalog) = Open();
        using var _ = stack;
        var map = catalog.Maps.Single(candidate => candidate.SectorFiles.Count > AreaMapPlans.MaxSectorFiles && candidate.Name == "arcanum1-024-fixed");
        var read = WholeMapMobileRows.Read(catalog, map, AreaMapPlans.ReadMapPatch(stack));
        Assert.IsTrue(read.Ready, read.Detail);
        var build = WholeMapMobileRows.Build(read.Facts);
        Assert.AreEqual(MapResidentRowsStatus.Ready, build.Status, build.Detail);
        var rows = build.Rows!;
        Console.WriteLine($"ARC-DEV-118 main map facts={read.Facts.Count} rows={rows.Count} statics={build.StaticRows} held={rows.Count(r => r.Holder is not null)}" +
            $" detached={rows.Count(r => r.Cell is null && r.Holder is null)} npcs={rows.Count(r => r.Kind == SessionResidentKind.Npc)}");
        Assert.AreEqual(7399, read.Facts.Count, "the census count (docs/world/limits-census.md)");
        Assert.IsTrue(rows.Count <= ResidencyLimits.DefaultMaxResidents);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, map.Name, new(1452, 1291));
        Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, planned.Detail);
        var runtimeMap = planned.Plan!.Plan.RuntimeMap;
        using var session = MapResidentRowsTests.Session(runtimeMap);
        var opened = MapResidentRows.Open(session, new SessionMapId(runtimeMap), build);
        Assert.AreEqual(ResidencyStatus.Open, opened.Status, opened.Detail);
        // The sector cache opens over the map's size: every row's sector is inside it.
        Assert.AreEqual(SectorsOpenStatus.Open, session.OpenSectors(new SessionMapId(runtimeMap), new(map.Properties.WidthTiles / 64, map.Properties.HeightTiles / 64)));

        // A window around the Crash Site: every mobile object it holds is a row of the whole read with the same flags and cell.
        using var owner = planned.Plan!.Open();
        var snapshot = owner.Inspect().Snapshot!;
        var byEntity = rows.ToDictionary(row => row.Entity);
        int windowRows = 0, missing = 0, different = 0;
        foreach (var record in snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile))
        {
            var entity = "oid:" + record.Key.ObjectIdSha256;
            windowRows++;
            if (!byEntity.TryGetValue(entity, out var row)) { if (record.Type is 0 or 1 or 3 or 17) continue; missing++; continue; }
            var flags = unchecked((uint)(record.EffectiveFields.FirstOrDefault(field => field.Slot == 18)?.Number ?? 0));
            if (row.Off != ((flags & 0x2) != 0) || row.Destroyed != ((flags & 0x1) != 0) || row.Dynamic != ((flags & 0x2000) != 0) || (int)row.Kind != record.Type) different++;
            else if (row.Cell is { } cell && (record.WorldX != cell.X || record.WorldY != cell.Y)) different++;
        }
        Console.WriteLine($"ARC-DEV-118 window rows={windowRows} missing={missing} different={different}");
        Assert.IsTrue(windowRows > 0);
        Assert.AreEqual(0, missing);
        Assert.AreEqual(0, different);
    }
}
