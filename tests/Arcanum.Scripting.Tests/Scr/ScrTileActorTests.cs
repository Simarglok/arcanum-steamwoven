using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The tile-script actor commands of set S20 (ARC-SCRIPT-052, ledger TD1–TD3): destroy (68) and stop attacking (109), on synthetic
/// tile programs of the installed story tiles' shapes (1724: the triggerer destroyed; 1779: the triggerer stops attacking), and call-ex
/// (81) of the stepping NPC's dialog point (1328) from a tile (audit slice 8).</summary>
[TestClass]
public sealed class ScrTileActorTests
{
    private const byte Triggerer = 0, Attachee = 1, Player = 21;
    private const int TileScript = 1724, DialogScript = 1324;

    // The viewer's tile set has this shape (without its guards, which need an object host): call-ex, the dialog action and the two actor commands.
    private static readonly ScrAdmissionSet Set = new("test-tile-actor", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.Dialog, ScrCatalogue.Action.CallScriptEx, ScrCatalogue.Action.Destroy, ScrCatalogue.Action.StopAttacking]),
        ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds.Append((ScrFocusKind)Player));
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static ScrRunResult Step(FakeHost host, ScrProgram program, ScrObjectRef triggerer)
    {
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), triggerer), host, Options);
    }

    [TestMethod]
    public void Destroy_and_stop_attacking_ask_for_the_first_object_and_the_run_goes_on()
    {
        var program = Program(
            Do(C(ScrCatalogue.Action.StopAttacking, (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.Destroy, (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.SetLocalFlag, N(3))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var run = Step(new FakeHost(), program, Npc);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.StopAttacking, Npc), new ScrObjectStateRequest(ScrObjectStateChange.Destroy, Npc) },
            run.Effects.ObjectStates.ToArray());
        Assert.AreEqual((ScrOutcome.SkipDefault, 1u << 3), (run.Outcome, run.Effects.Writeback!.Slot.LocalFlags), "TD1, TD3: the run goes on after both");
    }

    [TestMethod]
    public void An_absent_object_asks_for_nothing()
    {
        // A tile has no attachee: operand "attachee" names no object.
        var program = Program(Do(C(ScrCatalogue.Action.Destroy, (Attachee, -1))), Do(C(ScrCatalogue.Action.StopAttacking, (Attachee, -1))));
        var run = Step(new FakeHost(), program, Npc);
        Assert.AreEqual(0, run.Effects.ObjectStates.Count);
    }

    [TestMethod]
    public void A_tile_call_ex_runs_the_stepping_npcs_dialog_point_and_asks_for_his_talk_with_the_pc()
    {
        // 1328's shape: the NPC who stepped on the tile runs his dialog point from line 1, the PC as the triggerer; that line is a dialog.
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Dialog, DialogScript, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault)), Do(C(ScrCatalogue.Action.Dialog, N(250)))));
        var program = Program(
            Do(C(ScrCatalogue.Action.CallScriptEx, (Triggerer, 0), N(ScrPoint.Dialog), N(1), (Player, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Step(host, program, Npc);
        Assert.AreEqual((DialogScript, 250, Npc, Pc), (run.Effects.Dialog!.Script, run.Effects.Dialog.Node, run.Effects.Dialog.Npc, run.Effects.Dialog.Pc));
    }

    [TestMethod]
    public void S20_admits_the_two_commands_over_S19()
    {
        var program = Program(Do(C(ScrCatalogue.Action.Destroy, (Triggerer, 0))), Do(C(ScrCatalogue.Action.StopAttacking, (Triggerer, 0))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S20).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S19).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S19.IsSubsetOf(ScrAdmissionSet.S20) && Set.IsSubsetOf(ScrAdmissionSet.S20));
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.Destroy, ScrCatalogue.Action.StopAttacking },
            ScrAdmissionSet.S20.Actions.Except(ScrAdmissionSet.S19.Actions).ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S19.Conditions.ToArray(), ScrAdmissionSet.S20.Conditions.ToArray());
        Assert.IsTrue(ScrAdmissionSet.S20.CounterIndexWraps);
    }
}
