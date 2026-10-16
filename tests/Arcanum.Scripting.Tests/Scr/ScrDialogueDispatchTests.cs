using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Region admission, the S3 dialogue set and the dialog action (ARC-SCRIPT-009).</summary>
[TestClass]
public sealed class ScrDialogueDispatchTests
{
    private const int Dialog = ScrPoint.Dialog, Attachee = 1, Triggerer = 0, Player = 21, Extra = 12;

    private static FakeHost With(ScrProgram program, uint localFlags = 0)
    {
        var host = new FakeHost();
        host.Attach(Npc, Dialog, 1324, program, localFlags);
        return host;
    }

    private static ScrRunResult Talk(FakeHost host, int line = 0) => ScrDispatcher.RunObject(new(Dialog, Npc, Pc, null, line), host);

    // Lines 0..5 as Virgil's script starts (FOLLOWING, local flags, JILTED, dialog, return); line 6 onwards holds
    // commands outside the set, reached only through a different entry.
    private static ScrProgram VirgilLike() => Program(
        L(C(18, (Attachee, 0), (Triggerer, 0)), C(3, N(6)), C(0)),
        L(C(3, N(31)), C(3, N(8)), C(0)),
        L(C(39, (Attachee, 0)), C(3, N(10)), C(0)),
        L(C(3, N(1)), C(3, N(12)), C(0)),
        Do(C(4, N(1))),
        Do(C(1)),
        Do(C(4, N(385))),
        Do(C(1)),
        Do(C(4, N(527))),
        Do(C(1)),
        Do(C(4, N(538))),
        Do(C(1)),
        Do(C(4, N(235))),
        Do(C(1)),
        Do(C(23, N(286), (Attachee, 0))),
        Do(C(1)));

    [TestMethod]
    public void Region_admission_reads_only_the_lines_a_run_can_reach()
    {
        var program = VirgilLike();
        Assert.IsFalse(ScrAdmission.Admit(program, ScrAdmissionSet.S3).IsAdmitted, "the whole script has a float line");
        var region = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S3, 0);
        Assert.IsTrue(region.IsAdmitted, region.Refusal?.ToString());
        // The dialog actions end every path, so the return lines after them are reached only by a resumption.
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 6, 8, 10, 12 }, region.Admitted!.Region.ToArray());
        Assert.AreEqual(0, region.Admitted.EntryLine);
        // A resumption at line 5 reaches only line 5; one at line 14 reaches the float line.
        CollectionAssert.AreEqual(new[] { 5 }, ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S3, 5).Admitted!.Region.ToArray());
        var refused = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S3, 14);
        Assert.AreEqual((14, ScrRefusalCode.ActionNotAdmitted, 23), (refused.Refusal!.Line, refused.Refusal.Code, refused.Refusal.Type));
        // Outside the script the region is empty; the whole-script admission keeps every line.
        Assert.AreEqual(0, ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S3, 99).Admitted!.Region.Count);
        Assert.IsNull(ScrAdmission.Admit(Program(Do(C(1))), ScrAdmissionSet.S3).Admitted!.EntryLine);
    }

    [TestMethod]
    public void Region_admission_follows_gotos_fall_through_and_both_branches_and_reports_the_first_line()
    {
        var program = Program(
            L(C(3, N(0)), C(3, N(3)), C(0)),        // 0: both branches: 3 and 1
            Do(C(44, (21, 0), N(1), N(1), N(1))),   // 1: refused
            Do(C(23, N(1), (Attachee, 0))),          // 2: unreachable float line
            Do(C(3, N(5))),                          // 3: goto 5, line 4 skipped
            Do(C(18, (1, 0))),                       // 4: unreachable loop
            Do(C(0)));                               // 5: falls off the end
        var r = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S3, 0);
        Assert.AreEqual((1, 44), (r.Refusal!.Line, r.Refusal.Type));
        var fromThree = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S3, 3);
        CollectionAssert.AreEqual(new[] { 3, 5 }, fromThree.Admitted!.Region.ToArray());
        // The dead false action of an always-true line is neither checked nor followed.
        var dead = Program(L(C(0), C(1), C(3, N(1))), Do(C(23, N(1), (Attachee, 0))));
        CollectionAssert.AreEqual(new[] { 0 }, ScrAdmission.AdmitFrom(dead, ScrAdmissionSet.S3, 0).Admitted!.Region.ToArray());
        Assert.IsTrue(ScrAdmission.AdmitFrom(Program(L(C(0), C(1), C(44, (21, 0), N(1), N(1), N(1)))), ScrAdmissionSet.S3, 0).IsAdmitted);
        // Two refusals in the region: the lower line is reported, whatever order the walk met them in; a refused
        // line's successors are not explored (line 4 behind refused line 3 is never reached).
        var two = Program(
            L(C(3, N(0)), C(3, N(2)), C(3, N(3))),
            Do(C(1)),
            Do(C(23, N(1), (Attachee, 0))),
            Do(C(44, (21, 0), N(1), N(1), N(1))),
            Do(C(18, (1, 0))));
        var first = ScrAdmission.AdmitFrom(two, ScrAdmissionSet.S3, 0).Refusal!;
        Assert.AreEqual((2, 23), (first.Line, first.Type));
        Assert.AreEqual((3, 44), (ScrAdmission.AdmitFrom(two, ScrAdmissionSet.S3, 3).Refusal!.Line, ScrAdmission.AdmitFrom(two, ScrAdmissionSet.S3, 3).Refusal!.Type));
    }

    [TestMethod]
    public void Virgil_like_entry_starts_the_first_conversation_and_writes_the_slot_first()
    {
        var host = With(VirgilLike());
        var first = Talk(host);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.Dialog, 4), (first.Outcome, first.Stop, first.StopLine));
        Assert.AreEqual(new ScrDialogStart(Pc, Npc, 1324, 4, 1), first.Effects.Dialog);
        Assert.IsNull(first.Effects.Writeback);
        // Following the PC: node 385; local flag 31: node 527; jilted: node 538; local flag 1: node 235.
        host.Leaders[Npc] = Pc;
        Assert.AreEqual(385, Talk(host).Effects.Dialog!.Node);
        host.Leaders.Clear();
        host.Slots[(Npc, Dialog)] = new(1324, 1u << 31, 0);
        Assert.AreEqual(527, Talk(host).Effects.Dialog!.Node);
        host.Slots[(Npc, Dialog)] = new(1324, 0, 0); host.Jilted.Add(Npc);
        Assert.AreEqual(538, Talk(host).Effects.Dialog!.Node);
        host.Jilted.Clear(); host.Slots[(Npc, Dialog)] = new(1324, 2, 0);
        Assert.AreEqual(235, Talk(host).Effects.Dialog!.Node);
        // A follower of someone else is not following the PC.
        host.Slots[(Npc, Dialog)] = new(1324, 0, 0); host.Leaders[Npc] = Chest;
        Assert.AreEqual(1, Talk(host).Effects.Dialog!.Node);
        // Resumption after the DLG ends with 0: the line after the dialog action.
        var resumed = Talk(host, line: 5);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (resumed.Outcome, resumed.Stop));
        Assert.IsTrue(resumed.Effects.IsEmpty);
        Assert.AreEqual(ScrOutcome.Refused, Talk(host, line: 14).Outcome);
    }

    [TestMethod]
    public void The_dialog_action_starts_a_conversation_only_for_a_pc_and_auto_removes()
    {
        var host = With(Program(ScrCatalogue.FileFlag.AutoRemoving, Do(C(8, N(3))), Do(C(4, N(7)))), localFlags: 0);
        var r = Talk(host);
        Assert.AreEqual(ScrStop.Dialog, r.Stop);
        Assert.AreEqual(new ScrSlot(0, 8, 0), r.Effects.Writeback!.Slot);
        Assert.AreEqual(new ScrDialogStart(Pc, Npc, 1324, 1, 7), r.Effects.Dialog);
        host.Pcs.Clear();
        var npcTalker = Talk(host);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.Dialog), (npcTalker.Outcome, npcTalker.Stop));
        Assert.IsNull(npcTalker.Effects.Dialog);
        Assert.IsNull(ScrDispatcher.RunObject(new(Dialog, Npc, null, null), host).Effects.Dialog);
    }

    [TestMethod]
    public void A_mind_controlled_npc_starts_the_dlg_of_2188()
    {
        var host = With(Program(Do(C(4, N(1)))));
        host.Scripts[ScrDispatcher.MindControlScript] = ScrScriptLookup.Of(Program(Do(C(4, N(9)))));
        host.Dialog = ScrDialogState.MindControlled;
        Assert.AreEqual(new ScrDialogStart(Pc, Npc, ScrDispatcher.MindControlScript, 0, 9), Talk(host).Effects.Dialog);
    }

    [TestMethod]
    public void Object_operands_resolve_the_roles_and_a_missing_role_stops_the_run()
    {
        var npc2 = new ScrObjectRef(1, "npc.2");
        var program = Program(L(C(18, (Extra, 0), (Player, 0)), C(2), C(1)));
        var host = With(program); host.Leaders[npc2] = Pc;
        Assert.AreEqual(ScrOutcome.RunDefault, ScrDispatcher.RunObject(new(Dialog, Npc, Pc, npc2), host).Outcome);
        var noExtra = ScrDispatcher.RunObject(new(Dialog, Npc, Pc, null), host);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.MissingObject, 0), (noExtra.Outcome, noExtra.Stop, noExtra.StopLine));
        host.PlayerRef = null;
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunObject(new(Dialog, Npc, Pc, npc2), host).Stop);
        // A tile or sector script has no attachee.
        var tileHost = new FakeHost();
        tileHost.Scripts[50] = ScrScriptLookup.Of(Program(L(C(39, (Attachee, 0)), C(2), C(1))));
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunTile(new(new(50, 0, 0), Pc), tileHost).Stop);
        tileHost.Scripts[51] = ScrScriptLookup.Of(Program(L(C(39, (Triggerer, 0)), C(2), C(1))));
        Assert.AreEqual(ScrOutcome.SkipDefault, ScrDispatcher.RunTile(new(new(51, 0, 0), Pc), tileHost).Outcome, "a PC is not a jilted NPC");
    }

    [TestMethod]
    public void The_S3_set_and_the_interpreter_capability()
    {
        Assert.IsTrue(ScrAdmissionSet.S2.IsSubsetOf(ScrAdmissionSet.S3));
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S2.Conditions.Append(18).Append(39).ToArray(), ScrAdmissionSet.S3.Conditions.ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S2.Actions.Append(4).ToArray(), ScrAdmissionSet.S3.Actions.ToArray());
        CollectionAssert.AreEquivalent(new[] { ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.Player }, ScrAdmissionSet.S3.FocusKinds.ToArray());
        Assert.AreSame(ScrAdmissionSet.S3, new ScrRunOptions().Admission);
        // A group focus is outside the set.
        var group = ScrDispatcher.RunObject(new(Dialog, Npc, Pc, null), With(Program(L(C(39, (8, 0)), C(2), C(1)))));
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.FocusKindNotAdmitted), (group.Outcome, group.Refusal!.Code));
    }
}
