using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DATA-029 opt-in census (<c>DAT_ROOT</c>, the game folder): the session's policy maxima against every installed map.
/// A whole playthrough may change every map, every object of the largest map and of all maps once, and hold a life,
/// reaction and experience record for every NPC; each maximum must keep at least 1.5 times that. Only counts are printed.
/// </summary>
[TestClass]
public sealed class SessionLimitsCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Session_maxima_hold_a_whole_playthrough_of_the_installed_maps()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        long largest = 0, total = 0, npcs = 0;
        foreach (var map in catalog.Maps)
        {
            long objects = 0;
            foreach (var coordinate in map.SectorFiles)
            {
                var sector = catalog.ReadSector(map, coordinate).Sector!;
                objects += sector.Objects.Count;
                npcs += sector.Objects.Count(o => o.Type == 16);
            }
            var mobiles = catalog.ReadMobiles(map).Objects;
            objects += mobiles.Count;
            npcs += mobiles.Count(o => o.Type == 16);
            largest = Math.Max(largest, objects);
            total += objects;
        }
        Console.WriteLine($"ARC-DATA-029 session maps={catalog.Maps.Count} largestMapObjects={largest} objects={total} npcs={npcs}");
        Assert.IsTrue(catalog.Maps.Count * 3L <= GameSessionLimits.PolicyMaxMaps * 2L, $"maps {catalog.Maps.Count}");
        Assert.IsTrue(largest * 3 <= GameSessionLimits.PolicyMaxChangesPerMap * 2L, $"largest map {largest}");
        Assert.IsTrue(total * 3 <= GameSessionLimits.PolicyMaxTotalChanges * 2L, $"all maps {total}");
        foreach (var cap in new[] { GameSessionLimits.PolicyMaxLifeRecords, GameSessionLimits.PolicyMaxReactionRecords,
                     GameSessionLimits.PolicyMaxExperienceRecords })
            Assert.IsTrue(npcs * 3 <= cap * 2L, $"npcs {npcs} under {cap}");
    }

    /// <summary>
    /// ARC-DEV-103 (MIG9 S3, K1 §3.2, §6.1): the residents a residency holds are a map's mobile rows (RL1: every object of its mobile files), so the
    /// residency's default cap must keep 1.5 times the largest installed map's. Prints the main world map's count by type class and the most rows one
    /// sector holds (instance locations only; held items carry a stale one, RL10). Only counts are printed.
    /// </summary>
    [TestMethod, TestCategory("RealData")]
    public void The_residency_cap_holds_every_installed_maps_mobile_rows()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var largest = (Map: "", Rows: 0);
        foreach (var map in catalog.Maps)
        {
            var read = catalog.ReadMobiles(map);
            Assert.AreEqual(0, read.Issues.Count, $"{map.Name}: {string.Join("; ", read.Issues.Select(issue => issue.Code))}");
            var objects = read.Objects;
            if (objects.Count > largest.Rows) largest = (map.Name, objects.Count);
            if (map.SectorFiles.Count <= 256) continue;
            static string Class(int type) => type switch { 16 => "npc", 15 => "pc", 2 => "container", 4 => "projectile", >= 5 and <= 14 => "item", _ => "sector-type" };
            var classes = objects.GroupBy(o => Class(o.Type)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}");
            var sectors = objects.Where(o => o.Fields.GetValueOrDefault(1)?.Number is not null)
                .GroupBy(o => { var at = WorldLocation.FromPacked(o.Fields[1].Number!.Value); return (at.X >> 6, at.Y >> 6); }).ToArray();
            Console.WriteLine($"ARC-DEV-103 main map {map.Name} source={read.Source} mobiles={objects.Count} {string.Join(" ", classes)} " +
                $"sectors={sectors.Length} busiestSector={sectors.Max(g => g.Count())}");
        }
        Console.WriteLine($"ARC-DEV-103 residency largestMapMobiles={largest.Rows} cap={ResidencyLimits.DefaultMaxResidents}");
        Assert.IsTrue(largest.Rows * 3L <= ResidencyLimits.DefaultMaxResidents * 2L, $"largest map {largest.Rows}");
    }
}
