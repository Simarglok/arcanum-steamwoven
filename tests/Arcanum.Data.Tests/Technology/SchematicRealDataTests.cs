using Arcanum.Data.Content;
using Arcanum.Data.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-001 opt-in check of the installed original (DAT_ROOT = the game folder): the schematic and tech texts
/// against the data census of the technology research (docs/research/technology-evidence.md#data-census). Counts only; no game
/// text.</summary>
[TestClass]
public sealed class SchematicRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_schematics_and_tech_texts_match_the_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in schematic check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;

        var tech = TechTexts.Open(stack);
        Assert.AreEqual(TechnologyDataStatus.Read, tech.Status, $"{tech.Refusal}");
        Assert.AreEqual(0, tech.Texts!.Missing.Count, "tech.mes holds all 90 entries 0..89");

        var load = SchematicCatalog.Open(stack);
        Assert.AreEqual(TechnologyDataStatus.Read, load.Status, $"{load.Refusal}");
        var catalog = load.Catalog!;
        var records = catalog.Records;
        Assert.AreEqual((126, 882, 0), (records.Count, records.Sum(r => r.MissingLines.Count(m => !m)), catalog.Unread.Count),
            "882 entries in 126 complete records, none outside a record");
        Assert.IsTrue(records.All(r => r.IsComplete));
        Assert.AreEqual((2000, 6070), (records[0].Number, records[^1].Number));

        var learned = records.Where(r => r.Kind == SchematicKind.Learned).ToList();
        var found = records.Where(r => r.Kind == SchematicKind.Found).ToList();
        var placeholders = records.Where(r => r.Kind == SchematicKind.Placeholder).ToList();
        Assert.AreEqual((56, 62, 8), (learned.Count, found.Count, placeholders.Count));
        Assert.AreEqual(56, catalog.LearnedCount, "392 entries in 2000..3999 / 7");
        Assert.AreEqual((4000, 5440), (found[0].Number, found[^1].Number));
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).Select(SchematicCatalog.PlaceholderNumber).ToArray(), placeholders.Select(r => r.Number).ToArray());

        // Seven per discipline; the k-th of each discipline in the window's order is the editor's learned number.
        var full = Enumerable.Repeat(7, 8).ToArray();
        CollectionAssert.AreEqual(learned.Select(r => r.Number).ToArray(), catalog.LearnedStarts.ToArray());
        for (var d = 0; d < 8; d++)
        {
            CollectionAssert.AreEqual(Enumerable.Range(1, 7).Select(k => SchematicCatalog.LearnedNumber(d, k)).ToArray(),
                learned.Where(r => catalog.Discipline(r.Number, NoPrototype) == d).Select(r => r.Number).ToArray(), $"discipline {d}");
            for (var k = 1; k <= 7; k++)
                Assert.AreEqual(SchematicCatalog.LearnedNumber(d, k), catalog.SelectLearned(full, d, k - 1), $"discipline {d} page {k - 1}");
            Assert.AreEqual(-1, catalog.SelectLearned(full, d, 7), "seven per discipline");
            Assert.AreEqual(SchematicCatalog.PlaceholderNumber(d), catalog.RecordToShow(catalog.SelectLearned(new int[8], d, 0), d).Record!.Number);
        }

        // Every list has one or three alternatives; every name and description exists in schematic_text.mes.
        Assert.IsTrue(records.All(r => new[] { r.Component1, r.Component2, r.Product }.All(a => a.Tokens is 1 or 3)));
        Assert.AreEqual(258, catalog.TextCount);
        Assert.AreEqual(0, records.Count(r => catalog.Text(r.NameText) is null || catalog.Text(r.DescriptionText) is null));
        Assert.IsTrue(records.All(r => r.Quantity >= 1));
    }

    private static int? NoPrototype(int prototype) => null;
}
