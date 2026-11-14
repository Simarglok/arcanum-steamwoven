using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// Opt-in census of ARC-SCRIPT-1863 (<c>DAT_ROOT</c>, the game folder): which installed scripts ask obj is in dialog (26), obj can see (28) or obj can hear (29), with the focus kinds of the operands. Only script numbers, line numbers and focus kinds are printed, never original text.
/// </summary>
[TestClass]
public sealed class ScrHeroSightCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_scripts_that_ask_the_dialog_sight_and_hearing_conditions()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in hero sight census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var rows = new List<(string Script, int Line, int Type, byte FirstKind, string Operands)>();
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".scr", StringComparison.Ordinal)))
        {
            var read = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!);
            if (read.Program is not { } program) continue;
            var name = Path.GetFileNameWithoutExtension(key);
            foreach (var line in program.Lines)
                if (line.Condition.Type is ScrCatalogue.Condition.IsInDialog or ScrCatalogue.Condition.CanSeeObject or ScrCatalogue.Condition.CanHearObject)
                    rows.Add((name, line.Number, line.Condition.Type, line.Condition.Kind(0), $"{(ScrFocusKind)line.Condition.Kind(0)}:{line.Condition.Value(0)}|{(ScrFocusKind)line.Condition.Kind(1)}:{line.Condition.Value(1)}"));
        }
        foreach (var row in rows) Console.WriteLine($"HERO-SIGHT-CENSUS|{row.Script}|line {row.Line}|condition {row.Type}|{row.Operands}");
        Console.WriteLine($"HERO-SIGHT-CENSUS|clauses {rows.Count}|scripts {rows.Select(r => r.Script).Distinct().Count()}");
        foreach (var type in new[] { ScrCatalogue.Condition.IsInDialog, ScrCatalogue.Condition.CanSeeObject, ScrCatalogue.Condition.CanHearObject })
            Console.WriteLine($"HERO-SIGHT-CENSUS|condition {type}|clauses {rows.Count(r => r.Type == type)}|scripts {rows.Where(r => r.Type == type).Select(r => r.Script).Distinct().Count()}");
        // Only the first operand's kind is asked: the PC itself (Player) is named by the dialog condition alone (1130 line 19 and 3073 line 5); no sight or hearing clause names the PC as the observer.
        Console.WriteLine($"HERO-SIGHT-CENSUS|PC as first operand|{string.Join(", ", rows.Where(r => r.FirstKind == (byte)ScrFocusKind.Player).Select(r => $"{r.Script} line {r.Line} condition {r.Type}"))}");
        // 21 clauses in 16 scripts ask in dialog, 18 in 17 ask can see, 5 in 5 ask can hear.
        CollectionAssert.AreEqual(new[] { (21, 16), (18, 17), (5, 5) }, new[] { ScrCatalogue.Condition.IsInDialog, ScrCatalogue.Condition.CanSeeObject, ScrCatalogue.Condition.CanHearObject }
            .Select(type => (rows.Count(r => r.Type == type), rows.Where(r => r.Type == type).Select(r => r.Script).Distinct().Count())).ToArray());
        Assert.AreEqual(0, rows.Count(r => r.Type != ScrCatalogue.Condition.IsInDialog && r.FirstKind == (byte)ScrFocusKind.Player));
    }
}
