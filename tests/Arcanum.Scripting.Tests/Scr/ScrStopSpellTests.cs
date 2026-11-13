using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1294 (ledger RM6, 0x4593F0): stop spell on object (130) of set S42 hands the spell of operand 0 and the first object of operand 1 to the host and the run goes on.</summary>
[TestClass]
public sealed class ScrStopSpellTests
{
    private const byte Triggerer = 0;
    private const int TileScript = 1725;
    // The narrow set of the other action tests (a set with a guard condition needs a host with object queries); S42 itself is checked below.
    private static readonly ScrAdmissionSet Set = new("test-stop-spell", ScrAdmissionSet.S2.Conditions, ScrAdmissionSet.S2.Actions.Append(ScrCatalogue.Action.StopSpellOnObject), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static ScrRunResult Step(ScrProgram program)
    {
        var host = new FakeHost();
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), Npc), host, Options);
    }

    [TestMethod]
    public void The_action_hands_over_the_object_and_the_spell_and_the_run_goes_on()
    {
        var run = Step(Program(
            Do(C(ScrCatalogue.Action.StopSpellOnObject, N(7), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.StopSpellOnObject, N(9), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "the answer of 0x4593F0 is not read: the run goes on to the next line");
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.StopSpell, Npc, 7),
            new ScrObjectStateRequest(ScrObjectStateChange.StopSpell, Npc, 9),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void A_spell_read_from_a_local_variable_and_a_missing_object_hands_nothing()
    {
        var run = Step(Program(
            Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(12))),
            Do(C(ScrCatalogue.Action.StopSpellOnObject, (Local, 0), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.StopSpellOnObject, N(3), (1, -1))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.StopSpell, Npc, 12) }, run.Effects.ObjectStates.ToArray(), "the absent object (the original's handle test) stops nothing");
    }

    [TestMethod]
    public void S42_adds_only_the_spell_stop_over_S40()
    {
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.StopSpellOnObject }, ScrAdmissionSet.S42.Actions.Except(ScrAdmissionSet.S40.Actions).ToArray());
        Assert.IsTrue(ScrAdmissionSet.S42.Conditions.SetEquals(ScrAdmissionSet.S40.Conditions) && ScrAdmissionSet.S40.IsSubsetOf(ScrAdmissionSet.S42));
        var program = Program(Do(C(ScrCatalogue.Action.StopSpellOnObject, N(7), (Triggerer, 0))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S42).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S40).Refusal!.Code);
    }

    [TestMethod]
    public void The_interpreter_runs_S42_and_nothing_above_it()
    {
        Assert.IsNotNull(new ScrRunOptions { Admission = ScrAdmissionSet.S42 });
        // A set with no guard condition (the host check does not fire) plus an action that no set holds: refused as above S42, by name.
        var above = new ScrAdmissionSet("above", ScrAdmissionSet.S2.Conditions, ScrAdmissionSet.S2.Actions.Append(UnsupportedAction), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
        var program = Program(Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var host = new FakeHost();
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), Npc), host, new() { Admission = above }));
        StringAssert.Contains(thrown.Message, "S42");
    }
}
