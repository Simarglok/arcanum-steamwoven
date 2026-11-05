using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b) opt-in (<c>DAT_ROOT</c>, the game folder): the fold's waypoint and standpoint rules over every NPC row of every installed
/// whole map, at noon and at midnight, as the first tick after a sector load runs them (leaderless, the teleport map as stored, the open map the
/// row's own). Counts of the outcomes are printed (no row, no location). The checks: every row's step ends without a failure, no row asks a teleport
/// (the installed maps store no teleport map), and every relocation target is a cell the session can hold (<see cref="SessionNpcPlacement.Axis"/>).
/// </summary>
[TestClass]
public sealed class NpcRouteRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_fold_steps_over_every_installed_npc_row_relocate_inside_their_map()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int npcs = 0, unreadable = 0, outOfRange = 0, requests = 0;
        void Count(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) continue;
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            Assert.AreEqual(AreaMapPlanStatus.Ready, created.Status, map.Name);
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            foreach (var row in snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile && o.Type == AreaNpcRouteReader.NpcType))
            {
                if (AreaNpcRouteReader.Read(row) is not { Status: AreaNpcRouteStatus.Ready, Route: { } route }) { unreadable++; continue; }
                if (row.WorldX is not { } x || row.WorldY is not { } y || x < 0 || y < 0) continue;
                npcs++;
                bool Flag(int slot, uint bit) => (unchecked((uint)(row.EffectiveFields.FirstOrDefault(f => f.Slot == slot)?.Number ?? 0)) & bit) != 0;
                foreach (var hour in new[] { 12, 0 })
                {
                    var facts = new NpcRouteFacts(Flag(36, 0x1000), Flag(18, 0x102), false, hour, route.NpcFlags, route.TeleportMap, snapshot.RuntimeMap,
                        new((uint)x, (uint)y), [.. route.Waypoints.Select(w => new RouteLocation(w.X, w.Y))], route.WaypointIndex,
                        new(route.StandpointDay.X, route.StandpointDay.Y), new(route.StandpointNight.X, route.StandpointNight.Y));
                    var waypoint = NpcRouteRules.ForcedWaypoints(facts);
                    var step = waypoint.Answer ? waypoint : NpcRouteRules.ForcedStandpoint(facts);
                    Count($"{(hour == 12 ? "noon" : "midnight")} {(waypoint.Answer ? "waypoint" : "standpoint")} {step.Outcome}");
                    if (step.Outcome == NpcRouteOutcome.RequestTeleport) requests++;
                    if (step.Target is { } t && (t.X >= SessionNpcPlacement.Axis || t.Y >= SessionNpcPlacement.Axis)) outOfRange++;
                }
            }
        }
        Console.WriteLine($"ARC-DEV-117 fold steps npcs={npcs} unreadable={unreadable} teleportRequests={requests} targetsOutsideSessionAxis={outOfRange} " +
            string.Join(" ", counts.Select(c => $"[{c.Key}]={c.Value}")));
        Assert.AreEqual(0, unreadable);
        Assert.AreEqual(0, requests, "the installed maps store no teleport map: every step stays on the open map");
        Assert.AreEqual(0, outOfRange);
    }
}
