using System.Globalization;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-003 opt-in check of the installed original (DAT_ROOT, the game folder, whose <c>data/proto</c> holds the
/// loose prototypes): the schematic documents against the data census of the technology research. Counts only; no game text.</summary>
[TestClass]
public sealed class WrittenSchematicRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_schematic_documents_point_at_the_found_records()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in schematic document check; this is not a synthetic pass.");
        var proto = Path.Combine(root, "data", "proto");
        var lookup = PremadeHeroReader.DirectoryPrototypes(proto);
        var numbers = Directory.EnumerateFiles(proto, "*.pro").Select(Path.GetFileName).Where(n => n!.Length > 6 && n[..6].All(char.IsAsciiDigit))
            .Select(n => int.Parse(n![..6], CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        var written = numbers.Select(n => CombatFieldReader.ReadPrototype(lookup, n)).Where(r => r.Fields?.Object.Type == CombatFieldReader.WrittenType).ToList();
        Assert.AreEqual(68, written.Count, "written prototypes");
        Assert.IsTrue(written.All(r => r.Fields!.Written is not null));
        var documents = written.Where(r => r.Fields!.Written!.IsSchematic).Select(r => r.Fields!.Written!.TextStartLine).ToList();
        Assert.AreEqual(58, documents.Count, "of subtype 5");
        Assert.AreEqual(documents.Count, documents.Distinct().Count(), "no two documents name one schematic");

        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = SchematicCatalog.Open(stack).Catalog!;
        var found = catalog.Records.Where(r => r.Kind == SchematicKind.Found).Select(r => r.Number).ToHashSet();
        Assert.AreEqual((57, 1), (documents.Count(found.Contains), documents.Count(n => n == 0)), "57 at found records, one numbered 0 (TC8)");
        Assert.AreEqual(5, found.Count(n => !documents.Contains(n)), "found records without a document (O3)");

        // Every found record has a discipline 0..7 through its first product's item discipline field.
        var disciplines = SchematicCatalog.PrototypeDisciplines(lookup);
        Assert.IsTrue(found.All(n => catalog.Discipline(n, disciplines) is >= 0 and < 8));
        var tabs = catalog.Tabs(Enumerable.Repeat(7, 8).ToArray(), [.. found], crafterIsPc: true, disciplines);
        Assert.AreEqual((56, 62), (tabs.Learned.Sum(t => t.Count), tabs.Found.Sum(t => t.Count)));
    }
}
