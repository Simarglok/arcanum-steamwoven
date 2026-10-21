using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

/// <summary>
/// Opt-in census of ARC-SKILLS-005 (<c>DAT_ROOT</c>, the game folder): the healing items among every prototype the content stack
/// resolves (generic items, type 14, whose flags field 0xD3 has bit 0x08), with their usage bonus, remaining uses and scripts.
/// Only counts and ranges are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class HealingItemRealDataTests
{
    private const int GenericType = 14;
    private const uint HealingFlag = 0x08;

    [TestMethod, TestCategory("RealData")]
    public void Healing_item_prototypes_and_their_bonus_uses_and_scripts()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in census.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        int generic = 0, refused = 0;
        var healing = new List<(int Prototype, CombatGenericFields Fields, IReadOnlyList<int> Scripts, int Points)>();
        foreach (var number in prototypes.Numbers)
        {
            var read = CombatFieldReader.ReadPrototype(prototypes.Read, number);
            if (read.Fields is not { } fields) { refused++; continue; }
            if (fields.Object.Type != GenericType || fields.Generic is not { } item) continue;
            generic++;
            if ((item.Flags & HealingFlag) != 0) healing.Add((number, item, fields.Object.ScriptPoints, fields.Object.HitPoints));
        }
        Console.WriteLine($"prototypes {prototypes.Numbers.Count}, unread {refused}, generic {generic}, healing {healing.Count}");
        foreach (var (prototype, fields, scripts, points) in healing)
            Console.WriteLine($"healing {prototype}: flags 0x{fields.Flags:X}, bonus {fields.UsageBonus}, uses {fields.UsageCountRemaining}, hp {points}, scripts [{string.Join(",", scripts)}]");
        // First observation 2026-10-21: two healing items, both without a bonus or a script (10 and 60 uses).
        CollectionAssert.AreEqual(new[] { "15199 0 10", "15226 0 60" }, healing.Select(h => $"{h.Prototype} {h.Fields.UsageBonus} {h.Fields.UsageCountRemaining}").ToArray());
        Assert.IsTrue(healing.All(h => h.Scripts.Count == 0));

        // Placed on the maps (every interior, whole; the main map is not counted): their prototypes and uses as placed.
        var catalog = WorldCatalog.Open(stack);
        var placed = new SortedDictionary<int, int>(); var maps = 0; var withScripts = 0;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            if (plan.Plan is null) continue;
            using var owner = plan.Plan.Open();
            if (owner.Inspect().Snapshot is not { } snapshot || CombatFieldReader.ReadArea(snapshot).Objects is not { } objects) continue;
            var found = objects.Where(o => o.Fields.Object.Type == GenericType && o.Fields.Generic is { } g && (g.Flags & HealingFlag) != 0).ToArray();
            if (found.Length > 0) maps++;
            foreach (var o in found)
            {
                placed[o.Prototype] = placed.GetValueOrDefault(o.Prototype) + 1;
                if (o.Fields.Object.ScriptPoints.Count > 0) withScripts++;
            }
        }
        Console.WriteLine($"placed healing items {placed.Values.Sum()} on {maps} maps: {string.Join(", ", placed.Select(p => $"{p.Key} x{p.Value}"))}; with scripts {withScripts}");
        Assert.AreEqual(("15199 x2", 2, 0), (string.Join(", ", placed.Select(p => $"{p.Key} x{p.Value}")), maps, withScripts));
    }
}
