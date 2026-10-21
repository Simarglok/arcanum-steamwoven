using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Data.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-008 opt-in check of the installed original (DAT_ROOT = the game folder): which technology items and
/// records reach the magic engine, against the technology census (docs/research/technology-evidence.md). Counts only; no game text.</summary>
[TestClass]
public sealed class TechDeviceRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_devices_and_technology_records_match_the_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in device check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var spells = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        Assert.AreEqual(SpellDataStatus.Read, spells.Status, $"{spells.Refusal}");
        var prototypes = ContentPrototypes.Open(stack);
        var items = new List<(int, CombatFields)>();
        foreach (var number in prototypes.Numbers)
            if (CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is { Item: not null } fields) items.Add((number, fields));
        var catalogue = TechDeviceCatalogue.Build(items, spells.Data!.Records);
        var slots = catalogue.Slots;
        Console.WriteLine($"items {items.Count}; device slots {slots.Count}, records {slots.Select(s => s.Spell).Distinct().Count()}, " +
            $"items {slots.Select(s => s.Prototype).Distinct().Count()} (technological {slots.Where(s => s.Technological).Select(s => s.Prototype).Distinct().Count()}); " +
            $"by type {string.Join(" ", slots.GroupBy(s => s.ObjectType).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Select(s => s.Prototype).Distinct().Count()}"))}; " +
            $"charges {string.Join(" ", slots.GroupBy(s => s.Charges).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}; anomalies {catalogue.Anomalies.Count}");
        Console.WriteLine($"no charges {string.Join(",", slots.Where(s => s.Charges == 0).Select(s => $"{s.Prototype}:t{s.ObjectType}:c{s.Complexity}:r{s.Spell}"))}; " +
            $"non-tech carriers {string.Join(",", slots.Where(s => !s.Technological).Select(s => $"{s.Prototype}:t{s.ObjectType}:c{s.Complexity}"))}");
        Assert.AreEqual(0, catalogue.Anomalies.Count, "every item spell slot names a record or none");
        Assert.AreEqual((70, 65, 70, 61), (slots.Count, slots.Select(s => s.Spell).Distinct().Count(), slots.Select(s => s.Prototype).Distinct().Count(),
            slots.Where(s => s.Technological).Select(s => s.Prototype).Distinct().Count()));
        Assert.AreEqual("-1:21 0:2 1:47", string.Join(" ", slots.GroupBy(s => s.Charges).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        Assert.AreEqual("146:SourcePayment 149:SourcePayment 168:None 187:None 209:None 216:None",
            string.Join(" ", catalogue.Maintained.Select(r => $"{r.Spell}:{r.MaintainPayment}")), "the cell records' maintenance cost is 0");
        Assert.AreEqual("147:Begin:1 168:Maintain:1 187:Begin:1", string.Join(" ", catalogue.Records.Where(r => r.ChargesFromCells)
            .SelectMany(r => r.CellBranches.Select(b => $"{r.Spell}:{b.Action}:{b.Cost}"))), "cells are taken by the ChargeNBranch components");
        Assert.IsTrue(catalogue.Records.All(r => !r.PaysAtBegin) && catalogue.Records.Where(r => r.CellBranches.Count > 0).All(r => r.ChargesFromCells));
        Assert.AreEqual("168:3", string.Join(" ", catalogue.Maintained.Select(r => (r.Spell, Carriers: slots.Count(s => s.Spell == r.Spell)))
            .Where(p => p.Carriers > 0).Select(p => $"{p.Spell}:{p.Carriers}")), "only record 168 is both maintained and carried (three armours)");
    }
}
