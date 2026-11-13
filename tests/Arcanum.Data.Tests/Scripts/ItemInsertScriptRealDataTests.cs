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
    private const int Script = 1341, Flag = 1085, PrintLine = 24;
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
        // ARC-AI-1655: the text and the alternate text agree byte for byte, so the line shows the same whoever triggers the script (an NPC's pickup, whose gender the host does not read).
        CollectionAssert.AreEqual(record.Text.Bytes.ToArray(), record.AlternateText.Bytes.ToArray());
        foreach (var text in new[] { record.Text, record.AlternateText })
        {
            Assert.AreEqual(75, text.Length);
            var prefix = Encoding.Latin1.GetString(text.Bytes.Take(2).ToArray()).ToLowerInvariant();
            CollectionAssert.DoesNotContain(new[] { "g:", "i:", "m:", "r:" }, prefix);
        }

        // Global flag 1085 and its 32-flag word (1056..1087): an unmasked flag write (ledger X10) of 2 or of a negative value
        // also sets higher flags of the same word, so every script operand that names a flag of the word, and every flag
        // index the script computes, is listed. No script writes 1085; the only writes into the word assign a literal 1.
        var scripts = new List<(int Script, int Line, string Side, int Type, int Slot, byte Kind, int Value)>();
        var dialogs = new Dictionary<ScriptContentStatus, int>();
        var dialogUses = new List<(int Dialog, int Record, string Field)>();
        var wordWrites = new List<(int Dialog, int Record, int Flag, int Value)>();
        // A DLG global-flag operation: the code, a C integer index (leading zeros allowed) and a C integer value.
        var gf = new Regex(@"(?i)\bgf\s*([+-]?\d+)\s*,?\s*([+-]?\d+)");
        foreach (var name in content.Names.Names)
        {
            var script = content.Script(name.Number);
            Assert.AreEqual(ScriptContentStatus.Read, script.Status, script.Detail);
            foreach (var line in script.Program!.Lines)
                foreach (var (clause, side) in new[] { (line.Condition, ScrClauseSide.Condition), (line.OnTrue, ScrClauseSide.OnTrue), (line.OnFalse, ScrClauseSide.OnFalse) })
                {
                    var signature = ScrCatalogue.Signature(side, clause.Type)!;
                    var flagIndex = side == ScrClauseSide.Condition ? clause.Type == ScrCatalogue.Condition.GlobalFlag
                        : clause.Type is ScrCatalogue.Action.SetGlobalFlag or ScrCatalogue.Action.ClearGlobalFlag;
                    for (var slot = 0; slot < signature.Count; slot++)
                    {
                        if (signature[slot] != ScrOperandRole.Number) continue;
                        var (kind, value) = (clause.Kind(slot), clause.Value(slot));
                        if ((kind == GlobalFlag && InWord(value)) || (flagIndex && slot == 0 && (kind != Number || InWord(value))))
                            scripts.Add((name.Number, line.Number, side.ToString(), clause.Type, slot, kind, value));
                    }
                }
            var opened = content.Dialog(name.Number);
            dialogs[opened.Status] = dialogs.GetValueOrDefault(opened.Status) + 1;
            if (opened.Document is not { } document) continue;
            foreach (var entry in document.Records)
                foreach (var (field, kind) in new[] { (entry.Condition, "condition"), (entry.Action, "action") })
                    foreach (Match match in gf.Matches(Encoding.Latin1.GetString(field.Bytes.ToArray())))
                    {
                        var (flag, value) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
                        if (flag == Flag) dialogUses.Add((name.Number, entry.Id, $"{entry.Role} {kind} {value}"));
                        if (kind == "action" && InWord(flag)) wordWrites.Add((name.Number, entry.Id, flag, value));
                    }
        }
        // 2,399 scripts read; 697 dialogs keep records, 4 keep none (ARC-SCRIPT-012), the rest have no DLG.
        Assert.AreEqual(2399, content.Names.Names.Count);
        Assert.AreEqual((697, 4, 1698, 3), (dialogs[ScriptContentStatus.Read], dialogs[ScriptContentStatus.Empty], dialogs[ScriptContentStatus.NotFound], dialogs.Count));

        const byte LocalVariable = (byte)ScrValueKind.LocalVariable;
        CollectionAssert.AreEquivalent(new (int, int, string, int, int, byte, int)[]
        {
            // Reads: equal-to-1 tests, 1341's included.
            (1066, 3, "Condition", 4, 0, GlobalFlag, 1059), (1257, 1, "Condition", 4, 0, GlobalFlag, 1059),
            (1190, 0, "Condition", 4, 0, GlobalFlag, 1056), (1231, 0, "Condition", 4, 0, GlobalFlag, 1056), (1233, 0, "Condition", 4, 0, GlobalFlag, 1056),
            (Script, 0, "Condition", 4, 0, GlobalFlag, Flag), (1410, 3, "Condition", 4, 0, GlobalFlag, 1084),
            // Writes into the word: assign a literal 1 (operand 1), no spill.
            (1070, 1, "OnTrue", 10, 0, GlobalFlag, 1062), (1075, 1, "OnTrue", 10, 0, GlobalFlag, 1070), (2453, 3, "OnTrue", 10, 0, GlobalFlag, 1070),
            (2460, 1, "OnTrue", 10, 0, GlobalFlag, 1068), (2537, 1, "OnTrue", 10, 0, GlobalFlag, 1068),
            // Computed flag indexes (a local variable): 1957 reads 1201..1250, 2231 reads and clears 2495..2750.
            (1957, 9, "Condition", ScrCatalogue.Condition.GlobalFlag, 0, LocalVariable, 3), (2231, 2, "Condition", ScrCatalogue.Condition.GlobalFlag, 0, LocalVariable, 0),
            (2231, 6, "OnTrue", ScrCatalogue.Action.ClearGlobalFlag, 0, LocalVariable, 0),
        }, scripts);
        foreach (var write in scripts.Where(use => use.Type == ScrCatalogue.Action.Assign))
        {
            var clause = content.Script(write.Script).Program!.Lines[write.Line].OnTrue;
            Assert.AreEqual((Number, 1), (clause.Kind(1), clause.Value(1)), write.Script.ToString());
        }

        // Dialogs: four set 1085 in NPC or player actions, two test it, none clears it; every dialog write into the word is 0 or 1.
        CollectionAssert.AreEquivalent(new[]
        {
            (1047, 1100, "Npc action 1"), (1047, 1130, "Npc action 1"), (1047, 1160, "Npc action 1"), (1047, 1195, "Npc action 1"),
            (1047, 1225, "Npc action 1"), (1047, 1235, "Npc action 1"),
            (1074, 376, "Player condition 1"), (1074, 377, "Player condition 0"), (1074, 379, "Npc action 1"),
            (1096, 19, "Npc action 1"),
            (3011, 4, "Player condition 0"), (3011, 4, "Player action 1"),
        }, dialogUses);
        Assert.IsTrue(wordWrites.Count > 0);
        Assert.IsTrue(wordWrites.All(write => write.Value is 0 or 1), string.Join("; ", wordWrites.Where(w => w.Value is not (0 or 1))));
    }

    private static bool InWord(int flag) => flag is >= 1056 and <= 1087;

    private static void AssertClause(ScrClause clause, ScrClauseSide side, int type, params (byte Kind, int Value)[] operands)
    {
        Assert.AreEqual(type, clause.Type);
        var signature = ScrCatalogue.Signature(side, type)!;
        var read = Enumerable.Range(0, signature.Count).Where(slot => signature[slot] != ScrOperandRole.Unused)
            .Select(slot => (clause.Kind(slot), clause.Value(slot))).ToArray();
        CollectionAssert.AreEqual(operands, read, $"type {type}");
    }
}
