using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-013: the print-line action (type 24) as a dispatcher effect, and the DLG text it shows (ledger IS2,
/// docs/research/item-insert-script.md). Programs, records and phrases are invented synthetic fixtures.</summary>
[TestClass]
public sealed class ScrPrintLineTests
{
    private const int InsertItem = 21, PrintLine = ScrCatalogue.Action.PrintLine;
    private static readonly ScrObjectRef Item = new(1, "item.amulet");
    private static readonly ScrRunOptions S6 = new() { Admission = ScrAdmissionSet.S6 };

    // GF 7 set → return, run default; else print line 1 (number 6), then a local variable line, then return, skip default.
    private static ScrProgram Amulet() => Program(
        L(C(ScrCatalogue.Condition.Equal, (GlobalFlag, 7), N(1)), C(ScrCatalogue.Action.Goto, N(5)), C(0)),
        Do(C(PrintLine, N(1), N(6))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(3))),
        Do(C(PrintLine, (Local, 0), N(2))),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    [TestMethod]
    public void Print_line_is_admitted_only_from_s6_and_returns_its_lines_in_order()
    {
        var host = new FakeHost();
        host.Attach(Item, InsertItem, 1341, Amulet());
        var refused = ScrDispatcher.RunObject(new(InsertItem, Item, Pc, null), host);
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted, PrintLine), (refused.Outcome, refused.Refusal!.Code, refused.Refusal.Type));

        var run = ScrDispatcher.RunObject(new(InsertItem, Item, Pc, null), host, S6);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop));
        CollectionAssert.AreEqual(new[] { new ScrPrintLine(1341, 1, 6), new ScrPrintLine(1341, 3, 2) }, run.Effects.Lines.ToArray());
        Assert.IsFalse(run.Effects.IsEmpty);
        Assert.IsNull(run.Effects.Writeback);

        host.Flags.Add(7);
        var silent = ScrDispatcher.RunObject(new(InsertItem, Item, Pc, null), host, S6);
        Assert.AreEqual(ScrStop.ReturnRunDefault, silent.Stop);
        Assert.IsTrue(silent.Effects.IsEmpty);
    }

    [TestMethod]
    public void Without_an_attachee_nothing_is_printed_and_a_wider_set_is_refused()
    {
        var host = new FakeHost();
        host.Scripts[1341] = ScrScriptLookup.Of(Amulet());
        var tile = ScrDispatcher.RunTile(new(new(1341, 0, 0), Pc), host, S6);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, tile.Stop);
        Assert.AreEqual(0, tile.Effects.Lines.Count);
        // A deferred call has no attachment; its lines name the called script.
        var call = ScrDispatcher.RunCall(new(1341, 0, Pc, Item), host, S6);
        CollectionAssert.AreEqual(new[] { new ScrPrintLine(1341, 1, 6), new ScrPrintLine(1341, 3, 2) }, call.Effects.Lines.ToArray());

        var wider = new ScrAdmissionSet("wider", ScrAdmissionSet.S6.Conditions, ScrAdmissionSet.S6.Actions.Append(ScrCatalogue.Action.Teleport),
            ScrAdmissionSet.S6.ValueKinds, ScrAdmissionSet.S6.FocusKinds);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(InsertItem, Item, Pc, null), host, new() { Admission = wider }));
        Assert.IsTrue(ScrAdmissionSet.S5.IsSubsetOf(ScrAdmissionSet.S6));
        Assert.IsFalse(ScrAdmissionSet.S5.Actions.Contains(PrintLine));
    }

    [TestMethod]
    public void A_printed_line_shows_plain_text_only()
    {
        var names = new DialogSpeakerNames("Ada", "Bram");
        var document = Read(
            Record("1", "It is warm, @PCNAME@.", "It is cool.")
            + Record("2", "Acted.", action: "gf 5 1")
            + Record("3", "g: greeting") + Record("4", "M:x") + Record("5", "r:") + Record("6", "i:") + Record("7", "i: is fine")
            + Record("8", "@who@") + Record("9", "Kept", condition: "gf 1 1"));
        ScrPrintedLineText Show(int line, bool alternate = false) => ScrPrintedLine.Read(document, line, alternate, names);
        Assert.AreEqual((ScrPrintedLineStatus.Shown, "It is warm, Ada."), (Show(1).Status, Show(1).Text));
        Assert.AreEqual("It is cool.", Show(1, alternate: true).Text);
        // A missing line prints one space; the condition field is not an action.
        Assert.AreEqual((ScrPrintedLineStatus.Shown, " "), (Show(40).Status, Show(40).Text));
        Assert.AreEqual((ScrPrintedLineStatus.Shown, "Kept"), (Show(9).Status, Show(9).Text));
        Assert.AreEqual((ScrPrintedLineStatus.Shown, "i: is fine"), (Show(7).Status, Show(7).Text));
        // The generated-text marker is the record text's, whichever field is shown.
        var marked = Read(Record("1", "g: greeting", "plain") + Record("2", "plain", "g:x"));
        Assert.AreEqual(ScrPrintedLineStatus.Unsupported, ScrPrintedLine.Read(marked, 1, true, names).Status);
        Assert.AreEqual((ScrPrintedLineStatus.Shown, "g:x"), (ScrPrintedLine.Read(marked, 2, true, names).Status, ScrPrintedLine.Read(marked, 2, true, names).Text));
        foreach (var line in new[] { 2, 3, 4, 5, 6, 8 })
            Assert.AreEqual(ScrPrintedLineStatus.Unsupported, Show(line).Status, line.ToString());
    }
}
