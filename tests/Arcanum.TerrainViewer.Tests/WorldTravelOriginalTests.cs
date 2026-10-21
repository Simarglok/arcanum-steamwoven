using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Simulation.Travel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-056 check (DAT_ROOT = the game folder): one-waypoint world-map routes on the installed start map,
/// from the Crash Site to each of the module's areas, over its terrain and own sector files: every planned journey lands on
/// its area under both rule settings.</summary>
[TestClass]
public sealed class WorldTravelOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void One_waypoint_routes_from_the_crash_site_reach_the_near_areas_and_land_on_them()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) { Assert.Inconclusive("DAT_ROOT is required for the opt-in travel check."); return; }
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var map = catalog.Find("arcanum1-024-fixed") ?? throw new InvalidOperationException("start map");
        var grid = catalog.ReadTerrain(map).Grid!;
        var types = TerrainTypeTable.Open(stack).Table!;
        var sectors = new WorldTravelSectors(map, grid, types);
        var areas = WorldAreaTable.Open(stack).Table!.Areas.Select(a => new TravelArea(new(a.Location.X, a.Location.Y), a.Radius)).ToArray();
        var width = map.Properties!.WidthTiles / 64;
        Assert.AreEqual(WorldTravelGeometry.DefaultWidth, width);
        var crash = areas[2].Location; // Crash Site
        int planned = 0, refused = 0; long hours = 0;
        var reached = new SortedSet<int>();
        foreach (var rules in TravelRuleProfile.All)
            for (var target = 1; target < areas.Length; target++)
            {
                if (target == 2) continue;
                var journey = new WorldJourney(new WorldRoute(), areas, width, WorldTravelGeometry.Pixel(crash, width));
                var result = journey.AddWaypoint(WorldTravelGeometry.Pixel(areas[target].Location, width), a => a == target, s => sectors.Passes(s.X, s.Y), rules);
                if (result != TravelWaypointRefusal.None) { refused++; continue; }
                planned++; reached.Add(target);
                var known = new HashSet<int>();
                TravelStepResult step = default;
                for (var guard = 0; guard < WorldRoute.BufferSteps && journey.Waypoints.Count > 0; guard++)
                {
                    step = journey.Step(known.Contains, rules);
                    if (step.Discovered > 0) known.Add(step.Discovered);
                    hours += step.GameMs / WorldJourney.MsPerSector;
                }
                Assert.AreEqual(0, journey.Waypoints.Count, $"{rules.Name} area {target} finished");
                Assert.AreEqual(areas[target].Location, step.Landing, $"{rules.Name} area {target} lands on it");
            }
        Console.WriteLine($"ARC-WORLD-056 routes planned={planned} refused={refused} hours={hours} reached={string.Join(',', reached)}");
        // One straight segment with detours of under 8 sectors (WT7) reaches only the near areas; a far one across blocked
        // mountains or water is refused and needs waypoints. Shrouded Hills (area 1, 43 sectors) plans under both settings.
        Assert.IsTrue(reached.Contains(1), $"planned {planned}, refused {refused}");
        Assert.AreEqual(2 * reached.Count, planned, "both settings plan the same one-segment routes");
        Assert.IsTrue(hours > 0, "a journey takes game time");
    }
}
