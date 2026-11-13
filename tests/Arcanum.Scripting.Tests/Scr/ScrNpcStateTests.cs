using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS1–NS8): the NPC state actions of set S36 on synthetic programs: critter disband (22), set and adjust reaction (30, 31), the day and night standpoints (45, 46, 140, 141),
/// change art number (59) and change script (99). The meaning of each request (the session's party, reaction and object fields) is the host's; these tests pin what the interpreter asks and reads back.</summary>
[TestClass]
public sealed class ScrNpcStateTests
{
    private const byte TriggererKind = 0, AttacheeKind = 1, PlayerKind = 21;
    private const int Script = 2820;
    // A set the FakeHost can run (the full S36 needs every query interface): the control flow of S1, the following test and the nine actions.
    private static readonly ScrAdmissionSet Narrow = new("test-npc-state", ScrAdmissionSet.S1.Conditions.Append(ScrCatalogue.Condition.FollowingPc),
        ScrAdmissionSet.S1.Actions.Concat(ScrAdmissionSet.NpcStateActions).Append(ScrCatalogue.Action.CallScriptEx).Append(ScrCatalogue.Action.SetLocalFlag), ScrAdmissionSet.S1.ValueKinds, [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.Player], counterIndexWraps: true);
    private static readonly ScrRunOptions Options = new() { Admission = Narrow };

    private static ScrRunResult Run(ScrProgram program, FakeHost? host = null, int point = ScrPoint.Heartbeat)
    {
        host ??= new FakeHost();
        host.Attach(Npc, point, Script, program);
        return ScrDispatcher.RunObject(new(point, Npc, Pc, null), host, Options);
    }

    [TestMethod]
    public void The_set_adds_exactly_the_nine_actions_to_the_fade_set()
    {
        CollectionAssert.AreEquivalent(new[] { 22, 30, 31, 45, 46, 59, 99, 140, 141 }, ScrAdmissionSet.S36.Actions.Except(ScrAdmissionSet.S35.Actions).ToArray());
        Assert.IsTrue(ScrAdmissionSet.S35.IsSubsetOf(ScrAdmissionSet.S36));
        Assert.IsTrue(ScrAdmissionSet.NpcStateHostActions.Contains(ScrCatalogue.Action.ChangeArtNumber), "ARC-SCRIPT-1735: the area hosts apply it");
    }

    [TestMethod]
    public void Disband_hands_the_first_object_to_the_host_and_the_run_reads_the_leader_gone()
    {
        // NS1: the original lets the object go at once, so the run's own following test afterwards reads false.
        var host = new FakeHost();
        host.Leaders[Npc] = Pc;
        var run = Run(Program(
            L(C(ScrCatalogue.Condition.FollowingPc, (AttacheeKind, 0), (TriggererKind, 0)), C(ScrCatalogue.Action.CritterDisband, (AttacheeKind, 0)), C(ScrCatalogue.Action.ReturnRunDefault)),
            L(C(ScrCatalogue.Condition.FollowingPc, (AttacheeKind, 0), (TriggererKind, 0)), C(ScrCatalogue.Action.ReturnRunDefault), C(ScrCatalogue.Action.ReturnSkipDefault))), host);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.Disband, Npc) }, run.Effects.ObjectStates.ToArray());
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "not following after the disband");
    }

    [TestMethod]
    public void Set_and_adjust_reaction_name_the_npc_the_pc_and_the_number()
    {
        // NS2: operand 0 the NPC, operand 1 the PC, operand 2 the number (a literal or a variable).
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.SetReaction, (AttacheeKind, 0), (PlayerKind, 0), N(60))),
            Do(C(ScrCatalogue.Action.AdjustReaction, (AttacheeKind, 0), (PlayerKind, 0), N(-15))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.SetReaction, Npc, 60, Other: Pc),
            new ScrObjectStateRequest(ScrObjectStateChange.AdjustReaction, Npc, -15, Other: Pc),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void The_standpoints_carry_the_location_with_the_sign_extension_the_original_ors_into_the_high_word()
    {
        // NS3 (0x447220 cases 45, 46): low word x, high word y | (x >> 31): a negative x gives −1 in the high word.
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.SetDayStandpoint, (AttacheeKind, 0), N(480), N(521))),
            Do(C(ScrCatalogue.Action.SetNightStandpoint, (AttacheeKind, 0), N(-3), N(7))),
            Do(C(ScrCatalogue.Action.SetDayStandpointEx, (AttacheeKind, 0), N(10), N(20), N(5012))),
            Do(C(ScrCatalogue.Action.SetNightStandpointEx, (AttacheeKind, 0), N(11), N(21), N(5013))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.SetDayStandpoint, Npc, 480, 521),
            new ScrObjectStateRequest(ScrObjectStateChange.SetNightStandpoint, Npc, -3, -1),
            new ScrObjectStateRequest(ScrObjectStateChange.SetDayStandpoint, Npc, 10, 20, Map: 5012),
            new ScrObjectStateRequest(ScrObjectStateChange.SetNightStandpoint, Npc, 11, 21, Map: 5013),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void Change_art_number_asks_one_request_per_object_of_the_list()
    {
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.ChangeArtNumber, (AttacheeKind, 0), N(12))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.ChangeArtNumber, Npc, 12) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void Change_script_of_the_runs_own_slot_is_the_runs_own_change()
    {
        // NS4: the attachee and the run's own point: the number takes effect when the run ends, as change this script does; no slot write.
        var host = new FakeHost();
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.ChangeScript, (AttacheeKind, 0), N(ScrPoint.Heartbeat), N(2999))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))), host);
        Assert.AreEqual(2999, run.Effects.Writeback!.Slot.Script);
        Assert.AreEqual(0, run.Effects.SlotWrites.Count);
    }

    [TestMethod]
    public void Change_script_of_another_slot_keeps_its_flags_and_counters_and_writes_it_back()
    {
        // NS4: another object, or another point: the slot keeps its local flags and counters and takes the script; a later call-ex reads the new number.
        var host = new FakeHost();
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.ChangeScript, (TriggererKind, 0), N(ScrPoint.Dialog), N(1))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))), host);
        // The triggerer (the PC) has no dialog slot: a new one is made with flags and counters 0.
        CollectionAssert.AreEqual(new[] { new ScrWriteback(ScrSource.Object, Pc, ScrPoint.Dialog, new ScrSlot(1, 0, 0)) }, run.Effects.SlotWrites.ToArray());
        Assert.IsNull(run.Effects.Writeback, "the run's own slot is untouched");
    }

    [TestMethod]
    public void Change_script_of_a_held_slot_keeps_its_local_flags_and_counters()
    {
        var host = new FakeHost();
        host.Slots[(Npc, ScrPoint.Dialog)] = new ScrSlot(1100, 0x5, 0x30);
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.ChangeScript, (AttacheeKind, 0), N(ScrPoint.Dialog), N(2000))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))), host);
        CollectionAssert.AreEqual(new[] { new ScrWriteback(ScrSource.Object, Npc, ScrPoint.Dialog, new ScrSlot(2000, 0x5, 0x30)) }, run.Effects.SlotWrites.ToArray());
    }

    [TestMethod]
    public void Change_script_of_a_call_ex_callees_own_slot_is_kept_with_the_callees_flags()
    {
        // NS4 (review): the exe tests the event's object and point at any depth, so the callee's change of its own slot is its pending result, which call-ex writes back with the flags the callee set.
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Dialog, 2821, Program(
            Do(C(ScrCatalogue.Action.SetLocalFlag, N(2))),
            Do(C(ScrCatalogue.Action.ChangeScript, (AttacheeKind, 0), N(ScrPoint.Dialog), N(1200))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.CallScriptEx, (AttacheeKind, 0), N(ScrPoint.Dialog), N(0), (TriggererKind, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))), host);
        CollectionAssert.AreEqual(new[] { new ScrWriteback(ScrSource.Object, Npc, ScrPoint.Dialog, new ScrSlot(1200, 1u << 2, 0)) }, run.Effects.SlotWrites.ToArray());
    }

    [TestMethod]
    public void Change_script_names_a_point_outside_the_array_and_the_dispatch_ends_applying_nothing()
    {
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.ChangeScript, (AttacheeKind, 0), N(36), N(5))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrStop.CallExPoint, run.Stop);
        Assert.IsTrue(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void The_actions_are_refused_outside_the_set()
    {
        var program = Program(Do(C(ScrCatalogue.Action.CritterDisband, (AttacheeKind, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        var run = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, new() { Admission = new("test-without", Narrow.Conditions, Narrow.Actions.Except(ScrAdmissionSet.NpcStateActions), Narrow.ValueKinds, Narrow.FocusKinds, counterIndexWraps: true) });
        Assert.AreEqual(ScrOutcome.Refused, run.Outcome);
    }
}
