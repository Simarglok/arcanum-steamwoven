using System.Globalization;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Technology;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-TECHNOLOGY-004 opt-in check of the installed original (DAT_ROOT, the game folder, whose <c>data/proto</c> holds the
/// loose prototypes): the gate and the plan of every one of the 118 schematic records on an empty and on a synthetic full
/// inventory. Counts only; no game text.</summary>
[TestClass]
public sealed class SchematicAssemblyRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_schematic_is_gated_on_an_empty_and_a_full_inventory()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in assembly check; this is not a synthetic pass.");
        var lookup = PremadeHeroReader.DirectoryPrototypes(Path.Combine(root, "data", "proto"));
        ComponentFacts? Facts(int prototype) => CombatFieldReader.ReadPrototype(lookup, prototype).Fields?.Item is { } item
            ? new ComponentFacts(item.MagickTechComplexity, item.Discipline) : null;
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var records = SchematicCatalog.Open(stack).Catalog!.Records.Where(r => r.Kind is SchematicKind.Learned or SchematicKind.Found).ToList();
        Assert.AreEqual(118, records.Count);
        var expert = Enumerable.Repeat(100, 8).ToArray();
        var outcomes = new Dictionary<string, int>();
        foreach (var record in records)
        {
            var empty = SchematicAssemblyRules.Plan(record.Component1.Bases, record.Component2.Bases, record.Product.Bases, record.Quantity, [], expert, Facts);
            Assert.AreEqual(AssemblyButton.ItemMissing, empty.Button, $"record {record.Number.ToString(CultureInfo.InvariantCulture)} on an empty inventory");
            // Two of every alternative of both lists, in the bag.
            var full = record.Component1.Bases.Concat(record.Component2.Bases).Distinct()
                .SelectMany(b => new[] { new CraftItem($"{b}a", b + 20, 10), new CraftItem($"{b}b", b + 20, 10) }).ToArray();
            var plan = SchematicAssemblyRules.Plan(record.Component1.Bases, record.Component2.Bases, record.Product.Bases, record.Quantity, full, expert, Facts);
            var key = plan.Button.ToString();
            outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
            if (plan.CanAssemble) Assert.IsNotNull(Facts(plan.ProductBase!.Value + 20), "every product prototype is an item");
        }
        Assert.AreEqual(118, outcomes.GetValueOrDefault(nameof(AssemblyButton.CanAssemble)), string.Join(", ", outcomes.Select(p => $"{p.Key}={p.Value}")));
    }
}
