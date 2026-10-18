using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Scripts;

/// <summary>ARC-RES-055: the data facts of script 1341, the examine and insert-item script of the Crash Site amulet 2527
/// (docs/research/item-insert-script.md). Opt-in, DAT_ROOT = the game folder (module Arcanum). Structure, numbers and
/// text sizes only; no game text is asserted or printed.</summary>
[TestClass]
public sealed class ItemInsertScriptRealDataTests
{
    private const int Script = 1341, Flag = 1085;
    private const byte Number = (byte)ScrValueKind.Number, GlobalFlag = (byte)ScrValueKind.GlobalFlag;

    [TestMethod, TestCategory("RealData")]
    public void Script_1341_prints_its_one_dialog_line_unless_global_flag_1085_is_set()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in item script check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var content = ScriptContent.Open(discovery.Plan!).Content!;

        var read = content.Script(Script);
        Assert.AreEqual(ScriptContentStatus.Read, read.Status, read.Detail);
        var program = read.Program!;
        Assert.AreEqual((0u, 0u, 0u, 4), (program.InitialLocalFlags, program.InitialCounters, program.FileFlags, program.Count));
        // Line 0: GF 1085 == 1 → goto 3, else nothing. Line 1: print line 1, operand 6. Line 2: return, skip default.
        // Line 3: return, run default. Lines 1..3 have an always-true condition and a do-nothing false branch.
        AssertClause(program.Lines[0].Condition, ScrClauseSide.Condition, ScrCatalogue.Condition.Equal, (GlobalFlag, Flag), (Number, 1));
        AssertClause(program.Lines[0].OnTrue, ScrClauseSide.OnTrue, ScrCatalogue.Action.Goto, (Number, 3));
        AssertClause(program.Lines[1].OnTrue, ScrClauseSide.OnTrue, PrintLine, (Number, 1), (Number, 6));
        AssertClause(program.Lines[2].OnTrue, ScrClauseSide.OnTrue, ScrCatalogue.Action.ReturnSkipDefault);
        AssertClause(program.Lines[3].OnTrue, ScrClauseSide.OnTrue, ScrCatalogue.Action.ReturnRunDefault);
        Assert.AreEqual(ScrCatalogue.Action.DoNothing, program.Lines[0].OnFalse.Type);
        foreach (var line in program.Lines.Skip(1))
            Assert.AreEqual((ScrCatalogue.Condition.True, ScrCatalogue.Action.DoNothing), (line.Condition.Type, line.OnFalse.Type), line.Number.ToString());

        // Its DLG holds one NPC line, number 1, with empty condition, response and action fields, so printing it runs no
        // dialog action (ledger IS2); both texts are present and neither starts with a generated-text prefix.
        var dialog = content.Dialog(Script);
        Assert.AreEqual(ScriptContentStatus.Read, dialog.Status, dialog.Detail);
        var record = dialog.Document!.Records.Single();
        Assert.AreEqual((1, DlgRecordRole.Npc, 0, 0, 0), (record.Id, record.Role, record.Condition.Length, record.Response.Length, record.Action.Length));
        foreach (var text in new[] { record.Text, record.AlternateText })
        {
            Assert.IsTrue(text.Length > 2);
            var prefix = Encoding.Latin1.GetString(text.Bytes.Take(2).ToArray()).ToLowerInvariant();
            CollectionAssert.DoesNotContain(new[] { "g:", "i:", "m:", "r:" }, prefix);
        }

        // Global flag 1085: no script writes it, only 1341 reads it; four dialogs set it in NPC or player actions.
        var scriptUses = new List<(int Script, int Line, string Side)>();
        var dialogUses = new List<(int Dialog, int Record, string Field)>();
        var gf = new Regex(@"(?i)\bgf\s*" + Flag + @"\s+(-?\d+)");
        foreach (var name in content.Names.Names)
        {
            foreach (var line in content.Script(name.Number).Program!.Lines)
                foreach (var (clause, side) in new[] { (line.Condition, ScrClauseSide.Condition), (line.OnTrue, ScrClauseSide.OnTrue), (line.OnFalse, ScrClauseSide.OnFalse) })
                    if (NamesFlag(clause, side)) scriptUses.Add((name.Number, line.Number, side.ToString()));
            if (content.Dialog(name.Number).Document is not { } document) continue;
            foreach (var entry in document.Records)
                foreach (var (field, kind) in new[] { (entry.Condition, "condition"), (entry.Action, "action") })
                    foreach (Match match in gf.Matches(Encoding.Latin1.GetString(field.Bytes.ToArray())))
                        dialogUses.Add((name.Number, entry.Id, $"{entry.Role} {kind} {match.Groups[1].Value}"));
        }
        CollectionAssert.AreEqual(new[] { (Script, 0, "Condition") }, scriptUses);
        CollectionAssert.AreEquivalent(new[]
        {
            (1047, 1100, "Npc action 1"), (1047, 1130, "Npc action 1"), (1047, 1160, "Npc action 1"), (1047, 1195, "Npc action 1"),
            (1047, 1225, "Npc action 1"), (1047, 1235, "Npc action 1"),
            (1074, 376, "Player condition 1"), (1074, 377, "Player condition 0"), (1074, 379, "Npc action 1"),
            (1096, 19, "Npc action 1"),
            (3011, 4, "Player condition 0"), (3011, 4, "Player action 1"),
        }, dialogUses);
        Assert.IsFalse(dialogUses.Any(use => use.Field.EndsWith("action 0", StringComparison.Ordinal)), "no dialog clears it");
    }

    private const int PrintLine = 24;

    // A number operand of the clause's signature that reads GF 1085: a global-flag value, or the flag number of the
    // global-flag condition and the set/clear global-flag actions.
    private static bool NamesFlag(ScrClause clause, ScrClauseSide side)
    {
        if (ScrCatalogue.Signature(side, clause.Type) is not { } signature) return false;
        var flagNumber = side == ScrClauseSide.Condition ? clause.Type == ScrCatalogue.Condition.GlobalFlag
            : clause.Type is ScrCatalogue.Action.SetGlobalFlag or ScrCatalogue.Action.ClearGlobalFlag;
        for (var slot = 0; slot < signature.Count; slot++)
            if (signature[slot] == ScrOperandRole.Number && clause.Value(slot) == Flag &&
                (clause.Kind(slot) == GlobalFlag || (flagNumber && clause.Kind(slot) == Number))) return true;
        return false;
    }

    private static void AssertClause(ScrClause clause, ScrClauseSide side, int type, params (byte Kind, int Value)[] operands)
    {
        Assert.AreEqual(type, clause.Type);
        var signature = ScrCatalogue.Signature(side, type)!;
        var read = Enumerable.Range(0, signature.Count).Where(slot => signature[slot] != ScrOperandRole.Unused)
            .Select(slot => (clause.Kind(slot), clause.Value(slot))).ToArray();
        CollectionAssert.AreEqual(operands, read, $"type {type}");
    }
}
