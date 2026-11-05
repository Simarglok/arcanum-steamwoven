using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b) opt-in (<c>DAT_ROOT</c>, the game folder): the route fields of every NPC row of every installed whole map. Each map is
/// loaded as the viewer loads it; each NPC row's route is read by <see cref="AreaNpcRouteReader"/>. The invariants are the ones the fold's steps
/// take for granted: every route reads (no malformed row), the waypoint index lies inside its list, the waypoint flags and the list agree in the
/// way the counts show. Only counts are printed (no row, no location).
/// </summary>
[TestClass]
public sealed class AreaNpcRouteRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_npc_row_of_every_whole_map_has_a_readable_route()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        int maps = 0, windows = 0, npcs = 0, withList = 0, withStand = 0, withoutStand = 0, zeroBoth = 0, dayFlag = 0, nightFlag = 0, flagNoList = 0, listNoFlag = 0,
            indexOut = 0, teleportSet = 0, teleportOther = 0, standOnCell = 0, standNear = 0, standFar = 0, largestList = 0;
        var failures = new List<string>();
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) { windows++; continue; }
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            if (created.Status != AreaMapPlanStatus.Ready) { failures.Add($"{map.Name}: plan {created.Status}"); continue; }
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            maps++;
            foreach (var row in snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile && o.Type == AreaNpcRouteReader.NpcType))
            {
                var read = AreaNpcRouteReader.Read(row);
                if (read.Status != AreaNpcRouteStatus.Ready) { failures.Add($"{map.Name}: {read.Status} {read.Detail}"); continue; }
                var route = read.Route!;
                npcs++;
                var hasList = route.Waypoints.Count > 0;
                if (hasList) { withList++; largestList = Math.Max(largestList, route.Waypoints.Count); }
                if (route.HasStandpoints) withStand++; else withoutStand++;
                if (route.StandpointDay == default && route.StandpointNight == default) zeroBoth++;
                var day = (route.NpcFlags & 0x2) != 0; var night = (route.NpcFlags & 0x4) != 0;
                if (day) dayFlag++;
                if (night) nightFlag++;
                if ((day || night) && !hasList) flagNoList++;
                if (hasList && !day && !night) listNoFlag++;
                if (hasList && route.WaypointIndex >= route.Waypoints.Count) indexOut++;
                if (route.TeleportMap != 0) { teleportSet++; if (route.TeleportMap != snapshot.RuntimeMap) teleportOther++; }
                if (row.WorldX is { } x && row.WorldY is { } y && route.HasStandpoints)
                {
                    var d = Math.Max(Math.Abs(x - route.StandpointDay.X), Math.Abs(y - route.StandpointDay.Y));
                    if (d == 0) standOnCell++; else if (d <= 4) standNear++; else standFar++;
                }
            }
        }
        Console.WriteLine($"ARC-DEV-117 npc routes maps={maps} windows={windows} npcs={npcs} withList={withList} largestList={largestList} listNoFlag={listNoFlag} flagNoList={flagNoList} " +
            $"dayFlag={dayFlag} nightFlag={nightFlag} indexOutOfList={indexOut} withStandpoints={withStand} noStandpointField={withoutStand} bothStandpointsZero={zeroBoth} " +
            $"dayStandOnItsCell={standOnCell} dayStandWithin4={standNear} dayStandFarther={standFar} teleportMapSet={teleportSet} teleportMapOtherThanOwn={teleportOther} failures={failures.Count}");
        foreach (var failure in failures.Take(20)) Console.WriteLine("ARC-DEV-117 failure " + failure);
        Assert.AreEqual(0, failures.Count, string.Join("; ", failures.Take(5)));
        Assert.AreEqual(0, indexOut, "a stored waypoint index lies outside its list");
    }
}
