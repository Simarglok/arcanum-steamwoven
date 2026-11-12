using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1118 (ledger PW2, PY1–PY4): opt-in census of the installed scripts' print line actions (DAT_ROOT = the game folder): the entry type
/// each one names as its second operand, and which scripts use a type other than 6. Counts and script numbers only.</summary>
[TestClass]
public sealed class ScrPrintLineTypesRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_print_lines_name_type_6_but_three_scripts()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in print line census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var byType = new SortedDictionary<int, int>();
        var read = new List<string>();
        var others = new List<string>();
        var total = 0;
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var result = stack.Read(stack.Resolve(key).Winner!);
            Assert.AreEqual(ContentReadStatus.Read, result.Status, key);
            var program = ScrProgram.Read(result.Bytes!).Program;
            Assert.IsNotNull(program, key);
            foreach (var line in program.Lines)
                foreach (var clause in new[] { line.OnTrue, line.OnFalse }.Where(c => c.Type == ScrCatalogue.Action.PrintLine))
                {
                    total++;
                    var name = Path.GetFileNameWithoutExtension(key);
                    if (clause.Kind(1) != (byte)ScrValueKind.Number) { read.Add($"{name} line {line.Number}"); continue; }
                    byType[clause.Value(1)] = byType.GetValueOrDefault(clause.Value(1)) + 1;
                    if (clause.Value(1) != 6) others.Add($"{name} line {line.Number} type {clause.Value(1)}");
                }
        }
        Assert.AreEqual(189, total, "the print line actions");
        CollectionAssert.AreEqual(new[] { (0, 1), (1, 1), (6, 186) }, byType.Select(p => (p.Key, p.Value)).ToArray(), "the types the scripts name outright");
        CollectionAssert.AreEqual(new[] { "01711room_number_examine line 0" }, read, "the one print line whose type is a counter");
        CollectionAssert.AreEqual(new[] { "01021sellingstolengoods line 8 type 0", "01143pillaroftruthexamine line 2 type 1" }, others);
    }
}
