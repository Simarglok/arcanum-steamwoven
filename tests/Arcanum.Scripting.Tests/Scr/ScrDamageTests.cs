using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-243 (MIG13 S14g, GT194/GT196): damage (60), apply unresistable damage (138) and heal hit points (75) of set S30 hand one request per object of their operand, in list order, and the run goes on.</summary>
[TestClass]
public sealed class ScrDamageTests
{
    private const byte Triggerer = 0;
    private const int TileScript = 1724;
    private static readonly ScrAdmissionSet Set = new("test-damage", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.HealHitPoints]), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static ScrRunResult Step(ScrProgram program)
    {
        var host = new FakeHost();
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), Npc), host, Options);
    }

    [TestMethod]
    public void Damage_hands_over_the_object_the_amount_and_the_type_and_the_run_goes_on()
    {
        var run = Step(Program(
            Do(C(ScrCatalogue.Action.Damage, (Triggerer, 0), N(12), N(3))),
            Do(C(ScrCatalogue.Action.DamageUnresistable, (Triggerer, 0), N(5), N(4))),
            Do(C(ScrCatalogue.Action.Damage, (1, -1), N(1), N(0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "GT154");
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.Damage, Npc, 12, 3),
            new ScrObjectStateRequest(ScrObjectStateChange.DamageUnresistable, Npc, 5, 4),
        }, run.Effects.ObjectStates.ToArray(), "an absent object asks for nothing; the type is passed as read");
    }

    [TestMethod]
    public void Heal_hit_points_hands_over_the_object_and_the_amount()
    {
        var run = Step(Program(Do(C(ScrCatalogue.Action.HealHitPoints, (Triggerer, 0), N(7))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.HealHitPoints, Npc, 7) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void S30_adds_the_three_over_S29()
    {
        var program = Program(Do(C(ScrCatalogue.Action.HealHitPoints, (Triggerer, 0), N(7))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S30).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S29).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S29.IsSubsetOf(ScrAdmissionSet.S30));
    }
}
