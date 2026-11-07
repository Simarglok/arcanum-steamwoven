using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-231 (MIG13 S14a, GT154–GT161): walk to (69) and run to (74) of set S25 hand the first object and the tile to the host and the run goes on.</summary>
[TestClass]
public sealed class ScrWalkRunTests
{
    private const byte Triggerer = 0, Attachee = 1;
    private const int TileScript = 1724;
    private static readonly ScrAdmissionSet Set = new("test-walk-run", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo]), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static ScrRunResult Step(ScrProgram program)
    {
        var host = new FakeHost();
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), Npc), host, Options);
    }

    [TestMethod]
    public void Walk_and_run_ask_for_the_first_object_and_the_tile_in_action_order_and_the_run_goes_on()
    {
        var run = Step(Program(
            Do(C(ScrCatalogue.Action.WalkTo, (Triggerer, 0), N(92965), N(82698))),
            Do(C(ScrCatalogue.Action.Assign, (Counter, 0), N(7))),
            Do(C(ScrCatalogue.Action.RunTo, (Triggerer, 0), (Counter, 0), N(3))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "GT154: the action goes on to the next line");
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.WalkTo, Npc, 92965, 82698),
            new ScrObjectStateRequest(ScrObjectStateChange.RunTo, Npc, 7, 3),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void An_absent_object_asks_for_nothing()
    {
        var run = Step(Program(Do(C(ScrCatalogue.Action.WalkTo, (Attachee, -1), N(1), N(2))), Do(C(ScrCatalogue.Action.RunTo, (Attachee, -1), N(1), N(2)))));
        Assert.AreEqual(0, run.Effects.ObjectStates.Count);
    }

    [TestMethod]
    public void S25_adds_the_two_actions_over_S24()
    {
        var program = Program(Do(C(ScrCatalogue.Action.WalkTo, (Triggerer, 0), N(1), N(2))), Do(C(ScrCatalogue.Action.RunTo, (Triggerer, 0), N(1), N(2))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S25).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S24).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S24.IsSubsetOf(ScrAdmissionSet.S25) && Set.IsSubsetOf(ScrAdmissionSet.S25));
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo }, ScrAdmissionSet.S25.Actions.Except(ScrAdmissionSet.S24.Actions).ToArray());
    }
}
