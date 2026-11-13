using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Written;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Written;

/// <summary>ARC-UI-049 opt-in check of the installed original (DAT_ROOT = the game folder): every written prototype's document
/// is read or is a schematic document, with the census the ledger WR anomaly rows cite. Counts only; no game text.</summary>
[TestClass]
public sealed class WrittenRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_written_prototype_reads_or_is_refused_by_type()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in written-text check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var texts = WrittenTexts.Open(stack);
        var prototypes = ContentPrototypes.Open(stack);
        var rows = new List<(int Prototype, CombatWrittenFields Written)>();
        int useScripts = 0, otherScripts = 0, magical = 0, targeted = 0;
        foreach (var number in prototypes.Numbers)
            if (CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is { Written: { } written } fields)
            {
                rows.Add((number, written));
                if (fields.Object.ScriptPoints.Contains(1)) useScripts++;
                else if (fields.Object.ScriptPoints.Count > 0) otherScripts++;
                if (fields.Item is { SpellManaStore: not 0 } || (fields.Item?.Flags & 0x4) != 0) magical++;
                if ((fields.Item?.Flags & 0x100) != 0) targeted++; // ARC-SCRIPT-020, IU2: a use that first asks for a target
            }
        Console.WriteLine($"with a use script {useScripts}, with other scripts only {otherScripts}, magical {magical}, targeted {targeted}");
        Assert.AreEqual(0, targeted, "no written prototype asks for a target before its use");
        var bySubtype = rows.GroupBy(r => r.Written.Subtype).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        var statuses = rows.Select(r => texts.Read(r.Written.Subtype, r.Written.TextStartLine, r.Prototype == WrittenTexts.NewspaperAlternatePrototype))
            .ToArray();
        var empty = rows.Zip(statuses).Count(p => p.Second.Readable && p.Second.Subtype is not WrittenSubtype.Picture && p.Second.Body.Count == 0);
        var spans = rows.Where(r => r.Written.Subtype is not (4 or 5)).Select(r => r.Written.TextEndLine - r.Written.TextStartLine).ToArray();
        var cut = rows.Zip(statuses).Count(p => p.Second.Readable && p.First.Written.TextEndLine - p.First.Written.TextStartLine + 1 > p.Second.Body.Count
            && p.Second.Subtype is not WrittenSubtype.Picture);
        var noPrefix = rows.Zip(statuses).Where(p => p.Second.Readable && p.Second.Subtype is WrittenSubtype.Book or WrittenSubtype.Plaque or WrittenSubtype.Newspaper)
            .Count(p => p.Second.Body.Any(m => m.Font == 0));
        Console.WriteLine($"files refused: {string.Join(",", texts.Refusals.Select(r => r.Path + ":" + r.Status))}; fillers {texts.NewspaperFillers}");
        Console.WriteLine($"written prototypes {rows.Count} by subtype {string.Join(" ", bySubtype)}");
        Console.WriteLine($"statuses {string.Join(" ", statuses.GroupBy(s => s.Status).Select(g => $"{g.Key}:{g.Count()}"))}; empty bodies {empty}");
        Console.WriteLine($"end-start spans min {spans.DefaultIfEmpty().Min()} max {spans.DefaultIfEmpty().Max()}; documents whose end line lies past what is shown {cut}; parsed documents with a font-0 message {noPrefix}");
        Console.WriteLine($"flags {string.Join(",", rows.Select(r => r.Written.Flags).Distinct())}; picture arts {string.Join(",", rows.Where(r => r.Written.Subtype == 4).Select(r => r.Written.TextStartLine))}");
        Assert.AreEqual(0, texts.Refusals.Count, "every written text file loads");
        Assert.IsTrue(rows.Count > 0, "the installation has written prototypes");
        // No installed written prototype is crash class: every one is a document the window shows or a schematic document.
        Assert.IsTrue(statuses.All(s => s.Status is WrittenReadStatus.Read or WrittenReadStatus.Schematic), "only read and schematic statuses");
        Assert.IsTrue(statuses.All(s => s.Body.Count <= WrittenTexts.MessagesPerDocument), "at most ten messages a document");
        Assert.IsTrue(statuses.Where(s => s.Subtype == WrittenSubtype.Newspaper).All(s => s.Header.Count == 6), "a newspaper's masthead has six entries");
    }

    /// <summary>ARC-UI-1739 (ledger NP2, NP6, NP7): the vendor's newspaper is a written newspaper item that starts at the issue it is given with, the default issue's headlines exist and the first headline reads. Counts and flags only; no game text.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_vendors_newspaper_prototype_and_the_default_issue_headlines_exist()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in newspaper check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var texts = WrittenTexts.Open(stack);
        var prototypes = ContentPrototypes.Open(stack);
        const int vendorPrototype = 14064 + 20; // the executable's basic number plus the project's shift
        var written = CombatFieldReader.ReadPrototype(prototypes.Read, vendorPrototype).Fields?.Written;
        Assert.IsNotNull(written, "the vendor's newspaper prototype is a written item");
        Assert.AreEqual((int)WrittenSubtype.Newspaper, written.Subtype);
        Assert.IsTrue(texts.Headline(5000).Length > 0, "the new game's head issue has a headline");
        Assert.IsTrue(texts.DefaultIssueChoices() > 0, "the newspaper file offers default issues above 5000");
        Console.WriteLine($"default issue choices {texts.DefaultIssueChoices()}; vendor prototype start line {written.TextStartLine}");
    }
}
