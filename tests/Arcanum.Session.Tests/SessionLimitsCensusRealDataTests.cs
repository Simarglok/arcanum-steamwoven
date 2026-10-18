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
}
