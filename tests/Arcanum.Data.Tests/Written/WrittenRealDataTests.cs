using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Written;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Written;

/// <summary>ARC-UI-049 opt-in check of the installed original (DAT_ROOT = the game folder): every written prototype's document
/// resolves or is refused with a typed status. Counts only; no game text.</summary>
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
        foreach (var number in prototypes.Numbers)
            if (CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is { Written: { } written }) rows.Add((number, written));
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
        foreach (var (row, doc) in rows.Zip(statuses).Where(p => p.First.Written.Subtype != 5))
            Console.WriteLine($"ROW {row.Prototype} st{row.Written.Subtype} {row.Written.TextStartLine}..{row.Written.TextEndLine} body {doc.Body.Count} fonts {string.Join("/", doc.Body.Select(m => m.Font))} header {doc.Header.Count(h => h.Text.Length > 0)}/{doc.Header.Count}");
        Assert.IsTrue(statuses.All(s => Enum.IsDefined(s.Status)));
    }
}
