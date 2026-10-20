using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-TECHNOLOGY-005: start schematic UI (129) as a dispatcher effect (ledger SW8; TC2, both fixed: one request for the
/// object operand, an absent object names none, the run goes on, no callback is called). Programs are invented.</summary>
[TestClass]
public sealed class ScrSchematicTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    // ScrAdmissionSet.S9 without the guard conditions of S4, which need a host with object queries (the fake host has none).
    private static readonly ScrRunOptions S7Schematic = new()
    {
        Admission = new("S7-schematic", ScrAdmissionSet.S7.Conditions, ScrAdmissionSet.S7.Actions.Append(ScrCatalogue.Action.StartSchematicUi),
            ScrAdmissionSet.S7.ValueKinds, ScrAdmissionSet.S7.FocusKinds),
    };

    [TestMethod]
    public void Start_schematic_ui_is_admitted_with_s9_and_returned_for_its_object()
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, Program(
            Do(C(ScrCatalogue.Action.StartSchematicUi, (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.StartSchematicUi, (Extra, 0))),
            Do(C(ScrCatalogue.Action.StartSchematicUi, (Attachee, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var refused = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, new() { Admission = ScrAdmissionSet.S7 });
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.StartSchematicUi),
            (refused.Outcome, refused.Refusal!.Code, refused.Refusal.Type));
        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S7Schematic);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the run goes on after each request");
        CollectionAssert.AreEqual(new[] { new ScrSchematicRequest(Pc), new ScrSchematicRequest(Npc) }, run.Effects.Schematics.ToArray(),
            "an absent object (no extra object) names none");
        Assert.IsFalse(run.Effects.IsEmpty);
        Assert.IsTrue(ScrAdmissionSet.S8.IsSubsetOf(ScrAdmissionSet.S9));
        Assert.IsFalse(ScrAdmissionSet.S8.Actions.Contains(ScrCatalogue.Action.StartSchematicUi));
        Assert.IsTrue(ScrAdmissionSet.S9.Actions.Contains(ScrCatalogue.Action.StartSchematicUi));
        Assert.IsTrue(S7Schematic.Admission.IsSubsetOf(ScrAdmissionSet.S9));
    }
}
