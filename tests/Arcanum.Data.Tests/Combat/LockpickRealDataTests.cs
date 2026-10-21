using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

/// <summary>
/// Opt-in census of ARC-SKILLS-006 (<c>DAT_ROOT</c>, the game folder): the lockpicks among every prototype the content stack
/// resolves (generic items, type 14, whose flags field 0xD3 has bit 0x02) with their usage bonus and scripts, the lockpicks placed
/// on the interiors, and the locked portals and containers there with their lock difficulty (portal field 47, container field
/// 57: the record's slot 37 for both, after the flags at 36). Only counts and ranges are compared and printed.
/// </summary>
[TestClass]
public sealed class LockpickRealDataTests
{
    private const int GenericType = 14;
    private const uint LockpickFlag = 0x02;

    [TestMethod, TestCategory("RealData")]
    public void Lockpick_prototypes_placed_lockpicks_and_locked_objects()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in census.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        var picks = new List<(int Prototype, CombatGenericFields Fields, int Scripts)>();
        foreach (var number in prototypes.Numbers)
        {
            if (CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is not { } fields) continue;
            if (fields.Object.Type == GenericType && fields.Generic is { } item && (item.Flags & LockpickFlag) != 0) picks.Add((number, item, fields.Object.ScriptPoints.Count));
        }
        Console.WriteLine($"lockpick prototypes {picks.Count}: " + string.Join("; ", picks.Select(p => $"{p.Prototype} flags 0x{p.Fields.Flags:X} bonus {p.Fields.UsageBonus} scripts {p.Scripts}")));

        var catalog = WorldCatalog.Open(stack);
        var placed = new SortedDictionary<int, int>(); int lockedPortals = 0, portals = 0, lockedContainers = 0, maps = 0;
        var difficulties = new SortedDictionary<long, int>();
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            if (plan.Plan is null) continue;
            using var owner = plan.Plan.Open();
            if (owner.Inspect().Snapshot is not { } snapshot) continue;
            maps++;
            if (CombatFieldReader.ReadArea(snapshot).Objects is { } objects)
                foreach (var o in objects.Where(o => o.Fields.Object.Type == GenericType && o.Fields.Generic is { } g && (g.Flags & LockpickFlag) != 0))
                    placed[o.Prototype] = placed.GetValueOrDefault(o.Prototype) + 1;
            foreach (var row in snapshot.Objects.Where(r => r.Type is 1 or 2))
            {
                long Field(int slot) => row.EffectiveFields.SingleOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0;
                var portal = row.Type == 1;
                if (portal) portals++;
                if ((Field(36) & 1) == 0) continue;
                if (portal) lockedPortals++; else lockedContainers++;
                var difficulty = Field(37);
                difficulties[difficulty] = difficulties.GetValueOrDefault(difficulty) + 1;
            }
        }
        Console.WriteLine($"maps {maps}; placed lockpicks {placed.Values.Sum()}: {string.Join(", ", placed.Select(p => $"{p.Key} x{p.Value}"))}");
        Console.WriteLine($"portals {portals}, locked {lockedPortals}; locked containers {lockedContainers}; difficulties {string.Join(", ", difficulties.Select(d => $"{d.Key} x{d.Value}"))}");
        // First observation 2026-10-21: three lockpick prototypes (bonus 10, 5 and 0, no scripts), three placed (15104) on the
        // 80 interiors, 398 of 519 portals and 442 containers locked, lock difficulties 0..1000 (635 of them 0).
        Assert.AreEqual("15101 10 0, 15104 5 0, 15198 0 0", string.Join(", ", picks.Select(p => $"{p.Prototype} {p.Fields.UsageBonus} {p.Scripts}")));
        Assert.AreEqual(("15104 x3", 80, 519, 398, 442), (string.Join(", ", placed.Select(p => $"{p.Key} x{p.Value}")), maps, portals, lockedPortals, lockedContainers));
        Assert.AreEqual((0L, 1000L, 635), (difficulties.Keys.First(), difficulties.Keys.Last(), difficulties[0]));
    }
}
